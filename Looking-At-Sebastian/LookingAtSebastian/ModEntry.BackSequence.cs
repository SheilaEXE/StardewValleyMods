using StardewModdingAPI;
using StardewValley;

namespace LookingAtSebastian;

public sealed partial class ModEntry
{
    private BackCloseSequenceKind GetBackCloseSequenceKind()
    {
        int hearts = this.GetHeartsWithSebastian();
        bool isDating = this.IsDatingSebastian();

        // Namorando com 8+ corações.
        if (isDating && hearts >= 8)
            return BackCloseSequenceKind.Dating8;

        // Se estiver namorando, mas por algum motivo ainda não tiver 8,
        // não usa sequência especial.
        if (isDating)
            return BackCloseSequenceKind.None;

        // Pré-namoro com 6, 7 ou 8+ corações.
        if (hearts >= 6)
            return BackCloseSequenceKind.HighHeart;

        // Pré-namoro com 2, 3, 4 ou 5 corações.
        if (hearts >= 2)
            return BackCloseSequenceKind.LowHeart;

        return BackCloseSequenceKind.None;
    }
    private void StartBackCloseSequence(NPC sebastian, long currentTick, BackCloseSequenceKind kind)
    {
        this.SaveSebastianFacingForSequence(sebastian);

        this.LastCloseSequenceTick = currentTick;
        this.CloseScenario = CloseStareScenario.Back;
        this.CloseStage = CloseStareStage.BackWaitingForNoticeEmoteToEnd;
        this.CloseStageStartTick = currentTick;
        this.CurrentBackSequenceKind = kind;

        int noticeEmote = Game1.random.Next(2) == 0
            ? this.Config.BackNoticeEmoteA
            : this.Config.BackNoticeEmoteB;

        sebastian.doEmote(noticeEmote);

        if (this.Config.DebugLogs)
            this.Monitor.Log($"Back close sequence started. Kind: {kind}, notice emote: {noticeEmote}", LogLevel.Info);
    }
    private void SetBackCloseClickableDialogue(NPC sebastian)
    {
        int hearts = this.GetHeartsWithSebastian();
        bool isDating = this.IsDatingSebastian();

        (string prefix, int count) = this.GetCloseDialoguePrefixAndCount(
            CloseStareScenario.Back,
            hearts,
            isDating
        );

        string? line = this.GetRandomDialogue(prefix, count);

        if (string.IsNullOrWhiteSpace(line))
            line = this.GetCloseDialogueFallback(CloseStareScenario.Back, hearts, isDating);

        if (!string.IsNullOrWhiteSpace(line))
            this.SetClickableDialogue(sebastian, line);
    }
}