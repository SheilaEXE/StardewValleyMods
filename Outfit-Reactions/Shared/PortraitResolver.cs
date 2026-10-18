using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace OutfitReactions.Ai
{
    /// <summary>
    /// Resolves and applies NPC portrait/emotion commands for generated dialogue: picking a
    /// portrait per dialogue box, finding a neutral fallback, sanitizing inline portrait commands,
    /// building the allowed-portrait list for the prompt, and suppressing portraits for local
    /// models when appropriate. Pure static text/profile processing; depends only on the profile
    /// passed in (and DialogueValidator.StripDialogueMarkup for text cleanup).
    /// </summary>
    internal static class PortraitResolver
    {
        /// <summary>
        /// Resolves the portrait key chosen by the AI directly to its $command string.
        /// The scoring/inference system (ResolvePortraitCommand) is disabled — the AI
        /// reads the NPC profile descriptions and picks the portrait itself.
        /// This method only validates that the key exists in the profile; if it doesn't,
        /// returns empty string (no portrait appended).
        /// </summary>
        private static string ResolvePortraitCommandSimple(CharacterAiProfile profile, string portraitKey, int availablePortraitCount = 0)
        {
            if (profile?.Portraits == null || string.IsNullOrWhiteSpace(portraitKey))
                return "";

            portraitKey = portraitKey.Trim();

            // The AI may return the $command directly (e.g. "$h", "$17") or just the key ("h", "17").
            string lookupKey = portraitKey.StartsWith("$", StringComparison.Ordinal)
                ? portraitKey.TrimStart('$')
                : portraitKey;

            if (profile.Portraits.TryGetValue(lookupKey, out PortraitProfile portrait) && !string.IsNullOrWhiteSpace(portrait?.Command))
                return ValidatePortraitCommand(portrait.Command.Trim(), availablePortraitCount);

            // Also accept if the AI returned the command directly and it matches any portrait command.
            if (portraitKey.StartsWith("$", StringComparison.Ordinal))
            {
                foreach (var pair in profile.Portraits)
                {
                    string cmd = pair.Value?.Command;
                    if (string.IsNullOrWhiteSpace(cmd))
                        cmd = "$" + pair.Key;
                    if (cmd.Equals(portraitKey, StringComparison.OrdinalIgnoreCase))
                        return ValidatePortraitCommand(cmd.Trim(), availablePortraitCount);
                }

                // The AI returned a $command that isn't in the profile (e.g. a hallucinated $4 or
                // $a). Accept it ONLY if it's a NUMERIC index that actually exists in the NPC's
                // sheet. Drop hallucinated letter emotions ($a/$l) the NPC may not have, so they
                // never render as an empty frame.
                int idx = PortraitCommandIndex(portraitKey);
                if (idx >= 0 && (availablePortraitCount <= 0 || idx < availablePortraitCount))
                    return portraitKey.Trim();
                return "";
            }

            return "";
        }

        /// <summary>
        /// Returns the portrait command only if it points to a portrait the NPC actually has.
        /// A command like "$7" that exceeds the NPC's real portrait count would render as an empty
        /// frame, so it's dropped (return "") which makes the dialogue fall back to neutral. When the
        /// count is unknown (0) or the command index can't be determined, the command is kept as-is.
        /// </summary>
        private static string ValidatePortraitCommand(string command, int availablePortraitCount)
        {
            if (string.IsNullOrWhiteSpace(command) || availablePortraitCount <= 0)
                return command ?? "";

            int index = PortraitCommandIndex(command);
            if (index < 0)
                return command; // unknown mapping (e.g. $a/$l) — leave it to Stardew
            return index < availablePortraitCount ? command : "";
        }

        /// <summary>
        /// Maps a portrait $command to its spritesheet index where it's reliably known:
        /// $0..$N numeric → that number; $h→1, $s→2. Returns -1 for anything else
        /// (e.g. $a/$l/$u, whose index varies and shouldn't be force-validated).
        /// </summary>
        private static int PortraitCommandIndex(string command)
        {
            if (string.IsNullOrWhiteSpace(command))
                return -1;
            string c = command.Trim().TrimStart('$').ToLowerInvariant();
            if (int.TryParse(c, out int n))
                return n;
            switch (c)
            {
                case "h": return 1;
                case "s": return 2;
                default: return -1;
            }
        }

        public static string ApplyPortraitsFromFields(CharacterAiProfile profile, string dialogueText, AiComplimentResult parsed, string inlinePortraitFallback = null, int availablePortraitCount = 0)
        {
            if (string.IsNullOrWhiteSpace(dialogueText))
                return dialogueText;

            string primaryPortraitKey = StringUtils.FirstNonEmpty(parsed?.Portrait, inlinePortraitFallback);
            List<string> perBoxKeys = parsed?.Portraits ?? new List<string>();
            return ApplyPortraitsToDialogueBoxes(profile, dialogueText, perBoxKeys, primaryPortraitKey, availablePortraitCount);
        }

        private static string ApplyPortraitsToDialogueBoxes(CharacterAiProfile profile, string dialogueText, List<string> portraitKeys, string primaryPortraitKey, int availablePortraitCount = 0)
        {
            if (string.IsNullOrWhiteSpace(dialogueText))
                return dialogueText;

            string primaryCommand = ResolvePortraitCommandSimple(profile, primaryPortraitKey, availablePortraitCount);

            string[] boxes = dialogueText.Split(new[] { "#$b#" }, StringSplitOptions.None);
            if (boxes.Length == 0)
                return dialogueText;

            StringBuilder result = new();
            string activeKey = primaryPortraitKey;
            for (int i = 0; i < boxes.Length; i++)
            {
                string box = boxes[i];
                if (portraitKeys != null && i < portraitKeys.Count && !string.IsNullOrWhiteSpace(portraitKeys[i]))
                    activeKey = portraitKeys[i];

                string command = ResolvePortraitCommandSimple(profile, activeKey, availablePortraitCount);

                if (string.IsNullOrWhiteSpace(command))
                    command = primaryCommand;

                // Stardew reads portrait commands at the end of EACH dialogue box,
                // before the #$b# break. Appending only once at the end makes the
                // first box show the default portrait and only later boxes change.
                if (!string.IsNullOrWhiteSpace(command) && !string.IsNullOrWhiteSpace(box))
                    box = box.TrimEnd() + command;

                result.Append(box);
                if (i < boxes.Length - 1)
                    result.Append("#$b#");
            }

            return result.ToString();
        }

        public static string ExtractLastAllowedPortraitKeyFromText(string text, CharacterAiProfile profile)
        {
            if (string.IsNullOrWhiteSpace(text) || profile?.Portraits == null || profile.Portraits.Count <= 0)
                return "";

            const string BreakToken = "\uE000OC_BREAK\uE000";
            string protectedText = text.Replace("#$b#", BreakToken);
            Dictionary<string, (string Key, PortraitProfile Portrait, string Command)> allowed = BuildAllowedPortraitCommandLookup(profile);
            if (allowed.Count <= 0)
                return "";

            string foundKey = "";
            foreach (Match match in Regex.Matches(protectedText, @"\$[A-Za-z0-9]+", RegexOptions.CultureInvariant))
            {
                string command = match.Value.Trim();
                if (allowed.TryGetValue(command, out var info))
                    foundKey = info.Key;
            }

            return foundKey;
        }

        public static string BuildPortraitCommandList(CharacterAiProfile profile)
        {
            if (profile?.Portraits == null || profile.Portraits.Count <= 0)
                return "none";

            List<string> commands = new();
            foreach (var pair in profile.Portraits)
            {
                string command = pair.Value?.Command;
                if (string.IsNullOrWhiteSpace(command))
                    command = "$" + pair.Key;

                string description = pair.Value?.Description ?? "";
                commands.Add(string.IsNullOrWhiteSpace(description)
                    ? command
                    : command + " (" + description + ")");
            }

            return string.Join(", ", commands);
        }

        public static string BuildPortraitKeyDescriptionList(CharacterAiProfile profile)
        {
            if (profile?.Portraits == null || profile.Portraits.Count <= 0)
                return "none";

            List<string> entries = new();
            foreach (var pair in profile.Portraits)
            {
                string description = pair.Value?.Description ?? "";
                entries.Add(string.IsNullOrWhiteSpace(description)
                    ? pair.Key
                    : pair.Key + " (" + description + ")");
            }

            return string.Join(", ", entries);
        }

        public static string SanitizeInlinePortraitCommands(string text, CharacterAiProfile profile, bool isLocalProvider, ModConfig config)
        {
            if (string.IsNullOrWhiteSpace(text))
                return text;

            // The AI is no longer allowed to place portrait commands inside the dialogue text.
            // It must write clean spoken dialogue and choose a portrait key in the JSON field.
            // This sanitizer is a safety net: if a model still returns $h/$l/$16 etc. inside
            // text, remove those commands before the mod appends one validated portrait at the end.
            const string BreakToken = "\uE000OC_BREAK\uE000";
            string sanitized = text.Replace("#$b#", BreakToken);

            sanitized = Regex.Replace(
                sanitized,
                @"\$[A-Za-z0-9]+",
                "",
                RegexOptions.CultureInvariant
            );

            sanitized = sanitized.Replace(BreakToken, "#$b#");
            sanitized = Regex.Replace(sanitized, @"\s*#\$b#\s*", "#$b#");
            sanitized = Regex.Replace(sanitized, @"\s+([,.;:!?])", "$1", RegexOptions.CultureInvariant);
            sanitized = Regex.Replace(sanitized, @"([*])\s+([,.;:!?])", "$1$2", RegexOptions.CultureInvariant);
            sanitized = Regex.Replace(sanitized, @"\s{2,}", " ").Trim();
            sanitized = Regex.Replace(sanitized, @"(?:#\$b#){4,}", "#$b##$b##$b#");
            return sanitized.Trim();
        }

        private static Dictionary<string, (string Key, PortraitProfile Portrait, string Command)> BuildAllowedPortraitCommandLookup(CharacterAiProfile profile)
        {
            Dictionary<string, (string Key, PortraitProfile Portrait, string Command)> result = new(StringComparer.OrdinalIgnoreCase);
            if (profile?.Portraits == null)
                return result;

            foreach (var pair in profile.Portraits)
            {
                string command = pair.Value?.Command;
                if (string.IsNullOrWhiteSpace(command))
                    command = "$" + pair.Key;

                command = command.Trim();
                if (string.IsNullOrWhiteSpace(command) || !command.StartsWith("$", StringComparison.Ordinal))
                    continue;

                result[command] = (pair.Key, pair.Value, command);
            }

            return result;
        }

        private static bool ShouldSuppressLocalPortrait(string portraitKey, PortraitProfile portrait, string dialogueText)
        {
            string joined = ((portraitKey ?? "") + " " + (portrait?.Description ?? "")).ToLowerInvariant();
            if (string.IsNullOrWhiteSpace(joined))
                return false;

            string[] unsafeMood =
            {
                "bravo", "irritado", "triste", "averso", "desapontado", "chorando", "gripado", "ciúmes", "ciumes",
                "raiva", "aff", "gemido", "prazer", "assustado", "choque", "mad", "angry", "annoyed", "sad", "crying",
                "sick", "jealous", "disgust", "upset", "frustrated"
            };

            bool unsafePortrait = false;
            foreach (string marker in unsafeMood)
            {
                if (joined.Contains(marker))
                {
                    unsafePortrait = true;
                    break;
                }
            }

            if (!unsafePortrait)
                return false;

            string lowerDialogue = " " + DialogueValidator.StripDialogueMarkup(dialogueText).ToLowerInvariant() + " ";
            bool dialogueIsNegative = lowerDialogue.Contains(" weird ") || lowerDialogue.Contains(" strange ") || lowerDialogue.Contains(" odd ")
                || lowerDialogue.Contains(" estranho ") || lowerDialogue.Contains(" esquisito ") || lowerDialogue.Contains(" sério? ") || lowerDialogue.Contains(" serio? ");

            return !dialogueIsNegative;
        }
    }
}
