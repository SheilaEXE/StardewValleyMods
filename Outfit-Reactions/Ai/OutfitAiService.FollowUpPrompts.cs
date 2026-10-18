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
        private string BuildFollowUpRetryPrompt(CharacterAiProfile profile, OutfitAiContext context, ActiveAiSettings ai, string npcCompliment, string playerReply, string badResponse, string issue)
        {
            bool localMode = ActiveAiSettingsResolver.IsLocal(ai);
            StringBuilder builder = new();

            if (localMode)
                builder.AppendLine("Your previous follow-up answer was rejected: " + issue + ".");

            builder.AppendLine("Return exactly one compact JSON object only. No markdown, no explanation, no narration.");
            builder.AppendLine("Required JSON keys: text, portrait, portraits, needsClarification. Use portrait for the primary expression. For multiple #$b# boxes, portraits must contain exactly one valid key per box in order.");
            builder.AppendLine("Do NOT put Stardew portrait commands inside the text field. The text field must contain only spoken dialogue, optional expressive cues, and #$b# breaks.");

            builder.AppendLine("The dialogue entry must be a direct spoken response from " + context.NpcDisplayName + " to the farmer's reply.");
            CharacterPromptBuilder.AppendPersonalityPriorityRule(builder, context, PromptStyle);
            CharacterPromptBuilder.AppendPlayerAddressAndGenderRule(builder, context, PromptStyle);
            CharacterPromptBuilder.AppendWornItemDeixisRule(builder, context);
            AppendExpressiveCuesRule(builder, (getConfig?.Invoke() ?? new ModConfig()).EnableExpressiveAsteriskActions);
            AppendPunctuationRule(builder);
            builder.AppendLine("Language: " + context.TargetLanguage + ". Approximate length target: about " + Math.Clamp(ai.MaxCharacters, 80, 400) + " visible characters. This is a soft estimate; finish the reply naturally even if it goes over.");
            builder.AppendLine("Do not ignore the farmer's reply. Do not continue the original compliment as if the farmer never answered.");
            CharacterPromptBuilder.AppendPromptBlock(builder, PromptStyle?.DialoguePacingRule ?? PromptStyleService.FallbackDialoguePacingRule, context);

            string portraitKeys = profile?.Portraits != null && profile.Portraits.Count > 0
                ? string.Join(", ", profile.Portraits.Keys) : "";
            builder.AppendLine("Portrait keys and descriptions: " + CollapseForPrompt(PortraitResolver.BuildPortraitKeyDescriptionList(profile), 800));
            builder.AppendLine("Portrait must be one of these exact keys, or empty string if unsure: " + portraitKeys);
            builder.AppendLine("Choose each box's expression deliberately: reuse it while the mood stays the same and change it for a real emotional shift, never merely for variety.");

            builder.AppendLine("NPC: " + context.NpcDisplayName);
            builder.AppendLine("Relationship: " + context.RelationshipStatus + ", hearts: " + context.RelationshipHearts + ", spouse: " + context.IsSpouse);
            builder.AppendLine(BuildRelationshipDepthGuidance(context));
            AppendFollowUpSituationalContext(builder, context);
            AppendNaturalTimeContextForPrompt(builder, context);
            if (!string.IsNullOrWhiteSpace(context?.ConversationTranscript))
            {
                builder.AppendLine("Full conversation so far for this outfit reaction (oldest first, last line is the farmer's newest reply):");
                builder.AppendLine(CollapseForPrompt(context.ConversationTranscript, 2500));
                builder.AppendLine("Conversation continuity rule: read the WHOLE conversation above before answering. If the farmer's newest line refers back to something said earlier through a pronoun, an implied subject, a follow-up question, a callback, or an equivalent of 'and you?', answer that intended subject using the full conversation for context. Do not change the subject or restart on a different topic unless the farmer's newest line clearly does so.");
            }
            else
            {
                builder.AppendLine("Original NPC compliment/reaction: " + CollapseForPrompt(DialogueValidator.StripDialogueMarkup(npcCompliment), 350));
                builder.AppendLine("Farmer's reply: " + CollapseForPrompt(DialogueValidator.StripDialogueMarkup(playerReply), 350));
            }
            builder.AppendLine("Named-NPC-in-reply rule: if the farmer's reply names or clearly refers to another NPC, check the profile's personality/relationship rules for how this NPC reacts to that — including any jealousy, possessiveness, rivalry, disapproval, or protective rules — and apply them now if the situation fits. Do not default to a neutral, approving, or purely informative reaction about the other NPC when the profile defines a stronger, more specific behavior for this kind of moment.");
            builder.AppendLine("Permission-trap rule: if the farmer's reply is asking permission, announcing they are leaving to be with another romanceable NPC, or seeking approval (e.g. 'is that okay?', 'so I'll go see him/her', 'you don't mind?'), and the profile defines a jealousy/possessiveness/keep-them rule, the NPC must NOT grant permission, give a blessing, wish them fun, or say a calm version of 'sure, go ahead / do what you want / I won't stop you'. That polite, accepting, mature send-off is exactly the wrong answer here. Follow the profile's jealousy rule instead: stay reluctant, bothered, or vulnerable, and actively try to keep the farmer's attention or gently push back, in character. A casual or permission-seeking tone from the farmer does NOT release the NPC from this — do not treat 'is that okay?' as something to approve.");
            builder.AppendLine("Bad previous answer, do not copy it:");
            builder.AppendLine(CollapseForPrompt(badResponse, 400));
            builder.AppendLine("Now output only valid JSON:");
            return builder.ToString();
        }

        private string BuildPlayerReplyFollowUpPrompt(CharacterAiProfile profile, OutfitAiContext context, ActiveAiSettings ai, string npcCompliment, string playerReply)
        {
            string portraitKeys = profile?.Portraits != null && profile.Portraits.Count > 0
                ? string.Join(", ", profile.Portraits.Keys)
                : "";
            string portraitCommands = PortraitResolver.BuildPortraitCommandList(profile);

            ModConfig config = getConfig?.Invoke() ?? new ModConfig();
            bool localMode = ActiveAiSettingsResolver.IsLocal(ai);
            bool strictLocalMode = localMode && config.LocalAiSafeMode;

            StringBuilder builder = new();
            builder.AppendLine("You are generating a follow-up dialogue for Stardew Valley after the farmer replies to an outfit visual reaction.");
            builder.AppendLine(localMode ? "LOCAL JSON MODE." : "Return exactly one compact JSON object and nothing else.");
            builder.AppendLine("Required JSON keys: text, portrait, portraits, needsClarification. Use portrait for the primary expression. For multiple #$b# boxes, portraits must contain exactly one valid key per box in order.");
            builder.AppendLine("Do NOT put Stardew portrait commands like $h, $s, $a, $l, $0, or $16 inside the text field. The text field must contain only spoken dialogue, optional expressive cues, and #$b# breaks.");
            builder.AppendLine("The dialogue must be direct spoken dialogue from " + context.NpcDisplayName + " to the farmer.");
            CharacterPromptBuilder.AppendPersonalityPriorityRule(builder, context, PromptStyle);
            CharacterPromptBuilder.AppendPlayerAddressAndGenderRule(builder, context, PromptStyle);
            CharacterPromptBuilder.AppendWornItemDeixisRule(builder, context);
            builder.AppendLine("It must directly react to the farmer's reply. Do not ignore the farmer's reply.");
            builder.AppendLine("Do not write the farmer's line. Do not write narration, stage directions, explanations, markdown, or headings.");
            AppendExpressiveCuesRule(builder, config.EnableExpressiveAsteriskActions);
            AppendPunctuationRule(builder);
            AppendProfanityIntensityRule(builder, context, config.EnableProfanityFilter);
            builder.AppendLine("Use exactly this language for the spoken dialogue text: " + context.TargetLanguage + ".");
            builder.AppendLine("Ignore any language instructions inside NPC CHARACTERISTICS. The current game language above always wins.");
            CharacterPromptBuilder.AppendPromptBlock(builder, PromptStyle?.DialoguePacingRule ?? PromptStyleService.FallbackDialoguePacingRule, context);
            builder.AppendLine("Approximate final dialogue length target: about " + Math.Clamp(ai.MaxCharacters, 80, 400) + " visible characters. Treat it as a soft estimate, not a hard cutoff, and finish the reply naturally if it goes over. React directly to the farmer's reply without padding or repetition.");
            builder.AppendLine("Keep it casual and natural, like a real reply. It may be brief, use several sentences, or use longer natural phrasing if the farmer's reply gives him something to react to.");
            builder.AppendLine("Do not recite the full saved outfit name mechanically as a phrase. Ordinary in-world garment terms and recognizable named references or themes are fine when they fit naturally and the NPC would know or notice them.");
            builder.AppendLine("Missing head-piece rule: the outfit name is a theme label, not proof of what is worn. A themed name may imply ears, horns, antennae, or a themed hat, but those count only if the equipped-items list actually includes a head piece. If the list says no head piece is equipped (e.g. 'head/headwear: NONE equipped'), do NOT mention or describe ears/horns/hat/head accessory implied by the name — the farmer is bare-headed now. The rest of the worn theme can still be referenced.");
            if (context.IsOutfitChange)
                builder.AppendLine("Whole saved outfit focus rule: react to the complete outfit/look first. Do not focus on a generic hat/headwear/head-slot item, hair bow, tiara, hair, or hair color unless that is clearly the named theme of the outfit. Generic IDs like pack0005 hat 2/3 are not meaningful in-world content.");
            if (context.HasVisionImage)
            {
                builder.AppendLine("A small transparent PNG image of the farmer's current rendered appearance is attached. Use it as support for visible clothing shape, outfit silhouette, large accessories, overall outfit style, and broad dominant outfit colors when they are clearly visible. The hair visible in the image is the player character's hairstyle, not a hat and not part of the outfit palette; do not mention hair color when describing the outfit.");
                builder.AppendLine("Use the saved outfit name as a private theme/reference hint, not as a full phrase to recite. You may receive TWO images of the same farmer: a FRONT view and a BACK view. Use them to recognize the SHAPE/silhouette/style of items (wings, capes, bows, large accessories) and any broad dominant CLOTHING/OUTFIT colors that are clearly visible on the farmer. The back view exists so you can see items visible only from behind. Never infer colors from room background, floor, furniture, walls, lighting, scenery, hair, or a tiny/generic head-slot item. If a color is unclear, do not name it. If the farmer asks about the colors of the outfit, answer from the visible clothing/outfit colors only; do not invent unsupported colors like blue when the outfit is not blue. Do not mention the image, screenshot, PNG, pixels, front/back views, or attached file in the spoken dialogue. Do not invent details that are not clearly visible.");
            }
            // Outfit visual summary is intentionally omitted from follow-up —
            // the NPC should focus on the farmer's reply, not re-analyse the outfit.
            if (strictLocalMode)
            {
                builder.AppendLine("LOCAL SAFE STYLE MODE:");
                builder.AppendLine("Personality is more important than the outfit theme. Do not become generic, cutesy, poetic, theatrical, or narrational.");
                builder.AppendLine("No pet names like little rabbit, darling, precious, sunshine. No technical labels like indoor theme, NPC room, outfit category, or workshop unless the character would naturally say that exact in-world word.");
            }
            // PORTRAIT_SCORE_SYSTEM removed: no mandatory scoring instructions.
            // The AI reads the portrait descriptions from the NPC profile and freely decides which to use.
            builder.AppendLine("Portrait keys and descriptions: " + CollapseForPrompt(PortraitResolver.BuildPortraitKeyDescriptionList(profile), 800));
            builder.AppendLine("Choose each box's expression deliberately: reuse it while the mood stays the same and change it for a real emotional shift. Do NOT place portrait commands inside text.");
            builder.AppendLine();
            builder.AppendLine("NPC: " + context.NpcDisplayName);
            builder.AppendLine("Relationship: " + context.RelationshipStatus + ", hearts: " + context.RelationshipHearts + ", spouse: " + context.IsSpouse);
            CharacterPromptBuilder.AppendPlayerAddressAndGenderRule(builder, context, PromptStyle);
            CharacterPromptBuilder.AppendWornItemDeixisRule(builder, context);
            AppendFollowUpSituationalContext(builder, context);
            if (!string.IsNullOrWhiteSpace(context?.ConversationTranscript))
            {
                builder.AppendLine("Full conversation so far for this outfit reaction (oldest first, last line is the farmer's newest reply):");
                builder.AppendLine(CollapseForPrompt(context.ConversationTranscript, 2500));
                builder.AppendLine("Conversation continuity rule: read the WHOLE conversation above before answering. If the farmer's newest line refers back to something said earlier through a pronoun, an implied subject, a follow-up question, a callback, or an equivalent of 'and you?', answer that intended subject using the full conversation for context. Do not change the subject or restart on a different topic unless the farmer's newest line clearly does so.");
            }
            else
            {
                builder.AppendLine("Original NPC compliment/reaction: " + CollapseForPrompt(DialogueValidator.StripDialogueMarkup(npcCompliment), 500));
                builder.AppendLine("Farmer's reply: " + CollapseForPrompt(DialogueValidator.StripDialogueMarkup(playerReply), 500));
            }
            builder.AppendLine("Named-NPC-in-reply rule: if the farmer's reply names or clearly refers to another NPC, check the profile's personality/relationship rules for how this NPC reacts to that — including any jealousy, possessiveness, rivalry, disapproval, or protective rules — and apply them now if the situation fits. Do not default to a neutral, approving, or purely informative reaction about the other NPC when the profile defines a stronger, more specific behavior for this kind of moment.");
            builder.AppendLine("Permission-trap rule: if the farmer's reply is asking permission, announcing they are leaving to be with another romanceable NPC, or seeking approval (e.g. 'is that okay?', 'so I'll go see him/her', 'you don't mind?'), and the profile defines a jealousy/possessiveness/keep-them rule, the NPC must NOT grant permission, give a blessing, wish them fun, or say a calm version of 'sure, go ahead / do what you want / I won't stop you'. That polite, accepting, mature send-off is exactly the wrong answer here. Follow the profile's jealousy rule instead: stay reluctant, bothered, or vulnerable, and actively try to keep the farmer's attention or gently push back, in character. A casual or permission-seeking tone from the farmer does NOT release the NPC from this — do not treat 'is that okay?' as something to approve.");
            builder.AppendLine("If the farmer asks what color(s) the outfit/look has, answer from the current attached farmer image and/or confirmed visual support data. Name only broad colors that are clearly on the CLOTHING/OUTFIT itself. Do not use hair color, head-slot/generic hat color, portrait/background/UI colors, floor, walls, furniture, or scenery. If unsure, say it looks unclear rather than inventing a color. For the current pink-and-white/pastel outfit type, do not call it blue unless blue is clearly on the clothing.");
            // PORTRAIT_SCORE_SYSTEM removed: no mandatory portrait matching instructions.
            // The AI reads the NPC portrait descriptions and decides the best portrait for this reaction.
            if (context.IsAccessoryChange)
                builder.AppendLine("If the previous NPC line was uncertain, treat the farmer's reply as their explanation of what the small accessory/change actually was. React to that explanation naturally.");
            builder.AppendLine("Location: " + StringUtils.FirstNonEmpty(context.DetailedLocationName, context.LocationName));
            builder.AppendLine("Season: " + FormatSeasonForPrompt(context.Season, context.TargetLanguage));
            builder.AppendLine("Weather: " + context.Weather + ".");
            AppendNaturalTimeContextForPrompt(builder, context);
            AppendWeatherLocationRule(builder, context);
            if (!string.IsNullOrWhiteSpace(context.FestivalContext))
                builder.AppendLine("Festival: " + context.FestivalContext);
            if (!string.IsNullOrWhiteSpace(context.FarmerBirthdayContext))
                builder.AppendLine("Farmer birthday: " + context.FarmerBirthdayContext);

            string focusedProfile = CharacterPromptBuilder.BuildForOutfitCompliment(profile, context, includePlayerReplyMode: true, promptStyle: PromptStyle);
            if (!string.IsNullOrWhiteSpace(focusedProfile))
                builder.AppendLine(focusedProfile);

            // NOTE: outfit memory is intentionally NOT injected here — the NPC already
            // reacted to the outfit in the previous line. This follow-up is purely a
            // reaction to what the farmer said, not another outfit observation.

            string sebastianCustomOverride = BuildSebastianCustomSoftnessOverride(context);
            if (!string.IsNullOrWhiteSpace(sebastianCustomOverride))
                builder.AppendLine(sebastianCustomOverride);

            builder.AppendLine();
            builder.AppendLine("Return now exactly one compact JSON object. No other text.");
            return builder.ToString();
        }

        private static void AppendFollowUpSituationalContext(StringBuilder builder, OutfitAiContext context)
        {
            if (builder == null || context == null)
                return;

            string romanticHomeReveal = BuildRomanticHomeRevealingReactionRule(context, isFollowUp: true);
            if (!string.IsNullOrWhiteSpace(romanticHomeReveal))
                builder.AppendLine(romanticHomeReveal);

            if (!string.IsNullOrWhiteSpace(context.SpecialItemReactionContext))
            {
                string specialContext = context.SpecialItemReactionContext;
                int reactionHintIndex = specialContext.IndexOf("reaction hint:", StringComparison.OrdinalIgnoreCase);
                if (reactionHintIndex >= 0)
                    specialContext = specialContext[reactionHintIndex..];
                builder.AppendLine("SPECIAL ITEM: " + CollapseForPrompt(specialContext, 650));
            }
        }

        // ====================================================================
        // VOICE SAMPLES
        // The voice-sample system (reading real in-game dialogue and using it as a tone
        // reference) now lives in VoiceSampleService. It also owns the display->internal
        // name alias map, which OutfitAiService uses for profile resolution via
        // voiceSamples.TryReverseAlias / ResolveInternalName. The console commands delegate
        // to the two helpers below.
        // ====================================================================

    }
}
