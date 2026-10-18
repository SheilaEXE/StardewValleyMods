using Microsoft.Xna.Framework;
using StardewValley;
using System;
using System.Collections.Generic;
using System.Linq;

namespace OutfitReactions
{
    /// <summary>Owns walking detection and the non-spouse NPC peeking state machine.</summary>
    internal sealed class NpcPeekingController
    {
        // Movement is sampled by the throttled discovery scan (~8.5 times/second),
        // so five samples preserve roughly the original half-second turn grace.
        private const int WalkingGraceScans = 5;
        // A route can remove its controller slightly before the destination pose or special
        // animation is fully applied. Nine discovery samples are roughly one second, giving
        // vanilla and schedule mods time to settle that state before it can be snapshotted.
        private const int StationarySettleScans = 9;
        private readonly Random random;
        private readonly Dictionary<string, PendingPrompt> pendingPrompts;
        private readonly Action<NPC> armPendingReaction;
        private readonly Func<GameLocation, int, int, bool> isVisionIgnoredTile;
        private readonly Dictionary<string, SpyingState> spyingNpcs = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, int> ticksSinceLastMoving = new(StringComparer.OrdinalIgnoreCase);

        public NpcPeekingController(
            Random random,
            Dictionary<string, PendingPrompt> pendingPrompts,
            Action<NPC> armPendingReaction,
            Func<GameLocation, int, int, bool> isVisionIgnoredTile)
        {
            this.random = random;
            this.pendingPrompts = pendingPrompts;
            this.armPendingReaction = armPendingReaction;
            this.isVisionIgnoredTile = isVisionIgnoredTile;
        }

        public void Clear()
        {
            spyingNpcs.Clear();
            ticksSinceLastMoving.Clear();
        }

        public bool Contains(string npcName) => !string.IsNullOrWhiteSpace(npcName) && spyingNpcs.ContainsKey(npcName);

        public void Remove(string npcName)
        {
            if (!string.IsNullOrWhiteSpace(npcName))
                spyingNpcs.Remove(npcName);
        }

        public bool TryGetState(string npcName, out SpyingState state)
        {
            return spyingNpcs.TryGetValue(npcName ?? "", out state);
        }

        public void Begin(NPC npc)
        {
            if (npc == null)
                return;
            spyingNpcs[npc.Name] = new SpyingState
            {
                OriginalFacingDirection = npc.FacingDirection,
                PeekGraceTimer = 30
            };
            if (npc.Sprite != null)
            {
                npc.Sprite.StopAnimation();
                npc.Sprite.CurrentFrame = GetNpcIdleFrameForDirection(npc.FacingDirection);
                npc.Sprite.UpdateSourceRect();
            }
        }

        public void UpdateMovementTracking(NPC npc)
        {
            if (npc == null || Contains(npc.Name))
                return;
            if (npc.isMoving())
                ticksSinceLastMoving[npc.Name] = 0;
            else if (ticksSinceLastMoving.TryGetValue(npc.Name, out int ticks))
                ticksSinceLastMoving[npc.Name] = ticks + 1;
            else
                ticksSinceLastMoving[npc.Name] = WalkingGraceScans + 1;
        }

        public bool IsRecentlyMoving(string npcName)
        {
            return ticksSinceLastMoving.TryGetValue(npcName ?? "", out int ticks) && ticks < WalkingGraceScans;
        }

        public bool HasStableStationaryState(string npcName)
        {
            return ticksSinceLastMoving.TryGetValue(npcName ?? "", out int ticks)
                && ticks >= StationarySettleScans;
        }

        public void Update(float noticeDistance, float cancelDistance)
        {
            if (Game1.player == null)
                return;

            foreach (string name in spyingNpcs.Keys.ToList())
            {
                NPC npc = Game1.currentLocation?.characters
                    .FirstOrDefault(character => character?.Name != null && character.Name.Equals(name, StringComparison.OrdinalIgnoreCase));

                if (npc == null || npc.currentLocation != Game1.player.currentLocation || DistanceToPlayer(npc) > cancelDistance)
                {
                    spyingNpcs.Remove(name);
                    continue;
                }

                SpyingState state = spyingNpcs[name];
                if (state.WalkCooldownTimer > 0)
                {
                    state.WalkCooldownTimer--;
                    continue;
                }

                if (DistanceToPlayer(npc) > noticeDistance)
                {
                    state.WalkCooldownTimer = 60;
                    continue;
                }

                if (state.IsBeingWatched)
                {
                    if (state.PretendTimer > 0)
                    {
                        state.PretendTimer--;
                        continue;
                    }
                    if (IsPlayerFacingNpc(npc))
                    {
                        state.PretendTimer = 12;
                        continue;
                    }

                    state.IsBeingWatched = false;
                    state.WalkCooldownTimer = 120;
                    continue;
                }

                if (npc.movementPause < 6)
                    npc.movementPause = 6;

                npc.faceGeneralDirection(Game1.player.getStandingPosition(), 0, false, false);
                if (npc.Sprite != null)
                {
                    npc.Sprite.StopAnimation();
                    npc.Sprite.CurrentFrame = GetNpcIdleFrameForDirection(npc.FacingDirection);
                    npc.Sprite.UpdateSourceRect();
                }

                if (state.PeekGraceTimer > 0)
                {
                    state.PeekGraceTimer--;
                    continue;
                }

                if (IsPlayerFacingNpc(npc))
                {
                    state.IsBeingWatched = true;
                    state.WasEverCaught = true;
                    state.PretendTimer = 15;

                    if (!pendingPrompts.TryGetValue(name, out PendingPrompt caughtPending) || caughtPending == null)
                    {
                        armPendingReaction?.Invoke(npc);
                        pendingPrompts.TryGetValue(name, out caughtPending);
                    }
                    if (caughtPending != null)
                        caughtPending.WasCaughtPeeking = true;

                    npc.doEmote(random.Next(2) == 0 ? 28 : 16);
                    // Let NPC.update expire the pause and clear its paired freezeMotion flag.
                    // Setting this to zero bypasses vanilla's unfreeze branch.
                    if (npc.movementPause <= 6)
                        npc.movementPause = 1;
                }
            }
        }

        public bool IsPlayerFacingNpc(NPC npc)
        {
            if (npc == null || Game1.player == null)
                return false;
            Vector2 delta = npc.getStandingPosition() - Game1.player.getStandingPosition();
            if (delta.LengthSquared() < 16f * 16f)
                return true;
            return Game1.player.FacingDirection switch
            {
                0 => delta.Y < 0,
                1 => delta.X > 0,
                2 => delta.Y > 0,
                3 => delta.X < 0,
                _ => true
            };
        }

        public bool HasLineOfSightToPlayer(NPC npc)
        {
            if (npc == null || Game1.player == null || npc.currentLocation == null)
                return false;

            GameLocation location = npc.currentLocation;
            Vector2 npcTile = new((int)(npc.Position.X / Game1.tileSize), (int)(npc.Position.Y / Game1.tileSize));
            Vector2 playerTile = new((int)(Game1.player.Position.X / Game1.tileSize), (int)(Game1.player.Position.Y / Game1.tileSize));
            float dx = playerTile.X - npcTile.X;
            float dy = playerTile.Y - npcTile.Y;
            int steps = (int)Math.Max(Math.Abs(dx), Math.Abs(dy));
            if (steps <= 1)
                return true;

            for (int i = 1; i < steps; i++)
            {
                float progress = (float)i / steps;
                int tileX = (int)Math.Round(npcTile.X + dx * progress);
                int tileY = (int)Math.Round(npcTile.Y + dy * progress);

                if (isVisionIgnoredTile?.Invoke(location, tileX, tileY) == true)
                    continue;

                try
                {
                    xTile.Dimensions.Location tileLocation = new(tileX, tileY);
                    if (!location.isTilePassable(tileLocation, Game1.viewport))
                        return false;
                }
                catch
                {
                    return false;
                }
            }
            return true;
        }

        public bool IsNpcFacingPlayer(NPC npc)
        {
            if (npc == null || Game1.player == null)
                return false;
            Vector2 delta = Game1.player.getStandingPosition() - npc.getStandingPosition();
            if (delta.LengthSquared() < 16f * 16f)
                return true;
            return npc.FacingDirection switch
            {
                0 => delta.Y < 0,
                1 => delta.X > 0,
                2 => delta.Y > 0,
                3 => delta.X < 0,
                _ => true
            };
        }

        public float DistanceToPlayer(NPC npc)
        {
            return npc == null || Game1.player == null ? float.MaxValue : Vector2.Distance(npc.Position, Game1.player.Position);
        }

        public bool FacePlayerIfSafe(NPC npc)
        {
            if (npc == null || Game1.player == null || npc.currentLocation != Game1.player.currentLocation || npc.isMoving())
                return false;
            npc.faceGeneralDirection(Game1.player.getStandingPosition(), 0, false, false);
            return true;
        }

        public bool FaceDirectionIfSafe(NPC npc, int direction)
        {
            if (npc == null || npc.isMoving())
                return false;
            npc.faceDirection(direction);
            return true;
        }

        private static int GetNpcIdleFrameForDirection(int facingDirection)
        {
            return facingDirection switch { 0 => 8, 1 => 4, 2 => 0, 3 => 12, _ => 0 };
        }
    }
}
