using StardewModdingAPI;

namespace AllRecipesForSale;

/// <summary>The subset of the Generic Mod Config Menu API used by this mod.</summary>
internal interface IGenericModConfigMenuApi
{
    void Register(IManifest mod, Action reset, Action save, bool titleScreenOnly = false);

    void AddBoolOption(
        IManifest mod,
        Func<bool> getValue,
        Action<bool> setValue,
        Func<string> name,
        Func<string>? tooltip = null,
        string? fieldId = null
    );
}
