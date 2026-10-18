# Json Reload

Reload changed Content Patcher JSON files without restarting Stardew Valley.

Json Reload is a small utility tool. Press one key and it finds the loaded content packs you changed, validates their JSON, and reloads only those packs.

## Requirements

- Stardew Valley 1.6
- SMAPI 4.5.0 or later
- Content Patcher 2.9.0 or later
- Generic Mod Config Menu (optional)

## Install

1. Install the requirements above.
2. Download and unzip Json Reload into your Stardew Valley `Mods` folder.
3. Start the game through SMAPI.

## Use

1. Edit and save a JSON or translation file in a loaded Content Patcher pack.
2. Close any open dialogue or menu and finish the current event.
3. Return to the game and press `F6`.

You can change the key through Generic Mod Config Menu. You can also run `json_reload` in the SMAPI console.

Json Reload reports invalid JSON in the SMAPI console instead of attempting to reload the affected pack.

### Example

You notice that a Content Patcher mod is not available in your language, or you forgot to add your translation file before starting the game. Add the translation file to the mod's `i18n` folder, return to the game, and press the reload key. Json Reload updates the content pack without requiring you to restart the game.

## What can be reloaded

- Content Patcher JSON files
- Content Patcher `i18n` translation files

Files such as `manifest.json` and `config.json`, C# mods, and content packs for other frameworks may still require a restart. Dialogues or events already in progress are not rebuilt.
