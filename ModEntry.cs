using System.Linq;
using Microsoft.Xna.Framework;
using StardewModdingAPI;
using StardewModdingAPI.Events;
using StardewValley;
using TileMarker.Api;
using TileMarker.Config;
using TileMarker.ContentPatcherInterop;
using TileMarker.Core;

namespace TileMarker
{
    public partial class ModEntry : Mod
    {
        internal ModConfig Config;

        private MarkedTileStore store;
        private EditorSession editor;
        private TileMarkerApi api;
        private ContentPatcherIntegration contentPatcherIntegration;
        private bool wasGameWindowActive = true;
        private SButton? activeDragButton;

        public override void Entry(IModHelper helper)
        {
            Config = ReadConfigSafely(helper);
            store = new MarkedTileStore(helper, Monitor);
            store.TileMarksChanged += OnTileMarksChangedForMultiplayer;
            contentPatcherIntegration = new ContentPatcherIntegration(
                helper,
                Monitor,
                ModManifest,
                store,
                () => Config?.MergeCompatibleCategories == true);

            helper.Events.GameLoop.GameLaunched += OnGameLaunched;
            helper.Events.GameLoop.SaveLoaded += OnSaveLoaded;
            helper.Events.GameLoop.ReturnedToTitle += OnReturnedToTitle;
            helper.Events.Multiplayer.PeerConnected += OnPeerConnected;
            helper.Events.Multiplayer.ModMessageReceived += OnModMessageReceived;
            helper.Events.Player.Warped += OnWarped;
            helper.Events.Input.ButtonPressed += OnButtonPressed;
            helper.Events.Input.CursorMoved += OnCursorMoved;
            helper.Events.Display.RenderedWorld += OnRenderedWorld;
            helper.Events.Display.MenuChanged += OnMenuChanged;
            helper.Events.GameLoop.UpdateTicked += OnUpdateTicked;
            helper.Events.Content.AssetRequested += contentPatcherIntegration.OnAssetRequested;
            helper.Events.Content.AssetsInvalidated += contentPatcherIntegration.OnAssetsInvalidated;
        }

        public override object GetApi()
        {
            editor ??= CreateEditorSession();
            api ??= new TileMarkerApi(store, OpenEditorFor, Monitor, () => Config?.MergeCompatibleCategories == true);
            return api;
        }

        private void OnGameLaunched(object sender, GameLaunchedEventArgs e)
        {
            var gmcm = Helper.ModRegistry.GetApi<IGenericModConfigMenuApi>("spacechase0.GenericModConfigMenu");
            if (gmcm != null)
                ModConfigMenu.Register(this, gmcm);

            contentPatcherIntegration.Initialize();
        }

        private void OnSaveLoaded(object sender, SaveLoadedEventArgs e)
        {
            if (Context.IsMainPlayer)
            {
                store.LoadForCurrentSave();
                SendTileSnapshot();
            }
            else
            {
                store.ClearLoadedData();
                RequestTileSnapshotFromHost();
            }
        }

        private void OnReturnedToTitle(object sender, ReturnedToTitleEventArgs e)
        {
            editor?.Abort();
            activeDragButton = null;
            store.ClearLoadedData();
        }

        private void OnWarped(object sender, WarpedEventArgs e)
        {
            if (e.IsLocalPlayer)
            {
                editor?.OnWarped();
                activeDragButton = null;
            }
        }

        private void OpenEditorFor(string ownerModId, string category)
        {
            if (!Context.IsMainPlayer)
            {
                ShowHudMessage("hud.host-only", HUDMessage.error_type);
                return;
            }

            editor ??= CreateEditorSession();
            editor.Open(ownerModId, category);
        }

        private void OnButtonPressed(object sender, ButtonPressedEventArgs e)
        {
            if (!Context.IsWorldReady)
                return;

            if (Config.OpenEditorKey.JustPressed())
            {
                if (editor != null && editor.IsActive)
                {
                    editor.CloseAndSave();
                    return;
                }

                if (!Context.IsMainPlayer)
                {
                    ShowHudMessage("hud.host-only", HUDMessage.error_type);
                    return;
                }

                if (IsEditorBlockedByGameState())
                {
                    ShowHudMessage("hud.editor-unavailable", HUDMessage.error_type);
                    return;
                }

                OpenPickerOrDirect();
                return;
            }

            if (editor == null || !editor.IsActive)
            {
                contentPatcherIntegration.TryHandleButton(e.Button);
                return;
            }

            if (e.Button == SButton.Escape)
            {
                if (!editor.TryCancelDrag())
                    editor.CloseAndSave();
                activeDragButton = null;
                Helper.Input.Suppress(e.Button);
                return;
            }

            if (e.Button == SButton.MouseLeft)
            {
                if (Config.EnableLeftClickBrush)
                {
                    if (!editor.IsDragging)
                    {
                        editor.StartDrag(CurrentCursorTile(), rectangleMode: false);
                        activeDragButton = SButton.MouseLeft;
                    }
                }
                else
                    editor.ToggleSingleTile(CurrentCursorTile());

                Helper.Input.Suppress(e.Button);
            }
        }

        private void OnCursorMoved(object sender, CursorMovedEventArgs e)
        {
            if (editor?.IsActive != true || !editor.IsDragging)
                return;

            Vector2 tile = e.NewPosition.Tile;
            editor.OnDragMoved(new Point((int)tile.X, (int)tile.Y));
        }

        private void OnRenderedWorld(object sender, RenderedWorldEventArgs e)
        {
            editor?.Draw(e.SpriteBatch);
        }

        private void OnMenuChanged(object sender, MenuChangedEventArgs e)
        {
            if (editor?.IsActive == true && e.NewMenu != null)
            {
                editor.CloseAndSave();
                activeDragButton = null;
            }
        }

        private void OnUpdateTicked(object sender, UpdateTickedEventArgs e)
        {
            bool isGameWindowActive = Game1.game1 == null || Game1.game1.IsActive;
            if (!isGameWindowActive)
            {
                if (wasGameWindowActive)
                {
                    editor?.TryCancelDrag();
                    activeDragButton = null;
                }

                wasGameWindowActive = false;
                return;
            }

            wasGameWindowActive = true;

            if (editor?.IsActive != true)
            {
                activeDragButton = null;
                return;
            }

            Point cursorTile = CurrentCursorTile();
            SButtonState rightButtonState = Helper.Input.GetState(SButton.MouseRight);

            if (!editor.IsDragging && rightButtonState == SButtonState.Pressed)
            {
                bool rectangleMode = Helper.Input.IsDown(SButton.LeftShift)
                    || Helper.Input.IsDown(SButton.RightShift);
                editor.StartDrag(cursorTile, rectangleMode);
                activeDragButton = SButton.MouseRight;
            }

            if (editor.IsDragging)
            {
                SButton dragButton = activeDragButton ?? SButton.MouseRight;
                bool dragButtonIsDown = Helper.Input.IsDown(dragButton)
                    || Helper.Input.IsSuppressed(dragButton);

                if (!dragButtonIsDown)
                {
                    editor.EndDrag(cursorTile);
                    activeDragButton = null;
                }
                else
                    editor.OnDragMoved(cursorTile);
            }
            else
            {
                activeDragButton = null;
                editor.UpdatePointer(cursorTile);
            }

            if (Game1.dialogueUp || Game1.eventUp)
                editor.CloseAndSave();
        }

        private void OpenPickerOrDirect()
        {
            var categories = store.RegisteredCategories;
            if (categories.Count == 0)
            {
                Monitor.Log("[Tile Marker] No mod has registered a tile category yet — nothing to mark.", LogLevel.Info);
                return;
            }

            if (categories.Count == 1)
            {
                OpenEditorFor(categories[0].OwnerModId, categories[0].Category);
                return;
            }

            Game1.activeClickableMenu = new CategoryPickerMenu(
                categories.ToList(),
                OpenEditorFor,
                Helper.Translation.Get("picker.title").ToString()
            );
        }

        private static bool IsEditorBlockedByGameState()
        {
            return Game1.activeClickableMenu != null
                || Game1.dialogueUp
                || Game1.eventUp
                || Game1.player == null
                || !Game1.player.CanMove;
        }

        private void ShowHudMessage(string translationKey, int messageType)
        {
            if (!Context.IsWorldReady)
                return;

            Game1.addHUDMessage(new HUDMessage(
                Helper.Translation.Get(translationKey).ToString(),
                messageType
            ));
        }

        private Point CurrentCursorTile()
        {
            Vector2 tile = Helper.Input.GetCursorPosition().Tile;
            return new Point((int)tile.X, (int)tile.Y);
        }

        private EditorSession CreateEditorSession()
        {
            return new EditorSession(
                Helper,
                Monitor,
                store,
                () => Config.OverlayOpacityPercent,
                () => Config.EnableLeftClickBrush
            );
        }
    }
}
