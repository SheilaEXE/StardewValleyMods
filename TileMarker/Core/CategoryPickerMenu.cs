using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using StardewValley;
using StardewValley.Menus;

namespace TileMarker.Core
{
    /// <summary>Bare-bones list menu shown when more than one mod has registered a category to mark.</summary>
    internal class CategoryPickerMenu : IClickableMenu
    {
        private const int MaximumVisibleRows = 6;
        private const int RowHeight = 68;
        private const int HeaderHeight = 152;
        private const int FooterHeight = 36;
        private const int HorizontalPadding = 32;

        private readonly List<(string OwnerModId, string Category, string DisplayName)> options;
        private readonly Action<string, string> onPicked;
        private readonly string title;
        private readonly List<ClickableComponent> rows = new();
        private readonly int visibleRowCount;
        private int scrollOffset;

        public CategoryPickerMenu(List<(string OwnerModId, string Category, string DisplayName)> options, Action<string, string> onPicked, string title)
            : this(options, onPicked, title, CalculateLayout(options?.Count ?? 0))
        {
        }

        private CategoryPickerMenu(
            List<(string OwnerModId, string Category, string DisplayName)> options,
            Action<string, string> onPicked,
            string title,
            PickerLayout layout)
            : base(layout.Bounds.X, layout.Bounds.Y, layout.Bounds.Width, layout.Bounds.Height, showUpperRightCloseButton: true)
        {
            this.options = options ?? new List<(string OwnerModId, string Category, string DisplayName)>();
            this.onPicked = onPicked;
            this.title = title;
            visibleRowCount = layout.VisibleRows;

            RebuildRows();
        }

        private static PickerLayout CalculateLayout(int optionCount)
        {
            int viewportWidth = Math.Max(320, Game1.uiViewport.Width);
            int viewportHeight = Math.Max(240, Game1.uiViewport.Height);
            int menuWidth = Math.Min(760, Math.Max(300, viewportWidth - 48));
            int maximumHeight = Math.Max(HeaderHeight + FooterHeight + RowHeight, viewportHeight - 48);
            int rowsThatFit = Math.Max(1, (maximumHeight - HeaderHeight - FooterHeight) / RowHeight);
            int visibleRows = Math.Max(1, Math.Min(Math.Min(MaximumVisibleRows, optionCount), rowsThatFit));
            int menuHeight = Math.Min(maximumHeight, HeaderHeight + FooterHeight + visibleRows * RowHeight);

            return new PickerLayout(
                new Rectangle(
                    (viewportWidth - menuWidth) / 2,
                    (viewportHeight - menuHeight) / 2,
                    menuWidth,
                    menuHeight
                ),
                visibleRows
            );
        }

        private void RebuildRows()
        {
            rows.Clear();
            int end = Math.Min(options.Count, scrollOffset + visibleRowCount);
            int displayedRows = Math.Max(0, end - scrollOffset);
            int rowBlockHeight = displayedRows * RowHeight;
            int availableTop = yPositionOnScreen + HeaderHeight;
            int availableBottom = yPositionOnScreen + height - FooterHeight;
            int availableHeight = Math.Max(rowBlockHeight, availableBottom - availableTop);
            int y = availableTop + Math.Max(0, (availableHeight - rowBlockHeight) / 2);

            for (int optionIndex = scrollOffset; optionIndex < end; optionIndex++)
            {
                rows.Add(new ClickableComponent(
                    new Rectangle(
                        xPositionOnScreen + HorizontalPadding,
                        y,
                        width - HorizontalPadding * 2,
                        RowHeight - 8
                    ),
                    optionIndex.ToString()
                ));
                y += RowHeight;
            }
        }

        public override void receiveLeftClick(int x, int y, bool playSound = true)
        {
            base.receiveLeftClick(x, y, playSound);

            foreach (ClickableComponent row in rows)
            {
                if (row.bounds.Contains(x, y)
                    && int.TryParse(row.name, out int optionIndex)
                    && optionIndex >= 0
                    && optionIndex < options.Count)
                {
                    var picked = options[optionIndex];
                    Game1.playSound("smallSelect");
                    Game1.activeClickableMenu = null;
                    onPicked?.Invoke(picked.OwnerModId, picked.Category);
                    return;
                }
            }
        }

        public override void receiveScrollWheelAction(int direction)
        {
            int oldOffset = scrollOffset;
            int maxOffset = Math.Max(0, options.Count - visibleRowCount);
            scrollOffset = direction > 0
                ? Math.Max(0, scrollOffset - 1)
                : Math.Min(maxOffset, scrollOffset + 1);

            if (scrollOffset != oldOffset)
            {
                RebuildRows();
                Game1.playSound("shiny4");
            }
        }

        public override void draw(SpriteBatch b)
        {
            b.Draw(Game1.fadeToBlackRect, Game1.graphics.GraphicsDevice.Viewport.Bounds, Color.Black * 0.4f);
            Game1.drawDialogueBox(xPositionOnScreen, yPositionOnScreen, width, height, false, true);

            string safeTitle = string.IsNullOrWhiteSpace(title) ? "Tile Marker" : title;
            Vector2 titleSize = Game1.dialogueFont.MeasureString(safeTitle);
            float titleScale = titleSize.X > width - HorizontalPadding * 2
                ? (width - HorizontalPadding * 2) / titleSize.X
                : 1f;
            b.DrawString(
                Game1.dialogueFont,
                safeTitle,
                new Vector2(xPositionOnScreen + (width - titleSize.X * titleScale) / 2f, yPositionOnScreen + 100),
                Game1.textColor,
                0f,
                Vector2.Zero,
                titleScale,
                SpriteEffects.None,
                0.9f
            );

            foreach (ClickableComponent row in rows)
            {
                if (!int.TryParse(row.name, out int optionIndex)
                    || optionIndex < 0
                    || optionIndex >= options.Count)
                    continue;

                Rectangle r = row.bounds;
                bool hovered = r.Contains(Game1.getMouseX(), Game1.getMouseY());
                if (hovered)
                    b.Draw(Game1.fadeToBlackRect, r, Color.White * 0.15f);

                string displayName = string.IsNullOrWhiteSpace(options[optionIndex].DisplayName)
                    ? options[optionIndex].Category
                    : options[optionIndex].DisplayName;
                string wrappedName = Game1.parseText(displayName, Game1.smallFont, r.Width - 24);
                Vector2 textSize = Game1.smallFont.MeasureString(wrappedName);
                b.DrawString(
                    Game1.smallFont,
                    wrappedName,
                    new Vector2(r.X + 12, r.Y + Math.Max(4f, (r.Height - textSize.Y) / 2f)),
                    Game1.textColor
                );
            }

            if (options.Count > visibleRowCount)
            {
                int first = scrollOffset + 1;
                int last = Math.Min(options.Count, scrollOffset + visibleRowCount);
                string pageText = $"{first}-{last} / {options.Count}";
                Vector2 size = Game1.smallFont.MeasureString(pageText);
                b.DrawString(
                    Game1.smallFont,
                    pageText,
                    new Vector2(xPositionOnScreen + width - 32 - size.X, yPositionOnScreen + height - 36),
                    Color.Gray
                );
            }

            upperRightCloseButton?.draw(b);
            drawMouse(b);
        }

        private readonly struct PickerLayout
        {
            public PickerLayout(Rectangle bounds, int visibleRows)
            {
                Bounds = bounds;
                VisibleRows = visibleRows;
            }

            public Rectangle Bounds { get; }
            public int VisibleRows { get; }
        }
    }
}
