using Microsoft.Xna.Framework;
using StardewModdingAPI;
using StardewValley;
using StardewValley.TerrainFeatures;
using System;
using System.Collections.Generic;

namespace OutfitReactions
{
    internal enum LewisShortsSlot
    {
        None,
        Pants,
        Hat
    }

    /// <summary>
    /// Experimental chase used after Lewis reads a purple-shorts reaction. Outside ManorHouse,
    /// it only starts and continues while Lewis has no visible witnesses nearby.
    /// Movement is driven by a small private A* path and direct position updates. This class
    /// deliberately never assigns, clears, replaces, or restores an NPC controller.
    /// </summary>
    internal sealed class LewisShortsChaseController
    {
        private const string LewisName = "Lewis";
        private const string ManorHouseName = "ManorHouse";
        private const string EscapeDialogueSituation = "Escape";
        private const string ConfiscatedDialogueSituation = "Confiscated";
        private const string EscapedDialogueSituation = "Escaped";
        private const int DemandBubbleTicks = 180;
        private const int PlayerStartleDelayTicks = 60;
        private const int PathRefreshTicks = 12;
        private const int GiveUpAfterFailedPathTicks = 180;
        private const int WitnessEmoteTimeoutTicks = 240;
        private const int MaximumPathSearchNodes = 500;
        private const float ChaseSpeed = 4f;
        private const float ReturnSpeed = 2f;
        private const float CatchDistance = 52f;
        private const float WitnessDistance = 1000f;

        private readonly IMonitor monitor;
        private readonly ITranslationHelper translation;
        private readonly Func<bool> isEnabled;
        private readonly Func<LewisShortsSlot> getEquippedShortsSlot;
        private readonly Func<LewisShortsSlot, bool> confiscateShorts;
        private readonly Func<LewisShortsSlot, string, string> getDialogueKey;
        private readonly Action<NPC> markCurrentVisualAsNoticed;
        private readonly NpcSpecialActionController specialActionController;
        private readonly Random random = new();

        private ChasePhase phase;
        private NPC lewis;
        private GameLocation originLocation;
        private Vector2 originPosition;
        private Point originTile;
        private int originalFacingDirection;
        private int originalFrame;
        private bool originalFlip;
        private int originalMovementPause;
        private int originalAddedSpeed;
        private List<FarmerSprite.AnimationFrame> originalAnimation;
        private NpcOutfitSpecialActionSnapshot specialActionSnapshot;
        private LewisShortsSlot chasedSlot;
        private Queue<Point> path = new();
        private Point? pathTarget;
        private int pathRefreshTimer;
        private int failedPathTicks;
        private int demandBubbleTimer;
        private int witnessEmoteTimer;
        private bool playerStartlePlayed;
        private bool visualChangedDuringSequence;
        private bool requiresPrivacy;

        public LewisShortsChaseController(
            IMonitor monitor,
            ITranslationHelper translation,
            Func<bool> isEnabled,
            Func<LewisShortsSlot> getEquippedShortsSlot,
            Func<LewisShortsSlot, bool> confiscateShorts,
            Func<LewisShortsSlot, string, string> getDialogueKey,
            Action<NPC> markCurrentVisualAsNoticed)
        {
            this.monitor = monitor;
            this.translation = translation;
            this.isEnabled = isEnabled;
            this.getEquippedShortsSlot = getEquippedShortsSlot;
            this.confiscateShorts = confiscateShorts;
            this.getDialogueKey = getDialogueKey;
            this.markCurrentVisualAsNoticed = markCurrentVisualAsNoticed;
            specialActionController = new NpcSpecialActionController(monitor);
        }

        public bool IsActive => phase != ChasePhase.None;

        public bool IsHandling(NPC npc)
        {
            return IsActive && npc != null && ReferenceEquals(npc, lewis);
        }

        public bool TryBeginAfterReaction(NPC npc, PendingPrompt pending)
        {
            if (isEnabled?.Invoke() != true || IsActive || npc == null || pending == null || Game1.player == null)
                return false;

            if (!string.Equals(npc.Name, LewisName, StringComparison.OrdinalIgnoreCase)
                || npc.currentLocation != Game1.player.currentLocation
                || Game1.activeClickableMenu != null
                || Game1.eventUp
                || Game1.CurrentEvent != null
                || Game1.currentMinigame != null)
            {
                return false;
            }

            bool startsOutsideManorHouse = !IsManorHouse(npc.currentLocation);
            if (startsOutsideManorHouse && FindVisibleWitness(npc, requireWithinWitnessDistance: false) != null)
            {
                LogDebug("Lewis chase was not started outside ManorHouse because another NPC was visible.");
                return false;
            }

            // Reading these fields is intentional. This experimental path must never take ownership
            // from vanilla or another mod, so it only starts while nobody else has a controller.
            if (HasExternalController(npc))
            {
                LogDebug("Lewis chase was not started because an NPC controller was already active.");
                return false;
            }

            LewisShortsSlot slot = getEquippedShortsSlot?.Invoke() ?? LewisShortsSlot.None;
            if (slot == LewisShortsSlot.None)
                return false;

            lewis = npc;
            originLocation = npc.currentLocation;
            originPosition = pending.HasOriginalVisualState ? pending.OriginalPosition : npc.Position;
            originTile = Utility.Vector2ToPoint(originPosition / 64f);
            originalFacingDirection = pending.HasOriginalVisualState ? pending.OriginalFacingDirection : npc.FacingDirection;
            originalFrame = pending.HasOriginalVisualState ? pending.OriginalFrame : npc.Sprite?.CurrentFrame ?? 0;
            originalFlip = pending.HasOriginalVisualState ? pending.OriginalFlip : npc.flip;
            originalMovementPause = pending.HasOriginalVisualState ? pending.OriginalMovementPause : (int)npc.movementPause;
            originalAddedSpeed = pending.HasOriginalVisualState ? pending.OriginalAddedSpeed : (int)npc.addedSpeed;
            originalAnimation = pending.OriginalAnimation == null
                ? null
                : new List<FarmerSprite.AnimationFrame>(pending.OriginalAnimation);
            specialActionSnapshot = pending.SpecialActionSnapshot;
            pending.SpecialActionSnapshot = null;
            chasedSlot = slot;
            visualChangedDuringSequence = false;
            requiresPrivacy = startsOutsideManorHouse;

            phase = ChasePhase.DemandBubble;
            demandBubbleTimer = DemandBubbleTicks;
            playerStartlePlayed = false;
            ClearPath();
            HoldLewisStill();
            FaceTowardPlayer();

            ShowLewisDialogue(EscapeDialogueSituation, "lewis-shorts-chase.demand", "Give that back right now!");
            LogDebug($"Lewis chase armed in {originLocation.NameOrUniqueName} for slot={slot}, origin={originTile}, requiresPrivacy={requiresPrivacy}.");
            return true;
        }

        public void Update()
        {
            if (!IsActive)
                return;

            if (isEnabled?.Invoke() != true)
            {
                Reset(restoreIfPossible: true);
                return;
            }

            if (!Context.IsWorldReady || lewis == null || originLocation == null || lewis.currentLocation == null)
            {
                ClearState();
                return;
            }

            if (!ReferenceEquals(lewis.currentLocation, originLocation))
            {
                ReleaseAtCurrentPosition("Lewis changed locations while the chase was active.", allowPoseRestore: false);
                return;
            }

            if (HasExternalController(lewis))
            {
                // A new vanilla/mod controller is a newer authority than our saved pose. Do not
                // overwrite it; release our small movement pause and leave the new controller intact.
                ReleaseAtCurrentPosition("A controller became active while the chase was running.", allowPoseRestore: false);
                return;
            }

            if (requiresPrivacy
                && (phase == ChasePhase.DemandBubble || phase == ChasePhase.Chasing)
                && FindVisibleWitness(lewis, requireWithinWitnessDistance: true) is NPC witness)
            {
                BeginWitnessDisguise(witness);
                return;
            }

            switch (phase)
            {
                case ChasePhase.DemandBubble:
                    UpdateDemandBubble();
                    break;
                case ChasePhase.Chasing:
                    UpdateChasing();
                    break;
                case ChasePhase.WitnessEmote:
                    UpdateWitnessEmote();
                    break;
                case ChasePhase.Returning:
                    UpdateReturning();
                    break;
            }
        }

        public void Reset(bool restoreIfPossible)
        {
            if (!IsActive)
                return;

            if (restoreIfPossible && lewis?.currentLocation == originLocation && !HasExternalController(lewis))
                ReleaseAtCurrentPosition("The chase was reset.", allowPoseRestore: true);
            else
                ClearState();
        }

        private void UpdateDemandBubble()
        {
            if (!PlayerStillWearingChasedShorts())
            {
                visualChangedDuringSequence = true;
                BeginReturn("The player removed the shorts before the chase began.");
                return;
            }

            if (!PlayerIsInsideOriginLocation())
            {
                BeginReturn("The player left the chase location during Lewis's demand bubble.");
                return;
            }

            HoldLewisStill();
            FaceTowardPlayer();
            KeepLewisShaking();

            if (Game1.activeClickableMenu != null || Game1.dialogueUp || Game1.freezeControls)
                return;

            demandBubbleTimer--;
            if (!playerStartlePlayed && demandBubbleTimer <= DemandBubbleTicks - PlayerStartleDelayTicks)
                PlayPlayerStartle();

            if (demandBubbleTimer > 0)
                return;

            lewis.clearTextAboveHead();
            StopLewisShaking();
            phase = ChasePhase.Chasing;
            ClearPath();
            LogDebug("Lewis demand bubble finished; chase started.");
        }

        private void UpdateChasing()
        {
            if (!PlayerStillWearingChasedShorts())
            {
                visualChangedDuringSequence = true;
                BeginReturn("The player removed the shorts during the chase.");
                return;
            }

            if (!PlayerIsInsideOriginLocation())
            {
                BeginReturn("The player left the chase location; Lewis is returning to his origin.");
                return;
            }

            if (Game1.activeClickableMenu != null || Game1.dialogueUp || Game1.freezeControls || Game1.eventUp)
            {
                HoldLewisStill();
                return;
            }

            if (HasCaughtPlayer())
            {
                ConfiscateAndReturn();
                return;
            }

            Point target = Game1.player.TilePoint;
            bool moved = MoveToward(target, ChaseSpeed);
            if (!moved)
            {
                failedPathTicks++;
                if (failedPathTicks >= GiveUpAfterFailedPathTicks)
                {
                    ShowLewisDialogue(EscapedDialogueSituation);
                    BeginReturn("Lewis could not reach the player and gave up.", clearText: false);
                }
            }
            else
            {
                failedPathTicks = 0;
            }

            if (HasCaughtPlayer())
                ConfiscateAndReturn();
        }

        private void UpdateReturning()
        {
            if (Game1.activeClickableMenu != null || Game1.dialogueUp || Game1.freezeControls || Game1.eventUp)
            {
                HoldLewisStill();
                return;
            }

            if (Vector2.Distance(lewis.Position, originPosition) <= ReturnSpeed + 0.5f)
            {
                CompleteReturn();
                return;
            }

            bool moved = MoveToward(originTile, ReturnSpeed);
            if (!moved)
            {
                failedPathTicks++;
                if (failedPathTicks >= GiveUpAfterFailedPathTicks)
                    ReleaseAtCurrentPosition("Lewis could not find a route back to his origin.", allowPoseRestore: true);
            }
            else
            {
                failedPathTicks = 0;
            }

            if (lewis != null && lewis.TilePoint == originTile)
                CompleteReturn();
        }

        private void BeginWitnessDisguise(NPC witness)
        {
            if (!IsActive || phase == ChasePhase.WitnessEmote || phase == ChasePhase.Returning)
                return;

            lewis?.clearTextAboveHead();
            StopLewisShaking();
            lewis?.Halt();
            ClearPath();

            phase = ChasePhase.WitnessEmote;
            witnessEmoteTimer = WitnessEmoteTimeoutTicks;
            int emote = random.Next(2) == 0 ? 28 : 12;
            lewis?.doEmote(emote);
            HoldLewisStill();

            LogDebug($"Lewis stopped the chase to disguise it because {witness?.Name ?? "a witness"} became visible within {WitnessDistance:F0}f; emote={emote}.");
        }

        private void UpdateWitnessEmote()
        {
            HoldLewisStill();

            if (Game1.activeClickableMenu != null || Game1.dialogueUp || Game1.freezeControls || Game1.eventUp)
                return;

            witnessEmoteTimer--;
            if (lewis?.IsEmoting == true && witnessEmoteTimer > 0)
                return;

            ShowLewisDialogue(EscapedDialogueSituation);
            BeginReturn("Lewis disguised the chase after a witness appeared.", clearText: false);
        }

        private void ConfiscateAndReturn()
        {
            bool confiscated = confiscateShorts?.Invoke(chasedSlot) == true;
            visualChangedDuringSequence |= confiscated;
            if (confiscated)
            {
                ShowLewisDialogue(ConfiscatedDialogueSituation);
                BeginReturn("Lewis caught the player and confiscated the shorts.", clearText: false);
            }
            else
            {
                BeginReturn("Lewis reached the player, but the shorts were no longer equipped.");
            }
        }

        private void BeginReturn(string reason, bool clearText = true)
        {
            if (!IsActive || phase == ChasePhase.Returning)
                return;

            if (clearText)
                lewis?.clearTextAboveHead();
            StopLewisShaking();
            lewis?.Halt();
            phase = ChasePhase.Returning;
            failedPathTicks = 0;
            ClearPath();
            LogDebug(reason);

            if (lewis != null && Vector2.Distance(lewis.Position, originPosition) <= ReturnSpeed + 0.5f)
                CompleteReturn();
        }

        private void CompleteReturn()
        {
            if (lewis == null)
            {
                ClearState();
                return;
            }

            lewis.Position = originPosition;
            lewis.Halt();
            RestoreOriginalPose();
            MarkChangedVisualAsHandled();
            LogDebug("Lewis returned to his exact origin and restored his previous pose.");
            ClearState();
        }

        private void ReleaseAtCurrentPosition(string reason, bool allowPoseRestore)
        {
            if (lewis != null)
            {
                lewis.clearTextAboveHead();
                StopLewisShaking();
                if (allowPoseRestore && !HasExternalController(lewis))
                {
                    lewis.Halt();
                    RestoreOriginalPose();
                }
                else
                {
                    lewis.movementPause = originalMovementPause;
                    lewis.addedSpeed = originalAddedSpeed;
                }
            }

            MarkChangedVisualAsHandled();
            LogDebug(reason + " Lewis was safely released at his current position.");
            ClearState();
        }

        private void RestoreOriginalPose()
        {
            if (lewis?.Sprite == null)
                return;

            if (specialActionSnapshot != null)
            {
                PendingPrompt restorePrompt = new PendingPrompt { SpecialActionSnapshot = specialActionSnapshot };
                specialActionController.TryRestore(lewis, restorePrompt, force: true);
                specialActionSnapshot = null;
                return;
            }

            lewis.FacingDirection = originalFacingDirection;
            lewis.flip = originalFlip;
            lewis.movementPause = originalMovementPause;
            lewis.addedSpeed = originalAddedSpeed;

            if (originalAnimation != null && originalAnimation.Count > 0)
                lewis.Sprite.CurrentAnimation = new List<FarmerSprite.AnimationFrame>(originalAnimation);
            else
            {
                lewis.Sprite.StopAnimation();
                lewis.Sprite.ClearAnimation();
                lewis.Sprite.CurrentAnimation = null;
            }

            lewis.Sprite.CurrentFrame = originalFrame;
            lewis.Sprite.UpdateSourceRect();
        }

        private void MarkChangedVisualAsHandled()
        {
            if (visualChangedDuringSequence && lewis != null)
                markCurrentVisualAsNoticed?.Invoke(lewis);
        }

        private bool MoveToward(Point target, float speed)
        {
            if (lewis?.currentLocation == null)
                return false;

            pathRefreshTimer--;
            if (!pathTarget.HasValue || pathTarget.Value != target || path.Count == 0 || pathRefreshTimer <= 0)
            {
                Queue<Point> rebuilt = FindPath(lewis.currentLocation, lewis.TilePoint, target);
                if (rebuilt == null || rebuilt.Count == 0)
                {
                    ClearPath();
                    return lewis.TilePoint == target;
                }

                path = rebuilt;
                pathTarget = target;
                pathRefreshTimer = PathRefreshTicks;
            }

            while (path.Count > 0 && path.Peek() == lewis.TilePoint)
                path.Dequeue();

            if (path.Count == 0)
                return lewis.TilePoint == target;

            Point nextTile = path.Peek();
            Vector2 nextStandingPosition = new Vector2(nextTile.X * 64f + 32f, nextTile.Y * 64f + 32f);
            Vector2 currentStandingPosition = lewis.getStandingPosition();
            Vector2 delta = nextStandingPosition - currentStandingPosition;

            if (delta.LengthSquared() <= speed * speed)
            {
                lewis.Position += delta;
                path.Dequeue();
            }
            else
            {
                delta.Normalize();
                lewis.Position += delta * speed;
            }

            int facingDirection = Math.Abs(delta.X) > Math.Abs(delta.Y)
                ? (delta.X > 0f ? 1 : 3)
                : (delta.Y > 0f ? 2 : 0);
            lewis.faceDirection(facingDirection);
            lewis.animateInFacingDirection(Game1.currentGameTime);
            if (lewis.movementPause < 6)
                lewis.movementPause = 6;
            return true;
        }

        private Queue<Point> FindPath(GameLocation location, Point start, Point goal)
        {
            if (start == goal)
                return new Queue<Point>();

            PriorityQueue<Point, int> frontier = new PriorityQueue<Point, int>();
            Dictionary<Point, Point> cameFrom = new Dictionary<Point, Point>();
            Dictionary<Point, int> costs = new Dictionary<Point, int> { [start] = 0 };
            frontier.Enqueue(start, 0);
            int visited = 0;

            while (frontier.Count > 0 && visited++ < MaximumPathSearchNodes)
            {
                Point current = frontier.Dequeue();
                if (current == goal)
                    return ReconstructPath(cameFrom, start, goal);

                foreach (Point neighbor in GetNeighbors(current))
                {
                    if (neighbor != goal && !IsTilePassable(location, neighbor))
                        continue;

                    int newCost = costs[current] + 1;
                    if (costs.TryGetValue(neighbor, out int oldCost) && newCost >= oldCost)
                        continue;

                    costs[neighbor] = newCost;
                    cameFrom[neighbor] = current;
                    int priority = newCost + Math.Abs(goal.X - neighbor.X) + Math.Abs(goal.Y - neighbor.Y);
                    frontier.Enqueue(neighbor, priority);
                }
            }

            return null;
        }

        private static Queue<Point> ReconstructPath(Dictionary<Point, Point> cameFrom, Point start, Point goal)
        {
            List<Point> reversed = new List<Point>();
            Point current = goal;
            while (current != start)
            {
                reversed.Add(current);
                if (!cameFrom.TryGetValue(current, out current))
                    return null;
            }

            reversed.Reverse();
            return new Queue<Point>(reversed);
        }

        private bool IsTilePassable(GameLocation location, Point tile)
        {
            if (location?.Map?.Layers == null || location.Map.Layers.Count == 0)
                return false;

            int width = location.Map.Layers[0].LayerWidth;
            int height = location.Map.Layers[0].LayerHeight;
            if (tile.X < 0 || tile.Y < 0 || tile.X >= width || tile.Y >= height)
                return false;

            if (!location.isTilePassable(new xTile.Dimensions.Location(tile.X, tile.Y), Game1.viewport))
                return false;

            Vector2 tileVector = new Vector2(tile.X, tile.Y);
            if (location.objects.TryGetValue(tileVector, out StardewValley.Object mapObject) && !mapObject.isPassable())
                return false;

            if (location.terrainFeatures.TryGetValue(tileVector, out TerrainFeature feature) && !feature.isPassable(lewis))
                return false;

            return true;
        }

        private static IEnumerable<Point> GetNeighbors(Point tile)
        {
            yield return new Point(tile.X, tile.Y - 1);
            yield return new Point(tile.X + 1, tile.Y);
            yield return new Point(tile.X, tile.Y + 1);
            yield return new Point(tile.X - 1, tile.Y);
        }

        private bool HasCaughtPlayer()
        {
            if (lewis == null || Game1.player == null || !PlayerIsInsideOriginLocation())
                return false;

            Rectangle playerBox = Game1.player.GetBoundingBox();
            playerBox.Inflate(12, 12);
            return lewis.GetBoundingBox().Intersects(playerBox)
                || Vector2.Distance(lewis.getStandingPosition(), Game1.player.getStandingPosition()) <= CatchDistance;
        }

        private bool PlayerStillWearingChasedShorts()
        {
            return chasedSlot != LewisShortsSlot.None
                && (getEquippedShortsSlot?.Invoke() ?? LewisShortsSlot.None) == chasedSlot;
        }

        private bool PlayerIsInsideOriginLocation()
        {
            return Game1.player != null && ReferenceEquals(Game1.player.currentLocation, originLocation);
        }

        private void HoldLewisStill()
        {
            if (lewis == null)
                return;

            lewis.Halt();
            if (lewis.movementPause < 6)
                lewis.movementPause = 6;
        }

        private void FaceTowardPlayer()
        {
            if (lewis == null || Game1.player == null || !PlayerIsInsideOriginLocation())
                return;

            Vector2 delta = Game1.player.getStandingPosition() - lewis.getStandingPosition();
            int direction = Math.Abs(delta.X) > Math.Abs(delta.Y)
                ? (delta.X > 0f ? 1 : 3)
                : (delta.Y > 0f ? 2 : 0);
            lewis.faceDirection(direction);
        }

        private void KeepLewisShaking()
        {
            // A short timer refreshed every update keeps the native NPC shake aligned with
            // the demand phase, including when that phase is paused by a menu.
            lewis?.shake(100);
        }

        private void StopLewisShaking()
        {
            if (lewis != null)
                lewis.shakeTimer = 0;
        }

        private void PlayPlayerStartle()
        {
            playerStartlePlayed = true;

            Farmer player = Game1.player;
            if (player == null || !PlayerIsInsideOriginLocation())
                return;

            // Farmer.jump is the game's native startled hop. It changes only the vertical
            // jump state and sprite pose, so the player remains free to run before the chase.
            player.jump();
            LogDebug("The player performed the startled hop during Lewis's demand bubble.");
        }

        private void ShowLewisDialogue(string situation, string fallbackKey = null, string fallbackText = null)
        {
            if (lewis == null)
                return;

            string key = getDialogueKey?.Invoke(chasedSlot, situation);
            if (string.IsNullOrWhiteSpace(key))
                key = fallbackKey;

            string text = string.IsNullOrWhiteSpace(key) ? "" : translation?.Get(key)?.ToString();
            if (string.IsNullOrWhiteSpace(text))
                text = fallbackText ?? "";

            if (!string.IsNullOrWhiteSpace(text))
                lewis.showTextAboveHead(text);
        }

        private static bool HasExternalController(NPC npc)
        {
            return npc != null && (npc.controller != null || npc.temporaryController != null);
        }

        private NPC FindVisibleWitness(NPC subject, bool requireWithinWitnessDistance)
        {
            if (subject?.currentLocation == null || Game1.player?.currentLocation != subject.currentLocation)
                return null;

            Rectangle viewport = new(Game1.viewport.X, Game1.viewport.Y, Game1.viewport.Width, Game1.viewport.Height);
            foreach (NPC candidate in NpcContextResolver.GetCurrentLocationNpcs())
            {
                if (candidate == null
                    || ReferenceEquals(candidate, subject)
                    || !candidate.IsVillager
                    || candidate.IsInvisible
                    || candidate.isSleeping.Value
                    || candidate.currentLocation != subject.currentLocation
                    || IsDecorativeCompanion(candidate))
                {
                    continue;
                }

                if (!viewport.Intersects(candidate.GetBoundingBox()))
                    continue;

                if (requireWithinWitnessDistance
                    && Vector2.Distance(subject.Position, candidate.Position) > WitnessDistance)
                {
                    continue;
                }

                return candidate;
            }

            return null;
        }

        private static bool IsDecorativeCompanion(NPC npc)
        {
            string assemblyName = npc?.GetType().Assembly.GetName().Name ?? "";
            return assemblyName.Contains("CustomCompanions", StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsManorHouse(GameLocation location)
        {
            return location != null
                && (string.Equals(location.Name, ManorHouseName, StringComparison.OrdinalIgnoreCase)
                    || string.Equals(location.NameOrUniqueName, ManorHouseName, StringComparison.OrdinalIgnoreCase));
        }

        private void ClearPath()
        {
            path.Clear();
            pathTarget = null;
            pathRefreshTimer = 0;
        }

        private void ClearState()
        {
            StopLewisShaking();
            phase = ChasePhase.None;
            lewis = null;
            originLocation = null;
            originPosition = Vector2.Zero;
            originTile = Point.Zero;
            originalAnimation = null;
            specialActionSnapshot = null;
            chasedSlot = LewisShortsSlot.None;
            failedPathTicks = 0;
            demandBubbleTimer = 0;
            witnessEmoteTimer = 0;
            playerStartlePlayed = false;
            visualChangedDuringSequence = false;
            requiresPrivacy = false;
            ClearPath();
        }

        private void LogDebug(string message)
        {
            if (ModEntry.DebugLog)
                monitor?.Log("[LEWIS SHORTS CHASE] " + message, LogLevel.Info);
        }

        private enum ChasePhase
        {
            None,
            DemandBubble,
            Chasing,
            WitnessEmote,
            Returning
        }
    }
}
