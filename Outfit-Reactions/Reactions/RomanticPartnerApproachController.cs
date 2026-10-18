using Microsoft.Xna.Framework;
using StardewModdingAPI;
using StardewValley;
using StardewValley.Pathfinding;
using StardewValley.TerrainFeatures;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace OutfitReactions
{
    internal enum RomanticPartnerApproachOutcome
    {
        None,
        CancelPending,
        KeepPending
    }

    /// <summary>
    /// Lets a romantic partner walk over after noticing the player's outfit. Approach and return
    /// movement use temporary vanilla tile controllers, while an interrupted schedule route is
    /// preserved and rejoined from the NPC's current position. Unsupported custom controllers are
    /// left untouched and make the approach fall back to the normal notice behavior.
    /// </summary>
    internal sealed class RomanticPartnerApproachController
    {
        public const float NoticeDistance = 650f;

        private const float WaitingReleaseDistance = 500f;
        private const int GiveUpAfterFailedPathTicks = 180;

        private static readonly FieldInfo DirectionsToNewLocationField =
            typeof(NPC).GetField("directionsToNewLocation", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);

        private readonly IMonitor monitor;
        private readonly NpcSpecialActionController specialActionController;
        private readonly Dictionary<string, ApproachSession> sessions = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, RomanticPartnerApproachOutcome> outcomes = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, StationaryAnchor> stationaryAnchors = new(StringComparer.OrdinalIgnoreCase);

        public RomanticPartnerApproachController(IMonitor monitor)
        {
            this.monitor = monitor;
            specialActionController = new NpcSpecialActionController(monitor);
        }

        public bool TryBegin(NPC npc, PendingPrompt pending, bool wasMovingWhenNoticed)
        {
            if (npc == null || pending == null || Game1.player == null || npc.currentLocation != Game1.player.currentLocation)
                return false;

            if (sessions.ContainsKey(npc.Name) || npc.temporaryController != null)
                return false;

            PathFindController activeController = npc.controller;
            bool hasController = activeController != null;
            bool hasSupportedVanillaRoute = hasController && activeController.GetType() == typeof(PathFindController);

            // A controller supplied by another mod is a newer authority which we cannot safely
            // rebuild from a different tile. Likewise, direct movement without a vanilla route may
            // belong to an external activity such as a follower framework.
            if ((hasController && !hasSupportedVanillaRoute) || (wasMovingWhenNoticed && !hasController))
            {
                LogDebug($"Skipped approach for {npc.Name} because their movement is owned by an unsupported controller or external direct-movement activity.");
                return false;
            }

            if (!wasMovingWhenNoticed && !hasSupportedVanillaRoute)
                PrepareProtectedStationaryState(npc, pending);

            ApproachSession session = new ApproachSession
            {
                Npc = npc,
                Pending = pending,
                Location = npc.currentLocation,
                OriginPosition = pending.HasOriginalVisualState ? pending.OriginalPosition : npc.Position,
                // Position / 64 isn't equivalent to NPC.TilePoint: the latter represents the tile
                // under the NPC's feet. Keeping the captured tile avoids returning one row above
                // the saved pixel position and then restoring a sideways walking frame.
                OriginTile = pending.HasOriginalVisualState ? pending.OriginalTile : npc.TilePoint,
                WasWalking = wasMovingWhenNoticed || hasSupportedVanillaRoute,
                OriginalFacingDirection = pending.HasOriginalVisualState ? pending.OriginalFacingDirection : npc.FacingDirection,
                OriginalFrame = pending.HasOriginalVisualState ? pending.OriginalFrame : npc.Sprite?.CurrentFrame ?? 0,
                OriginalFlip = pending.HasOriginalVisualState ? pending.OriginalFlip : npc.flip,
                OriginalMovementPause = pending.HasOriginalVisualState ? pending.OriginalMovementPause : (int)npc.movementPause,
                OriginalAddedSpeed = pending.HasOriginalVisualState ? pending.OriginalAddedSpeed : (int)npc.addedSpeed,
                OriginalAnimation = pending.OriginalAnimation == null
                    ? null
                    : new List<FarmerSprite.AnimationFrame>(pending.OriginalAnimation),
                Phase = ApproachPhase.Approaching
            };

            if (hasSupportedVanillaRoute && !CaptureVanillaRoute(activeController, session))
            {
                LogDebug($"Skipped approach for {npc.Name} because their vanilla route had no resumable destination.");
                return false;
            }

            // Only a confirmed vanilla PathFindController is suspended. No custom controller and
            // no queuedSchedulePaths collection is ever cleared or replaced here.
            if (hasSupportedVanillaRoute)
                npc.controller = null;

            sessions[npc.Name] = session;
            outcomes.Remove(npc.Name);

            npc.Halt();
            npc.movementPause = 0;
            if (IsAdjacentToPlayer(npc))
            {
                session.Phase = ApproachPhase.Waiting;
                HoldStill(npc, facePlayer: true);
            }
            else if (!TryStartApproachPath(session))
            {
                sessions.Remove(npc.Name);
                if (session.WasWalking)
                    npc.controller = session.OriginalController;
                else
                    RestoreStationaryState(session, atOrigin: true);
                LogDebug($"Skipped approach for {npc.Name} because no vanilla tile path could reach an adjacent tile.");
                return false;
            }

            // Cross-mod ownership handshake: while this session exists, Outfit Reactions owns
            // the partner's true stationary origin. Lots of Kisses may still capture/restore the
            // kiss pose, but must not treat the temporary near-player waiting position as the
            // NPC's permanent idle position.
            npc.modData[ModEntry.RomanticApproachActiveModDataKey] = "1";
            LogDebug($"{npc.Name} started a romantic outfit approach (walking={session.WasWalking}, origin={session.OriginTile}).");
            return true;
        }

        public void Update()
        {
            PruneInactiveStationaryAnchors();

            foreach (string npcName in sessions.Keys.ToList())
            {
                if (!sessions.TryGetValue(npcName, out ApproachSession session) || session?.Npc == null)
                    continue;

                UpdateSession(session);
            }
        }

        public bool IsHandling(NPC npc)
        {
            return npc != null && sessions.ContainsKey(npc.Name);
        }

        /// <summary>
        /// Protects a partner's stable destination state before proximity/facing checks can make
        /// them turn toward the player. This method is strictly observational and never stops an
        /// animation, movement, or controller.
        /// </summary>
        public void ObserveStationaryOrigin(NPC npc)
        {
            if (npc != null && !sessions.ContainsKey(npc.Name))
                npc.modData.Remove(ModEntry.RomanticApproachActiveModDataKey);

            if (npc == null
                || npc.Sprite == null
                || npc.currentLocation == null
                || sessions.ContainsKey(npc.Name)
                || npc.controller != null
                || npc.temporaryController != null
                || npc.isMoving())
            {
                return;
            }

            if (TryGetValidStationaryAnchor(npc, out _))
                return;

            NpcOutfitSpecialActionSnapshot specialSnapshot = specialActionController.CaptureReadOnly(npc);
            stationaryAnchors[npc.Name] = new StationaryAnchor
            {
                Npc = npc,
                Location = npc.currentLocation,
                Position = npc.Position,
                Tile = npc.TilePoint,
                FacingDirection = npc.FacingDirection,
                Frame = npc.Sprite.CurrentFrame,
                Flip = npc.flip,
                MovementPause = (int)npc.movementPause,
                AddedSpeed = (int)npc.addedSpeed,
                Animation = CloneAnimation(npc.Sprite.CurrentAnimation),
                SpecialActionSnapshot = CloneSpecialActionSnapshot(specialSnapshot)
            };
            LogDebug($"Observed {npc.Name}'s stable stationary origin before proximity detection at {npc.TilePoint} (frame={npc.Sprite.CurrentFrame}, facing={npc.FacingDirection}, special={specialSnapshot != null}).");
        }

        public bool IsWaiting(NPC npc)
        {
            return npc != null
                && sessions.TryGetValue(npc.Name, out ApproachSession session)
                && session.Phase == ApproachPhase.Waiting;
        }

        public void NotifyInteraction(NPC npc)
        {
            if (npc != null && sessions.TryGetValue(npc.Name, out ApproachSession session))
                session.Interacted = true;
        }

        public void SetSuspended(NPC npc, bool suspended)
        {
            if (npc == null || !sessions.TryGetValue(npc.Name, out ApproachSession session))
                return;

            session.Suspended = suspended;
            if (suspended)
            {
                CancelOwnedController(session);
                npc.movementPause = 0;
            }
        }

        public void ReleaseForExternalAction(NPC npc)
        {
            if (npc == null || !sessions.TryGetValue(npc.Name, out ApproachSession session))
                return;

            session.KeepPendingAfterRelease = true;
            session.Suspended = false;
            BeginRelease(session, "An external romantic action took ownership of the NPC.");
        }

        public void ReleaseWithoutPending(NPC npc)
        {
            if (npc == null || !sessions.TryGetValue(npc.Name, out ApproachSession session))
                return;

            session.DiscardOutcome = true;
            session.Suspended = false;
            BeginRelease(session, "The pending romantic outfit reaction was cancelled.");
        }

        public bool TryConsumeOutcome(NPC npc, out RomanticPartnerApproachOutcome outcome)
        {
            outcome = RomanticPartnerApproachOutcome.None;
            if (npc == null || !outcomes.TryGetValue(npc.Name, out outcome))
                return false;

            outcomes.Remove(npc.Name);
            return true;
        }

        public void Reset(bool restoreIfPossible)
        {
            foreach (ApproachSession session in sessions.Values.ToList())
            {
                if (session?.Npc == null)
                    continue;

                session.Npc.modData.Remove(ModEntry.RomanticApproachActiveModDataKey);

                bool ownedControllerActive = IsOwnedControllerActive(session);
                if (ownedControllerActive)
                    CancelOwnedController(session);

                if (restoreIfPossible && session.Npc.currentLocation == session.Location && session.Npc.controller == null)
                {
                    if (session.WasWalking)
                        ResumeWalkingRoutine(session);
                    else
                        RestoreStationaryState(session, atOrigin: false);
                }
                else
                {
                    session.Pending.SpecialActionSnapshot = null;
                    session.Npc.movementPause = session.OriginalMovementPause;
                    session.Npc.addedSpeed = session.OriginalAddedSpeed;
                }
            }

            sessions.Clear();
            outcomes.Clear();
            stationaryAnchors.Clear();
        }

        private void PrepareProtectedStationaryState(NPC npc, PendingPrompt pending)
        {
            if (TryGetValidStationaryAnchor(npc, out StationaryAnchor anchor))
            {
                ApplyStationaryAnchor(anchor, pending);

                // A special action must still be suspended visually for the walk. Capture the
                // currently-restored action to perform that suspension, then restore from a fresh
                // clone of the first immutable snapshot instead of accepting a later pose/frame.
                if (anchor.SpecialActionSnapshot != null)
                {
                    pending.SpecialActionSnapshot = null;
                    specialActionController.Capture(npc, pending);
                    pending.SpecialActionSnapshot = CloneSpecialActionSnapshot(anchor.SpecialActionSnapshot);
                }
                else
                {
                    pending.SpecialActionSnapshot = null;
                }

                LogDebug($"Reused {npc.Name}'s protected stationary origin at {anchor.Tile} (frame={anchor.Frame}, facing={anchor.FacingDirection}).");
                return;
            }

            // This is the only place allowed to establish a stationary origin. Capture the special
            // action first so the protected snapshot includes its untouched animation metadata.
            specialActionController.Capture(npc, pending);
            stationaryAnchors[npc.Name] = new StationaryAnchor
            {
                Npc = npc,
                Location = npc.currentLocation,
                Position = pending.OriginalPosition,
                Tile = pending.OriginalTile,
                FacingDirection = pending.OriginalFacingDirection,
                Frame = pending.OriginalFrame,
                Flip = pending.OriginalFlip,
                MovementPause = pending.OriginalMovementPause,
                AddedSpeed = pending.OriginalAddedSpeed,
                Animation = CloneAnimation(pending.OriginalAnimation),
                SpecialActionSnapshot = CloneSpecialActionSnapshot(pending.SpecialActionSnapshot)
            };
            LogDebug($"Protected {npc.Name}'s stationary origin at {pending.OriginalTile} (frame={pending.OriginalFrame}, facing={pending.OriginalFacingDirection}).");
        }

        private bool TryGetValidStationaryAnchor(NPC npc, out StationaryAnchor anchor)
        {
            anchor = null;
            if (npc == null || !stationaryAnchors.TryGetValue(npc.Name, out StationaryAnchor candidate) || candidate == null)
                return false;

            bool valid = candidate.Npc == npc
                && candidate.Location == npc.currentLocation
                && npc.controller == null
                && npc.temporaryController == null
                && Vector2.DistanceSquared(npc.Position, candidate.Position) <= 8f * 8f;
            if (!valid)
            {
                stationaryAnchors.Remove(npc.Name);
                return false;
            }

            anchor = candidate;
            return true;
        }

        private void PruneInactiveStationaryAnchors()
        {
            foreach (string npcName in stationaryAnchors.Keys.ToList())
            {
                if (sessions.ContainsKey(npcName))
                    continue;

                StationaryAnchor anchor = stationaryAnchors[npcName];
                NPC npc = anchor?.Npc;
                bool changedState = npc == null
                    || npc.currentLocation != anchor.Location
                    || npc.controller != null
                    || npc.temporaryController != null
                    || npc.isMoving()
                    || Vector2.DistanceSquared(npc.Position, anchor.Position) > 8f * 8f;
                if (changedState)
                {
                    stationaryAnchors.Remove(npcName);
                    LogDebug($"Released {npcName}'s protected stationary origin because their real routine changed.");
                }
            }
        }

        private static void ApplyStationaryAnchor(StationaryAnchor anchor, PendingPrompt pending)
        {
            pending.OriginalPosition = anchor.Position;
            pending.OriginalTile = anchor.Tile;
            pending.OriginalFacingDirection = anchor.FacingDirection;
            pending.OriginalFrame = anchor.Frame;
            pending.OriginalFlip = anchor.Flip;
            pending.OriginalMovementPause = anchor.MovementPause;
            pending.OriginalAddedSpeed = anchor.AddedSpeed;
            pending.OriginalAnimation = CloneAnimation(anchor.Animation);
            pending.HasOriginalVisualState = true;
        }

        private static List<FarmerSprite.AnimationFrame> CloneAnimation(List<FarmerSprite.AnimationFrame> animation)
        {
            return animation == null ? null : new List<FarmerSprite.AnimationFrame>(animation);
        }

        private static NpcOutfitSpecialActionSnapshot CloneSpecialActionSnapshot(NpcOutfitSpecialActionSnapshot snapshot)
        {
            if (snapshot == null)
                return null;

            return new NpcOutfitSpecialActionSnapshot
            {
                Npc = snapshot.Npc,
                Location = snapshot.Location,
                FacingDirection = snapshot.FacingDirection,
                CurrentFrame = snapshot.CurrentFrame,
                Flip = snapshot.Flip,
                MovementPause = snapshot.MovementPause,
                AddedSpeed = snapshot.AddedSpeed,
                CurrentAnimation = CloneAnimation(snapshot.CurrentAnimation),
                HasSavedSpriteDimensions = snapshot.HasSavedSpriteDimensions,
                SavedIgnoreSourceRectUpdates = snapshot.SavedIgnoreSourceRectUpdates,
                SavedSpriteWidth = snapshot.SavedSpriteWidth,
                SavedTempSpriteHeight = snapshot.SavedTempSpriteHeight,
                SavedDoingEndOfRouteAnimation = snapshot.SavedDoingEndOfRouteAnimation,
                SavedCurrentlyDoingEndOfRouteAnimation = snapshot.SavedCurrentlyDoingEndOfRouteAnimation,
                SavedStartedEndOfRouteBehavior = snapshot.SavedStartedEndOfRouteBehavior,
                HasSavedRodLayerFields = snapshot.HasSavedRodLayerFields,
                SavedYOffset = snapshot.SavedYOffset,
                SavedLoadedEndOfRouteBehavior = snapshot.SavedLoadedEndOfRouteBehavior,
                SavedDrawOffset = snapshot.SavedDrawOffset
            };
        }

        private void UpdateSession(ApproachSession session)
        {
            NPC npc = session.Npc;
            if (!Context.IsWorldReady || Game1.player == null || npc.currentLocation == null || session.Location == null)
            {
                CompleteAtCurrentPosition(session, allowVisualRestore: false, "World state became unavailable.");
                return;
            }

            if (npc.currentLocation != session.Location)
            {
                CompleteAtCurrentPosition(session, allowVisualRestore: false, "The partner changed locations.");
                return;
            }

            if (npc.controller != null && !IsOwnedControllerActive(session))
            {
                // Another system took control after the approach began. Do not overwrite it.
                CompleteAtCurrentPosition(session, allowVisualRestore: false, "A new controller took ownership of the partner.");
                return;
            }

            if (session.Suspended)
                return;

            if (Game1.activeClickableMenu != null || Game1.dialogueUp || Game1.freezeControls || Game1.eventUp)
            {
                if (IsOwnedControllerActive(session))
                    npc.Halt();
                else
                    HoldStill(npc, facePlayer: session.Phase == ApproachPhase.Waiting);
                return;
            }

            switch (session.Phase)
            {
                case ApproachPhase.Approaching:
                    UpdateApproaching(session);
                    break;
                case ApproachPhase.Waiting:
                    UpdateWaiting(session);
                    break;
                case ApproachPhase.Returning:
                    UpdateReturning(session);
                    break;
            }
        }

        private void UpdateApproaching(ApproachSession session)
        {
            NPC npc = session.Npc;
            if (Game1.player.currentLocation != session.Location || DistanceToPlayer(npc) > NoticeDistance)
            {
                BeginRelease(session, "The player left the romantic approach range.");
                return;
            }

            // Let the vanilla controller finish its current tile path before deciding that the
            // NPC has arrived. Cancelling based on pixel distance mid-step would leave them
            // between tiles, which is the visual drift this controller is meant to avoid.
            if (IsOwnedControllerActive(session))
                return;

            if (IsAdjacentToPlayer(npc))
            {
                CancelOwnedController(session);
                session.Phase = ApproachPhase.Waiting;
                HoldStill(npc, facePlayer: true);
                LogDebug($"{npc.Name} reached interaction distance and is waiting for the player.");
                return;
            }

            session.OwnedController = null;
            bool started = TryStartApproachPath(session);
            TrackFailedMovement(session, started, "The partner could not find a vanilla tile route to the player.");
        }

        private void UpdateWaiting(ApproachSession session)
        {
            NPC npc = session.Npc;
            if (Game1.player.currentLocation != session.Location || DistanceToPlayer(npc) > WaitingReleaseDistance)
            {
                BeginRelease(session, session.Interacted
                    ? "The player walked away after interacting."
                    : "The player walked away without interacting.");
                return;
            }

            HoldStill(npc, facePlayer: true);
        }

        private void UpdateReturning(ApproachSession session)
        {
            NPC npc = session.Npc;
            if (npc.TilePoint == session.OriginTile)
            {
                CancelOwnedController(session);
                npc.Position = session.OriginPosition;
                RestoreStationaryState(session, atOrigin: true);
                Complete(session, "The stationary partner returned to the original position.");
                return;
            }

            if (IsOwnedControllerActive(session))
                return;

            session.OwnedController = null;
            bool started = TryStartOwnedPath(session, session.OriginTile, session.OriginalFacingDirection);
            if (!started)
            {
                session.FailedPathTicks++;
                if (session.FailedPathTicks >= GiveUpAfterFailedPathTicks)
                {
                    RestoreStationaryState(session, atOrigin: false);
                    Complete(session, "The return path remained blocked; the partner was safely restored at the current position.");
                }
            }
            else
            {
                session.FailedPathTicks = 0;
            }
        }

        private void BeginRelease(ApproachSession session, string reason)
        {
            if (session.Phase == ApproachPhase.Returning)
                return;

            CancelOwnedController(session);
            session.FailedPathTicks = 0;

            if (session.WasWalking)
            {
                bool resumed = ResumeWalkingRoutine(session);
                Complete(session, resumed
                    ? reason + " Their original route was rejoined from the current tile."
                    : reason + " No safe route connection was available, so the walking pose was released cleanly.");
                return;
            }

            session.Phase = ApproachPhase.Returning;
            session.Npc.Halt();
            session.Npc.movementPause = 0;
            if (session.Npc.TilePoint == session.OriginTile)
            {
                session.Npc.Position = session.OriginPosition;
                RestoreStationaryState(session, atOrigin: true);
                Complete(session, reason + " Their original pose was restored.");
            }
            else
            {
                TryStartOwnedPath(session, session.OriginTile, session.OriginalFacingDirection);
                LogDebug(reason + $" {session.Npc.Name} is walking back to the original position.");
            }
        }

        private void CompleteAtCurrentPosition(ApproachSession session, bool allowVisualRestore, string reason)
        {
            if (session?.Npc != null)
            {
                CancelOwnedController(session);
                if (allowVisualRestore && !session.WasWalking)
                    RestoreStationaryState(session, atOrigin: false);
                else
                {
                    session.Pending.SpecialActionSnapshot = null;
                    session.Npc.movementPause = session.OriginalMovementPause;
                    session.Npc.addedSpeed = session.OriginalAddedSpeed;
                }
            }

            Complete(session, reason);
        }

        private void Complete(ApproachSession session, string reason)
        {
            if (session?.Npc == null)
                return;

            session.Npc.modData.Remove(ModEntry.RomanticApproachActiveModDataKey);
            RomanticPartnerApproachOutcome outcome = session.KeepPendingAfterRelease || session.Interacted
                ? RomanticPartnerApproachOutcome.KeepPending
                : RomanticPartnerApproachOutcome.CancelPending;
            if (!session.DiscardOutcome)
                outcomes[session.Npc.Name] = outcome;
            else
                outcomes.Remove(session.Npc.Name);
            sessions.Remove(session.Npc.Name);
            LogDebug(reason);
        }

        private bool TryStartApproachPath(ApproachSession session)
        {
            if (session?.Npc == null || Game1.player == null || session.Location == null)
                return false;

            Point start = session.Npc.TilePoint;
            Point playerTile = Game1.player.TilePoint;
            Point[] candidates =
            {
                new(playerTile.X, playerTile.Y - 1),
                new(playerTile.X + 1, playerTile.Y),
                new(playerTile.X, playerTile.Y + 1),
                new(playerTile.X - 1, playerTile.Y)
            };

            PathFindController bestController = null;
            Point bestTarget = Point.Zero;
            int bestLength = int.MaxValue;

            foreach (Point candidate in candidates.OrderBy(tile => Math.Abs(tile.X - start.X) + Math.Abs(tile.Y - start.Y)))
            {
                if (candidate != start && !IsTilePassable(session.Location, candidate, session.Npc))
                    continue;

                PathFindController candidateController = CreateVanillaTileController(
                    session.Npc,
                    session.Location,
                    candidate,
                    finalFacingDirection: -1);
                int pathLength = candidateController?.pathToEndPoint?.Count ?? 0;
                if (pathLength <= 0 || pathLength >= bestLength)
                    continue;

                bestController = candidateController;
                bestTarget = candidate;
                bestLength = pathLength;
            }

            if (bestController == null)
                return false;

            session.OwnedController = bestController;
            session.Npc.Halt();
            session.Npc.movementPause = 0;
            session.Npc.controller = bestController;
            LogDebug($"Started vanilla tile approach for {session.Npc.Name}: {start} -> {bestTarget} ({bestLength} steps).");
            return true;
        }

        private bool TryStartOwnedPath(ApproachSession session, Point target, int finalFacingDirection)
        {
            if (session?.Npc == null || session.Location == null)
                return false;

            if (session.Npc.TilePoint == target)
                return true;

            PathFindController controller = CreateVanillaTileController(
                session.Npc,
                session.Location,
                target,
                finalFacingDirection);
            if (controller?.pathToEndPoint == null || controller.pathToEndPoint.Count == 0)
                return false;

            session.OwnedController = controller;
            session.Npc.Halt();
            session.Npc.movementPause = 0;
            session.Npc.controller = controller;
            return true;
        }

        private static PathFindController CreateVanillaTileController(
            NPC npc,
            GameLocation location,
            Point target,
            int finalFacingDirection)
        {
            try
            {
                return new PathFindController(npc, location, target, finalFacingDirection, false);
            }
            catch
            {
                return null;
            }
        }

        private static bool IsOwnedControllerActive(ApproachSession session)
        {
            return session?.Npc != null
                && session.OwnedController != null
                && ReferenceEquals(session.Npc.controller, session.OwnedController);
        }

        private static void CancelOwnedController(ApproachSession session)
        {
            if (session?.Npc == null)
                return;

            if (session.OwnedController != null && ReferenceEquals(session.Npc.controller, session.OwnedController))
                session.Npc.controller = null;

            session.OwnedController = null;
            session.Npc.Halt();
            session.Npc.movementPause = 0;
        }

        private void TrackFailedMovement(ApproachSession session, bool moved, string reason)
        {
            if (moved)
            {
                session.FailedPathTicks = 0;
                return;
            }

            session.FailedPathTicks++;
            if (session.FailedPathTicks >= GiveUpAfterFailedPathTicks)
                BeginRelease(session, reason);
        }

        private bool IsTilePassable(GameLocation location, Point tile, NPC npc)
        {
            if (location?.Map?.Layers == null || location.Map.Layers.Count == 0)
                return false;

            int width = location.Map.Layers[0].LayerWidth;
            int height = location.Map.Layers[0].LayerHeight;
            if (tile.X < 0 || tile.Y < 0 || tile.X >= width || tile.Y >= height)
                return false;

            if (!location.isTilePassable(new xTile.Dimensions.Location(tile.X, tile.Y), Game1.viewport))
                return false;

            Vector2 tileVector = new(tile.X, tile.Y);
            if (location.objects.TryGetValue(tileVector, out StardewValley.Object mapObject) && !mapObject.isPassable())
                return false;

            if (location.terrainFeatures.TryGetValue(tileVector, out TerrainFeature feature) && !feature.isPassable(npc))
                return false;

            return true;
        }

        private bool CaptureVanillaRoute(PathFindController controller, ApproachSession session)
        {
            try
            {
                if (controller?.pathToEndPoint is not Stack<Point> path || path.Count == 0)
                    return false;

                session.OriginalController = controller;
                session.FinalDestination = controller.endPoint;
                session.OriginalRoutePath = path.ToList();
                session.Directions = GetDirections(session.Npc);
                LogDebug($"Captured {session.Npc.Name}'s original route endpoint {controller.endPoint} (schedule={controller.NPCSchedule}, remainingSteps={path.Count}).");
                return true;
            }
            catch
            {
                return false;
            }
        }

        private bool ResumeWalkingRoutine(ApproachSession session)
        {
            NPC npc = session.Npc;
            npc.Halt();
            npc.movementPause = session.OriginalMovementPause;
            npc.addedSpeed = session.OriginalAddedSpeed;

            try
            {
                if (session.FinalDestination.HasValue && session.OriginalController != null)
                {
                    PathFindController originalController = session.OriginalController;
                    Point destination = session.FinalDestination.Value;

                    if (TryReconnectOriginalRoute(session, out Stack<Point> reconnectedPath, out Point rejoinTile))
                    {
                        originalController.pathToEndPoint = reconnectedPath;
                        originalController.pausedTimer = 0;
                        originalController.timerSinceLastCheckPoint = 0;
                        npc.controller = originalController;
                        if (session.Directions != null)
                            SetDirections(npc, session.Directions);
                        LogDebug($"Rejoined {npc.Name}'s preserved route at {rejoinTile} from {npc.TilePoint} (schedule={originalController.NPCSchedule}, combinedSteps={reconnectedPath.Count}).");
                        return true;
                    }

                    if (destination != Point.Zero && npc.TilePoint == destination)
                    {
                        npc.controller = null;
                        originalController.endBehaviorFunction?.Invoke(npc, session.Location);
                        return npc.controller != null || originalController.endBehaviorFunction != null;
                    }

                    Stack<Point> rebuiltPath = null;
                    if (destination != Point.Zero && originalController.NPCSchedule)
                    {
                        rebuiltPath = PathFindController.findPathForNPCSchedules(
                            npc.TilePoint,
                            destination,
                            session.Location,
                            30000,
                            npc);
                    }
                    else if (destination != Point.Zero)
                    {
                        PathFindController probe = new(
                            npc,
                            session.Location,
                            destination,
                            originalController.finalFacingDirection,
                            originalController.endBehaviorFunction);
                        rebuiltPath = probe.pathToEndPoint;
                    }

                    if (rebuiltPath != null && rebuiltPath.Count > 0)
                    {
                        originalController.pathToEndPoint = rebuiltPath;
                        originalController.pausedTimer = 0;
                        originalController.timerSinceLastCheckPoint = 0;
                        npc.controller = originalController;
                        if (session.Directions != null)
                            SetDirections(npc, session.Directions);
                        LogDebug($"Resumed {npc.Name}'s original controller toward {destination} from {npc.TilePoint} (schedule={originalController.NPCSchedule}, rebuiltSteps={rebuiltPath.Count}).");
                        return true;
                    }
                }

                npc.checkSchedule(Game1.timeOfDay);
                if (npc.controller != null)
                    return true;
            }
            catch (Exception ex)
            {
                LogDebug($"Could not rebuild {npc.Name}'s route from the current position: {ex.Message}");
                npc.checkSchedule(Game1.timeOfDay);
                if (npc.controller != null)
                    return true;
            }

            ReleaseWalkingPose(npc, session.OriginalFacingDirection);
            return false;
        }

        private bool TryReconnectOriginalRoute(ApproachSession session, out Stack<Point> combinedPath, out Point rejoinTile)
        {
            combinedPath = null;
            rejoinTile = Point.Zero;

            if (session?.Npc == null
                || session.Location == null
                || session.OriginalRoutePath == null
                || session.OriginalRoutePath.Count == 0)
            {
                return false;
            }

            Point currentTile = session.Npc.TilePoint;
            var candidates = session.OriginalRoutePath
                .Select((tile, index) => new
                {
                    Tile = tile,
                    Index = index,
                    Distance = Math.Abs(tile.X - currentTile.X) + Math.Abs(tile.Y - currentTile.Y)
                })
                .OrderBy(candidate => candidate.Distance)
                .ThenBy(candidate => candidate.Index)
                .Take(16);

            foreach (var candidate in candidates)
            {
                List<Point> bridge;
                if (candidate.Tile == currentTile)
                {
                    bridge = new List<Point> { candidate.Tile };
                }
                else
                {
                    Stack<Point> bridgePath = PathFindController.findPathForNPCSchedules(
                        currentTile,
                        candidate.Tile,
                        session.Location,
                        30000,
                        session.Npc);
                    if (bridgePath == null || bridgePath.Count == 0)
                        continue;

                    bridge = bridgePath.ToList();
                }

                List<Point> orderedPath = new(bridge.Count + session.OriginalRoutePath.Count - candidate.Index - 1);
                orderedPath.AddRange(bridge);
                orderedPath.AddRange(session.OriginalRoutePath.Skip(candidate.Index + 1));
                if (orderedPath.Count == 0)
                    continue;

                combinedPath = new Stack<Point>(orderedPath.AsEnumerable().Reverse());
                rejoinTile = candidate.Tile;
                return combinedPath.Count > 0;
            }

            return false;
        }

        private static void ReleaseWalkingPose(NPC npc, int facingDirection)
        {
            if (npc?.Sprite == null)
                return;

            npc.Halt();
            npc.movementPause = 0;
            npc.Sprite.StopAnimation();
            npc.Sprite.ClearAnimation();
            npc.Sprite.CurrentAnimation = null;
            npc.faceDirection(facingDirection);
            npc.Sprite.CurrentFrame = facingDirection switch
            {
                0 => 8,
                1 => 4,
                2 => 0,
                3 => 12,
                _ => 0
            };
            npc.Sprite.UpdateSourceRect();
        }

        private void RestoreStationaryState(ApproachSession session, bool atOrigin)
        {
            NPC npc = session.Npc;
            npc.Halt();
            bool restoredSpecialAction = specialActionController.TryRestore(npc, session.Pending, force: true);
            if (restoredSpecialAction)
                return;

            npc.movementPause = session.OriginalMovementPause;
            npc.addedSpeed = session.OriginalAddedSpeed;
            npc.FacingDirection = session.OriginalFacingDirection;
            npc.flip = session.OriginalFlip;

            if (npc.Sprite == null)
                return;

            if (session.OriginalAnimation != null && session.OriginalAnimation.Count > 0)
                npc.Sprite.CurrentAnimation = new List<FarmerSprite.AnimationFrame>(session.OriginalAnimation);
            else
            {
                npc.Sprite.StopAnimation();
                npc.Sprite.ClearAnimation();
                npc.Sprite.CurrentAnimation = null;
            }

            npc.Sprite.CurrentFrame = session.OriginalFrame;
            npc.Sprite.UpdateSourceRect();
        }

        private static void HoldStill(NPC npc, bool facePlayer)
        {
            if (npc == null)
                return;

            npc.Halt();
            npc.movementPause = Math.Max((int)npc.movementPause, 6);
            npc.Sprite?.StopAnimation();
            if (facePlayer && Game1.player != null && npc.currentLocation == Game1.player.currentLocation)
                npc.faceGeneralDirection(Game1.player.getStandingPosition(), 0, false, false);
        }

        private static float DistanceToPlayer(NPC npc)
        {
            return npc == null || Game1.player == null
                ? float.MaxValue
                : Vector2.Distance(npc.Position, Game1.player.Position);
        }

        private static bool IsAdjacentToPlayer(NPC npc)
        {
            if (npc == null || Game1.player == null || npc.currentLocation != Game1.player.currentLocation)
                return false;

            Point npcTile = npc.TilePoint;
            Point playerTile = Game1.player.TilePoint;
            int tileDistance = Math.Abs(npcTile.X - playerTile.X) + Math.Abs(npcTile.Y - playerTile.Y);
            return tileDistance <= 1;
        }

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

        private void LogDebug(string message)
        {
            if (ModEntry.DebugLog)
                monitor?.Log("[ROMANTIC APPROACH] " + message, LogLevel.Info);
        }

        private sealed class ApproachSession
        {
            public NPC Npc { get; set; }
            public PendingPrompt Pending { get; set; }
            public GameLocation Location { get; set; }
            public Vector2 OriginPosition { get; set; }
            public Point OriginTile { get; set; }
            public bool WasWalking { get; set; }
            public bool Interacted { get; set; }
            public bool Suspended { get; set; }
            public bool KeepPendingAfterRelease { get; set; }
            public bool DiscardOutcome { get; set; }
            public int OriginalFacingDirection { get; set; }
            public int OriginalFrame { get; set; }
            public bool OriginalFlip { get; set; }
            public int OriginalMovementPause { get; set; }
            public int OriginalAddedSpeed { get; set; }
            public List<FarmerSprite.AnimationFrame> OriginalAnimation { get; set; }
            public Point? FinalDestination { get; set; }
            public PathFindController OriginalController { get; set; }
            public PathFindController OwnedController { get; set; }
            public List<Point> OriginalRoutePath { get; set; }
            public SchedulePathDescription Directions { get; set; }
            public int FailedPathTicks { get; set; }
            public ApproachPhase Phase { get; set; }
        }

        private sealed class StationaryAnchor
        {
            public NPC Npc { get; set; }
            public GameLocation Location { get; set; }
            public Vector2 Position { get; set; }
            public Point Tile { get; set; }
            public int FacingDirection { get; set; }
            public int Frame { get; set; }
            public bool Flip { get; set; }
            public int MovementPause { get; set; }
            public int AddedSpeed { get; set; }
            public List<FarmerSprite.AnimationFrame> Animation { get; set; }
            public NpcOutfitSpecialActionSnapshot SpecialActionSnapshot { get; set; }
        }

        private enum ApproachPhase
        {
            Approaching,
            Waiting,
            Returning
        }
    }
}
