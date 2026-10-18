using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using StardewModdingAPI;
using StardewValley;

namespace TileMarker.Core
{
    /// <summary>Drives one active tile-marking session: grid overlay, click/drag input, and saving on close.</summary>
    internal class EditorSession
    {
        private enum DragMode { None, Add, Remove }
        private enum DragShape { Brush, Rectangle }

        private readonly IModHelper helper;
        private readonly IMonitor monitor;
        private readonly MarkedTileStore store;
        private readonly Func<int> getOverlayOpacityPercent;
        private readonly Func<bool> getLeftClickBrushEnabled;

        private static Texture2D pixel;

        public bool IsActive { get; private set; }
        public bool IsDragging => isDragging;
        public string OwnerModId { get; private set; }
        public string Category { get; private set; }
        private string locationName;
        private string categoryDisplayName;

        private HashSet<Point> workingTiles = new();
        private HashSet<Point> originalTiles = new();

        private bool isDragging;
        private Point dragStart;
        private Point lastDragTile;
        private Point currentHoverTile;
        private DragMode dragMode = DragMode.None;
        private DragShape dragShape = DragShape.Brush;
        private HashSet<Point> tilesBeforeDrag = new();

        public EditorSession(
            IModHelper helper,
            IMonitor monitor,
            MarkedTileStore store,
            Func<int> getOverlayOpacityPercent,
            Func<bool> getLeftClickBrushEnabled
        )
        {
            this.helper = helper;
            this.monitor = monitor;
            this.store = store;
            this.getOverlayOpacityPercent = getOverlayOpacityPercent;
            this.getLeftClickBrushEnabled = getLeftClickBrushEnabled;
        }

        public void Open(string ownerModId, string category)
        {
            if (!Context.IsWorldReady || Game1.currentLocation == null)
                return;

            if (!store.IsRegistered(ownerModId, category))
            {
                monitor.Log(
                    $"[Tile Marker] Refused to edit an unregistered category: {ownerModId ?? "<null>"}/{category ?? "<null>"}.",
                    LogLevel.Warn
                );
                return;
            }

            OwnerModId = ownerModId;
            Category = category;
            locationName = Game1.currentLocation.NameOrUniqueName;
            workingTiles = store.GetExpandedTiles(ownerModId, category, locationName);
            originalTiles = new HashSet<Point>(workingTiles);
            categoryDisplayName = store.GetDisplayName(ownerModId, category);
            currentHoverTile = GetCursorTile();
            isDragging = false;
            dragMode = DragMode.None;
            IsActive = true;

            monitor.Log($"[Tile Marker] Editing {ownerModId}/{category} on {locationName} — {workingTiles.Count} tile(s) currently marked.", LogLevel.Info);
        }

        /// <summary>Saves the working set and exits editing. Call on Esc-while-idle, warp, or re-pressing the open key.</summary>
        public void CloseAndSave()
        {
            if (!IsActive)
                return;

            bool changed = !workingTiles.SetEquals(originalTiles);
            if (changed && !store.SaveTiles(OwnerModId, Category, locationName, workingTiles))
            {
                ShowHudMessage("hud.save-failed", HUDMessage.error_type);
                return;
            }

            IsActive = false;
            isDragging = false;
            dragMode = DragMode.None;
            ShowHudMessage(changed ? "hud.saved" : "hud.no-changes", HUDMessage.newQuest_type);
        }

        /// <summary>Discards the session without saving — used when the player warps away mid-edit.</summary>
        public void Abort(bool showWarpMessage = false)
        {
            IsActive = false;
            isDragging = false;
            dragMode = DragMode.None;

            if (showWarpMessage)
                ShowHudMessage("hud.cancelled-warp", HUDMessage.error_type);
        }

        public void ToggleSingleTile(Point tile)
        {
            if (!IsActive || isDragging || !IsTileInsideCurrentMap(tile))
                return;

            currentHoverTile = tile;
            if (!workingTiles.Add(tile))
                workingTiles.Remove(tile);
        }

        public void StartDrag(Point tile, bool rectangleMode)
        {
            if (!IsActive || isDragging || !IsTileInsideCurrentMap(tile))
                return;

            isDragging = true;
            dragStart = tile;
            lastDragTile = tile;
            currentHoverTile = tile;
            dragMode = workingTiles.Contains(tile) ? DragMode.Remove : DragMode.Add;
            dragShape = rectangleMode ? DragShape.Rectangle : DragShape.Brush;
            tilesBeforeDrag = new HashSet<Point>(workingTiles);

            if (dragShape == DragShape.Brush)
                ApplyDragTile(tile);
        }

        public void OnDragMoved(Point tile)
        {
            currentHoverTile = tile;

            if (!IsActive || !isDragging || dragShape != DragShape.Brush || tile == lastDragTile)
                return;

            ApplyBrushLine(lastDragTile, tile);
            lastDragTile = tile;
        }

        public void EndDrag(Point tile)
        {
            if (!IsActive || !isDragging)
                return;

            currentHoverTile = tile;

            if (dragShape == DragShape.Rectangle)
            {
                var rect = GetDragRect(dragStart, tile);
                for (int x = rect.Left; x <= rect.Right; x++)
                {
                    for (int y = rect.Top; y <= rect.Bottom; y++)
                        ApplyDragTile(new Point(x, y));
                }
            }
            else
                OnDragMoved(tile);

            isDragging = false;
            dragMode = DragMode.None;
        }

        public void UpdatePointer(Point tile)
        {
            currentHoverTile = tile;
            if (isDragging)
                OnDragMoved(tile);
        }

        private void ApplyBrushLine(Point from, Point to)
        {
            int x = from.X;
            int y = from.Y;
            int deltaX = Math.Abs(to.X - from.X);
            int stepX = from.X < to.X ? 1 : -1;
            int deltaY = -Math.Abs(to.Y - from.Y);
            int stepY = from.Y < to.Y ? 1 : -1;
            int error = deltaX + deltaY;

            while (true)
            {
                ApplyDragTile(new Point(x, y));
                if (x == to.X && y == to.Y)
                    break;

                int doubledError = error * 2;
                if (doubledError >= deltaY)
                {
                    error += deltaY;
                    x += stepX;
                }
                if (doubledError <= deltaX)
                {
                    error += deltaX;
                    y += stepY;
                }
            }
        }

        private void ApplyDragTile(Point tile)
        {
            if (!IsTileInsideCurrentMap(tile))
                return;

            if (dragMode == DragMode.Add)
                workingTiles.Add(tile);
            else if (dragMode == DragMode.Remove)
                workingTiles.Remove(tile);
        }

        private static bool IsTileInsideCurrentMap(Point tile)
        {
            if (Game1.currentLocation?.Map?.Layers == null || Game1.currentLocation.Map.Layers.Count == 0)
                return false;

            int width = Game1.currentLocation.Map.Layers[0].LayerWidth;
            int height = Game1.currentLocation.Map.Layers[0].LayerHeight;
            return tile.X >= 0 && tile.Y >= 0 && tile.X < width && tile.Y < height;
        }

        /// <summary>Esc: cancel an in-progress drag preview. Does nothing if not dragging (caller handles closing the editor instead).</summary>
        public bool TryCancelDrag()
        {
            if (!isDragging)
                return false;

            workingTiles = new HashSet<Point>(tilesBeforeDrag);
            isDragging = false;
            dragMode = DragMode.None;
            return true;
        }

        public void OnWarped()
        {
            if (IsActive)
            {
                monitor.Log("[Tile Marker] Location changed mid-edit — closing without saving. Re-open the editor on the new location if you meant to mark tiles there.", LogLevel.Info);
                Abort(showWarpMessage: true);
            }
        }

        private static Rectangle GetDragRect(Point a, Point b)
        {
            int minX = System.Math.Min(a.X, b.X);
            int maxX = System.Math.Max(a.X, b.X);
            int minY = System.Math.Min(a.Y, b.Y);
            int maxY = System.Math.Max(a.Y, b.Y);
            return new Rectangle(minX, minY, maxX - minX, maxY - minY);
        }

        public void Draw(SpriteBatch b)
        {
            if (!IsActive || Game1.currentLocation == null)
                return;

            pixel ??= CreateWhitePixel();

            int mapWidth = Game1.currentLocation.Map.Layers[0].LayerWidth;
            int mapHeight = Game1.currentLocation.Map.Layers[0].LayerHeight;
            int viewportX = Game1.viewport.X;
            int viewportY = Game1.viewport.Y;
            int viewportWidth = Game1.viewport.Width;
            int viewportHeight = Game1.viewport.Height;
            int startX = System.Math.Max(0, viewportX / Game1.tileSize);
            int startY = System.Math.Max(0, viewportY / Game1.tileSize);
            int endX = System.Math.Min(mapWidth - 1, (viewportX + viewportWidth) / Game1.tileSize + 1);
            int endY = System.Math.Min(mapHeight - 1, (viewportY + viewportHeight) / Game1.tileSize + 1);

            int overlayOpacityPercent = Math.Clamp(getOverlayOpacityPercent?.Invoke() ?? 45, 0, 100);
            Color gridLineColor = Color.White * 0.15f;
            Color markedColor = Color.DeepSkyBlue * (overlayOpacityPercent / 100f);

            Point hoverTile = currentHoverTile;
            Rectangle? dragRect = isDragging && dragShape == DragShape.Rectangle
                ? GetDragRect(dragStart, hoverTile)
                : (Rectangle?)null;
            Color previewColor = dragMode == DragMode.Remove
                ? Color.Red * (overlayOpacityPercent / 100f)
                : Color.LimeGreen * (overlayOpacityPercent / 100f);

            for (int x = startX; x <= endX; x++)
            {
                for (int y = startY; y <= endY; y++)
                {
                    Vector2 screenPos = Game1.GlobalToLocal(Game1.viewport, new Vector2(x * Game1.tileSize, y * Game1.tileSize));
                    var tileRect = new Rectangle((int)screenPos.X, (int)screenPos.Y, Game1.tileSize, Game1.tileSize);

                    if (workingTiles.Contains(new Point(x, y)))
                        b.Draw(pixel, tileRect, markedColor);

                    if (dragRect.HasValue && x >= dragRect.Value.Left && x <= dragRect.Value.Right
                        && y >= dragRect.Value.Top && y <= dragRect.Value.Bottom)
                        b.Draw(pixel, tileRect, previewColor);
                    else if (isDragging && dragShape == DragShape.Brush && x == hoverTile.X && y == hoverTile.Y)
                        b.Draw(pixel, tileRect, previewColor);

                    DrawTileOutline(b, tileRect, gridLineColor);
                }
            }

            DrawInstructions(b);
        }

        private void DrawInstructions(SpriteBatch b)
        {
            Point hoverTile = currentHoverTile;
            string[] lines =
            {
                helper.Translation.Get("editor.header", new { category = categoryDisplayName }).ToString(),
                helper.Translation.Get("editor.tile", new { x = hoverTile.X, y = hoverTile.Y }).ToString(),
                helper.Translation.Get(
                    getLeftClickBrushEnabled?.Invoke() == true
                        ? "editor.controls.left-brush"
                        : "editor.controls"
                ).ToString()
            };

            const int padding = 12;
            const int lineSpacing = 4;
            float widestLine = lines.Max(line => Game1.smallFont.MeasureString(line).X);
            int lineHeight = (int)Game1.smallFont.MeasureString("Ag").Y;
            int panelWidth = Math.Min(Game1.uiViewport.Width - 32, (int)widestLine + padding * 2);
            int panelHeight = padding * 2 + lineHeight * lines.Length + lineSpacing * (lines.Length - 1);
            var panel = new Rectangle(16, 16, panelWidth, panelHeight);

            b.Draw(pixel, panel, Color.Black * 0.78f);

            var position = new Vector2(panel.X + padding, panel.Y + padding);
            foreach (string line in lines)
            {
                b.DrawString(Game1.smallFont, line, position, Color.White);
                position.Y += lineHeight + lineSpacing;
            }
        }

        private void ShowHudMessage(string translationKey, int messageType)
        {
            if (!Context.IsWorldReady)
                return;

            string text = helper.Translation.Get(translationKey).ToString();
            Game1.addHUDMessage(new HUDMessage(text, messageType));
        }

        private Point GetCursorTile()
        {
            Vector2 tile = helper.Input.GetCursorPosition().Tile;
            return new Point((int)tile.X, (int)tile.Y);
        }

        private static void DrawTileOutline(SpriteBatch b, Rectangle tileRect, Color color)
        {
            b.Draw(pixel, new Rectangle(tileRect.X, tileRect.Y, tileRect.Width, 1), color);
            b.Draw(pixel, new Rectangle(tileRect.X, tileRect.Y, 1, tileRect.Height), color);
        }

        private static Texture2D CreateWhitePixel()
        {
            var tex = new Texture2D(Game1.graphics.GraphicsDevice, 1, 1);
            tex.SetData(new[] { Color.White });
            return tex;
        }
    }
}
