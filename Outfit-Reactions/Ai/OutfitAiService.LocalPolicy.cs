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
        private static string BuildLocalSeasonAuthorityInstruction(OutfitAiContext context)
        {
            if (context == null)
                return "";

            string actualSeasonKey = NormalizeSeasonKey(context.Season);
            string actualSeason = FormatSeasonForPrompt(context.Season, context.TargetLanguage);
            string outfitSeason = DescribeInferredOutfitSeasonForPrompt(context);

            StringBuilder builder = new();
            builder.Append("AUTHORITATIVE SEASON RULE: the actual current in-game season is ").Append(actualSeason).Append(". ");
            builder.Append("Outfit/theme clues may suggest a different season, but those clues describe the outfit style, not the date. ");
            builder.Append("Do not replace the actual season with another one. ");

            if (!string.IsNullOrWhiteSpace(outfitSeason))
            {
                builder.Append("This outfit appears to have ").Append(outfitSeason).Append(" vibes. ");
                builder.Append("If that clashes with the actual season, mention the contrast using the actual season above.");
            }
            else if (!string.IsNullOrWhiteSpace(actualSeasonKey))
            {
                builder.Append("Only mention another season if the outfit itself clearly has that seasonal theme.");
            }

            return builder.ToString();
        }

        private static string FormatSeasonForPrompt(string season, string targetLanguage)
        {
            string key = NormalizeSeasonKey(season);
            return key switch
            {
                "spring" => "spring",
                "summer" => "summer",
                "fall" => "fall / autumn",
                "winter" => "winter",
                _ => string.IsNullOrWhiteSpace(season) ? "unknown" : season
            };
        }

        private static string NormalizeSeasonKey(string season)
        {
            if (string.IsNullOrWhiteSpace(season))
                return "";

            string value = season.Trim().ToLowerInvariant();
            if (value.Contains("spring") || value.Contains("primavera"))
                return "spring";
            if (value.Contains("summer") || value.Contains("verão") || value.Contains("verao"))
                return "summer";
            if (value.Contains("fall") || value.Contains("autumn") || value.Contains("outono"))
                return "fall";
            if (value.Contains("winter") || value.Contains("inverno"))
                return "winter";

            return value;
        }

        private static string DescribeInferredOutfitSeasonForPrompt(OutfitAiContext context)
        {
            HashSet<string> inferred = InferSeasonKeysFromOutfitClues(context);
            if (inferred.Count <= 0)
                return "";

            List<string> labels = new();
            foreach (string season in inferred)
            {
                labels.Add(season switch
                {
                    "spring" => "spring",
                    "summer" => "summer/beach",
                    "fall" => "fall/autumn",
                    "winter" => "winter/Christmas",
                    _ => season
                });
            }

            return string.Join(" and ", labels);
        }

        private static HashSet<string> InferSeasonKeysFromOutfitClues(OutfitAiContext context)
        {
            HashSet<string> result = new(StringComparer.OrdinalIgnoreCase);
            if (context == null)
                return result;

            string allClues = string.Join(" ", new[]
            {
                context.OutfitName,
                context.SafeOutfitHint,
                context.NoticedChangeName,
                context.SafeNoticedChangeHint,
                context.DialogueKey,
                context.FashionSenseVisualSummary,
                SanitizeThemeContextForPrompt(context.ThemeContext),
                SanitizeThemeContextForPrompt(context.ThemePriorityInstruction)
            }).ToLowerInvariant();

            if (allClues.Contains("xmas") || allClues.Contains("christmas") || allClues.Contains("natal") || allClues.Contains("noel") || allClues.Contains("winter") || allClues.Contains("inverno"))
                result.Add("winter");

            // Swimwear is appropriate or strange based primarily on place and weather.
            // A bikini at the beach isn't automatically a summer costume.
            bool swimOrBeach = LooksLikeSwimwearOrBeachwear(allClues);
            if (!swimOrBeach && (allClues.Contains("summer") || allClues.Contains("verão") || allClues.Contains("verao")))
                result.Add("summer");

            if (allClues.Contains("spring") || allClues.Contains("primavera") || allClues.Contains("flower dance") || allClues.Contains("flowerdance"))
                result.Add("spring");

            if (allClues.Contains("fall") || allClues.Contains("autumn") || allClues.Contains("outono") || allClues.Contains("spirit") || allClues.Contains("halloween"))
                result.Add("fall");

            return result;
        }

        private static bool HasWeatherThemedOutfitClues(OutfitAiContext context)
        {
            if (context == null)
                return false;

            string allClues = string.Join(" ", new[]
            {
                context.OutfitName,
                context.SafeOutfitHint,
                context.NoticedChangeName,
                context.SafeNoticedChangeHint,
                context.DialogueKey,
                context.FashionSenseVisualSummary,
                SanitizeThemeContextForPrompt(context.ThemeContext),
                SanitizeThemeContextForPrompt(context.ThemePriorityInstruction)
            }).ToLowerInvariant();

            return Regex.IsMatch(
                allClues,
                @"\b(raincoat|rain[ -]?coat|rain[ -]?gear|umbrella|waterproof|wellies|galoshes?|poncho|snow[ -]?gear|snowsuit|snow[ -]?suit|snow[ -]?boots?|blizzard[ -]?gear|sun[ -]?hat|heatwave[ -]?gear|capa de chuva|guarda[ -]?chuva|galochas?|imperme[aá]vel|roupa de neve|botas? de neve)\b",
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant
            );
        }

        private static string ValidateLocalSeasonReferences(string lower, OutfitAiContext context)
        {
            if (string.IsNullOrWhiteSpace(lower) || context == null)
                return null;

            string normalizedText = " " + Regex.Replace(lower, @"[^\p{L}\p{N}]+", " ").Trim() + " ";

            string actual = NormalizeSeasonKey(context.Season);
            HashSet<string> allowed = InferSeasonKeysFromOutfitClues(context);
            if (!string.IsNullOrWhiteSpace(actual))
                allowed.Add(actual);

            Dictionary<string, string[]> aliases = new(StringComparer.OrdinalIgnoreCase)
            {
                ["spring"] = new[] { " spring ", " primavera " },
                ["summer"] = new[] { " summer ", " verão ", " verao " },
                ["fall"] = new[] { " fall ", " autumn ", " outono " },
                ["winter"] = new[] { " winter ", " inverno " }
            };

            foreach (var pair in aliases)
            {
                bool mentioned = pair.Value.Any(alias => normalizedText.Contains(alias));
                if (!mentioned)
                    continue;

                if (!allowed.Contains(pair.Key))
                    return "local response confused the actual season with " + pair.Key;
            }

            return null;
        }

        private static string BuildSeasonalAwarenessInstruction(OutfitAiContext context)
        {
            if (context == null)
                return "";

            string actualSeason = NormalizeSeasonKey(context.Season);
            HashSet<string> inferredSeasons = InferSeasonKeysFromOutfitClues(context);
            string allClues = string.Join(" ", new[]
            {
                context.OutfitName,
                context.SafeOutfitHint,
                context.NoticedChangeName,
                context.SafeNoticedChangeHint,
                context.DialogueKey,
                context.FashionSenseVisualSummary,
                SanitizeThemeContextForPrompt(context.ThemeContext),
                SanitizeThemeContextForPrompt(context.ThemePriorityInstruction)
            }).ToLowerInvariant();

            bool swimOrBeach = LooksLikeSwimwearOrBeachwear(allClues);
            bool hasDifferentNonSummerSeason = inferredSeasons.Any(season =>
                !season.Equals("summer", StringComparison.OrdinalIgnoreCase)
                && !season.Equals(actualSeason, StringComparison.OrdinalIgnoreCase));

            if (swimOrBeach && context.IsBeachOrIsland && !hasDifferentNonSummerSeason)
            {
                string weather = (context.Weather ?? "").ToLowerInvariant();
                bool clearlyColdWeather = weather.Contains("snow")
                    || weather.Contains("blizzard")
                    || weather.Contains("sleet")
                    || weather.Contains("hail")
                    || weather.Contains("severe cold")
                    || weather.Contains("cold rain");
                bool rainyOrStormyWeather = weather.Contains("rain")
                    || weather.Contains("storm")
                    || weather.Contains("deluge")
                    || weather.Contains("thunder");

                if (actualSeason == "winter" || clearlyColdWeather)
                {
                    return "SWIMWEAR CONTEXT RULE: the farmer is at a beach, island, or directly beach-connected place, but the authoritative current season/weather is cold ("
                        + FormatSeasonForPrompt(context.Season, context.TargetLanguage) + ", " + context.Weather
                        + "). Swimwear is contextually strange because of the cold. The NPC may naturally question, tease, worry about, or react to that mismatch according to personality. Do not invent a separate swimming or diving season.";
                }

                if (rainyOrStormyWeather)
                {
                    return "SWIMWEAR CONTEXT RULE: the beach/island location fits swimwear, but the authoritative current weather is "
                        + context.Weather
                        + ". The NPC may find swimming impractical right now because of today's weather, but must not call the outfit early, late, or out of season. Do not invent a swimming or diving season.";
                }

                return "SWIMWEAR CONTEXT RULE: the farmer is currently at a beach, island, or directly beach-connected place, and the weather is not clearly cold or stormy. Swimwear is appropriate here even if the current season is not summer. Do not frame it as early, late, out of season, or meant for another time of year, and do not invent a swimming or diving season.";
            }

            bool hasClearSeasonMismatch = inferredSeasons.Count > 0
                && !string.IsNullOrWhiteSpace(actualSeason)
                && !inferredSeasons.Contains(actualSeason);

            if (hasClearSeasonMismatch)
            {
                return "MANDATORY OUT-OF-SEASON REACTION: reliable outfit clues give this look a clear seasonal or holiday identity that conflicts with the authoritative current in-game season, " + FormatSeasonForPrompt(context.Season, context.TargetLanguage) + ". The spoken reaction MUST explicitly notice that the look is early, late, out of season, or belongs to a different time of year. This mismatch must be a meaningful part of the reaction, not omitted, softened into generic praise, or reduced to an analytical fashion comment. Express it naturally through this NPC's established personality and relationship. Do not claim the related season, holiday, or event is currently happening, and never mention these instructions or technical labels.";
            }

            if (HasWeatherThemedOutfitClues(context))
            {
                return "WEATHER-THEMED LOOK RULE: this look includes gear associated with particular weather. Judge whether that gear fits only against the authoritative current weather, " + context.Weather + ", not against the season. It may be impractical for today's weather, but never call it early, late, out of season, or meant for another time of year unless separate reliable clues clearly identify a season, holiday, or festival.";
            }

            return "";
        }

        private static string BuildLanguageExampleLocalLine(string targetLanguage)
        {
            return "<spoken outfit reaction in the current game language; no portrait commands inside the text>";
        }
















        // Inline helper so the scoring loop above stays readable.
        private static string ValidateLocalGeneratedDialogueText(string text, OutfitAiContext context, CharacterAiProfile profile, ModConfig config)
        {
            if (string.IsNullOrWhiteSpace(text))
                return "empty local dialogue";

            string stripped = DialogueValidator.StripDialogueMarkup(text);
            string lower = " " + stripped.ToLowerInvariant() + " ";

            if (LooksLikeThirdPersonNarrationForNpc(lower, context?.NpcDisplayName ?? context?.NpcName))
                return "local response was narration instead of spoken dialogue";

            if (LooksLikeGenericCutesyLocalLine(lower, context))
                return "local response sounded like generic cutesy/poetic praise instead of the NPC";

            string themeSpecificityIssue = DialogueValidator.ValidateRecognizableThemeSpecificity(text, context);
            if (!string.IsNullOrWhiteSpace(themeSpecificityIssue))
                return "local " + themeSpecificityIssue;

            string accessoryCombinationIssue = DialogueValidator.ValidateAccessoryOutfitCombinationSpecificity(text, context);
            if (!string.IsNullOrWhiteSpace(accessoryCombinationIssue))
                return "local " + accessoryCombinationIssue;

            if (LooksLikeUnrelatedLocalLine(lower))
                return "local response introduced unrelated generic details";

            string seasonIssue = ValidateLocalSeasonReferences(lower, context);
            if (!string.IsNullOrWhiteSpace(seasonIssue))
                return seasonIssue;

            return null;
        }

        private static bool LooksLikeThirdPersonNarrationForNpc(string lower, string npcDisplayName)
        {
            if (string.IsNullOrWhiteSpace(lower))
                return false;

            string npc = (npcDisplayName ?? "").Trim().ToLowerInvariant();
            if (!string.IsNullOrWhiteSpace(npc))
            {
                if (lower.Contains(" " + npc + " is ") || lower.Contains(" " + npc + " looks ") || lower.Contains(" " + npc + " smiles ") || lower.Contains(" " + npc + " blushes "))
                    return true;
                if (lower.Contains(" " + npc + " está ") || lower.Contains(" " + npc + " esta ") || lower.Contains(" " + npc + " olha ") || lower.Contains(" " + npc + " sorri ") || lower.Contains(" " + npc + " fica "))
                    return true;
            }

            return lower.Contains("contexto atual")
                || lower.Contains("current context")
                || lower.Contains("tonalidade")
                || lower.Contains("tone:")
                || lower.Contains("portrait:")
                || lower.Contains("**portrait**")
                || lower.Contains("stage direction")
                || lower.Contains("scene description");
        }

        private static bool LooksLikeGenericCutesyLocalLine(string lower, OutfitAiContext context)
        {
            // Do not block words like radiant/radiante, wonderful/maravilhoso, magical/mágico, etc.
            // They can sound clichéd in some outputs, but certain NPCs or outfit themes may use them naturally.
            // The prompt should guide style; the validator should not hard-ban this vocabulary.
            return false;
        }

        private static bool LooksLikeUnrelatedLocalLine(string lower)
        {
            if (string.IsNullOrWhiteSpace(lower))
                return false;

            string[] unrelated =
            {
                " crafting wood ", " chopping wood ", " perfect for mining ", " fighting monsters ", " watering crops ",
                " cortar madeira ", " minerar ", " lutar contra monstros ", " regar plantações ", " regar plantacoes "
            };

            foreach (string phrase in unrelated)
            {
                if (lower.Contains(phrase))
                    return true;
            }

            return false;
        }

        /* PORTRAIT_SCORE_SYSTEM — commented out: portrait selection is now left entirely to the AI.
           The AI reads portrait descriptions from the NPC profile and decides which to use.
        */


    }
}
