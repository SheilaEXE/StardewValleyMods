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
            List<RecipeSaleData> recipeEntries = new();
            foreach ((string recipeName, string rawData) in recipes)
            {
                if (TryParseRecipeSaleData(recipeName, rawData, out RecipeSaleData recipe))
                    recipeEntries.Add(recipe);
                else
                    this.Monitor.Log($"Skipped cooking recipe '{recipeName}' because its output item couldn't be parsed.", LogLevel.Warn);
            }

            Dictionary<string, List<RecipeSaleData>> recipesByOutput = recipeEntries
                .GroupBy(recipe => NormalizeObjectId(recipe.OutputItemId), StringComparer.OrdinalIgnoreCase)
                .ToDictionary(group => group.Key, group => group.ToList(), StringComparer.OrdinalIgnoreCase);
            HashSet<string> coveredRecipes = new(StringComparer.Ordinal);
            int updated = 0;
            int duplicatesRemoved = 0;

            for (int index = 0; index < saloon.Items.Count; index++)
            {
                ShopItemData item = saloon.Items[index];
                if (!item.IsRecipe || !TryMatchExistingRecipe(item, recipes, recipesByOutput, coveredRecipes, out RecipeSaleData recipe))
                    continue;

                if (!coveredRecipes.Add(recipe.RecipeName))
                {
                    saloon.Items.RemoveAt(index--);
                    duplicatesRemoved++;
                    continue;
                }

                this.NormalizeRecipeEntry(item, recipe);
                updated++;
            }

            int added = 0;
            foreach (RecipeSaleData recipe in recipeEntries.OrderBy(entry => entry.RecipeName, StringComparer.Ordinal))
            {
                if (!coveredRecipes.Add(recipe.RecipeName))
                    continue;

                saloon.Items.Add(new ShopItemData
                {
                    Id = EntryIdPrefix + recipe.RecipeName,
                    ItemId = QualifyObjectId(recipe.OutputItemId),
                    ObjectInternalName = recipe.RecipeName,
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

    private void NormalizeRecipeEntry(ShopItemData item, RecipeSaleData recipe)
    {
        item.ItemId = QualifyObjectId(recipe.OutputItemId);
        item.ObjectInternalName = recipe.RecipeName;
        item.Condition = null;
        item.Price = this.Config.RecipePrice;
        item.ApplyProfitMargins = false;
        item.TradeItemId = null;
        item.PriceModifiers = null;
        item.IgnoreShopPriceModifiers = true;
        item.AvailableStockModifiers = null;
    }

    private static bool TryMatchExistingRecipe(
        ShopItemData item,
        IReadOnlyDictionary<string, string> recipes,
        IReadOnlyDictionary<string, List<RecipeSaleData>> recipesByOutput,
        ISet<string> coveredRecipes,
        out RecipeSaleData recipe
    )
    {
        recipe = null!;

        if (!string.IsNullOrWhiteSpace(item.ObjectInternalName)
            && recipes.TryGetValue(item.ObjectInternalName, out string? namedRawData)
            && TryParseRecipeSaleData(item.ObjectInternalName, namedRawData, out recipe))
        {
            return true;
        }

        if (!string.IsNullOrWhiteSpace(item.ItemId)
            && recipes.TryGetValue(item.ItemId, out string? legacyRawData)
            && TryParseRecipeSaleData(item.ItemId, legacyRawData, out recipe))
        {
            return true;
        }

        if (string.IsNullOrWhiteSpace(item.ItemId)
            || !recipesByOutput.TryGetValue(NormalizeObjectId(item.ItemId), out List<RecipeSaleData>? matches))
        {
            return false;
        }

        recipe = matches.FirstOrDefault(match => !coveredRecipes.Contains(match.RecipeName)) ?? matches[0];
        return true;
    }

    private static bool TryParseRecipeSaleData(string recipeName, string rawData, out RecipeSaleData recipe)
    {
        recipe = null!;
        string[] fields = rawData.Split('/');
        if (fields.Length <= CraftingRecipe.index_output)
            return false;

        string[] outputParts = fields[CraftingRecipe.index_output]
            .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (outputParts.Length == 0 || string.IsNullOrWhiteSpace(outputParts[0]))
            return false;

        recipe = new RecipeSaleData(recipeName, outputParts[0]);
        return true;
    }

    private static string QualifyObjectId(string itemId)
    {
        return itemId.StartsWith('(') ? itemId : $"(O){itemId}";
    }

    private static string NormalizeObjectId(string itemId)
    {
        return itemId.StartsWith("(O)", StringComparison.OrdinalIgnoreCase) ? itemId[3..] : itemId;
    }

    private void OnAssetsInvalidated(object? sender, AssetsInvalidatedEventArgs e)
    {
        if (e.NamesWithoutLocale.Any(name => name.IsEquivalentTo(CookingRecipesAsset)))
            this.Helper.GameContent.InvalidateCache(ShopsAsset);
    }

    private sealed record RecipeSaleData(string RecipeName, string OutputItemId);
}
