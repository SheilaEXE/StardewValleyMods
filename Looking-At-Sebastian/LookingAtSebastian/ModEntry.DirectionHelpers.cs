using Microsoft.Xna.Framework;
using StardewModdingAPI;
using StardewValley;
using System.Reflection;

namespace LookingAtSebastian;

public sealed partial class ModEntry
{
    private sealed class SebastianSpecialAnimationSnapshot
    {
        public NPC Npc = null!;
        public GameLocation Location = null!;
        public int FacingDirection;
        public int CurrentFrame;
        public bool Flip;
        public int MovementPause;
        public int AddedSpeed;
        public List<FarmerSprite.AnimationFrame>? CurrentAnimation;
    }

    private void SaveSebastianFacingForSequence(NPC sebastian)
    {
        // No PC a gente não vira o Sebastian, então não precisa salvar/restaurar direção.
        if (this.IsSebastianAtComputer(sebastian))
            return;

        if (this.ShouldRestoreSebastianFacing)
            return;

        this.SavedSebastianFacingDirection = sebastian.FacingDirection;
        this.ShouldRestoreSebastianFacing = true;

        this.SaveSebastianSpecialAnimationForSequenceIfNeeded(sebastian);

        if (this.Config.DebugLogs)
            this.Monitor.Log($"Saved Sebastian ORIGINAL facing direction: {this.SavedSebastianFacingDirection}", LogLevel.Info);
    }

    private bool CanTemporarilyInterruptSpecialAnimation(NPC sebastian)
    {
        if (!this.Config.RestoreSpecialAnimationsAfterReaction)
            return false;

        if (this.IsSebastianAtComputer(sebastian))
            return false;

        if (sebastian.isMoving())
            return false;

        if (this.SavedSebastianSpriteState is not null && this.SavedSebastianSpriteState.Npc == sebastian)
            return true;

        // Geralmente as animações especiais de schedule ficam com controller ativo.
        if (sebastian.controller is null)
            return false;

        return this.HasActiveSpriteAnimation(sebastian) || sebastian.Sprite.CurrentFrame >= 16;
    }

    private bool CanContinueOrTemporarilyInterruptSebastian(NPC sebastian)
    {
        return this.CanSafelyTurnSebastian(sebastian) || this.CanTemporarilyInterruptSpecialAnimation(sebastian);
    }

    private int TryGetAnimationFrameIndex(FarmerSprite.AnimationFrame frame)
    {
        try
        {
            object boxed = frame;

            FieldInfo? field = boxed.GetType().GetField("frame", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (field is not null && field.GetValue(boxed) is int fieldValue)
                return fieldValue;

            PropertyInfo? property = boxed.GetType().GetProperty("Frame", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (property is not null && property.GetValue(boxed) is int propertyValue)
                return propertyValue;
        }
        catch
        {
            // Campo interno opcional.
        }

        return -1;
    }

    private bool AnimationLooksLikeSpecialAction(List<FarmerSprite.AnimationFrame>? animation)
    {
        if (animation is null || animation.Count <= 0)
            return false;

        foreach (FarmerSprite.AnimationFrame frame in animation)
        {
            int frameIndex = this.TryGetAnimationFrameIndex(frame);
            if (frameIndex >= 16)
                return true;
        }

        // Fallback: se tem lista de animação ativa e o NPC está parado com controller,
        // tratamos como ação especial de schedule.
        return true;
    }

    private bool HasActiveSpriteAnimation(NPC sebastian)
    {
        return sebastian.Sprite?.CurrentAnimation is not null && sebastian.Sprite.CurrentAnimation.Count > 0;
    }

    private bool IsSebastianInActiveComputerPose(NPC sebastian)
    {
        if (!this.IsSebastianAtComputer(sebastian) || sebastian.Sprite is null)
            return false;

        // As poses de ações da agenda (incluindo trabalhar sentado no PC) usam
        // os frames especiais do NPC. Um frame direcional comum, como o 4 visto
        // no log do bug, é apenas a pose em pé e não pode virar nosso snapshot.
        if (sebastian.Sprite.CurrentFrame >= 16)
            return true;

        List<FarmerSprite.AnimationFrame>? animation = sebastian.Sprite.CurrentAnimation;
        if (animation is null || animation.Count <= 0)
            return false;

        foreach (FarmerSprite.AnimationFrame frame in animation)
        {
            if (this.TryGetAnimationFrameIndex(frame) >= 16)
                return true;
        }

        return false;
    }


    private void SaveSebastianPcAnimationForSequenceIfNeeded(NPC sebastian)
    {
        if (!this.IsSebastianInActiveComputerPose(sebastian))
            return;

        if (this.SavedSebastianPcSpriteState is not null)
            return;

        if (sebastian.Sprite is null || sebastian.currentLocation is null)
            return;

        List<FarmerSprite.AnimationFrame>? animation = null;
        if (sebastian.Sprite.CurrentAnimation is not null && sebastian.Sprite.CurrentAnimation.Count > 0)
            animation = new List<FarmerSprite.AnimationFrame>(sebastian.Sprite.CurrentAnimation);

        this.SavedSebastianPcSpriteState = new SebastianSpecialAnimationSnapshot
        {
            Npc = sebastian,
            Location = sebastian.currentLocation,
            FacingDirection = sebastian.FacingDirection,
            CurrentFrame = sebastian.Sprite.CurrentFrame,
            Flip = sebastian.flip,
            MovementPause = (int)sebastian.movementPause,
            AddedSpeed = (int)sebastian.addedSpeed,
            CurrentAnimation = animation
        };

        if (this.Config.DebugLogs)
        {
            this.Monitor.Log(
                $"[PC ANIMATION SAVED] frame={sebastian.Sprite.CurrentFrame} anim={(animation is not null ? animation.Count : 0)}",
                LogLevel.Info
            );
        }
    }

    private void MaintainSebastianPcAnimationIfNeeded(NPC sebastian)
    {
        SebastianSpecialAnimationSnapshot? snapshot = this.SavedSebastianPcSpriteState;

        if (snapshot is null)
            return;

        if (!this.IsSebastianAtComputer(sebastian))
        {
            this.ClearSebastianPcAnimationRestoreState();
            return;
        }

        if (sebastian.Sprite is null || sebastian.currentLocation is null || sebastian.currentLocation != snapshot.Location)
        {
            this.ClearSebastianPcAnimationRestoreState();
            return;
        }

        // Se a animação do PC ainda está ativa, não mexe nela. Isso evita reiniciar
        // o ciclo e congelar no primeiro frame.
        if (sebastian.Sprite.CurrentAnimation is not null && sebastian.Sprite.CurrentAnimation.Count > 0)
            return;

        // Snapshot de pose estática já aplicado: não tente restaurá-lo de novo a
        // cada checagem. Isso também evita o spam de restore observado no log.
        if (
            (snapshot.CurrentAnimation is null || snapshot.CurrentAnimation.Count <= 0) &&
            sebastian.Sprite.CurrentFrame == snapshot.CurrentFrame &&
            sebastian.FacingDirection == snapshot.FacingDirection &&
            sebastian.flip == snapshot.Flip
        )
        {
            return;
        }

        this.RestoreSebastianPcAnimationSnapshot(sebastian);
    }

    private void RestoreSebastianPcAnimationSnapshot(NPC sebastian)
    {
        SebastianSpecialAnimationSnapshot? snapshot = this.SavedSebastianPcSpriteState;
        if (snapshot is null || snapshot.Npc is null || sebastian.Sprite is null)
            return;

        try
        {
            this.ClearVanillaFacePlayerLock(sebastian);

            // No PC, não chama faceDirection(), porque esse método pode trocar a pose
            // padrão e parecer que ele levantou. Só restaura os dados visuais salvos.
            sebastian.FacingDirection = snapshot.FacingDirection;
            sebastian.flip = snapshot.Flip;
            sebastian.movementPause = snapshot.MovementPause;
            sebastian.addedSpeed = snapshot.AddedSpeed;

            if (snapshot.CurrentAnimation is not null && snapshot.CurrentAnimation.Count > 0)
            {
                sebastian.Sprite.CurrentAnimation = new List<FarmerSprite.AnimationFrame>(snapshot.CurrentAnimation);
                this.TrySetSpritePrivateField(sebastian.Sprite, "currentAnimationIndex", 0);
                this.TrySetSpritePrivateField(sebastian.Sprite, "timer", 0);
            }
            else
            {
                sebastian.Sprite.CurrentAnimation = null;
                sebastian.Sprite.CurrentFrame = snapshot.CurrentFrame;
            }

            sebastian.Sprite.CurrentFrame = snapshot.CurrentFrame;
            sebastian.Sprite.UpdateSourceRect();
            this.ClearVanillaFacePlayerLock(sebastian);

            if (this.Config.DebugLogs)
            {
                this.Monitor.Log(
                    $"[PC ANIMATION RESTORED] frame={snapshot.CurrentFrame} anim={(snapshot.CurrentAnimation is not null ? snapshot.CurrentAnimation.Count : 0)}",
                    LogLevel.Info
                );
            }
        }
        catch (Exception ex)
        {
            this.Monitor.Log($"[PC ANIMATION RESTORE] Falha ao restaurar animação do PC: {ex.Message}", LogLevel.Warn);
        }
    }

    private void ClearSebastianPcAnimationRestoreState()
    {
        this.SavedSebastianPcSpriteState = null;
    }

    private void SaveSebastianSpecialAnimationForSequenceIfNeeded(NPC sebastian)
    {
        if (!this.Config.RestoreSpecialAnimationsAfterReaction)
            return;

        if (this.SavedSebastianSpriteState is not null)
            return;

        if (sebastian is null || sebastian.Sprite is null || sebastian.currentLocation is null)
            return;

        if (sebastian.isMoving())
            return;

        List<FarmerSprite.AnimationFrame>? animation = null;
        if (sebastian.Sprite.CurrentAnimation is not null && sebastian.Sprite.CurrentAnimation.Count > 0)
            animation = new List<FarmerSprite.AnimationFrame>(sebastian.Sprite.CurrentAnimation);

        bool hasSpecialAnimation = this.AnimationLooksLikeSpecialAction(animation);
        bool hasSpecialStaticFrame = sebastian.Sprite.CurrentFrame >= 16;

        if (!hasSpecialAnimation && !hasSpecialStaticFrame)
            return;

        this.SavedSebastianSpriteState = new SebastianSpecialAnimationSnapshot
        {
            Npc = sebastian,
            Location = sebastian.currentLocation,
            FacingDirection = sebastian.FacingDirection,
            CurrentFrame = sebastian.Sprite.CurrentFrame,
            Flip = sebastian.flip,
            MovementPause = (int)sebastian.movementPause,
            AddedSpeed = (int)sebastian.addedSpeed,
            CurrentAnimation = animation
        };

        // Interrompe só a animação visual especial para a reação/virada aparecer.
        // Não mexe em controller, schedule nem pathfinding.
        sebastian.Sprite.StopAnimation();
        sebastian.Sprite.ClearAnimation();
        sebastian.Sprite.CurrentAnimation = null;
        sebastian.flip = false;
        sebastian.Sprite.CurrentFrame = this.GetNpcIdleFrameForDirection(sebastian.FacingDirection);
        sebastian.Sprite.UpdateSourceRect();

        if (this.Config.DebugLogs)
        {
            this.Monitor.Log(
                $"[SPECIAL ANIMATION SAVED] npc={sebastian.Name} frame={this.SavedSebastianSpriteState.CurrentFrame} anim={(animation is not null ? animation.Count : 0)}",
                LogLevel.Info
            );
        }
    }

    private void ScheduleSebastianFacingRestoreAfterEmote(long currentTick)
    {
        if (!this.ShouldRestoreSebastianFacing && this.SavedSebastianSpriteState is null)
            return;

        this.PendingFacingRestore = true;
        this.PendingFacingRestoreTick = currentTick + this.Config.RestoreFacingAfterEmoteDelayTicks;

        if (this.Config.DebugLogs)
        {
            this.Monitor.Log(
                $"Scheduled Sebastian restore. Tick={this.PendingFacingRestoreTick}, Direction={this.SavedSebastianFacingDirection}, HasAnimation={this.SavedSebastianSpriteState is not null}",
                LogLevel.Info
            );
        }
    }

    private void TryRunPendingSebastianFacingRestore(long currentTick)
    {
        if (!this.PendingFacingRestore)
            return;

        if (currentTick < this.PendingFacingRestoreTick)
            return;

        NPC? sebastian = Game1.getCharacterFromName("Sebastian");

        if (sebastian is null)
        {
            this.ClearSebastianFacingRestoreState();
            this.ClearSebastianSpecialAnimationRestoreState();
            return;
        }

        if (sebastian.isMoving())
        {
            // Se ele começou a andar, deixa a agenda/controller cuidar.
            this.ClearSebastianFacingRestoreState();
            this.ClearSebastianSpecialAnimationRestoreState();
            return;
        }

        this.ApplySavedSebastianFacing(sebastian);
        this.RestoreSebastianSpecialAnimationIfNeeded(sebastian);
        this.ClearSebastianFacingRestoreState();
    }

    private void RestoreSebastianFacingIfNeeded(NPC sebastian)
    {
        if (!this.ShouldRestoreSebastianFacing && this.SavedSebastianSpriteState is null)
            return;

        if (sebastian.isMoving())
            return;

        this.ApplySavedSebastianFacing(sebastian);
        this.RestoreSebastianSpecialAnimationIfNeeded(sebastian);
        this.ClearSebastianFacingRestoreState();
    }

    private void RestoreSebastianFacingBeforeFinalReaction(NPC sebastian)
    {
        if (!this.ShouldRestoreSebastianFacing && this.SavedSebastianSpriteState is null)
        {
            this.ClearSebastianFacingRestoreState();
            this.ClearSebastianSpecialAnimationRestoreState();
            return;
        }

        if (sebastian.isMoving())
        {
            this.ClearSebastianFacingRestoreState();
            this.ClearSebastianSpecialAnimationRestoreState();
            return;
        }

        this.ApplySavedSebastianFacing(sebastian);
        this.RestoreSebastianSpecialAnimationIfNeeded(sebastian);
        this.ClearSebastianFacingRestoreState();
    }

    private void ClearVanillaFacePlayerLock(NPC npc)
    {
        try
        {
            this.Helper.Reflection
                .GetField<int>(npc, "faceTowardFarmerTimer", required: false)
                ?.SetValue(0);
        }
        catch
        {
            // Ignora se o campo não existir nessa versão.
        }

        try
        {
            this.Helper.Reflection
                .GetField<Farmer>(npc, "faceTowardFarmer", required: false)
                ?.SetValue(null!);
        }
        catch
        {
            // Ignora se o campo não existir nessa versão.
        }
    }

    private void ApplySavedSebastianFacing(NPC sebastian)
    {
        if (this.SavedSebastianFacingDirection < 0)
            return;

        int direction = this.SavedSebastianFacingDirection;

        this.ClearVanillaFacePlayerLock(sebastian);

        sebastian.Sprite.StopAnimation();
        sebastian.faceDirection(direction);
        sebastian.Sprite.faceDirectionStandard(direction);
        sebastian.Sprite.UpdateSourceRect();

        this.ClearVanillaFacePlayerLock(sebastian);

        if (this.Config.DebugLogs)
            this.Monitor.Log($"Applying saved Sebastian facing direction after clearing vanilla lock: {direction}", LogLevel.Info);
    }

    private bool RestoreSebastianSpecialAnimationIfNeeded(NPC sebastian)
    {
        SebastianSpecialAnimationSnapshot? snapshot = this.SavedSebastianSpriteState;
        if (snapshot is null || snapshot.Npc is null)
            return false;

        NPC npc = snapshot.Npc;

        if (npc.Sprite is null || npc.currentLocation is null || npc.currentLocation != snapshot.Location)
        {
            this.ClearSebastianSpecialAnimationRestoreState();
            return false;
        }

        try
        {
            npc.FacingDirection = snapshot.FacingDirection;
            npc.flip = snapshot.Flip;
            npc.movementPause = snapshot.MovementPause;
            npc.addedSpeed = snapshot.AddedSpeed;

            if (snapshot.CurrentAnimation is not null && snapshot.CurrentAnimation.Count > 0)
            {
                npc.Sprite.CurrentAnimation = new List<FarmerSprite.AnimationFrame>(snapshot.CurrentAnimation);
                this.TrySetSpritePrivateField(npc.Sprite, "currentAnimationIndex", 0);
                this.TrySetSpritePrivateField(npc.Sprite, "timer", 0);
            }
            else
            {
                npc.Sprite.StopAnimation();
                npc.Sprite.ClearAnimation();
                npc.Sprite.CurrentAnimation = null;
            }

            npc.Sprite.CurrentFrame = snapshot.CurrentFrame;
            npc.Sprite.UpdateSourceRect();

            if (this.Config.DebugLogs)
            {
                this.Monitor.Log(
                    $"[SPECIAL ANIMATION RESTORED] npc={npc.Name} frame={snapshot.CurrentFrame} anim={(snapshot.CurrentAnimation is not null ? snapshot.CurrentAnimation.Count : 0)}",
                    LogLevel.Info
                );
            }

            this.ClearSebastianSpecialAnimationRestoreState();
            return true;
        }
        catch (Exception ex)
        {
            this.Monitor.Log($"[SPECIAL ANIMATION RESTORE] Falha ao restaurar animação de {npc?.Name ?? "null"}: {ex.Message}", LogLevel.Warn);
            this.ClearSebastianSpecialAnimationRestoreState();
            return false;
        }
    }

    private void TrySetSpritePrivateField(object target, string fieldName, object value)
    {
        if (target is null || string.IsNullOrEmpty(fieldName))
            return;

        try
        {
            FieldInfo? field = target.GetType().GetField(fieldName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (field is not null)
                field.SetValue(target, value);
        }
        catch
        {
            // Campo interno opcional.
        }
    }

    private void ClearSebastianFacingRestoreState()
    {
        this.PendingFacingRestore = false;
        this.PendingFacingRestoreTick = 0;

        this.ShouldRestoreSebastianFacing = false;
        this.SavedSebastianFacingDirection = -1;
    }

    private void ClearSebastianSpecialAnimationRestoreState()
    {
        this.SavedSebastianSpriteState = null;
    }

    private bool CanSafelyTurnSebastian(NPC sebastian)
    {
        if (sebastian.controller is not null)
            return false;

        if (sebastian.isMoving())
            return false;

        return true;
    }

    private void TryMakeSebastianLookAtPlayer(NPC sebastian)
    {
        if (!this.CanContinueOrTemporarilyInterruptSebastian(sebastian))
            return;

        if (this.CanTemporarilyInterruptSpecialAnimation(sebastian))
            this.SaveSebastianSpecialAnimationForSequenceIfNeeded(sebastian);

        int direction = this.GetDirectionFromTo(sebastian.Position, Game1.player.Position);

        sebastian.faceDirection(direction);
        sebastian.Sprite.faceDirectionStandard(direction);
        sebastian.Sprite.UpdateSourceRect();
    }

    private CloseStareScenario GetSebastianStareScenario(NPC sebastian, Farmer player)
    {
        int directionToPlayer = this.GetDirectionFromTo(sebastian.Position, player.Position);
        int sebastianDirection = sebastian.FacingDirection;

        if (sebastianDirection == directionToPlayer)
            return CloseStareScenario.Front;

        if (this.IsOppositeDirection(sebastianDirection, directionToPlayer))
            return CloseStareScenario.Back;

        return CloseStareScenario.Side;
    }

    private bool IsOppositeDirection(int a, int b)
    {
        return
            a == 0 && b == 2 ||
            a == 2 && b == 0 ||
            a == 1 && b == 3 ||
            a == 3 && b == 1;
    }

    private int GetDirectionFromTo(Vector2 from, Vector2 to)
    {
        float dx = to.X - from.X;
        float dy = to.Y - from.Y;

        if (Math.Abs(dx) > Math.Abs(dy))
            return dx > 0 ? 1 : 3; // direita / esquerda

        return dy > 0 ? 2 : 0; // baixo / cima
    }

    private int GetNpcIdleFrameForDirection(int dir)
    {
        return dir switch
        {
            0 => 8,
            1 => 4,
            2 => 0,
            3 => 12,
            _ => 0
        };
    }

    private bool IsPlayerFacingTarget(Farmer player, Vector2 targetPosition)
    {
        Vector2 playerPosition = player.Position;

        float dx = targetPosition.X - playerPosition.X;
        float dy = targetPosition.Y - playerPosition.Y;

        int neededDirection;

        if (Math.Abs(dx) > Math.Abs(dy))
            neededDirection = dx > 0 ? 1 : 3; // direita / esquerda
        else
            neededDirection = dy > 0 ? 2 : 0; // baixo / cima

        return player.FacingDirection == neededDirection;
    }

    private bool IsSebastianAtComputer(NPC sebastian)
    {
        if (!this.Config.EnablePcSideStareSequence)
            return false;

        if (sebastian.Name != "Sebastian")
            return false;

        if (sebastian.currentLocation is null)
            return false;

        if (sebastian.currentLocation.NameOrUniqueName != "SebastianRoom")
            return false;

        int tileX = (int)(sebastian.Position.X / 64f);
        int tileY = (int)(sebastian.Position.Y / 64f);

        bool isAtComputer =
            tileX >= this.Config.SebastianComputerMinX &&
            tileX <= this.Config.SebastianComputerMaxX &&
            tileY == this.Config.SebastianComputerTileY;

        if (this.Config.DebugLogs)
        {
            this.Monitor.Log(
                $"Sebastian PC check | location={sebastian.currentLocation.NameOrUniqueName}, tile=({tileX}, {tileY}), blocked={isAtComputer}",
                LogLevel.Trace
            );
        }

        return isAtComputer;
    }
}
