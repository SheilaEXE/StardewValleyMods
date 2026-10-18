using Microsoft.Xna.Framework;
using StardewModdingAPI;
using StardewValley;
using StardewValley.Pathfinding;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace OutfitReactions
{
    /// <summary>Mutable progress for one close-partner outfit reaction.</summary>
    internal sealed class SpouseOutfitReactionProgressState
    {
        public bool IsReacting { get; set; }
        public int InteractionCooldown { get; set; }
        public bool PathStarted { get; set; }
        public bool ComplimentReady { get; set; }
        public Point PreferredOffset { get; set; } = Point.Zero;
        public Point LastPlayerTile { get; set; } = Point.Zero;
        public Point LastTargetTile { get; set; } = Point.Zero;
        public bool FirstNoticeDone { get; set; }
        public bool EmoteFired { get; set; }
        public int NoticePauseTimer { get; set; }
        public bool PlayerWasInNoticeRange { get; set; }
        public int SecondNoticeCooldown { get; set; }
        public int ChaseTimer { get; set; }
        public NPC ReactingNpc { get; set; }
        public bool SequenceActive { get; set; }

        public void AdvanceTimers()
        {
            if (NoticePauseTimer > 0)
                NoticePauseTimer--;
            if (ChaseTimer > 0)
                ChaseTimer--;
            if (SecondNoticeCooldown > 0)
                SecondNoticeCooldown--;
            if (InteractionCooldown > 0)
                InteractionCooldown--;
        }

        // Matches ResetClothesReactionState: cooldowns for interaction and second notice are retained.
        public void ClearCurrentReaction()
        {
            IsReacting = false;
            PathStarted = false;
            ComplimentReady = false;
            PreferredOffset = Point.Zero;
            LastPlayerTile = Point.Zero;
            LastTargetTile = Point.Zero;
            FirstNoticeDone = false;
            EmoteFired = false;
            NoticePauseTimer = 0;
            PlayerWasInNoticeRange = false;
            ChaseTimer = 0;
            ReactingNpc = null;
            SequenceActive = false;
        }

        // Matches ResetClothesState. ChaseTimer is intentionally retained because the prior
        // behavior did not clear it in this broader reset path.
        public void ClearAllProgress()
        {
            IsReacting = false;
            PathStarted = false;
            ComplimentReady = false;
            FirstNoticeDone = false;
            EmoteFired = false;
            NoticePauseTimer = 0;
            SecondNoticeCooldown = 0;
            PlayerWasInNoticeRange = false;
            InteractionCooldown = 0;
            PreferredOffset = Point.Zero;
            LastPlayerTile = Point.Zero;
            LastTargetTile = Point.Zero;
            ReactingNpc = null;
            SequenceActive = false;
        }

        public void BeginFirstNotice()
        {
            SequenceActive = true;
            FirstNoticeDone = true;
            NoticePauseTimer = 90;
        }

        public void BeginApproach(NPC npc)
        {
            SequenceActive = true;
            IsReacting = true;
            PathStarted = false;
            ComplimentReady = false;
            InteractionCooldown = 180;
            PreferredOffset = Point.Zero;
            LastPlayerTile = Point.Zero;
            LastTargetTile = Point.Zero;
            ChaseTimer = 420;
            ReactingNpc = npc;
        }

        public void BeginClickReady(NPC npc)
        {
            SequenceActive = true;
            IsReacting = true;
            PathStarted = false;
            ComplimentReady = true;
            InteractionCooldown = 180;
            PreferredOffset = Point.Zero;
            LastPlayerTile = Point.Zero;
            LastTargetTile = Point.Zero;
            ChaseTimer = 0;
            ReactingNpc = npc;
        }

        public void KeepPendingAfterAiFailure(NPC npc)
        {
            SequenceActive = true;
            IsReacting = true;
            ComplimentReady = true;
            PathStarted = false;
            FirstNoticeDone = true;
            NoticePauseTimer = 0;
            SecondNoticeCooldown = Math.Max(SecondNoticeCooldown, 300);
            ChaseTimer = 0;
            ReactingNpc = npc;
        }

        public void MarkComplimentStarted(NPC npc, bool playerWasInNoticeRange)
        {
            SequenceActive = true;
            IsReacting = true;
            PathStarted = true;
            ComplimentReady = true;
            ChaseTimer = 0;
            ReactingNpc = npc;
            PlayerWasInNoticeRange = playerWasInNoticeRange;
        }
    }

    /// <summary>
    /// Snapshot of an NPC's special-action state while a spouse outfit reaction temporarily
    /// takes control of the interaction. Kept outside ModEntry so the eventual spouse-reaction
    /// controller owns its state instead of the global mod entry point.
    /// </summary>
    internal sealed class SpouseOutfitSpecialActionSnapshot
    {
        public NPC Npc { get; set; }
        public GameLocation Location { get; set; }
        public int FacingDirection { get; set; }
        public int CurrentFrame { get; set; }
        public bool Flip { get; set; }
        public int MovementPause { get; set; }
        public int AddedSpeed { get; set; }
        public List<FarmerSprite.AnimationFrame> CurrentAnimation { get; set; }
    }

    /// <summary>Owns the temporary special-action snapshot for the active spouse reaction.</summary>
    internal sealed class SpouseSpecialActionController
    {
        public SpouseOutfitSpecialActionSnapshot Current { get; private set; }

        public bool HasSnapshotFor(NPC npc) => Current != null && Current.Npc == npc;
        public void Capture(SpouseOutfitSpecialActionSnapshot snapshot) => Current = snapshot;
        public void Clear() => Current = null;

        public bool TryRestore(
            bool force,
            Farmer player,
            bool menuOpen,
            bool dialogueUp,
            Func<NPC, float> distanceToPlayer,
            float restoreDistance,
            IMonitor monitor,
            bool debugLog)
        {
            SpouseOutfitSpecialActionSnapshot snapshot = Current;
            if (snapshot == null || snapshot.Npc == null)
                return false;

            NPC npc = snapshot.Npc;
            if (npc.Sprite == null || npc.currentLocation == null || npc.currentLocation != snapshot.Location)
            {
                Clear();
                return false;
            }

            if (!force)
            {
                if (menuOpen || dialogueUp)
                    return false;

                if (player != null && npc.currentLocation == player.currentLocation && distanceToPlayer(npc) < restoreDistance)
                    return false;
            }

            try
            {
                npc.FacingDirection = snapshot.FacingDirection;
                npc.flip = snapshot.Flip;
                npc.movementPause = snapshot.MovementPause;
                npc.addedSpeed = snapshot.AddedSpeed;

                if (snapshot.CurrentAnimation != null && snapshot.CurrentAnimation.Count > 0)
                {
                    npc.Sprite.CurrentAnimation = new List<FarmerSprite.AnimationFrame>(snapshot.CurrentAnimation);
                    TrySetSpritePrivateField(npc.Sprite, "currentAnimationIndex", 0);
                    TrySetSpritePrivateField(npc.Sprite, "timer", 0);
                }
                else
                {
                    npc.Sprite.StopAnimation();
                    npc.Sprite.ClearAnimation();
                    npc.Sprite.CurrentAnimation = null;
                }

                npc.Sprite.CurrentFrame = snapshot.CurrentFrame;
                npc.Sprite.UpdateSourceRect();

                if (debugLog) monitor.Log($"[CLOTHES SPOUSE] Restored special animation for {npc.Name} after outfit reaction. frame={snapshot.CurrentFrame} anim={(snapshot.CurrentAnimation != null ? snapshot.CurrentAnimation.Count : 0)}", LogLevel.Info);
                Clear();
                return true;
            }
            catch (Exception ex)
            {
                monitor.Log($"[CLOTHES SPOUSE] Could not restore special animation for {npc?.Name ?? "null"}: {ex.Message}", LogLevel.Warn);
                Clear();
                return false;
            }
        }

        private static void TrySetSpritePrivateField(object target, string fieldName, object value)
        {
            if (target == null || string.IsNullOrWhiteSpace(fieldName))
                return;

            try
            {
                FieldInfo field = target.GetType().GetField(fieldName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                field?.SetValue(target, value);
            }
            catch
            {
                // Optional internal field.
            }
        }
    }

    /// <summary>Temporary route data captured while an outfit reaction pauses a spouse.</summary>
    internal sealed class SpouseRouteSnapshot
    {
        public Point? FinalDestination { get; set; }
        public PathFindController.endBehavior EndBehavior { get; set; }
        public int FinalFacingDirection { get; set; } = -1;
        public SchedulePathDescription Directions { get; set; }

        public void Clear()
        {
            FinalDestination = null;
            EndBehavior = null;
            FinalFacingDirection = -1;
            Directions = null;
        }
    }

    /// <summary>Owns the interruption and restoration of the spouse's active route.</summary>
    internal sealed class SpouseRouteController
    {
        private static readonly FieldInfo DirectionsToNewLocationField =
            typeof(NPC).GetField("directionsToNewLocation", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);

        public SpouseRouteSnapshot Snapshot { get; } = new();
        public bool HasRoute => Snapshot.FinalDestination != null;

        public void Stop(NPC npc, IMonitor monitor, bool debugLog)
        {
            if (npc == null)
                return;

            if (!HasRoute)
            {
                try
                {
                    if (npc.controller != null && npc.controller.pathToEndPoint is Stack<Point> path && path.Count > 0)
                    {
                        Point finalTile = path.Last();
                        Snapshot.FinalDestination = finalTile;
                        Snapshot.EndBehavior = npc.controller.endBehaviorFunction;
                        Snapshot.FinalFacingDirection = npc.controller.finalFacingDirection;
                        Snapshot.Directions = GetDirections(npc);
                        if (debugLog) monitor.Log($"[CLOTHES SPOUSE] Captured destination {finalTile} for {npc.Name}.", LogLevel.Info);
                    }
                }
                catch (Exception ex)
                {
                    if (debugLog) monitor.Log($"[CLOTHES SPOUSE] Could not capture destination for {npc.Name}: {ex.Message}", LogLevel.Info);
                }
            }

            npc.controller = null;
            npc.Halt();
            npc.Sprite.StopAnimation();
        }

        public void Restore(NPC npc, IMonitor monitor, bool debugLog)
        {
            if (npc == null || !HasRoute)
            {
                npc?.checkSchedule(Game1.timeOfDay);
                Clear();
                return;
            }

            try
            {
                Point destination = Snapshot.FinalDestination.Value;
                var restoredController = new PathFindController(npc, Utility.getGameLocationOfCharacter(npc), destination, Snapshot.FinalFacingDirection, Snapshot.EndBehavior);
                if (restoredController.pathToEndPoint != null && restoredController.pathToEndPoint.Count > 0)
                {
                    restoredController.endBehaviorFunction = Snapshot.EndBehavior;
                    npc.controller = restoredController;
                    if (Snapshot.Directions != null)
                        SetDirections(npc, Snapshot.Directions);
                    if (debugLog) monitor.Log($"[CLOTHES SPOUSE] Restored {npc.Name}'s path to {destination} ({restoredController.pathToEndPoint.Count} steps).", LogLevel.Info);
                }
                else
                {
                    if (debugLog) monitor.Log($"[CLOTHES SPOUSE] Could not pathfind to {destination} for {npc.Name} — falling back to checkSchedule.", LogLevel.Info);
                    npc.checkSchedule(Game1.timeOfDay);
                }
            }
            catch (Exception ex)
            {
                if (debugLog) monitor.Log($"[CLOTHES SPOUSE] Error restoring path for {npc.Name}: {ex.Message} — falling back to checkSchedule.", LogLevel.Info);
                npc.checkSchedule(Game1.timeOfDay);
            }
            finally
            {
                Clear();
            }
        }

        public void Clear() => Snapshot.Clear();

        private static SchedulePathDescription GetDirections(NPC npc)
        {
            try { return DirectionsToNewLocationField?.GetValue(npc) as SchedulePathDescription; }
            catch { return null; }
        }

        private static void SetDirections(NPC npc, SchedulePathDescription value)
        {
            try { DirectionsToNewLocationField?.SetValue(npc, value); }
            catch { }
        }
    }

    /// <summary>Owns the optional pathing strategy used when a spouse can approach for an outfit reaction.</summary>
    internal sealed class SpouseOutfitApproachController
    {
        public bool ShouldApproach(NPC npc)
        {
            // Keep the current kiss-compatible behavior: notices use movementPause and never
            // replace the spouse's controller or schedule with a custom approach path.
            return false;
        }

        public bool TryStartPath(NPC npc, IMonitor monitor, bool debugLog)
        {
            if (npc == null || Game1.player == null || Game1.currentLocation == null)
                return false;

            Point playerTile = Game1.player.TilePoint;
            Point npcTile = npc.TilePoint;
            int offsetX = Math.Sign(npcTile.X - playerTile.X);
            int offsetY = Math.Sign(npcTile.Y - playerTile.Y);
            List<Point> candidates = new();

            if (Math.Abs(npcTile.X - playerTile.X) > Math.Abs(npcTile.Y - playerTile.Y))
            {
                if (offsetX != 0) candidates.Add(new Point(playerTile.X + offsetX, playerTile.Y));
                if (offsetY != 0) candidates.Add(new Point(playerTile.X, playerTile.Y + offsetY));
            }
            else
            {
                if (offsetY != 0) candidates.Add(new Point(playerTile.X, playerTile.Y + offsetY));
                if (offsetX != 0) candidates.Add(new Point(playerTile.X + offsetX, playerTile.Y));
            }

            candidates.Add(new Point(playerTile.X + 1, playerTile.Y));
            candidates.Add(new Point(playerTile.X - 1, playerTile.Y));
            candidates.Add(new Point(playerTile.X, playerTile.Y + 1));
            candidates.Add(new Point(playerTile.X, playerTile.Y - 1));
            candidates.Add(new Point(playerTile.X + 1, playerTile.Y + 1));
            candidates.Add(new Point(playerTile.X - 1, playerTile.Y + 1));
            candidates.Add(new Point(playerTile.X + 1, playerTile.Y - 1));
            candidates.Add(new Point(playerTile.X - 1, playerTile.Y - 1));

            foreach (Point target in candidates.Distinct().OrderBy(tile => Math.Abs(tile.X - npcTile.X) + Math.Abs(tile.Y - npcTile.Y)))
            {
                if (target == npcTile || !Game1.currentLocation.isTilePassable(new xTile.Dimensions.Location(target.X, target.Y), Game1.viewport))
                    continue;

                try
                {
                    var path = new PathFindController(npc, Game1.currentLocation, target, -1, false);
                    if (path?.pathToEndPoint != null && path.pathToEndPoint.Count > 0)
                    {
                        npc.controller = path;
                        if (debugLog) monitor.Log($"[CLOTHES SPOUSE] Started farmhouse approach path for {npc.Name} to {target} ({path.pathToEndPoint.Count} steps).", LogLevel.Info);
                        return true;
                    }
                }
                catch (Exception ex)
                {
                    if (debugLog) monitor.Log($"[CLOTHES SPOUSE] Failed approach path candidate {target} for {npc.Name}: {ex.Message}", LogLevel.Info);
                }
            }

            return false;
        }
    }

    /// <summary>Owns the soft notice pause and pending-dialogue bubble for a spouse reaction.</summary>
    internal sealed class SpouseOutfitNoticeController
    {
        public void UpdateHold(SpouseProximityState state, NPC npc, Farmer player, float distance, Action<NPC> captureSpecialAction)
        {
            if (npc == null || player == null || npc.currentLocation != player.currentLocation)
            {
                state.ClearNotice();
                return;
            }

            state.NoticePauseActive = SpouseOutfitReactionController.ResolveNoticePause(
                state.NoticePauseActive,
                isSameLocation: true,
                distance);

            if (!state.NoticePauseActive)
            {
                state.NoticeHoldPoseApplied = false;
                return;
            }

            captureSpecialAction(npc);
            if (npc.movementPause < 6)
                npc.movementPause = 6;

            // Do not cancel an animation supplied by another mod every tick (such as a kiss).
            if (state.NoticeHoldPoseApplied)
                return;

            npc.Sprite?.StopAnimation();
            npc.faceGeneralDirection(player.getStandingPosition(), 0, false, false);
            state.NoticeHoldPoseApplied = true;
        }

        public bool TryShowPendingBubble(
            SpouseProximityState state,
            NPC npc,
            Farmer player,
            bool force,
            bool alreadyEmoted,
            float noticeDistance,
            bool interactionBlocked,
            Func<NPC, float> distanceToPlayer)
        {
            if (npc == null || player == null || npc.currentLocation != player.currentLocation || interactionBlocked)
                return false;

            if (distanceToPlayer(npc) > noticeDistance)
                return false;

            if (!SpouseOutfitReactionController.CanShowPendingBubble(force, alreadyEmoted, state.PendingBubbleTimer))
                return false;

            npc.doEmote(40);
            state.PendingBubbleTimer = SpouseOutfitReactionController.GetPendingBubbleCooldown(force);
            return true;
        }
    }

    /// <summary>Dialogue and friendship state temporarily replaced by a spouse outfit reaction.</summary>
    internal sealed class SpouseDialogueSnapshot
    {
        public List<Dialogue> DialogueQueue { get; set; }
        public string NpcName { get; set; } = "";
        public bool FriendshipStateCaptured { get; set; }
        public bool OriginalTalkedToToday { get; set; }
        public bool ForcedTalkedToToday { get; set; }

        public void Clear()
        {
            DialogueQueue = null;
            NpcName = "";
            FriendshipStateCaptured = false;
            OriginalTalkedToToday = false;
            ForcedTalkedToToday = false;
        }
    }

    /// <summary>Owns the dialogue and friendship state temporarily replaced by an outfit reaction.</summary>
    internal sealed class SpouseDialogueController
    {
        public SpouseDialogueSnapshot Snapshot { get; } = new();
        public bool HasBackup => !string.IsNullOrWhiteSpace(Snapshot.NpcName);

        public void Capture(NPC npc, Farmer player, IMonitor monitor, bool debugLog)
        {
            Clear();
            if (npc == null)
                return;

            // Stack<T> enumerates top-to-bottom; Restore pushes bottom-to-top to preserve it.
            Snapshot.DialogueQueue = npc.CurrentDialogue?.ToList() ?? new List<Dialogue>();
            Snapshot.NpcName = npc.Name;
            TemporarilySkipFirstDailyDialogue(npc, player, monitor, debugLog);
        }

        public void TemporarilySkipFirstDailyDialogue(NPC npc, Farmer player, IMonitor monitor, bool debugLog)
        {
            if (npc == null || player == null)
                return;

            try
            {
                if (!player.friendshipData.TryGetValue(npc.Name, out Friendship friendship) || friendship == null)
                    return;

                Snapshot.FriendshipStateCaptured = true;
                Snapshot.OriginalTalkedToToday = friendship.TalkedToToday;
                if (!friendship.TalkedToToday)
                {
                    friendship.TalkedToToday = true;
                    Snapshot.ForcedTalkedToToday = true;
                    if (debugLog) monitor.Log($"[CLOTHES SPOUSE] Temporarily skipped first daily dialogue for {npc.Name} so the outfit compliment can play first.", LogLevel.Info);
                }
            }
            catch (Exception ex)
            {
                if (debugLog) monitor.Log($"[CLOTHES SPOUSE] Could not temporarily skip first daily dialogue for {npc.Name}: {ex.Message}", LogLevel.Info);
            }
        }

        public void Restore(NPC npc, Farmer player, bool restoreTalkState, bool clearCurrentDialogue, IMonitor monitor, bool debugLog)
        {
            if (npc == null || !HasBackup || !npc.Name.Equals(Snapshot.NpcName, StringComparison.OrdinalIgnoreCase))
                return;

            if (clearCurrentDialogue)
                npc.CurrentDialogue.Clear();

            if (Snapshot.DialogueQueue != null && Snapshot.DialogueQueue.Count > 0)
            {
                for (int i = Snapshot.DialogueQueue.Count - 1; i >= 0; i--)
                    npc.CurrentDialogue.Push(Snapshot.DialogueQueue[i]);
            }

            if (restoreTalkState)
                RestoreTalkedToToday(npc, player, monitor, debugLog);

            Clear();
        }

        public void Clear() => Snapshot.Clear();

        public bool TryQueueOwnAiDialogue(NPC npc, Func<NPC, bool> tryShowOwnAiDialogue, IMonitor monitor)
        {
            if (npc == null)
                return false;

            if (tryShowOwnAiDialogue(npc))
                return true;

            monitor.Log($" No AI outfit dialogue was queued for {npc.Name}. Manual JSON outfit dialogue is disabled in this AI-only build. Keeping this outfit notice pending until the player cancels by moving away.", LogLevel.Warn);
            return false;
        }

        public void RestoreNormalDialogueAfterAiFailure(
            NPC npc,
            Action<NPC> clearOutfitPrompt,
            Action<NPC> restoreDialogue,
            IMonitor monitor,
            bool debugLog)
        {
            if (npc == null)
                return;

            try
            {
                clearOutfitPrompt(npc);
                restoreDialogue(npc);
            }
            catch (Exception ex)
            {
                if (debugLog) monitor.Log($"[CLOTHES SPOUSE] Could not restore normal dialogue after failed outfit AI for {npc.Name}: {ex.Message}", LogLevel.Info);
            }
        }

        private void RestoreTalkedToToday(NPC npc, Farmer player, IMonitor monitor, bool debugLog)
        {
            if (npc == null || player == null || !Snapshot.FriendshipStateCaptured || !Snapshot.ForcedTalkedToToday)
                return;

            try
            {
                if (player.friendshipData.TryGetValue(npc.Name, out Friendship friendship) && friendship != null)
                    friendship.TalkedToToday = Snapshot.OriginalTalkedToToday;
            }
            catch (Exception ex)
            {
                if (debugLog) monitor.Log($"[CLOTHES SPOUSE] Could not restore first daily dialogue state for {npc.Name}: {ex.Message}", LogLevel.Info);
            }
        }
    }

    /// <summary>Temporary proximity, pause, and linger state for the active spouse reaction.</summary>
    internal sealed class SpouseProximityState
    {
        public const int PostOutfitLingerDelayTicks = 360;
        public const float PostOutfitLingerDistance = 600f;
        public const float NoticePauseDistance = 96f;
        public const float NoticeReleaseDistance = 300f;

        public bool LingerActive { get; set; }
        public NPC LingerNpc { get; set; }
        public int LingerTimer { get; set; }
        public bool LingerPoseApplied { get; set; }
        public bool NoticePauseActive { get; set; }
        public bool NoticeHoldPoseApplied { get; set; }
        public int PendingBubbleTimer { get; set; }

        public void ClearLinger()
        {
            LingerActive = false;
            LingerNpc = null;
            LingerTimer = 0;
            LingerPoseApplied = false;
        }

        public void ClearNotice()
        {
            NoticePauseActive = false;
            NoticeHoldPoseApplied = false;
            PendingBubbleTimer = 0;
        }
    }

    /// <summary>Owns the timing and release decision for the post-dialogue spouse linger.</summary>
    internal static class SpousePostOutfitLingerController
    {
        public static void Begin(SpouseProximityState state, NPC npc)
        {
            state.LingerActive = true;
            state.LingerNpc = npc;
            state.LingerTimer = SpouseProximityState.PostOutfitLingerDelayTicks;
            state.LingerPoseApplied = false;
        }

        public static bool TickAndShouldResume(
            SpouseProximityState state,
            bool sameLocation,
            float distance,
            bool hasCapturedSpecialAction,
            float specialActionRestoreDistance)
        {
            if (state.LingerTimer > 0)
                state.LingerTimer--;

            return hasCapturedSpecialAction
                ? (!sameLocation || distance >= specialActionRestoreDistance)
                : (!sameLocation || distance >= SpouseProximityState.PostOutfitLingerDistance || state.LingerTimer <= 0);
        }

        public static void ApplyHoldPose(SpouseProximityState state, NPC npc, Farmer player)
        {
            if (npc.movementPause < 6)
                npc.movementPause = 6;

            // The pose is applied once only, so another mod can animate this NPC while the
            // linger remains active (for example, an automatic kiss animation).
            if (state.LingerPoseApplied)
                return;

            npc.Sprite?.StopAnimation();
            npc.faceGeneralDirection(player.getStandingPosition(), 0, false, false);
            state.LingerPoseApplied = true;
        }

        public static void Clear(SpouseProximityState state) => state.ClearLinger();
    }

    /// <summary>Pure proximity decisions used by the spouse outfit-reaction flow.</summary>
    internal static class SpouseOutfitReactionController
    {
        public static bool ResolveNoticePause(bool wasPaused, bool isSameLocation, float distance)
        {
            if (!isSameLocation)
                return false;
            if (distance <= SpouseProximityState.NoticePauseDistance)
                return true;
            if (distance >= SpouseProximityState.NoticeReleaseDistance)
                return false;
            return wasPaused;
        }

        public static bool CanShowPendingBubble(bool force, bool alreadyEmoted, int bubbleTimer)
        {
            return force || (!alreadyEmoted && bubbleTimer <= 0);
        }

        public static int GetPendingBubbleCooldown(bool force) => force ? 180 : 240;
    }

    /// <summary>
    /// Coordinates the close-partner outfit-reaction lifecycle. It keeps SMAPI event handling
    /// in ModEntry while making the reaction flow's entry points explicit and testable.
    /// </summary>
    internal sealed class SpouseOutfitReactionCoordinator
    {
        private readonly SpouseOutfitReactionProgressState progressState;
        private readonly Action<NPC> updateActivePartner;
        private readonly Func<NPC, bool> shouldStartReaction;
        private readonly Action<bool> resetReaction;
        private readonly Action updatePostOutfitLinger;
        private readonly Func<NPC, bool> handleInteraction;

        public SpouseOutfitReactionCoordinator(
            SpouseOutfitReactionProgressState progressState,
            Action<NPC> updateActivePartner,
            Func<NPC, bool> shouldStartReaction,
            Action<bool> resetReaction,
            Action updatePostOutfitLinger,
            Func<NPC, bool> handleInteraction)
        {
            this.progressState = progressState;
            this.updateActivePartner = updateActivePartner;
            this.shouldStartReaction = shouldStartReaction;
            this.resetReaction = resetReaction;
            this.updatePostOutfitLinger = updatePostOutfitLinger;
            this.handleInteraction = handleInteraction;
        }

        public void AdvanceTimers() => progressState.AdvanceTimers();

        public void Update(NPC activePartner, NPC spouse, bool hasPendingOutfitChange)
        {
            if (activePartner != null)
                updateActivePartner(activePartner);
            else if (hasPendingOutfitChange && !shouldStartReaction(spouse))
                resetReaction(true);

            updatePostOutfitLinger();
        }

        public bool TryHandleInteraction(NPC npc) => handleInteraction(npc);
    }
}
