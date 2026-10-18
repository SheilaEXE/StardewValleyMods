using StardewModdingAPI;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace OutfitReactions.Ai
{
    /// <summary>
    /// Loads, normalizes, publishes, and resolves NPC character profiles used by AI generation.
    /// </summary>
    internal sealed class CharacterProfileCatalog
    {
        internal const string AssetName = "Mods/NatrollEXE.OutfitReactions/NpcCharacteristics";

        private readonly IModHelper helper;
        private readonly IMonitor monitor;
        private readonly VoiceSampleService voiceSamples;
        private volatile Dictionary<string, CharacterAiProfile> profiles = new(StringComparer.OrdinalIgnoreCase);
        private bool hasLoggedProfileLoad;

        public Func<string, bool> IsRomanceableNpc { get; set; }

        public CharacterProfileCatalog(IModHelper helper, IMonitor monitor, VoiceSampleService voiceSamples)
        {
            this.helper = helper;
            this.monitor = monitor;
            this.voiceSamples = voiceSamples;
        }

        public IReadOnlyList<string> GetNpcNames() => profiles.Keys.ToArray();

        public void Clear()
        {
            profiles = new Dictionary<string, CharacterAiProfile>(StringComparer.OrdinalIgnoreCase);
        }

        public void Load(bool quiet = false)
        {
            bool logThisLoad = !quiet && !hasLoggedProfileLoad;
            Dictionary<string, CharacterAiProfile> loaded = null;

            try
            {
                loaded = helper.GameContent.Load<Dictionary<string, CharacterAiProfile>>(AssetName);
            }
            catch (Exception ex)
            {
                monitor.Log(" Failed to load NPC characteristics from asset " + AssetName + ": " + ex.Message + ". Falling back to files.", LogLevel.Warn);
            }

            if (loaded == null || loaded.Count <= 0)
            {
                monitor.Log(" NPC characteristics asset is empty or unavailable. Falling back to assets/npc-characteristics files.", LogLevel.Trace);
                loaded = LoadDefaultProfilesFromFiles();
            }

            if (loaded == null || loaded.Count <= 0)
            {
                monitor.Log(" No usable NPC characteristics were loaded. Built-in AI generation will be skipped and fallbacks may be used.", LogLevel.Warn);
                return;
            }

            Dictionary<string, CharacterAiProfile> normalizedProfiles = new(StringComparer.OrdinalIgnoreCase);
            foreach (var pair in loaded)
            {
                CharacterAiProfile profile = pair.Value;
                NormalizeProfile(pair.Key, profile, mirrorExtraPortraitsToPortraits: true, isRomanceableLookup: IsRomanceableNpc);

                if (profile == null || string.IsNullOrWhiteSpace(profile.NpcName) || !profile.Enabled)
                    continue;

                normalizedProfiles[profile.NpcName] = profile;
                if (logThisLoad)
                    monitor.Log($" Loaded NPC characteristics for {profile.NpcName} with {profile.Portraits?.Count ?? 0} portrait descriptions.", LogLevel.Debug);
            }

            if (logThisLoad)
            {
                monitor.Log($" Loaded {normalizedProfiles.Count} usable NPC characteristic profile(s).", LogLevel.Debug);
                hasLoggedProfileLoad = true;
            }

            // Atomic reference replacement keeps background readers on a stable snapshot.
            profiles = normalizedProfiles;
        }

        public Dictionary<string, CharacterAiProfile> LoadDefaultProfilesFromFiles()
        {
            Dictionary<string, CharacterAiProfile> result = new(StringComparer.OrdinalIgnoreCase);
            JsonSerializerOptions options = new()
            {
                PropertyNameCaseInsensitive = true,
                ReadCommentHandling = JsonCommentHandling.Skip,
                AllowTrailingCommas = true
            };

            try
            {
                string mainFolder = Path.Combine(helper.DirectoryPath, "assets", "npc-characteristics");
                LoadProfilesFromFolder(result, mainFolder, options, "Outfit Compliments");
            }
            catch (Exception ex)
            {
                monitor.Log(" Failed to load built-in NPC characteristic files: " + ex.Message, LogLevel.Warn);
            }

            try
            {
                foreach (IContentPack pack in helper.ContentPacks.GetOwned())
                {
                    string folder = Path.Combine(pack.DirectoryPath, "assets", "npc-characteristics");
                    LoadProfilesFromFolder(result, folder, options, pack.Manifest.Name);
                }
            }
            catch (Exception ex)
            {
                monitor.Log(" Failed to load NPC characteristics from Outfit Compliments content packs: " + ex.Message, LogLevel.Warn);
            }

            return result;
        }

        public bool HasProfile(string npcName)
        {
            return TryResolveProfile(npcName, null, out _);
        }

        public bool TryResolveProfile(string internalName, string displayName, out CharacterAiProfile profile)
        {
            profile = null;
            Dictionary<string, CharacterAiProfile> profileSnapshot = profiles;

            if (!string.IsNullOrWhiteSpace(internalName)
                && profileSnapshot.TryGetValue(internalName, out profile) && profile != null && profile.Enabled)
                return true;

            if (!string.IsNullOrWhiteSpace(displayName)
                && profileSnapshot.TryGetValue(displayName, out profile) && profile != null && profile.Enabled)
                return true;

            if (!string.IsNullOrWhiteSpace(internalName)
                && voiceSamples.TryReverseAlias(internalName, out string aliasDisplayName)
                && profileSnapshot.TryGetValue(aliasDisplayName, out profile) && profile != null && profile.Enabled)
                return true;

            profile = null;
            return false;
        }

        private void LoadProfilesFromFolder(
            Dictionary<string, CharacterAiProfile> result,
            string folder,
            JsonSerializerOptions options,
            string sourceName)
        {
            if (!Directory.Exists(folder))
            {
                monitor.Log($" NPC characteristics folder was not found for {sourceName}: {folder}", LogLevel.Trace);
                return;
            }

            foreach (string file in Directory.GetFiles(folder, "*.json", SearchOption.TopDirectoryOnly))
            {
                try
                {
                    CharacterAiProfile profile = JsonSerializer.Deserialize<CharacterAiProfile>(File.ReadAllText(file), options);
                    string fallbackName = Path.GetFileNameWithoutExtension(file);
                    NormalizeProfile(fallbackName, profile, mirrorExtraPortraitsToPortraits: false);

                    if (profile == null || string.IsNullOrWhiteSpace(profile.NpcName))
                        continue;

                    result[profile.NpcName] = profile;
                }
                catch (Exception ex)
                {
                    monitor.Log($" Skipped invalid NPC characteristics file '{Path.GetFileName(file)}' from {sourceName}: {ex.Message}", LogLevel.Warn);
                }
            }
        }

        private static void NormalizeProfile(string fallbackName, CharacterAiProfile profile, bool mirrorExtraPortraitsToPortraits = true, Func<string, bool> isRomanceableLookup = null)
        {
            if (profile == null)
                return;

            if (string.IsNullOrWhiteSpace(profile.NpcName))
                profile.NpcName = fallbackName ?? "";

            profile.NarrativeProfile ??= new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            profile.RelationshipScaling ??= new Dictionary<string, CharacterRelationshipScalingProfile>(StringComparer.OrdinalIgnoreCase);
            profile.DialogueModes ??= new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            profile.TraitNarratives ??= new Dictionary<string, CharacterTraitNarrativeProfile>(StringComparer.OrdinalIgnoreCase);
            profile.Portraits ??= new Dictionary<string, PortraitProfile>(StringComparer.OrdinalIgnoreCase);
            profile.ExtraPortraits ??= new Dictionary<string, PortraitProfile>(StringComparer.OrdinalIgnoreCase);
            profile.Family ??= new Dictionary<string, CharacterRelationshipProfile>(StringComparer.OrdinalIgnoreCase);
            profile.Relationships ??= new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            NormalizePortraitDictionary(profile.Portraits);
            NormalizePortraitDictionary(profile.ExtraPortraits);

            if (mirrorExtraPortraitsToPortraits)
            {
                foreach (var pair in profile.ExtraPortraits)
                {
                    if (!profile.Portraits.ContainsKey(pair.Key))
                        profile.Portraits[pair.Key] = pair.Value;
                }

                bool isRomanceable = isRomanceableLookup?.Invoke(profile.NpcName) ?? false;
                AddCommonVanillaPortraits(profile.Portraits, isRomanceable);
            }
        }

        private static void AddCommonVanillaPortraits(Dictionary<string, PortraitProfile> portraits, bool isRomanceable)
        {
            if (portraits == null)
                return;

            AddCommonPortrait(portraits, "h", "$h", "happy or warmly pleased expression; smiling, amused, or genuinely positive");
            AddCommonPortrait(portraits, "s", "$s", "sad, worried, hurt, disappointed, or emotionally softened expression");

            if (isRomanceable)
            {
                AddCommonPortrait(portraits, "a", "$a", "angry, irritated, defensive, or visibly frustrated expression");
                AddCommonPortrait(portraits, "l", "$l", "blushing, shy, affectionate, or touched expression");
            }
        }

        private static void AddCommonPortrait(Dictionary<string, PortraitProfile> portraits, string key, string command, string description)
        {
            if (portraits.ContainsKey(key))
                return;

            portraits[key] = new PortraitProfile
            {
                Command = command,
                Description = description
            };
        }

        private static void NormalizePortraitDictionary(Dictionary<string, PortraitProfile> portraits)
        {
            if (portraits == null)
                return;

            foreach (string key in portraits.Keys.ToList())
            {
                PortraitProfile portrait = portraits[key] ?? new PortraitProfile();
                if (string.IsNullOrWhiteSpace(portrait.Command))
                    portrait.Command = "$" + key;
                if (string.IsNullOrWhiteSpace(portrait.Description))
                    portrait.Description = key;
                portraits[key] = portrait;
            }
        }
    }
}
