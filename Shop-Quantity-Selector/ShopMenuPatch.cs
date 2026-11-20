using HarmonyLib;
using StardewValley.Menus;

namespace ShopQuantitySelector;

[HarmonyPatch(typeof(ShopMenu), nameof(ShopMenu.receiveLeftClick))]
internal static class ShopMenuPatch
{
    private static bool Prefix(ShopMenu __instance, int x, int y, bool playSound = true)
    {
        try
        {
            // Returning false skips the normal single-item purchase because the selector took over.
            return !ModEntry.Instance.TryOpenQuantitySelector(__instance, x, y);
        }
        catch (Exception ex)
        {
            ModEntry.Instance.Monitor.Log($"Could not open the quantity selector. The normal shop click will be used instead.\n{ex}", StardewModdingAPI.LogLevel.Error);
            return true;
        }
    }
}
