using System;
using System.Linq;
using System.Reflection;
using System.Text;
using HarmonyLib;
using StardewModdingAPI;
using StardewValley;
using StardewValley.Menus;

namespace StayHydrated;

/// <summary>Adds the freshness level to both water item tooltips.</summary>
[HarmonyPatch]
internal static class WaterTooltipPatch
{
    private const string CupItemId = "SiL.IcedWaterCup";
    private const string BottleItemId = "SiL.IcedWaterBottle";
    private const int TimedEffectLineIndex = 12;

    private static ITranslationHelper Translation = null!;

    internal static void Initialize(ITranslationHelper translation)
    {
        Translation = translation;
    }

    private static MethodBase TargetMethod()
    {
        return typeof(IClickableMenu)
            .GetMethods(BindingFlags.Public | BindingFlags.Static)
            .Single(method =>
            {
                if (method.Name != nameof(IClickableMenu.drawHoverText))
                    return false;

                ParameterInfo[] parameters = method.GetParameters();
                return parameters.Length > 1 && parameters[1].ParameterType == typeof(StringBuilder);
            });
    }

    private static void Prefix(Item? hoveredItem, ref string[]? buffIconsToDisplay)
    {
        int freshness = hoveredItem?.ItemId switch
        {
            CupItemId => 1,
            BottleItemId => 2,
            _ => 0
        };

        if (freshness == 0)
            return;

        if (buffIconsToDisplay is null)
            buffIconsToDisplay = Enumerable.Repeat("0", 13).ToArray();
        else if (buffIconsToDisplay.Length < 13)
            Array.Resize(ref buffIconsToDisplay, 13);

        for (int index = 0; index < buffIconsToDisplay.Length; index++)
            buffIconsToDisplay[index] ??= index == TimedEffectLineIndex ? "" : "0";

        buffIconsToDisplay[TimedEffectLineIndex] = Translation
            .Get("tooltip.freshness", new { value = freshness })
            .ToString();
    }
}
