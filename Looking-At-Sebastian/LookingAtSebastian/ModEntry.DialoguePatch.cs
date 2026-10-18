using HarmonyLib;
using StardewValley;

namespace LookingAtSebastian;

[HarmonyPatch(typeof(NPC), nameof(NPC.checkAction))]
internal static class NPCCheckActionPatch
{
    private static bool Prefix(NPC __instance, Farmer who, GameLocation l, ref bool __result)
    {
        ModEntry? mod = ModEntry.Instance;

        if (mod is null)
            return true;

        return mod.TryOpenPendingSebastianDialogue(__instance, who, ref __result);
    }
}