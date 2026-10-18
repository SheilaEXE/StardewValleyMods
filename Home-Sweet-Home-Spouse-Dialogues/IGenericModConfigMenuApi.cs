using System;
using StardewModdingAPI;
using StardewModdingAPI.Utilities;

namespace HomeSweetHomeSpouseDialogues
{
    /// <summary>Minimal Generic Mod Config Menu API surface used by this mod.</summary>
    public interface IGenericModConfigMenuApi
    {
        void Register(IManifest mod, Action reset, Action save, bool titleScreenOnly = false);

        void AddKeybindList(
            IManifest mod,
            Func<KeybindList> getValue,
            Action<KeybindList> setValue,
            Func<string> name,
            Func<string> tooltip = null,
            string fieldId = null);
    }
}
