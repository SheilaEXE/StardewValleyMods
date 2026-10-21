using StardewModdingAPI;
using StardewValley;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace FashionSenseWardrobeManager;

/// <summary>
/// Keeps heart-button edits across a quit before the next normal Stardew save.
/// Only the edited outfits are replayed, so other Fashion Sense outfits remain untouched.
/// </summary>
internal sealed class ImmediateOutfitPersistence
{
    private const string CollectionKey = "FashionSense.Outfit.Collection";
    private const string RevisionKey = "NatrollEXE.FashionSenseWardrobeManager.ImmediateRevision";

    private readonly IModHelper _helper;
    private readonly IMonitor _monitor;
    private readonly CosmeticShoeManager _shoes;
    private PendingEdits _pending = new();
    private string? _storageKey;

    public ImmediateOutfitPersistence(IModHelper helper, IMonitor monitor, CosmeticShoeManager shoes)
    {
        _helper = helper;
        _monitor = monitor;
        _shoes = shoes;
    }

    public bool Capture(string outfitName)
    {
        if (!Context.IsWorldReady || string.IsNullOrWhiteSpace(_storageKey))
            return false;

        try
        {
            if (!Game1.player.modData.TryGetValue(CollectionKey, out string? collection))
                return false;

            JsonArray outfits = JsonNode.Parse(collection) as JsonArray
                ?? throw new JsonException("Fashion Sense outfit collection is not an array.");
            JsonNode? outfit = outfits.FirstOrDefault(item =>
                string.Equals(item?["Name"]?.GetValue<string>(), outfitName, StringComparison.OrdinalIgnoreCase));
            if (outfit is null)
                return false;

            var next = new PendingEdits
            {
                Revision = Guid.NewGuid().ToString("N"),
                Outfits = new Dictionary<string, PendingOutfit>(_pending.Outfits, StringComparer.OrdinalIgnoreCase)
            };
            next.Outfits[outfitName] = new PendingOutfit
            {
                OutfitJson = outfit.ToJsonString(),
                CosmeticShoeId = _shoes.GetShoeId(outfitName)
            };

            // WriteGlobalData writes immediately; WriteSaveData does not survive a quit
            // before the next in-game save.
            _helper.Data.WriteGlobalData(_storageKey, next);
            _pending = next;
            Game1.player.modData[RevisionKey] = next.Revision;
            return true;
        }
        catch (Exception ex)
        {
            _monitor.Log($"Could not immediately persist outfit '{outfitName}': {ex}", LogLevel.Error);
            return false;
        }
    }

    public void OnSaveLoaded()
    {
        _storageKey = $"wardrobe-immediate-{Constants.SaveFolderName}-{Game1.player.UniqueMultiplayerID}";
        _pending = new PendingEdits();

        try
        {
            PendingEdits? stored = _helper.Data.ReadGlobalData<PendingEdits>(_storageKey);
            if (stored?.Outfits is null || stored.Outfits.Count == 0)
                return;

            if (Game1.player.modData.TryGetValue(RevisionKey, out string? savedRevision)
                && savedRevision == stored.Revision)
            {
                ClearPending();
                return;
            }

            if (!Replay(stored))
                return;

            _pending = stored;
            Game1.player.modData[RevisionKey] = stored.Revision;
            _monitor.Log($"Recovered {stored.Outfits.Count} immediately saved outfit edit(s).", LogLevel.Info);
        }
        catch (Exception ex)
        {
            _monitor.Log($"Could not recover immediately saved outfit edits: {ex}", LogLevel.Error);
        }
    }

    public void OnSaved()
    {
        // The normal save now contains both Fashion Sense data and CP assignments.
        ClearPending();
    }

    private bool Replay(PendingEdits stored)
    {
        try
        {
            string collection = Game1.player.modData.TryGetValue(CollectionKey, out string? raw)
                ? raw : "[]";
            JsonArray outfits = JsonNode.Parse(collection) as JsonArray
                ?? throw new JsonException("Fashion Sense outfit collection is not an array.");

            foreach ((string name, PendingOutfit edit) in stored.Outfits)
            {
                JsonNode outfit = JsonNode.Parse(edit.OutfitJson)
                    ?? throw new JsonException($"Saved outfit '{name}' is empty.");
                if (!string.Equals(outfit["Name"]?.GetValue<string>(), name, StringComparison.OrdinalIgnoreCase))
                    throw new JsonException($"Saved outfit '{name}' has a mismatched name.");

                int existing = -1;
                for (int i = 0; i < outfits.Count; i++)
                {
                    if (string.Equals(outfits[i]?["Name"]?.GetValue<string>(), name, StringComparison.OrdinalIgnoreCase))
                    {
                        existing = i;
                        break;
                    }
                }

                if (existing >= 0)
                    outfits[existing] = outfit;
                else
                    outfits.Add(outfit);
            }

            Game1.player.modData[CollectionKey] = outfits.ToJsonString();
            foreach ((string name, PendingOutfit edit) in stored.Outfits)
                _shoes.SetShoe(name, edit.CosmeticShoeId);

            return true;
        }
        catch (Exception ex)
        {
            _monitor.Log($"Could not replay immediately saved outfits without risking the collection: {ex}", LogLevel.Error);
            return false;
        }
    }

    private void ClearPending()
    {
        if (string.IsNullOrWhiteSpace(_storageKey))
            return;

        try
        {
            _helper.Data.WriteGlobalData<PendingEdits>(_storageKey, null);
            _pending = new PendingEdits();
        }
        catch (Exception ex)
        {
            _monitor.Log($"Could not clear the immediate outfit backup: {ex}", LogLevel.Warn);
        }
    }

    public sealed class PendingEdits
    {
        public string Revision { get; set; } = string.Empty;
        public Dictionary<string, PendingOutfit> Outfits { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    }

    public sealed class PendingOutfit
    {
        public string OutfitJson { get; set; } = string.Empty;
        public string? CosmeticShoeId { get; set; }
    }
}
