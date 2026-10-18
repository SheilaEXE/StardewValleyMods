using Microsoft.Xna.Framework;
using StardewModdingAPI;
using StardewValley;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace OutfitReactions.Ai
{
    internal sealed class FashionSenseVisualService
    {
        private readonly IMonitor monitor;
        private readonly Func<IFashionSenseApi> getApi;

        public FashionSenseVisualService(IMonitor monitor, Func<IFashionSenseApi> getApi)
        {
            this.monitor = monitor;
            this.getApi = getApi;
        }

        public bool TryBuildVisualSummary(Farmer farmer, string currentOutfitId, out string summary, out string reason, bool suppressHairAndGenericHeadwearForSavedOutfit = false, bool visibleVanillaHatEquipped = true, bool includeInternalIdHints = true)
        {
            summary = "";
            reason = "unknown reason";

            IFashionSenseApi api = getApi?.Invoke();
            if (api == null)
            {
                reason = "Fashion Sense API is unavailable";
                return false;
            }

            if (farmer == null)
            {
                reason = "farmer is null";
                return false;
            }

            try
            {
                List<string> pieces = new();

                // VANILLA HAT EXCLUSIVE MODE: if the farmer has a real in-game vanilla hat equipped
                // (Game1.player.hat), the reaction is about THAT hat and nothing else. We deliberately
                // skip the saved outfit, hair, accessories, shirt, and pants so the NPC focuses purely
                // on the hat (especially important for strange/ugly special hats, where mixing in the
                // rest of the outfit softens or dilutes the reaction). When there is no vanilla hat,
                // we fall through to the normal full Fashion Sense appearance summary below.
                string vanillaHat = visibleVanillaHatEquipped ? TryGetVanillaHatName(farmer) : null;
                if (!string.IsNullOrWhiteSpace(vanillaHat))
                {
                    summary = "The farmer is wearing a real in-game hat: " + vanillaHat
                        + ". React specifically to this hat. Do not invent or describe other clothing; "
                        + "the hat is the whole point of this reaction.";
                    reason = "";
                    return true;
                }

                string outfitId = StringUtils.FirstNonEmpty(currentOutfitId, TryGetCurrentOutfitId(api));
                bool hasSavedOutfit = !string.IsNullOrWhiteSpace(outfitId);
                if (hasSavedOutfit)
                {
                    pieces.Add(includeInternalIdHints
                        ? "saved outfit clue: " + HumanizeFashionSenseId(outfitId)
                        : "a saved outfit is equipped; identify its visual theme only from the attached image");
                }

                AddAppearanceClue(api, farmer, IFashionSenseApi.Type.Hair, "hair", pieces, suppressHairAndGenericHeadwearForSavedOutfit, includeInternalIdHints);

                int piecesBeforeHat = pieces.Count;
                AddAppearanceClue(api, farmer, IFashionSenseApi.Type.Hat, "hat/headwear", pieces, suppressHairAndGenericHeadwearForSavedOutfit, includeInternalIdHints);
                bool addedHeadwear = pieces.Count > piecesBeforeHat;

                // If the farmer has a themed saved outfit (e.g. "Rabbit Outfit") but NO head piece
                // is actually equipped — common when they removed the themed ears/hat to wear a
                // vanilla hat, then took that off — make the absence explicit. Otherwise the AI fills
                // in head pieces the theme name implies (bunny ears, horns, etc.) that aren't worn.
                if (hasSavedOutfit && !addedHeadwear)
                    pieces.Add("head/headwear: NONE equipped (no hat, ears, horns, antennae, or themed head piece is being worn right now, even if the outfit name suggests one)");

                AddAppearanceClue(api, farmer, IFashionSenseApi.Type.Accessory, "visible accessory/extra visual item (may be wings, cape, umbrella, backpack, bow, earrings, or hair accessory; makeup is ignored)", pieces, suppressHairAndGenericHeadwearForSavedOutfit, includeInternalIdHints);
                AddAppearanceClue(api, farmer, IFashionSenseApi.Type.AccessorySecondary, "secondary visible accessory/extra visual item", pieces, suppressHairAndGenericHeadwearForSavedOutfit, includeInternalIdHints);
                AddAppearanceClue(api, farmer, IFashionSenseApi.Type.AccessoryTertiary, "tertiary visible accessory/extra visual item", pieces, suppressHairAndGenericHeadwearForSavedOutfit, includeInternalIdHints);
                AddAppearanceClue(api, farmer, IFashionSenseApi.Type.Shirt, "shirt/top", pieces, suppressHairAndGenericHeadwearForSavedOutfit, includeInternalIdHints);
                AddAppearanceClue(api, farmer, IFashionSenseApi.Type.Sleeves, "sleeves", pieces, suppressHairAndGenericHeadwearForSavedOutfit, includeInternalIdHints);
                AddAppearanceClue(api, farmer, IFashionSenseApi.Type.Pants, "pants/bottom", pieces, suppressHairAndGenericHeadwearForSavedOutfit, includeInternalIdHints);
                // Shoes are intentionally NOT included: the mod never comments on footwear.

                if (pieces.Count <= 0)
                {
                    reason = "no equipped Fashion Sense appearance data was found";
                    return false;
                }

                StringBuilder builder = new();
                builder.Append("Fashion Sense equipped appearance clues from the game API. Use only as support for outfit analysis; never mention Fashion Sense, API, IDs, filenames, or these labels in dialogue: ");
                builder.Append(string.Join("; ", pieces));
                summary = builder.ToString();
                reason = "ok";
                return true;
            }
            catch (Exception ex)
            {
                reason = ex.Message;
                if (OutfitReactions.ModEntry.DebugLog) monitor?.Log("[FS VISUAL] Could not build Fashion Sense visual summary: " + ex.Message, LogLevel.Info);
                return false;
            }
        }

        /// <summary>
        /// Returns the display name of the VANILLA hat the farmer currently has equipped
        /// (Game1.player.hat), or null if none. This is the base-game hat slot, independent of
        /// Fashion Sense. The name comes straight from the game, so modded hats that use the
        /// vanilla hat slot are covered too.
        /// </summary>
        private static string TryGetVanillaHatName(Farmer farmer)
        {
            try
            {
                StardewValley.Objects.Hat hat = farmer?.hat?.Value;
                if (hat == null)
                    return null;
                string name = hat.DisplayName;
                if (string.IsNullOrWhiteSpace(name))
                    name = hat.Name;
                return string.IsNullOrWhiteSpace(name) ? null : name.Trim();
            }
            catch
            {
                return null;
            }
        }

        private static string TryGetCurrentOutfitId(IFashionSenseApi api)
        {
            try
            {
                KeyValuePair<bool, string> response = api.GetCurrentOutfitId();
                if (response.Key && !string.IsNullOrWhiteSpace(response.Value))
                    return response.Value.Trim();
            }
            catch
            {
                // Some Fashion Sense versions may not have a selected saved outfit.
            }

            return "";
        }

        private static void AddAppearanceClue(IFashionSenseApi api, Farmer farmer, IFashionSenseApi.Type type, string label, List<string> pieces, bool suppressHairAndGenericHeadwearForSavedOutfit = false, bool includeInternalIdHints = true)
        {
            string id = TryGetAppearanceId(api, farmer, type);
            if (string.IsNullOrWhiteSpace(id))
                return;

            if (IsAccessoryType(type) && IsIgnoredMakeupAccessoryId(id))
                return;

            string name = HumanizeFashionSenseId(id);
            string color = TryGetAppearanceColorDescription(api, farmer, type);
            bool isHair = type == IFashionSenseApi.Type.Hair;
            bool isHat = type == IFashionSenseApi.Type.Hat;

            if (!includeInternalIdHints)
            {
                if (isHair && suppressHairAndGenericHeadwearForSavedOutfit)
                    return;

                if (isHat && suppressHairAndGenericHeadwearForSavedOutfit && IsUnhelpfulInternalAppearanceId(id))
                    return;

                string imageOnlyClue = label + ": equipped; identify its visible form/theme only from the attached image";
                if (!isHair && !isHat && !string.IsNullOrWhiteSpace(color))
                    imageOnlyClue += ", API color clue " + color;
                pieces.Add(imageOnlyClue);
                return;
            }

            if (isHair)
            {
                // For a full saved outfit reaction, the player's hair should not become the
                // topic and should not be treated as part of the outfit palette. This avoids
                // brown/auburn hair being mistaken for a brown hat or a brown outfit detail.
                if (suppressHairAndGenericHeadwearForSavedOutfit)
                    return;

                // Do NOT report the Fashion Sense tint as the hair color: for custom/texture
                // hair it is frequently wrong (it caused the "orange" bug). The actual hair
                // color is read from the rendered sprite pixels and merged in later as the
                // authoritative source, but only for explicit hair-change reactions. Here we
                // only pass the item name as a soft style clue.
                pieces.Add(label + ": " + name + " (do NOT guess hair color from the image; an authoritative hair color may be provided separately)");
                return;
            }

            if (isHat)
            {
                if (suppressHairAndGenericHeadwearForSavedOutfit)
                {
                    // Many Fashion Sense hair bows/tiaras/head accessories are stored in the
                    // Hat slot with unhelpful IDs like "pack0005 hat 2". If the player changed
                    // a whole saved outfit, do not let that generic slot name become the main
                    // dialogue hook. Keep only meaningful themed headwear names.
                    if (IsUnhelpfulInternalAppearanceId(id))
                        return;

                    pieces.Add("visible head accessory/headwear: " + name + " (theme/shape clue only; do not name its color; do not call it a hat unless it is clearly a hat)");
                    return;
                }

                // Match hair behavior for Fashion Sense hats/headwear too. Many custom hats
                // have their real color painted into the texture while the Fashion Sense tint
                // API reports default/untinted, so the authoritative hat color is read from
                // the rendered sprite pixels and merged in later, but only for explicit
                // hat/headwear-change reactions. Here we only pass the item name as a soft
                // shape/style clue.
                pieces.Add(label + ": " + name + " (do NOT guess hat/headwear color from the raw image; an authoritative hat color may be provided separately)");
                return;
            }

            if (!string.IsNullOrWhiteSpace(color))
                pieces.Add(label + ": " + name + ", color clue " + color);
            else
                pieces.Add(label + ": " + name);
        }

        private static string TryGetAppearanceId(IFashionSenseApi api, Farmer farmer, IFashionSenseApi.Type type)
        {
            try
            {
                KeyValuePair<bool, string> response = api.GetCurrentAppearanceId(type, farmer);
                if (response.Key && !string.IsNullOrWhiteSpace(response.Value))
                {
                    string value = response.Value.Trim();
                    if (!value.Equals("None", StringComparison.OrdinalIgnoreCase))
                        return value;
                }
            }
            catch
            {
                // Optional endpoint; ignore and keep prompt generation safe.
            }

            return "";
        }

        private static string TryGetAppearanceColorDescription(IFashionSenseApi api, Farmer farmer, IFashionSenseApi.Type type)
        {
            try
            {
                KeyValuePair<bool, Color> response = api.GetAppearanceColor(type, farmer);
                if (!response.Key)
                    return "";

                Color color = response.Value;
                if (color.A <= 0)
                    return "transparent";

                // White is often the neutral/default tint in Fashion Sense, so avoid over-emphasizing it.
                if (color.R >= 245 && color.G >= 245 && color.B >= 245)
                    return "default/untinted";

                return ColorNamer.ClosestSimpleColorName(color) + " (#" + color.R.ToString("X2", CultureInfo.InvariantCulture) + color.G.ToString("X2", CultureInfo.InvariantCulture) + color.B.ToString("X2", CultureInfo.InvariantCulture) + ")";
            }
            catch
            {
                return "";
            }
        }

        // Public wrapper so other classes (e.g. ModEntry) can reuse the same name cleanup
        // logic instead of duplicating it.
        public static string HumanizeAppearanceId(string id)
        {
            return HumanizeFashionSenseId(id);
        }

        private static string HumanizeFashionSenseId(string id)
        {
            if (string.IsNullOrWhiteSpace(id))
                return "";

            string text = id.Trim();
            int slash = text.LastIndexOf('/');
            if (slash >= 0 && slash < text.Length - 1)
                text = text[(slash + 1)..];

            text = Regex.Replace(text, @"[_\-.]+", " ");
            text = Regex.Replace(text, @"([a-zà-ÿ])([A-Z])", "$1 $2");
            text = Regex.Replace(text, @"\s+", " ").Trim();

            if (string.IsNullOrWhiteSpace(text))
                return id.Trim();

            return text;
        }

        public static bool IsUnhelpfulInternalAppearanceId(string id)
        {
            if (string.IsNullOrWhiteSpace(id))
                return true;

            string humanized = HumanizeFashionSenseId(id);
            string lower = (" " + humanized + " ").ToLowerInvariant();

            // Generic pack/slot names are not meaningful enough for dialogue. They tend to
            // make the AI say things like "brown hat" when the item is actually a bow,
            // hairband, tiara, or just the player's hair showing through a tiny sprite.
            if (Regex.IsMatch(lower, @"\bpack\s*\d+\b", RegexOptions.IgnoreCase))
                return true;

            if (Regex.IsMatch(lower, @"\b(hat|hair|accessory|acc|item|pack|slot|part)\s*\d+\b", RegexOptions.IgnoreCase))
                return true;

            // Mostly numbers and generic slot words? Not useful.
            string withoutGeneric = Regex.Replace(lower, @"\b(hat|hair|accessory|acc|item|pack|slot|part|fs|yomi)\b", " ", RegexOptions.IgnoreCase);
            withoutGeneric = Regex.Replace(withoutGeneric, @"[0-9_\-\.]+", " ");
            withoutGeneric = Regex.Replace(withoutGeneric, @"\s+", " ").Trim();

            return withoutGeneric.Length < 3;
        }

        private static bool IsAccessoryType(IFashionSenseApi.Type type)
        {
            return type == IFashionSenseApi.Type.Accessory
                || type == IFashionSenseApi.Type.AccessorySecondary
                || type == IFashionSenseApi.Type.AccessoryTertiary;
        }

        private static bool IsIgnoredMakeupAccessoryId(string id)
        {
            if (string.IsNullOrWhiteSpace(id))
                return false;

            string humanized = HumanizeFashionSenseId(id);
            string lower = " " + string.Join(" ", new[] { id, humanized })
                .ToLowerInvariant()
                .Replace('_', ' ')
                .Replace('-', ' ')
                .Replace('.', ' ')
                .Replace('/', ' ') + " ";

            bool isEyeCosmetic =
                (lower.Contains(" eye ") || lower.Contains(" eyes ") || lower.Contains(" olho ") || lower.Contains(" olhos "))
                && (lower.Contains(" highlight ")
                    || lower.Contains(" highlights ")
                    || lower.Contains(" sparkle ")
                    || lower.Contains(" sparkles ")
                    || lower.Contains(" shine ")
                    || lower.Contains(" glitter ")
                    || lower.Contains(" gloss ")
                    || lower.Contains(" brilho ")
                    || lower.Contains(" brilhos "));

            bool isFaceCosmetic =
                (lower.Contains(" face ") || lower.Contains(" facial ") || lower.Contains(" rosto "))
                && (lower.Contains(" makeup ")
                    || lower.Contains(" maquiagem ")
                    || lower.Contains(" highlight ")
                    || lower.Contains(" blush ")
                    || lower.Contains(" sparkle ")
                    || lower.Contains(" shine ")
                    || lower.Contains(" glitter ")
                    || lower.Contains(" gloss ")
                    || lower.Contains(" brilho "));

            return isEyeCosmetic
                || isFaceCosmetic
                || lower.Contains(" makeup ")
                || lower.Contains(" maquiagem ")
                || lower.Contains(" blush ")
                || lower.Contains(" lipstick ")
                || lower.Contains(" batom ")
                || lower.Contains(" eyeshadow ")
                || lower.Contains(" eye shadow ")
                || lower.Contains(" sombra ")
                || lower.Contains(" eyeliner ")
                || lower.Contains(" delineador ")
                || lower.Contains(" rimel ")
                || lower.Contains(" rímel ");
        }
    }
}
