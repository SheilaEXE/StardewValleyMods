using Microsoft.Xna.Framework;
using StardewValley;
using System.Collections.Generic;

namespace OutfitReactions
{
    public interface IFashionSenseApi
    {
        public enum Type
        {
            Unknown,
            Hair,
            Accessory,
            AccessorySecondary,
            AccessoryTertiary,
            Hat,
            Shirt,
            Pants,
            Sleeves,
            Shoes,
            Player
        }

        KeyValuePair<bool, string> GetCurrentOutfitId();
        KeyValuePair<bool, string> GetCurrentAppearanceId(Type appearanceType, Farmer target = null);
        KeyValuePair<bool, Color> GetAppearanceColor(Type appearanceType, Farmer target = null);
    }
}
