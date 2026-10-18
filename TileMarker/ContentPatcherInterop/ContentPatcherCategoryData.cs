using System.Collections.Generic;

namespace TileMarker.ContentPatcherInterop
{
    /// <summary>A tile category declared by a Content Patcher content pack.</summary>
    public sealed class ContentPatcherCategoryData
    {
        public string DisplayName { get; set; }
        public string SharedGroup { get; set; }
        public ContentPatcherInteractionData Interaction { get; set; }
    }

    /// <summary>An optional interaction which Tile Marker runs on a marked tile.</summary>
    public sealed class ContentPatcherInteractionData
    {
        /// <summary>Action, UseTool, or a SMAPI keybind string such as F or LeftShift + F.</summary>
        public string Button { get; set; } = "Action";

        /// <summary>Facing, Standing, or Cursor.</summary>
        public string Target { get; set; } = "Facing";

        /// <summary>Vanilla or mod-provided trigger actions to run in order.</summary>
        public List<string> Actions { get; set; } = new();

        /// <summary>Whether to suppress the activating input after at least one action succeeds.</summary>
        public bool SuppressButton { get; set; } = true;
    }
}
