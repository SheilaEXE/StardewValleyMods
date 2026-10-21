using StardewModdingAPI;
using StardewModdingAPI.Utilities;

namespace StayHydrated;

internal sealed class ModConfig
{
    public KeybindList WaterSinkButton { get; set; } = new(SButton.D5);
    public int GlassStaminaSavingsPercent { get; set; } = 5;
    public int BottleStaminaSavingsPercent { get; set; } = 10;
}
