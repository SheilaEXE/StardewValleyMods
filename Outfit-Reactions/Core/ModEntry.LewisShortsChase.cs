using StardewValley;
using System;

namespace OutfitReactions;

public sealed partial class ModEntry
{
    private const string MayorShortsEntryId = "MayorsPurpleShorts";
    private const string MayorShortsHatEntryId = "MayorsPurpleShortsHat";

    private string GetLewisShortsChaseDialogueKey(LewisShortsSlot slot, string situation)
    {
        string entryId = slot switch
        {
            LewisShortsSlot.Pants => MayorShortsEntryId,
            LewisShortsSlot.Hat => MayorShortsHatEntryId,
            _ => ""
        };

        return string.IsNullOrWhiteSpace(entryId)
            ? ""
            : specialItemReactionService?.GetNpcScriptedDialogueKey(entryId, "Lewis", situation) ?? "";
    }

    private LewisShortsSlot GetEquippedLewisShortsSlot()
    {
        if (Game1.player == null || specialItemReactionService == null)
            return LewisShortsSlot.None;

        string pantsName = GetCurrentVanillaPantsName();
        if (!string.IsNullOrWhiteSpace(pantsName)
            && TryResolveSpecialItemCandidates(
                GetCurrentVanillaPantsSpecialItemCandidates(pantsName),
                "Pants",
                Game1.getCharacterFromName("Lewis"),
                wasRemoved: false,
                out SpecialItemNoticeInfo pantsNotice)
            && string.Equals(pantsNotice?.EntryId, MayorShortsEntryId, StringComparison.OrdinalIgnoreCase))
        {
            return LewisShortsSlot.Pants;
        }

        string visibleHatId = GetVisibleVanillaHatId();
        if (!string.IsNullOrWhiteSpace(visibleHatId))
        {
            string hatName = GetCurrentVanillaHatName();
            if (TryResolveSpecialItemCandidates(
                    GetCurrentVisibleVanillaHatSpecialItemCandidates(hatName),
                    "Hat",
                    Game1.getCharacterFromName("Lewis"),
                    wasRemoved: false,
                    out SpecialItemNoticeInfo hatNotice)
                && string.Equals(hatNotice?.EntryId, MayorShortsHatEntryId, StringComparison.OrdinalIgnoreCase))
            {
                return LewisShortsSlot.Hat;
            }
        }

        return LewisShortsSlot.None;
    }

    private bool ConfiscateEquippedLewisShorts(LewisShortsSlot expectedSlot)
    {
        Farmer player = Game1.player;
        if (player == null || expectedSlot == LewisShortsSlot.None || GetEquippedLewisShortsSlot() != expectedSlot)
            return false;

        try
        {
            switch (expectedSlot)
            {
                case LewisShortsSlot.Pants:
                    player.pantsItem.Value = null;
                    break;
                case LewisShortsSlot.Hat:
                    player.hat.Value = null;
                    break;
                default:
                    return false;
            }

            player.FarmerRenderer?.MarkSpriteDirty();
            vanillaClothingPollTimer = 0;
            Game1.playSound("dwop");

            if (DebugLog)
                Monitor.Log($"[LEWIS SHORTS CHASE] Confiscated equipped {expectedSlot} from the player.", StardewModdingAPI.LogLevel.Info);
            return true;
        }
        catch (Exception ex)
        {
            Monitor.Log("[LEWIS SHORTS CHASE] Could not confiscate the equipped shorts: " + ex.Message, StardewModdingAPI.LogLevel.Warn);
            return false;
        }
    }
}
