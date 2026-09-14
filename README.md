# Tile Marker

Tile Marker is a small framework that lets players mark map tiles visually inside Stardew Valley,
so they don't need to find coordinates or edit long lists in `config.json`.

Other mods decide what the marked tiles do. A tile can become a special interaction spot, a
no-spawn area, a place that doesn't block vision, or anything else the integrating mod needs.
Tile Marker provides the visual editor, saves the selections, and makes them available to those
mods.

> **In short:** another mod registers a category, the player marks tiles in-game, and that mod uses
> the saved selection.

## For players

You only see categories added by mods that support Tile Marker.

1. Open Tile Marker's configuration in Generic Mod Config Menu and choose the **Open tile editor**
   key.
2. Press that key in-game and choose what you want to mark.
3. Click or drag over the map tiles, then press `Esc` to save.
4. Use the integrating mod normally. It will read the tiles you selected.

The host edits and saves selections in multiplayer. Connected farmhands receive the host's saved
tiles automatically.

## For mod authors

Tile Marker supports two integration styles:

| Your mod | Recommended integration | What you can do |
| --- | --- | --- |
| SMAPI mod with a DLL | [C# API](#c-api-quick-start) | Register categories, open the editor, query tiles, and react immediately to changes. |
| JSON-only content pack | [Content Patcher](#content-patcher-only-integration) | Register categories, run trigger actions on marked tiles, and query selections through tokens. |

Both options use the same editor and saved-tile system. A content pack doesn't need its own DLL,
and adding Content Patcher support doesn't change the existing C# API.

## C# API quick start

### 1. Copy the API contract into your project

Copy [`Api/ITileMarkerApi.cs`](Api/ITileMarkerApi.cs) into your mod's source code. You may change
its namespace to match your project.

You don't need to reference `TileMarker.dll`. The copied file only tells SMAPI which methods your
mod wants to use.

### 2. Add Tile Marker to your `manifest.json`

If Tile Marker is an optional convenience for your players, add this entry to your existing
`Dependencies` list:

```json
"Dependencies": [
  {
    "UniqueID": "NatrollEXE.TileMarker",
    "IsRequired": false
  }
]
```

Keep `IsRequired` as `false` if your mod can still work without the visual editor. Change it to
`true` only if your mod cannot work without Tile Marker.

### 3. Get the API and register a category

Register your category once in `GameLaunched`. A category is one independent group of marked
tiles owned by your mod.

```csharp
using StardewModdingAPI;
using StardewModdingAPI.Events;
using TileMarker.Api;

namespace ExampleMod
{
    public class ModEntry : Mod
    {
        private const string TileCategory = "NoSpawnArea";
        private ITileMarkerApi tileMarkerApi;

        public override void Entry(IModHelper helper)
        {
            helper.Events.GameLoop.GameLaunched += OnGameLaunched;
        }

        private void OnGameLaunched(object sender, GameLaunchedEventArgs e)
        {
            tileMarkerApi = Helper.ModRegistry.GetApi<ITileMarkerApi>("NatrollEXE.TileMarker");

            if (tileMarkerApi == null)
            {
                Monitor.Log("Tile Marker isn't installed; the visual tile editor will be unavailable.", LogLevel.Trace);
                return;
            }

            tileMarkerApi.RegisterCategory(
                ModManifest.UniqueID,
                TileCategory,
                "Areas where spawning is disabled"
            );
        }
    }
}
```

The three registration values are:

- `ModManifest.UniqueID`: identifies your mod as the owner.
- `TileCategory`: a stable internal ID chosen by you. Don't translate or rename it after release.
- The final text: the player-facing category name shown by Tile Marker. This can be translated.

You may register more than one category if your mod needs separate tile groups.

### Optional: let compatible categories merge

Two mods can keep independent selections while allowing players to use their combined tiles.
Register both categories with the same stable shared-group ID:

```csharp
tileMarkerApi.RegisterCategoryWithSharedGroup(
    ModManifest.UniqueID,
    TileCategory,
    "Transparent vision obstacles",
    "ExampleAuthor.SharedVisionIgnored"
);
```

When **Merge compatible categories** is enabled in Tile Marker's GMCM, API lookups for one
category include every registered category in the same shared group. The saved selections are
never copied or overwritten. Disabling the option immediately restores each category's individual
results. Categories registered normally with `RegisterCategory` are never merged.

### 4. Open the editor

This can be called from your own keybind, GMCM button, command, or menu:

```csharp
tileMarkerApi?.OpenEditor(ModManifest.UniqueID, TileCategory);
```

The category must be registered before you open it. The editor always opens on the player's
current location.

Players can also use Tile Marker's own keybind and choose a registered category from its picker.

### 5. Check whether a tile was marked

For most mods, this is the easiest way to use the result:

```csharp
bool isMarked = tileMarkerApi?.IsTileMarked(
    ModManifest.UniqueID,
    TileCategory,
    location,
    tileX,
    tileY
) == true;
```

`IsTileMarked()` returns `false` when the tile isn't marked. It also returns `false` safely when
the location is unavailable.

That's the complete basic integration. The sections below are only needed for more advanced uses.

## Getting all marked ranges

If you need the complete selection for a location, ask for its saved ranges:

```csharp
IReadOnlyList<string> ranges = tileMarkerApi?.GetMarkedTileRanges(
    ModManifest.UniqueID,
    TileCategory,
    location.NameOrUniqueName
) ?? Array.Empty<string>();
```

The result uses compact coordinate strings:

- `"5,8"` means one tile.
- `"5-9,8"` means a horizontal range.

Selections are returned row by row. For example, a rectangle covering three rows is returned as
three horizontal ranges. This keeps the saved coordinates easy to read and process.

The list is empty when that location has no marked tiles. If you only need to test individual
tiles, prefer `IsTileMarked()` so you don't need to parse these strings yourself.

## Reacting immediately when selections change

Subscribe to `TileMarksChanged` if your mod caches the selected tiles or needs to refresh something
as soon as the player closes the editor:

```csharp
// Add this immediately after RegisterCategory() in the GameLaunched example above:
tileMarkerApi.TileMarksChanged += OnTileMarksChanged;

private void OnTileMarksChanged(object sender, TileMarksChangedEventArgs e)
{
    if (e.OwnerModId != ModManifest.UniqueID || e.Category != TileCategory)
        return;

    Monitor.Log($"Tile selection changed in {e.LocationName}.", LogLevel.Trace);
    // Refresh your cached data here, if your mod uses a cache.
}
```

Always filter the event by owner and category, since other mods may also use Tile Marker.

## API reference

| Member | What it does |
| --- | --- |
| `RegisterCategory(ownerModId, category, displayName)` | Registers one tile group and makes it available in the category picker. |
| `RegisterCategoryWithSharedGroup(ownerModId, category, displayName, sharedGroup)` | Registers an independent category that can be read together with compatible categories when merging is enabled. |
| `OpenEditor(ownerModId, category)` | Opens a registered category on the player's current location. |
| `IsTileMarked(ownerModId, category, location, x, y)` | Checks one tile without requiring any range parsing. |
| `GetMarkedTileRanges(ownerModId, category, locationName)` | Returns every marked coordinate range for one location. |
| `TileMarksChanged` | Raised after a changed selection is saved. |

## Optional integration and fallback behavior

If Tile Marker is optional, always null-check `tileMarkerApi`. Your mod should keep its normal
behavior when the API isn't available.

For example, a mod may continue accepting manually written coordinates in its own `config.json`,
while Tile Marker gives players an easier visual way to create or update the same data.

## Content Patcher-only integration

Content packs can register categories and add interactions without shipping a DLL. This bridge is
optional: the existing C# API, editor, save format, and multiplayer synchronization don't depend on
Content Patcher.

Add Tile Marker as a dependency in the content pack's `manifest.json`:

```json
"Dependencies": [
  {
    "UniqueID": "NatrollEXE.TileMarker",
    "MinimumVersion": "1.1.0",
    "IsRequired": true
  }
]
```

Then add a category to `Mods/NatrollEXE.TileMarker/Categories`. The entry key must use
`OwnerModId/Category`; `{{ModId}}` supplies the content pack's own unique ID:

The interaction button should belong to the content pack's own `ConfigSchema`, for example:

```json
"ConfigSchema": {
  "WaterSinkButton": {
    "Default": "F"
  }
}
```

Pass that config token to Tile Marker in the category entry:

```json
{
  "Action": "EditData",
  "Target": "Mods/NatrollEXE.TileMarker/Categories",
  "Entries": {
    "{{ModId}}/WaterSources": {
      "DisplayName": "Water sources",
      "Interaction": {
        "Button": "{{WaterSinkButton}}",
        "Target": "Facing",
        "SuppressButton": true,
        "Actions": [
          "AddItem (O)YourName.YourItem 1"
        ]
      }
    }
  }
}
```

The category appears in Tile Marker's existing picker. After the player marks tiles, pressing the
configured interaction button on the chosen target runs the trigger actions in order.

### Category fields

| Field | Required | Meaning |
| --- | --- | --- |
| `DisplayName` | no | Text shown in the category picker; defaults to the category ID. Supports Content Patcher tokens like `{{i18n:key}}`. |
| `SharedGroup` | no | Stable group ID used by Tile Marker's existing merge-compatible-categories option. |
| `Interaction` | no | Omit this when the pack only needs the category and query tokens. |
| `Interaction.Button` | no | `Action`, `UseTool`, or a SMAPI keybind string such as `F` or `LeftShift + F`. Defaults to `Action`. This can use a config token owned by the content pack, such as `{{WaterSinkButton}}`; Tile Marker only reads the resolved value. |
| `Interaction.Target` | no | `Facing`, `Standing`, or `Cursor`. Defaults to `Facing`. |
| `Interaction.Actions` | yes, for interactions | One or more Stardew 1.6 trigger action strings. |
| `Interaction.SuppressButton` | no | Suppress the original button after an action succeeds. Defaults to `true`. |

Interaction actions can use these runtime placeholders, which Tile Marker replaces immediately
before running the action:

| Placeholder | Value |
| --- | --- |
| `{TileX}` / `{TileY}` | The marked target tile coordinates. |
| `{LocationName}` | The current location's base name. |
| `{LocationUniqueName}` | The current location's unique name. |
| `{PlayerId}` | The current player's multiplayer ID. |

These use single braces intentionally, so they don't conflict with Content Patcher tokens.

### Run a normal map action

Tile Marker 1.1.0 includes a trigger action for Content Patcher packs which need the marked tile to
behave like a normal map tile action:

```json
"Actions": [
  "NatrollEXE.TileMarker_PerformMapAction {TileX} {TileY} Kitchen"
]
```

The format is `NatrollEXE.TileMarker_PerformMapAction <tile X> <tile Y> <map action>`. For example,
`Kitchen` follows the game's normal kitchen activation path, so mods which replace the vanilla
cooking menu can handle it as usual.

### Content Patcher tokens

Tile Marker provides two tokens. Both use the same owner/category IDs as the category entry:

```json
"When": {
  "NatrollEXE.TileMarker/IsTileMarked:{{ModId}},WaterSources,{{LocationUniqueName}},5,8": "true"
}
```

`IsTileMarked` accepts `OwnerModId, Category, LocationName, X, Y` and returns `true` or `false`.

`MarkedTiles` accepts `OwnerModId, Category, LocationName` and returns every marked tile as an
`X_Y` value, such as `5_8`. The underscore avoids Content Patcher treating the coordinate comma as
a separator:

```json
"When": {
  "NatrollEXE.TileMarker/MarkedTiles:{{ModId}},WaterSources,{{LocationUniqueName}}": "5_8"
}
```

See [`examples/content-patcher`](examples/content-patcher) for a complete JSON-only example.

## Player controls

These controls apply only while the tile editor is open:

- **Left-click**: mark or unmark one tile.
- **Right-click and drag from an empty tile**: paint continuously.
- **Right-click and drag from a marked tile**: erase continuously.
- **Shift + right-click and drag**: mark or unmark a rectangle.
- **Esc during a drag**: cancel that drag.
- **Esc while not dragging**: close and save.
- Pressing the configured editor key again also closes and saves.

### Touchscreens and Cinderbox

Enable **Paint by dragging with left click** in Generic Mod Config Menu, or set this in
`config.json`:

```json
"EnableLeftClickBrush": true
```

While this option is enabled, tapping still changes one tile. Holding and dragging with a finger
paints or erases continuously. The option is disabled by default, so desktop mouse controls don't
change unless the player enables it.

## How selections are saved

- Selections are stored separately for each save file, location, mod, and category.
- Closing the editor saves changes immediately, so they survive quitting before the next in-game day.
- Stardew's save data also receives a backup copy during a normal game save.
- Only the host can edit selections in multiplayer.
- Reopening the editor shows the selections already stored for the current map.

## Building Tile Marker

Requirements:

- [.NET SDK](https://dotnet.microsoft.com/download), targeting `net6.0`.
- Stardew Valley installed locally.

The project uses
[`Pathoschild.Stardew.ModBuildConfig`](https://www.nuget.org/packages/Pathoschild.Stardew.ModBuildConfig)
to detect the game path and package the mod automatically.

```sh
dotnet build
```

After a successful build, the mod is copied to the game's `Mods` folder and packaged under
`bin/Debug/net6.0/`. If the game path can't be detected, set `<GamePath>` in `TileMarker.csproj`.
