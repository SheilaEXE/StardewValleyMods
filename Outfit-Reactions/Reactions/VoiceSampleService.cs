using StardewModdingAPI;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace OutfitReactions.Ai
{
    /// <summary>
    /// Owns everything about VOICE SAMPLES: reading real in-game dialogue for an NPC,
    /// cleaning/scoring it, picking the most characteristic lines, and the display->internal
    /// name alias map. This map is the single source of truth for name resolution and is also
    /// consumed by OutfitAiService for profile lookup (see TryResolveInternalNameFromAlias).
    ///
    /// Lines come from whatever the game currently has loaded at Characters/Dialogue/&lt;Name&gt;,
    /// so dialogue mods naturally provide their own lines.
    /// </summary>
    internal sealed class VoiceSampleService
    {
        private readonly IModHelper helper;
        private readonly IMonitor monitor;

        // Cache of cleaned (dialogueKey, line) pairs per NPC. Cleaning does not depend on
        // context, so it's cached once; the context-aware scoring/selection runs per reaction.
        // In-memory only: rebuilt each game session.
        // This cache is read by background AI work and populated by the game thread before that
        // work starts. ConcurrentDictionary keeps those reads/writes safe without ever allowing a
        // worker to touch SMAPI's GameContent API.
        private readonly ConcurrentDictionary<string, List<(string Key, string Line)>> voiceSampleCache = new(StringComparer.OrdinalIgnoreCase);

        public VoiceSampleService(IModHelper helper, IMonitor monitor)
        {
            this.helper = helper;
            this.monitor = monitor;
        }

        // Display name -> internal dialogue/character name, for NPCs whose internal name differs
        // from the name their profile is stored under. Single source of truth: used both for
        // reading dialogue here AND for profile resolution in OutfitAiService.
        private static readonly Dictionary<string, string> NameAliases = new(StringComparer.OrdinalIgnoreCase)
        {
            { "Magnus", "Wizard" },
            { "Morris", "MorrisTod" },
            { "Mr. Aguar", "Aguar" },
            { "Lil Acorn", "Acorn" },
            { "Hank", "HankSVE" },
            // Sunberry Village uses an "SBV" suffix on internal/dialogue names.
            { "Elias", "EliasSBV" },
            { "Amina", "AminaSBV" },
            { "Moon", "MoonSBV" },
            { "Pan", "PanSBV" },
            { "Raccoon", "RaccoonSBV" },
            { "Aicha", "AichaSBV" },
            { "Ari", "AriSBV" },
            { "Blake", "BlakeSBV" },
            { "Derya", "DeryaSBV" },
            { "Diala", "DialaSBV" },
            { "Ezra", "EzraSBV" },
            { "Iman", "ImanSBV" },
            { "Jumana", "JumanaSBV" },
            { "Lyenne", "LyenneSBV" },
            { "Maia", "MaiaSBV" },
            { "Miyoung", "MiyoungSBV" },
            { "Nadia", "NadiaSBV" },
            { "Ophelia", "OpheliaSBV" },
            { "Reihana", "ReihanaSBV" },
            { "Silas", "SilasSBV" },
            // East Scarp: several NPCs use suffixed internal/dialogue names (author tags).
            { "Victoria", "ToriLK" },
            { "Corwin", "CorwinLK" },
            { "Kataryna", "KatarynaLK" },
            { "Vivienne", "VivienneLK" },
            { "Dale", "DaleWaede" },
            { "Keanu", "KeanuAvis" },
            { "Edith", "EdithHart" },
            { "Ethan", "EthanHart" },
            { "Michael", "MichaelHart" },
            { "Stella", "StellaHart" },
            { "Eyvind", "Eyvinder" },
            { "Lexi", "Leximonster" },
            { "Luma", "LumaJunimo" },
            { "Josephine", "JosephineK" },
            { "Oliver", "OliverK" },
            { "Jade", "JadeMalic" },
            // East Scarp base + add-ons whose internal names carry author tags/prefixes.
            // Tristan's dialogue file is Tristan.json but it injects into the asset
            // "Characters/Dialogue/TristanLK" (confirmed by the file's Target), so the alias is needed.
            { "Tristan", "TristanLK" },
            { "Eli", "Nova.Eli" },
            { "Dylan", "Nova.Dylan" }
        };

        /// <summary>
        /// If a display name maps to a different internal name, return it; otherwise return the
        /// same name. (e.g. "Victoria" -> "ToriLK", "Abigail" -> "Abigail".)
        /// </summary>
        public string ResolveInternalName(string displayName)
        {
            if (!string.IsNullOrWhiteSpace(displayName)
                && NameAliases.TryGetValue(displayName, out string aliased)
                && !string.IsNullOrWhiteSpace(aliased))
                return aliased;
            return displayName;
        }

        /// <summary>
        /// Reverse lookup for profile resolution: given an internal name (e.g. "ToriLK"), find a
        /// display name whose alias points to it (e.g. "Victoria"). Returns true and sets
        /// displayName when found. Used by OutfitAiService.TryResolveProfile.
        /// </summary>
        public bool TryReverseAlias(string internalName, out string displayName)
        {
            displayName = null;
            if (string.IsNullOrWhiteSpace(internalName))
                return false;
            foreach (KeyValuePair<string, string> kv in NameAliases)
            {
                if (string.Equals(kv.Value, internalName, StringComparison.OrdinalIgnoreCase))
                {
                    displayName = kv.Key;
                    return true;
                }
            }
            return false;
        }

        /// <summary>
        /// Appends the VOICE REFERENCE block (a few real in-game lines, tone only) to the prompt.
        /// Never throws into the caller; voice samples are a non-critical enhancement.
        /// </summary>
        public void AppendToPrompt(StringBuilder builder, OutfitAiContext context, ModConfig config)
        {
            try
            {
                if (config == null || !config.UseVoiceSamples)
                    return;
                if (context == null || string.IsNullOrWhiteSpace(context.NpcName))
                    return;
                if (IsExcluded(context.NpcName, config.VoiceSampleExcludedNpcs))
                    return;

                int count = Math.Clamp(config.VoiceSampleCount, 1, 20);
                List<string> samples = GetCachedSamplesForNpc(context.NpcName, count, context?.Season);
                if (samples == null || samples.Count == 0)
                    return;

                // Diagnostic: show exactly which sample lines were chosen and injected into THIS
                // prompt. Debug keeps normal play quiet (enable Debug/Trace SMAPI logging to see).
                if (monitor != null)
                {
                    if (OutfitReactions.ModEntry.DebugLog) monitor.Log($"[VoiceSamples] {context.NpcName} ({context.NpcDisplayName}): injecting {samples.Count} voice sample line(s) into this prompt.", LogLevel.Info);
                    for (int i = 0; i < samples.Count; i++)
                    {
                        string preview = samples[i];
                        if (preview != null && preview.Length > 120)
                            preview = preview.Substring(0, 120) + "...";
                        monitor.Log($"[VoiceSamples]   {i + 1}. {preview}", LogLevel.Trace);
                    }
                }

                builder.AppendLine("VOICE REFERENCE (real in-game lines from " + context.NpcDisplayName + ", for TONE only):");
                builder.AppendLine("These are examples of how this character actually talks. Match their voice, rhythm, vocabulary, humor, and attitude. Do NOT copy, quote, translate, or reuse their content, topics, or sentences; they are not about the outfit. The personality above always wins if anything conflicts.");
                foreach (string line in samples)
                    builder.AppendLine("- " + line);
                builder.AppendLine();
            }
            catch (Exception ex)
            {
                monitor?.Log("Voice samples skipped for " + (context?.NpcName ?? "?") + ": " + ex.Message, LogLevel.Trace);
            }
        }

        public static bool IsExcluded(string npcName, string excludedCsv)
        {
            if (string.IsNullOrWhiteSpace(excludedCsv))
                return false;
            foreach (string part in excludedCsv.Split(','))
            {
                if (string.Equals(part.Trim(), npcName.Trim(), StringComparison.OrdinalIgnoreCase))
                    return true;
            }
            return false;
        }

        /// <summary>
        /// Loads one NPC's dialogue pool while running on the game thread. This must be called
        /// before any background AI generation that may use voice samples for that NPC.
        /// </summary>
        public void PrepareSamplesForNpc(string npcName, ModConfig config)
        {
            if (config == null || !config.UseVoiceSamples
                || string.IsNullOrWhiteSpace(npcName)
                || IsExcluded(npcName, config.VoiceSampleExcludedNpcs)
                || voiceSampleCache.ContainsKey(npcName))
            {
                return;
            }

            // GameContent is deliberately accessed only here, on the SMAPI/game thread.
            List<(string Key, string Line)> pool = LoadAndCleanDialogueLines(npcName);
            if (!voiceSampleCache.TryAdd(npcName, pool))
                return;

            if (pool.Count > 0)
            {
                if (ModEntry.DebugLog) monitor?.Log($"[VoiceSamples] {npcName}: {pool.Count} usable line(s) found (will pick the configured amount).", LogLevel.Info);
            }
            else
            {
                monitor?.Log($"[VoiceSamples] {npcName}: NO usable lines found. This NPC will rely on its profile only (no voice samples). If this NPC is from a mod, its internal name may differ from the profile's NpcName, or it may not use the standard dialogue file.", LogLevel.Warn);
            }
        }

        private List<string> GetCachedSamplesForNpc(string npcName, int count, string season)
        {
            // Never load GameContent from the Task.Run generation path. If a caller did not
            // prepare the cache on the game thread, simply omit this optional prompt enhancement.
            if (!voiceSampleCache.TryGetValue(npcName, out List<(string Key, string Line)> pool))
                return new List<string>();

            if (pool.Count == 0)
                return new List<string>();
            if (pool.Count <= count)
                return pool.Select(p => p.Line).ToList();

            string seasonLower = season?.Trim().ToLowerInvariant() ?? "";
            return pool
                .Select(p => new { p.Line, Score = ScoreLine(p.Key, p.Line, seasonLower) })
                .OrderByDescending(x => x.Score)
                .Take(count)
                .Select(x => x.Line)
                .ToList();
        }

        /// <summary>
        /// Scores a dialogue line for use as a VOICE sample. Higher = more representative of how
        /// the character talks. Favors expressive, well-sized lines; light bonus to current-season
        /// lines. Purely heuristic and cheap.
        /// </summary>
        private static int ScoreLine(string key, string line, string currentSeasonLower)
        {
            if (string.IsNullOrWhiteSpace(line))
                return int.MinValue;

            int score = 0;
            int len = line.Length;

            // 1) Sweet-spot length: long enough to show voice, short enough to stay punchy.
            if (len >= 40 && len <= 160) score += 3;
            else if (len > 160 && len <= 220) score += 2;
            else if (len >= 20 && len < 40) score += 1;
            else score -= 1; // very short fragments

            // 2) Expressiveness markers — signs of personality rather than flat/functional text.
            if (line.Contains("...")) score += 1;            // hesitation, trailing thought
            if (line.Contains("!")) score += 1;              // emotion/emphasis
            if (line.Contains("?")) score += 1;              // engagement, questions
            if (line.Contains(",")) score += 1;              // fuller, more natural sentences
            if (line.IndexOf("haha", StringComparison.OrdinalIgnoreCase) >= 0
                || line.IndexOf("heh", StringComparison.OrdinalIgnoreCase) >= 0) score += 1; // humor

            // 3) Light context bonus: line belongs to the current season (keys like "spring_12").
            if (!string.IsNullOrEmpty(currentSeasonLower)
                && !string.IsNullOrEmpty(key)
                && key.ToLowerInvariant().StartsWith(currentSeasonLower)) score += 2;

            // 4) Small penalties for low-signal content.
            int wordCount = line.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries).Length;
            if (wordCount <= 3) score -= 2;                  // too terse to convey voice

            return score;
        }

        /// <summary>
        /// Diagnostic: report how many usable voice-sample lines each loaded profile has.
        /// Called by the oc_test_voicesamples console command.
        /// </summary>
        public string BuildReport(IEnumerable<string> profileNames, ModConfig config)
        {
            var sb = new StringBuilder();
            sb.AppendLine("=== Voice Sample Report ===");
            config ??= new ModConfig();
            sb.AppendLine($"UseVoiceSamples: {config.UseVoiceSamples} | Count: {config.VoiceSampleCount} | Excluded: \"{config.VoiceSampleExcludedNpcs}\"");
            sb.AppendLine("(This reads dialogue currently loaded at Characters/Dialogue/<Name>, so mod-replaced lines are included.)");
            sb.AppendLine();

            List<string> names = (profileNames ?? Enumerable.Empty<string>())
                .OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToList();
            if (names.Count == 0)
            {
                sb.AppendLine("No NPC profiles are loaded.");
                return sb.ToString();
            }

            int withLines = 0, without = 0, excluded = 0;
            foreach (string name in names)
            {
                if (IsExcluded(name, config.VoiceSampleExcludedNpcs))
                {
                    sb.AppendLine($"  [EXCLUDED] {name}");
                    excluded++;
                    continue;
                }
                List<(string Key, string Line)> pool;
                try
                {
                    pool = LoadAndCleanDialogueLines(name);
                }
                catch (Exception ex)
                {
                    sb.AppendLine($"  [ERROR]    {name}: {ex.Message}");
                    without++;
                    continue;
                }
                if (pool.Count > 0)
                {
                    sb.AppendLine($"  [OK]       {name}: {pool.Count} usable line(s)");
                    withLines++;
                }
                else
                {
                    sb.AppendLine($"  [NONE]     {name}: no usable lines (profile-only; check internal name if this is a mod NPC)");
                    without++;
                }
            }

            sb.AppendLine();
            sb.AppendLine($"Summary: {withLines} with samples, {without} without, {excluded} excluded, out of {names.Count} profiles.");
            return sb.ToString();
        }

        /// <summary>
        /// Diagnostic: for ONE npc, show exactly which voice-sample lines would be selected and
        /// injected into the prompt right now. resolvedName should be the profile's NpcName when a
        /// profile matched (or the raw requested name otherwise); profileMatched controls the label.
        /// Called by the oc_preview_voicesamples console command.
        /// </summary>
        public string BuildPreview(string requestedName, string resolvedName, bool profileMatched, string currentSeason, ModConfig config)
        {
            var sb = new StringBuilder();
            sb.AppendLine("=== Voice Sample Preview ===");

            if (string.IsNullOrWhiteSpace(requestedName))
            {
                sb.AppendLine("Usage: oc_preview_voicesamples <NpcName>  (e.g. oc_preview_voicesamples Victoria)");
                return sb.ToString();
            }

            config ??= new ModConfig();
            string nameForLookup = string.IsNullOrWhiteSpace(resolvedName) ? requestedName : resolvedName;

            sb.AppendLine($"Requested: \"{requestedName}\" | Profile match: {(profileMatched ? "yes (" + nameForLookup + ")" : "NONE")}");

            string lookupName = ResolveInternalName(nameForLookup);
            sb.AppendLine($"Reading dialogue from: Characters/Dialogue/{lookupName}");
            sb.AppendLine($"UseVoiceSamples: {config.UseVoiceSamples} | Count: {config.VoiceSampleCount}");

            if (IsExcluded(nameForLookup, config.VoiceSampleExcludedNpcs))
                sb.AppendLine("NOTE: this NPC is in the exclusion list, so no samples are injected in-game.");
            sb.AppendLine();

            int count = Math.Clamp(config.VoiceSampleCount, 1, 20);
            List<(string Key, string Line)> pool;
            try
            {
                pool = LoadAndCleanDialogueLines(nameForLookup);
            }
            catch (Exception ex)
            {
                sb.AppendLine($"[ERROR] Could not read dialogue: {ex.Message}");
                return sb.ToString();
            }

            if (pool == null || pool.Count == 0)
            {
                sb.AppendLine("NO usable lines found. The NPC will rely on its profile only.");
                // Probe the asset directly so the cause shows up in the console (not just Trace).
                sb.AppendLine("Diagnosis: " + ProbeDialogueAsset(ResolveInternalName(nameForLookup)));
                sb.AppendLine("If this is a mod NPC, its internal name may differ from the profile's NpcName (add an alias), or it may not use the standard dialogue file.");
                return sb.ToString();
            }

            sb.AppendLine($"Usable pool: {pool.Count} line(s). These are the {Math.Min(count, pool.Count)} that would be injected right now:");
            sb.AppendLine();

            string seasonLower = currentSeason?.Trim().ToLowerInvariant() ?? "";
            List<string> picked = pool.Count <= count
                ? pool.Select(p => p.Line).ToList()
                : pool.Select(p => new { p.Line, Score = ScoreLine(p.Key, p.Line, seasonLower) })
                      .OrderByDescending(x => x.Score)
                      .Take(count)
                      .Select(x => x.Line)
                      .ToList();

            int n = 1;
            foreach (string line in picked)
                sb.AppendLine($"  {n++}. {line}");

            return sb.ToString();
        }

        /// <summary>
        /// Diagnostic helper for the preview command: checks the dialogue asset directly and
        /// returns a human-readable status (does it exist? is it empty? how many raw entries?),
        /// so the cause of a NONE shows up in the console output rather than only in Trace logs.
        /// </summary>
        private string ProbeDialogueAsset(string internalName)
        {
            string assetPath = "Characters/Dialogue/" + internalName;
            Dictionary<string, string> raw;
            try
            {
                raw = helper.GameContent.Load<Dictionary<string, string>>(assetPath);
            }
            catch (Exception ex)
            {
                return $"asset '{assetPath}' does NOT exist / failed to load ({ex.GetType().Name}). " +
                       "The mod likely doesn't register this exact asset in the current game state, " +
                       "or uses a different internal name. This is a content/availability issue, not a bug in this mod.";
            }
            if (raw == null || raw.Count == 0)
                return $"asset '{assetPath}' loaded but is EMPTY (0 entries).";

            // Asset exists with entries, but everything got filtered out (gift/event keys, narration,
            // length limits). Report the raw count so we know there WAS material.
            return $"asset '{assetPath}' exists with {raw.Count} raw entr(y/ies), but ALL were filtered out " +
                   "(non-sample keys, player-narration %, or length limits). If this NPC's lines look fine, " +
                   "the filters may be too strict for this mod.";
        }

        private List<(string Key, string Line)> LoadAndCleanDialogueLines(string npcName)
        {
            var cleaned = new List<(string Key, string Line)>();

            // Resolve display name -> internal dialogue name when they differ.
            string lookupName = npcName;
            if (NameAliases.TryGetValue(npcName, out string aliased) && !string.IsNullOrWhiteSpace(aliased))
            {
                lookupName = aliased;
                monitor?.Log($"[VoiceSamples] {npcName}: reading dialogue from internal name 'Characters/Dialogue/{lookupName}' (alias).", LogLevel.Trace);
            }

            Dictionary<string, string> raw = null;
            try
            {
                // Whatever the game currently has loaded — base game or mod-replaced.
                raw = helper.GameContent.Load<Dictionary<string, string>>("Characters/Dialogue/" + lookupName);
            }
            catch (Exception ex)
            {
                // Asset doesn't exist (or failed to load). For conditionally-loaded modded NPCs
                // (e.g. SVE Adventurer's Guild members), this can mean the asset isn't registered
                // in the current game state. Logged at Trace to aid diagnosis.
                monitor?.Log($"[VoiceSamples] {npcName}: no dialogue asset at 'Characters/Dialogue/{lookupName}' ({ex.GetType().Name}).", LogLevel.Trace);
                return cleaned;
            }
            if (raw == null || raw.Count == 0)
            {
                monitor?.Log($"[VoiceSamples] {npcName}: dialogue asset 'Characters/Dialogue/{lookupName}' loaded but is EMPTY.", LogLevel.Trace);
                return cleaned;
            }

            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (KeyValuePair<string, string> entry in raw)
            {
                // Skip keys that aren't ordinary spoken lines (gift/event/schedule reactions, etc.).
                if (LooksLikeNonSampleKey(entry.Key))
                    continue;

                foreach (string piece in SplitDialogueIntoLines(entry.Value))
                {
                    // Skip player-narration segments BEFORE cleaning (cleaning would strip the
                    // leading % marker). In Stardew dialogue, a segment beginning with % is stage
                    // direction / narration from the player's perspective (e.g. "%Sebastian hands
                    // me a jar..."), not the NPC speaking. Including these as voice samples poisons
                    // the tone, so they're excluded. Confirmed against expanded-dialogue mods.
                    if (LooksLikePlayerNarration(piece))
                        continue;

                    string line = CleanDialogueLineForSample(piece);
                    if (string.IsNullOrWhiteSpace(line))
                        continue;
                    if (line.Length < 12 || line.Length > 220) // skip tiny fragments and long monologues
                        continue;
                    if (!seen.Add(line))
                        continue;
                    cleaned.Add((entry.Key, line));
                    // No early line cut: collect the NPC's WHOLE usable dialogue so scoring can pick
                    // the most characteristic lines from all of it. The 2000 ceiling is purely a
                    // safety net against a pathological huge asset; after filtering, no real
                    // Stardew NPC (even very talkative ones) comes close.
                    if (cleaned.Count >= 2000)
                        return cleaned;
                }
            }
            return cleaned;
        }

        private static bool LooksLikeNonSampleKey(string key)
        {
            if (string.IsNullOrWhiteSpace(key))
                return true;
            string k = key.ToLowerInvariant();
            // Reaction/system entries that aren't representative casual speech.
            string[] skip = { "acceptgift", "rejectgift", "birthday", "event_", "resort", "schedule",
                              "mountain_", "introduction", "datingmemory", "memory", "funleave", "funreturn",
                              "spousepatio", "spouseroom", "wedding", "divorce", "jealous", "molestress" };
            foreach (string s in skip)
                if (k.Contains(s))
                    return true;
            return false;
        }

        private static IEnumerable<string> SplitDialogueIntoLines(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                yield break;
            // Stardew dialogue boxes are separated by #$b#; questions/branches use #$e#, etc.
            string[] parts = value.Split(new string[] { "#$b#", "#$e#", "#$d#" }, StringSplitOptions.RemoveEmptyEntries);
            foreach (string p in parts)
                yield return p;
        }

        /// <summary>
        /// True if a dialogue segment is player-perspective narration / stage direction rather than
        /// the NPC actually speaking. The reliable signal is a leading '%' marker (Stardew's
        /// convention for narrated action), which must be checked on the RAW segment before cleaning
        /// strips it. This is intentionally conservative: only the '%' marker is used, so real NPC
        /// lines (even first-person emotional ones) are never dropped.
        /// </summary>
        private static bool LooksLikePlayerNarration(string rawSegment)
        {
            if (string.IsNullOrWhiteSpace(rawSegment))
                return false;
            string s = rawSegment.TrimStart();
            // Tolerate a stray leading quote/space before the marker.
            s = s.TrimStart('"', '\'', ' ', '\t');
            if (!s.StartsWith("%", StringComparison.Ordinal))
                return false;

            // Distinguish two different uses of a leading '%':
            //  - "%Hank: actual line"  -> a SPEAKER-NAME PREFIX (some mods, e.g. SVE). This IS the
            //    NPC speaking; it must NOT be treated as narration (the prefix is stripped later).
            //  - "%Sebastian walks toward me..." -> stage direction / player narration. Discard.
            // The deciding signal is a "Name:" immediately after the % (a short proper name
            // followed by a colon).
            if (LooksLikeSpeakerNamePrefix(s))
                return false;

            return true;
        }

        /// <summary>True for "%Name:" style speaker prefixes (kept), false otherwise.</summary>
        private static bool LooksLikeSpeakerNamePrefix(string trimmedSegment)
        {
            if (string.IsNullOrEmpty(trimmedSegment) || trimmedSegment[0] != '%')
                return false;
            // % followed by a short name (letters/space) and then a colon, e.g. "%Hank:", "%Mr. Aguar:".
            return Regex.IsMatch(trimmedSegment, @"^%[\p{L}][\p{L} .'-]{0,20}:");
        }

        private static string CleanDialogueLineForSample(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return "";
            string t = text;

            // Strip a leading speaker-name prefix like "%Hank: " (used by some mods, e.g. SVE).
            // The line itself is real NPC speech; we only drop the "%Name:" tag.
            t = Regex.Replace(t.TrimStart(), @"^%[\p{L}][\p{L} .'-]{0,20}:\s*", "");

            // Drop any branch/question payloads after a fork marker.
            int fork = t.IndexOf("#$", StringComparison.Ordinal);
            if (fork >= 0)
                t = t.Substring(0, fork);

            // Remove portrait/emotion commands like $h $s $neutral $1 $k, gender forks ${..}, and tokens {{..}}.
            t = Regex.Replace(t, @"\$[a-zA-Z]+\b", " ");      // $neutral, $angry, $h ...
            t = Regex.Replace(t, @"\$-?\d+", " ");             // $1, $16, $-1
            t = Regex.Replace(t, @"\{\{.*?\}\}", " ");         // {{token}}
            t = Regex.Replace(t, @"\$\{.*?\}", " ");           // ${male^female}
            t = Regex.Replace(t, @"%[a-zA-Z]+", " ");          // %farm, %name placeholders
            t = t.Replace("#$b#", " ").Replace("@", "").Replace("^", " ");
            // Stray Stardew control leftovers.
            t = Regex.Replace(t, @"\$[a-z]\b", " ", RegexOptions.IgnoreCase);
            // Collapse whitespace.
            t = Regex.Replace(t, @"\s+", " ").Trim();
            // Trim surrounding quotes.
            t = t.Trim('"', '\'', '*', ' ');
            return t;
        }
    }
}
