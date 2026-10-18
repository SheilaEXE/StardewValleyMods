using StardewModdingAPI;
using StardewValley;

namespace LookingAtSebastian;

public sealed partial class ModEntry
{
    private SideCloseSequenceKind GetSideCloseSequenceKind()
    {
        int hearts = this.GetHeartsWithSebastian();
        bool isDating = this.IsDatingSebastian();

        if (isDating && hearts >= 8)
            return SideCloseSequenceKind.Dating8;

        if (isDating)
            return SideCloseSequenceKind.None;

        if (hearts >= 8)
            return SideCloseSequenceKind.PreDating8;

        if (hearts >= 2)
            return SideCloseSequenceKind.LowToSix;

        return SideCloseSequenceKind.None;
    }

    private void StartSideCloseSequence(NPC sebastian, long currentTick, SideCloseSequenceKind kind)
    {
        this.SaveSebastianFacingForSequence(sebastian);

        this.LastCloseSequenceTick = currentTick;
        this.CloseScenario = CloseStareScenario.Side;
        this.CloseStage = CloseStareStage.SideWaitingForSecondStare;
        this.CloseStageStartTick = currentTick;
        this.CurrentSideSequenceKind = kind;
        this.SideSecondStareTicks = 0;

        int noticeEmote = Game1.random.Next(2) == 0
            ? this.Config.SideNoticeEmoteA
            : this.Config.SideNoticeEmoteB;

        sebastian.doEmote(noticeEmote);
        this.TryMakeSebastianLookAtPlayer(sebastian);

        if (this.Config.DebugLogs)
            this.Monitor.Log($"Side close sequence started. Kind: {kind}, notice emote: {noticeEmote}", LogLevel.Info);
    }
    private void TriggerSideFinalReaction(NPC sebastian, long currentTick)
    {
        // Trava de segurança: se já chegou na parte do diálogo/saída,
        // não deixa disparar emote final de novo.
        if (
            this.CloseStage == CloseStareStage.SideWaitingForDialogueOpen ||
            this.CloseStage == CloseStareStage.SideWaitingForDialogueClose ||
            this.CloseStage == CloseStareStage.SideWaitingForLeave
        )
        {
            return;
        }

        int finalEmote;

        if (this.CurrentSideSequenceKind == SideCloseSequenceKind.Dating8)
        {
            finalEmote = Game1.random.Next(2) == 0
                ? this.Config.ShyEmoteId
                : this.Config.BackDatingHeartEmote;
        }
        else
        {
            finalEmote = Game1.random.Next(2) == 0
                ? this.Config.SideFinalNervousEmote
                : this.Config.ShyEmoteId;
        }

        sebastian.doEmote(finalEmote);

        this.SetSideCloseClickableDialogue(sebastian);

        // ESSA PARTE É A MAIS IMPORTANTE:
        // depois do emote final, a sequência precisa sair do estágio de tremedeira
        // e ir para o estágio de esperar a jogadora clicar nele.
        this.CloseStage = CloseStareStage.SideWaitingForDialogueOpen;
        this.CloseStageStartTick = currentTick;
        this.SideSecondStareTicks = 0;

        if (this.Config.DebugLogs)
            this.Monitor.Log($"Side close final reaction. Kind: {this.CurrentSideSequenceKind}, emote: {finalEmote}", LogLevel.Info);
    }
    private void SetSideCloseClickableDialogue(NPC sebastian)
    {
        int hearts = this.GetHeartsWithSebastian();
        bool isDating = this.IsDatingSebastian();

        (string prefix, int count) = this.GetCloseDialoguePrefixAndCount(
            CloseStareScenario.Side,
            hearts,
            isDating
        );

        string? line = this.GetRandomDialogue(prefix, count);

        if (string.IsNullOrWhiteSpace(line))
            line = this.GetCloseDialogueFallback(CloseStareScenario.Side, hearts, isDating);

        if (string.IsNullOrWhiteSpace(line))
        {
            this.Monitor.Log($"Nenhum diálogo encontrado para Side. Hearts={hearts}, Dating={isDating}, Prefix={prefix}, Count={count}", LogLevel.Warn);
            return;
        }

        this.SetClickableDialogue(sebastian, line);
    }
}