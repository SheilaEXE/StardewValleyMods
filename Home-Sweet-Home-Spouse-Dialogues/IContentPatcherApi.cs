using System;
using System.Collections.Generic;
using StardewModdingAPI;

namespace HomeSweetHomeSpouseDialogues
{
    /// <summary>Minimal Content Patcher API surface used to expose the configured sink button.</summary>
    public interface IContentPatcherApi
    {
        void RegisterToken(IManifest mod, string name, Func<IEnumerable<string>> getValue);
    }
}
