using Microsoft.Xna.Framework;
using StardewModdingAPI;
using StardewModdingAPI.Events;
using StardewValley;
using System;
using System.Collections.Generic;

namespace HomeSweetHomeSpouseDialogues
{
    /// <summary>Queues this mod's dialogue separately from the game's marriage-dialogue asset.</summary>
    public sealed class ModEntry : Mod
    {
        private ModConfig Config;
        private readonly Random random = new();
        private readonly Dictionary<string, HashSet<int>> usedDialogueIds = new();

        private int manualDialogueGraceTimer;
        private bool spouseWasBusyForManualDialogue;
        private bool manualDialogueQueuedToday;
        private bool homeDayTriggeredToday;
        private bool rainDayTriggeredToday;
        private bool homeNightTriggeredToday;
        private string dialoguePoolSeason = "";

        public override void Entry(IModHelper helper)
        {
            Config = helper.ReadConfig<ModConfig>();
            helper.Events.GameLoop.GameLaunched += OnGameLaunched;
            helper.Events.GameLoop.SaveLoaded += OnSaveLoaded;
            helper.Events.GameLoop.DayStarted += OnDayStarted;
            helper.Events.GameLoop.UpdateTicked += OnUpdateTicked;
            helper.Events.GameLoop.ReturnedToTitle += OnReturnedToTitle;
            helper.Events.Input.ButtonPressed += OnButtonPressed;
        }

        private void OnGameLaunched(object sender, GameLaunchedEventArgs e)
        {
            IGenericModConfigMenuApi configMenu = Helper.ModRegistry.GetApi<IGenericModConfigMenuApi>("spacechase0.GenericModConfigMenu");
            if (configMenu != null)
            {
                configMenu.Register(
                    ModManifest,
                    reset: () => Config = new ModConfig(),
                    save: () => Helper.WriteConfig(Config));

                configMenu.AddKeybindList(
                    ModManifest,
                    getValue: () => Config.WaterSinkButton,
                    setValue: value => Config.WaterSinkButton = value,
                    name: () => Helper.Translation.Get("config.water-sink-button.name").ToString(),
                    tooltip: () => Helper.Translation.Get("config.water-sink-button.description").ToString());
            }

            IContentPatcherApi contentPatcher = Helper.ModRegistry.GetApi<IContentPatcherApi>("Pathoschild.ContentPatcher");
            contentPatcher?.RegisterToken(
                ModManifest,
                "WaterSinkButton",
                () => new[] { Config.WaterSinkButton?.ToString() ?? "D5" });
        }

        private void OnSaveLoaded(object sender, SaveLoadedEventArgs e)
        {
            ResetDailyState();
        }

        private void OnDayStarted(object sender, DayStartedEventArgs e)
        {
            ResetDailyState();
        }

        private void OnReturnedToTitle(object sender, ReturnedToTitleEventArgs e)
        {
            dialoguePoolSeason = "";
            usedDialogueIds.Clear();
            ResetDailyState();
        }

        private void OnUpdateTicked(object sender, UpdateTickedEventArgs e)
        {
            if (!Context.IsWorldReady || !Config.EnableMod)
                return;

            if (manualDialogueGraceTimer > 0)
                manualDialogueGraceTimer--;

            NPC spouse = GetSpouse();
            if (spouse == null)
                return;

            bool spouseIsBusy = !CanQueueDialogue(spouse);
            if (spouseIsBusy)
            {
                spouseWasBusyForManualDialogue = true;
            }
            else if (spouseWasBusyForManualDialogue)
            {
                spouseWasBusyForManualDialogue = false;
                manualDialogueGraceTimer = Math.Max(manualDialogueGraceTimer, 90);
            }

            TryQueueSeasonalSpouseDialogue(spouse);
        }

        private void OnButtonPressed(object sender, ButtonPressedEventArgs e)
        {
            if (!Context.IsWorldReady || !Config.EnableMod)
                return;

            if (!e.Button.IsActionButton())
                return;

            NPC spouse = GetSpouse();
            if (spouse == null || Game1.player == null || !Game1.player.canMove || DistanceToPlayer(spouse) > 120f)
                return;

            manualDialogueGraceTimer = 90;
            spouseWasBusyForManualDialogue = spouse.controller != null;

            // Once the player interacts with their spouse, allow the later time slot to queue.
            if (manualDialogueQueuedToday)
                manualDialogueQueuedToday = false;
        }

        private bool TryQueueSeasonalSpouseDialogue(NPC spouse)
        {
            if (manualDialogueQueuedToday || manualDialogueGraceTimer > 0 || !CanQueueDialogue(spouse))
                return false;

            string slot = GetCurrentDialogueSlot();
            if (slot == null || HasSlotTriggered(slot))
                return false;

            string line = GetSeasonalDialogue(spouse, slot);
            if (string.IsNullOrEmpty(line))
                return false;

            QueueDialogueWithOptionalItems(spouse, line);
            manualDialogueQueuedToday = true;
            MarkSlotTriggered(slot);
            return true;
        }

        private string GetCurrentDialogueSlot()
        {
            int time = Game1.timeOfDay;

            if (Config.EnableHomeNightDialogues && time >= Config.NightDialogueStartTime && time < Config.NightDialogueEndTime)
                return "homeNight";

            if (time < Config.DayDialogueStartTime || time >= Config.DayDialogueEndTime)
                return null;

            if (Config.EnableRainDayDialogues && Game1.isRaining)
                return "rainDay";

            return Config.EnableHomeDayDialogues ? "homeDay" : null;
        }

        private string GetSeasonalDialogue(NPC spouse, string slot)
        {
            ResetDialoguePoolForNewSeason();
            string season = Game1.currentSeason;

            if (slot == "rainDay")
            {
                string line = GetNonRepeatingDialogue($"{season}RainDay", 1, 15, spouse)
                    ?? GetNonRepeatingDialogue("rainDay", 1, 15, spouse);

                if (!string.IsNullOrEmpty(line) || Game1.currentLocation?.Name != "FarmHouse")
                    return line;

                return GetNonRepeatingDialogue($"{season}HomeDay", 1, 30, spouse)
                    ?? GetNonRepeatingDialogue("HomeDay", 1, 30, spouse);
            }

            if (Game1.currentLocation?.Name != "FarmHouse")
                return null;

            if (slot == "homeDay")
                return GetNonRepeatingDialogue($"{season}HomeDay", 1, 30, spouse)
                    ?? GetNonRepeatingDialogue("HomeDay", 1, 30, spouse);

            if (slot == "homeNight")
                return GetNonRepeatingDialogue($"{season}HomeNight", 1, 30, spouse)
                    ?? GetNonRepeatingDialogue("HomeNight", 1, 30, spouse);

            return null;
        }

        private bool CanQueueDialogue(NPC spouse)
        {
            return spouse != null
                && Game1.player != null
                && spouse.currentLocation == Game1.player.currentLocation
                && !spouse.isSleeping.Value
                && spouse.controller == null
                && !spouse.isMoving()
                && spouse.movementPause <= 0
                && (spouse.CurrentDialogue == null || spouse.CurrentDialogue.Count == 0)
                && !Game1.dialogueUp
                && Game1.activeClickableMenu == null;
        }

        private bool HasSlotTriggered(string slot)
        {
            return slot switch
            {
                "homeDay" => homeDayTriggeredToday,
                "rainDay" => rainDayTriggeredToday,
                "homeNight" => homeNightTriggeredToday,
                _ => true,
            };
        }

        private void MarkSlotTriggered(string slot)
        {
            switch (slot)
            {
                case "homeDay": homeDayTriggeredToday = true; break;
                case "rainDay": rainDayTriggeredToday = true; break;
                case "homeNight": homeNightTriggeredToday = true; break;
            }
        }

        private void ResetDailyState()
        {
            manualDialogueGraceTimer = 0;
            spouseWasBusyForManualDialogue = false;
            manualDialogueQueuedToday = false;
            homeDayTriggeredToday = false;
            rainDayTriggeredToday = false;
            homeNightTriggeredToday = false;
        }

        private void ResetDialoguePoolForNewSeason()
        {
            string currentSeason = Game1.currentSeason ?? "";
            if (!string.Equals(dialoguePoolSeason, currentSeason, StringComparison.OrdinalIgnoreCase))
            {
                dialoguePoolSeason = currentSeason;
                usedDialogueIds.Clear();
            }
        }

        private string GetNonRepeatingDialogue(string prefix, int min, int max, NPC spouse)
        {
            string poolKey = $"{Game1.player?.spouse ?? "UnknownSpouse"}|{dialoguePoolSeason}|{prefix}";
            if (!usedDialogueIds.TryGetValue(poolKey, out HashSet<int> usedIds))
            {
                usedIds = new HashSet<int>();
                usedDialogueIds[poolKey] = usedIds;
            }

            List<int> available = GetAvailableDialogueIds(prefix, min, max, spouse, usedIds);
            if (available.Count == 0)
            {
                usedIds.Clear();
                available = GetAvailableDialogueIds(prefix, min, max, spouse, usedIds);
            }

            if (available.Count == 0)
                return null;

            int selectedId = available[random.Next(available.Count)];
            usedIds.Add(selectedId);
            return GetDialogueLineExact(prefix, selectedId, spouse);
        }

        private List<int> GetAvailableDialogueIds(string prefix, int min, int max, NPC spouse, HashSet<int> usedIds)
        {
            List<int> available = new();
            for (int id = min; id <= max; id++)
            {
                if (!usedIds.Contains(id) && !string.IsNullOrEmpty(GetDialogueLineExact(prefix, id, spouse)))
                    available.Add(id);
            }
            return available;
        }

        private string GetDialogueLineExact(string prefix, int number, NPC spouse)
        {
            string spouseName = spouse?.Name;
            string playerGender = Game1.player != null && Game1.player.IsMale ? "male" : "female";
            List<string> keys = new();

            if (!string.IsNullOrEmpty(spouseName))
            {
                keys.Add($"{prefix}.{spouseName}.{playerGender}.{number}");
                keys.Add($"{prefix}.{spouseName}.{number}");
            }

            keys.Add($"{prefix}.Generic.{playerGender}.{number}");
            keys.Add($"{prefix}.Generic.{number}");
            keys.Add($"{prefix}.{playerGender}.{number}");
            keys.Add($"{prefix}.{number}");

            foreach (string key in keys)
            {
                var translation = Helper.Translation.Get(key);
                if (translation.HasValue())
                    return translation.ToString().Replace("@", Game1.player.Name);
            }
            return null;
        }

        private string GetRandomDialogue(string prefix, int min, int max, NPC spouse)
        {
            for (int attempt = 0; attempt < 10; attempt++)
            {
                string line = GetDialogueLineExact(prefix, random.Next(min, max + 1), spouse);
                if (!string.IsNullOrEmpty(line))
                    return line;
            }

            for (int id = min; id <= max; id++)
            {
                string line = GetDialogueLineExact(prefix, id, spouse);
                if (!string.IsNullOrEmpty(line))
                    return line;
            }
            return null;
        }

        private sealed class DialogueItemReward
        {
            public string ItemId { get; init; }
            public int Amount { get; init; } = 1;
            public string FullBagKey { get; init; }
        }

        private static List<DialogueItemReward> ExtractItemTags(ref string line)
        {
            List<DialogueItemReward> rewards = new();
            const string startTag = "[item:";

            while (!string.IsNullOrEmpty(line))
            {
                int start = line.IndexOf(startTag, StringComparison.OrdinalIgnoreCase);
                if (start < 0)
                    break;

                int end = line.IndexOf(']', start);
                if (end < 0)
                    break;

                string[] parts = line.Substring(start + startTag.Length, end - start - startTag.Length).Trim().Split(':');
                string itemId = parts.Length > 0 ? parts[0].Trim() : "";
                int amount = parts.Length > 1 && int.TryParse(parts[1].Trim(), out int parsedAmount) && parsedAmount > 0 ? parsedAmount : 1;
                string fullBagKey = parts.Length > 2 ? parts[2].Trim() : null;

                if (!string.IsNullOrEmpty(itemId))
                    rewards.Add(new DialogueItemReward { ItemId = itemId, Amount = amount, FullBagKey = fullBagKey });

                line = line.Remove(start, end - start + 1).Trim();
            }
            return rewards;
        }

        private void QueueDialogueWithOptionalItems(NPC spouse, string line)
        {
            List<DialogueItemReward> rewards = ExtractItemTags(ref line);
            spouse.CurrentDialogue.Clear();
            spouse.CurrentDialogue.Push(new Dialogue(spouse, "", line));

            if (rewards.Count == 0)
                return;

            Game1.afterDialogues = () =>
            {
                foreach (DialogueItemReward reward in rewards)
                {
                    try
                    {
                        Item item = ItemRegistry.Create($"(O){reward.ItemId}", reward.Amount);
                        if (!Game1.player.isInventoryFull())
                        {
                            Game1.player.addItemToInventoryBool(item);
                            Game1.player.holdUpItemThenMessage(item);
                            continue;
                        }

                        string fullBagLine = string.IsNullOrEmpty(reward.FullBagKey)
                            ? "Sua bolsa está cheia..."
                            : GetRandomDialogue(reward.FullBagKey, 1, 3, spouse) ?? "Sua bolsa está cheia...";
                        spouse.showTextAboveHead(fullBagLine);
                        break;
                    }
                    catch (Exception ex)
                    {
                        Monitor.Log($"Couldn't create dialogue item '{reward.ItemId}' x{reward.Amount}: {ex.Message}", LogLevel.Warn);
                    }
                }
            };
        }

        private static NPC GetSpouse()
        {
            return Context.IsWorldReady && !string.IsNullOrWhiteSpace(Game1.player?.spouse)
                ? Game1.getCharacterFromName(Game1.player.spouse)
                : null;
        }

        private static float DistanceToPlayer(NPC npc)
        {
            return npc == null || Game1.player == null ? float.MaxValue : Vector2.Distance(npc.Position, Game1.player.Position);
        }

    }
}
