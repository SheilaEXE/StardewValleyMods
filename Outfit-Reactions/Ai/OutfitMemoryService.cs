using StardewModdingAPI;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace OutfitReactions.Ai
{
    /// <summary>
    /// Persists per-NPC outfit memories across sessions so NPCs can recognise
    /// outfits they have already seen and comment on any component changes.
    /// </summary>
    internal sealed class OutfitMemoryService
    {
        // ── Storage key ────────────────────────────────────────────────────────
        private const string SaveKey = "OutfitMemories";
        private const int CurrentMemorySchemaVersion = 2;

        // ── Dependencies ───────────────────────────────────────────────────────
        private readonly IModHelper helper;
        private readonly IMonitor monitor;

        // ── In-memory state ────────────────────────────────────────────────────
        // npcName (lower) → outfitId (lower) → memory
        private Dictionary<string, Dictionary<string, OutfitMemoryEntry>> memories = new(StringComparer.OrdinalIgnoreCase);
        private bool dirty = false;

        // ── Constructor ────────────────────────────────────────────────────────
        public OutfitMemoryService(IModHelper helper, IMonitor monitor)
        {
            this.helper = helper;
            this.monitor = monitor;
        }

        // ── Public API ─────────────────────────────────────────────────────────

        /// <summary>Load memories from disk (call on save loaded).</summary>
        public void Load()
        {
            try
            {
                var saved = helper.Data.ReadSaveData<OutfitMemoryData>(SaveKey);
                if (saved == null)
                {
                    memories = new Dictionary<string, Dictionary<string, OutfitMemoryEntry>>(StringComparer.OrdinalIgnoreCase);
                    dirty = false;
                    if (OutfitReactions.ModEntry.DebugLog) monitor.Log("[OUTFIT MEMORY] No saved outfit memories found.", LogLevel.Info);
                    return;
                }

                if (saved.Version < CurrentMemorySchemaVersion)
                {
                    // Older test builds could accidentally attach an accessory from the previous
                    // saved outfit to the new saved outfit (e.g. Pikachu + moth wings -> dinosaur).
                    // Reset once so NPCs don't keep repeating corrupted cross-outfit memories.
                    memories = new Dictionary<string, Dictionary<string, OutfitMemoryEntry>>(StringComparer.OrdinalIgnoreCase);
                    dirty = true;
                    if (OutfitReactions.ModEntry.DebugLog) monitor.Log($"[OUTFIT MEMORY] Old memory schema v{saved.Version} detected; clearing outfit memories to prevent corrupted accessory/outfit associations.", LogLevel.Info);
                    return;
                }

                memories = saved.Memories
                    ?? new Dictionary<string, Dictionary<string, OutfitMemoryEntry>>(StringComparer.OrdinalIgnoreCase);
                dirty = false;
                if (OutfitReactions.ModEntry.DebugLog) monitor.Log($"[OUTFIT MEMORY] Loaded memories for {memories.Count} NPC(s).", LogLevel.Info);
            }
            catch (Exception ex)
            {
                monitor.Log($"[OUTFIT MEMORY] Failed to load memories: {ex.Message}", LogLevel.Warn);
                memories = new Dictionary<string, Dictionary<string, OutfitMemoryEntry>>(StringComparer.OrdinalIgnoreCase);
            }
        }

        /// <summary>Save memories to disk (call on day ending / save).</summary>
        public void Save()
        {
            if (!dirty)
                return;
            try
            {
                helper.Data.WriteSaveData(SaveKey, new OutfitMemoryData { Version = CurrentMemorySchemaVersion, Memories = memories });
                dirty = false;
                if (OutfitReactions.ModEntry.DebugLog) monitor.Log("[OUTFIT MEMORY] Memories saved.", LogLevel.Info);
            }
            catch (Exception ex)
            {
                monitor.Log($"[OUTFIT MEMORY] Failed to save memories: {ex.Message}", LogLevel.Warn);
            }
        }

        /// <summary>
        /// Check if an NPC has seen this outfit before and return comparison info.
        /// Returns null if this is the first time.
        /// </summary>
        public OutfitMemoryComparison GetMemory(string npcName, string outfitId, OutfitComponents current)
        {
            if (string.IsNullOrWhiteSpace(npcName) || string.IsNullOrWhiteSpace(outfitId))
                return null;

            if (!memories.TryGetValue(npcName, out var npcMemories))
                return null;

            if (!npcMemories.TryGetValue(outfitId, out var entry))
                return null;

            entry.Components ??= new OutfitComponents();
            entry.AccessoryHistory ??= new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            current ??= new OutfitComponents();

            // Build list of changed components
            var changes = new List<OutfitComponentChange>();
            CompareComponent(changes, "Hat",       entry.Components.Hat,       current.Hat);
            CompareComponent(changes, "Hair",      entry.Components.Hair,      current.Hair);
            CompareComponent(changes, "Shirt",     entry.Components.Shirt,     current.Shirt);
            CompareComponent(changes, "Pants",     entry.Components.Pants,     current.Pants);
            CompareComponent(changes, "Sleeves",   entry.Components.Sleeves,   current.Sleeves);
            CompareComponent(changes, "Accessory", entry.Components.Accessory, current.Accessory);

            string currentAccessory = NormalizeAccessoryMemoryKey(current?.Accessory);
            int currentAccessorySeenBefore = 0;
            if (!string.IsNullOrWhiteSpace(currentAccessory) && entry.AccessoryHistory != null)
                entry.AccessoryHistory.TryGetValue(currentAccessory, out currentAccessorySeenBefore);

            return new OutfitMemoryComparison
            {
                FirstSeenSeason             = entry.FirstSeenSeason,
                FirstSeenDay                = entry.FirstSeenDay,
                FirstSeenYear               = entry.FirstSeenYear,
                TimesSeenBefore             = entry.TimesSeen,
                ComponentChanges            = changes,
                LastRecordedAccessory       = entry.Components?.Accessory ?? "",
                CurrentAccessory            = current?.Accessory ?? "",
                CurrentAccessorySeenBefore  = currentAccessorySeenBefore
            };
        }

        /// <summary>
        /// Record (or update) the memory for this NPC + outfit.
        /// Call this after the compliment dialogue has been shown.
        /// </summary>
        public void RecordMemory(string npcName, string outfitId, OutfitComponents components,
                                  string season, int day, int year)
        {
            if (string.IsNullOrWhiteSpace(npcName) || string.IsNullOrWhiteSpace(outfitId))
                return;

            if (!memories.TryGetValue(npcName, out var npcMemories))
            {
                npcMemories = new Dictionary<string, OutfitMemoryEntry>(StringComparer.OrdinalIgnoreCase);
                memories[npcName] = npcMemories;
            }

            if (npcMemories.TryGetValue(outfitId, out var existing))
            {
                // Update components (player may have tweaked the saved outfit)
                // and increment the seen counter. Keep a small per-outfit accessory history
                // so the NPC can recognise an accessory that was removed and later put back on.
                components ??= new OutfitComponents();
                // Seed history from the last recorded state too, so saves created before
                // accessory history existed can still remember the accessory when it is removed.
                RecordAccessoryHistory(existing, existing.Components?.Accessory);
                RecordAccessoryHistory(existing, components.Accessory);
                existing.Components  = components;
                existing.TimesSeen  += 1;
            }
            else
            {
                components ??= new OutfitComponents();
                var created = new OutfitMemoryEntry
                {
                    OutfitId        = outfitId,
                    FirstSeenSeason = season,
                    FirstSeenDay    = day,
                    FirstSeenYear   = year,
                    TimesSeen       = 1,
                    Components      = components
                };
                RecordAccessoryHistory(created, components.Accessory);
                npcMemories[outfitId] = created;
            }

            dirty = true;
            if (OutfitReactions.ModEntry.DebugLog) monitor.Log($"[OUTFIT MEMORY] Recorded memory of '{outfitId}' for {npcName}.", LogLevel.Info);
        }

        /// <summary>Build an English internal context string to inject into the AI prompt.</summary>
        public string BuildMemoryContextHint(OutfitMemoryComparison memory, string targetLanguage)
        {
            if (memory == null)
                return null;

            // Prompt instructions remain English for every output language. The provider still
            // writes the final dialogue in targetLanguage, but internal metadata must not mix
            // localized examples into the instruction layer.
            bool isPt = false;

            string firstSeenLabel = FormatFirstSeen(memory, isPt);
            int times = memory.TimesSeenBefore;
            var changes = memory.ComponentChanges;

            string accessoryMemoryNote = BuildAccessoryMemoryNote(memory, isPt);

            if (isPt)
            {
                string freq = times == 1
                    ? "você já usou esse conjunto antes (uma vez)"
                    : $"você já usou esse conjunto antes ({times} vezes)";

                if (changes.Count == 0)
                {
                    return $"MEMÓRIA DO PERSONAGEM: {freq}, da primeira vez foi em {firstSeenLabel}. " +
                           "O conjunto está idêntico ao que você viu antes — exatamente as mesmas peças. " +
                           "Reaja com carinho e reconhecimento: mencione que lembra desse look, " +
                           "que fica feliz de ver novamente, que é um look favorito, etc. " +
                           accessoryMemoryNote +
                           "NÃO reaja como se fosse a primeira vez.";
                }
                else
                {
                    string changesDesc = string.Join(", ", changes.Select(c =>
                        $"{TranslateComponentPt(c.ComponentName)} mudou de '{BuildSafeHint(c.OldValue)}' para '{BuildSafeHint(c.NewValue)}'"));
                    return $"MEMÓRIA DO PERSONAGEM: {freq}, da primeira vez foi em {firstSeenLabel}. " +
                           $"O conjunto é basicamente o mesmo, mas algumas peças mudaram: {changesDesc}. " +
                           "Reconheça o look que já conhece e comente naturalmente sobre a(s) peça(s) diferente(s), " +
                           "como se tivesse notado a mudança agora. A reação pode ser curiosa, engraçada, estranhada, " +
                           "implicante, carinhosa ou dramática conforme a personalidade do NPC e o tema do visual. " +
                           "Se um acessório foi removido, trocado ou adicionado, pode comparar com como o conjunto estava antes. " +
                           accessoryMemoryNote +
                           "NÃO reaja como se fosse a primeira vez.";
                }
            }
            else
            {
                string freq = times == 1
                    ? "you have worn this outfit before (once)"
                    : $"you have worn this outfit before ({times} times)";

                if (changes.Count == 0)
                {
                    return $"CHARACTER MEMORY: {freq}, first worn {firstSeenLabel}. " +
                           "The outfit is identical to the one seen before — every piece is the same. " +
                           "React with warmth and recognition: mention remembering this look, " +
                           "being happy to see it again, it being a favourite, etc. " +
                           accessoryMemoryNote +
                           "Do NOT react as if seeing it for the first time.";
                }
                else
                {
                    string changesDesc = string.Join(", ", changes.Select(c =>
                        $"{c.ComponentName} changed from '{BuildSafeHint(c.OldValue)}' to '{BuildSafeHint(c.NewValue)}'"));
                    return $"CHARACTER MEMORY: {freq}, first worn {firstSeenLabel}. " +
                           $"The outfit is mostly the same but some pieces changed: {changesDesc}. " +
                           "Recognise the familiar look and naturally comment on the changed piece(s) as if you just noticed the difference. " +
                           "The reaction may be curious, funny, weirded-out, teasing, warm, dramatic, or practical depending on the NPC personality and outfit theme. " +
                           "If an accessory was removed, swapped, or added, you may compare it to how the outfit looked before. " +
                           accessoryMemoryNote +
                           "Do NOT react as if seeing it for the first time.";
                }
            }
        }

        // ── Helpers ────────────────────────────────────────────────────────────

        private static void RecordAccessoryHistory(OutfitMemoryEntry entry, string accessory)
        {
            if (entry == null)
                return;

            string key = NormalizeAccessoryMemoryKey(accessory);
            if (string.IsNullOrWhiteSpace(key))
                return;

            entry.AccessoryHistory ??= new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            entry.AccessoryHistory.TryGetValue(key, out int count);
            entry.AccessoryHistory[key] = count + 1;
        }

        private static string BuildAccessoryMemoryNote(OutfitMemoryComparison memory, bool isPt)
        {
            if (memory == null || memory.CurrentAccessorySeenBefore <= 0 || string.IsNullOrWhiteSpace(memory.CurrentAccessory))
                return "";

            string current = BuildSafeHint(memory.CurrentAccessory);
            if (isPt)
            {
                string times = memory.CurrentAccessorySeenBefore == 1
                    ? "uma vez"
                    : memory.CurrentAccessorySeenBefore + " vezes";
                return $"Esse acessório atual ('{current}') também já apareceu com esse mesmo conjunto antes ({times}); se ele tinha sido removido e agora voltou, reconheça que ele voltou/foi colocado de novo em vez de tratar como primeira vez. ";
            }
            else
            {
                string times = memory.CurrentAccessorySeenBefore == 1
                    ? "once"
                    : memory.CurrentAccessorySeenBefore + " times";
                return $"This current accessory ('{current}') has also appeared with this same outfit before ({times}); if it had been removed and is now back, recognise that it returned/was put back on instead of treating it like the first time. ";
            }
        }

        private static string NormalizeAccessoryMemoryKey(string accessory)
        {
            if (string.IsNullOrWhiteSpace(accessory))
                return "";

            string[] parts = accessory
                .Split(new[] { " + " }, StringSplitOptions.RemoveEmptyEntries)
                .Select(part => part.Trim())
                .Where(part => !string.IsNullOrWhiteSpace(part) && !IsIgnoredMakeupAccessoryValue(part))
                .ToArray();

            return string.Join(" + ", parts);
        }

        private static bool IsIgnoredMakeupAccessoryValue(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return false;

            string lower = " " + value.ToLowerInvariant().Replace('_', ' ').Replace('-', ' ').Replace('.', ' ') + " ";
            return lower.Contains(" makeup ")
                || lower.Contains(" maquiagem ")
                || lower.Contains(" blush ")
                || lower.Contains(" lipstick ")
                || lower.Contains(" batom ")
                || lower.Contains(" eyeshadow ")
                || lower.Contains(" eye shadow ")
                || lower.Contains(" sombra ")
                || lower.Contains(" eyeliner ")
                || lower.Contains(" delineador ")
                || lower.Contains(" rimel ")
                || lower.Contains(" rímel ");
        }

        private static void CompareComponent(List<OutfitComponentChange> changes,
            string name, string oldVal, string newVal)
        {
            oldVal = oldVal ?? "";
            newVal = newVal ?? "";
            if (string.Equals(name, "Accessory", StringComparison.OrdinalIgnoreCase))
            {
                oldVal = NormalizeAccessoryMemoryKey(oldVal);
                newVal = NormalizeAccessoryMemoryKey(newVal);
            }

            if (!string.Equals(oldVal, newVal, StringComparison.OrdinalIgnoreCase))
                changes.Add(new OutfitComponentChange { ComponentName = name, OldValue = oldVal, NewValue = newVal });
        }

        private static string FormatFirstSeen(OutfitMemoryComparison m, bool isPt)
        {
            string season = isPt ? TranslateSeasonPt(m.FirstSeenSeason) : m.FirstSeenSeason;
            if (isPt)
                return $"{season}, dia {m.FirstSeenDay}, ano {m.FirstSeenYear}";
            return $"{season} {m.FirstSeenDay}, Year {m.FirstSeenYear}";
        }

        private static string TranslateSeasonPt(string season) => season?.ToLowerInvariant() switch
        {
            "spring" => "primavera",
            "summer" => "verão",
            "fall"   => "outono",
            "winter" => "inverno",
            _        => season ?? ""
        };

        private static string TranslateComponentPt(string component) => component?.ToLowerInvariant() switch
        {
            "hat"       => "chapéu",
            "hair"      => "cabelo",
            "shirt"     => "blusa",
            "pants"     => "calça",
            "sleeves"   => "mangas",
            "accessory" => "acessório",
            _           => component ?? ""
        };

        /// <summary>
        /// Convert a raw item ID into a readable hint without revealing the exact technical name.
        /// Mirrors the approach used in BuildSafeOutfitNameHint in ModEntry.
        /// </summary>
        private static string BuildSafeHint(string id)
        {
            if (string.IsNullOrWhiteSpace(id))
                return "none";
            // Strip author prefix (e.g. "AuthorName.PackName.ItemName" → "Item Name")
            string last = id.Contains('.') ? id.Substring(id.LastIndexOf('.') + 1) : id;
            // CamelCase → spaced
            last = System.Text.RegularExpressions.Regex.Replace(last, @"(?<=[a-z])(?=[A-Z])", " ");
            // Remove digits-only segments
            last = System.Text.RegularExpressions.Regex.Replace(last, @"\s*\d+\s*", " ").Trim();
            return string.IsNullOrWhiteSpace(last) ? "unknown" : last;
        }
    }

    // ── Data models ────────────────────────────────────────────────────────────

    internal sealed class OutfitMemoryData
    {
        [JsonPropertyName("Version")]
        public int Version { get; set; } = 2;

        [JsonPropertyName("Memories")]
        public Dictionary<string, Dictionary<string, OutfitMemoryEntry>> Memories { get; set; }
            = new(StringComparer.OrdinalIgnoreCase);
    }

    internal sealed class OutfitMemoryEntry
    {
        [JsonPropertyName("OutfitId")]
        public string OutfitId { get; set; } = "";

        [JsonPropertyName("FirstSeenSeason")]
        public string FirstSeenSeason { get; set; } = "";

        [JsonPropertyName("FirstSeenDay")]
        public int FirstSeenDay { get; set; }

        [JsonPropertyName("FirstSeenYear")]
        public int FirstSeenYear { get; set; }

        [JsonPropertyName("TimesSeen")]
        public int TimesSeen { get; set; } = 1;

        [JsonPropertyName("Components")]
        public OutfitComponents Components { get; set; } = new();

        [JsonPropertyName("AccessoryHistory")]
        public Dictionary<string, int> AccessoryHistory { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    }

    internal sealed class OutfitComponents
    {
        [JsonPropertyName("Hat")]       public string Hat       { get; set; } = "";
        [JsonPropertyName("Hair")]      public string Hair      { get; set; } = "";
        [JsonPropertyName("Shirt")]     public string Shirt     { get; set; } = "";
        [JsonPropertyName("Pants")]     public string Pants     { get; set; } = "";
        [JsonPropertyName("Sleeves")]   public string Sleeves   { get; set; } = "";
        [JsonPropertyName("Accessory")] public string Accessory { get; set; } = "";
    }

    internal sealed class OutfitMemoryComparison
    {
        public string FirstSeenSeason      { get; set; } = "";
        public int    FirstSeenDay         { get; set; }
        public int    FirstSeenYear        { get; set; }
        public int    TimesSeenBefore      { get; set; }
        public List<OutfitComponentChange> ComponentChanges { get; set; } = new();
        public string LastRecordedAccessory { get; set; } = "";
        public string CurrentAccessory { get; set; } = "";
        public int CurrentAccessorySeenBefore { get; set; }
    }

    internal sealed class OutfitComponentChange
    {
        public string ComponentName { get; set; } = "";
        public string OldValue      { get; set; } = "";
        public string NewValue      { get; set; } = "";
    }
}
