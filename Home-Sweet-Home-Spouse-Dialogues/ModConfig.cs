using StardewModdingAPI;
using StardewModdingAPI.Utilities;

namespace HomeSweetHomeSpouseDialogues
{
    public class ModConfig
    {
        public bool EnableMod { get; set; } = true;

        // Exposed to Content Patcher as a token; the sink interaction itself remains in the CP pack.
        public KeybindList WaterSinkButton { get; set; } = new(SButton.D5);

        // Mesmo horário que estava no Attentive Spouse.
        public int DayDialogueStartTime { get; set; } = 1400;
        public int DayDialogueEndTime { get; set; } = 1800;

        public int NightDialogueStartTime { get; set; } = 2000;
        public int NightDialogueEndTime { get; set; } = 2200;

        public bool EnableHomeDayDialogues { get; set; } = true;
        public bool EnableHomeNightDialogues { get; set; } = true;
        public bool EnableRainDayDialogues { get; set; } = true;
    }
}
