using StardewModdingAPI;
using StardewModdingAPI.Utilities;

namespace StayHydrated;

internal sealed class ModConfig
{
    public KeybindList WaterSinkButton { get; set; } = new(SButton.D5);
}
