using HarmonyLib;
using StardewModdingAPI;
using StardewValley;
using StardewValley.Buffs;

namespace StayHydrated;

/// <summary>Prevents Weather Wonders from reapplying intense heat while hydration is active.</summary>
[HarmonyPatch(typeof(BuffManager), nameof(BuffManager.Apply))]
internal static class BuffManagerApplyPatch
{
    [HarmonyPrefix]
    private static bool BeforeApply(BuffManager __instance, Buff buff)
    {
        if (buff?.id != ModEntry.WeatherWondersHotBuffId)
            return true;

        Farmer? player = Game1.player;
        if (!Context.IsWorldReady || player is null || !ReferenceEquals(__instance, player.buffs))
            return true;

        return !ModEntry.IsHydrated(player);
    }
}
