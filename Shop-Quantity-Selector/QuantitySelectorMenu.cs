using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using StardewValley;
using StardewValley.Internal;
using StardewValley.Menus;

namespace ShopQuantitySelector;

internal sealed class QuantitySelectorMenu : IClickableMenu
{
    private readonly ShopMenu shop;
    private readonly ISalable item;
    private readonly int originalIndex;
    private readonly int originalMouseX;
    private readonly int originalMouseY;
    private readonly int maximumQuantity;

    private readonly ClickableComponent minusButton;
    private readonly ClickableComponent plusButton;
    private readonly ClickableComponent cancelButton;
    private readonly ClickableComponent buyButton;

    // Slider nativo do jogo (o mesmo usado nos volumes de música/som nas Opções).
    private readonly OptionsSlider nativeSlider;
    private bool draggingSlider;
    private int quantity = 1;

    private int ContentLeft => xPositionOnScreen + 48;
    private int ContentRight => xPositionOnScreen + width - 48;

    internal QuantitySelectorMenu(
        ShopMenu shop,
        ISalable item,
        int originalIndex,
        int mouseX,
        int mouseY,
        int maximumQuantity
    )
        : base(0, 0, 0, 0, false)
    {
        this.shop = shop;
        this.item = item;
        this.originalIndex = originalIndex;
        originalMouseX = mouseX;
        originalMouseY = mouseY;
        this.maximumQuantity = Math.Max(1, maximumQuantity);

        RepositionElements();

        minusButton = new ClickableComponent(Rectangle.Empty, "minus");
        plusButton = new ClickableComponent(Rectangle.Empty, "plus");
        cancelButton = new ClickableComponent(Rectangle.Empty, "cancel");
        buyButton = new ClickableComponent(Rectangle.Empty, "buy");

        // "-1" desativa o rótulo embutido do OptionsSlider; nosso texto "Quantidade: X" já é
        // desenhado separadamente acima da barra.
        nativeSlider = new OptionsSlider("", -1);
        RepositionElements();
        RepositionButtons();
    }

    private void RepositionElements()
    {
        width = Math.Min(620, Math.Max(420, Game1.uiViewport.Width - 96));
        height = 410;
        xPositionOnScreen = (Game1.uiViewport.Width - width) / 2;
        yPositionOnScreen = Math.Max(32, (Game1.uiViewport.Height - height) / 2);

        if (nativeSlider != null)
            nativeSlider.bounds = new Rectangle(xPositionOnScreen + (width - 280) / 2, yPositionOnScreen + 228, 280, 24);
    }

    private void RepositionButtons()
    {
        int sliderButtonY = nativeSlider.bounds.Center.Y - 16;
        minusButton.bounds = new Rectangle(nativeSlider.bounds.Left - 48, sliderButtonY, 28, 32);
        plusButton.bounds = new Rectangle(nativeSlider.bounds.Right + 20, sliderButtonY, 28, 32);

        int actionWidth = 220;
        int actionHeight = 64;
        int actionY = yPositionOnScreen + height - 88;
        cancelButton.bounds = new Rectangle(ContentLeft, actionY, actionWidth, actionHeight);
        buyButton.bounds = new Rectangle(ContentRight - actionWidth, actionY, actionWidth, actionHeight);
    }

    public override void gameWindowSizeChanged(Rectangle oldBounds, Rectangle newBounds)
    {
        base.gameWindowSizeChanged(oldBounds, newBounds);
        RepositionElements();
        RepositionButtons();
    }

    public override void receiveLeftClick(int x, int y, bool playSound = true)
    {
        if (minusButton.containsPoint(x, y))
        {
            SetQuantity(quantity - 1, playSound: true);
            return;
        }

        if (plusButton.containsPoint(x, y))
        {
            SetQuantity(quantity + 1, playSound: true);
            return;
        }

        if (nativeSlider.bounds.Contains(x, y))
        {
            draggingSlider = true;
            nativeSlider.receiveLeftClick(x, y);
            SetQuantity(SliderValueToQuantity(nativeSlider.value), playSound: false);
            Game1.playSound("smallSelect");
            return;
        }

        if (cancelButton.containsPoint(x, y))
        {
            Cancel();
            return;
        }

        if (buyButton.containsPoint(x, y))
        {
            ConfirmPurchase();
            return;
        }
    }

    public override void leftClickHeld(int x, int y)
    {
        base.leftClickHeld(x, y);
        if (draggingSlider)
        {
            nativeSlider.leftClickHeld(x, y);
            SetQuantity(SliderValueToQuantity(nativeSlider.value), playSound: false);
        }
    }

    public override void releaseLeftClick(int x, int y)
    {
        base.releaseLeftClick(x, y);
        draggingSlider = false;
    }

    public override void receiveScrollWheelAction(int direction)
    {
        base.receiveScrollWheelAction(direction);
        SetQuantity(quantity + Math.Sign(direction), playSound: false);
    }

    public override void receiveKeyPress(Keys key)
    {
        switch (key)
        {
            case Keys.Escape:
                Cancel();
                return;
            case Keys.Enter:
            case Keys.Space:
                ConfirmPurchase();
                return;
            case Keys.Left:
            case Keys.Down:
                SetQuantity(quantity - 1, playSound: true);
                return;
            case Keys.Right:
            case Keys.Up:
                SetQuantity(quantity + 1, playSound: true);
                return;
            case Keys.PageDown:
                SetQuantity(quantity - 10, playSound: true);
                return;
            case Keys.PageUp:
                SetQuantity(quantity + 10, playSound: true);
                return;
            case Keys.Home:
                SetQuantity(1, playSound: true);
                return;
            case Keys.End:
                SetQuantity(maximumQuantity, playSound: true);
                return;
        }

        base.receiveKeyPress(key);
    }

    private void SetQuantity(int value, bool playSound)
    {
        int previous = quantity;
        quantity = Math.Clamp(value, 1, maximumQuantity);
        nativeSlider.value = QuantityToSliderValue(quantity);

        if (playSound && previous != quantity)
            Game1.playSound("shiny4");
    }

    // O OptionsSlider nativo trabalha com uma porcentagem de 0 a 100; aqui convertemos
    // esse valor pra nossa faixa real de quantidade (1 até maximumQuantity) e vice-versa.
    private int SliderValueToQuantity(int sliderValue)
    {
        float percentage = Math.Clamp(sliderValue / 100f, 0f, 1f);
        return 1 + (int)MathF.Round(percentage * (maximumQuantity - 1));
    }

    private int QuantityToSliderValue(int quantityValue)
    {
        if (maximumQuantity <= 1)
            return 0;

        float percentage = (quantityValue - 1) / (float)(maximumQuantity - 1);
        return (int)MathF.Round(Math.Clamp(percentage, 0f, 1f) * 100f);
    }

    private void Cancel()
    {
        Game1.playSound("smallSelect");
        Game1.activeClickableMenu = shop;
    }

    private void ConfirmPurchase()
    {
        try
        {
            ShopPurchaseHelper.Purchase(shop, item, originalIndex, quantity, originalMouseX, originalMouseY);
        }
        catch (Exception ex)
        {
            ModEntry.Instance.Monitor.Log($"The selected shop purchase failed.\n{ex}", StardewModdingAPI.LogLevel.Error);
            Game1.playSound("cancel");
            Game1.activeClickableMenu = shop;
        }
    }

    public override void draw(SpriteBatch b)
    {
        // Keep the shop visible underneath, like the quantity panel does.
        shop.draw(b);
        b.Draw(Game1.fadeToBlackRect, Game1.graphics.GraphicsDevice.Viewport.Bounds, Color.Black * 0.42f);

        IClickableMenu.drawTextureBox(
            b,
            Game1.menuTexture,
            new Rectangle(0, 256, 60, 60),
            xPositionOnScreen,
            yPositionOnScreen,
            width,
            height,
            Color.White,
            1f,
            false
        );

        DrawItemHeader(b);
        DrawQuantityArea(b);
        DrawCostSummary(b);
        DrawButton(b, cancelButton.bounds, ModEntry.I18n.Get("menu.cancel"));
        DrawButton(b, buyButton.bounds, ModEntry.I18n.Get("menu.buy", new { quantity }));

        drawMouse(b);
    }

    private void DrawItemHeader(SpriteBatch b)
    {
        Vector2 iconPosition = new(ContentLeft, yPositionOnScreen + 44);
        item.drawInMenu(
            b,
            iconPosition,
            1f,
            1f,
            0.9f,
            StackDrawType.HideButShowQuality,
            Color.White,
            drawShadow: true
        );

        string title = item.DisplayName;
        Vector2 titlePosition = new(ContentLeft + 82, yPositionOnScreen + 58);
        Utility.drawTextWithShadow(b, title, Game1.dialogueFont, titlePosition, Game1.textColor);
    }

    private void DrawQuantityArea(SpriteBatch b)
    {
        string quantityLabel = ModEntry.I18n.Get("menu.quantity", new { quantity });
        Vector2 quantitySize = Game1.dialogueFont.MeasureString(quantityLabel);
        Utility.drawTextWithShadow(
            b,
            quantityLabel,
            Game1.dialogueFont,
            new Vector2(xPositionOnScreen + (width - quantitySize.X) / 2f, yPositionOnScreen + 144),
            Game1.textColor
        );

        DrawQuantityButton(b, minusButton.bounds, OptionsPlusMinus.minusButtonSource, quantity > 1);
        DrawQuantityButton(b, plusButton.bounds, OptionsPlusMinus.plusButtonSource, quantity < maximumQuantity);

        // Desenha a barrinha com os sprites originais do jogo (mesmos das Opções de volume).
        nativeSlider.draw(b, 0, 0);
    }

    private void DrawCostSummary(SpriteBatch b)
    {
        if (!shop.itemPriceAndStock.TryGetValue(item, out ItemStockInformation? stock))
            return;

        List<string> lines = new();

        if (stock.Price > 0)
        {
            long totalPrice = (long)stock.Price * quantity;
            lines.Add(ModEntry.I18n.Get(
                "menu.total-price",
                new
                {
                    amount = totalPrice,
                    currency = GetCurrencyName(shop.currency)
                }
            ));
        }

        if (!string.IsNullOrWhiteSpace(stock.TradeItem))
        {
            int tradePerPurchase = Math.Max(1, stock.TradeItemCount ?? 5);
            int totalTrade = tradePerPurchase * quantity;
            string tradeName = ItemRegistry.GetDataOrErrorItem(stock.TradeItem).DisplayName;
            lines.Add(ModEntry.I18n.Get("menu.total-trade", new { amount = totalTrade, item = tradeName }));
        }

        if (lines.Count == 0)
            lines.Add(ModEntry.I18n.Get("menu.free"));

        float y = yPositionOnScreen + 264;
        foreach (string line in lines)
        {
            Vector2 size = Game1.smallFont.MeasureString(line);
            Utility.drawTextWithShadow(
                b,
                line,
                Game1.smallFont,
                new Vector2(xPositionOnScreen + (width - size.X) / 2f, y),
                Game1.textColor
            );
            y += 30;
        }
    }

    private static string GetCurrencyName(int currency)
    {
        string key = currency switch
        {
            0 => "currency.gold",
            1 => "currency.star-tokens",
            2 => "currency.qi-coins",
            4 => "currency.qi-gems",
            _ => "currency.unknown"
        };

        return ModEntry.I18n.Get(key);
    }

    private static void DrawQuantityButton(SpriteBatch b, Rectangle bounds, Rectangle source, bool enabled)
    {
        b.Draw(Game1.mouseCursors, new Vector2(bounds.X, bounds.Y), source,
            Color.White * (enabled ? 1f : 0.5f), 0f, Vector2.Zero, 4f, SpriteEffects.None, 0.4f);
    }

    private static void DrawButton(SpriteBatch b, Rectangle bounds, string label)
    {
        IClickableMenu.drawTextureBox(
            b,
            Game1.menuTexture,
            new Rectangle(0, 256, 60, 60),
            bounds.X,
            bounds.Y,
            bounds.Width,
            bounds.Height,
            Color.White,
            1f,
            true
        );

        Vector2 size = Game1.smallFont.MeasureString(label);
        Utility.drawTextWithShadow(
            b,
            label,
            Game1.smallFont,
            new Vector2(bounds.Center.X - size.X / 2f, bounds.Center.Y - size.Y / 2f + 2f),
            Game1.textColor
        );
    }
}
