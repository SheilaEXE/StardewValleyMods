using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using StardewModdingAPI;
using StardewModdingAPI.Events;
using StardewValley;
using System.Text.Json;

namespace FashionSenseWardrobeManager;

/// <summary>Applies GH's Peach Body sleeves and footwear per saved outfit.</summary>
internal sealed class PeachBodyManager : IOutfitBodyLayerManager
{
    private const string PackId = "GH.Pitch.female";
    private const string SleeveKey = "FashionSenseWardrobeManager.PeachBodySleeves";
    private const string ShoesKey = "FashionSenseWardrobeManager.PeachBodyShoes";
    private static readonly string[] FarmerAssets =
    {
        "Characters/Farmer/farmer_girl_base", "Characters/Farmer/farmer_girl_base_bald"
    };
    private static readonly string[] SleeveValues =
    {
        "S", "SS", "LS", "OS", "S_ShirtOS", "C_ShirtOS", "OffS", "OneOffS", "OneOffLS", "OneOffOS"
    };
    private static readonly string[] ShoeValues =
    {
        "S_Socks", "S_LS", "S_Shoes", "S_SS", "S_SLS", "S_Sandals", "S_LB", "S_MB", "S_MBLS", "S_FB",
        "C_Socks", "C_LS", "C_Shoes", "C_SS", "C_SLS", "C_Sandals", "C_LB", "C_MB", "C_MBLS"
    };

    private readonly IModHelper _helper;
    private readonly IMonitor _monitor;
    private string? _packDirectory;
    private bool _searched;
    private string? _activeOutfit;
    private string? _activeSleeve;
    private string? _activeShoes;
    private bool _previewActive;
    private string? _lastWarning;

    public PeachBodyManager(IModHelper helper, IMonitor monitor)
    {
        _helper = helper;
        _monitor = monitor;
        helper.Events.Content.AssetRequested += OnAssetRequested;
    }

    public bool IsAvailable => Context.IsWorldReady && !Game1.player.IsMale
        && _helper.ModRegistry.IsLoaded(PackId)
        && !_helper.ModRegistry.IsLoaded("Heart.SweetFarmer")
        && FindPackDirectory() is not null;
    public IReadOnlyList<string> Sleeves => SleeveValues;
    public IReadOnlyList<string> Shoes => ShoeValues;
    public string? GetSleeve(string name) => Load(SleeveKey).GetValueOrDefault(name);
    public string? GetShoes(string name) => Load(ShoesKey).GetValueOrDefault(name);
    public void SetSleeve(string name, string? value) => Set(SleeveKey, name, value, SleeveValues);
    public void SetShoes(string name, string? value) => Set(ShoesKey, name, value, ShoeValues);

    private void Set(string key, string name, string? value, string[] allowed)
    {
        if (!Context.IsWorldReady)
            return;
        Dictionary<string, string> saved = Load(key);
        if (value is null)
            saved.Remove(name);
        else if (allowed.Contains(value, StringComparer.Ordinal))
            saved[name] = value;
        Game1.player.modData[key] = JsonSerializer.Serialize(saved);
    }

    public void RenameOutfit(string from, string to)
    {
        foreach (string key in new[] { SleeveKey, ShoesKey })
        {
            Dictionary<string, string> saved = Load(key);
            if (!saved.Remove(from, out string? value))
                continue;
            saved[to] = value;
            Game1.player.modData[key] = JsonSerializer.Serialize(saved);
        }
    }

    public void RemoveOutfits(IEnumerable<string> names)
    {
        foreach (string key in new[] { SleeveKey, ShoesKey })
        {
            Dictionary<string, string> saved = Load(key);
            bool changed = false;
            foreach (string name in names)
                changed |= saved.Remove(name);
            if (changed)
                Game1.player.modData[key] = JsonSerializer.Serialize(saved);
        }
    }

    public void Reset()
    {
        _previewActive = false;
        _activeOutfit = null;
        _activeSleeve = null;
        _activeShoes = null;
    }

    public void Preview(string? outfitName, string? sleeve, string? shoes)
    {
        if (!Context.IsWorldReady || !IsAvailable || Game1.player.IsMale)
            return;
        _previewActive = true;
        SetActive(outfitName, sleeve, shoes);
    }

    public void EndPreview(string? equippedOutfitName)
    {
        if (!_previewActive)
            return;
        _previewActive = false;
        _activeOutfit = null;
        _activeSleeve = null;
        _activeShoes = null;
        SyncCurrentOutfit(equippedOutfitName);
    }

    public void SyncCurrentOutfit(string? outfitName)
    {
        if (_previewActive || !Context.IsWorldReady || !IsAvailable || Game1.player.IsMale)
            return;
        SetActive(outfitName,
            string.IsNullOrWhiteSpace(outfitName) ? null : GetSleeve(outfitName),
            string.IsNullOrWhiteSpace(outfitName) ? null : GetShoes(outfitName));
    }

    private void SetActive(string? outfitName, string? sleeve, string? shoes)
    {
        bool layersChanged = _activeSleeve != sleeve || _activeShoes != shoes;
        _activeOutfit = outfitName;
        if (!layersChanged)
            return;
        _activeSleeve = sleeve;
        _activeShoes = shoes;
        foreach (string asset in FarmerAssets)
            _helper.GameContent.InvalidateCache(asset);
        Game1.player.FarmerRenderer.MarkSpriteDirty();
    }

    private void OnAssetRequested(object? sender, AssetRequestedEventArgs e)
    {
        if (!IsAvailable || !Context.IsWorldReady || Game1.player.IsMale
            || !FarmerAssets.Any(asset => e.NameWithoutLocale.IsEquivalentTo(asset))
            || (_activeSleeve is null && _activeShoes is null))
            return;

        bool sleevesNeutral, shoesNeutral;
        try
        {
            using JsonDocument config = JsonDocument.Parse(File.ReadAllText(Path.Combine(_packDirectory!, "config.json")));
            sleevesNeutral = IsFalse(config, "Sleeve") && IsFalse(config, "Seasonal Sleeves");
            shoesNeutral = IsFalse(config, "Shoes");
        }
        catch (Exception ex)
        {
            WarnOnce($"Could not read Peach Body config: {ex.Message}");
            return;
        }

        string? sleevePath = null, shoesPath = null;
        if (_activeSleeve is not null)
        {
            if (sleevesNeutral)
                sleevePath = Path.Combine(_packDirectory!, "assets", "Sleeve", _activeSleeve + ".png");
            else
                WarnOnce("Set Peach Body Sleeve and Seasonal Sleeves to false for per-outfit sleeves.");
        }
        if (_activeShoes is not null)
        {
            if (shoesNeutral)
                shoesPath = Path.Combine(_packDirectory!, "assets", "Shoes", _activeShoes + ".png");
            else
                WarnOnce("Set Peach Body Shoes to false for per-outfit footwear.");
        }
        foreach (string path in new[] { sleevePath, shoesPath }.OfType<string>())
        {
            if (File.Exists(path))
                continue;
            WarnOnce($"Peach Body image is missing: {Path.GetFileName(path)}");
            return;
        }
        if (sleevePath is null && shoesPath is null)
            return;

        e.Edit(asset =>
        {
            if (sleevePath is not null)
            {
                using FileStream stream = File.OpenRead(sleevePath);
                using Texture2D texture = Texture2D.FromStream(Game1.graphics.GraphicsDevice, stream);
                Rectangle area = new(96, 0, 192, 672);
                asset.AsImage().PatchImage(texture, sourceArea: area, targetArea: area, patchMode: PatchMode.Replace);
            }
            if (shoesPath is not null)
            {
                using FileStream stream = File.OpenRead(shoesPath);
                using Texture2D texture = Texture2D.FromStream(Game1.graphics.GraphicsDevice, stream);
                Rectangle area = new(0, 0, 96, 672);
                asset.AsImage().PatchImage(texture, sourceArea: area, targetArea: area, patchMode: PatchMode.Overlay);
            }
        }, AssetEditPriority.Late);
    }

    private static bool IsFalse(JsonDocument config, string key)
        => string.Equals(config.RootElement.GetProperty(key).ToString(), "false", StringComparison.OrdinalIgnoreCase);

    private string? FindPackDirectory()
    {
        if (_searched)
            return _packDirectory;
        _searched = true;
        string root = Path.GetDirectoryName(_helper.DirectoryPath)!;
        foreach (string parent in Directory.EnumerateDirectories(root))
        {
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

    private static Dictionary<string, string> Empty() => new(StringComparer.OrdinalIgnoreCase);

    private Dictionary<string, string> Load(string key)
    {
        if (!Context.IsWorldReady || !Game1.player.modData.TryGetValue(key, out string? json))
            return Empty();
        try { return new(JsonSerializer.Deserialize<Dictionary<string, string>>(json) ?? Empty(), StringComparer.OrdinalIgnoreCase); }
        catch (JsonException) { return Empty(); }
    }

    private void WarnOnce(string message)
    {
        if (_lastWarning == message)
            return;
        _lastWarning = message;
        _monitor.Log(message, LogLevel.Warn);
    }
}
