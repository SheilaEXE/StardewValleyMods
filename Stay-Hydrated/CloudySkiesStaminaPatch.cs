using System;
using System.Reflection;
using HarmonyLib;
using StardewModdingAPI;
using StardewValley;

namespace StayHydrated;

/// <summary>Prevents Cloudy Skies' direct heatwave stamina drain while hydration is active.</summary>
internal static class CloudySkiesStaminaPatch
{
    private const string EffectTypeName = "Leclair.Stardew.CloudySkies.Effects.ModifyStaminaEffect";
    private static FieldInfo? AmountField;

    internal static void Apply(Harmony harmony, IMonitor monitor)
    {
        Type? effectType = AccessTools.TypeByName(EffectTypeName);
        MethodInfo? updateMethod = effectType is null ? null : AccessTools.Method(effectType, "Update");
        AmountField = effectType is null ? null : AccessTools.Field(effectType, "Amount");

        if (updateMethod is null || AmountField is null)
        {
            monitor.Log("Cloudy Skies' stamina effect wasn't found. Heatwave stamina protection will stay inactive.", LogLevel.Trace);
            return;
        }

        harmony.Patch(updateMethod, prefix: new HarmonyMethod(typeof(CloudySkiesStaminaPatch), nameof(BeforeUpdate)));
        monitor.Log("Enabled hydration protection against Cloudy Skies' direct heatwave stamina drain.", LogLevel.Trace);
    }

    private static bool BeforeUpdate(object __instance)
    {
        if (!Context.IsWorldReady || Game1.player is null || AmountField is null)
            return true;

        int amount = (int)(AmountField.GetValue(__instance) ?? 0);
        if (amount >= 0 || !ModEntry.IsHeatwaveActive())
            return true;

        return !ModEntry.IsHydrated(Game1.player);
    }
}
