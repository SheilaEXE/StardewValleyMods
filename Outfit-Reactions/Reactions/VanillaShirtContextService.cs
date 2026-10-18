using StardewModdingAPI;
using StardewValley;
using StardewValley.Objects;
using System;
using System.Collections.Generic;
using System.Linq;

namespace OutfitReactions.Ai
{
    /// <summary>Builds neutral factual context for the currently equipped vanilla shirt.</summary>
    internal sealed class VanillaShirtContextService
    {
        private readonly IMonitor monitor;

        public VanillaShirtContextService(IMonitor monitor)
        {
            this.monitor = monitor;
        }

        public string BuildContextForCurrentVanillaShirt(Farmer farmer)
        {
            try
            {
                Clothing shirt = farmer?.shirtItem?.Value;
                if (shirt == null)
                    return "";

                string name = FirstNonEmpty(shirt.DisplayName, shirt.Name, shirt.ItemId, shirt.QualifiedItemId);
                if (string.IsNullOrWhiteSpace(name))
                    return "";

                List<string> facts = new()
                {
                    "Current vanilla shirt: " + CleanForPrompt(name)
                };

                string description = CleanForPrompt(shirt.getDescription());
                if (!string.IsNullOrWhiteSpace(description))
                    facts.Add("official in-game description: " + description);

                return string.Join("; ", facts.Where(fact => !string.IsNullOrWhiteSpace(fact)));
            }
            catch (Exception ex)
            {
                if (OutfitReactions.ModEntry.DebugLog)
                    monitor?.Log("[VANILLA SHIRT] Could not build factual shirt context: " + ex.Message, LogLevel.Info);
                return "";
            }
        }

        private static string FirstNonEmpty(params string[] values)
        {
            return values?.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value))?.Trim() ?? "";
        }

        private static string CleanForPrompt(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return "";

            return string.Join(" ", value
                .Replace('\r', ' ')
                .Replace('\n', ' ')
                .Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries));
        }
    }
}
