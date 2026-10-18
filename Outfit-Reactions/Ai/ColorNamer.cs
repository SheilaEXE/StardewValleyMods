using Microsoft.Xna.Framework;

namespace OutfitReactions.Ai
{
    /// <summary>
    /// Single source of truth for turning an exact RGB color into a simple, dialogue-friendly
    /// color name (e.g. "light pink", "auburn"). Used by both the hair/hat pixel reader
    /// (OutfitVisionService) and the Fashion Sense tint reader (FashionSenseVisualService),
    /// so the palette only needs to be edited in ONE place.
    ///
    /// To add a color: add one line to the palette below with a natural name and its RGB.
    /// Keep colors reasonably spaced apart so they don't "steal" each other's matches.
    /// </summary>
    internal static class ColorNamer
    {
        private static readonly (string Name, Color Value)[] Palette =
        {
            // Neutrals
            ("black", new Color(15, 15, 18)),
            ("dark gray", new Color(70, 70, 75)),
            ("gray", new Color(130, 130, 135)),
            ("light gray", new Color(195, 195, 200)),
            ("white", new Color(250, 250, 250)),
            ("cream", new Color(245, 235, 200)),
            // Reds / pinks
            ("dark red", new Color(130, 25, 30)),
            ("red", new Color(220, 35, 35)),
            ("crimson", new Color(200, 30, 80)),
            ("hot pink", new Color(240, 55, 140)),
            ("pink", new Color(255, 120, 190)),
            ("light pink", new Color(250, 200, 215)),
            ("rose", new Color(215, 110, 120)),
            // Oranges / browns / skin
            ("orange", new Color(240, 130, 30)),
            ("peach", new Color(255, 205, 165)),
            ("gold", new Color(220, 170, 45)),
            ("brown", new Color(115, 70, 40)),
            ("light brown", new Color(175, 130, 90)),
            ("tan", new Color(215, 185, 145)),
            ("auburn", new Color(165, 60, 35)),
            // Yellows / blonde
            ("yellow", new Color(245, 220, 55)),
            ("blonde", new Color(225, 200, 120)),
            // Greens
            ("dark green", new Color(30, 90, 45)),
            ("green", new Color(60, 175, 70)),
            ("light green", new Color(165, 220, 145)),
            ("mint", new Color(160, 235, 205)),
            ("olive", new Color(120, 130, 55)),
            ("teal", new Color(35, 165, 170)),
            // Blues
            ("navy", new Color(30, 40, 100)),
            ("blue", new Color(50, 120, 220)),
            ("light blue", new Color(140, 200, 240)),
            ("cyan", new Color(70, 220, 235)),
            // Purples
            ("purple", new Color(150, 80, 210)),
            ("light purple", new Color(195, 155, 230)),
            ("lavender", new Color(220, 205, 245)),
            ("magenta", new Color(205, 50, 195))
        };

        /// <summary>Returns the palette name closest to the given color (squared RGB distance).</summary>
        public static string ClosestSimpleColorName(Color color)
        {
            string best = "color";
            double bestDistance = double.MaxValue;

            foreach ((string name, Color target) in Palette)
            {
                int dr = color.R - target.R;
                int dg = color.G - target.G;
                int db = color.B - target.B;
                double distance = dr * dr + dg * dg + db * db;
                if (distance < bestDistance)
                {
                    bestDistance = distance;
                    best = name;
                }
            }

            return best;
        }
    }
}
