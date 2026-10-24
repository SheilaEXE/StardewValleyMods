using StardewModdingAPI;
using StardewModdingAPI.Events;
using StardewValley;
using StardewValley.GameData.Shops;

namespace AllRecipesForSale;

internal sealed class ModEntry : Mod
{
    private const string CookingRecipesAsset = "Data/CookingRecipes";
    private const string ShopsAsset = "Data/Shops";
    private const string SaloonShopId = "Saloon";
    private const string EntryIdPrefix = "NatrollEXE.AllRecipesForSale/";

    private ModConfig Config = null!;

    public override void Entry(IModHelper helper)
    {
        this.Config = helper.ReadConfig<ModConfig>();
        this.Config.RecipePrice = Math.Max(0, this.Config.RecipePrice);

        helper.Events.GameLoop.GameLaunched += this.OnGameLaunched;
        helper.Events.Content.AssetRequested += this.OnAssetRequested;
        helper.Events.Content.AssetsInvalidated += this.OnAssetsInvalidated;
    }

    private void OnGameLaunched(object? sender, GameLaunchedEventArgs e)
    {
        IGenericModConfigMenuApi? configMenu =
            this.Helper.ModRegistry.GetApi<IGenericModConfigMenuApi>("spacechase0.GenericModConfigMenu");
        if (configMenu is null)
            return;

        configMenu.Register(
            this.ModManifest,
            reset: () => this.Config = new ModConfig(),
            save: () =>
            {
                this.Config.RecipePrice = Math.Max(0, this.Config.RecipePrice);
                this.Helper.WriteConfig(this.Config);
                this.Helper.GameContent.InvalidateCache(ShopsAsset);
            }
        );

        configMenu.AddBoolOption(
            this.ModManifest,
            getValue: () => this.Config.Enabled,
            setValue: value => this.Config.Enabled = value,
            name: () => this.Helper.Translation.Get("config.enabled.name"),
            tooltip: () => this.Helper.Translation.Get("config.enabled.tooltip"),
            fieldId: "Enabled"
        );
    }

    private void OnAssetRequested(object? sender, AssetRequestedEventArgs e)
    {
        if (!e.NameWithoutLocale.IsEquivalentTo(ShopsAsset))
            return;

        e.Edit(asset =>
        {
            if (!this.Config.Enabled)
                return;

            IDictionary<string, ShopData> shops = asset.AsDictionary<string, ShopData>().Data;
            if (!shops.TryGetValue(SaloonShopId, out ShopData? saloon))
            {
                this.Monitor.Log($"Couldn't find the '{SaloonShopId}' shop in {ShopsAsset}.", LogLevel.Error);
                return;
            }

            Dictionary<string, string> recipes = Game1.content.Load<Dictionary<string, string>>(CookingRecipesAsset);
            HashSet<string> coveredRecipes = new(StringComparer.Ordinal);
            int updated = 0;
            int duplicatesRemoved = 0;

            for (int index = 0; index < saloon.Items.Count; index++)
            {
                ShopItemData item = saloon.Items[index];
                if (!item.IsRecipe || item.ItemId is null || !recipes.ContainsKey(item.ItemId))
                    continue;

                if (!coveredRecipes.Add(item.ItemId))
                {
                    saloon.Items.RemoveAt(index--);
                    duplicatesRemoved++;
                    continue;
                }

                this.NormalizeRecipeEntry(item);
                updated++;
            }

            int added = 0;
            foreach (string recipeName in recipes.Keys.OrderBy(name => name, StringComparer.Ordinal))
            {
                if (!coveredRecipes.Add(recipeName))
                    continue;

                saloon.Items.Add(new ShopItemData
                {
                    Id = EntryIdPrefix + recipeName,
                    ItemId = recipeName,
                    IsRecipe = true,
                    Price = this.Config.RecipePrice,
                    ApplyProfitMargins = false,
                    IgnoreShopPriceModifiers = true,
                    AvoidRepeat = true
                });
                added++;
            }

            this.Monitor.Log(
                $"Prepared {coveredRecipes.Count} cooking recipes in Gus's shop for {this.Config.RecipePrice}g each "
                + $"({added} added, {updated} existing entries updated, {duplicatesRemoved} duplicates removed).",
                LogLevel.Trace
            );
        }, AssetEditPriority.Late);
    }

    private void NormalizeRecipeEntry(ShopItemData item)
    {
        item.Condition = null;
        item.Price = this.Config.RecipePrice;
        item.ApplyProfitMargins = false;
        item.TradeItemId = null;
        item.PriceModifiers = null;
        item.IgnoreShopPriceModifiers = true;
        item.AvailableStockModifiers = null;
    }

    private void OnAssetsInvalidated(object? sender, AssetsInvalidatedEventArgs e)
    {
        if (e.NamesWithoutLocale.Any(name => name.IsEquivalentTo(CookingRecipesAsset)))
            this.Helper.GameContent.InvalidateCache(ShopsAsset);
    }
}
