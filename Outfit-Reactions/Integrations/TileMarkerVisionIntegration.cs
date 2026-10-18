using System;
using StardewModdingAPI;
using StardewValley;

namespace OutfitReactions
{
    /// <summary>Optional Tile Marker bridge for line-of-sight exceptions.</summary>
    internal sealed class TileMarkerVisionIntegration
    {
        private const string TileMarkerModId = "NatrollEXE.TileMarker";
        private const string VisionCategory = "VisionIgnored";
        private const string SharedVisionGroup = "NatrollEXE.SharedVisionIgnored";

        private readonly IModHelper helper;
        private readonly IMonitor monitor;
        private readonly IManifest manifest;
        private ITileMarkerApi api;

        public TileMarkerVisionIntegration(IModHelper helper, IMonitor monitor, IManifest manifest)
        {
            this.helper = helper;
            this.monitor = monitor;
            this.manifest = manifest;
        }

        public void Initialize()
        {
            try
            {
                string displayName = helper.Translation.Get("tile-marker.vision-ignored.name").ToString();
                try
                {
                    ISharedTileMarkerApi sharedApi = helper.ModRegistry.GetApi<ISharedTileMarkerApi>(TileMarkerModId);
                    if (sharedApi != null)
                    {
                        sharedApi.RegisterCategoryWithSharedGroup(
                            manifest.UniqueID,
                            VisionCategory,
                            displayName,
                            SharedVisionGroup
                        );
                        api = sharedApi;
                    }
                    else
                    {
                        api = helper.ModRegistry.GetApi<ITileMarkerApi>(TileMarkerModId);
                        api?.RegisterCategory(manifest.UniqueID, VisionCategory, displayName);
                    }
                }
                catch
                {
                    // Tile Marker 1.0.x still supports an independent category; only shared
                    // merging waits for the newer API.
                    api = helper.ModRegistry.GetApi<ITileMarkerApi>(TileMarkerModId);
                    api?.RegisterCategory(manifest.UniqueID, VisionCategory, displayName);
                }
            }
            catch (Exception ex)
            {
                api = null;
                monitor.Log($"[Tile Marker] Could not register Outfit Reactions' vision category: {ex}", LogLevel.Warn);
            }
        }

        public bool IsVisionIgnoredTile(GameLocation location, int tileX, int tileY)
        {
            if (api == null || location == null)
                return false;

            try
            {
                return api.IsTileMarked(manifest.UniqueID, VisionCategory, location, tileX, tileY);
            }
            catch (Exception ex)
            {
                api = null;
                monitor.Log($"[Tile Marker] Vision tile lookup failed; the optional integration was disabled for this session: {ex}", LogLevel.Warn);
                return false;
            }
        }
    }

    /// <summary>Consumer-side API contract; Tile Marker remains an optional dependency.</summary>
    public interface ITileMarkerApi
    {
        void RegisterCategory(string ownerModId, string category, string displayName);

        bool IsTileMarked(
            string ownerModId,
            string category,
            GameLocation location,
            int x,
            int y
        );
    }

    public interface ISharedTileMarkerApi : ITileMarkerApi
    {
        void RegisterCategoryWithSharedGroup(
            string ownerModId,
            string category,
            string displayName,
            string sharedGroup
        );
    }
}
