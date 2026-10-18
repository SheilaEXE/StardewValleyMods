using StardewModdingAPI;
using StardewValley;

namespace LookingAtSebastian;

public sealed partial class ModEntry
{
    private bool TryHandlePcSideStare(NPC sebastian, Farmer player, float distance, long currentTick)
    {
        if (!this.Config.EnablePcSideStareSequence)
            return false;

        // Estar no tile do computador não basta: se o mapa recarregou Sebastian
        // com um frame comum em pé, não inicia nem salva uma sequência inválida.
        if (!this.IsSebastianInActiveComputerPose(sebastian))
            return false;

        PcSideSequenceKind pcKind = this.GetPcSideSequenceKind();

        if (pcKind == PcSideSequenceKind.None)
            return false;

        if (distance > this.Config.PcSideStareDistance)
            return false;

        if (!this.IsPlayerFacingTarget(player, sebastian.Position))
            return false;

        CloseStareScenario scenario = this.GetSebastianStareScenario(sebastian, player);

        // No PC bloqueia a cena se a jogadora estiver na frente dele.
        // Só libera quando ela estiver encarando pela lateral ou por trás,
        // porque ele não vai virar nem sair da animação do computador.
        if (scenario == CloseStareScenario.Front)
            return false;

        if (!this.IsCooldownReady(currentTick, this.LastCloseSequenceTick, this.Config.CloseCooldownSeconds))
            return false;

        if (this.PlayerStillTicks < this.Config.PcSideRequiredStillTicks)
            return false;

        if (!this.RollChance(this.Config.CloseSequenceChance))
        {
            this.LastCloseSequenceTick = currentTick;
            return false;
        }

        this.StartPcSideSequence(sebastian, currentTick, pcKind);
        return true;
    }

    private PcSideSequenceKind GetPcSideSequenceKind()
    {
        int hearts = this.GetHeartsWithSebastian();
        bool isDating = this.IsDatingSebastian();

        if (isDating && hearts >= 8)
            return PcSideSequenceKind.Dating8;

        if (isDating)
            return PcSideSequenceKind.None;

        if (hearts >= 8)
            return PcSideSequenceKind.PreDating8;

        if (hearts >= 6)
            return PcSideSequenceKind.SixHeart;

        if (hearts >= 4)
            return PcSideSequenceKind.FourHeart;

        if (hearts >= 2)
            return PcSideSequenceKind.TwoHeart;

        return PcSideSequenceKind.None;
    }

    private void StartPcSideSequence(NPC sebastian, long currentTick, PcSideSequenceKind kind)
    {
        this.LastCloseSequenceTick = currentTick;
        this.CloseScenario = CloseStareScenario.Side;
        this.CloseStage = CloseStareStage.PcSideWaitingForNoticeEmoteToEnd;
        this.CloseStageStartTick = currentTick;
        this.CurrentPcSideSequenceKind = kind;

        // No PC, salva a animação sem interromper. Se o clique/diálogo fizer
        // o jogo levantar/parar a sprite, a gente restaura em seguida.
        this.SaveSebastianPcAnimationForSequenceIfNeeded(sebastian);

        this.PlayerStillTicks = 0;
        this.LastPlayerPosition = Game1.player.Position;

        // No PC ele NÃO vira pra jogadora.
        sebastian.doEmote(this.Config.PcSideNoticeEmote);

        if (this.Config.DebugLogs)
            this.Monitor.Log($"PC side sequence started. Kind={kind}", LogLevel.Info);
    }

    private void SetPcSideClickableDialogue(NPC sebastian)
    {
        string prefix;
        int count;

        switch (this.CurrentPcSideSequenceKind)
        {
            case PcSideSequenceKind.Dating8:
                prefix = "DatingPcSide8.Sebastian";
                count = this.Config.DatingPcSide8DialogueCount;
                break;

            case PcSideSequenceKind.PreDating8:
                prefix = "preDatingPcSide8.Sebastian";
                count = this.Config.PcSide8DialogueCount;
                break;

            case PcSideSequenceKind.SixHeart:
                prefix = "preDatingPcSide6.Sebastian";
                count = this.Config.PcSide6DialogueCount;
                break;

            case PcSideSequenceKind.FourHeart:
                prefix = "preDatingPcSide4.Sebastian";
                count = this.Config.PcSide4DialogueCount;
                break;

            default:
                prefix = "preDatingPcSide2.Sebastian";
                count = this.Config.PcSide2DialogueCount;
                break;
        }

        string? line = this.GetRandomDialogue(prefix, count);

        if (string.IsNullOrWhiteSpace(line))
        {
            this.Monitor.Log($"Nenhum diálogo encontrado para {prefix}.", LogLevel.Warn);
            return;
        }

        this.SetPcClickableDialogue(sebastian, line);
    }
    private string? GetPcSideAfterLeaveBalloonDialogue()
    {
        string prefix;
        int count;

        switch (this.CurrentPcSideSequenceKind)
        {
            case PcSideSequenceKind.Dating8:
                prefix = "DatingPcSideAfterLeave8.Sebastian";
                count = this.Config.DatingPcSide8AfterLeaveBalloonCount;
                break;

            case PcSideSequenceKind.PreDating8:
                prefix = "preDatingPcSideAfterLeave8.Sebastian";
                count = this.Config.PcSide8AfterLeaveBalloonCount;
                break;

            case PcSideSequenceKind.SixHeart:
                prefix = "preDatingPcSideAfterLeave6.Sebastian";
                count = this.Config.PcSide6AfterLeaveBalloonCount;
                break;

            default:
                return null;
        }

        string? line = this.GetRandomDialogue(prefix, count);

        if (string.IsNullOrWhiteSpace(line))
            this.Monitor.Log($"Nenhum balão encontrado para {prefix}.", LogLevel.Warn);

        return line;
    }
}
