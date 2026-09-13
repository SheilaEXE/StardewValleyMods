using System.Globalization;
using System.Linq;
using StardewModdingAPI;
using StardewValley;
using StardewValley.Delegates;
using xTile.Dimensions;

namespace TileMarker.ContentPatcherInterop
{
    /// <summary>Lets Content Patcher integrations invoke a normal map action on a marked tile.</summary>
    internal static class MapActionTrigger
    {
        internal const string ActionName = "NatrollEXE.TileMarker_PerformMapAction";

        internal static bool PerformMapAction(string[] args, TriggerActionContext context, out string error)
        {
            if (args.Length < 4)
            {
                error = $"invalid format: expected '{ActionName} <tile X> <tile Y> <map action>'";
                return false;
            }

            if (!int.TryParse(args[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out int tileX)
                || !int.TryParse(args[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out int tileY))
            {
                error = "the tile X and Y arguments must be whole numbers";
                return false;
            }

            if (!Context.IsWorldReady || Game1.currentLocation == null || Game1.player == null)
            {
                error = "no player location is currently available";
                return false;
            }

            string mapAction = string.Join(" ", args.Skip(3));
            bool handled = Game1.currentLocation.performAction(
                mapAction,
                Game1.player,
                new Location(tileX, tileY));

            error = handled ? null : $"map action '{mapAction}' wasn't handled at tile {tileX}, {tileY}";
            return handled;
        }
    }
}
