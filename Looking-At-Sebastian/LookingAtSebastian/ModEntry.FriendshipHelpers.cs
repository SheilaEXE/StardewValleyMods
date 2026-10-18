using StardewValley;

namespace LookingAtSebastian;

public sealed partial class ModEntry
{
    private int GetHeartsWithSebastian()
    {
        if (!Game1.player.friendshipData.TryGetValue("Sebastian", out Friendship friendship))
            return 0;

        return friendship.Points / 250;
    }
    private bool IsDatingSebastian()
    {
        if (!Game1.player.friendshipData.TryGetValue("Sebastian", out Friendship friendship))
            return false;

        return friendship.IsDating();
    }
}