using StardewModdingAPI;
using StardewValley;
using StardewValley.Menus;

namespace LookingAtSebastian;

public sealed partial class ModEntry
{
    private void TriggerFinalCloseReaction(NPC sebastian, CloseStareScenario scenario, long currentTick)
    {
        sebastian.doEmote(this.Config.ShyEmoteId);

        int hearts = this.GetHeartsWithSebastian();
        bool isDating = this.IsDatingSebastian();

        (string prefix, int count) = this.GetCloseDialoguePrefixAndCount(scenario, hearts, isDating);

        string? line = this.GetRandomDialogue(prefix, count);

        // Fallbacks, caso alguma família esteja sem diálogo.
        if (string.IsNullOrWhiteSpace(line))
            line = this.GetCloseDialogueFallback(scenario, hearts, isDating);

        if (!string.IsNullOrWhiteSpace(line))
            this.SetClickableDialogue(sebastian, line);

        this.LastCloseSequenceTick = currentTick;

        if (this.Config.DebugLogs)
        {
            this.Monitor.Log(
                $"Close stare final reaction triggered. Scenario: {scenario}, hearts: {hearts}, dating: {isDating}, prefix: {prefix}",
                LogLevel.Info
            );
        }
    }
    private bool TryHandleCloseStare(NPC sebastian, Farmer player, float distance, long currentTick)
    {
        if (this.PendingFacingRestore)
            return false;

        if (this.CloseStage != CloseStareStage.None)
            return this.AdvanceCloseStareSequence(sebastian, player, distance, currentTick);

        // PC: sequência especial, sem virar o Sebastian e sem depender de CanSafelyTurn.
        if (this.IsSebastianAtComputer(sebastian))
            return this.TryHandlePcSideStare(sebastian, player, distance, currentTick);

        if (!this.IsPlayerFacingTarget(player, sebastian.Position))
        {
            this.ResetCloseStareSequence();
            return false;
        }

        if (!this.CanContinueOrTemporarilyInterruptSebastian(sebastian))
        {
            this.ResetCloseStareSequence();
            return false;
        }

        if (!this.IsCooldownReady(currentTick, this.LastCloseSequenceTick, this.Config.CloseCooldownSeconds))
            return false;

        CloseStareScenario scenario = this.GetSebastianStareScenario(sebastian, player);

        // Frente, 2/4 corações: começa até 400f.
        if (scenario == CloseStareScenario.Front)
        {
            FrontCloseSequenceKind frontKind = this.GetFrontCloseSequenceKind();

            if (frontKind != FrontCloseSequenceKind.None)
            {
                bool isInBalloonDistance =
                    distance >= this.Config.FrontBalloonMinDistance &&
                    distance <= this.Config.FrontBalloonMaxDistance;

                // Só a cena com balão começa de longe.
                // Se estiver perto demais, deixa cair para a cena normal de frente lá embaixo.
                if (isInBalloonDistance)
                {
                    if (this.PlayerStillTicks < this.Config.FrontLowHeartFirstStareRequiredTicks)
                        return false;

                    if (!this.RollChance(this.Config.CloseSequenceChance))
                    {
                        this.LastCloseSequenceTick = currentTick;
                        return false;
                    }

                    this.StartFrontCloseSequence(sebastian, currentTick, frontKind);
                    return true;
                }
            }
        }

        // Daqui pra baixo continuam as sequências normais de perto.
        if (distance > this.Config.CloseStareDistance)
        {
            this.ResetCloseStareSequence();
            return false;
        }

        if (scenario == CloseStareScenario.Back)
        {
            BackCloseSequenceKind backKind = this.GetBackCloseSequenceKind();

            if (backKind == BackCloseSequenceKind.None)
                return false;

            if (this.PlayerStillTicks < this.Config.BackCloseRequiredStillTicks)
                return false;

            if (!this.RollChance(this.Config.CloseSequenceChance))
            {
                this.LastCloseSequenceTick = currentTick;
                return false;
            }

            this.StartBackCloseSequence(sebastian, currentTick, backKind);
            return true;
        }

        if (scenario == CloseStareScenario.Side)
        {
            SideCloseSequenceKind sideKind = this.GetSideCloseSequenceKind();

            if (sideKind == SideCloseSequenceKind.None)
                return false;

            if (this.PlayerStillTicks < this.Config.SideFirstStareRequiredTicks)
                return false;

            if (!this.RollChance(this.Config.CloseSequenceChance))
            {
                this.LastCloseSequenceTick = currentTick;
                return false;
            }

            this.StartSideCloseSequence(sebastian, currentTick, sideKind);
            return true;
        }

        // Frente normal, para tiers que não usam a sequência especial.
        if (scenario == CloseStareScenario.Front)
        {
            if (this.PlayerStillTicks < this.Config.CloseFrontRequiredStillTicks)
                return false;

            if (!this.RollChance(this.Config.CloseSequenceChance))
            {
                this.LastCloseSequenceTick = currentTick;
                return false;
            }

            this.TriggerFinalCloseReaction(sebastian, CloseStareScenario.Front, currentTick);
            return true;
        }

        return false;
    }
    private bool AdvanceCloseStareSequence(NPC sebastian, Farmer player, float distance, long currentTick)
    {
        switch (this.CloseStage)
        {
            case CloseStareStage.PcSideWaitingForNoticeEmoteToEnd:
                {
                    bool stillValid =
                        this.IsSebastianAtComputer(sebastian) &&
                        distance <= this.Config.PcSideStareDistance &&
                        this.IsPlayerFacingTarget(player, sebastian.Position);

                    if (!stillValid)
                    {
                        this.ResetCloseStareSequence();
                        return false;
                    }

                    if (currentTick - this.CloseStageStartTick < this.Config.PcSideNoticeEmoteDurationTicks)
                        return true;

                    int finalEmote;

                    switch (this.CurrentPcSideSequenceKind)
                    {
                        case PcSideSequenceKind.FourHeart:
                            finalEmote = this.Config.ShyEmoteId;
                            break;

                        case PcSideSequenceKind.SixHeart:
                            finalEmote = Game1.random.Next(2) == 0
                                ? this.Config.SideFinalNervousEmote
                                : this.Config.ShyEmoteId;
                            break;

                        case PcSideSequenceKind.PreDating8:
                            finalEmote = this.Config.ShyEmoteId;
                            break;

                        case PcSideSequenceKind.Dating8:
                            finalEmote = Game1.random.Next(3) switch
                            {
                                0 => this.Config.BackDatingHeartEmote,
                                1 => this.Config.FrontDatingHappyEmote,
                                _ => this.Config.ShyEmoteId
                            };
                            break;

                        default:
                            finalEmote = Game1.random.Next(2) == 0
                                ? this.Config.SideFinalNervousEmote
                                : this.Config.ShyEmoteId;
                            break;
                    }

                    sebastian.doEmote(finalEmote);

                    this.SetPcSideClickableDialogue(sebastian);

                    this.CloseStage = CloseStareStage.PcSideWaitingForDialogueOpen;
                    this.CloseStageStartTick = currentTick;

                    if (this.Config.DebugLogs)
                        this.Monitor.Log($"PC side final emote triggered. Kind={this.CurrentPcSideSequenceKind}, emote={finalEmote}", LogLevel.Info);

                    return true;
                }

            case CloseStareStage.PcSideWaitingForDialogueOpen:
                {
                    if (Game1.activeClickableMenu is DialogueBox)
                    {
                        this.CloseStage = CloseStareStage.PcSideWaitingForDialogueClose;
                        this.CloseStageStartTick = currentTick;
                        return true;
                    }

                    bool timedOut = currentTick - this.CloseStageStartTick >= this.Config.PcSideDialogueOpenTimeoutTicks;
                    bool walkedAway = distance > this.Config.PcSideStareDistance;

                    if (timedOut || walkedAway)
                    {
                        this.PlayerStillTicks = 0;
                        this.LastPlayerPosition = player.Position;

                        this.ResetCloseStareSequence();
                        return false;
                    }

                    return true;
                }

            case CloseStareStage.PcSideWaitingForDialogueClose:
                {
                    if (Game1.activeClickableMenu is DialogueBox)
                        return true;

                    this.PlayerStillTicks = 0;
                    this.LastPlayerPosition = player.Position;

                    if (this.CurrentPcSideSequenceKind == PcSideSequenceKind.TwoHeart)
                    {
                        this.ResetCloseStareSequence();
                        return true;
                    }

                    this.CloseStage = CloseStareStage.PcSideWaitingForLeave;
                    this.CloseStageStartTick = currentTick;
                    return true;
                }
            case CloseStareStage.PcSideWaitingForLeave:
                {
                    if (distance < this.Config.PcSideLeaveDistance)
                        return true;

                    // 4/5 corações no PC: não solta emote ao afastar.
                    if (this.CurrentPcSideSequenceKind == PcSideSequenceKind.FourHeart)
                    {
                        if (this.Config.DebugLogs)
                            this.Monitor.Log("PC side 4-heart sequence: player left, no final emote.", LogLevel.Info);

                        this.ResetCloseStareSequence();
                        return true;
                    }

                    string? leaveLine = this.GetPcSideAfterLeaveBalloonDialogue();

                    if (!string.IsNullOrWhiteSpace(leaveLine))
                        sebastian.showTextAboveHead(leaveLine);

                    if (this.Config.DebugLogs)
                        this.Monitor.Log($"PC side sequence: player left. Kind={this.CurrentPcSideSequenceKind}", LogLevel.Info);

                    this.ResetCloseStareSequence();
                    return true;
                }

            case CloseStareStage.FrontLowHeartWaitingForApproach:
                {
                    bool stillValid =
                        distance <= this.Config.FrontBalloonMaxDistance &&
                        this.CanContinueOrTemporarilyInterruptSebastian(sebastian);

                    if (!stillValid)
                    {
                        this.ResetCloseStareSequence();
                        return false;
                    }

                    // Ele acompanha a jogadora com o olhar se ela passar reto.
                    this.TryMakeSebastianLookAtPlayer(sebastian);

                    if (!this.FrontLowHeartJumped && distance <= this.Config.FrontLowHeartApproachJumpDistance)
                    {
                        if (this.CurrentFrontSequenceKind == FrontCloseSequenceKind.LowHeart)
                        {
                            sebastian.jump();
                        }
                        else if (this.CurrentFrontSequenceKind == FrontCloseSequenceKind.SixHeart)
                        {
                            sebastian.doEmote(this.Config.FrontSixHeartApproachEmote);
                        }
                        // 8 corações e 8 namorando: não faz nada ao chegar em 250f.

                        this.FrontLowHeartJumped = true;

                        this.CloseStage = CloseStareStage.FrontLowHeartWatchingAfterApproach;
                        this.CloseStageStartTick = currentTick;

                        if (this.Config.DebugLogs)
                            this.Monitor.Log($"Front sequence: player approached. Kind: {this.CurrentFrontSequenceKind}", LogLevel.Info);

                        return true;
                    }

                    return true;
                }

            case CloseStareStage.FrontLowHeartWatchingAfterApproach:
                {
                    bool stillValid =
                        distance <= this.Config.FrontBalloonMaxDistance &&
                        this.CanContinueOrTemporarilyInterruptSebastian(sebastian);

                    if (!stillValid)
                    {
                        this.ResetCloseStareSequence();
                        return false;
                    }

                    // Continua seguindo com o olhar.
                    this.TryMakeSebastianLookAtPlayer(sebastian);

                    bool closeStareConditions =
                        distance <= this.Config.FrontLowHeartCloseDistance &&
                        this.IsPlayerFacingTarget(player, sebastian.Position);

                    if (closeStareConditions)
                        this.FrontLowHeartSecondStareTicks += 15;
                    else
                        this.FrontLowHeartSecondStareTicks = 0;

                    if (this.FrontLowHeartSecondStareTicks < this.Config.FrontLowHeartSecondStareRequiredTicks)
                        return true;

                    if (this.CurrentFrontSequenceKind == FrontCloseSequenceKind.LowHeart)
                    {
                        sebastian.shake(this.Config.FrontLowHeartShakeDurationMs);

                        this.CloseStage = CloseStareStage.FrontLowHeartWaitingAfterCloseShake;
                        this.CloseStageStartTick = currentTick;

                        return true;
                    }

                    // 6, 8 e 8 namorando: não tremem aqui.
                    this.TriggerFrontFinalReaction(sebastian, currentTick);
                    return true;
                }

            case CloseStareStage.FrontLowHeartWaitingAfterCloseShake:
                {
                    bool stillValid =
                        distance <= this.Config.FrontLowHeartCloseDistance &&
                        this.IsPlayerFacingTarget(player, sebastian.Position) &&
                        this.CanContinueOrTemporarilyInterruptSebastian(sebastian);

                    if (!stillValid)
                    {
                        this.ResetCloseStareSequence();
                        return false;
                    }

                    this.TryMakeSebastianLookAtPlayer(sebastian);

                    if (currentTick - this.CloseStageStartTick < this.Config.FrontLowHeartShakeToEmoteDelayTicks)
                        return true;

                    this.TriggerFrontFinalReaction(sebastian, currentTick);
                    return true;
                }

            case CloseStareStage.FrontLowHeartWaitingForDialogueOpen:
                {
                    if (Game1.activeClickableMenu is DialogueBox)
                    {
                        this.CloseStage = CloseStareStage.FrontLowHeartWaitingForDialogueClose;
                        this.CloseStageStartTick = currentTick;
                        return true;
                    }

                    bool timedOut = currentTick - this.CloseStageStartTick >= this.Config.FrontLowHeartDialogueOpenTimeoutTicks;
                    bool walkedAway = distance >= this.Config.FrontLowHeartLeaveDistance;

                    if (timedOut || walkedAway)
                    {
                        this.ResetCloseStareSequence();
                        return false;
                    }

                    return true;
                }
            case CloseStareStage.FrontLowHeartWaitingForDialogueClose:
                {
                    if (Game1.activeClickableMenu is DialogueBox)
                        return true;

                    if (this.CurrentDialogueHasDatingFrontCounterEmotes)
                    {
                        this.TriggerDatingFrontCounterEmotes(sebastian);

                        // Não espera afastar e não dispara balão/emote final de distância.
                        this.ClearSebastianFacingRestoreState();
                        this.ResetCloseStareSequence(restoreFacing: false);
                        return true;
                    }

                    this.CloseStage = CloseStareStage.FrontLowHeartWaitingForLeave;
                    this.CloseStageStartTick = currentTick;
                    return true;
                }

            case CloseStareStage.FrontLowHeartWaitingForLeave:
                {
                    if (distance < this.Config.FrontLowHeartLeaveDistance)
                        return true;

                    // 2/4 corações: não solta emote nem balão ao afastar.
                    if (this.IsLowOrFourHeartTier())
                    {
                        this.ScheduleSebastianFacingRestoreAfterEmote(currentTick);

                        if (this.Config.DebugLogs)
                            this.Monitor.Log($"Front sequence: player left, no final reaction for 2/4 hearts. Kind: {this.CurrentFrontSequenceKind}", LogLevel.Info);

                        this.ResetCloseStareSequence(restoreFacing: false);
                        return true;
                    }

                    if (
                        this.CurrentFrontSequenceKind == FrontCloseSequenceKind.PreDating8 ||
                        this.CurrentFrontSequenceKind == FrontCloseSequenceKind.Dating8
                    )
                    {
                        string? leaveLine = this.GetFrontAfterLeaveBalloonDialogue();

                        if (!string.IsNullOrWhiteSpace(leaveLine))
                            sebastian.showTextAboveHead(leaveLine);
                    }
                    else
                    {
                        sebastian.doEmote(this.Config.ShyEmoteId);
                    }

                    this.ScheduleSebastianFacingRestoreAfterEmote(currentTick);

                    if (this.Config.DebugLogs)
                        this.Monitor.Log($"Front sequence: player left, final reaction triggered, restore scheduled. Kind: {this.CurrentFrontSequenceKind}", LogLevel.Info);

                    this.ResetCloseStareSequence(restoreFacing: false);
                    return true;
                }

            case CloseStareStage.FrontLowHeartWaitingForFinalLeaveReaction:
                {
                    if (currentTick - this.CloseStageStartTick < this.Config.FinalLeaveReactionDelayTicks)
                        return true;

                    if (
                        this.CurrentFrontSequenceKind == FrontCloseSequenceKind.PreDating8 ||
                        this.CurrentFrontSequenceKind == FrontCloseSequenceKind.Dating8
                    )
                    {
                        string? leaveLine = this.GetFrontAfterLeaveBalloonDialogue();

                        if (!string.IsNullOrWhiteSpace(leaveLine))
                            sebastian.showTextAboveHead(leaveLine);
                    }
                    else
                    {
                        sebastian.doEmote(this.Config.ShyEmoteId);
                    }

                    if (this.Config.DebugLogs)
                        this.Monitor.Log($"Front sequence: final leave reaction triggered after restore. Kind: {this.CurrentFrontSequenceKind}", LogLevel.Info);

                    this.ResetCloseStareSequence(restoreFacing: false);
                    return true;
                }

            case CloseStareStage.SideWaitingForSecondStare:
                {
                    bool canContinueSideSequence =
                        this.CanContinueOrTemporarilyInterruptSebastian(sebastian);

                    bool stillValid =
                        distance <= this.Config.CloseStareDistance &&
                        this.IsPlayerFacingTarget(player, sebastian.Position) &&
                        canContinueSideSequence;

                    if (!stillValid)
                    {
                        this.ResetCloseStareSequence();
                        return false;
                    }

                    this.TryMakeSebastianLookAtPlayer(sebastian);

                    this.SideSecondStareTicks += 15;

                    if (this.SideSecondStareTicks < this.Config.SideSecondStareRequiredTicks)
                        return true;

                    if (this.CurrentSideSequenceKind == SideCloseSequenceKind.LowToSix)
                    {
                        sebastian.shake(this.Config.SideShakeDurationMs);

                        this.CloseStage = CloseStareStage.SideWaitingAfterShake;
                        this.CloseStageStartTick = currentTick;
                        return true;
                    }

                    this.TriggerSideFinalReaction(sebastian, currentTick);
                    return true;
                }

            case CloseStareStage.SideWaitingAfterShake:
                {
                    bool canContinueSideSequence =
                        this.CanContinueOrTemporarilyInterruptSebastian(sebastian);

                    bool stillValid =
                        distance <= this.Config.CloseStareDistance &&
                        this.IsPlayerFacingTarget(player, sebastian.Position) &&
                        canContinueSideSequence;

                    if (!stillValid)
                    {
                        this.ResetCloseStareSequence();
                        return false;
                    }

                    this.TryMakeSebastianLookAtPlayer(sebastian);

                    if (currentTick - this.CloseStageStartTick < this.Config.SideShakeToEmoteDelayTicks)
                        return true;

                    this.TriggerSideFinalReaction(sebastian, currentTick);
                    return true;
                }

            case CloseStareStage.SideWaitingForDialogueOpen:
                {
                    if (Game1.activeClickableMenu is DialogueBox)
                    {
                        this.CloseStage = CloseStareStage.SideWaitingForDialogueClose;
                        this.CloseStageStartTick = currentTick;
                        return true;
                    }

                    bool timedOut = currentTick - this.CloseStageStartTick >= this.Config.SideDialogueOpenTimeoutTicks;
                    bool walkedAway = distance >= this.Config.SideLeaveDistance;

                    if (timedOut || walkedAway)
                    {
                        this.ResetCloseStareSequence();
                        return false;
                    }

                    return true;
                }

            case CloseStareStage.SideWaitingForDialogueClose:
                {
                    if (Game1.activeClickableMenu is DialogueBox)
                        return true;

                    // Namorando 8 corações: termina aqui, sem emote ao afastar.
                    if (this.CurrentSideSequenceKind == SideCloseSequenceKind.Dating8)
                    {
                        this.ResetCloseStareSequence();
                        return true;
                    }

                    this.CloseStage = CloseStareStage.SideWaitingForLeave;
                    this.CloseStageStartTick = currentTick;
                    return true;
                }

            case CloseStareStage.SideWaitingForLeave:
                {
                    if (distance < this.Config.SideLeaveDistance)
                        return true;

                    // 2/4 corações: não solta emote ao afastar.
                    if (this.IsLowOrFourHeartTier())
                    {
                        this.ScheduleSebastianFacingRestoreAfterEmote(currentTick);

                        if (this.Config.DebugLogs)
                            this.Monitor.Log("Side close sequence: player left, no final emote for 2/4 hearts.", LogLevel.Info);

                        this.ResetCloseStareSequence(restoreFacing: false);
                        return true;
                    }

                    sebastian.doEmote(this.Config.ShyEmoteId);

                    this.ScheduleSebastianFacingRestoreAfterEmote(currentTick);

                    if (this.Config.DebugLogs)
                        this.Monitor.Log("Side close sequence: player left, shy emote triggered, restore scheduled.", LogLevel.Info);

                    this.ResetCloseStareSequence(restoreFacing: false);
                    return true;
                }

            case CloseStareStage.SideWaitingForFinalLeaveReaction:
                {
                    if (currentTick - this.CloseStageStartTick < this.Config.FinalLeaveReactionDelayTicks)
                        return true;

                    sebastian.doEmote(this.Config.ShyEmoteId);

                    if (this.Config.DebugLogs)
                        this.Monitor.Log("Side close sequence: final shy emote triggered after restore.", LogLevel.Info);

                    this.ResetCloseStareSequence(restoreFacing: false);
                    return true;
                }

            case CloseStareStage.BackWaitingForNoticeEmoteToEnd:
                {
                    bool stillValid =
                        distance <= this.Config.CloseStareDistance &&
                        this.CanContinueOrTemporarilyInterruptSebastian(sebastian);

                    if (!stillValid)
                    {
                        this.ResetCloseStareSequence();
                        return false;
                    }

                    if (currentTick - this.CloseStageStartTick < this.Config.BackNoticeEmoteDurationTicks)
                        return true;

                    this.TryMakeSebastianLookAtPlayer(sebastian);

                    if (this.CurrentBackSequenceKind == BackCloseSequenceKind.LowHeart)
                    {
                        sebastian.jump();
                        sebastian.doEmote(this.Config.BackLowHeartSurpriseEmote);
                    }
                    else if (this.CurrentBackSequenceKind == BackCloseSequenceKind.Dating8)
                    {
                        int emote = Game1.random.Next(2) == 0
                            ? this.Config.ShyEmoteId
                            : this.Config.BackDatingHeartEmote;

                        sebastian.doEmote(emote);
                    }
                    else
                    {
                        sebastian.doEmote(this.Config.ShyEmoteId);
                    }

                    this.SetBackCloseClickableDialogue(sebastian);

                    this.CloseStage = CloseStareStage.BackWaitingForDialogueOpen;
                    this.CloseStageStartTick = currentTick;

                    if (this.Config.DebugLogs)
                        this.Monitor.Log($"Back close sequence turned. Kind: {this.CurrentBackSequenceKind}", LogLevel.Info);

                    return true;
                }

            case CloseStareStage.BackWaitingForDialogueOpen:
                {
                    if (Game1.activeClickableMenu is DialogueBox)
                    {
                        this.CloseStage = CloseStareStage.BackWaitingForDialogueClose;
                        this.CloseStageStartTick = currentTick;
                        return true;
                    }

                    bool timedOut = currentTick - this.CloseStageStartTick >= this.Config.BackDialogueOpenTimeoutTicks;
                    bool walkedAway = distance >= this.Config.BackLeaveDistance;

                    if (timedOut || walkedAway)
                    {
                        this.ResetCloseStareSequence();
                        return false;
                    }

                    return true;
                }

            case CloseStareStage.BackWaitingForDialogueClose:
                {
                    if (Game1.activeClickableMenu is DialogueBox)
                        return true;

                    if (this.CurrentBackSequenceKind == BackCloseSequenceKind.Dating8)
                    {
                        this.ResetCloseStareSequence();
                        return true;
                    }

                    this.CloseStage = CloseStareStage.BackWaitingForLeave;
                    this.CloseStageStartTick = currentTick;
                    return true;
                }

            case CloseStareStage.BackWaitingForLeave:
                {
                    if (distance < this.Config.BackLeaveDistance)
                        return true;

                    // 2/4 corações: não solta emote ao afastar.
                    if (this.IsLowOrFourHeartTier())
                    {
                        this.ScheduleSebastianFacingRestoreAfterEmote(currentTick);

                        if (this.Config.DebugLogs)
                            this.Monitor.Log("Back close sequence: player left, no final emote for 2/4 hearts.", LogLevel.Info);

                        this.ResetCloseStareSequence(restoreFacing: false);
                        return true;
                    }

                    sebastian.doEmote(this.Config.ShyEmoteId);

                    this.ScheduleSebastianFacingRestoreAfterEmote(currentTick);

                    if (this.Config.DebugLogs)
                        this.Monitor.Log("Back close sequence: player left, shy emote triggered, restore scheduled.", LogLevel.Info);

                    this.ResetCloseStareSequence(restoreFacing: false);
                    return true;
                }

            case CloseStareStage.BackWaitingForFinalLeaveReaction:
                {
                    if (currentTick - this.CloseStageStartTick < this.Config.FinalLeaveReactionDelayTicks)
                        return true;

                    sebastian.doEmote(this.Config.ShyEmoteId);

                    if (this.Config.DebugLogs)
                        this.Monitor.Log("Back close sequence: final shy emote triggered after restore.", LogLevel.Info);

                    this.ResetCloseStareSequence(restoreFacing: false);
                    return true;
                }

            default:
                this.ResetCloseStareSequence();
                return false;
        }
    }
    private void ResetCloseStareSequence(bool restoreFacing = true)
    {
        if (restoreFacing)
        {
            NPC? sebastian = Game1.getCharacterFromName("Sebastian");

            if (sebastian is not null)
                this.RestoreSebastianFacingIfNeeded(sebastian);
        }

        this.PendingSebastianClickableDialogue = null;
        this.PendingSebastianClickableDialogueIsPc = false;
        this.ClearSebastianPcAnimationRestoreState();

        this.CloseStage = CloseStareStage.None;
        this.CloseScenario = CloseStareScenario.None;
        this.CloseStageStartTick = 0;

        this.CurrentBackSequenceKind = BackCloseSequenceKind.None;

        this.CurrentSideSequenceKind = SideCloseSequenceKind.None;
        this.SideSecondStareTicks = 0;

        this.CurrentFrontSequenceKind = FrontCloseSequenceKind.None;
        this.FrontLowHeartSecondStareTicks = 0;
        this.FrontLowHeartJumped = false;

        this.CurrentPcSideSequenceKind = PcSideSequenceKind.None;
    }
}