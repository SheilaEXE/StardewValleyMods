using StardewModdingAPI;
using StardewValley;
using StardewValley.Objects;
using System;
using System.Collections.Generic;
using System.Linq;

namespace OutfitReactions.Ai
{
    /// <summary>
    /// Supplies neutral, factual context for vanilla hats directly from the game's item data.
    /// The ??? mask alone retains its fourth-wall-free familiarity clue. The NPC profile,
    /// relationship, and scene still decide the tone of the reaction.
    /// </summary>
    internal sealed class SpecialHatReactionService
    {
        private readonly IMonitor monitor;

        public SpecialHatReactionService(IModHelper helper, IMonitor monitor)
        {
            this.monitor = monitor;
        }

        public string BuildContextForCurrentVanillaHat(Farmer farmer, string targetLanguage)
        {
            try
            {
                Hat hat = farmer?.hat?.Value;
                if (hat == null)
                    return "";

                return BuildNeutralHatContext(
                    hat.DisplayName,
                    hat.Name,
                    hat.ItemId,
                    hat.QualifiedItemId,
                    hat.getDescription(),
                    wasRemoved: false);
            }
            catch (Exception ex)
            {
                if (OutfitReactions.ModEntry.DebugLog)
                    monitor?.Log("[VANILLA HAT] Could not build factual hat context: " + ex.Message, LogLevel.Info);
                return "";
            }
        }

        public string BuildContextForRemovedHat(string removedHatId, string removedHatName, string targetLanguage)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(removedHatId) && string.IsNullOrWhiteSpace(removedHatName))
                    return "";

                Hat hat = TryCreateHat(removedHatId);
                return BuildNeutralHatContext(
                    removedHatName,
                    hat?.Name,
                    removedHatId,
                    hat?.QualifiedItemId,
                    hat?.getDescription(),
                    wasRemoved: true);
            }
            catch (Exception ex)
            {
                if (OutfitReactions.ModEntry.DebugLog)
                    monitor?.Log("[VANILLA HAT] Could not build removed-hat facts: " + ex.Message, LogLevel.Info);
                return "";
            }
        }

        private static Hat TryCreateHat(string itemId)
        {
            if (string.IsNullOrWhiteSpace(itemId))
                return null;

            try
            {
                string qualifiedId = itemId.Trim();
                if (!qualifiedId.StartsWith("(H)", StringComparison.OrdinalIgnoreCase))
                    qualifiedId = "(H)" + qualifiedId;

                return ItemRegistry.Create<Hat>(qualifiedId);
            }
            catch
            {
                return null;
            }
        }

        private static string BuildNeutralHatContext(
            string displayName,
            string internalName,
            string itemId,
            string qualifiedItemId,
            string description,
            bool wasRemoved)
        {
            string knownName = FirstNonEmpty(displayName, internalName, itemId, qualifiedItemId);
            if (string.IsNullOrWhiteSpace(knownName))
                return "";

            List<string> facts = new()
            {
                (wasRemoved ? "Vanilla hat just removed: " : "Current vanilla hat: ") + CleanForPrompt(knownName)
            };

            string cleanDescription = CleanForPrompt(description);
            if (!string.IsNullOrWhiteSpace(cleanDescription))
                facts.Add("official in-game description: " + cleanDescription);

            if (!wasRemoved && IsQuestionMarkMask(itemId, qualifiedItemId))
                facts.Add("special clue for this mask only: its face feels inexplicably familiar to this NPC, as though they have seen it somewhere but cannot place where. Express that recognition naturally in this NPC's own voice. This is familiarity with the face, not prior acquaintance with the farmer. Never identify the face as ConcernedApe, Eric Barone, a developer, or the game's creator, and do not break the fourth wall");

            if (wasRemoved)
                facts.Add("current state: the farmer is now bare-headed; these facts describe the hat that is no longer being worn");

            return string.Join("; ", facts.Where(fact => !string.IsNullOrWhiteSpace(fact)));
        }

        private static bool IsQuestionMarkMask(string itemId, string qualifiedItemId)
        {
            return string.Equals(itemId?.Trim(), "92", StringComparison.OrdinalIgnoreCase)
                || string.Equals(itemId?.Trim(), "(H)92", StringComparison.OrdinalIgnoreCase)
                || string.Equals(qualifiedItemId?.Trim(), "(H)92", StringComparison.OrdinalIgnoreCase);
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
