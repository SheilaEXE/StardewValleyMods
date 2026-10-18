using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using StardewModdingAPI;
using StardewValley;
using TileMarker.Core;

namespace TileMarker.Api
{
    public sealed class TileMarkerApi : ITileMarkerApi
    {
        private readonly MarkedTileStore store;
        private readonly Action<string, string> openEditor;
        private readonly IMonitor monitor;
        private readonly Func<bool> mergeCompatibleCategories;

        public event EventHandler<TileMarksChangedEventArgs> TileMarksChanged
        {
            add => store.TileMarksChanged += value;
            remove => store.TileMarksChanged -= value;
        }

        internal TileMarkerApi(MarkedTileStore store, Action<string, string> openEditor, IMonitor monitor, Func<bool> mergeCompatibleCategories)
        {
            this.store = store;
            this.openEditor = openEditor;
            this.monitor = monitor;
            this.mergeCompatibleCategories = mergeCompatibleCategories;
        }

        public void RegisterCategory(string ownerModId, string category, string displayName)
        {
            if (!store.Register(ownerModId, category, displayName))
            {
                monitor.Log(
                    "[Tile Marker API] Ignored a category registration with an empty owner mod ID or category ID.",
                    LogLevel.Warn
                );
            }
        }

        public void RegisterCategoryWithSharedGroup(string ownerModId, string category, string displayName, string sharedGroup)
        {
            if (!store.Register(ownerModId, category, displayName, sharedGroup))
            {
                monitor.Log(
                    "[Tile Marker API] Ignored a shared category registration with an empty owner mod ID or category ID.",
                    LogLevel.Warn
                );
            }
        }

        public void OpenEditor(string ownerModId, string category)
        {
            if (!store.IsRegistered(ownerModId, category))
            {
                monitor.Log(
                    $"[Tile Marker API] Refused to open an unregistered category: {ownerModId ?? "<null>"}/{category ?? "<null>"}.",
                    LogLevel.Warn
                );
                return;
            }

            openEditor?.Invoke(ownerModId, category);
        }

        public IReadOnlyList<string> GetMarkedTileRanges(string ownerModId, string category, string locationName)
        {
            if (mergeCompatibleCategories?.Invoke() != true)
                return store.GetRanges(ownerModId, category, locationName);

            var tiles = new HashSet<Point>();
            foreach (var compatible in store.GetCompatibleCategories(ownerModId, category))
                tiles.UnionWith(store.GetExpandedTiles(compatible.OwnerModId, compatible.Category, locationName));

            return TileRangeCodec.Compress(tiles).AsReadOnly();
        }

        public bool IsTileMarked(string ownerModId, string category, GameLocation location, int x, int y)
        {
            if (location == null)
                return false;

            Point tile = new(x, y);
            if (mergeCompatibleCategories?.Invoke() == true)
            {
                foreach (var compatible in store.GetCompatibleCategories(ownerModId, category))
                {
                    if (IsTileMarkedForLocation(compatible.OwnerModId, compatible.Category, location, tile))
                        return true;
                }

                return false;
            }

            return IsTileMarkedForLocation(ownerModId, category, location, tile);
        }

        private bool IsTileMarkedForLocation(string ownerModId, string category, GameLocation location, Point tile)
        {
            if (store.IsTileMarked(ownerModId, category, location.NameOrUniqueName, tile))
                return true;

            return !string.Equals(location.NameOrUniqueName, location.Name, StringComparison.Ordinal)
                && store.IsTileMarked(ownerModId, category, location.Name, tile);
        }
    }
}
