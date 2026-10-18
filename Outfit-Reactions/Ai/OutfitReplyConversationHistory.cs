using System;
using System.Collections.Generic;
using System.Text;

namespace OutfitReactions.Ai
{
    /// <summary>
    /// Keeps the short-lived transcript for an outfit-reaction conversation with each NPC.
    /// This is intentionally session-only state; it is not saved with the farm.
    /// </summary>
    internal sealed class OutfitReplyConversationHistory
    {
        private readonly Dictionary<string, List<(string Speaker, string Text)>> conversations = new(StringComparer.OrdinalIgnoreCase);

        public void Reset(string npcName)
        {
            if (!string.IsNullOrWhiteSpace(npcName))
                conversations.Remove(npcName);
        }

        public void Start(string npcName, string npcOpeningLine)
        {
            if (string.IsNullOrWhiteSpace(npcName))
                return;

            List<(string Speaker, string Text)> history = new();
            if (!string.IsNullOrWhiteSpace(npcOpeningLine))
                history.Add(("NPC", npcOpeningLine));
            conversations[npcName] = history;
        }

        public void Append(string npcName, string speaker, string text)
        {
            if (string.IsNullOrWhiteSpace(npcName) || string.IsNullOrWhiteSpace(text))
                return;

            if (!conversations.TryGetValue(npcName, out List<(string Speaker, string Text)> history))
            {
                history = new();
                conversations[npcName] = history;
            }
            history.Add((speaker, text));
        }

        public string BuildTranscript(string npcName, int maxChars = 2500)
        {
            if (string.IsNullOrWhiteSpace(npcName)
                || !conversations.TryGetValue(npcName, out List<(string Speaker, string Text)> history)
                || history.Count == 0)
            {
                return "";
            }

            StringBuilder builder = new();
            foreach ((string speaker, string text) in history)
            {
                string label = speaker == "NPC" ? "NPC" : "Farmer";
                builder.Append(label).Append(": ").Append(text.Trim()).Append('\n');
            }

            string transcript = builder.ToString().Trim();
            if (transcript.Length > maxChars)
                transcript = "...(earlier conversation trimmed)...\n" + transcript.Substring(transcript.Length - maxChars);

            return transcript;
        }
    }
}
