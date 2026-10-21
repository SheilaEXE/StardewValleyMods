using StardewModdingAPI;
using TileMarker.ContentPatcherInterop;

namespace TileMarker.Config
{
    /// <summary>Mirrors the GMCM API interface shape already used by your other mods.</summary>
    public interface IGenericModConfigMenuApi
    {
        void Register(IManifest mod, System.Action reset, System.Action save, bool titleScreenOnly = false);
        void Unregister(IManifest mod);
        void AddKeybindList(IManifest mod, System.Func<StardewModdingAPI.Utilities.KeybindList> getValue, System.Action<StardewModdingAPI.Utilities.KeybindList> setValue, System.Func<string> name, System.Func<string> tooltip = null, string fieldId = null);
        void AddNumberOption(IManifest mod, System.Func<int> getValue, System.Action<int> setValue, System.Func<string> name, System.Func<string> tooltip = null, int? min = null, int? max = null, int? interval = null, System.Func<int, string> formatValue = null, string fieldId = null);
        void AddBoolOption(IManifest mod, System.Func<bool> getValue, System.Action<bool> setValue, System.Func<string> name, System.Func<string> tooltip = null, string fieldId = null);
    }

    internal static class ModConfigMenu
    {
        public static void Register(
            ModEntry mod,
            IGenericModConfigMenuApi gmcm,
            ContentPatcherIntegration contentPatcherIntegration)
        {
            gmcm.Register(
                mod: mod.ModManifest,
                reset: () =>
                {
                    mod.Config = new ModConfig();
                    contentPatcherIntegration.RefreshConfiguredButtons();
                },
                save: mod.TryWriteConfig
            );

            gmcm.AddKeybindList(
                mod: mod.ModManifest,
                getValue: () => mod.Config.OpenEditorKey,
                setValue: value => mod.Config.OpenEditorKey = value,
                name: () => mod.Helper.Translation.Get("gmcm.option.open-editor-key.name").ToString()
            );

            gmcm.AddNumberOption(
                mod: mod.ModManifest,
                getValue: () => mod.Config.OverlayOpacityPercent,
                setValue: value => mod.Config.OverlayOpacityPercent = value,
                name: () => mod.Helper.Translation.Get("gmcm.option.overlay-opacity.name").ToString(),
                min: 10,
                max: 90
            );

            gmcm.AddBoolOption(
                mod: mod.ModManifest,
                getValue: () => mod.Config.EnableLeftClickBrush,
                setValue: value => mod.Config.EnableLeftClickBrush = value,
                name: () => mod.Helper.Translation.Get("gmcm.option.left-click-brush.name").ToString(),
                tooltip: () => mod.Helper.Translation.Get("gmcm.option.left-click-brush.tooltip").ToString()
            );

            gmcm.AddBoolOption(
                mod: mod.ModManifest,
                getValue: () => mod.Config.MergeCompatibleCategories,
                setValue: value => mod.Config.MergeCompatibleCategories = value,
                name: () => mod.Helper.Translation.Get("gmcm.option.merge-compatible-categories.name").ToString(),
                tooltip: () => mod.Helper.Translation.Get("gmcm.option.merge-compatible-categories.tooltip").ToString()
            );

            foreach (ConfigurableInteractionButton option in contentPatcherIntegration.ConfigurableButtons)
            {
                ConfigurableInteractionButton captured = option;
                gmcm.AddKeybindList(
                    mod: mod.ModManifest,
                    getValue: () => contentPatcherIntegration.GetConfiguredButton(captured),
                    setValue: value => contentPatcherIntegration.SetConfiguredButton(captured, value),
                    name: () => captured.DisplayName,
                    tooltip: string.IsNullOrWhiteSpace(captured.Tooltip)
                        ? null
                        : () => captured.Tooltip,
                    fieldId: $"cp-button-{captured.ConfigKey.Replace('/', '-')}"
                );
            }
        }
    }
}
