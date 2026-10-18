using Microsoft.Xna.Framework;
using StardewModdingAPI;
using StardewModdingAPI.Events;
using StardewValley;

namespace LookingAtSebastian;

public sealed partial class ModEntry
{
    private const string OutfitReactionActiveModDataKey = "NatrollEXE.OutfitReactions/ReactionActive";

    private void OnWarped(object? sender, WarpedEventArgs e)
    {
        if (!e.IsLocalPlayer)
            return;

        // Uma reação iniciada em um mapa não pode continuar depois que a jogadora
        // sai dele. Principalmente no quarto, isso evita reaplicar uma pose antiga
        // do Sebastian quando o mapa é carregado novamente.
        this.ResetCloseStareSequence(restoreFacing: false);
        this.ClearSebastianFacingRestoreState();
        this.ClearSebastianSpecialAnimationRestoreState();

        this.PlayerStillTicks = 0;
        this.LastPlayerPosition = Game1.player.Position;
        this.LastDistanceToSebastian = float.MaxValue;
    }

    private void OnUpdateTicked(object? sender, UpdateTickedEventArgs e)
    {
        if (!Context.IsWorldReady || !this.Config.Enabled)
            return;

        // Checa 4 vezes por segundo, em vez de todo frame.
        if (!e.IsMultipleOf(15))
            return;

        long currentTick = (long)e.Ticks;

        this.TryRunPendingSebastianFacingRestore(currentTick);

        bool sequenceActive = this.CloseStage != CloseStareStage.None;

        // Se uma sequência está ativa, precisamos continuar atualizando mesmo com caixa de diálogo aberta.
        if (!Context.IsPlayerFree && !sequenceActive)
            return;

        Farmer player = Game1.player;

        if (!sequenceActive)
        {
            this.UpdatePlayerStillTicks(player);
        }
        else
        {
            // Enquanto uma sequência está rodando, não deixa o contador de encarada
            // continuar acumulando por trás do diálogo.
            this.LastPlayerPosition = player.Position;
        }

        NPC? sebastian = Game1.getCharacterFromName("Sebastian");

        if (sebastian is null || sebastian.currentLocation != Game1.currentLocation)
        {
            this.LastDistanceToSebastian = float.MaxValue;
            return;
        }

        // Outfit Reactions owns the temporary pose while it is showing an outfit
        // reaction. Do not start, maintain, or restore a looking sequence over it.
        if (Game1.player.modData.ContainsKey(OutfitReactionActiveModDataKey))
        {
            this.ResetCloseStareSequence(restoreFacing: false);
            this.ClearSebastianFacingRestoreState();
            this.ClearSebastianSpecialAnimationRestoreState();
            this.LastDistanceToSebastian = Vector2.Distance(player.Position, sebastian.Position);
            return;
        }

        // Se o diálogo especial do PC fez o jogo interromper a pose/animação,
        // restaura sem mexer em controller/schedule.
        this.MaintainSebastianPcAnimationIfNeeded(sebastian);

        if (!this.CanSebastianReact(sebastian))
        {
            this.LastDistanceToSebastian = Vector2.Distance(player.Position, sebastian.Position);
            return;
        }

        float distance = Vector2.Distance(player.Position, sebastian.Position);

        // Se já existe uma sequência ativa, ela tem prioridade.
        if (sequenceActive)
        {
            this.TryHandleCloseStare(sebastian, player, distance, currentTick);
            this.LastDistanceToSebastian = distance;
            return;
        }

        bool triggeredApproach = this.TryHandleApproachReaction(sebastian, player, distance, currentTick);

        if (!triggeredApproach)
        {
            bool triggeredClose = this.TryHandleCloseStare(sebastian, player, distance, currentTick);

            if (!triggeredClose)
                this.TryHandleFarStare(sebastian, player, distance, currentTick);
        }

        this.LastDistanceToSebastian = distance;
    }
    private void UpdatePlayerStillTicks(Farmer player)
    {
        float moved = Vector2.Distance(player.Position, this.LastPlayerPosition);

        if (moved <= 1f)
            this.PlayerStillTicks += 15;
        else
            this.PlayerStillTicks = 0;

        this.LastPlayerPosition = player.Position;
    }
    private bool CanSebastianReact(NPC sebastian)
    {
        if (Game1.eventUp)
            return false;

        if (Game1.player.friendshipData is null)
            return false;

        if (!Game1.player.friendshipData.TryGetValue("Sebastian", out Friendship friendship))
            return false;

        int requiredPoints = this.Config.MinHearts * 250;

        if (friendship.Points < requiredPoints)
            return false;

        // Pode reagir antes do namoro e durante o namoro.
        // Mas bloqueia se já estiver noivo/casado.
        if (friendship.IsEngaged() || friendship.IsMarried())
            return false;

        return true;
    }
    private bool TryHandleFarStare(NPC sebastian, Farmer player, float distance, long currentTick)

    {
        if (this.IsSebastianAtComputer(sebastian))
            return false;

        // Evita que o sistema antigo de "longe" atrapalhe a sequência especial
        // de frente com 2/4 corações.
        if (this.GetFrontCloseSequenceKind() != FrontCloseSequenceKind.None)
        {
            CloseStareScenario scenario = this.GetSebastianStareScenario(sebastian, player);

            if (
                scenario == CloseStareScenario.Front &&
                distance <= this.Config.FrontBalloonMaxDistance &&
                this.IsPlayerFacingTarget(player, sebastian.Position)
            )
            {
                return false;
            }
        }

        if (distance < this.Config.FarStareMinDistance || distance > this.Config.FarStareMaxDistance)
            return false;

        if (this.PlayerStillTicks < this.Config.FarRequiredStillTicks)
            return false;

        if (!this.IsPlayerFacingTarget(player, sebastian.Position))
            return false;

        if (!this.IsCooldownReady(currentTick, this.LastFarTriggerTick, this.Config.FarCooldownSeconds))
            return false;

        this.LastFarTriggerTick = currentTick;

        if (!this.RollChance(this.Config.FarEmoteChance))
            return false;

        this.TriggerReaction(
            sebastian,
            "preDatingFarShy.Sebastian",
            this.Config.FarDialogueCount,
            this.Config.FarDialogueChance,
            "far stare"
        );

        return true;
    }
    private bool TryHandleApproachReaction(NPC sebastian, Farmer player, float distance, long currentTick)
    {
        if (this.IsSebastianAtComputer(sebastian))
            return false;

        bool justEnteredRange =
            this.LastDistanceToSebastian > this.Config.ApproachResetDistance &&
            distance <= this.Config.ApproachDistance;     

        if (!justEnteredRange)
            return false;

        CloseStareScenario scenario = this.GetSebastianStareScenario(sebastian, player);

        // Se Sebastian está de costas para a jogadora, ele não viu ela passar.
        if (scenario == CloseStareScenario.Back)
            return false;

        if (this.Config.ApproachRequiresPlayerFacingSebastian && !this.IsPlayerFacingTarget(player, sebastian.Position))
            return false;

        if (!this.IsCooldownReady(currentTick, this.LastApproachTriggerTick, this.Config.ApproachCooldownSeconds))
            return false;

        this.LastApproachTriggerTick = currentTick;

        if (!this.RollChance(this.Config.ApproachEmoteChance))
            return false;

        this.TriggerReaction(
            sebastian,
            "preDatingApproach.Sebastian",
            this.Config.ApproachDialogueCount,
            this.Config.ApproachDialogueChance,
            "approach"
        );

        return true;
    }
    private void TriggerReaction(NPC sebastian, string dialoguePrefix, int dialogueCount, int dialogueChance, string debugName)
    {
        sebastian.doEmote(this.Config.ShyEmoteId);

        if (this.Config.DebugLogs)
            this.Monitor.Log($"Sebastian reaction triggered: {debugName}", LogLevel.Info);

        if (!this.RollChance(dialogueChance))
            return;

        string? line = this.GetRandomDialogue(dialoguePrefix, dialogueCount);

        if (string.IsNullOrWhiteSpace(line))
            return;

        sebastian.showTextAboveHead(line);
    }
}
