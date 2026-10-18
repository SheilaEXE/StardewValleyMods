using StardewModdingAPI;
using StardewValley;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace OutfitReactions.Ai
{
    /// <summary>
    /// Loads and queries <c>assets/special-reactions/luckypurpleshorts.json</c> to inject
    /// NPC-specific or item-specific reaction context into AI prompts.
    /// Also manages the per-NPC "knows the secret" modData flags that persist
    /// across sessions for items with a <c>SecretId</c>.
    /// </summary>
    internal sealed class SpecialItemReactionService
    {
        // ── modData key prefix ────────────────────────────────────────────────
        // Full key: "NatrollEXE.OutfitReactions/Secret/{SecretId}/{NpcName}"
        private const string SecretModDataPrefix = "NatrollEXE.OutfitReactions/Secret/";

        private readonly IModHelper helper;
        private readonly IMonitor monitor;
        private SpecialItemDefinitions definitions;
        private bool missingFileLogged;
        private readonly List<string> entryLookupOrder = new();
        private readonly Dictionary<string, List<string>> globalRulesByEntryId = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, string> sourceByEntryId = new(StringComparer.OrdinalIgnoreCase);
        private static readonly HashSet<string> ProtectedSecretEntryIds = new(StringComparer.OrdinalIgnoreCase)
        {
            "MayorsPurpleShorts",
            "MayorsPurpleShortsHat"
        };
        // Cache of which optional mod IDs are currently installed.
        private readonly HashSet<string> installedModIds = new(StringComparer.OrdinalIgnoreCase);
        private bool installedModIdsLoaded;

        public SpecialItemReactionService(IModHelper helper, IMonitor monitor)
        {
            this.helper = helper;
            this.monitor = monitor;
        }

        public sealed class ResolvedSpecialItem
        {
            public string EntryId { get; set; } = "";
            public string DisplayName { get; set; } = "";
            public string ItemType { get; set; } = "";
            public string MatchedName { get; set; } = "";
            public string ReactionContext { get; set; } = "";
            public bool HasSecret { get; set; }
            public string SecretId { get; set; } = "";
            public bool NpcKnowsSecret { get; set; }
        }

        // ── Public API ────────────────────────────────────────────────────────

        /// <summary>
        /// Builds a reaction-context string for an item currently being worn
        /// (outfit piece or mod hat), injecting NPC-specific overrides and
        /// secret knowledge when applicable.
        /// </summary>
        public string BuildContextForCurrentItem(string itemName, string itemType, NPC npc, string targetLanguage)
        {
            return TryResolveItem(new[] { itemName }, itemType, npc, targetLanguage, out ResolvedSpecialItem resolved)
                ? resolved.ReactionContext
                : "";
        }

        public bool TryResolveItem(IEnumerable<string> candidateNames, string itemType, NPC npc, string targetLanguage, out ResolvedSpecialItem resolved, bool wasRemoved = false)
        {
            resolved = null;

            try
            {
                if (candidateNames == null)
                    return false;

                foreach (string rawName in candidateNames)
                {
                    if (string.IsNullOrWhiteSpace(rawName))
                        continue;

                    string candidateName = rawName.Trim();
                    if (!TryFindEntry(candidateName, itemType, out string entryId, out SpecialItemEntry entry))
                        continue;

                    string npcName = npc?.Name ?? "";
                    bool npcKnowsSecret = !string.IsNullOrWhiteSpace(npcName)
                        && (NpcKnowsSecret(entry.SecretId, npcName) || NpcKnowsByDefault(entry, npcName));

                    string displayName = StringUtils.FirstNonEmpty(GetLocalizedName(entry, targetLanguage), entry.DisplayName, entryId) ?? entryId;
                    string context = BuildPromptContext(entryId, entry, npcName, targetLanguage, npcKnowsSecret, wasRemoved);

                    resolved = new ResolvedSpecialItem
                    {
                        EntryId = entryId,
                        DisplayName = displayName,
                        ItemType = StringUtils.FirstNonEmpty(entry.ItemType, itemType) ?? "",
                        MatchedName = candidateName,
                        ReactionContext = context,
                        HasSecret = !string.IsNullOrWhiteSpace(entry.SecretId),
                        SecretId = entry.SecretId ?? "",
                        NpcKnowsSecret = npcKnowsSecret
                    };

                    return true;
                }

                return false;
            }
            catch (Exception ex)
            {
                if (ModEntry.DebugLog) monitor?.Log("[SPECIAL ITEM] Error resolving special item: " + ex.Message, LogLevel.Info);
                resolved = null;
                return false;
            }
        }

        /// <summary>
        /// Returns the SecretRevealMessage for the entry that owns the given secretId,
        /// or "" if not found. Used as the "player reply" in the secret follow-up prompt.
        /// </summary>
        public string GetSecretRevealMessage(string secretId)
        {
            if (string.IsNullOrWhiteSpace(secretId))
                return "";
            try
            {
                SpecialItemDefinitions data = LoadDefinitions();
                if (data?.Items == null)
                    return "";
                foreach (var pair in data.Items)
                {
                    if (pair.Value != null
                        && string.Equals(pair.Value.SecretId, secretId, StringComparison.OrdinalIgnoreCase)
                        && !string.IsNullOrWhiteSpace(pair.Value.SecretRevealMessage))
                        return pair.Value.SecretRevealMessage;
                }
            }
            catch { }
            return "";
        }

        /// <summary>
        /// Gets a randomly selected translation key for a scripted special-item action.
        /// The JSON selects keys, while the visible text remains in the mod's i18n files.
        /// </summary>
        public string GetNpcScriptedDialogueKey(string entryId, string npcName, string situation)
        {
            if (string.IsNullOrWhiteSpace(entryId) || string.IsNullOrWhiteSpace(npcName) || string.IsNullOrWhiteSpace(situation))
                return "";

            try
            {
                SpecialItemDefinitions data = LoadDefinitions();
                if (data?.Items == null
                    || !data.Items.TryGetValue(entryId, out SpecialItemEntry entry)
                    || entry?.NpcOverrides == null
                    || !entry.NpcOverrides.TryGetValue(npcName, out SpecialItemNpcOverride npcOverride)
                    || npcOverride?.ScriptedDialogueKeys == null
                    || !npcOverride.ScriptedDialogueKeys.TryGetValue(situation, out List<string> keys))
                {
                    return "";
                }

                List<string> usableKeys = keys.Where(key => !string.IsNullOrWhiteSpace(key)).ToList();
                return usableKeys.Count == 0 ? "" : usableKeys[Game1.random.Next(usableKeys.Count)];
            }
            catch (Exception ex)
            {
                if (ModEntry.DebugLog)
                    monitor?.Log("[SPECIAL ITEM] Could not resolve scripted dialogue key: " + ex.Message, LogLevel.Info);
                return "";
            }
        }

        /// <summary>
        /// Directly reveals the secret for the given secretId to the given NPC,
        /// persisting the flag in modData. Returns true if newly revealed.
        /// </summary>
        public bool RevealSecret(string secretId, string npcName)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(secretId) || string.IsNullOrWhiteSpace(npcName) || Game1.player == null)
                    return false;

                if (NpcKnowsSecret(secretId, npcName))
                    return false;

                string modDataKey = SecretModDataPrefix + secretId + "/" + npcName;
                Game1.player.modData[modDataKey] = "1";

                if (ModEntry.DebugLog)
                    monitor?.Log($"[SPECIAL ITEM] Secret '{secretId}' revealed to {npcName} via direct choice.", LogLevel.Info);

                return true;
            }
            catch (Exception ex)
            {
                if (ModEntry.DebugLog) monitor?.Log("[SPECIAL ITEM] Error in RevealSecret: " + ex.Message, LogLevel.Info);
                return false;
            }
        }

        /// <summary>
        /// Returns true if the current outfit name or mod-hat name matches any
        /// special item entry, so callers can decide whether to run follow-up
        /// secret detection.
        /// </summary>
        /// <summary>
        /// Clears the installed-mod-IDs cache so it is repopulated on the next
        /// query. Call this after all mods are guaranteed to be loaded (e.g. on
        /// SaveLoaded) to ensure ConditionalMatchNames are evaluated correctly.
        /// </summary>
        public void ResetModRegistryCache()
        {
            installedModIds.Clear();
            installedModIdsLoaded = false;
            definitions = null;
            entryLookupOrder.Clear();
            globalRulesByEntryId.Clear();
            sourceByEntryId.Clear();
        }

        public bool HasEntryForItem(string itemName, string itemType)
        {
            try
            {
                return TryFindEntry(itemName, itemType, out _, out _);
            }
            catch { return false; }
        }

        // ── Secret modData helpers ────────────────────────────────────────────

        public bool NpcKnowsSecret(string secretId, string npcName)
        {
            if (string.IsNullOrWhiteSpace(secretId) || string.IsNullOrWhiteSpace(npcName) || Game1.player == null)
                return false;

            return Game1.player.modData.ContainsKey(SecretModDataPrefix + secretId + "/" + npcName);
        }

        /// <summary>
        /// Returns true if the NPC already knows the secret — either because they have
        /// <c>KnowsSecretByDefault</c> in their NpcOverride, or because it was previously
        /// revealed and persisted in modData. Use this to decide whether to show the
        /// secret-reveal choice menu (skip it when this returns true).
        /// </summary>
        public bool NpcAlreadyKnowsSecret(string secretId, string npcName)
        {
            if (string.IsNullOrWhiteSpace(secretId) || string.IsNullOrWhiteSpace(npcName))
                return false;

            if (NpcKnowsSecret(secretId, npcName))
                return true;

            // Check KnowsSecretByDefault, but skip NPCs marked as SecretRevealable —
            // those should still see the reveal menu even though they know in-lore.
            try
            {
                SpecialItemDefinitions data = LoadDefinitions();
                if (data?.Items == null)
                    return false;

                foreach (var pair in data.Items)
                {
                    if (pair.Value == null || !string.Equals(pair.Value.SecretId, secretId, StringComparison.OrdinalIgnoreCase))
                        continue;

                    if (!NpcKnowsByDefault(pair.Value, npcName))
                        continue;

                    // If SecretRevealable is true, treat as "doesn't know yet" for menu purposes.
                    if (pair.Value.NpcOverrides != null
                        && pair.Value.NpcOverrides.TryGetValue(npcName, out SpecialItemNpcOverride ov)
                        && ov != null
                        && ov.SecretRevealable)
                        return false;

                    return true;
                }
            }
            catch { }

            return false;
        }

        // ── Internal ─────────────────────────────────────────────────────────

        private bool IsModInstalled(string modId)
        {
            if (string.IsNullOrWhiteSpace(modId))
                return false;

            if (!installedModIdsLoaded)
            {
                installedModIdsLoaded = true;
                foreach (var mod in helper.ModRegistry.GetAll())
                    if (!string.IsNullOrWhiteSpace(mod.Manifest?.UniqueID))
                        installedModIds.Add(mod.Manifest.UniqueID);
            }

            return installedModIds.Contains(modId);
        }

        private static bool NpcKnowsByDefault(SpecialItemEntry entry, string npcName)
        {
            if (entry?.NpcOverrides == null || string.IsNullOrWhiteSpace(npcName))
                return false;

            return entry.NpcOverrides.TryGetValue(npcName, out SpecialItemNpcOverride ov)
                && ov != null
                && ov.KnowsSecretByDefault;
        }

        private bool TryFindEntry(string itemName, string itemType, out string entryId, out SpecialItemEntry entry)
        {
            entryId = "";
            entry = null;

            SpecialItemDefinitions data = LoadDefinitions();
            if (data?.Items == null || data.Items.Count == 0)
                return false;

            string nameLower = NormalizeForMatch(itemName);
            string typeLower = NormalizeItemType(itemType);

            IEnumerable<string> orderedIds = entryLookupOrder.Count > 0
                ? entryLookupOrder
                : data.Items.Keys;

            foreach (string id in orderedIds)
            {
                if (!data.Items.TryGetValue(id, out SpecialItemEntry candidate))
                    continue;
                if (candidate == null)
                    continue;

                // Optional type filter: if entry declares ItemType, it must match.
                if (!string.IsNullOrWhiteSpace(candidate.ItemType)
                    && !string.IsNullOrWhiteSpace(typeLower)
                    && !NormalizeItemType(candidate.ItemType).Equals(typeLower, StringComparison.OrdinalIgnoreCase))
                    continue;

                foreach (string matchName in GetAllMatchNames(id, candidate))
                {
                    if (string.IsNullOrWhiteSpace(matchName))
                        continue;

                    if (NormalizeForMatch(matchName).Equals(nameLower, StringComparison.OrdinalIgnoreCase))
                    {
                        entryId = id;
                        entry = candidate;
                        return true;
                    }
                }
            }

            return false;
        }

        private static string NormalizeItemType(string itemType)
        {
            string normalized = (itemType ?? "").Trim().ToLowerInvariant();
            return normalized == "boots" || normalized == "shoe" ? "shoes" : normalized;
        }

        private string BuildPromptContext(string entryId, SpecialItemEntry entry, string npcName, string targetLanguage, bool npcKnowsSecret, bool wasRemoved = false)
        {
            List<string> parts = new();
            bool protectUnknownSecret = !string.IsNullOrWhiteSpace(entry.SecretId) && !npcKnowsSecret;

            if (protectUnknownSecret)
                parts.Add("special item: an unidentified intimate garment of unknown ownership");
            else
                parts.Add("special item: " + StringUtils.FirstNonEmpty(entry.DisplayName, entryId));

            string localized = GetLocalizedName(entry, targetLanguage);
            if (!protectUnknownSecret
                && !string.IsNullOrWhiteSpace(localized)
                && !localized.Equals(entry.DisplayName, StringComparison.OrdinalIgnoreCase))
                parts.Add("localized name: " + localized);

            if (!string.IsNullOrWhiteSpace(entry.ItemType))
                parts.Add("item type: " + entry.ItemType);

            if (!string.IsNullOrWhiteSpace(entry.ReactionPriority))
                parts.Add("reaction priority: " + entry.ReactionPriority);

            if (!string.IsNullOrWhiteSpace(entry.CoreDescription))
                parts.Add("description: " + entry.CoreDescription);

            // NPC-specific override takes priority over generic hint.
            SpecialItemNpcOverride npcOverride = null;
            if (!string.IsNullOrWhiteSpace(npcName) && entry.NpcOverrides != null)
                entry.NpcOverrides.TryGetValue(npcName, out npcOverride);

            string reactionHint = npcKnowsSecret
                ? StringUtils.FirstNonEmpty(npcOverride?.SecretReactionHint, npcOverride?.ReactionHint, entry.SecretReactionHint, entry.ReactionHint)
                : StringUtils.FirstNonEmpty(npcOverride?.ReactionHint, entry.ReactionHint);

            if (!string.IsNullOrWhiteSpace(reactionHint))
                parts.Add("reaction hint: " + reactionHint);

            if (protectUnknownSecret)
            {
                parts.Add("secret boundary: this NPC can clearly recognize that the garment looks like bright purple personal underwear and should react strongly to how strange, intimate, embarrassing, absurd, or inappropriate it is; however, they do NOT know its owner or origin. Do not identify or guess Lewis, the mayor, Marnie, their relationship, where the garment was found, or any hidden backstory. Base-game lore and model prior knowledge must not reveal the secret before the farmer explicitly tells this NPC");
            }

            if (npcKnowsSecret && !string.IsNullOrWhiteSpace(entry.SecretId))
                parts.Add("secret context: this NPC already knows the secret behind this item — factor that prior knowledge into the reaction");

            // When the item was just removed, add explicit framing so the NPC reacts to the
            // absence rather than treating the item as currently worn.
            if (wasRemoved)
                parts.Add("item status: JUST REMOVED — the farmer is no longer wearing this item. React to its absence (relief, disappointment, curiosity about why it was taken off, etc.), not to it being worn right now. Do NOT describe or react as if the item is currently equipped.");

            List<string> applicableRules = globalRulesByEntryId.TryGetValue(entryId, out List<string> entryRules)
                ? entryRules
                : definitions?.GlobalRules;
            if (applicableRules != null && applicableRules.Count > 0)
            {
                string rules = string.Join(" ", applicableRules.Take(3).Where(r => !string.IsNullOrWhiteSpace(r)));
                if (!string.IsNullOrWhiteSpace(rules))
                    parts.Add("global rules: " + rules);
            }

            return string.Join("; ", parts.Where(p => !string.IsNullOrWhiteSpace(p)));
        }

        private SpecialItemDefinitions LoadDefinitions()
        {
            if (definitions != null)
                return definitions;

            string path = Path.Combine(helper.DirectoryPath, "assets", "special-reactions", "luckypurpleshorts.json");
            try
            {
                if (!File.Exists(path))
                {
                    if (!missingFileLogged)
                    {
                        missingFileLogged = true;
                        if (ModEntry.DebugLog) monitor?.Log("[SPECIAL ITEM] No luckypurpleshorts.json found at assets/special-reactions/luckypurpleshorts.json. Special item reactions are disabled.", LogLevel.Info);
                    }
                    definitions = null;
                    return null;
                }

                JsonSerializerOptions opts = new()
                {
                    PropertyNameCaseInsensitive = true,
                    ReadCommentHandling = JsonCommentHandling.Skip,
                    AllowTrailingCommas = true
                };

                definitions = ReadDefinitionsFile(path, opts);
                missingFileLogged = false;

                definitions.Items ??= new Dictionary<string, SpecialItemEntry>(StringComparer.OrdinalIgnoreCase);
                foreach (var pair in definitions.Items)
                {
                    entryLookupOrder.Add(pair.Key);
                    globalRulesByEntryId[pair.Key] = definitions.GlobalRules ?? new List<string>();
                    sourceByEntryId[pair.Key] = ModEntry.Instance?.ModManifest?.UniqueID ?? "NatrollEXE.OutfitReactions";
                }

                LoadOwnedContentPackDefinitions(opts);

                // The two purple-shorts entries always stay first so a differently named pack
                // entry cannot accidentally bypass their protected secret behavior.
                foreach (string protectedId in ProtectedSecretEntryIds.Reverse())
                {
                    if (!definitions.Items.ContainsKey(protectedId))
                        continue;
                    entryLookupOrder.RemoveAll(id => id.Equals(protectedId, StringComparison.OrdinalIgnoreCase));
                    entryLookupOrder.Insert(0, protectedId);
                }

                int count = definitions.Items?.Count ?? 0;
                if (ModEntry.DebugLog) monitor?.Log($"[SPECIAL ITEM] Loaded {count} merged special item definitions (built-in + content packs).", LogLevel.Info);
                return definitions;
            }
            catch (Exception ex)
            {
                monitor?.Log("[SPECIAL ITEM] Failed to load luckypurpleshorts.json: " + ex.Message, LogLevel.Warn);
                definitions = null;
                return null;
            }
        }

        private static SpecialItemDefinitions ReadDefinitionsFile(string path, JsonSerializerOptions options)
        {
            string json = File.ReadAllText(path, Encoding.UTF8);
            return JsonSerializer.Deserialize<SpecialItemDefinitions>(json, options) ?? new SpecialItemDefinitions();
        }

        private void LoadOwnedContentPackDefinitions(JsonSerializerOptions options)
        {
            IEnumerable<IContentPack> packs = helper.ContentPacks.GetOwned()
                .OrderBy(pack => pack.Manifest?.UniqueID ?? "", StringComparer.OrdinalIgnoreCase);

            foreach (IContentPack pack in packs)
            {
                string folder = Path.Combine(pack.DirectoryPath, "assets", "special-reactions");
                if (!Directory.Exists(folder))
                    continue;

                foreach (string file in Directory.EnumerateFiles(folder, "*.json", SearchOption.AllDirectories)
                    .OrderBy(value => value, StringComparer.OrdinalIgnoreCase))
                {
                    try
                    {
                        SpecialItemDefinitions packDefinitions = ReadDefinitionsFile(file, options);
                        string sourceId = pack.Manifest?.UniqueID ?? Path.GetFileName(pack.DirectoryPath);

                        foreach (var pair in packDefinitions.Items ?? new Dictionary<string, SpecialItemEntry>())
                        {
                            if (string.IsNullOrWhiteSpace(pair.Key) || pair.Value == null)
                                continue;

                            SpecialItemEntry externalEntry = pair.Value;
                            StripExternalSecretFields(externalEntry);

                            if (ProtectedSecretEntryIds.Contains(pair.Key)
                                && definitions.Items.TryGetValue(pair.Key, out SpecialItemEntry protectedEntry))
                            {
                                RestoreProtectedSecretFields(protectedEntry, externalEntry);
                            }

                            if (sourceByEntryId.TryGetValue(pair.Key, out string previousSource))
                            {
                                monitor?.Log($"[SPECIAL ITEM] Content pack '{sourceId}' overrides entry '{pair.Key}' from '{previousSource}'.", LogLevel.Info);
                            }

                            definitions.Items[pair.Key] = externalEntry;
                            globalRulesByEntryId[pair.Key] = packDefinitions.GlobalRules ?? new List<string>();
                            sourceByEntryId[pair.Key] = sourceId;
                            entryLookupOrder.RemoveAll(id => id.Equals(pair.Key, StringComparison.OrdinalIgnoreCase));
                            entryLookupOrder.Insert(0, pair.Key);
                        }
                    }
                    catch (Exception ex)
                    {
                        monitor?.Log($"[SPECIAL ITEM] Failed to load content-pack file '{file}': {ex.Message}", LogLevel.Warn);
                    }
                }
            }
        }

        private static void StripExternalSecretFields(SpecialItemEntry entry)
        {
            entry.SecretId = "";
            entry.SecretRevealMessage = "";
            entry.SecretReactionHint = "";

            foreach (SpecialItemNpcOverride npcOverride in entry.NpcOverrides?.Values ?? Enumerable.Empty<SpecialItemNpcOverride>())
            {
                if (npcOverride == null)
                    continue;
                npcOverride.KnowsSecretByDefault = false;
                npcOverride.SecretRevealable = false;
                npcOverride.SecretReactionHint = "";
            }
        }

        private static void RestoreProtectedSecretFields(SpecialItemEntry builtIn, SpecialItemEntry external)
        {
            external.SecretId = builtIn.SecretId;
            external.SecretRevealMessage = builtIn.SecretRevealMessage;
            external.SecretReactionHint = builtIn.SecretReactionHint;

            external.NpcOverrides ??= new Dictionary<string, SpecialItemNpcOverride>(StringComparer.OrdinalIgnoreCase);
            foreach (var pair in builtIn.NpcOverrides ?? new Dictionary<string, SpecialItemNpcOverride>())
            {
                if (pair.Value == null)
                    continue;

                if (!external.NpcOverrides.TryGetValue(pair.Key, out SpecialItemNpcOverride target) || target == null)
                {
                    target = new SpecialItemNpcOverride();
                    external.NpcOverrides[pair.Key] = target;
                }

                target.KnowsSecretByDefault = pair.Value.KnowsSecretByDefault;
                target.SecretRevealable = pair.Value.SecretRevealable;
                target.SecretReactionHint = pair.Value.SecretReactionHint;
                if (string.IsNullOrWhiteSpace(target.ReactionHint))
                    target.ReactionHint = pair.Value.ReactionHint;
            }
        }

        private IEnumerable<string> GetAllMatchNames(string entryId, SpecialItemEntry entry)
        {
            yield return entryId;
            yield return entry?.DisplayName;

            if (entry?.LocalizedNames != null)
                foreach (string v in entry.LocalizedNames.Values)
                    yield return v;

            if (entry?.MatchNames != null)
                foreach (string v in entry.MatchNames)
                    yield return v;

            if (entry?.MatchIds != null)
                foreach (string v in entry.MatchIds)
                    yield return v;

            // Conditional matches are only active if the required mod is installed.
            if (entry?.ConditionalMatchNames != null)
            {
                foreach (var conditional in entry.ConditionalMatchNames)
                {
                    if (conditional == null || !IsModInstalled(conditional.RequiredModId))
                        continue;
                    if (conditional.MatchNames != null)
                        foreach (string v in conditional.MatchNames)
                            yield return v;
                    if (conditional.MatchIds != null)
                        foreach (string v in conditional.MatchIds)
                            yield return v;
                }
            }
        }

        private static string NormalizeForMatch(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return "";

            string normalized = text.Trim().ToLowerInvariant().Normalize(NormalizationForm.FormD);
            StringBuilder builder = new();
            bool lastWasSpace = false;

            foreach (char c in normalized)
            {
                UnicodeCategory category = CharUnicodeInfo.GetUnicodeCategory(c);
                if (category == UnicodeCategory.NonSpacingMark)
                    continue;

                if (char.IsLetterOrDigit(c))
                {
                    builder.Append(c);
                    lastWasSpace = false;
                }
                else if (!lastWasSpace)
                {
                    builder.Append(' ');
                    lastWasSpace = true;
                }
            }

            return builder.ToString().Trim().Normalize(NormalizationForm.FormC);
        }

        private static string GetLocalizedName(SpecialItemEntry entry, string targetLanguage)
        {
            if (entry?.LocalizedNames == null || entry.LocalizedNames.Count == 0)
                return "";

            string key = LanguageToLocalizationKey(targetLanguage);
            if (!string.IsNullOrWhiteSpace(key)
                && entry.LocalizedNames.TryGetValue(key, out string loc)
                && !string.IsNullOrWhiteSpace(loc))
                return loc;

            return "";
        }

        private static string LanguageToLocalizationKey(string lang)
        {
            if (string.IsNullOrWhiteSpace(lang)) return "";
            string lower = lang.ToLowerInvariant();
            if (lower.Contains("pt") || lower.Contains("portuguese") || lower.Contains("brazilian")) return "pt-BR";
            if (lower.Contains("es") || lower.Contains("spanish")) return "es";
            if (lower.Contains("fr") || lower.Contains("french")) return "fr";
            if (lower.Contains("de") || lower.Contains("german")) return "de";
            if (lower.Contains("it") || lower.Contains("italian")) return "it";
            if (lower.Contains("ja") || lower.Contains("japanese")) return "ja";
            if (lower.Contains("ko") || lower.Contains("korean")) return "ko";
            if (lower.Contains("ru") || lower.Contains("russian")) return "ru";
            if (lower.Contains("tr") || lower.Contains("turkish")) return "tr";
            if (lower.Contains("zh") || lower.Contains("chinese")) return "zh";
            return "";
        }

        // ── DTOs ──────────────────────────────────────────────────────────────

        private sealed class SpecialItemDefinitions
        {
            public List<string> GlobalRules { get; set; } = new();
            public Dictionary<string, SpecialItemEntry> Items { get; set; } = new(StringComparer.OrdinalIgnoreCase);
        }

        private sealed class SpecialItemEntry
        {
            public string DisplayName { get; set; } = "";
            public Dictionary<string, string> LocalizedNames { get; set; } = new(StringComparer.OrdinalIgnoreCase);
            public List<string> MatchNames { get; set; } = new();
            public List<string> MatchIds { get; set; } = new();
            public string ItemType { get; set; } = "";
            public string ReactionPriority { get; set; } = "";
            public string CoreDescription { get; set; } = "";
            public string ReactionHint { get; set; } = "";
            public string SecretId { get; set; } = "";
            public string SecretReactionHint { get; set; } = "";
            // What the player just told the NPC — used as the "player reply" in the follow-up
            // prompt so the AI knows exactly what was revealed. Keep it short and factual.
            public string SecretRevealMessage { get; set; } = "";
            public Dictionary<string, SpecialItemNpcOverride> NpcOverrides { get; set; } = new(StringComparer.OrdinalIgnoreCase);
            public List<ConditionalMatchEntry> ConditionalMatchNames { get; set; } = new();
        }

        private sealed class ConditionalMatchEntry
        {
            public string RequiredModId { get; set; } = "";
            public List<string> MatchNames { get; set; } = new();
            public List<string> MatchIds { get; set; } = new();
        }

        private sealed class SpecialItemNpcOverride
        {
            public bool KnowsSecretByDefault { get; set; }
            // When true, the secret-reveal choice menu still appears for this NPC even though
            // they have KnowsSecretByDefault — useful for NPCs who "know" in-lore but whose
            // best gameplay experience is the player "revealing" it to them (e.g. Marnie pretending
            // not to recognize Lewis's underwear while barely suppressing laughter).
            public bool SecretRevealable { get; set; }
            public string ReactionHint { get; set; } = "";
            public string SecretReactionHint { get; set; } = "";
            public Dictionary<string, List<string>> ScriptedDialogueKeys { get; set; } = new(StringComparer.OrdinalIgnoreCase);
        }
    }
}
