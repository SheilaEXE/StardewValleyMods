using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using StardewValley;
using StardewValley.Menus;
using System;
using System.Collections.Generic;

namespace OutfitReactions
{
    internal sealed class OutfitPlayerReplyTextInputMenu : IClickableMenu
    {
        private readonly string title;
        private readonly string sendLabel;
        private readonly string cancelLabel;
        private readonly Action<string> submit;
        private readonly Action cancel;
        private readonly int textLimit;
        private readonly TextBox textBox;
        private readonly ClickableTextureComponent sendButton;
        private readonly ClickableTextureComponent cancelButton;
        private string hoverText = "";

        // Multi-line input area drawn manually on top of the TextBox (which only renders one
        // line natively). We capture typed characters ourselves and wrap them for display.
        private string inputText = "";
        private double caretBlinkTimer = 0;
        private bool caretVisible = true;

        // Held-backspace repeat: receiveKeyPress only fires once per press, so holding Backspace
        // would otherwise delete a single character. We track the held state here and delete
        // repeatedly after a short initial delay.
        private bool backspaceWasDown = false;
        private double backspaceHeldTimer = 0;
        private double backspaceRepeatTimer = 0;

        public OutfitPlayerReplyTextInputMenu(string title, string sendLabel, string cancelLabel, Action<string> submit, Action cancel, string initialText = "", int textLimit = 800)
            : base((Game1.uiViewport.Width - Math.Min(1200, Game1.uiViewport.Width - 96)) / 2, Math.Max(48, Game1.uiViewport.Height - 520), Math.Min(1200, Game1.uiViewport.Width - 96), 420, true)
        {
            this.title = title ?? "Write your reply:";
            this.sendLabel = sendLabel ?? "Send";
            this.cancelLabel = cancelLabel ?? "Cancel";
            this.submit = submit;
            this.cancel = cancel;

            // Keep a hidden TextBox just to capture keyboard input via the game's dispatcher.
            Texture2D textBoxTexture = Game1.content.Load<Texture2D>("LooseSprites\\textBox");
            textBox = new TextBox(textBoxTexture, null, Game1.smallFont, Game1.textColor)
            {
                X = -9999,
                Y = -9999,
                Width = width - 128,
                Height = 64,
                Selected = true,
                Text = ""
            };
            this.textLimit = Math.Max(1, textLimit);
            textBox.textLimit = this.textLimit;
            inputText = (initialText ?? "").Length <= this.textLimit
                ? initialText ?? ""
                : (initialText ?? "").Substring(0, this.textLimit);

            int buttonY = yPositionOnScreen + height - 88;
            sendButton = new ClickableTextureComponent(
                new Rectangle(xPositionOnScreen + width - 64 - 64, buttonY, 64, 64),
                Game1.mouseCursors,
                new Rectangle(128, 256, 64, 64),
                1f)
            {
                name = "send"
            };
            cancelButton = new ClickableTextureComponent(
                new Rectangle(xPositionOnScreen + 64, buttonY + 8, 48, 48),
                Game1.mouseCursors,
                new Rectangle(337, 494, 12, 12),
                4f)
            {
                name = "cancel"
            };

            Game1.keyboardDispatcher.Subscriber = textBox;
        }

        private int InputAreaX => xPositionOnScreen + 64;
        private int InputAreaY => yPositionOnScreen + 110;
        private int InputAreaWidth => width - 128;
        private int InputAreaHeight => height - 110 - 100;

        public override void receiveLeftClick(int x, int y, bool playSound = true)
        {
            textBox.Selected = true;
            Game1.keyboardDispatcher.Subscriber = textBox;

            if (sendButton.containsPoint(x, y))
            {
                Game1.playSound("smallSelect");
                submit?.Invoke(inputText);
                return;
            }

            if (cancelButton.containsPoint(x, y))
            {
                Game1.playSound("smallSelect");
                DoCancel();
            }
        }

        // Closes this input menu before running the cancel callback, so the window actually
        // dismisses no matter what the callback does next (reopen the choice menu, finish, etc.).
        private void DoCancel()
        {
            Game1.keyboardDispatcher.Subscriber = null;
            if (Game1.activeClickableMenu == this)
                Game1.activeClickableMenu = null;
            cancel?.Invoke();
        }

        protected override void cleanupBeforeExit()
        {
            if (Game1.keyboardDispatcher.Subscriber == textBox)
                Game1.keyboardDispatcher.Subscriber = null;
            base.cleanupBeforeExit();
        }

        public override void receiveKeyPress(Keys key)
        {
            if (key == Keys.Escape)
            {
                Game1.playSound("smallSelect");
                DoCancel();
                return;
            }

            if (key == Keys.Enter)
            {
                // Shift+Enter adds a newline; plain Enter submits.
                if (Game1.oldKBState.IsKeyDown(Keys.LeftShift) || Game1.oldKBState.IsKeyDown(Keys.RightShift))
                {
                    if (inputText.Length < textLimit)
                        inputText += "\n";
                }
                else
                {
                    Game1.playSound("smallSelect");
                    submit?.Invoke(inputText);
                }
                return;
            }

            if (key == Keys.Back && inputText.Length > 0)
            {
                inputText = inputText[..^1];
                return;
            }
        }

        public override void receiveGamePadButton(Buttons b)
        {
            if (b == Buttons.B || b == Buttons.Back)
            {
                Game1.playSound("smallSelect");
                DoCancel();
            }
        }

        public override void performHoverAction(int x, int y)
        {
            base.performHoverAction(x, y);
            sendButton.tryHover(x, y, 0.2f);
            cancelButton.tryHover(x, y, 0.2f);
            hoverText = sendButton.containsPoint(x, y)
                ? sendLabel
                : cancelButton.containsPoint(x, y)
                    ? cancelLabel
                    : "";
        }

        public override void update(GameTime time)
        {
            base.update(time);

            // Sync text typed via the hidden TextBox into our inputText.
            string boxText = textBox.Text ?? "";
            if (boxText.Length > 0)
            {
                int available = Math.Max(0, textLimit - inputText.Length);
                if (available > 0)
                    inputText += boxText.Length <= available ? boxText : boxText.Substring(0, available);
                textBox.Text = "";
            }

            // Held-backspace repeat. The first delete happens in receiveKeyPress; if the key stays
            // held, start deleting repeatedly after a short initial delay.
            double elapsed = time.ElapsedGameTime.TotalSeconds;
            bool backDown = Keyboard.GetState().IsKeyDown(Keys.Back);
            if (backDown && backspaceWasDown)
            {
                backspaceHeldTimer += elapsed;
                if (backspaceHeldTimer >= 0.4)
                {
                    backspaceRepeatTimer += elapsed;
                    if (backspaceRepeatTimer >= 0.03)
                    {
                        backspaceRepeatTimer = 0;
                        if (inputText.Length > 0)
                            inputText = inputText[..^1];
                    }
                }
            }
            else
            {
                backspaceHeldTimer = 0;
                backspaceRepeatTimer = 0;
            }
            backspaceWasDown = backDown;

            // Caret blink.
            caretBlinkTimer += elapsed;
            if (caretBlinkTimer >= 0.5)
            {
                caretBlinkTimer = 0;
                caretVisible = !caretVisible;
            }
        }

        public override void draw(SpriteBatch b)
        {
            // Dim the background behind the menu.
            b.Draw(Game1.fadeToBlackRect, Game1.graphics.GraphicsDevice.Viewport.Bounds, Color.Black * 0.4f);

            // Background panel — drawShadow must be false to avoid SpriteBatch Begin/End conflict.
            IClickableMenu.drawTextureBox(b, Game1.menuTexture, new Rectangle(0, 256, 60, 60), xPositionOnScreen, yPositionOnScreen, width, height, Color.White, 1f, false);

            // Title.
            Utility.drawTextWithShadow(b, title, Game1.dialogueFont, new Vector2(xPositionOnScreen + 64, yPositionOnScreen + 48), Game1.textColor);

            // Input area background.
            int ax = InputAreaX, ay = InputAreaY, aw = InputAreaWidth, ah = InputAreaHeight;
            b.Draw(Game1.staminaRect, new Rectangle(ax, ay, aw, ah), new Color(240, 200, 200));
            IClickableMenu.drawTextureBox(b, Game1.menuTexture, new Rectangle(0, 256, 60, 60), ax, ay, aw, ah, Color.White * 0.6f, 1f, false);

            // Draw wrapped input text with caret.
            SpriteFont font = Game1.smallFont;
            float lineHeight = font.MeasureString("A").Y + 2;
            int padding = 12;
            int textAreaWidth = aw - padding * 2;

            string displayText = inputText + (caretVisible ? "|" : " ");
            List<string> lines = WrapText(font, displayText, textAreaWidth);

            float textY = ay + padding;
            int maxLines = (int)((ah - padding * 2) / lineHeight);
            int startLine = Math.Max(0, lines.Count - maxLines);
            for (int i = startLine; i < lines.Count; i++)
            {
                b.DrawString(font, lines[i], new Vector2(ax + padding, textY), Game1.textColor);
                textY += lineHeight;
            }

            cancelButton.draw(b);
            sendButton.draw(b);
            if (!string.IsNullOrWhiteSpace(hoverText))
                drawHoverText(b, hoverText, Game1.smallFont);

            drawMouse(b);
        }

        private static List<string> WrapText(SpriteFont font, string text, int maxWidth)
        {
            List<string> lines = new();
            foreach (string paragraph in text.Split('\n'))
            {
                string[] words = paragraph.Split(' ');
                string current = "";
                foreach (string word in words)
                {
                    string test = string.IsNullOrEmpty(current) ? word : current + " " + word;
                    if (font.MeasureString(test).X > maxWidth && !string.IsNullOrEmpty(current))
                    {
                        lines.Add(current);
                        current = word;
                    }
                    else current = test;
                }
                lines.Add(current);
            }
            return lines;
        }

    }
}
