using StardewModdingAPI;
using System;
using System.Collections.Generic;

namespace OutfitReactions.Ai
{
    /// <summary>
    /// Tracks, per NPC, the history of VANILLA hats the farmer has worn — completely independent
    /// of the Fashion Sense saved-outfit memory (OutfitMemoryService). This lets an NPC remember
    /// things like "you wore the Sombrero last time", "you've worn this hat several times", or
    /// "you finally took that hat off", with seasonal/temporal context, even when the player has
    /// no saved Fashion Sense outfit at all. Persisted in the save file under its own key.
    /// </summary>
    internal sealed class HatMemoryService
    {
        private const string SaveKey = "VanillaHatMemories";
        private const int CurrentSchemaVersion = 4;
        private const int MaxStoredNpcDialogueCharacters = 1200;
        private const int MaxStoredPlayerReplyCharacters = 800;

        private readonly IModHelper helper;
        private readonly IMonitor monitor;

        // npcName -> hatId -> entry
        private Dictionary<string, Dictionary<string, HatMemoryEntry>> memories
            = new(StringComparer.OrdinalIgnoreCase);

        // npcName -> the hatId most recently RECORDED for that NPC (for "last time you wore X").
        private Dictionary<string, string> lastHatPerNpc
            = new(StringComparer.OrdinalIgnoreCase);

        // Session-only drafts. A new exchange replaces the saved one only after the player either
        // leaves after the opening line or reads a successfully generated follow-up.
        private readonly Dictionary<string, HatReactionMemoryDraft> reactionDrafts
            = new(StringComparer.OrdinalIgnoreCase);

        public HatMemoryService(IModHelper helper, IMonitor monitor)
        {
            this.helper = helper;
            this.monitor = monitor;
        }

        /// <summary>
        /// Returns the display name of the last vanilla hat this NPC saw the farmer wearing
        /// (before now), or "" if none/unknown. Used to say specifically WHICH hat was removed.
        /// </summary>
        public string GetLastHatNameForNpc(string npcName)
        {
            return GetLastHatMemoryForNpc(npcName)?.HatName ?? "";
        }

        /// <summary>
        /// Returns the exact vanilla-hat entry most recently seen by this NPC. Keeping the stable
        /// item ID together with the localized display name makes removals language-independent.
        /// </summary>
        public HatMemorySnapshot GetLastHatMemoryForNpc(string npcName)
        {
            if (string.IsNullOrWhiteSpace(npcName))
                return null;
            string lastId = lastHatPerNpc.TryGetValue(npcName, out string recordedLastId) ? recordedLastId : "";
            if (string.IsNullOrWhiteSpace(lastId)
                && reactionDrafts.TryGetValue(npcName, out HatReactionMemoryDraft removalDraft)
                && removalDraft.WasRemoval)
            {
                // While a removal exchange is still in progress, keep the prior hat addressable so
                // the follow-up context can use the old completed conversation until atomic commit.
                lastId = removalDraft.HatId;
            }
            if (string.IsNullOrWhiteSpace(lastId))
                return null;
            if (!memories.TryGetValue(npcName, out var npcHats)
                || !npcHats.TryGetValue(lastId, out var entry)
                || entry == null)
                return null;

            return new HatMemorySnapshot
            {
                HatId = entry.HatId ?? lastId,
                HatName = entry.HatName ?? "",
                LastReactionText = entry.LastReactionText ?? "",
                LastPlayerReplyText = entry.LastPlayerReplyText ?? "",
                LastNpcFollowUpText = entry.LastNpcFollowUpText ?? "",
                LastReactionWasRemoval = entry.LastReactionWasRemoval
            };
        }

        /// <summary>Load hat memories from the save (call on save loaded).</summary>
        public void Load()
        {
            try
            {
                var saved = helper.Data.ReadSaveData<HatMemoryData>(SaveKey);
                if (saved?.Memories == null)
                {
                    memories = new(StringComparer.OrdinalIgnoreCase);
                    lastHatPerNpc = new(StringComparer.OrdinalIgnoreCase);
                }
                else
                {
                    memories = saved.Memories;
                    lastHatPerNpc = saved.LastHatPerNpc ?? new(StringComparer.OrdinalIgnoreCase);
                }
                reactionDrafts.Clear();
                if (OutfitReactions.ModEntry.DebugLog) monitor.Log($"[HAT MEMORY] Loaded vanilla-hat memories for {memories.Count} NPC(s).", LogLevel.Info);
            }
            catch (Exception ex)
            {
                if (OutfitReactions.ModEntry.DebugLog) monitor.Log("[HAT MEMORY] Failed to load hat memories: " + ex.Message, LogLevel.Info);
                memories = new(StringComparer.OrdinalIgnoreCase);
                lastHatPerNpc = new(StringComparer.OrdinalIgnoreCase);
                reactionDrafts.Clear();
            }
        }

        /// <summary>Persist hat memories to the save (call on saving).</summary>
        public void Save()
        {
            try
            {
                helper.Data.WriteSaveData(SaveKey, new HatMemoryData
                {
                    Version = CurrentSchemaVersion,
                    Memories = memories,
                    LastHatPerNpc = lastHatPerNpc
                });
            }
            catch (Exception ex)
            {
                if (OutfitReactions.ModEntry.DebugLog) monitor.Log("[HAT MEMORY] Failed to save hat memories: " + ex.Message, LogLevel.Info);
            }
        }

        /// <summary>
        /// Builds a memory comparison for the hat the NPC is about to react to. Returns null when
        /// there is nothing meaningful to remember (e.g. first time ever, no prior hat history).
        /// Does NOT record anything — call RecordMemory after the reaction is committed.
        /// </summary>
        public HatMemoryComparison GetMemory(string npcName, string currentHatId, string currentHatName)
        {
            if (string.IsNullOrWhiteSpace(npcName))
                return null;

            string previousHatForNpc = lastHatPerNpc.TryGetValue(npcName, out string prev) ? prev : "";
            if (string.IsNullOrWhiteSpace(previousHatForNpc)
                && reactionDrafts.TryGetValue(npcName, out HatReactionMemoryDraft removalDraft)
                && removalDraft.WasRemoval)
            {
                previousHatForNpc = removalDraft.HatId;
            }

            // No current hat: this is a "took the hat off" moment. Only meaningful if the NPC has
            // actually seen the farmer wear a hat before.
            bool currentlyHatless = string.IsNullOrWhiteSpace(currentHatId);
            if (currentlyHatless && string.IsNullOrWhiteSpace(previousHatForNpc))
                return null;

            HatMemoryEntry entry = null;
            string relevantHatId = currentlyHatless ? previousHatForNpc : currentHatId;
            if (!string.IsNullOrWhiteSpace(relevantHatId)
                && memories.TryGetValue(npcName, out var npcHats))
            {
                npcHats.TryGetValue(relevantHatId, out entry);
            }


            return new HatMemoryComparison
            {
                CurrentHatId = currentHatId ?? "",
                CurrentHatName = currentHatName ?? "",
                CurrentlyHatless = currentlyHatless,
                PreviousHatId = previousHatForNpc,
                PreviousHatName = currentlyHatless ? (entry?.HatName ?? "") : "",
                TimesSeenBefore = entry?.TimesSeen ?? 0,
                FirstSeenSeason = entry?.FirstSeenSeason ?? "",
                FirstSeenDay = entry?.FirstSeenDay ?? 0,
                FirstSeenYear = entry?.FirstSeenYear ?? 0,
                LastSeenSeason = entry?.LastSeenSeason ?? "",
                LastSeenDay = entry?.LastSeenDay ?? 0,
                LastSeenYear = entry?.LastSeenYear ?? 0,
                LastReactionText = entry?.LastReactionText ?? "",
                LastPlayerReplyText = entry?.LastPlayerReplyText ?? "",
                LastNpcFollowUpText = entry?.LastNpcFollowUpText ?? "",
                LastReactionWasRemoval = entry?.LastReactionWasRemoval ?? false,
                FirstWearingReactionText = FirstWearingText(entry),
                FirstWearingPlayerReplyText = FirstWearingPlayerReply(entry),
                FirstWearingNpcFollowUpText = FirstWearingNpcFollowUp(entry),
                LastRemovalReactionText = LastRemovalText(entry),
                LastRemovalPlayerReplyText = LastRemovalPlayerReply(entry),
                LastRemovalNpcFollowUpText = LastRemovalNpcFollowUp(entry)
            };
        }

        /// <summary>
        /// Records that the NPC saw the farmer in this hat (or hatless) now. Updates first/last
        /// seen and counts. Pass the current in-game date for temporal context.
        /// </summary>
        public void RecordMemory(string npcName, string hatId, string hatName,
            string season, int day, int year)
        {
            if (string.IsNullOrWhiteSpace(npcName))
                return;

            // Always remember what the NPC last saw, even if hatless ("" = bare-headed),
            // so we can later say "you took the hat off".
            lastHatPerNpc[npcName] = hatId ?? "";

            if (string.IsNullOrWhiteSpace(hatId))
                return; // a bare head is not another sighting of the hat

            if (!memories.TryGetValue(npcName, out var npcHats))
            {
                npcHats = new Dictionary<string, HatMemoryEntry>(StringComparer.OrdinalIgnoreCase);
                memories[npcName] = npcHats;
            }

            if (!npcHats.TryGetValue(hatId, out var entry) || entry == null)
            {
                entry = new HatMemoryEntry
                {
                    HatId = hatId,
                    HatName = hatName ?? "",
                    FirstSeenSeason = season ?? "",
                    FirstSeenDay = day,
                    FirstSeenYear = year,
                    LastSeenSeason = season ?? "",
                    LastSeenDay = day,
                    LastSeenYear = year,
                    TimesSeen = 1
                };
                npcHats[hatId] = entry;
            }
            else
            {
                entry.TimesSeen++;
                entry.LastSeenSeason = season ?? "";
                entry.LastSeenDay = day;
                entry.LastSeenYear = year;
                if (!string.IsNullOrWhiteSpace(hatName))
                    entry.HatName = hatName;
            }

        }

        public void BeginReactionDraft(string npcName, string hatId, string hatName, bool wasRemoval, string npcOpeningLine)
        {
            if (string.IsNullOrWhiteSpace(npcName))
                return;

            reactionDrafts.Remove(npcName);
            string opening = CleanStoredText(npcOpeningLine, MaxStoredNpcDialogueCharacters);
            if (string.IsNullOrWhiteSpace(hatId) || string.IsNullOrWhiteSpace(opening))
                return;

            reactionDrafts[npcName] = new HatReactionMemoryDraft
            {
                HatId = hatId.Trim(),
                HatName = (hatName ?? "").Trim(),
                WasRemoval = wasRemoval,
                NpcOpeningLine = opening
            };
        }

        public bool HasReactionDraft(string npcName)
        {
            return !string.IsNullOrWhiteSpace(npcName) && reactionDrafts.ContainsKey(npcName);
        }

        public void SetDraftPlayerReply(string npcName, string playerReply)
        {
            if (!string.IsNullOrWhiteSpace(npcName)
                && reactionDrafts.TryGetValue(npcName, out HatReactionMemoryDraft draft))
            {
                draft.PlayerReply = CleanStoredText(playerReply, MaxStoredPlayerReplyCharacters);
                // Retrying a reply must never retain an older generated follow-up in the draft.
                draft.NpcFollowUp = "";
            }
        }

        public void SetDraftNpcFollowUp(string npcName, string npcFollowUp)
        {
            if (!string.IsNullOrWhiteSpace(npcName)
                && reactionDrafts.TryGetValue(npcName, out HatReactionMemoryDraft draft))
            {
                draft.NpcFollowUp = CleanStoredText(npcFollowUp, MaxStoredNpcDialogueCharacters);
            }
        }

        public bool CommitReactionDraft(string npcName)
        {
            if (string.IsNullOrWhiteSpace(npcName)
                || !reactionDrafts.TryGetValue(npcName, out HatReactionMemoryDraft draft))
                return false;

            try
            {
                if (!memories.TryGetValue(npcName, out var npcHats))
                {
                    npcHats = new Dictionary<string, HatMemoryEntry>(StringComparer.OrdinalIgnoreCase);
                    memories[npcName] = npcHats;
                }
                if (!npcHats.TryGetValue(draft.HatId, out HatMemoryEntry entry) || entry == null)
                {
                    entry = new HatMemoryEntry
                    {
                        HatId = draft.HatId,
                        HatName = draft.HatName,
                        TimesSeen = 0
                    };
                    npcHats[draft.HatId] = entry;
                }

                if (!string.IsNullOrWhiteSpace(draft.HatName))
                    entry.HatName = draft.HatName;

                // Preserve the older single-exchange format before replacing its last reaction.
                if (string.IsNullOrWhiteSpace(entry.FirstWearingReactionText) && !entry.LastReactionWasRemoval)
                {
                    entry.FirstWearingReactionText = entry.LastReactionText;
                    entry.FirstWearingPlayerReplyText = entry.LastPlayerReplyText;
                    entry.FirstWearingNpcFollowUpText = entry.LastNpcFollowUpText;
                }
                if (string.IsNullOrWhiteSpace(entry.LastRemovalReactionText) && entry.LastReactionWasRemoval)
                {
                    entry.LastRemovalReactionText = entry.LastReactionText;
                    entry.LastRemovalPlayerReplyText = entry.LastPlayerReplyText;
                    entry.LastRemovalNpcFollowUpText = entry.LastNpcFollowUpText;
                }

                // Atomic replacement: empty reply/follow-up fields intentionally clear the prior
                // exchange when the player chose to leave after this new opening reaction.
                entry.LastReactionText = draft.NpcOpeningLine;
                entry.LastPlayerReplyText = draft.PlayerReply;
                entry.LastNpcFollowUpText = draft.NpcFollowUp;
                entry.LastReactionWasRemoval = draft.WasRemoval;
                if (draft.WasRemoval)
                {
                    entry.LastRemovalReactionText = draft.NpcOpeningLine;
                    entry.LastRemovalPlayerReplyText = draft.PlayerReply;
                    entry.LastRemovalNpcFollowUpText = draft.NpcFollowUp;
                }
                else if (string.IsNullOrWhiteSpace(entry.FirstWearingReactionText))
                {
                    entry.FirstWearingReactionText = draft.NpcOpeningLine;
                    entry.FirstWearingPlayerReplyText = draft.PlayerReply;
                    entry.FirstWearingNpcFollowUpText = draft.NpcFollowUp;
                }
                if (OutfitReactions.ModEntry.DebugLog)
                {
                    monitor?.Log($"[HAT MEMORY] Committed completed hat exchange for {npcName} and '{draft.HatId}' (removal={draft.WasRemoval}, playerReply={!string.IsNullOrWhiteSpace(draft.PlayerReply)}, npcFollowUp={!string.IsNullOrWhiteSpace(draft.NpcFollowUp)}).", LogLevel.Info);
                }
                return true;
            }
            finally
            {
                reactionDrafts.Remove(npcName);
            }
        }

        public void DiscardReactionDraft(string npcName)
        {
            if (!string.IsNullOrWhiteSpace(npcName)
                && reactionDrafts.Remove(npcName)
                && OutfitReactions.ModEntry.DebugLog)
            {
                monitor?.Log($"[HAT MEMORY] Discarded incomplete hat exchange for {npcName}; the previous completed memory was preserved.", LogLevel.Info);
            }
        }

        public void DiscardAllReactionDrafts()
        {
            reactionDrafts.Clear();
        }

        private static string CleanStoredText(string text, int maxCharacters)
        {
            string cleaned = DialogueValidator.StripDialogueMarkup(text);
            if (string.IsNullOrWhiteSpace(cleaned))
                return "";
            return cleaned.Length <= maxCharacters
                ? cleaned
                : cleaned.Substring(0, maxCharacters).TrimEnd();
        }

        /// <summary>
        /// Produces an English internal memory hint for the prompt, or null if there
        /// is nothing worth saying. Kept separate from the outfit memory hint.
        /// </summary>
        public string BuildMemoryContextHint(HatMemoryComparison memory, string targetLanguage)
        {
            if (memory == null)
                return null;

            // Case 1: the farmer just took the hat off (no current hat, but had one before).
            if (memory.CurrentlyHatless)
            {
                if (string.IsNullOrWhiteSpace(memory.PreviousHatId))
                    return null;
                string identity = BuildHatIdentity(memory.PreviousHatName, memory.PreviousHatId);
                string priorReaction = BuildReactionHistoryHint(memory);
                return "HAT MEMORY: the farmer is now bare-headed after removing the exact hat " + identity + ". "
                     + "React to that specific removal. When its identity or your earlier opinion gives you a recognizable detail, "
                     + "make clear which hat you remember instead of reducing the response to a generic comment about seeing the farmer's face. "
                     + priorReaction;
            }

            // Case 2: first time ever seeing this hat → no memory hint (let it be a fresh reaction).
            if (memory.TimesSeenBefore <= 0)
                return null;

            string firstSeen = FormatDate(memory.FirstSeenSeason, memory.FirstSeenDay, memory.FirstSeenYear);
            int times = memory.TimesSeenBefore;
            string freq = times == 1
                ? "you have seen the farmer in this hat once before"
                : $"you have seen the farmer in this hat {times} times before";
            string firstNote = string.IsNullOrWhiteSpace(firstSeen) ? "" : $" (first seen on {firstSeen})";
            string previousReaction = BuildReactionHistoryHint(memory);
            return $"HAT MEMORY: {freq}{firstNote}. "
                 + "Show that you recognize this hat through familiarity, teasing, or another response that fits your personality. "
                 + "Do NOT react as if seeing this hat for the first time. "
                 + previousReaction;
        }

        private static string BuildHatIdentity(string hatName, string hatId)
        {
            string readableName = string.IsNullOrWhiteSpace(hatName) ? "" : hatName.Trim();
            string stableId = string.IsNullOrWhiteSpace(hatId) ? "" : hatId.Trim();
            if (!string.IsNullOrWhiteSpace(readableName))
                return $"recorded as '{readableName}'";
            return $"with stable item ID '{stableId}' (the ID must never be spoken aloud)";
        }

        private static string FirstWearingText(HatMemoryEntry entry)
        {
            if (entry == null)
                return "";
            return !string.IsNullOrWhiteSpace(entry.FirstWearingReactionText)
                ? entry.FirstWearingReactionText
                : (entry.LastReactionWasRemoval ? "" : entry.LastReactionText ?? "");
        }

        private static string FirstWearingPlayerReply(HatMemoryEntry entry)
        {
            if (entry == null)
                return "";
            return !string.IsNullOrWhiteSpace(entry.FirstWearingReactionText)
                ? entry.FirstWearingPlayerReplyText ?? ""
                : (entry.LastReactionWasRemoval ? "" : entry.LastPlayerReplyText ?? "");
        }

        private static string FirstWearingNpcFollowUp(HatMemoryEntry entry)
        {
            if (entry == null)
                return "";
            return !string.IsNullOrWhiteSpace(entry.FirstWearingReactionText)
                ? entry.FirstWearingNpcFollowUpText ?? ""
                : (entry.LastReactionWasRemoval ? "" : entry.LastNpcFollowUpText ?? "");
        }

        private static string LastRemovalText(HatMemoryEntry entry)
        {
            if (entry == null)
                return "";
            return !string.IsNullOrWhiteSpace(entry.LastRemovalReactionText)
                ? entry.LastRemovalReactionText
                : (entry.LastReactionWasRemoval ? entry.LastReactionText ?? "" : "");
        }

        private static string LastRemovalPlayerReply(HatMemoryEntry entry)
        {
            if (entry == null)
                return "";
            return !string.IsNullOrWhiteSpace(entry.LastRemovalReactionText)
                ? entry.LastRemovalPlayerReplyText ?? ""
                : (entry.LastReactionWasRemoval ? entry.LastPlayerReplyText ?? "" : "");
        }

        private static string LastRemovalNpcFollowUp(HatMemoryEntry entry)
        {
            if (entry == null)
                return "";
            return !string.IsNullOrWhiteSpace(entry.LastRemovalReactionText)
                ? entry.LastRemovalNpcFollowUpText ?? ""
                : (entry.LastReactionWasRemoval ? entry.LastNpcFollowUpText ?? "" : "");
        }

        private static string BuildReactionHistoryHint(HatMemoryComparison memory)
        {
            string firstWearing = BuildExchangeHint("When you first saw the farmer wearing this hat", memory.FirstWearingReactionText,
                memory.FirstWearingPlayerReplyText, memory.FirstWearingNpcFollowUpText);
            string lastRemoval = BuildExchangeHint("When the farmer last removed this hat", memory.LastRemovalReactionText,
                memory.LastRemovalPlayerReplyText, memory.LastRemovalNpcFollowUpText);
            if (string.IsNullOrWhiteSpace(firstWearing) && string.IsNullOrWhiteSpace(lastRemoval))
                return "";

            return "Your completed exchanges about this exact hat were:\n" + firstWearing + lastRemoval
                 + "Treat factual clarifications from the farmer as reliable continuity, including whether the item was cleaned, altered, borrowed, made, or worn for a stated reason. "
                 + "Preserve the opinions and established facts behind the available exchanges, but write a fresh natural reaction; do not quote, closely paraphrase, or mechanically continue their wording.";
        }

        private static string BuildExchangeHint(string situation, string reactionText, string playerReply, string npcFollowUp)
        {
            if (string.IsNullOrWhiteSpace(reactionText))
                return "";

            string memory = situation + ":\nNPC: " + reactionText;
            if (!string.IsNullOrWhiteSpace(playerReply))
                memory += $"\nFarmer: {playerReply}";
            if (!string.IsNullOrWhiteSpace(npcFollowUp))
                memory += $"\nNPC: {npcFollowUp}";

            return memory + "\n";
        }

        private static string FormatDate(string season, int day, int year)
        {
            if (string.IsNullOrWhiteSpace(season) || day <= 0)
                return "";
            return $"day {day} of {season}, year {year}";
        }
    }

    internal sealed class HatMemoryData
    {
        public int Version { get; set; } = 4;
        public Dictionary<string, Dictionary<string, HatMemoryEntry>> Memories { get; set; }
        public Dictionary<string, string> LastHatPerNpc { get; set; }
    }

    internal sealed class HatMemoryEntry
    {
        public string HatId { get; set; } = "";
        public string HatName { get; set; } = "";
        public string FirstSeenSeason { get; set; } = "";
        public int FirstSeenDay { get; set; }
        public int FirstSeenYear { get; set; }
        public string LastSeenSeason { get; set; } = "";
        public int LastSeenDay { get; set; }
        public int LastSeenYear { get; set; }
        public int TimesSeen { get; set; } = 1;
        public string LastReactionText { get; set; } = "";
        public string LastPlayerReplyText { get; set; } = "";
        public string LastNpcFollowUpText { get; set; } = "";
        public bool LastReactionWasRemoval { get; set; }
        public string FirstWearingReactionText { get; set; } = "";
        public string FirstWearingPlayerReplyText { get; set; } = "";
        public string FirstWearingNpcFollowUpText { get; set; } = "";
        public string LastRemovalReactionText { get; set; } = "";
        public string LastRemovalPlayerReplyText { get; set; } = "";
        public string LastRemovalNpcFollowUpText { get; set; } = "";
    }

    internal sealed class HatMemorySnapshot
    {
        public string HatId { get; set; } = "";
        public string HatName { get; set; } = "";
        public string LastReactionText { get; set; } = "";
        public string LastPlayerReplyText { get; set; } = "";
        public string LastNpcFollowUpText { get; set; } = "";
        public bool LastReactionWasRemoval { get; set; }
    }

    internal sealed class HatReactionMemoryDraft
    {
        public string HatId { get; set; } = "";
        public string HatName { get; set; } = "";
        public bool WasRemoval { get; set; }
        public string NpcOpeningLine { get; set; } = "";
        public string PlayerReply { get; set; } = "";
        public string NpcFollowUp { get; set; } = "";
    }

    internal sealed class HatMemoryComparison
    {
        public string CurrentHatId { get; set; } = "";
        public string CurrentHatName { get; set; } = "";
        public bool CurrentlyHatless { get; set; }
        public string PreviousHatId { get; set; } = "";
        public string PreviousHatName { get; set; } = "";
        public int TimesSeenBefore { get; set; }
        public string FirstSeenSeason { get; set; } = "";
        public int FirstSeenDay { get; set; }
        public int FirstSeenYear { get; set; }
        public string LastSeenSeason { get; set; } = "";
        public int LastSeenDay { get; set; }
        public int LastSeenYear { get; set; }
        public string LastReactionText { get; set; } = "";
        public string LastPlayerReplyText { get; set; } = "";
        public string LastNpcFollowUpText { get; set; } = "";
        public bool LastReactionWasRemoval { get; set; }
        public string FirstWearingReactionText { get; set; } = "";
        public string FirstWearingPlayerReplyText { get; set; } = "";
        public string FirstWearingNpcFollowUpText { get; set; } = "";
        public string LastRemovalReactionText { get; set; } = "";
        public string LastRemovalPlayerReplyText { get; set; } = "";
        public string LastRemovalNpcFollowUpText { get; set; } = "";
    }
}
