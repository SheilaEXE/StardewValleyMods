using StardewModdingAPI;
using StardewValley;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;

namespace FashionSenseWardrobeManager;

internal sealed class IndoorOutfitAdjustment
{
    public string HatId { get; set; } = string.Empty;
    public List<string> AccessoryIds { get; set; } = new();

    public bool HasItems => !string.IsNullOrWhiteSpace(HatId) || AccessoryIds.Count > 0;
}

/// <summary>Stores only the Fashion Sense pieces hidden indoors for each saved outfit.</summary>
internal sealed class IndoorOutfitAdjustmentManager
{
    private const string ModDataKey = "FashionSenseWardrobeManager.IndoorAdjustments";
    private const string ActiveOutfitKey = "FashionSenseWardrobeManager.ActiveIndoorAdjustment";

    public string? GetActiveOutfit()
        => Context.IsWorldReady && Game1.player.modData.TryGetValue(ActiveOutfitKey, out string? name)
            ? name : null;

    public void SetActiveOutfit(string? outfitName)
    {
        if (!Context.IsWorldReady)
            return;

        if (string.IsNullOrWhiteSpace(outfitName))
            Game1.player.modData.Remove(ActiveOutfitKey);
        else
            Game1.player.modData[ActiveOutfitKey] = outfitName;
    }

    public IndoorOutfitAdjustment Get(string outfitName)
    {
        Dictionary<string, IndoorOutfitAdjustment> adjustments = Load();
        if (!adjustments.TryGetValue(outfitName, out IndoorOutfitAdjustment? value) || value is null)
            return new IndoorOutfitAdjustment();

        value.HatId ??= string.Empty;
        value.AccessoryIds ??= new();
        return value;
    }

    public void Set(string outfitName, IndoorOutfitAdjustment adjustment)
    {
        Dictionary<string, IndoorOutfitAdjustment> adjustments = Load();
        adjustment.HatId ??= string.Empty;
        adjustment.AccessoryIds ??= new();
        adjustment.AccessoryIds = adjustment.AccessoryIds
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (adjustment.HasItems)
            adjustments[outfitName] = adjustment;
        else
            adjustments.Remove(outfitName);

        Save(adjustments);
    }

    public void RenameOutfit(string oldName, string newName)
    {
        Dictionary<string, IndoorOutfitAdjustment> adjustments = Load();
        if (!adjustments.Remove(oldName, out IndoorOutfitAdjustment? adjustment))
            return;

        adjustments[newName] = adjustment;
        Save(adjustments);
    }

    public void RemoveOutfits(IEnumerable<string> outfitNames)
    {
        Dictionary<string, IndoorOutfitAdjustment> adjustments = Load();
        bool changed = false;
        foreach (string name in outfitNames)
            changed |= adjustments.Remove(name);

        if (changed)
            Save(adjustments);
    }

    private static Dictionary<string, IndoorOutfitAdjustment> Load()
    {
        if (!Context.IsWorldReady || !Game1.player.modData.TryGetValue(ModDataKey, out string? json)
            || string.IsNullOrWhiteSpace(json))
            return new(StringComparer.OrdinalIgnoreCase);

        try
        {
            Dictionary<string, IndoorOutfitAdjustment>? saved =
                JsonSerializer.Deserialize<Dictionary<string, IndoorOutfitAdjustment>>(json);
            return new(saved ?? new(), StringComparer.OrdinalIgnoreCase);
        }
        catch
        {
            return new(StringComparer.OrdinalIgnoreCase);
        }
    }

    private static void Save(Dictionary<string, IndoorOutfitAdjustment> adjustments)
    {
        if (!Context.IsWorldReady)
            return;

        if (adjustments.Count == 0)
            Game1.player.modData.Remove(ModDataKey);
        else
            Game1.player.modData[ModDataKey] = JsonSerializer.Serialize(adjustments);
    }
}
