using StardewValley;

namespace LotsOfKisses
{
    public partial class ModEntry
    {
        /// <summary>
        /// Check the vanilla spouse gift prerequisites without changing the one-time save flag.
        /// The gift itself must remain available through the player's normal interaction.
        /// </summary>
        private static bool IsSpouseStardropPending(NPC npc)
        {
            Farmer player = Game1.player;
            if (npc == null || player == null || !player.IsLocalPlayer || npc.Name != player.spouse)
                return false;

            return player.friendshipData.TryGetValue(npc.Name, out Friendship friendship)
                && friendship != null
                && friendship.Points >= 3125
                && !player.mailReceived.Contains("CF_Spouse");
        }
    }
}
