using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using StardewModdingAPI;
using StardewValley;
using StardewValley.Tools;

namespace StayHydrated;

/// <summary>Tracks vanilla tool execution so only tool-related stamina costs receive hydration savings.</summary>
internal static class ToolStaminaPatch
{
    private static readonly Type[] DoFunctionParameters =
    {
        typeof(GameLocation),
        typeof(int),
        typeof(int),
        typeof(int),
        typeof(Farmer)
    };

    [ThreadStatic]
    private static int ActiveDepth;

    [ThreadStatic]
    private static Farmer? ActiveFarmer;

    internal static void Apply(Harmony harmony, IMonitor monitor)
    {
        IEnumerable<MethodInfo> methods = typeof(Tool).Assembly
            .GetTypes()
            .Where(type => typeof(Tool).IsAssignableFrom(type))
            .Select(type => type.GetMethod(
                nameof(Tool.DoFunction),
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly,
                binder: null,
                types: DoFunctionParameters,
                modifiers: null))
            .Where(method => method is not null && !method.IsAbstract)
            .Cast<MethodInfo>()
            .Distinct();

        HarmonyMethod prefix = new(typeof(ToolStaminaPatch), nameof(BeforeDoFunction));
        HarmonyMethod finalizer = new(typeof(ToolStaminaPatch), nameof(AfterDoFunction));
        int patchedCount = 0;

        foreach (MethodInfo method in methods)
        {
            harmony.Patch(method, prefix: prefix, finalizer: finalizer);
            patchedCount++;
        }

        monitor.Log($"Enabled hydration stamina savings for {patchedCount} vanilla tool actions.", LogLevel.Trace);
    }

    internal static bool IsActiveFor(Farmer farmer)
    {
        return ActiveDepth > 0 && ReferenceEquals(ActiveFarmer, farmer);
    }

    private static void BeforeDoFunction(object[] __args)
    {
        Farmer? farmer = __args.Length > 4 ? __args[4] as Farmer : null;
        if (ActiveDepth == 0)
            ActiveFarmer = farmer;

        ActiveDepth++;
    }

    private static Exception? AfterDoFunction(Exception? __exception)
    {
        if (ActiveDepth > 0)
            ActiveDepth--;

        if (ActiveDepth == 0)
            ActiveFarmer = null;

        return __exception;
    }
}

/// <summary>Reduces stamina decreases made while a vanilla tool action is running.</summary>
[HarmonyPatch(typeof(Farmer), nameof(Farmer.Stamina), MethodType.Setter)]
internal static class FarmerStaminaPatch
{
    private static void Prefix(Farmer __instance, ref float value)
    {
        if (!ReferenceEquals(__instance, Game1.player)
            || !ToolStaminaPatch.IsActiveFor(__instance)
            || value >= __instance.Stamina)
        {
            return;
        }

        int freshness = ModEntry.GetFreshnessLevel(__instance);
        float reduction = freshness switch
        {
            1 => 0.05f,
            2 => 0.10f,
            _ => 0f
        };

        if (reduction <= 0f)
            return;

        float staminaCost = __instance.Stamina - value;
        value += staminaCost * reduction;
    }
}
