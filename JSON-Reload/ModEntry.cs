using StardewModdingAPI;
using StardewModdingAPI.Events;
using StardewValley;

namespace JsonReload;

public sealed class ModEntry : Mod
{
    private ModConfig config = new();
    private JsonFileTracker tracker = null!;
    private SmapiCommandInvoker commandInvoker = null!;
    private int lastReloadTick = -1;
    private int initialScanTicksRemaining = -1;

    public override void Entry(IModHelper helper)
    {
        this.config = helper.ReadConfig<ModConfig>();
        this.tracker = new JsonFileTracker(helper, this.Monitor);
        this.commandInvoker = new SmapiCommandInvoker(helper.ConsoleCommands);

        helper.Events.GameLoop.GameLaunched += this.OnGameLaunched;
        helper.Events.GameLoop.UpdateTicked += this.OnUpdateTicked;
        helper.Events.Input.ButtonPressed += this.OnButtonPressed;

        helper.ConsoleCommands.Add(
            "json_reload",
            "Reload changed Content Patcher JSON and SMAPI mod i18n files.",
            (_, _) => this.ReloadChangedJson()
        );
    }

    private void OnGameLaunched(object? sender, GameLaunchedEventArgs e)
    {
        this.RegisterConfigMenu();
        this.initialScanTicksRemaining = 4;
    }

    private void OnUpdateTicked(object? sender, UpdateTickedEventArgs e)
    {
        if (this.initialScanTicksRemaining < 0)
            return;

        if (this.initialScanTicksRemaining-- > 0)
            return;

        TrackingSummary summary = this.tracker.DiscoverAndSnapshot();
        this.Monitor.Log(
            $"Tracking {summary.ContentPackCount} loaded Content Patcher content pack(s) and "
            + $"{summary.TranslationModCount} SMAPI mod translation folder(s) for JSON changes.",
            LogLevel.Info
        );
        this.initialScanTicksRemaining = -1;
    }

    private void OnButtonPressed(object? sender, ButtonPressedEventArgs e)
    {
        if (!this.config.ReloadKey.JustPressed() || this.lastReloadTick == Game1.ticks)
            return;

        this.lastReloadTick = Game1.ticks;
        this.Helper.Input.Suppress(e.Button);

        if (!Context.IsWorldReady)
        {
            this.Monitor.Log(this.Helper.Translation.Get("message.world-not-ready"), LogLevel.Warn);
            return;
        }

        if (Game1.activeClickableMenu is not null || Game1.eventUp)
        {
            this.ShowHudMessage(this.Helper.Translation.Get("message.blocked"), isError: true);
            return;
        }

        this.ReloadChangedJson();
    }

    private void ReloadChangedJson()
    {
        IReadOnlyList<ModChanges> changes = this.tracker.FindChanges();
        if (changes.Count == 0)
        {
            this.Monitor.Log("No changed reloadable JSON files were found.", LogLevel.Info);
            this.ShowHudMessage(this.Helper.Translation.Get("message.no-changes"));
            return;
        }

        var validReloads = new List<ModChanges>();
        var invalidMods = new List<string>();
        var restartOnlyFiles = new List<string>();
        bool translationsChanged = false;

        foreach (ModChanges modChanges in changes)
        {
            if (!this.tracker.TryValidateChanges(modChanges, out IReadOnlyList<JsonValidationError> errors))
            {
                invalidMods.Add(modChanges.Mod.Name);
                foreach (JsonValidationError error in errors)
                {
                    string position = error.Line is int line
                        ? $" (line {line}, column {error.Column ?? 0})"
                        : string.Empty;
                    this.Monitor.Log(
                        $"Invalid JSON in '{modChanges.Mod.Name}': {error.RelativePath}{position}: {error.Message}",
                        LogLevel.Error
                    );
                }

                continue;
            }

            string[] reloadablePaths = modChanges.ChangedPaths
                .Where(path => !JsonFileTracker.IsRestartOnlyPath(path))
                .ToArray();

            restartOnlyFiles.AddRange(modChanges.ChangedPaths
                .Where(JsonFileTracker.IsRestartOnlyPath)
                .Select(path => $"{modChanges.Mod.Name}: {path}"));

            if (reloadablePaths.Any(JsonFileTracker.IsTranslationPath))
                translationsChanged = true;

            if (reloadablePaths.Length > 0)
                validReloads.Add(modChanges);
            else
                modChanges.Mod.ReplaceSnapshot(modChanges.CurrentSnapshot);
        }

        if (invalidMods.Count > 0)
        {
            this.ShowHudMessage(
                this.Helper.Translation.Get("message.invalid", new { count = invalidMods.Count }),
                isError: true
            );
        }

        if (restartOnlyFiles.Count > 0)
        {
            this.Monitor.Log(
                "These files can't be safely hot-reloaded and may require a game restart or their own config UI:\n- "
                + string.Join("\n- ", restartOnlyFiles),
                LogLevel.Warn
            );
        }

        if (validReloads.Count == 0)
        {
            if (restartOnlyFiles.Count > 0)
                this.ShowHudMessage(this.Helper.Translation.Get("message.restart-needed"), isError: true);
            return;
        }

        if (translationsChanged && !this.TryRunCommand("reload_i18n", Array.Empty<string>()))
            return;

        var reloadedNames = new List<string>();
        foreach (ModChanges modChanges in validReloads)
        {
            if (modChanges.Mod.IsContentPatcherPack
                && !this.TryRunCommand("patch", new[] { "reload", modChanges.Mod.UniqueId }))
            {
                continue;
            }

            modChanges.Mod.ReplaceSnapshot(modChanges.CurrentSnapshot);
            reloadedNames.Add(modChanges.Mod.Name);
        }

        if (reloadedNames.Count > 0)
        {
            string names = string.Join(", ", reloadedNames);
            this.Monitor.Log($"Reloaded changed JSON for {reloadedNames.Count} mod(s): {names}.", LogLevel.Info);
            this.ShowHudMessage(this.Helper.Translation.Get(
                "message.success",
                new { count = reloadedNames.Count, packs = names }
            ));
        }

        if (restartOnlyFiles.Count > 0)
            this.ShowHudMessage(this.Helper.Translation.Get("message.restart-needed"), isError: true);
    }

    private bool TryRunCommand(string commandName, string[] args)
    {
        if (this.commandInvoker.TryInvoke(commandName, args, out string? error))
            return true;

        this.Monitor.Log($"Could not invoke SMAPI command '{commandName}': {error}", LogLevel.Error);
        this.ShowHudMessage(this.Helper.Translation.Get("message.integration-failed"), isError: true);
        return false;
    }

    private void RegisterConfigMenu()
    {
        IGenericModConfigMenuApi? api = this.Helper.ModRegistry.GetApi<IGenericModConfigMenuApi>(
            "spacechase0.GenericModConfigMenu"
        );
        if (api is null)
            return;

        api.Register(
            this.ModManifest,
            reset: () => this.config = new ModConfig(),
            save: () => this.Helper.WriteConfig(this.config)
        );

        api.AddKeybindList(
            this.ModManifest,
            getValue: () => this.config.ReloadKey,
            setValue: value => this.config.ReloadKey = value,
            name: () => this.Helper.Translation.Get("config.reload-key.name"),
            tooltip: () => this.Helper.Translation.Get("config.reload-key.tooltip"),
            fieldId: "ReloadKey"
        );

        api.AddBoolOption(
            this.ModManifest,
            getValue: () => this.config.ShowHudMessages,
            setValue: value => this.config.ShowHudMessages = value,
            name: () => this.Helper.Translation.Get("config.show-hud.name"),
            tooltip: () => this.Helper.Translation.Get("config.show-hud.tooltip"),
            fieldId: "ShowHudMessages"
        );
    }

    private void ShowHudMessage(string text, bool isError = false)
    {
        if (!this.config.ShowHudMessages || !Context.IsWorldReady)
            return;

        int messageType = isError ? HUDMessage.error_type : HUDMessage.newQuest_type;
        Game1.addHUDMessage(new HUDMessage(text, messageType)
        {
            timeLeft = 4500f,
            noIcon = true
        });
    }
}
