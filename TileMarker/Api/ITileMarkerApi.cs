using System;
using System.Collections.Generic;
using StardewValley;

namespace TileMarker.Api
{
    /// <summary>
    /// Public API for Tile Marker. Get it via
    /// <c>Helper.ModRegistry.GetApi&lt;ITileMarkerApi&gt;("NatrollEXE.TileMarker")</c> in your
    /// GameLaunched handler.
    /// </summary>
    public interface ITileMarkerApi
    {
        /// <summary>
        /// Call once in GameLaunched so Tile Marker knows this category exists and can offer it
        /// in its picker when its keybind is pressed. Registering is what makes your mod show up —
        /// Tile Marker doesn't know or care what the marked tiles are used for.
        /// </summary>
        /// <param name="ownerModId">Your mod's UniqueID (e.g. "NatrollEXE.LotsOfKisses").</param>
        /// <param name="category">A short id for this specific kind of marking (e.g. "VisionIgnored").
        /// Lets one mod register more than one independent tile set if needed.</param>
        /// <param name="displayName">Shown to the player in Tile Marker's category picker.</param>
        void RegisterCategory(string ownerModId, string category, string displayName);

        /// <summary>
        /// Registers a category that can share its effective marked tiles with other categories
        /// using the same group ID when the player enables merging. Saved selections remain separate.
        /// </summary>
        void RegisterCategoryWithSharedGroup(string ownerModId, string category, string displayName, string sharedGroup);

        /// <summary>
        /// Opens the tile editor directly for this owner/category on the player's current location,
        /// skipping the picker. Useful if your own mod wants its own keybind/GMCM button as a
        /// shortcut into Tile Marker instead of making the player use Tile Marker's picker.
        /// </summary>
        void OpenEditor(string ownerModId, string category);

        /// <summary>
        /// Marked tiles for this owner/category/location, in the same "x,y" / "x1-x2,y1-y2" range
        /// string format used by Lots of Kisses' VisionIgnoredTiles config — copy that parser as-is.
        /// Returns an empty list if nothing is marked (never null).
        /// </summary>
        IReadOnlyList<string> GetMarkedTileRanges(string ownerModId, string category, string locationName);

        /// <summary>Convenience check against a specific tile, without expanding ranges yourself.</summary>
        bool IsTileMarked(string ownerModId, string category, GameLocation location, int x, int y);

        /// <summary>Raised after the player finishes editing and the marks for an owner/category change.</summary>
        event EventHandler<TileMarksChangedEventArgs> TileMarksChanged;
    }

    public class TileMarksChangedEventArgs : EventArgs
    {
        public string OwnerModId { get; }
        public string Category { get; }
        public string LocationName { get; }

        public TileMarksChangedEventArgs(string ownerModId, string category, string locationName)
        {
            OwnerModId = ownerModId;
            Category = category;
            LocationName = locationName;
        }
    }
}
