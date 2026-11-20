using HarmonyLib;
using StardewValley;
using StardewValley.Internal;
using StardewValley.Menus;
using System.Reflection;

namespace ShopQuantitySelector;

internal static class ShopPurchaseHelper
{
    internal const int MaximumSelectableQuantity = 999;

    private static readonly MethodInfo? TryPurchaseMethod = AccessTools.Method(
        typeof(ShopMenu),
        "tryToPurchaseItem",
        new[] { typeof(ISalable), typeof(ISalable), typeof(int), typeof(int), typeof(int) }
    );

    private static readonly MethodInfo? SetScrollBarMethod = AccessTools.Method(
        typeof(ShopMenu),
        "setScrollBarToCurrentIndex"
    );

    internal static int GetMaximumQuantity(ShopMenu shop, ISalable item)
    {
        if (!shop.itemPriceAndStock.TryGetValue(item, out ItemStockInformation? stock))
            return 0;

        if (item.IsRecipe)
            return 1;

        int maximum = stock.Stock == int.MaxValue
            ? MaximumSelectableQuantity
            : Math.Clamp(stock.Stock, 0, MaximumSelectableQuantity);

        if (maximum <= 0)
            return 0;

        int unitsPerPurchase = Math.Max(1, item.Stack);
        int maximumStackSize;
        try
        {
            maximumStackSize = Math.Max(1, item.GetSalableInstance().maximumStackSize());
        }
        catch
        {
            maximumStackSize = 1;
        }

        if (shop.heldItem is null)
        {
            maximum = Math.Min(maximum, Math.Max(1, maximumStackSize / unitsPerPurchase));
        }
        else
        {
            if (!shop.heldItem.canStackWith(item))
                return 0;

            int freeStackSpace = Math.Max(0, shop.heldItem.maximumStackSize() - shop.heldItem.Stack);
            maximum = Math.Min(maximum, freeStackSpace / unitsPerPurchase);
        }

        if (stock.Price > 0)
        {
            int currency = ShopMenu.getPlayerCurrencyAmount(Game1.player, shop.currency);
            maximum = Math.Min(maximum, currency / stock.Price);
        }

        if (!string.IsNullOrWhiteSpace(stock.TradeItem))
        {
            int tradePerPurchase = Math.Max(1, stock.TradeItemCount ?? 5);
            maximum = LimitByAvailableTradeItems(shop, stock.TradeItem, tradePerPurchase, maximum);
        }

        return Math.Clamp(maximum, 0, MaximumSelectableQuantity);
    }

    private static int LimitByAvailableTradeItems(ShopMenu shop, string tradeItemId, int tradePerPurchase, int upperBound)
    {
        int low = 0;
        int high = Math.Max(0, upperBound);

        while (low < high)
        {
            int middle = low + (high - low + 1) / 2;
            int needed = middle * tradePerPurchase;

            if (shop.HasTradeItem(tradeItemId, needed))
                low = middle;
            else
                high = middle - 1;
        }

        return low;
    }

    internal static bool Purchase(ShopMenu shop, ISalable item, int originalIndex, int quantity, int mouseX, int mouseY)
    {
        if (TryPurchaseMethod is null)
            throw new MissingMethodException(typeof(ShopMenu).FullName, "tryToPurchaseItem");

        if (!shop.itemPriceAndStock.ContainsKey(item) || !shop.forSale.Contains(item))
            return false;

        int currentMaximum = GetMaximumQuantity(shop, item);
        quantity = Math.Clamp(quantity, 1, Math.Max(1, currentMaximum));

        // The game's private purchase method expects the active menu to be the shop itself.
        Game1.activeClickableMenu = shop;

        bool removeFromShop = (bool?)TryPurchaseMethod.Invoke(
            shop,
            new object?[] { item, shop.heldItem, quantity, mouseX, mouseY }
        ) ?? false;

        if (removeFromShop && shop.itemPriceAndStock.ContainsKey(item))
        {
            shop.itemPriceAndStock.Remove(item);
            shop.forSale.Remove(item);
        }

        // Put the purchased stack straight into the inventory when possible, which is the useful
        // part of the quantity-selection behavior. If the inventory is full, the item remains on the cursor.
        if (shop.heldItem is Item held && Game1.player.addItemToInventoryBool(held))
        {
            shop.heldItem = null;
            DelayedAction.playSoundAfterDelay("coin", 100);
        }

        shop.currentItemIndex = Math.Max(0, Math.Min(shop.currentItemIndex, Math.Max(0, shop.forSale.Count - 4)));
        shop.updateSaleButtonNeighbors();
        SetScrollBarMethod?.Invoke(shop, null);

        return true;
    }
}
