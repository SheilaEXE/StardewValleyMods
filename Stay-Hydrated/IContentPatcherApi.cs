using System;
using System.Collections.Generic;
using StardewModdingAPI;

namespace StayHydrated;

public interface IContentPatcherApi
{
    void RegisterToken(IManifest mod, string name, Func<IEnumerable<string>> getValue);
}
