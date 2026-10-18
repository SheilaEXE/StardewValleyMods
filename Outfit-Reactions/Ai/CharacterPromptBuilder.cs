using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using OutfitReactions;

namespace OutfitReactions.Ai
{
    internal static class CharacterPromptBuilder
    {
        private const int DialogueModeMaxChars = 1500;
        public static string BuildForOutfitCompliment(CharacterAiProfile profile, OutfitAiContext context, bool includePlayerReplyMode = false, PromptStyleService promptStyle = null, PromptSizeBreakdown diagnostics = null)
        {
            if (profile == null)
                return "";

            return BuildNarrativeV2Profile(profile, context, includePlayerReplyMode, promptStyle, diagnostics);
        }

        private static string BuildNarrativeV2Profile(CharacterAiProfile profile, OutfitAiContext context, bool includePlayerReplyMode, PromptStyleService promptStyle, PromptSizeBreakdown diagnostics)
        {
            StringBuilder builder = new();
            int checkpoint = builder.Length;
            string npcName = !string.IsNullOrWhiteSpace(profile.NpcName) ? profile.NpcName : context?.NpcDisplayName ?? "NPC";

            builder.AppendLine("FOCUSED CHARACTER PROFILE FOR THIS OUTFIT VISUAL-REACTION SCENE");
            builder.AppendLine("You are " + npcName + " from Stardew Valley. Use this as a focused character sheet, not as dialogue to copy.");
            AppendSection(builder, "Profile mode", StringUtils.FirstNonEmpty(profile.ProfileName, profile.ProfileId, profile.ProfileType));

            // Track what we already emitted so the free-form pass below never duplicates it.
            HashSet<string> emittedNarrative = new(StringComparer.OrdinalIgnoreCase);

            // 1. Core identity sections first, in a stable preferred order (when present).
            bool allowIntimate = AllowIntimateContent(context, includePlayerReplyMode);
            foreach (string key in CoreNarrativeOrder)
            {
                if (!CanEmitNarrativeKey(profile, key, allowIntimate, allowGuardrails: true))
                    continue;

                if (AppendNarrativeSectionByKey(builder, profile, key))
                    emittedNarrative.Add(key);
            }
            diagnostics?.Add("profile.identity-and-core-narrative", builder.Length - checkpoint);
            checkpoint = builder.Length;

            AppendRelationshipSection(builder, profile, context);
            diagnostics?.Add("profile.relationship", builder.Length - checkpoint);
            checkpoint = builder.Length;
            if (!string.IsNullOrWhiteSpace(context?.PlayerSelfDescription))
            {
                builder.AppendLine("Farmer self-description supplied by the player (descriptive context, not dialogue):");
                builder.AppendLine(context.PlayerSelfDescription.Trim());
            }
            diagnostics?.Add("profile.player-self-description", builder.Length - checkpoint);
            checkpoint = builder.Length;
            AppendDialogueModeSection(builder, profile, context, includePlayerReplyMode, promptStyle);
            diagnostics?.Add("profile.dialogue-mode", builder.Length - checkpoint);
            checkpoint = builder.Length;
            AppendRelevantTraits(builder, profile, context, includePlayerReplyMode);
            diagnostics?.Add("profile.selected-traits", builder.Length - checkpoint);
            checkpoint = builder.Length;
            AppendSpeechHesitationRestraint(builder, profile, context, includePlayerReplyMode);
            diagnostics?.Add("profile.speech-restraint", builder.Length - checkpoint);
            checkpoint = builder.Length;

            // 2. Every remaining narrative key, free-form per NPC, in the ficha's own order.
            //    Guardrail-style keys (HardLimits, WhatMustNeverBeLost, ...) are pushed to the
            //    very end so they read as the final, binding constraints on the model.
            //    Intimate/spicy keys are skipped entirely unless the relationship is romantic
            //    and the moment is private, so friendship never sees that side of the NPC.
            if (profile.NarrativeProfile != null)
            {
                List<string> descriptive = new();
                List<string> guardrails = new();

                foreach (string key in profile.NarrativeProfile.Keys)
                {
                    if (string.IsNullOrWhiteSpace(key) || emittedNarrative.Contains(key))
                        continue;

                    bool guardrail = IsGuardrailKey(key);

                    // Speech hesitation/stammer descriptions are intentionally not emitted as
                    // regular personality anchors, because models tend to copy the examples too
                    // literally and overuse them in every short compliment. A concise restraint
                    // rule is appended separately instead.
                    if (IsSpeechHesitationKey(key))
                        continue;

                    // Guardrails (HardLimits / WhatMustNeverBeLost) are protection rules and
                    // must ALWAYS reach the model, even though they may mention intimate words.
                    if (!CanEmitNarrativeKey(profile, key, allowIntimate, allowGuardrails: true))
                        continue;

                    if (guardrail)
                        guardrails.Add(key);
                    else
                        descriptive.Add(key);
                }

                foreach (string key in descriptive)
                    AppendNarrativeSectionByKey(builder, profile, key);
                foreach (string key in guardrails)
                    AppendNarrativeSectionByKey(builder, profile, key);
            }
            diagnostics?.Add("profile.remaining-narrative-and-guardrails", builder.Length - checkpoint);
            checkpoint = builder.Length;

            AppendNaturalReactionStyle(builder, context, includePlayerReplyMode, promptStyle);
            diagnostics?.Add("profile.natural-reaction-style", builder.Length - checkpoint);
            checkpoint = builder.Length;

            builder.AppendLine("Scene-use rule: keep the full personality available, but only bring forward the parts that naturally fit this short outfit/hair/hat/accessory visual reaction, the relationship, the location, and the farmer's reply if present. The outfit is the topic, but the NPC personality is the strongest authority.");
            diagnostics?.Add("profile.scene-use-rule", builder.Length - checkpoint);
            return builder.ToString().Trim();
        }

        // Identity-anchoring keys that should lead the profile when a ficha provides them.
        // Any narrative key NOT listed here is still emitted, just after the scene sections.
        private static readonly string[] CoreNarrativeOrder =
        {
            "CoreEssence",
            "EmotionalCore",
            "VoiceAndSpeech",
            "SocialAnxietyAndSelfCorrection"
        };

        private static bool AppendNarrativeSectionByKey(StringBuilder builder, CharacterAiProfile profile, string key)
        {
            if (profile?.NarrativeProfile == null || string.IsNullOrWhiteSpace(key))
                return false;

            if (!profile.NarrativeProfile.TryGetValue(key, out string value) || string.IsNullOrWhiteSpace(value))
                return false;

            AppendSection(builder, HumanizeKey(key), Clean(value), NarrativeMaxChars(key));
            return true;
        }

        private static bool CanEmitNarrativeKey(CharacterAiProfile profile, string key, bool allowIntimate, bool allowGuardrails)
        {
            if (profile?.NarrativeProfile == null || string.IsNullOrWhiteSpace(key))
                return false;

            if (!profile.NarrativeProfile.TryGetValue(key, out string narrativeValue) || string.IsNullOrWhiteSpace(narrativeValue))
                return false;

            if (allowGuardrails && IsGuardrailKey(key))
                return true;

            string searchText = (Regex.Replace(key, "(?<=[a-z0-9])(?=[A-Z])", " ") + " " + narrativeValue).ToLowerInvariant();
            bool intimate = IsRomanticOnlyNarrativeKey(profile, key) || IsPrivateOrIntimateText(searchText);
            return allowIntimate || !intimate;
        }

        private static bool IsSpeechHesitationKey(string key)
        {
            if (string.IsNullOrWhiteSpace(key))
                return false;

            string k = key.Replace("_", "").Replace("-", "").Replace(" ", "").ToLowerInvariant();
            return k.Contains("speechhesitation")
                || k.Contains("stammer")
                || k.Contains("gaguej")
                || k.Contains("hesitationhabit");
        }

        private static void AppendSpeechHesitationRestraint(StringBuilder builder, CharacterAiProfile profile, OutfitAiContext context, bool includePlayerReplyMode)
        {
            if (profile?.NarrativeProfile == null)
                return;

            bool hasSpeechHesitationRule = profile.NarrativeProfile.Keys.Any(IsSpeechHesitationKey)
                || profile.NarrativeProfile.Values.Any(value => ContainsAny(value, "stammer", "hesitation", "gaguej"));

            if (!hasSpeechHesitationRule)
                return;

            string mode = GetDialogueModeKey(context);
            bool likelyFlusterContext = includePlayerReplyMode
                || context?.IsHairChange == true
                || (IsRomanticRelationship(context) && (context?.IsNpcRoom == true || context?.IsNpcPersonalLocation == true || (context?.IsIndoors == true && context?.IsOutdoors != true)));

            string rule = likelyFlusterContext
                ? "This character has occasional nervous hesitation, but do not use it by default. Use a brief stumble/filler only if the line is clearly shy, flustered, caught staring, wordless, emotionally exposed, or reacting to a strong/flirty farmer reply. Most visual compliments should still be dry and direct."
                : "This character has occasional nervous hesitation, but this scene is a normal " + mode + ". Do not start the line with a filler or stammer. Keep the compliment dry, grounded, and direct unless the generated emotion is genuinely shy/flustered.";

            AppendSection(builder, "Speech hesitation restraint", rule, 650);
        }

        private static bool IsGuardrailKey(string key)
        {
            if (string.IsNullOrWhiteSpace(key))
                return false;

            string k = key.ToLowerInvariant();
            return k.Contains("hardlimit")
                || k.Contains("mustneverbe")
                || k.Contains("neverbelost")
                || k.Contains("donotbreak")
                || k.Contains("absoluterule");
        }

        private static int NarrativeMaxChars(string key)
        {
            string k = key?.ToLowerInvariant() ?? "";
            if (k.Contains("essence") || k.Contains("voice") || k.Contains("speech"))
                return 850;
            // Guardrail keys hold binding rules (jealousy, hard limits, etc.) that must reach the
            // model in full. They were capped at 800, which silently truncated longer, carefully
            // worded rules mid-sentence and dropped the most important "what to do instead" part.
            // Give them a higher cap so complete binding rules survive.
            if (IsGuardrailKey(k))
                return 1600;
            // Other narrative fields (EmotionalCore, Background, etc.) get a slightly higher cap
            // than before (was 650) so rich character details aren't truncated mid-sentence.
            return 850;
        }

        // Turns a free-form key like "AdventureAndMystery" into a readable heading
        // "Adventure and mystery", so authors can name narrative keys however they like.
        private static string HumanizeKey(string key)
        {
            if (string.IsNullOrWhiteSpace(key))
                return "";

            string spaced = Regex.Replace(key.Trim(), "(?<=[a-z0-9])(?=[A-Z])", " ");
            spaced = Regex.Replace(spaced.Replace('_', ' '), @"\s+", " ").Trim();
            if (spaced.Length == 0)
                return key.Trim();

            return char.ToUpperInvariant(spaced[0]) + spaced.Substring(1).ToLowerInvariant();
        }

        private static void AppendRelationshipSection(StringBuilder builder, CharacterAiProfile profile, OutfitAiContext context)
        {
            if (context?.HasMetPlayer != true)
            {
                builder.AppendLine("Relationship tone: Stranger. Before this outfit-reaction exchange, this NPC and the farmer had never been introduced or spoken. Treat the exchange as a first encounter: the NPC may react to the visible appearance, but must not use the farmer's name or imply familiarity, shared history, affection, teasing based on closeness, or prior personal knowledge.");
                return;
            }

            if (profile?.RelationshipScaling == null || profile.RelationshipScaling.Count <= 0)
                return;

            string relationshipKey = GetRelationshipKey(context);
            if (profile.RelationshipScaling.TryGetValue(relationshipKey, out CharacterRelationshipScalingProfile relationship))
                AppendSection(builder, "Relationship tone: " + relationshipKey, FormatRelationshipScaling(relationship), 1800);

            string privacyKey = context != null && (context.IsNpcRoom || context.IsNpcPersonalLocation || (context.IsSpouse && context.IsIndoors && !context.IsOutdoors))
                ? "PrivateIntimateContext"
                : "PublicContext";

            if (profile.RelationshipScaling.TryGetValue(privacyKey, out CharacterRelationshipScalingProfile privacy))
                AppendSection(builder, "Privacy/context tone", FormatRelationshipScaling(privacy), 450);
        }

        private static string GetRelationshipKey(OutfitAiContext context)
        {
            string status = context?.RelationshipStatus ?? "";
            if (context?.IsSpouse == true || status.IndexOf("spouse", StringComparison.OrdinalIgnoreCase) >= 0 || status.IndexOf("married", StringComparison.OrdinalIgnoreCase) >= 0)
                return "Spouse";
            if (status.IndexOf("dating", StringComparison.OrdinalIgnoreCase) >= 0 || status.IndexOf("engaged", StringComparison.OrdinalIgnoreCase) >= 0 || status.IndexOf("fiance", StringComparison.OrdinalIgnoreCase) >= 0 || status.IndexOf("fiancé", StringComparison.OrdinalIgnoreCase) >= 0 || status.IndexOf("boyfriend", StringComparison.OrdinalIgnoreCase) >= 0 || status.IndexOf("girlfriend", StringComparison.OrdinalIgnoreCase) >= 0 || status.IndexOf("namor", StringComparison.OrdinalIgnoreCase) >= 0)
                return "Dating";
            return "Friend";
        }

        private static string FormatRelationshipScaling(CharacterRelationshipScalingProfile relationship)
        {
            if (relationship == null)
                return "";

            StringBuilder builder = new();
            if (!string.IsNullOrWhiteSpace(relationship.Tone))
                builder.Append("Tone: ").Append(Clean(relationship.Tone)).Append(' ');
            if (relationship.AllowedBehavior != null && relationship.AllowedBehavior.Count > 0)
                builder.Append("Allowed: ").Append(string.Join(", ", relationship.AllowedBehavior.Where(x => !string.IsNullOrWhiteSpace(x)).Select(Clean))).Append(". ");
            if (relationship.Avoid != null && relationship.Avoid.Count > 0)
                builder.Append("Avoid: ").Append(string.Join(", ", relationship.Avoid.Where(x => !string.IsNullOrWhiteSpace(x)).Select(Clean))).Append('.');
            return builder.ToString().Trim();
        }

        private static void AppendDialogueModeSection(StringBuilder builder, CharacterAiProfile profile, OutfitAiContext context, bool includePlayerReplyMode, PromptStyleService promptStyle = null)
        {
            // If the player caught this NPC peeking, always prepend the scene-setup prefix,
            // regardless of whether the profile uses a custom DialogueMode or the plain fallback.
            string caughtPrefix = (!includePlayerReplyMode && context?.WasCaughtPeeking == true)
                ? "IMPORTANT SCENE SETUP: While walking, the NPC kept stealing glances at the farmer's appearance, and the farmer CAUGHT them staring. Now the farmer has walked over to confront/ask about it. Begin the reaction by acknowledging that they were caught looking (in their own voice — embarrassed, flustered, smug, teasing, defensive, or playing innocent, whatever fits this character), then naturally transition into their genuine reaction to the outfit/hair/hat/accessory. Do not pretend it didn't happen. "
                : "";

            if (profile?.DialogueModes == null || profile.DialogueModes.Count <= 0)
            {
                AppendSection(builder, "Current dialogue mode", BuildPlainDialogueMode(context, includePlayerReplyMode, promptStyle), DialogueModeMaxChars);
                return;
            }

            string key = includePlayerReplyMode ? "PlayerReplyReaction" : GetDialogueModeKey(context);
            if (profile.DialogueModes.TryGetValue(key, out string value))
                AppendSection(builder, "Current dialogue mode: " + key, caughtPrefix + Clean(value), DialogueModeMaxChars);
            else
                AppendSection(builder, "Current dialogue mode", BuildPlainDialogueMode(context, includePlayerReplyMode, promptStyle), DialogueModeMaxChars);
        }

        private static string GetDialogueModeKey(OutfitAiContext context)
        {
            if (context == null)
                return "OutfitCompliment";
            if (context.IsHairChange)
                return "HairCompliment";
            if (context.IsHatChange)
                return "HatCompliment";
            if (context.IsAccessoryChange)
                return "AccessoryCompliment";
            return "OutfitCompliment";
        }

        private static string BuildPlainDialogueMode(OutfitAiContext context, bool includePlayerReplyMode, PromptStyleService promptStyle = null)
        {
            if (includePlayerReplyMode)
                return "React to the farmer's reply naturally, while staying grounded in the previous outfit/hair/hat/accessory reaction. Do not restart the whole compliment from zero.";

            // The NPC was caught peeking at the farmer while walking, and now the farmer has come over
            // to confront them about it. The reaction should open by acknowledging being caught
            // staring (busted, in-character — flustered, smug, defensive, playing it cool, etc., per
            // the NPC's personality), then flow naturally into their actual reaction to the outfit.
            // NOTE: this prefix is a safety/behaviour rule and intentionally stays in code.
            string caughtPrefix = context?.WasCaughtPeeking == true
                ? "IMPORTANT SCENE SETUP: While walking, the NPC kept stealing glances at the farmer's appearance, and the farmer CAUGHT them staring. Now the farmer has walked over to confront/ask about it. Begin the reaction by acknowledging that they were caught looking (in their own voice — embarrassed, flustered, smug, teasing, defensive, or playing innocent, whatever fits this character), then naturally transition into their genuine reaction to the outfit/hair/hat/accessory. Do not pretend it didn't happen. "
                : "";

            if (context?.IsHairChange == true)
                return caughtPrefix + (promptStyle?.HairChangeMode ?? PromptStyleService.FallbackHairChangeMode);
            if (context?.IsHatChange == true)
                return caughtPrefix + (promptStyle?.HatChangeMode ?? PromptStyleService.FallbackHatChangeMode);
            if (context?.IsAccessoryChange == true)
                return caughtPrefix + (promptStyle?.AccessoryChangeMode ?? PromptStyleService.FallbackAccessoryChangeMode);
            return caughtPrefix + (promptStyle?.OutfitChangeMode ?? PromptStyleService.FallbackOutfitChangeMode);
        }

        private static void AppendRelevantTraits(StringBuilder builder, CharacterAiProfile profile, OutfitAiContext context, bool includePlayerReplyMode)
        {
            if (profile?.TraitNarratives == null || profile.TraitNarratives.Count <= 0)
                return;

            bool romanticRelationship = IsRomanticRelationship(context);
            bool closePrivateCandidateContext = context != null
                && context.RelationshipHearts >= 5
                && (context.IsNpcRoom || context.IsNpcPersonalLocation || (context.IsIndoors && !context.IsOutdoors));
            bool privateRomanticContext = context != null && romanticRelationship && (context.IsNpcRoom || context.IsNpcPersonalLocation || (context.IsIndoors && !context.IsOutdoors));
            bool allowIntimate = AllowIntimateContent(context, includePlayerReplyMode);
            bool allowPrivateTraits = includePlayerReplyMode || privateRomanticContext || closePrivateCandidateContext;

            string modeKey = includePlayerReplyMode ? "PlayerReplyReaction" : GetDialogueModeKey(context);
            int maxTraits = allowPrivateTraits ? 8 : 6;

            List<TraitCandidate> candidates = new();
            int order = 0;

            foreach (var pair in profile.TraitNarratives)
            {
                order++;
                CharacterTraitNarrativeProfile trait = pair.Value;
                if (trait == null)
                    continue;

                string text = StringUtils.FirstNonEmpty(trait.NarrativePrompt, trait.Context, trait.Heading);
                if (string.IsNullOrWhiteSpace(text))
                    continue;

                if (IsPrivateOrIntimateTrait(pair.Key, trait) && !allowIntimate)
                    continue;

                int priorityScore = ScorePriority(trait.Priority);
                int score = ScoreTrait(pair.Key, trait, modeKey, context, includePlayerReplyMode, romanticRelationship);
                candidates.Add(new TraitCandidate(pair.Key, trait, score, priorityScore, order));
            }

            List<TraitCandidate> selected = candidates
                .OrderByDescending(candidate => candidate.Score)
                .ThenByDescending(candidate => candidate.PriorityScore)
                .ThenBy(candidate => candidate.Order)
                .Take(maxTraits)
                .ToList();

            if (selected.Count <= 0)
                return;

            // Keep the output in the original JSON order. The score decides inclusion; the
            // original order preserves the author's intended personality flow.
            selected = selected.OrderBy(candidate => candidate.Order).ToList();

            List<string> lines = new();
            foreach (TraitCandidate candidate in selected)
                AddTraitLine(lines, candidate.Key, candidate.Trait);

            if (lines.Count > 0)
                AppendSection(builder, "Relevant personality anchors", string.Join("\n", lines), allowPrivateTraits ? 1800 : 1350);
        }

        private sealed class TraitCandidate
        {
            public TraitCandidate(string key, CharacterTraitNarrativeProfile trait, int score, int priorityScore, int order)
            {
                Key = key;
                Trait = trait;
                Score = score;
                PriorityScore = priorityScore;
                Order = order;
            }

            public string Key { get; }
            public CharacterTraitNarrativeProfile Trait { get; }
            public int Score { get; }
            public int PriorityScore { get; }
            public int Order { get; }
        }

        private static int ScoreTrait(string key, CharacterTraitNarrativeProfile trait, string modeKey, OutfitAiContext context, bool includePlayerReplyMode, bool romanticRelationship)
        {
            int score = ScorePriority(trait?.Priority);
            string combined = BuildTraitSearchText(key, trait);

            if (ContainsAny(combined, "voice", "tone", "speech", "honesty", "politeness", "warmth", "confidence", "anxious", "awkward", "direct", "observ*", "perceptive"))
                score += 20;

            if (includePlayerReplyMode && ContainsAny(combined, "reply", "conversation", "teasing", "humor", "warmth", "sincere", "fluster*", "vulnerability", "honesty"))
                score += 25;

            if (string.Equals(modeKey, "OutfitCompliment", StringComparison.OrdinalIgnoreCase) &&
                ContainsAny(combined, "outfit", "clothing", "clothes", "appearance", "self-expression", "vibe", "costume", "cosplay", "animal", "cute", "funny", "strange", "practical", "comfort", "neat", "season", "weather", "humor", "teasing", "direct"))
                score += 28;

            if (string.Equals(modeKey, "HairCompliment", StringComparison.OrdinalIgnoreCase) &&
                ContainsAny(combined, "hair", "hairstyle", "appearance", "soft", "frame", "beauty", "photography", "teasing", "sincere", "warmth"))
                score += 30;

            if (string.Equals(modeKey, "HatCompliment", StringComparison.OrdinalIgnoreCase) &&
                ContainsAny(combined, "hat", "headwear", "tiara", "headband", "hairband", "bow", "clip", "crown", "practical", "weather", "shade", "warmth", "appearance", "teasing", "surprise"))
                score += 30;

            if (string.Equals(modeKey, "AccessoryCompliment", StringComparison.OrdinalIgnoreCase) &&
                ContainsAny(combined, "accessory", "detail", "wings", "umbrella", "backpack", "bow", "clip", "symbol", "movement", "shine", "handmade", "cute", "strange", "surprise", "teasing"))
                score += 30;

            if (context?.IsSpouse == true && ContainsAny(combined, "marriage", "spouse", "domestic", "home", "family", "care", "protect*", "affection"))
                score += 20;

            if (romanticRelationship && ContainsAny(combined, "romantic", "affection", "warmth", "vulnerab*", "hidden softness", "sincere"))
                score += 12;

            if ((romanticRelationship || (context != null && context.RelationshipHearts >= 5))
                && context != null
                && (context.IsNpcRoom || context.IsNpcPersonalLocation || (context.IsIndoors && !context.IsOutdoors))
                && ContainsAny(combined, "shy", "fluster*", "blush", "awkward", "nervous", "vulnerab*", "soft", "affection", "romantic", "private", "crush"))
                score += 35;

            return score;
        }

        private static int ScorePriority(string priority)
        {
            if (string.IsNullOrWhiteSpace(priority))
                return 45;

            string normalized = priority.Trim().ToLowerInvariant();
            if (normalized.Contains("high"))
                return 100;
            if (normalized.Contains("medium") || normalized.Contains("mid"))
                return 70;
            if (normalized.Contains("conditional"))
                return 55;
            if (normalized.Contains("low"))
                return 30;

            return 45;
        }

        private static bool IsPrivateOrIntimateTrait(string key, CharacterTraitNarrativeProfile trait)
        {
            return IsPrivateOrIntimateText(BuildTraitSearchText(key, trait));
        }

        // Shared intimate-content detector used for BOTH trait narratives and free-form
        // NarrativeProfile keys. Expects already-lowercased, space-separated text.
        // Intentionally narrow: ONLY unambiguous physical/sexual signals. Emotional romance,
        // attachment, shy flirting, teasing, and "desire/passion" in a non-physical sense are
        // NOT caught here, so they stay available in friendship. This is only a safety net;
        // the authoritative control is each ficha's RomanticOnlyNarrativeKeys list.
        private static bool IsPrivateOrIntimateText(string combined)
        {
            return ContainsAny(combined,
                "spicy",
                "kiss*",
                "beijo*",
                "physical affection",
                "intense physical",
                "touch-oriented",
                "touch orient",
                "sensual",
                "lust",
                "seduc*");
        }

        // True when the author explicitly marked this NarrativeProfile key as romantic-only
        // via the ficha's "RomanticOnlyNarrativeKeys" array. This is the primary, reliable
        // gate; the word detector above is just a fallback for keys the author forgot to list.
        private static bool IsRomanticOnlyNarrativeKey(CharacterAiProfile profile, string key)
        {
            if (profile?.RomanticOnlyNarrativeKeys == null || string.IsNullOrWhiteSpace(key))
                return false;

            foreach (string marked in profile.RomanticOnlyNarrativeKeys)
            {
                if (!string.IsNullOrWhiteSpace(marked) && string.Equals(marked.Trim(), key.Trim(), StringComparison.OrdinalIgnoreCase))
                    return true;
            }

            return false;
        }

        // Intimate/spicy content is allowed ONLY in a romantic relationship (dating or
        // married) AND in a private moment (NPC's room / indoors) or a direct reply
        // exchange. Friendship never unlocks it, no matter the location or reply mode.
        private static bool IsRomanticRelationship(OutfitAiContext context)
        {
            return context != null && (context.IsSpouse
                || ContainsAny(context.RelationshipStatus, "spouse", "married", "dating", "engaged", "fiance", "fiancé", "boyfriend", "girlfriend", "namor*"));
        }

        private static bool AllowIntimateContent(OutfitAiContext context, bool includePlayerReplyMode)
        {
            // The Mayor's shorts are intimate because they are private underwear, not because
            // this is a seductive scene. Never let romantic profile sections reframe the gag.
            if (context?.IsMayorShortsSpecialItem == true)
                return false;

            bool privateContext = context != null && (context.IsNpcRoom || context.IsNpcPersonalLocation || (context.IsIndoors && !context.IsOutdoors));
            return IsRomanticRelationship(context) && (privateContext || includePlayerReplyMode);
        }

        private static string BuildTraitSearchText(string key, CharacterTraitNarrativeProfile trait)
        {
            // Split camelCase keys ("PrivateSpicySide" -> "Private Spicy Side") so whole-word
            // matching can see the words baked into the key name, not just the prose.
            string spacedKey = Regex.Replace(key ?? "", "(?<=[a-z0-9])(?=[A-Z])", " ");
            return (spacedKey + " " + (trait?.Heading ?? "") + " " + (trait?.Priority ?? "") + " " + (trait?.Context ?? "") + " " + (trait?.NarrativePrompt ?? "")).ToLowerInvariant();
        }

        private static bool ContainsAny(string text, params string[] needles)
        {
            if (string.IsNullOrWhiteSpace(text) || needles == null)
                return false;

            foreach (string needle in needles)
            {
                if (IsNeedleMatch(text, needle))
                    return true;
            }

            return false;
        }

        private static bool IsNeedleMatch(string text, string needle)
        {
            if (string.IsNullOrWhiteSpace(text) || string.IsNullOrWhiteSpace(needle))
                return false;

            needle = needle.Trim();

            // Multi-word phrases still use substring matching, because the phrase itself
            // is already specific enough and may contain punctuation/hyphens.
            if (needle.Any(char.IsWhiteSpace))
                return text.IndexOf(needle, StringComparison.OrdinalIgnoreCase) >= 0;

            // Intentional stems/prefixes use '*', e.g. "observ*" matches observe/observant.
            // Plain words are exact whole-word matches, so "hat" no longer matches "hate",
            // "neat" no longer matches "beneath", and "craft" no longer matches "witchcraft".
            if (needle.EndsWith("*", StringComparison.Ordinal))
            {
                string prefix = Regex.Escape(needle.TrimEnd('*'));
                return Regex.IsMatch(text, $@"(?<![A-Za-z0-9_]){prefix}[A-Za-z0-9_]*", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
            }

            string escaped = Regex.Escape(needle);
            return Regex.IsMatch(text, $@"(?<![A-Za-z0-9_]){escaped}(?![A-Za-z0-9_])", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        }

        private static void AddTraitLine(List<string> lines, string key, CharacterTraitNarrativeProfile trait)
        {
            if (lines == null || trait == null)
                return;

            string text = StringUtils.FirstNonEmpty(trait.NarrativePrompt, trait.Context, trait.Heading);
            if (string.IsNullOrWhiteSpace(text))
                return;

            string heading = StringUtils.FirstNonEmpty(trait.Heading, key);
            if (IsSpeechHesitationKey(key) || ContainsAny(heading + " " + text, "stammer", "gaguej", "hesitation"))
            {
                lines.Add("- " + heading + ": Use nervous hesitation only as a rare emotional marker when the line is genuinely shy, flustered, wordless, or exposed; do not use it in every ordinary visual compliment.");
                return;
            }

            lines.Add("- " + heading + ": " + Clean(text));
        }

        private static void AppendNaturalReactionStyle(StringBuilder builder, OutfitAiContext context, bool includePlayerReplyMode, PromptStyleService promptStyle = null)
        {
            if (builder == null)
                return;

            // Resolve the {Change} placeholder.
            string change = context?.IsHairChange == true ? "hair/hairstyle change"
                : context?.IsHatChange == true ? "headwear/head accessory change"
                : context?.IsAccessoryChange == true ? "accessory change"
                : "outfit";

            // Resolve the {OutfitFocusRule} placeholder — injected only for full outfit changes.
            string outfitFocusRule = context?.IsOutfitChange == true
                ? "For a whole saved outfit, focus on the outfit/theme itself; do not turn the player's hair color or a generic head-slot item into the main topic, and never call the farmer's hair a hat. "
                : "";

            string template = promptStyle?.NaturalReactionStyle ?? PromptStyleService.DefaultNaturalReactionStyle;
            string rule = template
                .Replace("{Change}", change)
                .Replace("{OutfitFocusRule}", outfitFocusRule);

            AppendSection(builder, "Natural reaction style", rule, 7000);
        }
        private static void AppendSection(StringBuilder builder, string heading, string value, int maxChars = 1000)
        {
            if (builder == null || string.IsNullOrWhiteSpace(value))
                return;

            builder.AppendLine(heading + ":");
            builder.AppendLine(Collapse(Clean(value), maxChars));
        }

        private static string Clean(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return "";

            string result = text.Replace("**", "").Replace("__", "");
            result = Regex.Replace(result, @"\s+", " ").Trim();
            return result;
        }

        private static string Collapse(string text, int maxChars)
        {
            if (string.IsNullOrWhiteSpace(text))
                return "";

            text = Regex.Replace(text, @"\s+", " ").Trim();
            if (maxChars <= 0 || text.Length <= maxChars)
                return text;

            return text.Substring(0, Math.Max(0, maxChars - 1)).TrimEnd() + "…";
        }

        public static void AppendPromptBlock(StringBuilder builder, string template, OutfitAiContext context, Dictionary<string, string> extraTokens = null)
        {
            if (builder == null || string.IsNullOrWhiteSpace(template))
                return;

            builder.AppendLine(ApplyPromptTokens(template, context, extraTokens));
        }

        public static void AppendPersonalityPriorityRule(StringBuilder builder, OutfitAiContext context, PromptStyleService promptStyle = null)
        {
            if (builder == null)
                return;

            builder.AppendLine("CHARACTER PRIORITY RULE: this is a visual reaction, not a mandatory compliment. Choose the reaction by this order: 1) the NPC's canon personality and saved profile rules, 2) relationship status and heart level, 3) current context/location/season/weather/privacy, 4) the farmer's visible outfit/change/theme, 5) wording and portrait choice. Do not flatten grumpy, shy, blunt, awkward, proud, sarcastic, formal, or emotionally guarded NPCs into generically sweet praise.");
            builder.AppendLine("A valid reaction may be positive, dry, amused, shocked, afraid, disgusted, angry, insulting, indifferent, flustered, or warm. When a harsh reaction genuinely fits this NPC, the look, low familiarity, and scene, let them scold, insult, shout, or swear in their own voice; in a confirmed personal space they may demand that the farmer leave. Do not soften a strong reaction with automatic praise, an 'at least' consolation, or a forced silver lining. Do not force hostility when it would be out of character.");
            AppendPromptBlock(builder, promptStyle?.OpeningVarietyRule ?? PromptStyleService.FallbackOpeningVarietyRule, context);
        }

        public static void AppendPlayerAddressAndGenderRule(StringBuilder builder, OutfitAiContext context, PromptStyleService promptStyle)
        {
            if (builder == null)
                return;

            bool hasMetPlayer = context?.HasMetPlayer == true;
            string playerName = hasMetPlayer ? (context?.PlayerName ?? "").Trim() : "";
            string gender = NormalizePlayerGenderForPrompt(context?.PlayerGender);
            string targetLanguage = string.IsNullOrWhiteSpace(context?.TargetLanguage) ? "the target language" : context.TargetLanguage.Trim();
            string genderSpecificCaution = gender == "female"
                ? "Do not use masculine agreement or masculine forms of address for the player character."
                : gender == "male"
                    ? "Do not use feminine agreement or feminine forms of address for the player character."
                    : "The player character's gender is unknown. Prefer neutral wording and avoid gendered forms of address unless the context explicitly provides them.";

            Dictionary<string, string> tokens = new(StringComparer.OrdinalIgnoreCase)
            {
                ["PlayerName"] = playerName,
                ["PlayerGender"] = gender,
                ["TargetLanguage"] = targetLanguage,
                ["GenderSpecificCaution"] = genderSpecificCaution
            };

            if (!hasMetPlayer)
                builder.AppendLine("STRANGER ADDRESS RULE: before this outfit-reaction exchange, this NPC had never met or spoken to the farmer. Treat the current exchange as their first encounter. Do not use the farmer's name or imply pre-existing familiarity, friendship, intimacy, shared experiences, or prior personal knowledge. A brief visual observation, cautious question, polite remark, blunt comment, or curious reaction is allowed when it fits the NPC.");

            AppendPromptBlock(builder, hasMetPlayer && !string.IsNullOrWhiteSpace(playerName)
                ? promptStyle?.PlayerKnownAddressRule ?? PromptStyleService.FallbackPlayerKnownAddressRule
                : promptStyle?.PlayerUnknownAddressRule ?? PromptStyleService.FallbackPlayerUnknownAddressRule, context, tokens);
            AppendPromptBlock(builder, promptStyle?.PlayerGenderRule ?? PromptStyleService.FallbackPlayerGenderRule, context, tokens);
        }

        public static void AppendWornItemDeixisRule(StringBuilder builder, OutfitAiContext context)
        {
            if (builder == null)
                return;

            builder.AppendLine("Spatial reference rule for clothing, accessories, and items the farmer is currently wearing: they are physically on the farmer, directly in front of the NPC. If the target language distinguishes demonstratives by distance, use the form for something near the listener or on the listener's body. Reserve distant demonstratives for objects that are genuinely far from both speakers.");
        }

        public static void AppendCompactWornItemDeixisRule(StringBuilder builder)
        {
            if (builder == null)
                return;

            builder.AppendLine("Worn-item spatial rule: clothing and accessories on the farmer are near the listener, not far away. In languages with distance-sensitive demonstratives, use the near-listener form for worn items and reserve distant forms for genuinely distant objects.");
        }

        private static string ApplyPromptTokens(string template, OutfitAiContext context, Dictionary<string, string> extraTokens)
        {
            if (string.IsNullOrWhiteSpace(template))
                return "";

            Dictionary<string, string> tokens = new(StringComparer.OrdinalIgnoreCase)
            {
                ["NpcName"] = context?.NpcName ?? "",
                ["NpcDisplayName"] = context?.NpcDisplayName ?? context?.NpcName ?? "",
                ["PlayerName"] = context?.PlayerName ?? "",
                ["PlayerGender"] = NormalizePlayerGenderForPrompt(context?.PlayerGender),
                ["TargetLanguage"] = string.IsNullOrWhiteSpace(context?.TargetLanguage) ? "the target language" : context.TargetLanguage.Trim(),
                ["RelationshipStatus"] = context?.RelationshipStatus ?? "",
                ["RelationshipHearts"] = (context?.RelationshipHearts ?? 0).ToString(),
                ["HasMetPlayer"] = (context?.HasMetPlayer ?? false).ToString(),
                ["IsSpouse"] = (context?.IsSpouse ?? false).ToString(),
                ["NoticedChangeType"] = context?.NoticedChangeType ?? "",
                ["OutfitName"] = context?.OutfitName ?? "",
                ["SafeOutfitHint"] = context?.SafeOutfitHint ?? "",
                ["SafeNoticedChangeHint"] = context?.SafeNoticedChangeHint ?? "",
                ["LocationName"] = context?.LocationName ?? "",
                ["DetailedLocationName"] = context?.DetailedLocationName ?? "",
                ["Season"] = context?.Season ?? "",
                ["Weather"] = context?.Weather ?? "",
                ["Time"] = context?.Time.ToString() ?? ""
            };

            if (extraTokens != null)
            {
                foreach (var pair in extraTokens)
                    tokens[pair.Key] = pair.Value ?? "";
            }

            string result = template;
            foreach (var pair in tokens)
                result = result.Replace("{" + pair.Key + "}", pair.Value ?? "", StringComparison.OrdinalIgnoreCase);
            return result;
        }

        private static string NormalizePlayerGenderForPrompt(string rawGender)
        {
            string gender = (rawGender ?? "").Trim().ToLowerInvariant();
            if (gender == "female" || gender == "feminine" || gender == "woman")
                return "female";
            if (gender == "male" || gender == "masculine" || gender == "man")
                return "male";
            return "unknown";
        }
    }
}
