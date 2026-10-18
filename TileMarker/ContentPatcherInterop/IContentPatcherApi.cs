using StardewModdingAPI;

namespace TileMarker.ContentPatcherInterop
{
    /// <summary>Minimal Content Patcher API surface used to register Tile Marker tokens.</summary>
    public interface IContentPatcherApi
    {
        void RegisterToken(IManifest mod, string name, object token);
    }
}
