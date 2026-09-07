using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using StardewModdingAPI;
using StardewValley;
using TileMarker.Api;

namespace TileMarker.Core
{
    /// <summary>
    /// Owner -> Category -> Location -> range strings. Persisted per-save via SMAPI's save data
    /// API, since which tiles make sense depends on that save's installed map mods.
    /// </summary>
    internal class MarkedTileStore
    {
        private const string SaveDataKey = "marked-tiles";
        private const string ImmediateDataKey = "marked-tiles-immediate-v1";

        private readonly IModHelper helper;
        private readonly IMonitor monitor;

        private Dictionary<string, Dictionary<string, Dictionary<string, List<string>>>> data = new();
        private readonly Dictionary<(string OwnerModId, string Category, string LocationName), HashSet<Point>> expandedTileCache = new();
        private readonly Dictionary<(string OwnerModId, string Category, string LocationName), IReadOnlyList<string>> rangeViewCache = new();

        // Registered categories, so the picker menu has something to show. Not persisted —
        // consuming mods re-register every GameLaunched.
        public List<(string OwnerModId, string Category, string DisplayName)> RegisteredCategories { get; } = new();
        private readonly Dictionary<(string OwnerModId, string Category), string> sharedGroups = new();
        private readonly Dictionary<(string OwnerModId, string Category), string> registrationSources = new();

        /// <summary>Changes whenever registrations or marked-tile data change.</summary>
        internal long Revision { get; private set; }

        public event EventHandler<TileMarksChangedEventArgs> TileMarksChanged;

        public MarkedTileStore(IModHelper helper, IMonitor monitor)
        {
            this.helper = helper;
            this.monitor = monitor;
        }

        public bool Register(string ownerModId, string category, string displayName, string sharedGroup = null)
        {
            return Register(ownerModId, category, displayName, sharedGroup, "Api", replaceOtherSources: true);
        }

        internal bool RegisterFromContentPatcher(string ownerModId, string category, string displayName, string sharedGroup = null)
        {
            return Register(ownerModId, category, displayName, sharedGroup, "ContentPatcher", replaceOtherSources: false);
        }

        private bool Register(
            string ownerModId,
            string category,
            string displayName,
            string sharedGroup,
            string source,
            bool replaceOtherSources)
        {
            if (string.IsNullOrWhiteSpace(ownerModId) || string.IsNullOrWhiteSpace(category))
                return false;

            ownerModId = ownerModId.Trim();
            category = category.Trim();
            var key = (ownerModId, category);

            if (!replaceOtherSources
                && registrationSources.TryGetValue(key, out string existingSource)
                && !string.Equals(existingSource, source, StringComparison.Ordinal))
            {
                return false;
            }

            RegisteredCategories.RemoveAll(r => r.OwnerModId == ownerModId && r.Category == category);
            RegisteredCategories.Add((ownerModId, category, string.IsNullOrWhiteSpace(displayName) ? category : displayName));

            if (string.IsNullOrWhiteSpace(sharedGroup))
                sharedGroups.Remove(key);
            else
                sharedGroups[key] = sharedGroup.Trim();

            registrationSources[key] = source;
            Touch();

            return true;
        }

        internal void UnregisterContentPatcherCategory(string ownerModId, string category)
        {
            var key = (ownerModId, category);
            if (!registrationSources.TryGetValue(key, out string source)
                || !string.Equals(source, "ContentPatcher", StringComparison.Ordinal))
            {
                return;
            }

            RegisteredCategories.RemoveAll(r => r.OwnerModId == ownerModId && r.Category == category);
            registrationSources.Remove(key);
            sharedGroups.Remove(key);
            Touch();
        }

        public IReadOnlyList<(string OwnerModId, string Category)> GetCompatibleCategories(string ownerModId, string category)
        {
            var ownKey = (ownerModId, category);
            if (!sharedGroups.TryGetValue(ownKey, out string sharedGroup) || string.IsNullOrWhiteSpace(sharedGroup))
                return new[] { ownKey };

            var compatible = new List<(string OwnerModId, string Category)>();
            foreach (var registration in RegisteredCategories)
            {
                var candidate = (registration.OwnerModId, registration.Category);
                if (sharedGroups.TryGetValue(candidate, out string candidateGroup)
                    && string.Equals(sharedGroup, candidateGroup, StringComparison.OrdinalIgnoreCase))
                {
                    compatible.Add(candidate);
                }
            }

            if (compatible.Count == 0)
                compatible.Add(ownKey);

            return compatible;
        }

        public bool IsRegistered(string ownerModId, string category)
        {
            return !string.IsNullOrWhiteSpace(ownerModId)
                && !string.IsNullOrWhiteSpace(category)
                && RegisteredCategories.Exists(r =>
                    r.OwnerModId == ownerModId && r.Category == category
                );
        }

        public string GetDisplayName(string ownerModId, string category)
        {
            var registration = RegisteredCategories.Find(r =>
                r.OwnerModId == ownerModId && r.Category == category
            );

            return string.IsNullOrWhiteSpace(registration.DisplayName)
                ? category
                : registration.DisplayName;
        }

        public void LoadForCurrentSave()
        {
            if (!Context.IsMainPlayer)
            {
                ClearLoadedData();
                monitor.Log("[Tile Marker] A farmhand skipped direct save-data loading and is waiting for the host's tile snapshot.", LogLevel.Trace);
                return;
            }

            bool loadedImmediateData = false;

            try
            {
                TileMarkerImmediateData immediateData = helper.Data.ReadGlobalData<TileMarkerImmediateData>(ImmediateDataKey);
                if (immediateData?.Saves != null
                    && immediateData.Saves.TryGetValue(GetCurrentSaveKey(), out var savedForCurrentSlot)
                    && savedForCurrentSlot != null)
                {
                    data = CloneData(savedForCurrentSlot);
                    loadedImmediateData = true;
                }
            }
            catch (Exception ex)
            {
                monitor.Log($"[Tile Marker] Could not read the immediate tile data; trying the embedded save copy: {ex}", LogLevel.Warn);
            }

            if (!loadedImmediateData)
            {
                try
                {
                    data = helper.Data.ReadSaveData<Dictionary<string, Dictionary<string, Dictionary<string, List<string>>>>>(SaveDataKey)
                           ?? new Dictionary<string, Dictionary<string, Dictionary<string, List<string>>>>();
                }
                catch (Exception ex)
                {
                    data = new Dictionary<string, Dictionary<string, Dictionary<string, List<string>>>>();
                    monitor.Log(
                        $"[Tile Marker] Could not load either saved tile copy. The editor will use an empty in-memory set for this session: {ex}",
                        LogLevel.Error
                    );
                }
            }

            int removedEntries = SanitizeLoadedData();
            if (removedEntries > 0)
            {
                monitor.Log(
                    $"[Tile Marker] Ignored {removedEntries} invalid saved tile entr{(removedEntries == 1 ? "y" : "ies")} while loading.",
                    LogLevel.Warn
                );
            }

            // Migrate a legacy embedded save copy into the immediate per-save store.
            if (!loadedImmediateData && Context.IsMainPlayer && data.Count > 0)
            {
                try
                {
                    WriteImmediateData(data);
                    monitor.Log("[Tile Marker] Migrated embedded tile data to immediate per-save storage.", LogLevel.Trace);
                }
                catch (Exception ex)
                {
                    monitor.Log($"[Tile Marker] Could not migrate embedded tile data to immediate storage: {ex}", LogLevel.Warn);
                }
            }

            expandedTileCache.Clear();
            rangeViewCache.Clear();
            Touch();
        }

        private int SanitizeLoadedData()
        {
            int removedEntries = 0;
            var sanitized = new Dictionary<string, Dictionary<string, Dictionary<string, List<string>>>>();

            foreach (var ownerPair in data)
            {
                if (string.IsNullOrWhiteSpace(ownerPair.Key) || ownerPair.Value == null)
                {
                    removedEntries++;
                    continue;
                }

                var cleanCategories = new Dictionary<string, Dictionary<string, List<string>>>();
                foreach (var categoryPair in ownerPair.Value)
                {
                    if (string.IsNullOrWhiteSpace(categoryPair.Key) || categoryPair.Value == null)
                    {
                        removedEntries++;
                        continue;
                    }

                    var cleanLocations = new Dictionary<string, List<string>>();
                    foreach (var locationPair in categoryPair.Value)
                    {
                        if (string.IsNullOrWhiteSpace(locationPair.Key) || locationPair.Value == null)
                        {
                            removedEntries++;
                            continue;
                        }

                        var cleanRanges = new List<string>();
                        var seenRanges = new HashSet<string>(StringComparer.Ordinal);
                        foreach (string entry in locationPair.Value)
                        {
                            string trimmed = entry?.Trim();
                            if (string.IsNullOrWhiteSpace(trimmed)
                                || !TileRangeCodec.TryParseEntry(trimmed, out _, out _, out _, out _)
                                || !seenRanges.Add(trimmed))
                            {
                                removedEntries++;
                                continue;
                            }

                            cleanRanges.Add(trimmed);
                        }

                        if (cleanRanges.Count > 0)
                            cleanLocations[locationPair.Key] = cleanRanges;
                    }

                    if (cleanLocations.Count > 0)
                        cleanCategories[categoryPair.Key] = cleanLocations;
                }

                if (cleanCategories.Count > 0)
                    sanitized[ownerPair.Key] = cleanCategories;
            }

            data = sanitized;
            return removedEntries;
        }

        public void ClearLoadedData()
        {
            data = new Dictionary<string, Dictionary<string, Dictionary<string, List<string>>>>();
            expandedTileCache.Clear();
            rangeViewCache.Clear();
            Touch();
        }

        public Dictionary<string, Dictionary<string, Dictionary<string, List<string>>>> CreateSnapshot()
        {
            return CloneData(data);
        }

        public void ReplaceWithHostSnapshot(
            Dictionary<string, Dictionary<string, Dictionary<string, List<string>>>> snapshot
        )
        {
            var changedKeys = CollectStoredKeys(data);
            data = CloneData(snapshot ?? new Dictionary<string, Dictionary<string, Dictionary<string, List<string>>>>());
            int removedEntries = SanitizeLoadedData();
            changedKeys.UnionWith(CollectStoredKeys(data));

            expandedTileCache.Clear();
            rangeViewCache.Clear();
            Touch();

            if (removedEntries > 0)
            {
                monitor.Log(
                    $"[Tile Marker] Ignored {removedEntries} invalid tile entr{(removedEntries == 1 ? "y" : "ies")} received from the host.",
                    LogLevel.Warn
                );
            }

            foreach (var key in changedKeys)
                RaiseTileMarksChanged(key.OwnerModId, key.Category, key.LocationName);
        }

        public IReadOnlyList<string> GetRanges(string ownerModId, string category, string locationName)
        {
            if (string.IsNullOrWhiteSpace(ownerModId)
                || string.IsNullOrWhiteSpace(category)
                || string.IsNullOrWhiteSpace(locationName))
                return Array.Empty<string>();

            var key = (ownerModId, category, locationName);
            if (rangeViewCache.TryGetValue(key, out IReadOnlyList<string> cachedView))
                return cachedView;

            if (data.TryGetValue(ownerModId, out var byCategory)
                && byCategory.TryGetValue(category, out var byLocation)
                && byLocation.TryGetValue(locationName, out var ranges))
            {
                IReadOnlyList<string> readOnlyView = ranges.AsReadOnly();
                rangeViewCache[key] = readOnlyView;
                return readOnlyView;
            }

            return Array.Empty<string>();
        }

        public HashSet<Point> GetExpandedTiles(string ownerModId, string category, string locationName)
        {
            return new HashSet<Point>(GetCachedExpandedTiles(ownerModId, category, locationName));
        }

        public bool IsTileMarked(string ownerModId, string category, string locationName, Point tile)
        {
            return GetCachedExpandedTiles(ownerModId, category, locationName).Contains(tile);
        }

        private HashSet<Point> GetCachedExpandedTiles(string ownerModId, string category, string locationName)
        {
            if (string.IsNullOrWhiteSpace(ownerModId)
                || string.IsNullOrWhiteSpace(category)
                || string.IsNullOrWhiteSpace(locationName))
                return new HashSet<Point>();

            var key = (ownerModId, category, locationName);
            if (!expandedTileCache.TryGetValue(key, out HashSet<Point> tiles))
            {
                tiles = TileRangeCodec.Expand(GetRanges(ownerModId, category, locationName));
                expandedTileCache[key] = tiles;
            }

            return tiles;
        }

        /// <summary>Replaces the saved tiles for one owner/category/location and persists them transactionally.</summary>
        public bool SaveTiles(string ownerModId, string category, string locationName, HashSet<Point> tiles)
        {
            if (!Context.IsMainPlayer)
            {
                monitor.Log("[Tile Marker] Only the host can save tile selections in multiplayer.", LogLevel.Warn);
                return false;
            }

            if (string.IsNullOrWhiteSpace(ownerModId)
                || string.IsNullOrWhiteSpace(category)
                || string.IsNullOrWhiteSpace(locationName))
            {
                monitor.Log("[Tile Marker] Refused to save tiles with an empty owner, category, or location ID.", LogLevel.Warn);
                return false;
            }

            var key = (ownerModId, category, locationName);
            var savedTiles = tiles == null ? new HashSet<Point>() : new HashSet<Point>(tiles);
            List<string> compressed = TileRangeCodec.Compress(savedTiles);
            var previousData = CloneData(data);

            ApplyStoredRanges(ownerModId, category, locationName, compressed);

            try
            {
                WriteImmediateData(data);
            }
            catch (Exception ex)
            {
                data = previousData;
                expandedTileCache.Clear();
                rangeViewCache.Clear();
                monitor.Log($"[Tile Marker] Could not save tile selections immediately; the previous data was restored in memory: {ex}", LogLevel.Error);
                return false;
            }

            // Keep an embedded copy too. SMAPI commits this one when Stardew saves the day, so it
            // remains useful when the save is transferred even though the immediate copy is primary.
            try
            {
                helper.Data.WriteSaveData(SaveDataKey, data);
            }
            catch (Exception ex)
            {
                monitor.Log($"[Tile Marker] Immediate data was saved, but the embedded save backup could not be updated: {ex}", LogLevel.Warn);
            }

            expandedTileCache[key] = savedTiles;
            rangeViewCache.Remove(key);
            Touch();
            monitor.Log($"[Tile Marker] Saved {compressed.Count} range(s) immediately for {ownerModId}/{category}/{locationName}.", LogLevel.Trace);

            RaiseTileMarksChanged(ownerModId, category, locationName);

            return true;
        }

        private void Touch()
        {
            unchecked
            {
                Revision++;
            }
        }

        private void RaiseTileMarksChanged(string ownerModId, string category, string locationName)
        {
            var changeArgs = new TileMarksChangedEventArgs(ownerModId, category, locationName);
            Delegate[] subscribers = TileMarksChanged?.GetInvocationList() ?? Array.Empty<Delegate>();
            foreach (Delegate subscriber in subscribers)
            {
                try
                {
                    ((EventHandler<TileMarksChangedEventArgs>)subscriber)(this, changeArgs);
                }
                catch (Exception ex)
                {
                    monitor.Log($"[Tile Marker] A consuming mod failed while handling TileMarksChanged: {ex}", LogLevel.Warn);
                }
            }
        }

        private static HashSet<(string OwnerModId, string Category, string LocationName)> CollectStoredKeys(
            Dictionary<string, Dictionary<string, Dictionary<string, List<string>>>> source
        )
        {
            var keys = new HashSet<(string OwnerModId, string Category, string LocationName)>();
            if (source == null)
                return keys;

            foreach (var ownerPair in source)
            {
                if (ownerPair.Value == null)
                    continue;

                foreach (var categoryPair in ownerPair.Value)
                {
                    if (categoryPair.Value == null)
                        continue;

                    foreach (string locationName in categoryPair.Value.Keys)
                        keys.Add((ownerPair.Key, categoryPair.Key, locationName));
                }
            }

            return keys;
        }

        private void ApplyStoredRanges(string ownerModId, string category, string locationName, List<string> ranges)
        {
            if (ranges.Count > 0)
            {
                if (!data.TryGetValue(ownerModId, out var byCategory))
                    data[ownerModId] = byCategory = new Dictionary<string, Dictionary<string, List<string>>>();

                if (!byCategory.TryGetValue(category, out var byLocation))
                    byCategory[category] = byLocation = new Dictionary<string, List<string>>();

                byLocation[locationName] = ranges;
                return;
            }

            if (!data.TryGetValue(ownerModId, out var existingCategories)
                || !existingCategories.TryGetValue(category, out var existingLocations))
                return;

            existingLocations.Remove(locationName);
            if (existingLocations.Count == 0)
                existingCategories.Remove(category);
            if (existingCategories.Count == 0)
                data.Remove(ownerModId);
        }

        private static Dictionary<string, Dictionary<string, Dictionary<string, List<string>>>> CloneData(
            Dictionary<string, Dictionary<string, Dictionary<string, List<string>>>> source
        )
        {
            var clone = new Dictionary<string, Dictionary<string, Dictionary<string, List<string>>>>();
            foreach (var ownerPair in source)
            {
                var categories = new Dictionary<string, Dictionary<string, List<string>>>();
                clone[ownerPair.Key] = categories;

                foreach (var categoryPair in ownerPair.Value)
                {
                    var locations = new Dictionary<string, List<string>>();
                    categories[categoryPair.Key] = locations;

                    foreach (var locationPair in categoryPair.Value)
                        locations[locationPair.Key] = new List<string>(locationPair.Value);
                }
            }

            return clone;
        }

        private void WriteImmediateData(
            Dictionary<string, Dictionary<string, Dictionary<string, List<string>>>> currentSaveData
        )
        {
            TileMarkerImmediateData immediateData = helper.Data.ReadGlobalData<TileMarkerImmediateData>(ImmediateDataKey)
                                                    ?? new TileMarkerImmediateData();
            immediateData.Saves ??= new Dictionary<string, Dictionary<string, Dictionary<string, Dictionary<string, List<string>>>>>();
            immediateData.Saves[GetCurrentSaveKey()] = CloneData(currentSaveData);
            helper.Data.WriteGlobalData(ImmediateDataKey, immediateData);
        }

        private static string GetCurrentSaveKey()
        {
            if (!string.IsNullOrWhiteSpace(Constants.SaveFolderName))
                return Constants.SaveFolderName;

            return Game1.uniqueIDForThisGame.ToString();
        }
    }

    internal sealed class TileMarkerImmediateData
    {
        public Dictionary<string, Dictionary<string, Dictionary<string, Dictionary<string, List<string>>>>> Saves { get; set; } = new();
    }
}
