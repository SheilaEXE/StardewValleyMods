# All Recipes for Sale!

Adds every cooking recipe from `Data/CookingRecipes` to Gus's Saloon shop. Recipes already known by the current player are hidden by Stardew Valley's native recipe-shop behavior.

This includes recipes added by compatible mods, such as Content Patcher packs that edit `Data/CookingRecipes`.

If Generic Mod Config Menu is installed, the mod can be enabled or disabled through its in-game settings. After changing this option, close and reopen Gus's shop.

## Configuration

After the first launch, edit `config.json`:

```json
{
  "Enabled": true,
  "RecipePrice": 1000
}
```

`RecipePrice` is the gold price for each recipe. Negative values are treated as zero.

`Enabled` controls whether the mod adds recipes to Gus's shop.
