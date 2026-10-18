using System;
using System.Collections.Generic;

namespace TileMarker.ContentPatcherInterop
{
    internal enum TileMarkerTokenKind
    {
        MarkedTiles,
        IsTileMarked,
    }

    /// <summary>Content Patcher advanced token backed by a stable per-context snapshot.</summary>
    internal sealed class TileMarkerToken
    {
        private readonly ContentPatcherIntegration integration;
        private readonly TileMarkerTokenKind kind;
        private readonly Dictionary<string, string[]> valueCache = new(StringComparer.Ordinal);

        private long lastRevision = -1;
        private bool lastMergeState;

        public TileMarkerToken(ContentPatcherIntegration integration, TileMarkerTokenKind kind)
        {
            this.integration = integration;
            this.kind = kind;
        }

        public bool AllowsInput()
        {
            return true;
        }

        public bool CanHaveMultipleValues(string input = null)
        {
            return kind == TileMarkerTokenKind.MarkedTiles;
        }

        public bool UpdateContext()
        {
            long revision = integration.StoreRevision;
            bool mergeState = integration.MergeCompatibleCategories;
            bool changed = revision != lastRevision || mergeState != lastMergeState;
            if (changed)
            {
                lastRevision = revision;
                lastMergeState = mergeState;
                valueCache.Clear();
            }

            return changed;
        }

        public bool IsReady()
        {
            return StardewModdingAPI.Context.IsWorldReady;
        }

        public IEnumerable<string> GetValues(string input)
        {
            string cacheKey = input ?? "";
            if (!valueCache.TryGetValue(cacheKey, out string[] values))
            {
                values = kind == TileMarkerTokenKind.MarkedTiles
                    ? integration.GetMarkedTileTokenValues(input)
                    : integration.GetIsTileMarkedTokenValue(input);
                valueCache[cacheKey] = values;
            }

            return values;
        }
    }
}
