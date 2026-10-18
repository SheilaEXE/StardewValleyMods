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
        /// <summary>
        /// Last-resort salvage when both the follow-up attempt and its retry fail validation.
        /// Relaxes all length and #$b# requirements to extract any coherent spoken text so the
        /// player's reply does not go completely unanswered.
        /// </summary>
        private string TrySalvageFollowUpRaw(string raw, CharacterAiProfile profile, OutfitAiContext context, ActiveAiSettings ai)
        {
            if (string.IsNullOrWhiteSpace(raw))
                return null;

            try
            {
                // Try JSON parse first.
                AiComplimentResult parsed = AiResponseParser.ParseAiResult(raw);
                if (parsed != null && !string.IsNullOrWhiteSpace(parsed.Text))
                {
                    string cleaned = DialogueValidator.CleanDialogueText(parsed.Text, ai.MaxCharacters);
                    string inlinePortraitFallback = PortraitResolver.ExtractLastAllowedPortraitKeyFromText(cleaned, profile);
                    cleaned = DialogueValidator.RestoreEllipsesAndNormalise(cleaned);
                    ModConfig config = getConfig?.Invoke() ?? new ModConfig();
                    cleaned = PortraitResolver.SanitizeInlinePortraitCommands(cleaned, profile, ActiveAiSettingsResolver.IsLocal(ai), config);
                    cleaned = SanitizeContextInappropriateProfanity(cleaned, context, config.EnableProfanityFilter);
                    if (!string.IsNullOrWhiteSpace(cleaned) &&
                        !DialogueValidator.LooksLikeInstructionLeak(cleaned) &&
                        !DialogueValidator.LooksLikeCopiedFormatExample(cleaned))
                    {
                        // If the salvaged text has no #$b# break, append a soft continuation
                        // so it passes validation. Better to show something than lose the reply.
                        if (!cleaned.Contains("#$b#"))
                        {
                            string lang = context?.TargetLanguage ?? "en";
                            string continuation = lang.StartsWith("pt", StringComparison.OrdinalIgnoreCase)
                                ? "#$b#..."
                                : "#$b#...";
                            cleaned = cleaned.TrimEnd('.', '!', '?') + "..." + continuation;
                        }

                        return PortraitResolver.ApplyPortraitsFromFields(profile, cleaned, parsed, inlinePortraitFallback, context?.AvailablePortraitCount ?? 0);
                    }
                }
            }
            catch { }

            return null;
        }
        // AI PROVIDER HTTP CALLS moved to AiProviderClient (Ai/AiProviderClient.cs).
        // OutfitAiService builds ActiveAiSettings and calls aiClient.GenerateRawAsync(...).

        private bool TryBuildValidatedDialogue(CharacterAiProfile profile, OutfitAiContext context, ActiveAiSettings ai, string raw, out string dialogue, out string issue)
        {
            dialogue = null;
            issue = "unknown issue";

            if (string.IsNullOrWhiteSpace(raw))
            {
                issue = "empty provider response";
                return false;
            }

            AiComplimentResult parsed = ActiveAiSettingsResolver.IsLocal(ai)
                ? (AiResponseParser.ParseAiResult(raw) ?? AiResponseParser.ParseLocalDashLineStyleResult(raw, profile))
                : AiResponseParser.ParseAiResult(raw);
            if (parsed == null)
            {
                issue = ActiveAiSettingsResolver.IsLocal(ai) ? "invalid local JSON or fallback text format" : "invalid JSON";
                return false;
            }

            string cleaned = DialogueValidator.CleanDialogueText(parsed.Text, ai.MaxCharacters);
            string inlinePortraitFallback = PortraitResolver.ExtractLastAllowedPortraitKeyFromText(cleaned, profile);
            ModConfig config = getConfig?.Invoke() ?? new ModConfig();
            cleaned = PortraitResolver.SanitizeInlinePortraitCommands(cleaned, profile, ActiveAiSettingsResolver.IsLocal(ai), config);
            cleaned = SanitizeContextInappropriateProfanity(cleaned, context, config.EnableProfanityFilter);
            cleaned = DialogueValidator.RestoreEllipsesAndNormalise(cleaned);
            if (string.IsNullOrWhiteSpace(cleaned))
            {
                issue = "JSON did not contain a usable text field";
                return false;
            }

            string themeAnchorIssue = DialogueValidator.ValidateSemanticThemeAnchor(
                cleaned,
                parsed.ThemeAnchor,
                RequiresSemanticThemeAnchor(context) && !parsed.NeedsClarification);
            if (!string.IsNullOrWhiteSpace(themeAnchorIssue))
            {
                issue = themeAnchorIssue;
                return false;
            }

            string validationIssue = DialogueValidator.ValidateGeneratedDialogueText(cleaned, context, config, ai);
            if (!string.IsNullOrWhiteSpace(validationIssue))
            {
                issue = validationIssue;
                return false;
            }
            string seasonIssue = ValidateLocalSeasonReferences(" " + DialogueValidator.StripDialogueMarkup(cleaned).ToLowerInvariant() + " ", context);
            if (!string.IsNullOrWhiteSpace(seasonIssue))
            {
                issue = seasonIssue;
                return false;
            }
            if (ActiveAiSettingsResolver.IsLocal(ai) && config.LocalAiSafeMode)
            {
                string localIssue = ValidateLocalGeneratedDialogueText(cleaned, context, profile, config);
                if (!string.IsNullOrWhiteSpace(localIssue))
                {
                    issue = localIssue;
                    return false;
                }
            }

            dialogue = PortraitResolver.ApplyPortraitsFromFields(profile, cleaned, parsed, inlinePortraitFallback, context?.AvailablePortraitCount ?? 0);
            LogPortraitSelection(context, parsed, dialogue);

            if (parsed.NeedsClarification && context != null && context.IsAccessoryChange)
                dialogue = AccessoryClarificationMarker + dialogue;

            issue = null;
            return true;
        }

        private bool TryBuildLenientDialogue(CharacterAiProfile profile, OutfitAiContext context, ActiveAiSettings ai, string raw, out string dialogue, out string issue)
        {
            dialogue = null;
            issue = "unknown issue";

            if (string.IsNullOrWhiteSpace(raw))
            {
                issue = "empty provider response";
                return false;
            }

            AiComplimentResult parsed = ActiveAiSettingsResolver.IsLocal(ai)
                ? (AiResponseParser.ParseAiResult(raw) ?? AiResponseParser.ParseLocalDashLineStyleResult(raw, profile))
                : AiResponseParser.ParseAiResult(raw);
            if (parsed == null)
            {
                issue = ActiveAiSettingsResolver.IsLocal(ai) ? "invalid local JSON or fallback text format" : "invalid JSON";
                return false;
            }

            string cleaned = DialogueValidator.CleanDialogueText(parsed.Text, ai.MaxCharacters);
            string inlinePortraitFallback = PortraitResolver.ExtractLastAllowedPortraitKeyFromText(cleaned, profile);
            ModConfig config = getConfig?.Invoke() ?? new ModConfig();
            cleaned = PortraitResolver.SanitizeInlinePortraitCommands(cleaned, profile, ActiveAiSettingsResolver.IsLocal(ai), config);
            cleaned = SanitizeContextInappropriateProfanity(cleaned, context, config.EnableProfanityFilter);
            cleaned = DialogueValidator.RestoreEllipsesAndNormalise(cleaned);
            if (string.IsNullOrWhiteSpace(cleaned))
            {
                issue = "response did not contain a usable dialogue text";
                return false;
            }

            string themeAnchorIssue = DialogueValidator.ValidateSemanticThemeAnchor(
                cleaned,
                parsed.ThemeAnchor,
                RequiresSemanticThemeAnchor(context) && !parsed.NeedsClarification);
            if (!string.IsNullOrWhiteSpace(themeAnchorIssue))
            {
                issue = themeAnchorIssue;
                return false;
            }

            if (DialogueValidator.LooksLikeInstructionLeak(cleaned) || DialogueValidator.LooksLikeCopiedFormatExample(cleaned))
            {
                issue = "instruction or format example leaked into dialogue";
                return false;
            }

            string pacingIssue = DialogueValidator.ValidateDialogueBoxPacing(cleaned, config, ai);
            if (!string.IsNullOrWhiteSpace(pacingIssue))
            {
                issue = pacingIssue;
                return false;
            }

            // Lenient mode may relax style and length rules, but it must never accept a
            // fabricated season comparison that contradicts the authoritative scene context.
            string seasonIssue = ValidateLocalSeasonReferences(" " + DialogueValidator.StripDialogueMarkup(cleaned).ToLowerInvariant() + " ", context);
            if (!string.IsNullOrWhiteSpace(seasonIssue))
            {
                issue = seasonIssue;
                return false;
            }

            dialogue = PortraitResolver.ApplyPortraitsFromFields(profile, cleaned, parsed, inlinePortraitFallback, context?.AvailablePortraitCount ?? 0);
            LogPortraitSelection(context, parsed, dialogue);

            if (parsed.NeedsClarification && context != null && context.IsAccessoryChange)
                dialogue = AccessoryClarificationMarker + dialogue;

            issue = null;
            return true;
        }

        private void LogPortraitSelection(OutfitAiContext context, AiComplimentResult parsed, string dialogue)
        {
            if (!OutfitReactions.ModEntry.DebugLog)
                return;

            string applied = string.Join(",", (dialogue ?? "").Split(new[] { "#$b#" }, StringSplitOptions.None)
                .Select(box => Regex.Match(box.TrimEnd(), @"\$[A-Za-z0-9]+$"))
                .Where(match => match.Success)
                .Select(match => match.Value));
            string requestedPerBox = parsed?.Portraits == null ? "" : string.Join(",", parsed.Portraits);
            monitor.Log("[AI PORTRAIT] NPC=" + (context?.NpcName ?? "<unknown>")
                + " primary='" + (parsed?.Portrait ?? "")
                + "' perBox=[" + requestedPerBox
                + "] applied=[" + applied + "]", LogLevel.Trace);
        }
    }
}
