using StardewModdingAPI;
using StardewModdingAPI.Events;
using StardewValley;

namespace JsonReload;

public sealed class ModEntry : Mod
{
    private ModConfig config = new();
    private ContentPackTracker tracker = null!;
    private SmapiCommandInvoker commandInvoker = null!;
    private int lastReloadTick = -1;
    private int initialScanTicksRemaining = -1;

    public override void Entry(IModHelper helper)
    {
        this.config = helper.ReadConfig<ModConfig>();
        this.tracker = new ContentPackTracker(helper, this.Monitor);
        this.commandInvoker = new SmapiCommandInvoker(helper.ConsoleCommands);

        helper.Events.GameLoop.GameLaunched += this.OnGameLaunched;
        helper.Events.GameLoop.UpdateTicked += this.OnUpdateTicked;
        helper.Events.Input.ButtonPressed += this.OnButtonPressed;

        helper.ConsoleCommands.Add(
            "json_reload",
            "Reload changed Content Patcher JSON and i18n files.",
            (_, _) => this.ReloadChangedPacks()
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

        int count = this.tracker.DiscoverAndSnapshot();
        this.Monitor.Log($"Tracking {count} loaded Content Patcher content pack(s) for JSON changes.", LogLevel.Info);
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

        this.ReloadChangedPacks();
    }

    private void ReloadChangedPacks()
    {
        IReadOnlyList<PackChanges> changes = this.tracker.FindChanges();
        if (changes.Count == 0)
        {
            this.Monitor.Log("No changed Content Patcher JSON files were found.", LogLevel.Info);
            this.ShowHudMessage(this.Helper.Translation.Get("message.no-changes"));
            return;
        }

        var validReloads = new List<PackChanges>();
        var invalidPacks = new List<string>();
        var restartOnlyFiles = new List<string>();
        bool translationsChanged = false;

        foreach (PackChanges packChanges in changes)
        {
            if (!this.tracker.TryValidateChanges(packChanges, out IReadOnlyList<JsonValidationError> errors))
            {
                invalidPacks.Add(packChanges.Pack.Name);
                foreach (JsonValidationError error in errors)
                {
                    string position = error.Line is int line
                        ? $" (line {line}, column {error.Column ?? 0})"
                        : string.Empty;
                    this.Monitor.Log(
                        $"Invalid JSON in '{packChanges.Pack.Name}': {error.RelativePath}{position}: {error.Message}",
                        LogLevel.Error
                    );
                }

                continue;
            }

            string[] reloadablePaths = packChanges.ChangedPaths
                .Where(path => !ContentPackTracker.IsRestartOnlyPath(path))
                .ToArray();

            restartOnlyFiles.AddRange(packChanges.ChangedPaths
                .Where(ContentPackTracker.IsRestartOnlyPath)
                .Select(path => $"{packChanges.Pack.Name}: {path}"));

            if (reloadablePaths.Any(ContentPackTracker.IsTranslationPath))
                translationsChanged = true;

            if (reloadablePaths.Length > 0)
                validReloads.Add(packChanges);
            else
                packChanges.Pack.ReplaceSnapshot(packChanges.CurrentSnapshot);
        }

        if (invalidPacks.Count > 0)
        {
            this.ShowHudMessage(
                this.Helper.Translation.Get("message.invalid", new { count = invalidPacks.Count }),
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
        foreach (PackChanges packChanges in validReloads)
        {
            if (!this.TryRunCommand("patch", new[] { "reload", packChanges.Pack.UniqueId }))
                continue;

            packChanges.Pack.ReplaceSnapshot(packChanges.CurrentSnapshot);
            reloadedNames.Add(packChanges.Pack.Name);
        }

        if (reloadedNames.Count > 0)
        {
            string names = string.Join(", ", reloadedNames);
            this.Monitor.Log($"Hot-reloaded {reloadedNames.Count} content pack(s): {names}.", LogLevel.Info);
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
