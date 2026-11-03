using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using StardewModdingAPI;
using StardewModdingAPI.Events;
using StardewValley;
using System.Text.Json;

namespace FashionSenseWardrobeManager;

internal interface IOutfitBodyLayerManager
{
    bool IsAvailable { get; }
    IReadOnlyList<string> Sleeves { get; }
    IReadOnlyList<string> Shoes { get; }
    string? GetSleeve(string name);
    string? GetShoes(string name);
    void SetSleeve(string name, string? value);
    void SetShoes(string name, string? value);
    void Preview(string? outfitName, string? sleeve, string? shoes);
    void EndPreview(string? equippedOutfitName);
    void SyncCurrentOutfit(string? outfitName);
    void RenameOutfit(string from, string to);
    void RemoveOutfits(IEnumerable<string> names);
    void Reset();
}

/// <summary>Applies More Elegant Farmer sleeves and footwear per saved outfit.</summary>
internal sealed class ElegantFarmerBodyManager : IOutfitBodyLayerManager
{
    private const string PackId = "Kyuya.FarmerBody";
    private const string SleeveKey = "FashionSenseWardrobeManager.ElegantFarmerSleeves";
    private const string ShoesKey = "FashionSenseWardrobeManager.ElegantFarmerShoes";
    private static readonly string[] SleeveValues = { "normal", "long", "big", "none" };
    private static readonly string[] ShoeValues = { "normal", "with socks" };

    private readonly IModHelper _helper;
    private readonly IMonitor _monitor;
    private string? _packDirectory;
    private bool _searched;
    private string? _activeOutfit;
    private string? _activeSleeve;
    private string? _activeShoes;
    private bool _previewActive;
    private string? _lastWarning;

    public ElegantFarmerBodyManager(IModHelper helper, IMonitor monitor)
    {
        _helper = helper;
        _monitor = monitor;
        helper.Events.Content.AssetRequested += OnAssetRequested;
    }

    public bool IsAvailable => Context.IsWorldReady
        && _helper.ModRegistry.IsLoaded(PackId)
        && !_helper.ModRegistry.IsLoaded("Heart.SweetFarmer")
        && !_helper.ModRegistry.IsLoaded("GH.Pitch.female")
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
        if (!Context.IsWorldReady || !IsAvailable)
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
        if (_previewActive || !Context.IsWorldReady || !IsAvailable)
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
        foreach (string asset in GetFarmerAssets())
            _helper.GameContent.InvalidateCache(asset);
        Game1.player.FarmerRenderer.MarkSpriteDirty();
    }

    private void OnAssetRequested(object? sender, AssetRequestedEventArgs e)
    {
        if (!IsAvailable || !Context.IsWorldReady
            || !GetFarmerAssets().Any(asset => e.NameWithoutLocale.IsEquivalentTo(asset))
            || (_activeSleeve is null && _activeShoes is null))
            return;

        bool sleevesNeutral, shoesNeutral;
        try
        {
            using JsonDocument config = JsonDocument.Parse(File.ReadAllText(Path.Combine(_packDirectory!, "config.json")));
            sleevesNeutral = IsValue(config, "sleeve", "none");
            shoesNeutral = IsValue(config, "showshoes", "false");
        }
        catch (Exception ex)
        {
            WarnOnce($"Could not read More Elegant Farmer config: {ex.Message}");
            return;
        }

        string? sleevePath = null, shoesPath = null;
        if (_activeSleeve is not null)
        {
            if (sleevesNeutral)
                sleevePath = Path.Combine(_packDirectory!, "assets", Game1.player.IsMale ? "Male" : "Female", "sleeve", _activeSleeve + ".png");
            else
                WarnOnce("Set More Elegant Farmer sleeve to none for per-outfit sleeves.");
        }
        if (_activeShoes is not null)
        {
            if (shoesNeutral)
                shoesPath = Path.Combine(_packDirectory!, "assets", Game1.player.IsMale ? "Male" : "Female", "shoes", _activeShoes + ".png");
            else
                WarnOnce("Set More Elegant Farmer showshoes to false for per-outfit footwear.");
        }
        foreach (string path in new[] { sleevePath, shoesPath }.OfType<string>())
        {
            if (File.Exists(path))
                continue;
            WarnOnce($"More Elegant Farmer image is missing: {Path.GetFileName(path)}");
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
                asset.AsImage().PatchImage(texture, sourceArea: new Rectangle(0, 0, 192, 672),
                    targetArea: new Rectangle(96, 0, 192, 672), patchMode: PatchMode.Replace);
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

    private static bool IsValue(JsonDocument config, string key, string value)
        => config.RootElement.TryGetProperty(key, out JsonElement current)
            && string.Equals(current.ToString(), value, StringComparison.OrdinalIgnoreCase);

    private static string[] GetFarmerAssets()
    {
        string name = Game1.player.IsMale ? "farmer_base" : "farmer_girl_base";
        return new[] { $"Characters/Farmer/{name}", $"Characters/Farmer/{name}_bald" };
    }

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
