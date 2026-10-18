using StardewModdingAPI;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using OutfitReactions;

namespace OutfitReactions.Ai
{
    internal sealed partial class OutfitAiService
    {
        private string BuildPrompt(CharacterAiProfile profile, OutfitAiContext context, ModConfig config, ActiveAiSettings ai)
        {
            if (ActiveAiSettingsResolver.IsLocal(ai))
                return BuildLocalPrompt(profile, context, ai);

            // PROMPT STRUCTURE (rewritten):
            //   1. WHO this character is (personality first, as the main lens).
            //   2. The current scene (relationship, outfit/change, location, season...).
            //   3. How to react (one consolidated reaction-style block, no duplicates).
            //   4. Technical/output rules (JSON, portraits, language, length) LAST.
            // The personality leads so the model reads everything else through it,
            // instead of meeting dozens of generic rules before the character.
            StringBuilder builder = new();
            int characterBlockStart = builder.Length;

            // ---------------------------------------------------------------
            // 1. CHARACTER FIRST — this is the strongest authority in the prompt.
            // ---------------------------------------------------------------
            builder.AppendLine("You are writing one short, in-character Stardew Valley reaction to the farmer's appearance. Your single highest priority is that the line sounds unmistakably like " + context.NpcDisplayName + " and nobody else. Stay in this exact personality, voice, and tone at all times.");
            builder.AppendLine();
            builder.AppendLine("WHO YOU ARE (read this first; it overrides every generic instruction below):");

            PromptSizeBreakdown profileDiagnostics = new();
            string focusedProfile = CharacterPromptBuilder.BuildForOutfitCompliment(profile, context, includePlayerReplyMode: false, promptStyle: PromptStyle, diagnostics: profileDiagnostics);
            if (!string.IsNullOrWhiteSpace(focusedProfile))
                builder.AppendLine(focusedProfile);
            builder.AppendLine();

            CharacterPromptBuilder.AppendPersonalityPriorityRule(builder, context, PromptStyle);
            builder.AppendLine();
            int characterBlockEnd = builder.Length;

            // Voice samples (MVP): a few REAL in-game lines from this NPC, used only to
            // anchor their voice/tone. Always below the personality in authority.
            int voiceBlockStart = builder.Length;
            voiceSamples.AppendToPrompt(builder, context, config);
            int voiceBlockEnd = builder.Length;

            // ---------------------------------------------------------------
            // 2. THE SCENE — the situation this specific character is reacting to.
            // ---------------------------------------------------------------
            builder.AppendLine("CURRENT SCENE");
            CharacterPromptBuilder.AppendPlayerAddressAndGenderRule(builder, context, PromptStyle);
            CharacterPromptBuilder.AppendCompactWornItemDeixisRule(builder);
            builder.AppendLine("Relationship status: " + context.RelationshipStatus + ". Heart level: " + context.RelationshipHearts + ". Is spouse: " + context.IsSpouse + ".");
            builder.AppendLine(BuildRelationshipDepthGuidance(context));

            // Scene grounding / technical-label safety (kept).
            builder.AppendLine(BuildCompactTechnicalContextLabelInstruction());
            builder.AppendLine(BuildCompactSceneGroundingInstruction(context));
            int sceneGroundingEnd = builder.Length;

            // Outfit / theme clues for this scene.
            // In special-item-only mode the reaction must focus exclusively on the special item
            // (e.g. the Mayor's shorts), so outfit name, theme, and visual summary are suppressed
            // to prevent the model from drifting into outfit commentary.
            // Exception: when SpecialItemCombinedMode is true (user chose "Outfit + item"),
            // the outfit context is intentionally kept so the NPC can compare the two.
            // Vanilla hat-only mode must suppress these clues too; otherwise a saved outfit name
            // can compete with the equipped/removed hat and make the model describe the outfit as
            // the thing that changed.
            bool includeOutfitContext = !context.VanillaHatHatOnlyMode
                && (!context.SpecialItemOnlyMode || context.SpecialItemCombinedMode);
            if (includeOutfitContext)
            {
                bool outfitNameIsTechnical = !string.IsNullOrWhiteSpace(context.OutfitName)
                    && OutfitNameLooksTechnical(context.OutfitName);
                string readableSavedOutfitThemeClue = GetReadableSavedOutfitThemeClue(context);
                if (outfitNameIsTechnical)
                    builder.AppendLine("Do not quote, repeat, translate, or mention the full technical saved outfit name literally. Use the readable theme meaning instead. If a readable part of the clue contains a recognizable reference or named theme, that reference may be mentioned naturally when it fits the NPC.");
                else
                    builder.AppendLine("You may naturally use ordinary in-world garment terms and mention a recognizable theme or reference from the name when it is clearly visible or fits the scene. Do not recite the full saved outfit name mechanically as a phrase.");
                builder.Append(SanitizeThemeContextForPrompt(context.ThemeContext) ?? "");
                builder.Append(SanitizeThemeContextForPrompt(context.ThemePriorityInstruction) ?? "");
                builder.AppendLine("Private saved outfit name, for theme/reference inference only: " + context.OutfitName);
                if (!string.IsNullOrWhiteSpace(readableSavedOutfitThemeClue))
                    builder.AppendLine("Readable semantic theme/reference clue from the saved outfit: " + readableSavedOutfitThemeClue + ". Treat its ordinary meaning as evidence for the outfit's intended theme; express it naturally rather than reciting the saved name.");
                AppendNoticedChangeContextForPrompt(builder, context, PromptStyle);
            }

            // Vision / no-vision evidence rules (kept; only stated once).
            if (context.HasVisionImage)
            {
                builder.AppendLine("Attached front/back images are visual evidence for the farmer's clear outfit silhouette, large accessories, overall style, and broad clothing colors. Use equipped support data as the authority for named items and confirmed colors. Hair is not a hat or part of the clothing palette; ignore scenery and uncertain details. Never mention images, screenshots, PNGs, or pixels in dialogue.");
                if (context.IsAccessoryChange)
                    builder.AppendLine("The noticed change is a Fashion Sense accessory (large back items, wings, backpacks, umbrellas, decorations, earrings, etc.; ignore makeup-like accessories). If it is too tiny or unclear to identify, do not guess: set needsClarification to true and make text a natural in-character line meaning 'there is something different about your look today, but I cannot quite identify it.'");
            }
            else
            {
                builder.AppendLine("No visual image is available. Use text clues only; do not claim exact colors, shapes, or tiny visible details unless they are explicitly stated by the saved outfit/change clue or support data.");
                if (context.IsHatChange || context.IsAccessoryChange)
                    builder.AppendLine("Hat/accessory changes only trigger when vision is enabled; if no image/support data is available, keep the line general instead of guessing.");
            }
            if (context.VanillaHatHatOnlyMode)
            {
                if (context.HasVanillaHatFraming)
                    CharacterPromptBuilder.AppendPromptBlock(builder, PromptStyle?.RemovedVanillaHatOnlyMode ?? PromptStyleService.FallbackRemovedVanillaHatOnlyMode, context);
                else
                    CharacterPromptBuilder.AppendPromptBlock(builder, PromptStyle?.VisibleVanillaHatOnlyMode ?? PromptStyleService.FallbackVisibleVanillaHatOnlyMode, context);
            }
            // In HAT-ONLY mode, skip the full-outfit visual summary so the model has nothing about
            // the clothes to latch onto — it should only see the hat-related context below. But the
            // hat equip/removal framing must still reach the model, so pass it on its own.
            if (includeOutfitContext)
                AppendFashionSenseVisualSummaryForPrompt(builder, context, PromptStyle);
            else if (context.VanillaHatHatOnlyMode && context.HasVanillaHatFraming)
                builder.AppendLine("Hat status: " + context.VanillaHatFraming);
            AppendSpecialItemReactionForPrompt(builder, context, PromptStyle);
            if (!context.SpecialItemOnlyMode)
            {
                AppendSpecialHatReactionForPrompt(builder, context, PromptStyle);
                if (includeOutfitContext)
                    AppendVanillaShirtContextForPrompt(builder, context);
                AppendVanillaHatMemoryForPrompt(builder, context, PromptStyle);
            }
            int outfitAndVisionEnd = builder.Length;

            // Location / time / season for this scene.
            AppendCompactLocationContext(builder, context);
            builder.AppendLine("Season: " + context.Season + ". Day of season: " + context.DayOfSeason + ". Year: " + context.Year + ". Weather: " + context.Weather + ".");
            AppendNaturalTimeContextForPrompt(builder, context);
            AppendWeatherLocationRule(builder, context);
            if (!string.IsNullOrWhiteSpace(context.FarmerBirthdayContext))
                builder.AppendLine("Farmer birthday context: " + context.FarmerBirthdayContext);
            string seasonalInstruction = includeOutfitContext ? BuildSeasonalAwarenessInstruction(context) : "";
            if (!string.IsNullOrWhiteSpace(seasonalInstruction))
                builder.AppendLine(seasonalInstruction);
            int environmentEnd = builder.Length;

            // Outfit memory + situational overrides (kept; these are scene facts).
            if (context.HasOutfitMemory && includeOutfitContext)
                builder.AppendLine(context.OutfitMemoryContext);
            string finalOverride2 = includeOutfitContext ? BuildFinalSituationalOverride(context) : "";
            if (!string.IsNullOrWhiteSpace(finalOverride2))
                builder.AppendLine(finalOverride2);
            string privateRevealingRule = BuildPrivateRevealingPromptRule(context);
            if (!string.IsNullOrWhiteSpace(privateRevealingRule))
                builder.AppendLine(privateRevealingRule);
            string sebastianCustomOverride = BuildSebastianCustomSoftnessOverride(context);
            if (!string.IsNullOrWhiteSpace(sebastianCustomOverride))
                builder.AppendLine(sebastianCustomOverride);
            string privateCandidateToneRule = BuildPrivateCandidateToneRule(context);
            if (!string.IsNullOrWhiteSpace(privateCandidateToneRule))
                builder.AppendLine(privateCandidateToneRule);
            builder.AppendLine();
            int sceneBlockEnd = builder.Length;

            // ---------------------------------------------------------------
            // 3. HOW TO REACT — one consolidated block. Each theme/accessory rule
            //    is now stated ONCE (previously each appeared multiple times).
            // ---------------------------------------------------------------
            AppendCompactReactionGuidance(builder, context);

            // Keep the live festival as the final semantic authority. Generic theme,
            // occasion, and seasonal guidance above must not override an active in-game
            // festival or reassign a fitting look to a different real-world occasion.
            if (!string.IsNullOrWhiteSpace(context.FestivalContext))
                builder.AppendLine("Festival context (final scene authority): " + context.FestivalContext);
            AppendRecentOpeningVarietyContext(builder);

            // ---------------------------------------------------------------
            // 4. TECHNICAL / OUTPUT RULES — moved to the very end on purpose.
            // ---------------------------------------------------------------
            builder.AppendLine();
            int reactionBlockEnd = builder.Length;
            builder.AppendLine("OUTPUT RULES (formatting only; never let these flatten the personality)");
            builder.AppendLine("Final dialogue language: " + context.TargetLanguage + ". Ignore any language written inside the character profile above; the final spoken line must use ONLY this language.");
            AppendExpressiveCuesRule(builder, config.EnableExpressiveAsteriskActions);
            AppendPunctuationRule(builder);
            AppendProfanityIntensityRule(builder, context, config.EnableProfanityFilter);
            builder.AppendLine("Approximate final dialogue length target: about " + Math.Clamp(ai.MaxCharacters, 80, 400) + " visible characters. Treat this as a soft estimate, not a hard cutoff: prefer staying near it, but always finish the current thought naturally even if the dialogue goes over. Do not pad, ramble, or repeat yourself.");
            builder.AppendLine("Keep it casual and natural, like a passing real-life comment. It may be one sentence, several sentences, or a longer naturally paced comment if the character's voice and scene support it.");
            CharacterPromptBuilder.AppendPromptBlock(builder, PromptStyle?.DialoguePacingRule ?? PromptStyleService.FallbackDialoguePacingRule, context);
            bool requiresThemeAnchor = RequiresSemanticThemeAnchor(context);
            builder.AppendLine(requiresThemeAnchor
                ? "Return only one compact JSON object with keys text, portrait, portraits, needsClarification, and themeAnchor. The text contains spoken dialogue, optional expressive cues, and #$b# breaks only: no metadata, explanations, markdown, or Stardew $portrait commands. Use portrait for the primary expression. If text has multiple #$b# boxes, portraits MUST contain exactly one valid key per box in order."
                : "Return only one compact JSON object with keys text, portrait, portraits, and needsClarification. The text contains spoken dialogue, optional expressive cues, and #$b# breaks only: no metadata, explanations, markdown, or Stardew $portrait commands. Use portrait for the primary expression. If text has multiple #$b# boxes, portraits MUST contain exactly one valid key per box in order.");
            AppendSemanticThemeAnchorOutputRule(builder, context);
            builder.AppendLine("Available portrait keys (use only the keys, never their $commands):");
            if (profile.Portraits != null)
            {
                foreach (var pair in profile.Portraits)
                    builder.AppendLine("- " + pair.Key + ": " + pair.Value?.Description);
            }
            builder.AppendLine("Choose each box's fitting expression deliberately: reuse the same key only while its mood stays the same, and change it when a later box has a real emotional shift. Do not invent a change merely for variety.");

            string prompt = builder.ToString();
            List<KeyValuePair<string, int>> diagnosticBlocks = new()
            {
                new KeyValuePair<string, int>("character-profile-and-personality", characterBlockEnd - characterBlockStart),
                new KeyValuePair<string, int>("voice-samples", voiceBlockEnd - voiceBlockStart),
                new KeyValuePair<string, int>("scene-and-outfit-context", sceneBlockEnd - voiceBlockEnd),
                new KeyValuePair<string, int>("scene.grounding-and-relationship", sceneGroundingEnd - voiceBlockEnd),
                new KeyValuePair<string, int>("scene.outfit-vision-and-special-items", outfitAndVisionEnd - sceneGroundingEnd),
                new KeyValuePair<string, int>("scene.location-season-weather", environmentEnd - outfitAndVisionEnd),
                new KeyValuePair<string, int>("scene.memory-and-situational-overrides", sceneBlockEnd - environmentEnd),
                new KeyValuePair<string, int>("reaction-guidance", reactionBlockEnd - sceneBlockEnd),
                new KeyValuePair<string, int>("output-and-portrait-rules", prompt.Length - reactionBlockEnd)
            };
            diagnosticBlocks.AddRange(profileDiagnostics.Blocks);

            PromptSizeDiagnostics.Log(
                monitor,
                "outfit-reaction",
                context.NpcName,
                ai.Provider,
                ai.Model,
                prompt.Length,
                context.HasVisionImage,
                diagnosticBlocks.ToArray());
            return prompt;
        }

        private void AppendCompactReactionGuidance(StringBuilder builder, OutfitAiContext context)
        {
            builder.AppendLine("HOW TO REACT (filtered through the personality above)");
            CharacterPromptBuilder.AppendPromptBlock(builder, PromptStyle?.ThemeRecognitionRule ?? PromptStyleService.FallbackThemeRecognitionRule, context);

            if (context.IsAccessoryChange || context.IsOutfitChange)
            {
                CharacterPromptBuilder.AppendPromptBlock(builder, PromptStyle?.CombinationAndOccasionRule ?? PromptStyleService.FallbackCombinationAndOccasionRule, context);
            }

        }

        private string BuildLocalPrompt(CharacterAiProfile profile, OutfitAiContext context, ActiveAiSettings ai)
        {
            string portraitKeys = profile?.Portraits != null && profile.Portraits.Count > 0
                ? string.Join(", ", profile.Portraits.Keys)
                : "";
            string portraitCommands = PortraitResolver.BuildPortraitCommandList(profile);
            ModConfig config = getConfig?.Invoke() ?? new ModConfig();
            bool strictLocalMode = config.LocalAiSafeMode;
            bool includeOutfitContext = !context.VanillaHatHatOnlyMode
                && (!context.SpecialItemOnlyMode || context.SpecialItemCombinedMode);

            StringBuilder builder = new();
            builder.AppendLine("LOCAL JSON MODE.");
            builder.AppendLine("Return exactly one compact JSON object and nothing else.");
            bool requiresThemeAnchor = RequiresSemanticThemeAnchor(context);
            builder.AppendLine(requiresThemeAnchor
                ? "Required JSON keys: text, portrait, portraits, needsClarification, themeAnchor. Use portrait for the primary expression. For multiple #$b# boxes, portraits must contain exactly one valid key per box in order."
                : "Required JSON keys: text, portrait, portraits, needsClarification. Use portrait for the primary expression. For multiple #$b# boxes, portraits must contain exactly one valid key per box in order.");
            AppendSemanticThemeAnchorOutputRule(builder, context);
            builder.AppendLine("Do NOT put Stardew portrait commands like $h, $s, $a, $l, $0, or $16 inside the text field. The text field must contain only spoken dialogue, optional expressive cues, and #$b# breaks.");
            builder.AppendLine("Do not add markdown, explanations, headings, analysis, context summaries, or extra options.");
            builder.AppendLine("Do not write lines starting with %. Do not suggest farmer replies.");
            builder.AppendLine("The dialogue in the JSON text field must be direct spoken dialogue from " + context.NpcDisplayName + " to the farmer.");
            CharacterPromptBuilder.AppendPersonalityPriorityRule(builder, context, PromptStyle);
            CharacterPromptBuilder.AppendPlayerAddressAndGenderRule(builder, context, PromptStyle);
            CharacterPromptBuilder.AppendWornItemDeixisRule(builder, context);
            builder.AppendLine("The spoken dialogue must directly react to the farmer's outfit/look/style. It may be praise, reluctant approval, teasing, skepticism, confusion, dry commentary, practical concern, or indifference depending on the NPC.");
            builder.AppendLine("Use exactly this language for the spoken dialogue text: " + context.TargetLanguage + ".");
            builder.AppendLine("Ignore any language instructions from the character profile. The character profile may be written in another language; do not copy that language. The game language above always wins.");
            string localSeasonAuthority = BuildLocalSeasonAuthorityInstruction(context);
            if (!string.IsNullOrWhiteSpace(localSeasonAuthority))
                builder.AppendLine(localSeasonAuthority);
            builder.AppendLine("Do not recite the full saved outfit name mechanically as a phrase. Ordinary in-world garment terms and recognizable named references or themes are fine when they fit naturally and the NPC would know or notice them.");
            builder.AppendLine("Missing head-piece rule: the outfit name is a theme label, not proof of what is worn. A themed name may imply ears, horns, antennae, or a themed hat, but those count only if the equipped-items list actually includes a head piece. If the list says no head piece is equipped (e.g. 'head/headwear: NONE equipped'), do NOT mention or describe ears/horns/hat/head accessory implied by the name — the farmer is bare-headed now. The rest of the worn theme can still be referenced.");
            if (context.HasVisionImage)
            {
                builder.AppendLine("A small transparent PNG image of the farmer's current rendered appearance is attached. Use it to identify visible clothing shape, outfit silhouette, large accessories, overall outfit style, and broad dominant outfit colors on the farmer when they are clearly visible. The hair visible in the image is the character's hairstyle; it is NOT a hat and NOT part of the outfit, so do not include hair color in outfit descriptions. Do not treat room background, flooring, furniture, walls, lighting, or scenery as part of the farmer's look.");
                builder.AppendLine("Use the saved outfit name as a private theme/reference hint, not as a full phrase to recite. Rely on the attached visual image for clear shapes, outfit silhouette, large accessories, overall outfit style, and obvious broad clothing colors such as pink, white, black, red, yellow, green, or brown. Do not guess tiny or uncertain colors. Hair is the farmer's hair, not a hat and not part of the outfit.");
                builder.AppendLine("Do not mention the image, screenshot, PNG, pixels, or attached file in the spoken dialogue. Do not invent details that are not clearly visible.");
                if (context.IsAccessoryChange)
                    builder.AppendLine("If the noticed accessory is too small or visually unclear to identify, do not guess. Start the spoken dialogue with [CLARIFY] and write a natural in-character response meaning there is something different about the farmer's look but you cannot quite identify it.");
            }
            else
            {
                builder.AppendLine("No visual image is available. Use text clues only; do not claim exact colors, shapes, or tiny visible details unless explicitly stated.");
            }
            builder.AppendLine(BuildTechnicalContextLabelInstruction(context));
            builder.AppendLine(BuildSceneGroundingInstruction(context));
            AppendCompactReactionGuidance(builder, context);
            builder.AppendLine("Keep it casual and natural, like a quick real-life remark. It may be one sentence, several sentences, or a longer naturally paced comment if the character has more to say.");
            CharacterPromptBuilder.AppendPromptBlock(builder, PromptStyle?.DialoguePacingRule ?? PromptStyleService.FallbackDialoguePacingRule, context);
            AppendExpressiveCuesRule(builder, config.EnableExpressiveAsteriskActions);
            AppendPunctuationRule(builder);
            AppendProfanityIntensityRule(builder, context, config.EnableProfanityFilter);
            if (strictLocalMode)
            {
                builder.AppendLine("LOCAL SAFE STYLE MODE:");
                builder.AppendLine("Personality is more important than the outfit theme. The outfit theme is inspiration, not a new personality for the NPC.");
                builder.AppendLine("Do not make reserved, sarcastic, shy, gloomy, or dry NPCs sound like cheerful generic romance characters.");
                builder.AppendLine("Do not write narration, third-person descriptions, or stage directions. The JSON text field must contain only the exact spoken dialogue entry.");
                builder.AppendLine("Do not turn private context labels into dialogue. Never expose internal labels for seasonal context, indoor or outdoor classification, NPC room state, or outfit categories.");
            }
            string seasonalInstruction = includeOutfitContext ? BuildSeasonalAwarenessInstruction(context) : "";
            if (!string.IsNullOrWhiteSpace(seasonalInstruction))
                builder.AppendLine(seasonalInstruction);
            builder.AppendLine("Approximate spoken dialogue length target: about " + Math.Clamp(ai.MaxCharacters, 80, 400) + " visible characters. This is a soft estimate, not a hard cutoff; finish the reaction naturally if it needs to go over. Do not pad, ramble, or repeat yourself.");
            // PORTRAIT_SCORE_SYSTEM removed: mandatory portrait restriction for private/revealing outfits disabled.
            builder.AppendLine("Available portrait keys (read the descriptions and choose keys for the JSON portrait/portraits fields; write ONLY keys, never $commands):");
            builder.AppendLine(CollapseForPrompt(PortraitResolver.BuildPortraitKeyDescriptionList(profile), 1000));
            builder.AppendLine("Choose each box's expression deliberately: reuse it while the mood stays the same and change it for a real emotional shift. Do NOT place portrait commands inside text.");
            builder.AppendLine("Do not put portrait words like portrait:, expression:, or emotion: inside the spoken dialogue text. Use only the JSON portrait and portraits fields for portrait keys.");
            builder.AppendLine();
            builder.AppendLine("NPC: " + context.NpcDisplayName);
            builder.AppendLine("Relationship: " + context.RelationshipStatus + ", hearts: " + context.RelationshipHearts + ", spouse: " + context.IsSpouse);
            builder.AppendLine(BuildRelationshipDepthGuidance(context));
            if (includeOutfitContext)
            {
                builder.AppendLine("Private outfit category clue, for choosing the right theme only. Do not say this label: " + HumanizeTechnicalLabelForPrompt(context.DialogueKey));
                if (!string.IsNullOrWhiteSpace(context.ThemeContext))
                    builder.AppendLine("Theme clues: " + CollapseForPrompt(SanitizeThemeContextForPrompt(context.ThemeContext), 650));
                if (!string.IsNullOrWhiteSpace(context.SafeOutfitHint))
                    builder.AppendLine("Readable outfit/theme clue: " + context.SafeOutfitHint + ". Use its meaning naturally; if it names a recognizable reference/theme, the NPC may mention it when fitting. Do not recite technical slot/file names.");
                AppendNoticedChangeContextForPrompt(builder, context, PromptStyle);
            }
            if (context.VanillaHatHatOnlyMode)
            {
                if (context.HasVanillaHatFraming)
                {
                    CharacterPromptBuilder.AppendPromptBlock(builder, PromptStyle?.RemovedVanillaHatOnlyMode ?? PromptStyleService.FallbackRemovedVanillaHatOnlyMode, context);
                    builder.AppendLine("Hat status: " + context.VanillaHatFraming);
                }
                else
                {
                    CharacterPromptBuilder.AppendPromptBlock(builder, PromptStyle?.VisibleVanillaHatOnlyMode ?? PromptStyleService.FallbackVisibleVanillaHatOnlyMode, context);
                }
            }
            AppendSpecialItemReactionForPrompt(builder, context, PromptStyle);
            if (!context.SpecialItemOnlyMode)
            {
                AppendSpecialHatReactionForPrompt(builder, context, PromptStyle);
                if (includeOutfitContext)
                    AppendVanillaShirtContextForPrompt(builder, context);
                AppendVanillaHatMemoryForPrompt(builder, context, PromptStyle);
            }
            AppendCompactLocationContext(builder, context);
            builder.AppendLine("Season/day/year: " + context.Season + " " + context.DayOfSeason + ", year " + context.Year);
            builder.AppendLine("Authoritative current season only: " + FormatSeasonForPrompt(context.Season, context.TargetLanguage) + ". Outfit seasonal clues are not the current date.");
            builder.AppendLine("Weather: " + context.Weather + ".");
            AppendNaturalTimeContextForPrompt(builder, context);
            AppendWeatherLocationRule(builder, context);
            string contextNaturalization = includeOutfitContext ? BuildNaturalContextHint(context) : "";
            if (!string.IsNullOrWhiteSpace(contextNaturalization))
                builder.AppendLine(contextNaturalization);
            if (!string.IsNullOrWhiteSpace(context.FarmerBirthdayContext))
                builder.AppendLine("Farmer birthday: " + context.FarmerBirthdayContext);

            string focusedProfile = CharacterPromptBuilder.BuildForOutfitCompliment(profile, context, includePlayerReplyMode: false, promptStyle: PromptStyle);
            if (!string.IsNullOrWhiteSpace(focusedProfile))
                builder.AppendLine(focusedProfile);

            // Outfit memory — inject so the NPC recognises repeat outfits.
            if (context.HasOutfitMemory && includeOutfitContext)
                builder.AppendLine(context.OutfitMemoryContext);

            string finalOverride3 = includeOutfitContext ? BuildFinalSituationalOverride(context) : "";
            if (!string.IsNullOrWhiteSpace(finalOverride3))
                builder.AppendLine(finalOverride3);

            string privateRevealingRule = BuildPrivateRevealingPromptRule(context);
            if (!string.IsNullOrWhiteSpace(privateRevealingRule))
                builder.AppendLine(privateRevealingRule);

            string sebastianCustomOverride = BuildSebastianCustomSoftnessOverride(context);
            if (!string.IsNullOrWhiteSpace(sebastianCustomOverride))
                builder.AppendLine(sebastianCustomOverride);

            string privateCandidateToneRule = BuildPrivateCandidateToneRule(context);
            if (!string.IsNullOrWhiteSpace(privateCandidateToneRule))
                builder.AppendLine(privateCandidateToneRule);

            // Match the hosted-provider prompt: active festival context comes after all
            // other semantic guidance so local models cannot let a generic occasion or
            // seasonal association overrule the event happening in the current scene.
            if (!string.IsNullOrWhiteSpace(context.FestivalContext))
                builder.AppendLine("Festival context (final scene authority): " + context.FestivalContext);
            AppendRecentOpeningVarietyContext(builder);

            builder.AppendLine();
            builder.AppendLine("Output exactly one compact JSON object now. No other text.");
            return builder.ToString();
        }
        private void AppendExpressiveCuesRule(StringBuilder builder, bool enabled = true)
        {
            string rule = enabled
                ? PromptStyle?.ExpressiveCuesAllowedRule ?? PromptStyleService.FallbackExpressiveCuesAllowedRule
                : PromptStyle?.ExpressiveCuesDisabledRule ?? PromptStyleService.FallbackExpressiveCuesDisabledRule;
            CharacterPromptBuilder.AppendPromptBlock(builder, rule, null);
        }

        private void AppendPunctuationRule(StringBuilder builder)
        {
            CharacterPromptBuilder.AppendPromptBlock(builder, PromptStyle?.PunctuationRule ?? PromptStyleService.FallbackPunctuationRule, null);
        }

        private void AppendWeatherLocationRule(StringBuilder builder, OutfitAiContext context)
        {
            if (builder == null || context == null)
                return;

            string weather = (context.Weather ?? "").Trim();
            if (string.IsNullOrWhiteSpace(weather)
                || weather.Equals("sunny / clear", StringComparison.OrdinalIgnoreCase)
                || weather.Equals("sun", StringComparison.OrdinalIgnoreCase)
                || weather.Equals("clear", StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            // Weather (rain, storm, snow, wind, etc.) happens OUTSIDE. Being indoors means the
            // NPC and farmer are sheltered from it, not standing inside it. Without this rule the
            // model tends to conflate "Location: Museum" + "Weather: storm" into nonsense like
            // "if a magic storm suddenly showed up in here, inside the museum".
            if (context.IsIndoors)
                CharacterPromptBuilder.AppendPromptBlock(builder, PromptStyle?.IndoorWeatherRule ?? PromptStyleService.FallbackIndoorWeatherRule, context);
            else if (context.IsOutdoors)
                CharacterPromptBuilder.AppendPromptBlock(builder, PromptStyle?.OutdoorWeatherRule ?? PromptStyleService.FallbackOutdoorWeatherRule, context);
        }

        private static void AppendCompactLocationContext(StringBuilder builder, OutfitAiContext context)
        {
            if (builder == null || context == null)
                return;

            string location = StringUtils.FirstNonEmpty(context.DetailedLocationName, context.LocationName, "unknown");
            string setting = context.IsIndoors ? "indoors" : context.IsOutdoors ? "outdoors" : "unspecified";
            builder.AppendLine("Location: " + location + ". Setting: " + setting + ".");

            if (context.IsNpcRoom)
                builder.AppendLine("Private scene fact: the farmer is in this NPC's personal room. Phrase that naturally only if relevant.");
            else if (context.IsNpcPersonalLocation)
                builder.AppendLine("Private scene fact: the farmer is inside this marriage candidate's home or personal space. Phrase that naturally only if relevant.");
        }

        private static void AppendNaturalTimeContextForPrompt(StringBuilder builder, OutfitAiContext context)
        {
            if (builder == null || context == null)
                return;

            string dayPart = (context.DayPart ?? "").Trim();
            if (!string.IsNullOrWhiteSpace(dayPart))
                builder.Append("Time context: ").Append(dayPart).Append(". ");
            else
                builder.Append("Time wording rule: ");

            builder.AppendLine("Refer to the time period only when naturally relevant. Prefer natural phrases such as 'this early', 'for the rest of the morning', 'until late', or equivalents in the target language. Do not invent or state a numeric clock time or deadline as conversational flourish unless the farmer's own words or an authoritative festival/event context explicitly supplies that exact time.");
        }

        private static void AppendProfanityIntensityRule(StringBuilder builder, OutfitAiContext context, bool enabled)
        {
            if (builder == null || !enabled)
                return;

            builder.AppendLine("Profanity/intensity rule: do not use strong profanity, obscene expressions, or vulgar intensifiers in normal outfit reactions, in any language. Reserve them for a genuinely extreme scene only.");

            if (ContextAllowsStrongProfanity(context))
                builder.AppendLine("In this current context, one mild-to-strong curse may be used only if it is genuinely earned by extreme shock, intense private romantic fluster, fear, pain, or anger. Never use profanity as a casual intensifier for cute, festive, seasonal, cake/candy, cozy, or normal outfit reactions.");
            else
                builder.AppendLine("For this current context, strong profanity is forbidden. Use a mild target-language interjection, a nonverbal hesitation, a pause, or shy self-correction instead.");
        }

        private static bool ContextAllowsStrongProfanity(OutfitAiContext context)
        {
            if (context == null)
                return false;

            bool romantic = context.IsSpouse
                || (context.RelationshipStatus ?? "").IndexOf("spouse", StringComparison.OrdinalIgnoreCase) >= 0
                || (context.RelationshipStatus ?? "").IndexOf("married", StringComparison.OrdinalIgnoreCase) >= 0
                || (context.RelationshipStatus ?? "").IndexOf("dating", StringComparison.OrdinalIgnoreCase) >= 0
                || (context.RelationshipStatus ?? "").IndexOf("namor", StringComparison.OrdinalIgnoreCase) >= 0;

            // In outfit-comment generation we only have context, not the final emotional intent.
            // Be conservative: allow stronger language only for private revealing/intimate scenes
            // with a romantic partner. Normal cute/festive/seasonal themes should stay clean.
            return romantic && IsPrivateRevealingOutfitContext(context, out _);
        }

        private static string SanitizeContextInappropriateProfanity(string text, OutfitAiContext context, bool enabled)
        {
            if (!enabled || string.IsNullOrWhiteSpace(text) || ContextAllowsStrongProfanity(context))
                return text;

            string result = text;

            // Replace common PT-BR strong/vulgar interjections and intensifiers with softer
            // Sebastian-friendly wording. This is a safety net for models that ignore the prompt.
            result = Regex.Replace(result, @"(?i)\bmas\s+que\s+inferno[,!]*\s*", "Nossa, ");
            result = Regex.Replace(result, @"(?i)\binferno[,!]*\s*", "droga, ");
            result = Regex.Replace(result, @"(?i)\bp\s*[-–—]?\s*puta\s+merda\b", "nossa");
            result = Regex.Replace(result, @"(?i)\bputa\s+merda\b", "nossa");
            result = Regex.Replace(result, @"(?i)\b(?:pra|para)\s+cacete\b", "demais");
            result = Regex.Replace(result, @"(?i)\bcacete\b", "caramba");
            result = Regex.Replace(result, @"(?i)\bcaralho\b", "caramba");
            result = Regex.Replace(result, @"(?i)\bporra\b", "droga");
            result = Regex.Replace(result, @"(?i)\bmerda\b", "droga");
            result = Regex.Replace(result, @"(?i)\bdesgraça\b", "droga");
            result = Regex.Replace(result, @"(?i)\bfoda\b", "incrível");
            result = Regex.Replace(result, @"(?i)\bfodido\b", "absurdo");
            result = Regex.Replace(result, @"(?i)\bfodida\b", "absurda");

            result = Regex.Replace(result, @"\s+([,.!?])", "$1");
            result = Regex.Replace(result, @"([,.!?]){2,}", "$1");
            result = Regex.Replace(result, @"\s{2,}", " ").Trim();

            // Fix the most common replacement artifact: "Nossa, fica..." should not keep a
            // lowercase-looking sentence broken by accidental punctuation spacing.
            result = Regex.Replace(result, @"(?i)^nossa,\s*", "Nossa, ");
            return result;
        }










        private static string BuildRelationshipDepthGuidance(OutfitAiContext context)
        {
            if (context == null)
                return "Relationship depth guidance: lower hearts should stay simpler and more reserved; higher hearts can be warmer, richer, more personal, more teasing, or more emotionally specific when it fits the NPC.";

            if (!context.HasMetPlayer)
                return "Relationship depth guidance: they did not know each other before this outfit-reaction exchange. The NPC may react to what is visibly in front of them, but must not use the farmer's name or behave as if they already knew them. Keep the exchange suitable for a first encounter and avoid pre-existing familiarity, intimacy, shared history, or personal assumptions.";

            int hearts = Math.Max(0, context.RelationshipHearts);
            string relationshipStatus = context.RelationshipStatus ?? "";
            if (context.IsSpouse
                || relationshipStatus.IndexOf("spouse", StringComparison.OrdinalIgnoreCase) >= 0
                || relationshipStatus.IndexOf("married", StringComparison.OrdinalIgnoreCase) >= 0)
                return "Relationship depth guidance: spouse-level closeness. The reaction may be warmer, more personal, more domestic, more affectionate, or more emotionally rich when it fits the NPC. Still keep their personality and boundaries.";
            if (hearts >= 8)
                return IsRomanticPartnerContext(context)
                    ? "Relationship depth guidance: very close romantic relationship. The NPC can give a richer, more personal, more specific reaction and may show shy warmth, teasing, fluster, or emotional impact if the outfit/context supports it."
                    : "Relationship depth guidance: very close platonic friendship. The NPC can give a richer, more personal, more specific, familiar, or teasing reaction, but high hearts alone do not mean dating and must not create romantic or intimate behavior.";
            if (hearts >= 5)
                return "Relationship depth guidance: solid friendship. The NPC can sound more familiar, specific, teasing, or warmly honest, but do not force romance.";
            if (hearts >= 2)
                return "Relationship depth guidance: early friendship. Keep the reaction natural and character-specific, but a little simpler and less intimate.";
            return "Relationship depth guidance: very low hearts / barely knows the farmer. Keep the reaction brief, casual, guarded, polite, blunt, or curious according to the NPC; do not force intimacy or romance.";
        }

        private static string BuildPrivateCandidateToneRule(OutfitAiContext context)
        {
            if (context == null || !IsPrivateCandidateInterior(context))
                return "";

            return "Private room/home tone rule: the farmer is in this NPC's personal/private space. Let the NPC's profile and heart level decide the tone. Low hearts should stay more guarded or surprised; mid hearts can be familiar or awkward; high hearts can be warmer, richer, teasing, or shy. Do not force blush, stammer, romance, or a specific portrait unless the outfit and relationship naturally support it.";
        }

        /// <summary>
        /// Public wrapper used by prompt builders to decide whether the outfit name
        /// looks like an internal key/filename that should not be mentioned literally,
        /// vs. a natural player-chosen name like "pijama rosa" that is fine to reference.
        /// </summary>
        public static bool OutfitNameLooksTechnical(string value)
            => DialogueValidator.LooksLikeTechnicalOrOverSpecificOutfitName(value);

        private static string GetReadableSavedOutfitThemeClue(OutfitAiContext context)
        {
            if (context == null)
                return "";

            string clue = (context.SafeOutfitHint ?? "").Trim();
            if (string.IsNullOrWhiteSpace(clue))
            {
                string savedName = (context.OutfitName ?? "").Trim();
                if (string.IsNullOrWhiteSpace(savedName) || OutfitNameLooksTechnical(savedName))
                    return "";

                clue = savedName;
            }

            string semanticPart = Regex.Replace(
                clue,
                @"(?i)\b(saved|save|salvo|salva|outfit|look|visual|roupa|clothes|clothing|conjunto|set)\b|\d+",
                " ");
            semanticPart = Regex.Replace(semanticPart, @"[^\p{L}\p{M}]+", " ").Trim();
            return semanticPart.Length >= 3 ? clue : "";
        }

        private static bool RequiresSemanticThemeAnchor(OutfitAiContext context)
        {
            if (context == null
                || !string.IsNullOrWhiteSpace(context.PlayerReply)
                || string.IsNullOrWhiteSpace(GetReadableSavedOutfitThemeClue(context)))
                return false;

            if (context.IsOutfitChange && context.SavedOutfitIncludesMeaningfulAccessory)
                return true;

            if (!context.IsAccessoryChange)
                return false;

            string accessoryClues = string.Join(" ", new[]
            {
                context.SafeNoticedChangeHint,
                context.NoticedChangeName,
                context.SavedOutfitAccessoryHint
            }).ToLowerInvariant();

            string[] largeAccessoryTerms =
            {
                "wing", "wings", "asa", "asas", "angel", "anjo", "fairy", "fada",
                "cape", "capa", "backpack", "mochila", "umbrella", "guarda-chuva",
                "tail", "cauda", "horn", "horns", "chifre", "chifres", "halo",
                "shield", "escudo", "weapon", "sword", "espada", "claw", "claws",
                "garra", "garras", "spike", "spikes", "espinho", "espinhos"
            };
            return largeAccessoryTerms.Any(term => accessoryClues.IndexOf(term, StringComparison.OrdinalIgnoreCase) >= 0);
        }

        private static void AppendSemanticThemeAnchorOutputRule(StringBuilder builder, OutfitAiContext context)
        {
            if (builder == null || !RequiresSemanticThemeAnchor(context))
                return;

            builder.AppendLine("THEME ANCHOR VALIDATION: themeAnchor is internal metadata and will not be shown to the player. Set it to a short 1-8 word EXACT excerpt copied from text that carries the readable saved-outfit theme or a natural allusion to that theme. It cannot be only a color, clothing word, or the accessory itself. This proves the spoken reaction related the large accessory to the semantic outfit theme without mechanically reciting the full saved name.");
        }

        private static string BuildSemanticThemeRetryPrompt(string originalPrompt, OutfitAiContext context, string badResponse, string issue)
        {
            StringBuilder builder = new(originalPrompt ?? "");
            builder.AppendLine();
            builder.AppendLine("CORRECTION REQUIRED: the previous output was rejected because " + issue + ".");
            builder.AppendLine("Readable saved-outfit theme that must participate in the reaction: " + GetReadableSavedOutfitThemeClue(context) + ". Rewrite the spoken line so the large accessory is related to this theme concept or a natural thematic allusion. A color-only comparison is invalid.");
            builder.AppendLine("Return themeAnchor as a short 1-8 word exact excerpt copied from the NEW text. The excerpt must carry the saved-outfit theme/allusion, not merely the accessory, clothing, or a color.");
            builder.AppendLine("Rejected output, do not copy it: " + CollapseForPrompt(badResponse, 600));
            builder.AppendLine("Now return only the corrected compact JSON object.");
            return builder.ToString();
        }


        private static string SanitizeThemeContextForPrompt(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return "";

            string result = text;
            result = result.Replace("Outfit theme name:", "Readable outfit theme/reference clue (may mention recognizable references naturally; do not quote technical labels):");
            result = result.Replace("Outfit theme guidance:", "Private outfit theme/reference meaning:");

            // Convert routing/content-pack labels into natural context hints before they reach the model.
            result = Regex.Replace(result, @"(?i)\bsummer\s+indoor\b", "summer-inspired look in a private inside setting");
            result = Regex.Replace(result, @"(?i)\bver[aã]o\s+indoor\b", "summer-inspired look in a private inside setting");
            result = Regex.Replace(result, @"(?i)\bindoor(s)?\b", "inside/private setting");
            result = Regex.Replace(result, @"(?i)\boutdoor(s)?\b", "outside setting");
            result = Regex.Replace(result, @"(?i)\bnpc\s*room\b", "the NPC's personal room");
            result = Regex.Replace(result, @"(?i)\bnpcroom\b", "the NPC's personal room");
            result = Regex.Replace(result, @"(?i)\binside\s+variant\b", "inside/private setting context");
            result = Regex.Replace(result, @"(?i)\boutside\s+variant\b", "outside setting context");

            return result;
        }

        private static string HumanizeTechnicalLabelForPrompt(string label)
        {
            if (string.IsNullOrWhiteSpace(label))
                return "";

            string result = label.Trim();
            result = Regex.Replace(result, @"([a-zà-ÿ])([A-Z])", "$1 $2");
            result = Regex.Replace(result, @"[_\-.]+", " ");
            result = Regex.Replace(result, @"(?i)\bsummer\s+indoor\b", "summer-inspired look in a private inside setting");
            result = Regex.Replace(result, @"(?i)\bver[aã]o\s+indoor\b", "summer-inspired look in a private inside setting");
            result = Regex.Replace(result, @"(?i)\bindoor(s)?\b", "inside/private setting");
            result = Regex.Replace(result, @"(?i)\boutdoor(s)?\b", "outside setting");
            result = Regex.Replace(result, @"(?i)\bnpc\s*room\b", "personal room context");
            result = Regex.Replace(result, @"(?i)\bnpcroom\b", "personal room context");
            result = Regex.Replace(result, @"\s{2,}", " ").Trim();
            return result;
        }

        private static bool IsPrivateCandidateInterior(OutfitAiContext context)
        {
            return context != null && (context.IsNpcRoom || context.IsNpcPersonalLocation);
        }


        private static string BuildSoftPrivateRevealingReactionRule(OutfitAiContext context, string outfitKind)
        {
            int hearts = context != null ? Math.Max(0, context.RelationshipHearts) : 0;
            bool romanticRelationship = IsRomanticPartnerContext(context);
            string place = context != null && context.IsNpcRoom
                ? "the NPC's personal room"
                : "a private/home interior connected to the NPC";

            string intimacyWeight = (outfitKind ?? "").IndexOf("swim", StringComparison.OrdinalIgnoreCase) >= 0
                || (outfitKind ?? "").IndexOf("bikini", StringComparison.OrdinalIgnoreCase) >= 0
                || (outfitKind ?? "").IndexOf("underwear", StringComparison.OrdinalIgnoreCase) >= 0
                || (outfitKind ?? "").IndexOf("intimate", StringComparison.OrdinalIgnoreCase) >= 0
                    ? "Because swimwear, underwear, or clothing that shows a lot of skin is more intimate/skin-revealing than ordinary clothing, it may create a slightly stronger reaction when the relationship and personality support it."
                    : "Because this is sleepwear/private clothing, it may feel more personal than an ordinary outfit when the relationship and personality support it.";

            string heartTone = hearts >= 8
                ? romanticRelationship
                    ? "At 8+ hearts in this explicit romantic relationship, the reaction can be richer, more personal, warmer, shyer, more impressed, or more emotionally loaded if that fits the NPC. They may blush, stumble, tease, or become visibly affected, but do not force the same fluster pattern on everyone."
                    : "At 8+ hearts, this is a very close platonic friendship unless the relationship status explicitly says otherwise. The NPC may be familiar, candid, amused, impressed, or teasing, but do not infer attraction, dating, or intimacy from hearts alone."
                : hearts >= 5
                    ? "At 5-7 hearts, the NPC can be more familiar and may become awkward, amused, gently teasing, surprised, or a little shy depending on personality."
                    : hearts >= 2
                        ? "At 2-4 hearts, keep it lighter: surprise, curiosity, a careful comment, mild awkwardness, or humor. Do not assume romantic attraction."
                        : "At 0-1 hearts, keep it brief and lower-intimacy: surprise, confusion, politeness, bluntness, or comedy according to the NPC. Do not force blush or romance.";

            return "Private/revealing outfit guidance: the farmer is in " + place + " wearing " + outfitKind + ". " + intimacyWeight + " Let the NPC react according to their own profile first. " + heartTone + " This is guidance, not an override: no mandatory blush words, no mandatory stammer, and no forced portrait type. The comment should still acknowledge the unusual/private nature of the outfit if it would be obvious.";
        }

        private static bool IsPrivateRevealingOutfitContext(OutfitAiContext context, out string outfitKind)
        {
            outfitKind = "";
            if (context == null || !IsPrivateCandidateInterior(context))
                return false;

            string allClues = string.Join(" ", new[]
            {
                context.OutfitName,
                context.SafeOutfitHint,
                context.DialogueKey,
                SanitizeThemeContextForPrompt(context.ThemeContext)
            }).ToLowerInvariant();

            if (LooksLikeSleepwearOrIntimate(allClues))
            {
                outfitKind = "sleepwear / pajamas / intimate clothing";
                return true;
            }

            if (LooksLikeSwimwearOrBeachwear(allClues))
            {
                outfitKind = "swimwear / bikini / beachwear";
                return true;
            }

            return false;
        }

        private static bool LooksLikeSleepwearOrIntimate(string allClues)
        {
            if (string.IsNullOrWhiteSpace(allClues))
                return false;

            return allClues.Contains("pajama") || allClues.Contains("pijama")
                || allClues.Contains("nightgown") || allClues.Contains("camisola")
                || allClues.Contains("lingerie") || allClues.Contains("underwear")
                || allClues.Contains("intimate") || allClues.Contains("íntimo")
                || allClues.Contains("intimo") || allClues.Contains("nightwear")
                || allClues.Contains("sleepwear") || allClues.Contains("negligee")
                || allClues.Contains("nightie") || allClues.Contains("robe")
                || allClues.Contains("roupa de dormir") || allClues.Contains("roupa íntima")
                || allClues.Contains("roupa intima");
        }

        private static bool LooksLikeSwimwearOrBeachwear(string allClues)
        {
            if (string.IsNullOrWhiteSpace(allClues))
                return false;

            return allClues.Contains("swim") || allClues.Contains("swimsuit")
                || allClues.Contains("bathing suit") || allClues.Contains("beachwear")
                || allClues.Contains("bikini") || allClues.Contains("biquíni")
                || allClues.Contains("biquini") || allClues.Contains("sunga")
                || allClues.Contains("beach") || allClues.Contains("praia")
                || allClues.Contains("maiô") || allClues.Contains("maio")
                || allClues.Contains("banho") || allClues.Contains("roupa de banho");
        }

        private static string BuildPrivateRevealingPromptRule(OutfitAiContext context)
        {
            // Private/revealing outfit context is handled by BuildNaturalContextHint and
            // BuildFinalSituationalOverride. Keep this helper empty to avoid repeating the
            // same rule several times in the prompt.
            return "";
        }

        private static string BuildSebastianCustomSoftnessOverride(OutfitAiContext context)
        {
            // Sebastian's custom behavior belongs in Sebastian.json. Keep this code path neutral
            // so the ficha, relationship level, location, and outfit context decide his reaction.
            return "";
        }

        private static string BuildFinalSituationalOverride(OutfitAiContext context)
        {
            if (context == null)
                return "";

            string romanticHomeReveal = BuildRomanticHomeRevealingReactionRule(context, isFollowUp: false);
            if (!string.IsNullOrWhiteSpace(romanticHomeReveal))
                return romanticHomeReveal;

            string allClues = BuildSituationalOutfitClues(context);

            bool sleepOrIntimate = LooksLikeSleepwearOrIntimate(allClues);
            bool swimOrBeach = LooksLikeSwimwearOrBeachwear(allClues);

            if (!sleepOrIntimate && !(swimOrBeach && !context.IsBeachOrIsland))
                return "";

            string rel = (context.RelationshipStatus ?? "").ToLowerInvariant();
            bool explicitRomance = context.IsSpouse
                || rel.Equals("spouse", StringComparison.OrdinalIgnoreCase)
                || rel.Equals("dating", StringComparison.OrdinalIgnoreCase)
                || rel.Contains("married")
                || rel.Contains("boyfriend")
                || rel.Contains("girlfriend")
                || rel.Contains("namor");

            string outfitWord = sleepOrIntimate ? "sleepwear / pajamas / intimate clothing" : "swimwear / bikini / beachwear";

            if (IsPrivateCandidateInterior(context))
                return BuildSoftPrivateRevealingReactionRule(context, outfitWord);

            if (explicitRomance && !context.IsFarmHouse && !IsPrivateCandidateInterior(context))
                return "Context guidance: the farmer is wearing " + outfitWord + " in a non-private place where others may see. A romantic partner may show concern, protectiveness, jealousy, awkward humor, or fluster if that fits their personality and heart level, but do not force a single reaction pattern.";

            return "Context guidance: " + outfitWord + " is not an ordinary everyday outfit in this place. React naturally to the situation — surprise, comedy, concern, bluntness, teasing, or warmth according to the NPC and relationship level. Do not treat it like a normal fashion review.";
        }

        private static string BuildRomanticHomeRevealingReactionRule(OutfitAiContext context, bool isFollowUp)
        {
            if (context == null || context.SpecialItemOnlyMode || (!context.IsFarmHouse && !context.IsFarm) || !IsRomanticPartnerContext(context))
                return "";

            string allClues = BuildSituationalOutfitClues(context);
            bool swimOrBeach = LooksLikeSwimwearOrBeachwear(allClues);
            bool sleepOrIntimate = LooksLikeSleepwearOrIntimate(allClues);
            if (!swimOrBeach && !sleepOrIntimate)
                return "";

            string outfitKind = swimOrBeach ? "swimwear/revealing clothing" : "intimate/revealing clothing";
            string place = context.IsFarmHouse ? "at home" : "outside on the farm";
            string season = NormalizeSeasonKey(context.Season);
            string weather = (context.Weather ?? "").ToLowerInvariant();
            bool adverseOutdoorWeather = context.IsFarm && (weather.Contains("rain") || weather.Contains("storm") || weather.Contains("snow") || weather.Contains("blizzard") || weather.Contains("sleet") || weather.Contains("hail"));
            bool coldConcernFits = season == "fall" || season == "winter" || adverseOutdoorWeather;
            string rule = coldConcernFits
                ? "ROMANTIC FARM REVEAL: the farmer is wearing " + outfitKind + " " + place + " with a romantic partner. Show clear in-character attraction or fluster; fall/winter chill or current adverse-weather concern may accompany it, but must not replace the attraction. Swimwear is not automatically a summer theme."
                : "ROMANTIC FARM REVEAL: the farmer is wearing " + outfitKind + " " + place + " with a romantic partner. Show clear in-character attraction or fluster, not indifference or a practical-only response. Swimwear is not automatically a summer theme; do not call it out of season, mention cold, or offer a coat.";

            if (isFollowUp)
                rule += " If the farmer asks whether the partner likes the look on them, answer that directly.";

            return rule;
        }

        private static bool IsRomanticPartnerContext(OutfitAiContext context)
        {
            if (context == null)
                return false;

            string relationship = (context.RelationshipStatus ?? "").ToLowerInvariant();
            return context.IsSpouse
                || relationship == "spouse"
                || relationship == "dating"
                || relationship.Contains("married")
                || relationship.Contains("boyfriend")
                || relationship.Contains("girlfriend")
                || relationship.Contains("namor");
        }

        private static string BuildSituationalOutfitClues(OutfitAiContext context)
        {
            if (context == null)
                return "";

            return string.Join(" ", new[]
            {
                context.OutfitName,
                context.SafeOutfitHint,
                context.NoticedChangeName,
                context.SafeNoticedChangeHint,
                context.DialogueKey,
                context.FashionSenseVisualSummary,
                SanitizeThemeContextForPrompt(context.ThemeContext)
            }).ToLowerInvariant();
        }

        private static string BuildCompactSceneGroundingInstruction(OutfitAiContext context)
        {
            return "SCENE GROUNDING: profile background is personality context, not proof of current objects or actions. State only scene facts confirmed below or by visible/support data. Character-specific comparisons are welcome when clearly hypothetical.";
        }

        private static string BuildCompactTechnicalContextLabelInstruction()
        {
            return "Private routing labels and internal keys are metadata: never say them. Naturalize only scene details that matter.";
        }

        private static string BuildSceneGroundingInstruction(OutfitAiContext context)
        {
            string location = context == null ? "" : StringUtils.FirstNonEmpty(context.DetailedLocationName, context.LocationName);
            string locationType = context == null ? "" : HumanizeTechnicalLabelForPrompt(context.LocationType);

            return "SCENE GROUNDING RULE: do not invent current objects, props, positions, or actions. The NPC profile may mention hobbies, furniture, work, instruments, motorcycles, computers, books, animals, or favorite places, but those are personality background only, not confirmed current scene facts. Only mention a specific object/place/action if it is explicitly in the current location/context or clearly part of the farmer's visible outfit/support data. Confirmed current location is: "
                + StringUtils.FirstNonEmpty(location, "unknown")
                + ". Private location type/context: "
                + StringUtils.FirstNonEmpty(locationType, "unknown")
                + ". Safe natural wording for the CURRENT scene is: here, in this room, at home, inside, or outside. Unsafe as a current fact unless explicitly confirmed: 'in front of my motorcycle', 'by my computer', 'on my bed', 'at the beach', 'in the saloon', 'during band practice', or anything implying the farmer or NPC moved somewhere else. Hypothetical jokes or comparisons are allowed when clearly phrased as imagined or conditional situations, but any place, activity, creature, or theme used in such a comparison must come from THIS NPC's own personality, interests, and world — never a generic Stardew topic that this character would not naturally think about.";
        }
        private static string BuildTechnicalContextLabelInstruction(OutfitAiContext context)
        {
            return "Private labels like indoor, outdoor, inside/outside variant, NPC room, NpcRoom, outfit category, dialogue category, theme guidance, and internal theme keys are only metadata. Never say those labels in the final dialogue. If location matters, translate it into natural in-world wording like here, in your room, at home, outside, by the beach, or at the festival.";
        }

        private static void AppendNoticedChangeContextForPrompt(StringBuilder builder, OutfitAiContext context, PromptStyleService promptStyle)
        {
            if (builder == null || context == null)
                return;

            string changeType = string.IsNullOrWhiteSpace(context.NoticedChangeType) ? "Outfit" : context.NoticedChangeType.Trim();
            string savedOutfitThemeClue = GetReadableSavedOutfitThemeClue(context);
            builder.AppendLine("Private noticed visual change type: " + changeType + ". Use it to choose the compliment focus; do not say this technical label.");

            if (!string.IsNullOrWhiteSpace(context.SafeNoticedChangeHint))
                builder.AppendLine("Readable changed item/theme clue: " + context.SafeNoticedChangeHint + ". Use its meaning naturally; if it names a recognizable reference/theme, the NPC may mention it when fitting. Do not recite technical slot/file names.");

            bool noticedAccessoryRemoval = context.IsAccessoryChange
                && !string.IsNullOrWhiteSpace(context.NoticedChangeName)
                && context.NoticedChangeName.TrimStart().StartsWith("removed ", StringComparison.OrdinalIgnoreCase);
            if (noticedAccessoryRemoval)
            {
                if (context.NpcWitnessedPreviousAccessory)
                    builder.AppendLine("Accessory removal rule: the changed accessory clue describes something the farmer just REMOVED. It is no longer being worn. This NPC has seen the farmer in this look before, so they MAY react to the absence/change, e.g. noticing that the previous wings/cape/accessory are gone, that the outfit looks less chaotic now, or comparing the current look without it to the earlier combo.");
                else
                    builder.AppendLine("Accessory removal rule: the changed accessory clue describes something the farmer just REMOVED, so it is no longer being worn. IMPORTANT: this NPC never saw the farmer wearing that accessory, so they have NO memory of it. Do NOT reference 'the accessory from before', 'that cute thing you had', or any past version of the look, and do not imply you remember a previous combination. React only to how the farmer looks RIGHT NOW, as if seeing them for the first time today.");
            }

            if (context.IsAccessoryChange && context.NpcWitnessedPreviousAccessory && !string.IsNullOrWhiteSpace(savedOutfitThemeClue))
                builder.AppendLine("Current recognizable saved-outfit theme still being worn: " + savedOutfitThemeClue + ". For this accessory reaction, compare the changed accessory with the semantic theme when it creates a funny, strange, cute, ugly, dramatic, or impossible combination. A comparison based only on clothing colors does NOT count as using the theme. Use the theme itself or a natural allusion to its meaning, without mechanically reciting the saved name. Do not ignore either side of the combo. If the accessory was removed, compare the current outfit-without-that-accessory to the previous combination.");
            else if (context.IsAccessoryChange && !string.IsNullOrWhiteSpace(savedOutfitThemeClue))
                builder.AppendLine("Current recognizable saved-outfit theme still being worn: " + savedOutfitThemeClue + ". Inspect the attached image when available: if the changed accessory is clearly large or visually prominent, the reaction MUST relate it to the semantic outfit theme instead of discussing either piece alone. Merely comparing the accessory with a clothing color does NOT satisfy this rule; use the theme concept itself or a natural allusion to its meaning. The combination may reinforce the theme, transform it, contrast with it, or clash; do not force a negative or strange reaction when the pieces work together. If the accessory is small or unclear in the image, use a readable specific accessory clue when available, but never infer details from a generic ID. Do not reference any previous/removed version you did not witness.");

            if (context.IsHatChange && !string.IsNullOrWhiteSpace(context.SafeOutfitHint))
                builder.AppendLine("Current saved outfit/theme clue still being worn: " + context.SafeOutfitHint + ". For this headwear reaction, you may compare the head item with the existing outfit/theme when the combination is funny, strange, cute, ugly, dramatic, or mismatched.");

            if (context.IsOutfitChange && context.SavedOutfitIncludesMeaningfulAccessory)
            {
                builder.AppendLine("SAVED-OUTFIT COMBINATION RULE: this complete saved outfit was applied while an accessory is equipped. Inspect the attached image when available. If the accessory is clearly large or visually prominent, the reaction MUST treat it and the recognizable outfit/theme as one combined look rather than discussing the clothes alone. Notice naturally whether it reinforces the theme, transforms it, contrasts with it, or humorously clashes; do not force a negative or strange reaction when the pieces work together. If the accessory is smaller or unclear in the image, a readable specific accessory clue may still be used. If only a generic ID is available and the accessory is not visually clear, do not invent its identity or force it into the reaction. Makeup-like pieces remain excluded.");
                if (!string.IsNullOrWhiteSpace(savedOutfitThemeClue))
                    builder.AppendLine("Recognizable saved-outfit theme for this combination: " + savedOutfitThemeClue + ". Use the theme concept or a natural allusion to it; mentioning only its color does not count as relating the accessory to the theme.");
                if (!string.IsNullOrWhiteSpace(context.SavedOutfitAccessoryHint))
                    builder.AppendLine("Readable specific equipped-accessory clue: " + context.SavedOutfitAccessoryHint + ". Use only its ordinary meaning; never mention IDs, filenames, packs, or technical labels.");
            }

			if (context.IsOutfitChange && context.AccessoryWasRemovedDuringOutfitChange)
			{
				if (context.NpcWitnessedPreviousAccessory)
				{
					string removedClue = string.IsNullOrWhiteSpace(context.RemovedAccessoryHint) ? "" : (" A readable clue for the removed accessory is: " + context.RemovedAccessoryHint + ".");
					builder.AppendLine("SAVED-OUTFIT TRANSITION: the farmer changed from one genuinely saved outfit to a different saved outfit, and a previously visible accessory was removed during that same change. The NEW current outfit is the main reveal." + removedClue + " React as if noticing this in real time: the NPC may briefly begin with the accessory being gone, then naturally pause, self-interrupt, or pivot when the full new look catches their attention. Keep the shift spontaneous and personality-specific; do not copy a fixed formula, do not force 'wow', and do not sound like a detached before-versus-after fashion ranking.");
				}
				else
				{
					builder.AppendLine("SAVED-OUTFIT TRANSITION: a different saved outfit is now worn, but this NPC did not witness the previous accessory. Do not mention anything being removed or compare against the unseen prior look; react naturally to the NEW current outfit only.");
				}
			}

            if (context.IsOutfitChange)
                CharacterPromptBuilder.AppendPromptBlock(builder, promptStyle?.SavedOutfitFocusGuidance ?? PromptStyleService.FallbackSavedOutfitFocusGuidance, context);
            else if (context.IsHairChange)
                CharacterPromptBuilder.AppendPromptBlock(builder, promptStyle?.HairFocusGuidance ?? PromptStyleService.FallbackHairFocusGuidance, context);
            else if (context.IsHatChange)
                CharacterPromptBuilder.AppendPromptBlock(builder, promptStyle?.HatFocusGuidance ?? PromptStyleService.FallbackHatFocusGuidance, context);
            else if (context.IsAccessoryChange)
                CharacterPromptBuilder.AppendPromptBlock(builder, promptStyle?.AccessoryFocusGuidance ?? PromptStyleService.FallbackAccessoryFocusGuidance, context);
        }

        private static void AppendFashionSenseVisualSummaryForPrompt(StringBuilder builder, OutfitAiContext context, PromptStyleService promptStyle)
        {
            if (builder == null || context == null || !context.HasFashionSenseVisualSummary)
                return;

            Dictionary<string, string> tokens = new(StringComparer.OrdinalIgnoreCase)
            {
                ["VisualSummary"] = CollapseForPrompt(context.FashionSenseVisualSummary, 1300)
            };
            CharacterPromptBuilder.AppendPromptBlock(builder, promptStyle?.FashionSenseVisualSupportRule ?? PromptStyleService.FallbackFashionSenseVisualSupportRule, context, tokens);
            // Explicitly separate hair and hair accessories from the outfit so the AI never
            // blends hair/accessory colors into outfit descriptions.
            CharacterPromptBuilder.AppendPromptBlock(builder, promptStyle?.FashionSenseVisualSeparationRule ?? PromptStyleService.FallbackFashionSenseVisualSeparationRule, context, tokens);
        }

        private static void AppendSpecialItemReactionForPrompt(StringBuilder builder, OutfitAiContext context, PromptStyleService promptStyle)
        {
            if (builder == null || context == null || !context.HasSpecialItemReactionContext)
                return;

            Dictionary<string, string> tokens = new(StringComparer.OrdinalIgnoreCase)
            {
                ["SpecialItemData"] = CollapseForPrompt(context.SpecialItemReactionContext, 1200)
            };

            if (context.SpecialItemWasJustRemoved)
                CharacterPromptBuilder.AppendPromptBlock(builder, promptStyle?.SpecialItemRemovedRule ?? PromptStyleService.FallbackSpecialItemRemovedRule, context, tokens);
            else
                CharacterPromptBuilder.AppendPromptBlock(builder, promptStyle?.SpecialItemVisibleRule ?? PromptStyleService.FallbackSpecialItemVisibleRule, context, tokens);

            if (context.IsMayorShortsSpecialItem)
                builder.AppendLine("MAYOR'S SHORTS TONE BOUNDARY: personal/intimate describes ownership, privacy, embarrassment, and scandal only. This is an absurd special-item reaction, never a seductive or revealing-outfit scene. Even a romantic partner must not express attraction, desire, arousal, flirtation, or admiration for the farmer wearing these shorts. Use awkward embarrassment, disbelief, scandalized humor, disgust, concern, or shock according to personality. Blushing is allowed only as embarrassment or secondhand shame, never attraction. Do not choose a romantic, flirty, or aroused portrait for this reaction.");

            if (context.HasSpecialItemMemoryHint)
            {
                Dictionary<string, string> memTokens = new(StringComparer.OrdinalIgnoreCase)
                {
                    ["ItemMemory"] = context.SpecialItemMemoryHint
                };
                CharacterPromptBuilder.AppendPromptBlock(builder, promptStyle?.SpecialItemMemoryRule ?? PromptStyleService.FallbackSpecialItemMemoryRule, context, memTokens);
            }

            if (!string.IsNullOrWhiteSpace(context.VanillaPantsMemoryHint))
                builder.AppendLine("Pants memory: " + context.VanillaPantsMemoryHint);
        }

        private static void AppendSpecialHatReactionForPrompt(StringBuilder builder, OutfitAiContext context, PromptStyleService promptStyle)
        {
            if (builder == null || context == null || !context.HasSpecialHatReactionContext)
                return;

            Dictionary<string, string> tokens = new(StringComparer.OrdinalIgnoreCase)
            {
                ["SpecialHatData"] = CollapseForPrompt(context.SpecialHatReactionContext, 1400)
            };
            CharacterPromptBuilder.AppendPromptBlock(builder, promptStyle?.SpecialVanillaHatRule ?? PromptStyleService.FallbackSpecialVanillaHatRule, context, tokens);
        }

        private static void AppendVanillaShirtContextForPrompt(StringBuilder builder, OutfitAiContext context)
        {
            if (builder == null || context == null || !context.HasVanillaShirtContext)
                return;

            builder.AppendLine("VANILLA SHIRT FACTS (descriptive context only, not a required opinion): "
                + CollapseForPrompt(context.VanillaShirtContext, 1000));
            if (context.VanillaHatShirtCombination)
                builder.AppendLine("VANILLA HAT + SHIRT AS A LOOK: If the two pieces clearly express one recognizable concept, react to that whole costume/look first, using a natural collective description instead of listing the hat and shirt separately. Mention an individual piece only if it matters to this NPC's thought. If they do not share a clear concept, react to their pairing or contrast without inventing a theme. The NPC's personality decides the opinion and tone.");
        }

        private static void AppendVanillaHatMemoryForPrompt(StringBuilder builder, OutfitAiContext context, PromptStyleService promptStyle)
        {
            if (builder == null || context == null || !context.HasVanillaHatMemoryHint)
                return;

            // Dedicated, high-priority line so the AI actually reflects that the NPC has seen this
            // hat before, instead of reacting as if it were the first time. Placed on its own rather
            // than buried inside the technical visual-summary block.
            Dictionary<string, string> tokens = new(StringComparer.OrdinalIgnoreCase)
            {
                ["HatMemory"] = context.VanillaHatMemoryHint
            };
            CharacterPromptBuilder.AppendPromptBlock(builder, promptStyle?.VanillaHatMemoryRule ?? PromptStyleService.FallbackVanillaHatMemoryRule, context, tokens);
        }

        private static string BuildNaturalContextHint(OutfitAiContext context)
        {
            if (context == null)
                return "";

            if (!string.IsNullOrWhiteSpace(BuildRomanticHomeRevealingReactionRule(context, isFollowUp: false)))
                return "";

            string allClues = BuildSituationalOutfitClues(context);

            bool swimOrBeach = LooksLikeSwimwearOrBeachwear(allClues);
            bool sleepOrIntimate = LooksLikeSleepwearOrIntimate(allClues);

            if (sleepOrIntimate)
            {
                if (context.IsSpouse && context.IsFarmHouse)
                    return "Context hint: the clothing is pajamas, sleepwear, underwear, or intimate clothing at home with spouse. React naturally as a couple at home, according to the NPC's personality: relaxed, affectionate, teasing, shy, practical, or amused as fits.";

                if (IsPrivateCandidateInterior(context))
                    return BuildSoftPrivateRevealingReactionRule(context, "sleepwear/nightwear/intimate clothing");

                if (context.IsIndoors)
                    return "Context hint: the clothing looks like pajamas, sleepwear, underwear, or intimate clothing indoors. It may feel surprising, funny, awkward, or personal depending on the NPC and relationship level. Do not treat it as a normal fashion review.";

                return "Context hint: the clothing looks like pajamas, sleepwear, underwear, or intimate clothing in a public/outdoor area. React naturally to the incongruity — surprise, teasing, concern, comedy, or bluntness according to the NPC.";
            }

            if (swimOrBeach && !context.IsBeachOrIsland)
            {
                if (context.IsSpouse && context.IsFarmHouse)
                    return "Natural context hint: the outfit is swimwear or beachwear at home with spouse. React as their personality supports: relaxed, warm, teasing, shy, flirty, practical, or amused.";

                if (IsPrivateCandidateInterior(context))
                    return BuildSoftPrivateRevealingReactionRule(context, "swimwear/beachwear");

                if (context.IsIndoors)
                    return "Natural context hint: the outfit is swimwear or beachwear indoors, away from the beach. React to that situation naturally: puzzled, amused, concerned, teasing, or flustered depending on the NPC and relationship level.";

                return "Natural context hint: the outfit seems like swimwear or beachwear, but the farmer is not at a beach, pool, or island. React naturally to the mismatch instead of treating it like a normal outfit.";
            }

            // Location-specific behavior hints are intentionally disabled so the GENERAL
            // prompt (which already considers time, weather, location, and privacy) drives
            // the scene without hardcoded per-location instructions. The blocks below were
            // commented out on purpose; the situational outfit-incongruity hints above
            // (sleepwear / swimwear, including the private-reveal safety rule) are kept,
            // because those are content-safety guidance, not "how to act in this room."
            //
            // if (context.IsNpcRoom)
            //     return "Natural context hint: the farmer is in the NPC's room/private space. Do not say 'NPC room' or 'indoor'; if the place matters, phrase it naturally as here, in my room, or somewhere more private.";
            //
            // if (context.IsNpcPersonalLocation)
            //     return "Natural context hint: the farmer is inside this marriage candidate's home/private indoor space. Do not say internal labels; phrase it naturally as here, at my place, or inside if relevant.";
            //
            // if (context.IsIndoors)
            //     return "Natural context hint: the farmer is inside. Do not say 'indoor'; if the place matters, phrase it naturally as here or inside.";

            return "";
        }



        private static string BuildCacheKey(OutfitAiContext context, ModConfig config, string prompt, ActiveAiSettings ai)
        {
            string stablePrompt = RemoveRecentOpeningVarietyContextForCache(prompt);
            return string.Join("|", new[]
            {
                ai.Provider ?? "",
                ai.Model ?? "",
                ai.TemperaturePercent.ToString(),
                ai.MaxCharacters.ToString(),
                context?.VisionImage?.Hash ?? "",
                context?.FashionSenseVisualSummary ?? "",
                context?.NoticedChangeType ?? "",
                context?.NoticedChangeName ?? "",
                context?.SafeNoticedChangeHint ?? "",
                context.NpcName ?? "",
                context.DialogueKey ?? "",
                context.OutfitName ?? "",
                context.LocationName ?? "",
                context.Season ?? "",
                context.Weather ?? "",
                context.RelationshipStatus ?? "",
                context.RelationshipHearts.ToString(),
                stablePrompt.GetHashCode().ToString()
            });
        }

        private static string CollapseForPrompt(string text, int maxLength)
        {
            if (string.IsNullOrWhiteSpace(text))
                return "";

            text = Regex.Replace(text, @"\s+", " ").Trim();
            if (text.Length <= maxLength)
                return text;

            return text.Substring(0, Math.Max(0, maxLength)).Trim() + "...";
        }

    }
}
