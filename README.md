# All Recipes for Sale!

Adds every cooking recipe from `Data/CookingRecipes` to Gus's Saloon shop. Recipes already known by the current player are hidden by Stardew Valley's native recipe-shop behavior.

This includes recipes added by compatible mods, such as Content Patcher packs that edit `Data/CookingRecipes`.

## Configuration

After the first launch, edit `config.json`:

```json
{
  "RecipePrice": 1000
}
```

`RecipePrice` is the gold price for each recipe. Negative values are treated as zero.
