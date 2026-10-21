using System.Collections.Generic;
using StardewModdingAPI;
using StardewModdingAPI.Utilities;

namespace TileMarker.Config
{
    public class ModConfig
    {
        public KeybindList OpenEditorKey { get; set; } = KeybindList.Parse("OemPipe");

        // How opaque the add/remove preview squares are (0-100).
        public int OverlayOpacityPercent { get; set; } = 45;

        // Lets touchscreens and left-button-only environments paint by dragging.
        public bool EnableLeftClickBrush { get; set; } = false;

        // Categories remain stored separately. When enabled, categories that deliberately
        // register the same shared group are read as one union by consuming mods.
        public bool MergeCompatibleCategories { get; set; } = false;

        // Per-category overrides for Content Patcher interactions that opt into Tile Marker's GMCM.
        public Dictionary<string, string> InteractionButtons { get; set; } = new();
    }
}
