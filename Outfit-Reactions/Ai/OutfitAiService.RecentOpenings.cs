using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace OutfitReactions.Ai
{
    internal sealed partial class OutfitAiService
    {
        private const int RecentOpeningMemoryLimit = 8;
        private const int RecentOpeningPromptLimit = 6;
        private const int RecentOpeningCharacterLimit = 140;
        private const string RecentOpeningBlockStart = "RECENT OUTFIT-REACTION OPENINGS (variety reference only):";
        private const string RecentOpeningBlockEnd = "END OF RECENT OUTFIT-REACTION OPENINGS.";

        private readonly object recentOpeningLock = new();
        private readonly List<string> recentDialogueOpenings = new();

        private void AppendRecentOpeningVarietyContext(StringBuilder builder)
        {
            if (builder == null)
                return;

            string[] recentOpenings;
            lock (recentOpeningLock)
            {
                recentOpenings = recentDialogueOpenings
                    .TakeLast(RecentOpeningPromptLimit)
                    .ToArray();
            }

            if (recentOpenings.Length <= 0)
                return;

            builder.AppendLine(RecentOpeningBlockStart);
            builder.AppendLine("The lines below were recently used by NPCs. They are not forbidden phrases and their topics may still be mentioned naturally. However, begin this NPC's reaction through a genuinely different observation, action, question, joke, concern, or conversational angle that fits their personality. Do not preserve the same opening formula by merely swapping an interjection, the NPC's wording, or the farmer's name.");
            foreach (string opening in recentOpenings)
                builder.AppendLine("- " + opening);
            builder.AppendLine(RecentOpeningBlockEnd);
        }

        private void RememberDialogueOpening(string dialogue)
        {
            string opening = ExtractDialogueOpening(dialogue);
            if (string.IsNullOrWhiteSpace(opening))
                return;

            lock (recentOpeningLock)
            {
                recentDialogueOpenings.RemoveAll(existing =>
                    string.Equals(existing, opening, StringComparison.OrdinalIgnoreCase));
                recentDialogueOpenings.Add(opening);

                while (recentDialogueOpenings.Count > RecentOpeningMemoryLimit)
                    recentDialogueOpenings.RemoveAt(0);
            }
        }

        private static string ExtractDialogueOpening(string dialogue)
        {
            if (string.IsNullOrWhiteSpace(dialogue))
                return null;

            string firstBox = dialogue.Split(new[] { "#$b#" }, StringSplitOptions.None)[0];
            string spoken = DialogueValidator.StripDialogueMarkup(firstBox);
            spoken = Regex.Replace(spoken, @"\s+", " ").Trim();
            if (spoken.Length <= 0)
                return null;

            int sentenceEnd = FindFirstSentenceEnd(spoken);
            if (sentenceEnd >= 0)
                spoken = spoken.Substring(0, sentenceEnd + 1).Trim();

            // A line can legitimately begin with a pause ("..." or "…"). Never remember a
            // punctuation-only fragment, since it gives the model no useful variety reference.
            if (spoken.Count(char.IsLetterOrDigit) < 2)
                return null;

            if (spoken.Length <= RecentOpeningCharacterLimit)
                return spoken;

            string shortened = spoken.Substring(0, RecentOpeningCharacterLimit);
            int lastSpace = shortened.LastIndexOf(' ');
            if (lastSpace >= RecentOpeningCharacterLimit / 2)
                shortened = shortened.Substring(0, lastSpace);

            return shortened.TrimEnd(',', ';', ':', ' ') + "...";
        }

        private static int FindFirstSentenceEnd(string text)
        {
            for (int i = 0; i < text.Length; i++)
            {
                if (text[i] == '!' || text[i] == '?')
                    return i;

                if (text[i] != '.')
                    continue;

                // Treat runs of periods as an ellipsis/pause instead of cutting the remembered
                // opening at the first dot. A later single period can still end the sentence.
                bool belongsToEllipsis = (i > 0 && text[i - 1] == '.')
                    || (i + 1 < text.Length && text[i + 1] == '.');
                if (!belongsToEllipsis)
                    return i;
            }

            return -1;
        }

        private static string RemoveRecentOpeningVarietyContextForCache(string prompt)
        {
            if (string.IsNullOrWhiteSpace(prompt))
                return prompt ?? "";

            int start = prompt.IndexOf(RecentOpeningBlockStart, StringComparison.Ordinal);
            if (start < 0)
                return prompt;

            int end = prompt.IndexOf(RecentOpeningBlockEnd, start, StringComparison.Ordinal);
            if (end < 0)
                return prompt;

            end += RecentOpeningBlockEnd.Length;
            while (end < prompt.Length && (prompt[end] == '\r' || prompt[end] == '\n'))
                end++;

            return prompt.Remove(start, end - start);
        }
    }
}
