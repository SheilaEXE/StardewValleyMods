using StardewValley;

namespace OutfitReactions;

public partial class ModEntry
{
    private const int PlayerSelfDescriptionLimit = 500;

    private string GetPlayerSelfDescription()
    {
        if (Game1.player?.modData.TryGetValue(PlayerSelfDescriptionModDataKey, out string value) != true)
            return "";
        return CleanPlayerSelfDescription(value);
    }

    private void OpenPlayerSelfDescriptionMenu()
    {
        string title = Helper.Translation.Get("player-personality.title");
        string save = Helper.Translation.Get("player-personality.save");
        string cancel = Helper.Translation.Get("player-personality.cancel");
        Game1.activeClickableMenu = new OutfitPlayerReplyTextInputMenu(
            title,
            save,
            cancel,
            text =>
            {
                string cleaned = CleanPlayerSelfDescription(text);
                if (string.IsNullOrWhiteSpace(cleaned))
                    Game1.player.modData.Remove(PlayerSelfDescriptionModDataKey);
                else
                    Game1.player.modData[PlayerSelfDescriptionModDataKey] = cleaned;
                Game1.exitActiveMenu();
                Game1.addHUDMessage(new HUDMessage(Helper.Translation.Get(
                    string.IsNullOrWhiteSpace(cleaned) ? "player-personality.cleared" : "player-personality.saved")));
            },
            () => { },
            GetPlayerSelfDescription(),
            PlayerSelfDescriptionLimit);
    }

    private static string CleanPlayerSelfDescription(string value)
    {
        string cleaned = (value ?? "").Replace('\r', ' ').Replace('\n', ' ').Trim();
        while (cleaned.Contains("  "))
            cleaned = cleaned.Replace("  ", " ");
        return cleaned.Length <= PlayerSelfDescriptionLimit
            ? cleaned
            : cleaned.Substring(0, PlayerSelfDescriptionLimit).TrimEnd();
    }
}
