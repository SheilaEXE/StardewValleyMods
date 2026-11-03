using Microsoft.Xna.Framework.Graphics;
using StardewModdingAPI;
using StardewModdingAPI.Events;
using StardewValley;
using System.Text.Json;

namespace FashionSenseWardrobeManager;

/// <summary>Applies Sweet Farmer sleeve overlays to the active outfit without changing its global config.</summary>
internal sealed class SweetFarmerSleeveManager
{
    private const string PackId = "Heart.SweetFarmer";
    private const string DataKey = "FashionSenseWardrobeManager.SweetFarmerSleeves";
    private const string SocksDataKey = "FashionSenseWardrobeManager.SweetFarmerSocks";
    private const string ShoesDataKey = "FashionSenseWardrobeManager.SweetFarmerShoes";
    private static readonly string[] SleeveNames =
    {
        "No Sleeves", "Short", "Elbow", "Long", "Off Shoulders",
        "Off Shoulders Long", "Off Shoulders Mini", "Oversized"
    };
    private static readonly string[] SockNames =
    {
        "No Socks", "Ankle", "Mid Calf", "Knee high", "Thigh high", "Thights"
    };
    private static readonly string[] ShoeNames = { "Basic", "Sandals", "Boots", "Barefoot" };
    private static readonly string[] FarmerAssets =
    {
        "Characters/Farmer/farmer_girl_base", "Characters/Farmer/farmer_girl_base_bald",
        "Characters/Farmer/farmer_base", "Characters/Farmer/farmer_base_bald"
    };

    private readonly IModHelper _helper;
    private readonly IMonitor _monitor;
    private string? _packDirectory;
    private bool _searchedForPack;
    private string? _activeOutfit;
    private string? _activeSleeve;
    private string? _activeSocks;
    private string? _activeShoes;
    private bool _previewActive;
    private string? _lastWarning;

    public SweetFarmerSleeveManager(IModHelper helper, IMonitor monitor)
    {
        _helper = helper;
        _monitor = monitor;
        _helper.Events.Content.AssetRequested += OnAssetRequested;
    }

    public bool IsAvailable => _helper.ModRegistry.IsLoaded(PackId) && FindPackDirectory() is not null;
    public IReadOnlyList<string> Options => SleeveNames;
    public IReadOnlyList<string> SocksOptions => SockNames;
    public IReadOnlyList<string> ShoesOptions => ShoeNames;

    public string? Get(string outfitName)
        => Load(DataKey).TryGetValue(outfitName, out string? sleeve) ? sleeve : null;
    public string? GetSocks(string outfitName)
        => Load(SocksDataKey).TryGetValue(outfitName, out string? socks) ? socks : null;
    public string? GetShoes(string outfitName)
        => Load(ShoesDataKey).TryGetValue(outfitName, out string? shoes) ? shoes : null;
    public string GetEffectiveShoes(string outfitName)
        => GetShoes(outfitName) ?? "Basic";

    public string? GetConfiguredLegHeight()
    {
        if (!IsAvailable || !Context.IsWorldReady)
            return null;

        try
        {
            using JsonDocument config = JsonDocument.Parse(File.ReadAllText(Path.Combine(_packDirectory!, "config.json")));
            string key = Game1.player.IsMale ? "Male Legs" : "Female Legs";
            return config.RootElement.GetProperty(key).GetString();
        }
        catch (Exception ex)
        {
            WarnOnce($"Could not read Sweet Farmer leg height: {ex.Message}");
            return null;
        }
    }

    public void Set(string outfitName, string? sleeve)
        => SetValue(DataKey, outfitName, sleeve, SleeveNames);
    public void SetSocks(string outfitName, string? socks)
        => SetValue(SocksDataKey, outfitName, socks, SockNames);
    public void SetShoes(string outfitName, string? shoes)
        => SetValue(ShoesDataKey, outfitName, shoes, ShoeNames);

    private void SetValue(string key, string outfitName, string? value, string[] validValues)
    {
        if (!Context.IsWorldReady)
            return;

        Dictionary<string, string> saved = Load(key);
        if (value is null)
            saved.Remove(outfitName);
        else if (validValues.Contains(value, StringComparer.Ordinal))
            saved[outfitName] = value;
        Game1.player.modData[key] = JsonSerializer.Serialize(saved);
    }

    public void RenameOutfit(string oldName, string newName)
    {
        foreach (string key in new[] { DataKey, SocksDataKey, ShoesDataKey })
        {
            Dictionary<string, string> saved = Load(key);
            if (!saved.Remove(oldName, out string? value))
                continue;
            saved[newName] = value;
            Game1.player.modData[key] = JsonSerializer.Serialize(saved);
        }
    }

    public void RemoveOutfits(IEnumerable<string> names)
    {
        string[] outfitNames = names.ToArray();
        foreach (string key in new[] { DataKey, SocksDataKey, ShoesDataKey })
        {
            Dictionary<string, string> saved = Load(key);
            if (outfitNames.Aggregate(false, (changed, name) => saved.Remove(name) || changed))
                Game1.player.modData[key] = JsonSerializer.Serialize(saved);
        }
    }

    public void Reset()
    {
        _previewActive = false;
        _activeOutfit = null;
        _activeSleeve = null;
        _activeSocks = null;
        _activeShoes = null;
    }

    /// <summary>Show an unsaved sleeve in the Wardrobe preview; no outfit assignment is changed.</summary>
    public void Preview(string? outfitName, string? sleeve, string? socks, string? shoes)
    {
        if (!Context.IsWorldReady || !IsAvailable)
            return;

        _previewActive = true;
        SetActive(outfitName, sleeve, socks, outfitName is null ? shoes : shoes ?? "Basic");
    }

    public void EndPreview(string? equippedOutfitName)
    {
        if (!_previewActive)
            return;

        _previewActive = false;
        // Force a refresh even if the real outfit has the same name as the preview.
        // Its unsaved sleeve may be different.
        _activeOutfit = null;
        _activeSleeve = null;
        _activeSocks = null;
        _activeShoes = null;
        SyncCurrentOutfit(equippedOutfitName);
    }

    public void SyncCurrentOutfit(string? outfitName)
    {
        if (_previewActive || !Context.IsWorldReady || !IsAvailable)
            return;

        string? sleeve = string.IsNullOrWhiteSpace(outfitName) ? null : Get(outfitName);
        string? socks = string.IsNullOrWhiteSpace(outfitName) ? null : GetSocks(outfitName);
        string? shoes = string.IsNullOrWhiteSpace(outfitName) ? null : GetEffectiveShoes(outfitName);
        SetActive(outfitName, sleeve, socks, shoes);
    }

    private void SetActive(string? outfitName, string? sleeve, string? socks, string? shoes)
    {
        bool layersChanged = !string.Equals(_activeSleeve, sleeve, StringComparison.Ordinal)
            || !string.Equals(_activeSocks, socks, StringComparison.Ordinal)
            || !string.Equals(_activeShoes, shoes, StringComparison.Ordinal);

        _activeOutfit = outfitName;
        if (!layersChanged)
            return;
        _activeSleeve = sleeve;
        _activeSocks = socks;
        _activeShoes = shoes;
        int first = Game1.player.IsMale ? 2 : 0;
        _helper.GameContent.InvalidateCache(FarmerAssets[first]);
        _helper.GameContent.InvalidateCache(FarmerAssets[first + 1]);
        Game1.player.FarmerRenderer.MarkSpriteDirty();
    }

    private void OnAssetRequested(object? sender, AssetRequestedEventArgs e)
    {
        if ((_activeSleeve is null || _activeSleeve == "No Sleeves")
            && (_activeSocks is null || _activeSocks == "No Socks")
            && (_activeShoes is null || _activeShoes == "Barefoot"))
            return;
        if (!IsAvailable)
            return;

        bool female = e.NameWithoutLocale.IsEquivalentTo(FarmerAssets[0])
            || e.NameWithoutLocale.IsEquivalentTo(FarmerAssets[1]);
        bool male = e.NameWithoutLocale.IsEquivalentTo(FarmerAssets[2])
            || e.NameWithoutLocale.IsEquivalentTo(FarmerAssets[3]);
        if (!female && !male)
            return;

        string configPath = Path.Combine(_packDirectory!, "config.json");
        string prefix = female ? "Female" : "Male";
        string configKey = $"{prefix} Sleeves";
        string armKey = female ? "Female Arm Type" : "Male Arm Type";
        string socksKey = $"{prefix} Socks";
        string shoesKey = $"{prefix} Shoes";
        string configuredSleeve, configuredSocks, configuredShoes, arm, legs;
        bool shoeTips, shoeGaps;
        try
        {
            using JsonDocument config = JsonDocument.Parse(File.ReadAllText(configPath));
            configuredSleeve = config.RootElement.GetProperty(configKey).GetString() ?? string.Empty;
            arm = config.RootElement.GetProperty(armKey).GetString() ?? string.Empty;
            configuredSocks = config.RootElement.GetProperty(socksKey).GetString() ?? string.Empty;
            configuredShoes = config.RootElement.GetProperty(shoesKey).GetString() ?? string.Empty;
            legs = config.RootElement.GetProperty($"{prefix} Legs").GetString() ?? string.Empty;
            shoeTips = string.Equals(config.RootElement.GetProperty($"{prefix} Shoe Tips").ToString(), "true", StringComparison.OrdinalIgnoreCase);
            shoeGaps = string.Equals(config.RootElement.GetProperty($"{prefix} Shoe Gaps").ToString(), "true", StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception ex)
        {
            WarnOnce($"Could not read Sweet Farmer config: {ex.Message}");
            return;
        }

        var overlays = new List<string>();
        if (_activeSleeve is not null && _activeSleeve != "No Sleeves")
        {
            if (configuredSleeve == "No Sleeves")
                overlays.Add(Path.Combine("Sleeves", $"{(female ? "" : "MF_")}{arm}_{_activeSleeve}.png"));
            else
                WarnOnce($"Set Sweet Farmer > {configKey} to 'No Sleeves' before using per-outfit sleeves.");
        }

        string? socks = _activeSocks is "No Socks" ? null : _activeSocks;
        string? shoes = _activeShoes is "Barefoot" ? null : _activeShoes;
        if (socks is not null || shoes is not null)
        {
            if (configuredSocks != "No Socks" || configuredShoes != "Barefoot")
            {
                WarnOnce($"Set Sweet Farmer > {socksKey} to 'No Socks' and {shoesKey} to 'Barefoot' before using per-outfit socks and shoes.");
            }
            else
            {
                string folder = Path.Combine("Shoes&Socks", prefix);
                if (socks is not null)
                    overlays.Add(Path.Combine(folder, "Socks", $"{socks}_{legs}.png"));
                if (shoes is not null)
                    overlays.Add(Path.Combine(folder, $"{shoes}_{legs}.png"));

                // Keep the same drawing order as Sweet Farmer's own patches.
                if (shoeTips && shoes is not null)
                    overlays.Add(Path.Combine(folder, $"Tips_{shoes}_{legs}.png"));
                if (female && shoeTips && socks is not null && shoes is null)
                    overlays.Add(Path.Combine(folder, $"Tips_Socks_{legs}.png"));
                // Sweet Farmer's Barefoot base draws a skin-colored gap. Replace it
                // with the normal shoe gap when this outfit selects footwear.
                if (shoeGaps && shoes is not null)
                    overlays.Add(Path.Combine(folder, $"Gap_{legs}.png"));
                if (shoeGaps && socks is not null && shoes is null)
                    overlays.Add(Path.Combine(folder, $"Gap_{legs}_Socks.png"));
                if (socks is not null && shoes is not null)
                    overlays.Add(Path.Combine(folder, "Correction", $"{shoes}_{legs}.png"));
                if (socks is not null && shoes == "Sandals" && shoeTips)
                    overlays.Add(Path.Combine(folder, "Correction", $"Tips_Sandals_{legs}.png"));
            }
        }

        string[] paths = overlays.Select(path => Path.Combine(_packDirectory!, "Assets", path)).ToArray();
        foreach (string path in paths)
        {
            if (File.Exists(path))
                continue;
            WarnOnce($"Sweet Farmer image is missing: {Path.GetFileName(path)}");
            return;
        }
        if (paths.Length == 0)
            return;

        e.Edit(asset =>
        {
            foreach (string path in paths)
            {
                using FileStream stream = File.OpenRead(path);
                using Texture2D overlay = Texture2D.FromStream(Game1.graphics.GraphicsDevice, stream);
                asset.AsImage().PatchImage(overlay, patchMode: PatchMode.Overlay);
            }
        }, AssetEditPriority.Late);
    }

    private string? FindPackDirectory()
    {
        if (_searchedForPack)
            return _packDirectory;
        _searchedForPack = true;
        string modsRoot = Path.GetDirectoryName(_helper.DirectoryPath)!;
        foreach (string parent in Directory.EnumerateDirectories(modsRoot))
        {
            // SMAPI supports a pack directly in Mods or one folder deeper.
            foreach (string directory in new[] { parent }.Concat(Directory.EnumerateDirectories(parent)))
            {
                string manifestPath = Path.Combine(directory, "manifest.json");
                if (!File.Exists(manifestPath))
                    continue;
                try
                {
                    using JsonDocument manifest = JsonDocument.Parse(File.ReadAllText(manifestPath));
                    if (manifest.RootElement.TryGetProperty("UniqueID", out JsonElement id)
                        && string.Equals(id.GetString(), PackId, StringComparison.OrdinalIgnoreCase))
                        return _packDirectory = directory;
                }
                catch (JsonException) { }
            }
        }
        return null;
    }

    private Dictionary<string, string> Load(string key)
    {
        if (!Context.IsWorldReady || !Game1.player.modData.TryGetValue(key, out string? json))
            return new(StringComparer.OrdinalIgnoreCase);
        try
        {
            return new(JsonSerializer.Deserialize<Dictionary<string, string>>(json) ?? new(),
                StringComparer.OrdinalIgnoreCase);
        }
        catch (JsonException)
        {
            return new(StringComparer.OrdinalIgnoreCase);
        }
    }

    private void WarnOnce(string message)
    {
        if (_lastWarning == message)
            return;
        _lastWarning = message;
        _monitor.Log(message, LogLevel.Warn);
    }
}
