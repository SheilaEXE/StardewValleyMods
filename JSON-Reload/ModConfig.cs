using StardewModdingAPI;
using StardewModdingAPI.Utilities;

namespace JsonReload;

internal sealed class ModConfig
{
    public KeybindList ReloadKey { get; set; } = new(SButton.F6);

    public bool ShowHudMessages { get; set; } = true;
}
