using StardewValley;

namespace LookingAtSebastian;

public sealed partial class ModEntry
{
    private bool IsCooldownReady(long currentTick, long lastTriggerTick, int cooldownSeconds)
    {
        long cooldownTicks = cooldownSeconds * 60L;
        return currentTick - lastTriggerTick >= cooldownTicks;
    }
    private bool RollChance(int chance)
    {
        chance = Math.Clamp(chance, 0, 100);
        return Game1.random.Next(100) < chance;
    }
    private bool IsLowOrFourHeartTier()
    {
        int hearts = this.GetHeartsWithSebastian();

        // 2/3/4/5 corações, antes do namoro.
        return hearts >= 2 && hearts < 6 && !this.IsDatingSebastian();
    }
}