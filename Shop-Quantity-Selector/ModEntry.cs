using HarmonyLib;
using StardewModdingAPI;
using StardewValley;
using StardewValley.Menus;

namespace ShopQuantitySelector;

internal sealed class ModEntry : Mod
{
    internal static ModEntry Instance { get; private set; } = null!;
    internal static ITranslationHelper I18n => Instance.Helper.Translation;

    public override void Entry(IModHelper helper)
    {
        Instance = this;

        Harmony harmony = new(ModManifest.UniqueID);
        harmony.PatchAll();
    }

    internal bool TryOpenQuantitySelector(ShopMenu shop, int mouseX, int mouseY)
    {
        if (Game1.activeClickableMenu != shop || shop.readOnly || shop.safetyTimer > 0)
            return false;

        for (int row = 0; row < shop.forSaleButtons.Count; row++)
        {
            int index = shop.currentItemIndex + row;
            if (index < 0 || index >= shop.forSale.Count)
                continue;

            if (!shop.forSaleButtons[row].containsPoint(mouseX, mouseY))
                continue;

            ISalable item = shop.forSale[index];
            if (item is null || !shop.itemPriceAndStock.ContainsKey(item))
                return false;

            if (shop.canPurchaseCheck is not null && !shop.canPurchaseCheck(index))
                return false;

            int maximum = ShopPurchaseHelper.GetMaximumQuantity(shop, item);

            // Do not add an extra confirmation click when only one can be bought.
            if (maximum <= 1)
                return false;

            Game1.playSound("smallSelect");
            Game1.activeClickableMenu = new QuantitySelectorMenu(shop, item, index, mouseX, mouseY, maximum);
            return true;
        }

        return false;
    }
}
