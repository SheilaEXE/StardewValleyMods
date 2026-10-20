using HarmonyLib;
using StardewModdingAPI;
using StardewModdingAPI.Events;
using StardewValley;

namespace StayHydrated;

public sealed class ModEntry : Mod
{
    internal const string CupHydrationBuffId = "NatrollEXE.StayHydrated_Cup";
    internal const string BottleHydrationBuffId = "NatrollEXE.StayHydrated_Bottle";
    internal const string WeatherWondersHotBuffId = "Kana.WeatherWonders_Hot";
    internal const string WeatherWondersHeatwaveId = "Kana.WeatherWonders_Heatwave";

    private ModConfig Config = null!;
    private Harmony HarmonyInstance = null!;

    public override void Entry(IModHelper helper)
    {
        Config = helper.ReadConfig<ModConfig>();
        WaterTooltipPatch.Initialize(helper.Translation);
        HarmonyInstance = new Harmony(ModManifest.UniqueID);
        HarmonyInstance.PatchAll();
        helper.Events.GameLoop.GameLaunched += OnGameLaunched;
        helper.Events.GameLoop.UpdateTicked += OnUpdateTicked;
    }

    private void OnGameLaunched(object? sender, GameLaunchedEventArgs e)
    {
        CloudySkiesStaminaPatch.Apply(HarmonyInstance, Monitor);
        ToolStaminaPatch.Apply(HarmonyInstance, Monitor);

        IGenericModConfigMenuApi? configMenu = Helper.ModRegistry.GetApi<IGenericModConfigMenuApi>("spacechase0.GenericModConfigMenu");
        if (configMenu is not null)
        {
            configMenu.Register(
                ModManifest,
                reset: () => Config = new ModConfig(),
                save: () => Helper.WriteConfig(Config));

            configMenu.AddKeybindList(
                ModManifest,
                getValue: () => Config.WaterSinkButton,
                setValue: value => Config.WaterSinkButton = value,
                name: () => Helper.Translation.Get("config.water-sink-button.name").ToString(),
                tooltip: () => Helper.Translation.Get("config.water-sink-button.description").ToString());
        }

        IContentPatcherApi? contentPatcher = Helper.ModRegistry.GetApi<IContentPatcherApi>("Pathoschild.ContentPatcher");
        contentPatcher?.RegisterToken(
            ModManifest,
            "WaterSinkButton",
            () => new[] { Config.WaterSinkButton?.ToString() ?? "D5" });
    }

    internal static bool IsHydrated(Farmer player)
    {
        return player.buffs.IsApplied(CupHydrationBuffId)
            || player.buffs.IsApplied(BottleHydrationBuffId);
    }

    internal static int GetFreshnessLevel(Farmer player)
    {
        if (player.buffs.IsApplied(BottleHydrationBuffId))
            return 2;

        return player.buffs.IsApplied(CupHydrationBuffId) ? 1 : 0;
    }

    internal static bool IsHeatwaveActive()
    {
        if (!Context.IsWorldReady || Game1.currentLocation is null)
            return false;

        string contextId = Game1.currentLocation.GetLocationContextId();
        return Game1.netWorldState.Value.GetWeatherForLocation(contextId).Weather == WeatherWondersHeatwaveId;
    }

    private void OnUpdateTicked(object? sender, UpdateTickedEventArgs e)
    {
        if (!Context.IsWorldReady || Game1.player is null)
            return;

        bool hasCupHydration = Game1.player.buffs.IsApplied(CupHydrationBuffId);
        bool hasBottleHydration = Game1.player.buffs.IsApplied(BottleHydrationBuffId);

        // Keep one hydration icon if both drinks are consumed. The longer bottle effect wins.
        if (hasCupHydration && hasBottleHydration)
        {
            Game1.player.buffs.Remove(CupHydrationBuffId);
            hasCupHydration = false;
        }

        // Remove a heat debuff which was already active when the player drank the water.
        // Further applications are blocked by BuffManagerApplyPatch until hydration expires.
        if ((hasCupHydration || hasBottleHydration)
            && Game1.player.buffs.IsApplied(WeatherWondersHotBuffId))
        {
            Game1.player.buffs.Remove(WeatherWondersHotBuffId);
        }
    }
}
