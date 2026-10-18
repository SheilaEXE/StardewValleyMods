using StardewModdingAPI;
using StardewValley;

namespace LookingAtSebastian;

public sealed partial class ModEntry
{
    private FrontCloseSequenceKind GetFrontCloseSequenceKind()
    {
        int hearts = this.GetHeartsWithSebastian();
        bool isDating = this.IsDatingSebastian();

        // Namorando com 8+ corações.
        if (isDating && hearts >= 8)
            return FrontCloseSequenceKind.Dating8;

        if (isDating)
            return FrontCloseSequenceKind.None;

        // Pré-namoro com 8+ corações.
        if (hearts >= 8)
            return FrontCloseSequenceKind.PreDating8;

        // Pré-namoro com 6 ou 7 corações.
        if (hearts >= 6)
            return FrontCloseSequenceKind.SixHeart;

        // Pré-namoro com 2, 3, 4 ou 5 corações.
        if (hearts >= 2)
            return FrontCloseSequenceKind.LowHeart;

        return FrontCloseSequenceKind.None;
    }
    private void StartFrontCloseSequence(NPC sebastian, long currentTick, FrontCloseSequenceKind kind)
    {
        this.SaveSebastianFacingForSequence(sebastian);

        this.LastCloseSequenceTick = currentTick;
        this.CloseScenario = CloseStareScenario.Front;
        this.CloseStage = CloseStareStage.FrontLowHeartWaitingForApproach;
        this.CloseStageStartTick = currentTick;
        this.CurrentFrontSequenceKind = kind;
        this.FrontLowHeartSecondStareTicks = 0;
        this.FrontLowHeartJumped = false;

        int emote;

        switch (kind)
        {
            case FrontCloseSequenceKind.LowHeart:
                sebastian.shake(this.Config.FrontLowHeartShakeDurationMs);
                emote = Game1.random.Next(2) == 0
                    ? this.Config.SideFinalNervousEmote
                    : this.Config.ShyEmoteId;
                break;

            case FrontCloseSequenceKind.SixHeart:
                emote = Game1.random.Next(2) == 0
                    ? this.Config.SideFinalNervousEmote
                    : this.Config.ShyEmoteId;
                break;

            case FrontCloseSequenceKind.Dating8:
                emote = Game1.random.Next(3) switch
                {
                    0 => this.Config.ShyEmoteId,
                    1 => this.Config.BackDatingHeartEmote,
                    _ => this.Config.FrontDatingHappyEmote
                };
                break;

            default:
                // 8 corações pré-namoro.
                emote = this.Config.ShyEmoteId;
                break;
        }

        sebastian.doEmote(emote);

        string? balloonLine = this.GetFrontBalloonDialogue();

        if (!string.IsNullOrWhiteSpace(balloonLine))
            sebastian.showTextAboveHead(balloonLine);

        if (this.Config.DebugLogs)
            this.Monitor.Log($"Front sequence started. Kind: {kind}, emote: {emote}", LogLevel.Info);
    }
    private string? GetFrontBalloonDialogue()
    {
        int hearts = this.GetHeartsWithSebastian();
        bool isDating = this.IsDatingSebastian();

        // Namorando com 8+ corações continua tendo balões próprios.
        if (isDating && hearts >= 8)
        {
            string? dating8 = this.GetRandomDialogue(
                "DatingFrontBalloon8.Sebastian",
                this.Config.DatingFrontEightHeartBalloonDialogueCount
            );

            if (!string.IsNullOrWhiteSpace(dating8))
                return dating8;
        }

        // Pré-namoro: 4, 6 e 8 corações usam a mesma família de balões.
        if (hearts >= 4)
        {
            string? line = this.GetRandomDialogue(
                "preDatingFrontBalloon4_6_8.Sebastian",
                this.Config.FrontBalloon4_6_8DialogueCount
            );

            if (!string.IsNullOrWhiteSpace(line))
                return line;
        }

        // 2/3 corações não tem mais balão de frente.
        return null;
    }
    private void TriggerFrontFinalReaction(NPC sebastian, long currentTick)
    {
        int emote;

        if (this.CurrentFrontSequenceKind == FrontCloseSequenceKind.Dating8)
        {
            emote = Game1.random.Next(2) == 0
                ? this.Config.ShyEmoteId
                : this.Config.BackDatingHeartEmote;
        }
        else if (this.CurrentFrontSequenceKind == FrontCloseSequenceKind.PreDating8)
        {
            emote = this.Config.ShyEmoteId;
        }
        else
        {
            emote = Game1.random.Next(2) == 0
                ? this.Config.SideFinalNervousEmote
                : this.Config.ShyEmoteId;
        }

        sebastian.doEmote(emote);

        this.SetFrontClickableDialogue(sebastian);

        this.CloseStage = CloseStareStage.FrontLowHeartWaitingForDialogueOpen;
        this.CloseStageStartTick = currentTick;

        if (this.Config.DebugLogs)
            this.Monitor.Log($"Front final reaction triggered. Kind: {this.CurrentFrontSequenceKind}, emote: {emote}", LogLevel.Info);
    }
    private void SetFrontClickableDialogue(NPC sebastian)
    {
        this.CurrentDialogueHasDatingFrontCounterEmotes = false;

        int hearts = this.GetHeartsWithSebastian();
        bool isDating = this.IsDatingSebastian();

        (string prefix, int count) = this.GetCloseDialoguePrefixAndCount(
            CloseStareScenario.Front,
            hearts,
            isDating
        );

        string? line;
        string? selectedKey = null;

        if (prefix == "DatingCloseFront8.Sebastian")
        {
            (line, selectedKey) = this.GetRandomDialogueWithKey(prefix, count);

            if (this.IsDatingFrontCounterDialogueKey(selectedKey))
                this.CurrentDialogueHasDatingFrontCounterEmotes = true;
        }
        else
        {
            line = this.GetRandomDialogue(prefix, count);
        }

        if (string.IsNullOrWhiteSpace(line))
        {
            line = this.GetCloseDialogueFallback(CloseStareScenario.Front, hearts, isDating);
            this.CurrentDialogueHasDatingFrontCounterEmotes = false;
        }

        if (!string.IsNullOrWhiteSpace(line))
            this.SetClickableDialogue(sebastian, line);

        if (this.Config.DebugLogs && !string.IsNullOrWhiteSpace(selectedKey))
        {
            this.Monitor.Log(
                $"Front dialogue selected: {selectedKey}, special counter emotes: {this.CurrentDialogueHasDatingFrontCounterEmotes}",
                LogLevel.Info
            );
        }
    }
    private void TriggerDatingFrontCounterEmotes(NPC sebastian)
    {
        int playerEmote = Game1.random.Next(2) == 0
            ? this.Config.SideFinalNervousEmote
            : this.Config.PlayerAngryEmoteId;

        int sebastianEmote = Game1.random.Next(3) switch
        {
            0 => this.Config.BackDatingHeartEmote,
            1 => this.Config.DatingFrontCounterMusicEmoteId,
            _ => this.Config.FrontDatingHappyEmote
        };

        Game1.player.doEmote(playerEmote);
        sebastian.doEmote(sebastianEmote);

        if (this.Config.DebugLogs)
        {
            this.Monitor.Log(
                $"Dating front counter emotes triggered. Player={playerEmote}, Sebastian={sebastianEmote}",
                LogLevel.Info
            );
        }
    }
    private string? GetFrontAfterLeaveBalloonDialogue()
    {
        int hearts = this.GetHeartsWithSebastian();
        bool isDating = this.IsDatingSebastian();

        if (isDating && hearts >= 8)
        {
            string? datingLine = this.GetRandomDialogue(
                "DatingFrontAfterLeaveBalloon8.Sebastian",
                this.Config.DatingFrontEightHeartAfterLeaveBalloonDialogueCount
            );

            if (!string.IsNullOrWhiteSpace(datingLine))
                return datingLine;
        }

        if (hearts >= 8)
        {
            string? line8 = this.GetRandomDialogue(
                "preDatingFrontAfterLeaveBalloon8.Sebastian",
                this.Config.FrontEightHeartAfterLeaveBalloonDialogueCount
            );

            if (!string.IsNullOrWhiteSpace(line8))
                return line8;
        }

        return null;
    }
}