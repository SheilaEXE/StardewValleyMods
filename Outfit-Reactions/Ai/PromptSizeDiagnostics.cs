using StardewModdingAPI;
using System;
using System.Collections.Generic;

namespace OutfitReactions.Ai
{
    internal sealed class PromptSizeBreakdown
    {
        private readonly List<KeyValuePair<string, int>> blocks = new();

        public IReadOnlyList<KeyValuePair<string, int>> Blocks => blocks;

        public void Add(string name, int characters)
        {
            blocks.Add(new KeyValuePair<string, int>(name, Math.Max(0, characters)));
        }
    }

    internal static class PromptSizeDiagnostics
    {
        public static void Log(
            IMonitor monitor,
            string promptKind,
            string npcName,
            string provider,
            string model,
            int totalCharacters,
            bool hasVisionImage,
            params KeyValuePair<string, int>[] blocks)
        {
            if (!OutfitReactions.ModEntry.DebugLog || monitor == null)
                return;

            monitor.Log(
                $"[AI PROMPT SIZE] kind={promptKind}, npc={npcName ?? "?"}, provider={provider ?? "?"}, model={model ?? "?"}, totalChars={totalCharacters}, estimatedTextTokens~={EstimateTextTokens(totalCharacters)}, visionAttached={hasVisionImage}.",
                LogLevel.Info);

            if (blocks == null)
                return;

            foreach (KeyValuePair<string, int> block in blocks)
            {
                int characters = Math.Max(0, block.Value);
                double percentage = totalCharacters > 0 ? characters * 100.0 / totalCharacters : 0.0;
                monitor.Log(
                    $"[AI PROMPT SIZE]   {block.Key}: chars={characters}, estimatedTextTokens~={EstimateTextTokens(characters)}, share={percentage:0.0}%.",
                    LogLevel.Info);
            }
        }

        private static int EstimateTextTokens(int characters)
        {
            // Provider-neutral approximation for comparing blocks. The provider tokenizer
            // remains authoritative, especially when an image is attached.
            return (int)Math.Ceiling(Math.Max(0, characters) / 4.0);
        }
    }
}
