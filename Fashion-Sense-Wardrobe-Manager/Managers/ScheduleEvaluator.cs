using StardewModdingAPI;
using StardewValley;
using StardewValley.Locations;
using System;
using System.Collections.Generic;
using System.Linq;

namespace FashionSenseWardrobeManager;

internal sealed class ScheduleEvaluator
{
    private const string FinishedFestivalDayKey = "FashionSenseWardrobeManager.FinishedActiveFestivalDay";
    private readonly ScheduleManager _manager;
    private readonly TagManager _tagManager;
    private readonly IndoorOutfitAdjustmentManager _indoorAdjustments;
    private readonly OutfitPreviewRenderer _renderer;
    private readonly IMonitor _monitor;
    private readonly Random _random = new();
    private string? _managedRuleId;
    private string? _managedOutfitName;
    private string? _managedBaseOutfitName;
    private string? _managedDayKey;
    private string? _appliedIndoorAdjustmentKey;

    public ScheduleEvaluator(ScheduleManager manager, TagManager tagManager, IndoorOutfitAdjustmentManager indoorAdjustments,
        OutfitPreviewRenderer renderer, IMonitor monitor)
    {
        _manager = manager;
        _tagManager = tagManager;
        _indoorAdjustments = indoorAdjustments;
        _renderer = renderer;
        _monitor = monitor;
    }

    public bool Evaluate(bool exactTimeTrigger = false)
    {
        if (!Context.IsWorldReady || Game1.eventUp || Game1.player?.currentLocation is null)
            return false;

        List<OutfitScheduleRule> rules = _manager.LoadRules();
        OutfitScheduleRule? selectedRule = FindWinningRule(rules, exactTimeTrigger);
        if (selectedRule is null)
        {
            string? activeAdjustment = _indoorAdjustments.GetActiveOutfit();
            if (activeAdjustment is not null)
            {
                if (activeAdjustment.Equals(GetCurrentOutfitName(), StringComparison.OrdinalIgnoreCase))
                {
                    if (!_renderer.EquipOutfitImmediately(activeAdjustment))
                        return false;
                }
                _indoorAdjustments.SetActiveOutfit(null);
            }
            ResetTracking();
            return true;
        }

        HashSet<string> selectedOutfits = new(selectedRule.OutfitNames, StringComparer.OrdinalIgnoreCase);
        if (selectedRule.TagIds.Count > 0)
        {
            HashSet<string> selectedTagIds = new(selectedRule.TagIds, StringComparer.OrdinalIgnoreCase);
            foreach (OutfitTag tag in _tagManager.LoadTags().Where(tag => selectedTagIds.Contains(tag.Id)))
                selectedOutfits.UnionWith(tag.OutfitNames);
        }

        List<string> available = selectedOutfits
            .Where(_renderer.OutfitExists)
            .ToList();

        HashSet<string> availableNames = new(available, StringComparer.OrdinalIgnoreCase);

        if (available.Count == 0)
            return false;

        string currentDayKey = GetCurrentDayKey();
        string? lockedOutfit = selectedRule.ChangeFrequency == ScheduleChangeFrequency.OncePerDay
            && selectedRule.LockedDayKey.Equals(currentDayKey, StringComparison.Ordinal)
            ? available.FirstOrDefault(name => name.Equals(selectedRule.LockedOutfitName, StringComparison.OrdinalIgnoreCase))
            : null;
        string? currentAdjustedBase = selectedRule.ChangeFrequency == ScheduleChangeFrequency.EveryActivation
            && _managedRuleId == selectedRule.Id && _managedDayKey == currentDayKey
            && _managedBaseOutfitName is not null
            && available.Contains(_managedBaseOutfitName, StringComparer.OrdinalIgnoreCase)
            && _indoorAdjustments.Get(_managedBaseOutfitName).HasItems
            ? _managedBaseOutfitName
            : null;

        string baseOutfitName;
        bool newDailySelection = lockedOutfit is null
            && selectedRule.ChangeFrequency == ScheduleChangeFrequency.OncePerDay;
        bool selectionStateChanged = false;
        if (lockedOutfit is not null)
        {
            baseOutfitName = lockedOutfit;
        }
        else if (currentAdjustedBase is not null)
        {
            baseOutfitName = currentAdjustedBase;
        }
        else
        {
            availableNames = new(available, StringComparer.OrdinalIgnoreCase);
            int removed = selectedRule.UsedOutfitNames.RemoveAll(name => !availableNames.Contains(name));
            selectionStateChanged |= removed > 0;

            // Migrate existing schedules naturally: the previously selected outfit
            // counts as used when the new rotation state is still empty.
            if (selectedRule.UsedOutfitNames.Count == 0
                && !string.IsNullOrWhiteSpace(selectedRule.LastOutfitName)
                && availableNames.Contains(selectedRule.LastOutfitName))
            {
                selectedRule.UsedOutfitNames.Add(selectedRule.LastOutfitName);
                selectionStateChanged = true;
            }

            HashSet<string> used = new(selectedRule.UsedOutfitNames, StringComparer.OrdinalIgnoreCase);
            List<string> choices = available.Where(name => !used.Contains(name)).ToList();

            if (choices.Count == 0)
            {
                selectedRule.UsedOutfitNames.Clear();
                selectionStateChanged = true;

                // At the boundary between rotations, don't immediately repeat the
                // outfit which finished the previous rotation.
                choices = available.Count > 1 && !string.IsNullOrWhiteSpace(selectedRule.LastOutfitName)
                    ? available.Where(name => !name.Equals(selectedRule.LastOutfitName, StringComparison.OrdinalIgnoreCase)).ToList()
                    : available;
            }

            baseOutfitName = choices[_random.Next(choices.Count)];
        }

        IndoorOutfitAdjustment adjustment = _indoorAdjustments.Get(baseOutfitName);
        bool hideIndoors = !Game1.player.currentLocation.IsOutdoors && adjustment.HasItems;
        string? desiredAdjustmentKey = hideIndoors
            ? $"{baseOutfitName}|{adjustment.HatId}|{string.Join("|", adjustment.AccessoryIds.OrderBy(id => id))}"
            : null;
        string? persistedActiveOutfit = _indoorAdjustments.GetActiveOutfit();
        bool adjustmentChanged = !string.Equals(_appliedIndoorAdjustmentKey, desiredAdjustmentKey, StringComparison.Ordinal)
            || !string.Equals(persistedActiveOutfit, hideIndoors ? baseOutfitName : null,
                StringComparison.OrdinalIgnoreCase);

        bool alreadyEquipped = !_renderer.IsPreviewActive
            && baseOutfitName.Equals(GetCurrentOutfitName(), StringComparison.OrdinalIgnoreCase);
        bool needsRefresh = adjustmentChanged
            || (adjustment.HasItems && _managedRuleId != selectedRule.Id);
        bool didEquip = !alreadyEquipped || needsRefresh;
        if (!didEquip || _renderer.EquipOutfitImmediately(baseOutfitName))
        {
            if (didEquip && hideIndoors && !_renderer.ApplyIndoorAdjustment(adjustment))
                return false;

            if (didEquip || adjustmentChanged)
                _indoorAdjustments.SetActiveOutfit(hideIndoors ? baseOutfitName : null);
            _appliedIndoorAdjustmentKey = desiredAdjustmentKey;
            _managedRuleId = selectedRule.Id;
            _managedOutfitName = baseOutfitName;
            _managedBaseOutfitName = baseOutfitName;
            _managedDayKey = currentDayKey;
            if (!selectedRule.UsedOutfitNames.Contains(baseOutfitName, StringComparer.OrdinalIgnoreCase))
            {
                selectedRule.UsedOutfitNames.Add(baseOutfitName);
                selectionStateChanged = true;
            }

            if (!selectedRule.LastOutfitName.Equals(baseOutfitName, StringComparison.OrdinalIgnoreCase))
            {
                selectedRule.LastOutfitName = baseOutfitName;
                selectionStateChanged = true;
            }

            if (newDailySelection)
            {
                selectedRule.LockedOutfitName = baseOutfitName;
                selectedRule.LockedDayKey = currentDayKey;
                selectionStateChanged = true;
            }
            else if (selectedRule.ChangeFrequency == ScheduleChangeFrequency.EveryActivation
                && (!string.IsNullOrEmpty(selectedRule.LockedOutfitName)
                    || !string.IsNullOrEmpty(selectedRule.LockedDayKey)))
            {
                selectedRule.LockedOutfitName = string.Empty;
                selectedRule.LockedDayKey = string.Empty;
                selectionStateChanged = true;
            }

            if (selectionStateChanged)
                _manager.SaveRules(rules);
            if (didEquip)
                _monitor.Log($"Schedule equipped Fashion Sense outfit '{baseOutfitName}'.", LogLevel.Trace);
            return true;
        }

        return false;
    }

    public void ResetTracking()
    {
        _managedRuleId = null;
        _managedOutfitName = null;
        _managedBaseOutfitName = null;
        _managedDayKey = null;
        _appliedIndoorAdjustmentKey = null;
    }

    public void MarkActiveFestivalFinished()
    {
        Game1.player.modData[FinishedFestivalDayKey] = GetCurrentDayKey();
    }

    private static bool ActiveFestivalFinishedToday()
        => Game1.player.modData.TryGetValue(FinishedFestivalDayKey, out string? dayKey)
            && dayKey == GetCurrentDayKey();

    public void StopAfterManualChange()
    {
        if (_managedRuleId is null || _managedOutfitName is null
            || _managedOutfitName.Equals(GetCurrentOutfitName(), StringComparison.OrdinalIgnoreCase))
            return;

        List<OutfitScheduleRule> rules = _manager.LoadRules();
        OutfitScheduleRule? managedRule = rules.FirstOrDefault(rule => rule.Id == _managedRuleId);
        if (managedRule?.Enabled == true)
        {
            managedRule.Enabled = false;
            _manager.SaveRules(rules);
            _monitor.Log($"Paused outfit schedule '{managedRule.Name}' after a manual outfit change.", LogLevel.Info);
        }

        ResetTracking();
    }

    private static string? GetCurrentOutfitName()
    {
        if (Game1.player.modData.TryGetValue("FashionSense.Outfit.CurrentId", out string? currentId)
            && !string.IsNullOrWhiteSpace(currentId))
            return currentId;

        return Game1.player.modData.TryGetValue("FashionSense.CurrentOutfit", out string? name)
            ? name
            : null;
    }

    private static string GetCurrentDayKey()
        => $"{Game1.year}:{Game1.currentSeason}:{Game1.dayOfMonth}";

    public string? GetWinningRuleId(IEnumerable<OutfitScheduleRule> rules)
    {
        if (!Context.IsWorldReady || Game1.eventUp || Game1.player?.currentLocation is null)
            return null;

        return FindWinningRule(rules, exactTimeTrigger: false)?.Id;
    }

    public bool IsRuleMatchingNow(OutfitScheduleRule rule)
    {
        if (!Context.IsWorldReady || Game1.eventUp || Game1.player?.currentLocation is null)
            return false;

        return Matches(rule, exactTimeTrigger: false);
    }

    public bool HasTimeBoundaryNow()
    {
        if (!Context.IsWorldReady || Game1.player is null)
            return false;

        int time = Game1.timeOfDay;
        return time is 600 or 1200 or 1800
            || _manager.LoadRules().Any(rule => rule.Enabled && rule.TimeWindows.Any(window =>
                window.Start == time || window.End == time));
    }

    private static OutfitScheduleRule? FindWinningRule(
        IEnumerable<OutfitScheduleRule> rules,
        bool exactTimeTrigger)
        => rules
            .Where(rule => Matches(rule, exactTimeTrigger))
            .OrderByDescending(GetPriority)
            .FirstOrDefault();

    private static bool Matches(OutfitScheduleRule rule, bool exactTimeTrigger)
    {
        if (!rule.Enabled || (rule.OutfitNames.Count == 0 && rule.TagIds.Count == 0))
            return false;

        bool festivalRule = rule.Section == ScheduleSection.Festivals;
        if (!MatchesSection(rule)
            || (!festivalRule && !MatchesDay(rule))
            || (!festivalRule && !MatchesWeather(rule.WeatherIds))
            || !MatchesPeriod(rule.Period)
            || (!festivalRule && !MatchesLocation(rule.LocationIds)))
        {
            return false;
        }

        if (rule.TimeWindows.Count == 0)
        {
            if (!exactTimeTrigger)
                return true;

            // A broad rule should only re-evaluate when its period begins, not every
            // ten in-game minutes (which would continuously reroll selected outfits).
            return rule.Period switch
            {
                SchedulePeriod.Morning => Game1.timeOfDay == 600,
                SchedulePeriod.Afternoon => Game1.timeOfDay == 1200,
                SchedulePeriod.Night => Game1.timeOfDay == 1800,
                _ => false
            };
        }

        if (exactTimeTrigger)
            return rule.TimeWindows.Any(window => window.Start == Game1.timeOfDay);

        int now = Game1.timeOfDay;
        return rule.TimeWindows.Any(window => window.End is not int end
            ? now >= window.Start
            : window.Start < end
                ? now >= window.Start && now < end
                : now >= window.Start || now < end);
    }

    private static bool MatchesSection(OutfitScheduleRule rule)
    {
        return rule.Section switch
        {
            ScheduleSection.Spring => Game1.currentSeason == "spring",
            ScheduleSection.Summer => Game1.currentSeason == "summer",
            ScheduleSection.Fall => Game1.currentSeason == "fall",
            ScheduleSection.Winter => Game1.currentSeason == "winter",
            ScheduleSection.Festivals => MatchesFestival(rule.FestivalIds),
            ScheduleSection.Daily => true,
            _ => false
        };
    }

    private static bool MatchesFestival(IReadOnlyCollection<string> festivalIds)
    {
        if (festivalIds.Count == 0)
            return false;

        if (!festivalIds.Contains(ScheduleConditionIds.AnyFestival, StringComparer.OrdinalIgnoreCase))
            return festivalIds.Any(IsFestivalActiveToday);

        if (!ActiveFestivalFinishedToday() && Utility.isFestivalDay(Game1.dayOfMonth, Game1.season))
            return true;

        try
        {
            return DataLoader.PassiveFestivals(Game1.content).Keys.Any(Utility.IsPassiveFestivalDay);
        }
        catch
        {
            return false;
        }
    }

    private static bool IsFestivalActiveToday(string id)
    {
        if (id.StartsWith(ScheduleConditionIds.ActiveFestivalPrefix, StringComparison.OrdinalIgnoreCase))
        {
            string date = id[ScheduleConditionIds.ActiveFestivalPrefix.Length..];
            string today = $"{Game1.currentSeason}{Game1.dayOfMonth}";
            return date.Equals(today, StringComparison.OrdinalIgnoreCase)
                && !ActiveFestivalFinishedToday()
                && Utility.isFestivalDay(Game1.dayOfMonth, Game1.season);
        }

        if (id.StartsWith(ScheduleConditionIds.PassiveFestivalPrefix, StringComparison.OrdinalIgnoreCase))
        {
            string festivalId = id[ScheduleConditionIds.PassiveFestivalPrefix.Length..];
            return Utility.IsPassiveFestivalDay(festivalId);
        }

        return false;
    }

    private static bool MatchesDay(OutfitScheduleRule rule)
    {
        return rule.DayMode switch
        {
            ScheduleDayMode.All => true,
            ScheduleDayMode.Single => rule.SingleDay == Game1.dayOfMonth,
            ScheduleDayMode.Multiple => rule.Days.Contains(Game1.dayOfMonth),
            _ => false
        };
    }

    private static bool MatchesWeather(IReadOnlyCollection<string> weatherIds)
    {
        if (weatherIds.Count == 0)
            return true;

        string current = Game1.player.currentLocation.GetWeather()?.Weather ?? "Sun";
        return weatherIds.Any(id => id switch
        {
            ScheduleConditionIds.Sunny => current.Equals("Sun", StringComparison.OrdinalIgnoreCase)
                || current.Equals("Wind", StringComparison.OrdinalIgnoreCase)
                || current.Equals("Debris", StringComparison.OrdinalIgnoreCase),
            "Wind" => current.Equals("Wind", StringComparison.OrdinalIgnoreCase)
                || current.Equals("Debris", StringComparison.OrdinalIgnoreCase),
            _ => current.Equals(id, StringComparison.OrdinalIgnoreCase)
        });
    }

    private static bool MatchesPeriod(SchedulePeriod period)
    {
        int time = Game1.timeOfDay;
        return period switch
        {
            SchedulePeriod.Any => true,
            SchedulePeriod.Morning => time >= 600 && time < 1200,
            SchedulePeriod.Afternoon => time >= 1200 && time < 1800,
            SchedulePeriod.Night => time >= 1800,
            _ => false
        };
    }

    private static bool MatchesLocation(IReadOnlyCollection<string> locationIds)
    {
        if (locationIds.Count == 0)
            return true;

        GameLocation current = Game1.player.currentLocation;
        return locationIds.Any(id => id switch
        {
            ScheduleConditionIds.FarmHouse => current is FarmHouse,
            ScheduleConditionIds.Indoors => !current.IsOutdoors && current is not FarmHouse,
            ScheduleConditionIds.Outdoors => current.IsOutdoors,
            ScheduleConditionIds.GingerIsland => current.InIslandContext(),
            ScheduleConditionIds.Mines => current is MineShaft
                || current.Name.Equals("Mine", StringComparison.OrdinalIgnoreCase),
            _ => current.NameOrUniqueName.Equals(id, StringComparison.OrdinalIgnoreCase)
                || current.Name.Equals(id, StringComparison.OrdinalIgnoreCase)
        });
    }

    private static int GetPriority(OutfitScheduleRule rule)
    {
        int section = rule.Section switch
        {
            ScheduleSection.Festivals => 10000,
            ScheduleSection.Daily => 0,
            _ => 100
        };

        int day = rule.Section == ScheduleSection.Festivals ? 0 : rule.DayMode switch
        {
            ScheduleDayMode.Single => 30,
            ScheduleDayMode.Multiple => 20,
            _ => 0
        };

        int conditions = (rule.Section != ScheduleSection.Festivals && rule.WeatherIds.Count > 0 ? 1000 : 0)
            + (rule.Period != SchedulePeriod.Any ? 4 : 0)
            + (rule.Section != ScheduleSection.Festivals && rule.LocationIds.Count > 0 ? 2000 : 0)
            + (rule.Section == ScheduleSection.Festivals
                && rule.FestivalIds.Count > 0
                && !rule.FestivalIds.Contains(ScheduleConditionIds.AnyFestival, StringComparer.OrdinalIgnoreCase) ? 100 : 0)
            + (rule.TimeWindows.Count > 0 ? 8 : 0);

        return section + day + conditions;
    }
}
