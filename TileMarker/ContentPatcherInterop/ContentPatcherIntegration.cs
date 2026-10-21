using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Microsoft.Xna.Framework;
using StardewModdingAPI;
using StardewModdingAPI.Events;
using StardewModdingAPI.Utilities;
using StardewValley;
using StardewValley.Triggers;
using TileMarker.Core;

namespace TileMarker.ContentPatcherInterop
{
    /// <summary>Optional bridge which lets JSON-only Content Patcher packs use Tile Marker.</summary>
    internal sealed class ContentPatcherIntegration
    {
        public const string CategoriesAssetName = "Mods/NatrollEXE.TileMarker/Categories";

        private readonly IModHelper helper;
        private readonly IMonitor monitor;
        private readonly IManifest manifest;
        private readonly MarkedTileStore store;
        private readonly Func<bool> getMergeCompatibleCategories;
        private readonly Func<IDictionary<string, string>> getInteractionButtonOverrides;
        private readonly IAssetName categoriesAsset;

        private readonly HashSet<(string OwnerModId, string Category)> registeredCategories = new();
        private readonly Dictionary<(string OwnerModId, string Category), RuntimeCategory> runtimeCategories = new();
        private readonly List<ConfigurableInteractionButton> configurableButtons = new();

        private bool initialized;

        internal long StoreRevision => store.Revision;
        internal bool MergeCompatibleCategories => getMergeCompatibleCategories?.Invoke() == true;
        internal IReadOnlyList<ConfigurableInteractionButton> ConfigurableButtons => configurableButtons;

        public ContentPatcherIntegration(
            IModHelper helper,
            IMonitor monitor,
            IManifest manifest,
            MarkedTileStore store,
            Func<bool> getMergeCompatibleCategories,
            Func<IDictionary<string, string>> getInteractionButtonOverrides)
        {
            this.helper = helper;
            this.monitor = monitor;
            this.manifest = manifest;
            this.store = store;
            this.getMergeCompatibleCategories = getMergeCompatibleCategories;
            this.getInteractionButtonOverrides = getInteractionButtonOverrides;
            categoriesAsset = helper.GameContent.ParseAssetName(CategoriesAssetName);
        }

        public void OnAssetRequested(object sender, AssetRequestedEventArgs e)
        {
            if (e.NameWithoutLocale.IsEquivalentTo(categoriesAsset))
            {
                e.LoadFrom(
                    () => new Dictionary<string, ContentPatcherCategoryData>(StringComparer.Ordinal),
                    AssetLoadPriority.Exclusive);
            }
        }

        public void OnAssetsInvalidated(object sender, AssetsInvalidatedEventArgs e)
        {
            if (initialized && e.NamesWithoutLocale.Any(name => name.IsEquivalentTo(categoriesAsset)))
                ReloadCategories();
        }

        public void Initialize()
        {
            initialized = true;

            IContentPatcherApi contentPatcher = helper.ModRegistry.GetApi<IContentPatcherApi>("Pathoschild.ContentPatcher");
            if (contentPatcher == null)
                return;

            contentPatcher.RegisterToken(
                manifest,
                "MarkedTiles",
                new TileMarkerToken(this, TileMarkerTokenKind.MarkedTiles));
            contentPatcher.RegisterToken(
                manifest,
                "IsTileMarked",
                new TileMarkerToken(this, TileMarkerTokenKind.IsTileMarked));

            ReloadCategories();
        }

        private void ReloadCategories()
        {
            Dictionary<string, ContentPatcherCategoryData> definitions;
            try
            {
                definitions = helper.GameContent.Load<Dictionary<string, ContentPatcherCategoryData>>(categoriesAsset)
                    ?? new Dictionary<string, ContentPatcherCategoryData>(StringComparer.Ordinal);
            }
            catch (Exception ex)
            {
                monitor.Log($"[Tile Marker CP] Couldn't load {CategoriesAssetName}; existing CP categories were kept: {ex}", LogLevel.Error);
                return;
            }

            foreach (var key in registeredCategories)
                store.UnregisterContentPatcherCategory(key.OwnerModId, key.Category);

            registeredCategories.Clear();
            runtimeCategories.Clear();
            configurableButtons.Clear();

            foreach (var pair in definitions.OrderBy(pair => pair.Key, StringComparer.OrdinalIgnoreCase))
            {
                if (!TryParseCategoryKey(pair.Key, out string ownerModId, out string category, out string keyError))
                {
                    monitor.Log($"[Tile Marker CP] Ignored category '{pair.Key ?? "<null>"}': {keyError}", LogLevel.Warn);
                    continue;
                }

                ContentPatcherCategoryData definition = pair.Value;
                if (definition == null)
                {
                    monitor.Log($"[Tile Marker CP] Ignored null category '{pair.Key}'.", LogLevel.Warn);
                    continue;
                }

                string displayName = string.IsNullOrWhiteSpace(definition.DisplayName)
                    ? category
                    : definition.DisplayName.Trim();

                if (!store.RegisterFromContentPatcher(ownerModId, category, displayName, definition.SharedGroup))
                {
                    monitor.Log(
                        $"[Tile Marker CP] Ignored '{pair.Key}' because that owner/category is already registered through the C# API.",
                        LogLevel.Warn);
                    continue;
                }

                var key = (ownerModId, category);
                registeredCategories.Add(key);

                RuntimeCategory runtime = TryCreateRuntimeCategory(key, displayName, definition.Interaction);
                if (runtime != null)
                    runtimeCategories[key] = runtime;
            }

            monitor.Log(
                $"[Tile Marker CP] Loaded {registeredCategories.Count} Content Patcher categor{(registeredCategories.Count == 1 ? "y" : "ies")}, including {runtimeCategories.Count} interaction definition(s).",
                LogLevel.Trace);
        }

        internal KeybindList GetConfiguredButton(ConfigurableInteractionButton option)
        {
            if (option == null)
                return new KeybindList();

            return ResolveConfiguredButton(option.ConfigKey, option.DefaultButton);
        }

        internal void SetConfiguredButton(ConfigurableInteractionButton option, KeybindList value)
        {
            if (option == null)
                return;

            IDictionary<string, string> overrides = getInteractionButtonOverrides?.Invoke();
            if (overrides != null)
                overrides[option.ConfigKey] = value?.ToString() ?? "";

            if (runtimeCategories.TryGetValue(option.CategoryKey, out RuntimeCategory runtime))
                runtime.Keybind = value ?? new KeybindList();
        }

        internal void RefreshConfiguredButtons()
        {
            foreach (ConfigurableInteractionButton option in configurableButtons)
            {
                if (runtimeCategories.TryGetValue(option.CategoryKey, out RuntimeCategory runtime))
                    runtime.Keybind = ResolveConfiguredButton(option.ConfigKey, option.DefaultButton);
            }
        }

        public bool TryHandleButton(SButton pressedButton)
        {
            if (!Context.IsWorldReady
                || runtimeCategories.Count == 0
                || Game1.player == null
                || !Game1.player.CanMove
                || Game1.currentLocation == null
                || Game1.activeClickableMenu != null
                || Game1.dialogueUp
                || Game1.eventUp)
            {
                return false;
            }

            bool ranAnyAction = false;
            bool shouldSuppress = false;

            foreach (RuntimeCategory runtime in runtimeCategories.Values)
            {
                if (!runtime.MatchesButton(pressedButton))
                    continue;

                Point tile = GetTargetTile(runtime.Target);
                if (!IsEffectivelyMarked(runtime.OwnerModId, runtime.Category, Game1.currentLocation, tile))
                    continue;

                bool categorySucceeded = false;
                foreach (string rawAction in runtime.Actions)
                {
                    string action = ExpandRuntimePlaceholders(rawAction, Game1.currentLocation, tile);
                    try
                    {
                        if (TriggerActionManager.TryRunAction(action, out string error, out Exception exception))
                        {
                            categorySucceeded = true;
                            ranAnyAction = true;
                        }
                        else
                        {
                            monitor.Log(
                                $"[Tile Marker CP] Action failed for {runtime.OwnerModId}/{runtime.Category}: {error ?? "unknown error"}. Action: {action}",
                                LogLevel.Warn);
                            if (exception != null)
                                monitor.Log($"[Tile Marker CP] Technical details: {exception}", LogLevel.Trace);
                        }
                    }
                    catch (Exception ex)
                    {
                        monitor.Log(
                            $"[Tile Marker CP] Action crashed for {runtime.OwnerModId}/{runtime.Category}; the remaining Tile Marker features will continue: {ex}",
                            LogLevel.Error);
                    }
                }

                if (categorySucceeded && runtime.SuppressButton)
                    shouldSuppress = true;
            }

            if (shouldSuppress)
                helper.Input.Suppress(pressedButton);

            return ranAnyAction;
        }

        internal string[] GetMarkedTileTokenValues(string input)
        {
            if (!TryParseLookupInput(input, expectedParts: 3, out string[] parts))
                return Array.Empty<string>();

            return GetEffectiveTiles(parts[0], parts[1], parts[2])
                .OrderBy(tile => tile.Y)
                .ThenBy(tile => tile.X)
                .Select(tile => $"{tile.X}_{tile.Y}")
                .ToArray();
        }

        internal string[] GetIsTileMarkedTokenValue(string input)
        {
            if (!TryParseLookupInput(input, expectedParts: 5, out string[] parts)
                || !int.TryParse(parts[3], NumberStyles.Integer, CultureInfo.InvariantCulture, out int x)
                || !int.TryParse(parts[4], NumberStyles.Integer, CultureInfo.InvariantCulture, out int y))
            {
                return new[] { "false" };
            }

            bool marked = GetEffectiveTiles(parts[0], parts[1], parts[2]).Contains(new Point(x, y));
            return new[] { marked ? "true" : "false" };
        }

        private HashSet<Point> GetEffectiveTiles(string ownerModId, string category, string locationName)
        {
            var result = new HashSet<Point>();
            if (string.IsNullOrWhiteSpace(ownerModId)
                || string.IsNullOrWhiteSpace(category)
                || string.IsNullOrWhiteSpace(locationName))
            {
                return result;
            }

            IReadOnlyList<(string OwnerModId, string Category)> categories = MergeCompatibleCategories
                ? store.GetCompatibleCategories(ownerModId, category)
                : new[] { (ownerModId, category) };

            foreach (var compatible in categories)
                result.UnionWith(store.GetExpandedTiles(compatible.OwnerModId, compatible.Category, locationName));

            return result;
        }

        private bool IsEffectivelyMarked(
            string ownerModId,
            string category,
            GameLocation location,
            Point tile)
        {
            IReadOnlyList<(string OwnerModId, string Category)> categories = MergeCompatibleCategories
                ? store.GetCompatibleCategories(ownerModId, category)
                : new[] { (ownerModId, category) };

            foreach (var compatible in categories)
            {
                if (store.IsTileMarked(compatible.OwnerModId, compatible.Category, location.NameOrUniqueName, tile))
                    return true;

                if (!string.Equals(location.NameOrUniqueName, location.Name, StringComparison.Ordinal)
                    && store.IsTileMarked(compatible.OwnerModId, compatible.Category, location.Name, tile))
                {
                    return true;
                }
            }

            return false;
        }

        private RuntimeCategory TryCreateRuntimeCategory(
            (string OwnerModId, string Category) key,
            string categoryDisplayName,
            ContentPatcherInteractionData interaction)
        {
            string[] actions = interaction?.Actions?
                .Where(action => !string.IsNullOrWhiteSpace(action))
                .Select(action => action.Trim())
                .ToArray()
                ?? Array.Empty<string>();

            if (actions.Length == 0)
                return null;

            if (!TryParseTarget(interaction.Target, out InteractionTarget target))
            {
                monitor.Log(
                    $"[Tile Marker CP] Ignored interactions for {key.OwnerModId}/{key.Category}: Target must be Facing, Standing, or Cursor.",
                    LogLevel.Warn);
                return null;
            }

            string rawButton = string.IsNullOrWhiteSpace(interaction.Button) ? "Action" : interaction.Button.Trim();
            if (rawButton.Equals("Action", StringComparison.OrdinalIgnoreCase))
            {
                return new RuntimeCategory(key, target, RuntimeButton.Action, null, actions, interaction.SuppressButton);
            }

            if (rawButton.Equals("UseTool", StringComparison.OrdinalIgnoreCase))
            {
                return new RuntimeCategory(key, target, RuntimeButton.UseTool, null, actions, interaction.SuppressButton);
            }

            try
            {
                // KeybindList.Parse accepts a bare digit as an undefined numeric SButton value
                // (e.g. "5" becomes enum value 5), rather than the corresponding keyboard key.
                // Content Patcher config fields are plain text, so normalize the natural value
                // players enter in GMCM to SMAPI's D0-D9 names.
                string keybindText = NormalizeKeybindText(rawButton);
                KeybindList keybind = KeybindList.Parse(keybindText);
                if (keybind == null || !keybind.Keybinds.Any())
                    throw new FormatException("the keybind was empty");

                if (interaction.ConfigurableButton)
                {
                    string configKey = $"{key.OwnerModId}/{key.Category}";
                    string optionName = string.IsNullOrWhiteSpace(interaction.ButtonConfigName)
                        ? helper.Translation.Get(
                            "gmcm.option.interaction-button.name",
                            new { category = categoryDisplayName }).ToString()
                        : interaction.ButtonConfigName.Trim();
                    string optionTooltip = string.IsNullOrWhiteSpace(interaction.ButtonConfigTooltip)
                        ? null
                        : interaction.ButtonConfigTooltip.Trim();

                    var option = new ConfigurableInteractionButton(
                        key,
                        configKey,
                        optionName,
                        optionTooltip,
                        keybindText);
                    configurableButtons.Add(option);
                    keybind = ResolveConfiguredButton(configKey, keybindText);
                }

                return new RuntimeCategory(key, target, RuntimeButton.Keybind, keybind, actions, interaction.SuppressButton);
            }
            catch (Exception ex)
            {
                monitor.Log(
                    $"[Tile Marker CP] Ignored interactions for {key.OwnerModId}/{key.Category}: Button '{rawButton}' isn't Action, UseTool, or a valid SMAPI keybind ({ex.Message}).",
                    LogLevel.Warn);
                return null;
            }
        }

        private KeybindList ResolveConfiguredButton(string configKey, string defaultButton)
        {
            IDictionary<string, string> overrides = getInteractionButtonOverrides?.Invoke();
            if (overrides != null && overrides.TryGetValue(configKey, out string configured))
            {
                try
                {
                    if (string.IsNullOrWhiteSpace(configured))
                        return new KeybindList();

                    return KeybindList.Parse(NormalizeKeybindText(configured));
                }
                catch (Exception ex)
                {
                    monitor.Log(
                        $"[Tile Marker CP] Ignored invalid configured button '{configured}' for {configKey}: {ex.Message}",
                        LogLevel.Warn);
                }
            }

            return KeybindList.Parse(defaultButton);
        }

        private static string NormalizeKeybindText(string rawButton)
        {
            return rawButton.Length == 1 && char.IsDigit(rawButton[0])
                ? $"D{rawButton}"
                : rawButton;
        }

        private static bool TryParseCategoryKey(
            string rawKey,
            out string ownerModId,
            out string category,
            out string error)
        {
            ownerModId = null;
            category = null;
            error = null;

            if (string.IsNullOrWhiteSpace(rawKey))
            {
                error = "the key is empty; expected 'OwnerModId/Category'.";
                return false;
            }

            int separator = rawKey.IndexOf('/');
            if (separator <= 0 || separator >= rawKey.Length - 1)
            {
                error = "expected a key in the form 'OwnerModId/Category'.";
                return false;
            }

            ownerModId = rawKey.Substring(0, separator).Trim();
            category = rawKey.Substring(separator + 1).Trim();
            if (ownerModId.Length == 0 || category.Length == 0)
            {
                error = "the owner mod ID and category must both be non-empty.";
                return false;
            }

            return true;
        }

        private static bool TryParseLookupInput(string input, int expectedParts, out string[] parts)
        {
            parts = (input ?? "")
                .Split(',')
                .Select(part => part.Trim())
                .ToArray();

            return parts.Length == expectedParts && parts.All(part => part.Length > 0);
        }

        private static bool TryParseTarget(string rawTarget, out InteractionTarget target)
        {
            string value = string.IsNullOrWhiteSpace(rawTarget) ? "Facing" : rawTarget.Trim();
            if (value.Equals("Facing", StringComparison.OrdinalIgnoreCase))
            {
                target = InteractionTarget.Facing;
                return true;
            }
            if (value.Equals("Standing", StringComparison.OrdinalIgnoreCase))
            {
                target = InteractionTarget.Standing;
                return true;
            }
            if (value.Equals("Cursor", StringComparison.OrdinalIgnoreCase))
            {
                target = InteractionTarget.Cursor;
                return true;
            }

            target = default;
            return false;
        }

        private Point GetTargetTile(InteractionTarget target)
        {
            Point standing = Game1.player.TilePoint;
            if (target == InteractionTarget.Standing)
                return standing;
            if (target == InteractionTarget.Cursor)
            {
                Vector2 cursor = helper.Input.GetCursorPosition().Tile;
                return new Point((int)cursor.X, (int)cursor.Y);
            }

            return Game1.player.FacingDirection switch
            {
                Game1.up => new Point(standing.X, standing.Y - 1),
                Game1.right => new Point(standing.X + 1, standing.Y),
                Game1.down => new Point(standing.X, standing.Y + 1),
                Game1.left => new Point(standing.X - 1, standing.Y),
                _ => standing,
            };
        }

        private static string ExpandRuntimePlaceholders(string action, GameLocation location, Point tile)
        {
            return action
                .Replace("{TileX}", tile.X.ToString(CultureInfo.InvariantCulture), StringComparison.OrdinalIgnoreCase)
                .Replace("{TileY}", tile.Y.ToString(CultureInfo.InvariantCulture), StringComparison.OrdinalIgnoreCase)
                .Replace("{LocationName}", location.Name ?? "", StringComparison.OrdinalIgnoreCase)
                .Replace("{LocationUniqueName}", location.NameOrUniqueName ?? "", StringComparison.OrdinalIgnoreCase)
                .Replace("{PlayerId}", Game1.player.UniqueMultiplayerID.ToString(CultureInfo.InvariantCulture), StringComparison.OrdinalIgnoreCase);
        }

        private enum InteractionTarget
        {
            Facing,
            Standing,
            Cursor,
        }

        private enum RuntimeButton
        {
            Action,
            UseTool,
            Keybind,
        }

        private sealed class RuntimeCategory
        {
            public string OwnerModId { get; }
            public string Category { get; }
            public InteractionTarget Target { get; }
            public RuntimeButton Button { get; }
            public KeybindList Keybind { get; set; }
            public string[] Actions { get; }
            public bool SuppressButton { get; }

            public RuntimeCategory(
                (string OwnerModId, string Category) key,
                InteractionTarget target,
                RuntimeButton button,
                KeybindList keybind,
                string[] actions,
                bool suppressButton)
            {
                OwnerModId = key.OwnerModId;
                Category = key.Category;
                Target = target;
                Button = button;
                Keybind = keybind;
                Actions = actions;
                SuppressButton = suppressButton;
            }

            public bool MatchesButton(SButton pressedButton)
            {
                return Button switch
                {
                    RuntimeButton.Action => pressedButton.IsActionButton(),
                    RuntimeButton.UseTool => pressedButton.IsUseToolButton(),
                    RuntimeButton.Keybind => Keybind?.JustPressed() == true,
                    _ => false,
                };
            }
        }
    }

    internal sealed class ConfigurableInteractionButton
    {
        public (string OwnerModId, string Category) CategoryKey { get; }
        public string ConfigKey { get; }
        public string DisplayName { get; }
        public string Tooltip { get; }
        public string DefaultButton { get; }

        public ConfigurableInteractionButton(
            (string OwnerModId, string Category) categoryKey,
            string configKey,
            string displayName,
            string tooltip,
            string defaultButton)
        {
            CategoryKey = categoryKey;
            ConfigKey = configKey;
            DisplayName = displayName;
            Tooltip = tooltip;
            DefaultButton = defaultButton;
        }
    }
}
