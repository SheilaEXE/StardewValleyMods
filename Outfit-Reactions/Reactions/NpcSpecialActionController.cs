using Microsoft.Xna.Framework;
using StardewModdingAPI;
using StardewValley;
using System;
using System.Collections.Generic;
using System.Reflection;

namespace OutfitReactions
{
    /// <summary>Suspends and restores non-spouse NPC scene animations during outfit reactions.</summary>
    internal sealed class NpcSpecialActionController
    {
        private const float RestoreDistance = 300f;
        private readonly IMonitor monitor;

        public NpcSpecialActionController(IMonitor monitor)
        {
            this.monitor = monitor;
        }

        /// <summary>
        /// Reads a special stationary pose without stopping it or changing any NPC fields. This is
        /// used to protect a romantic partner's real destination state before the player approaches.
        /// </summary>
        public NpcOutfitSpecialActionSnapshot CaptureReadOnly(NPC npc)
        {
            if (npc == null || npc.Sprite == null || npc.currentLocation == null || npc.isMoving())
                return null;

            List<FarmerSprite.AnimationFrame> animation = null;
            if (npc.Sprite.CurrentAnimation != null && npc.Sprite.CurrentAnimation.Count > 0)
                animation = new List<FarmerSprite.AnimationFrame>(npc.Sprite.CurrentAnimation);

            bool hasSpecialAnimation = animation != null && animation.Count > 0;
            bool hasSpecialStaticFrame = npc.Sprite.CurrentFrame >= 16;
            if (!hasSpecialAnimation && !hasSpecialStaticFrame)
                return null;

            NpcOutfitSpecialActionSnapshot snapshot = new()
            {
                Npc = npc,
                Location = npc.currentLocation,
                FacingDirection = npc.FacingDirection,
                CurrentFrame = npc.Sprite.CurrentFrame,
                Flip = npc.flip,
                MovementPause = (int)npc.movementPause,
                AddedSpeed = (int)npc.addedSpeed,
                CurrentAnimation = animation
            };

            string endOfRouteBehaviorName = TryGetNetStringField(npc, "endOfRouteBehaviorName");
            bool isFishingBehavior = !string.IsNullOrEmpty(endOfRouteBehaviorName)
                && endOfRouteBehaviorName.IndexOf("fish", StringComparison.OrdinalIgnoreCase) >= 0;
            if (isFishingBehavior)
            {
                snapshot.SavedIgnoreSourceRectUpdates = TryGetPrivateField(npc.Sprite, "ignoreSourceRectUpdates") as bool? ?? false;
                snapshot.SavedSpriteWidth = TryGetPrivateField(npc.Sprite, "spriteWidth") as int? ?? npc.Sprite.SpriteWidth;
                snapshot.SavedTempSpriteHeight = TryGetPrivateField(npc.Sprite, "tempSpriteHeight") as int? ?? -1;
                snapshot.HasSavedSpriteDimensions = true;
                snapshot.SavedDoingEndOfRouteAnimation = TryGetNetBoolField(npc, "doingEndOfRouteAnimation");
                snapshot.SavedCurrentlyDoingEndOfRouteAnimation = TryGetPrivateField(npc, "currentlyDoingEndOfRouteAnimation") as bool?;
                snapshot.SavedStartedEndOfRouteBehavior = endOfRouteBehaviorName;
                snapshot.SavedYOffset = TryGetPrivateField(npc, "yOffset") as float? ?? 0f;
                snapshot.SavedLoadedEndOfRouteBehavior = TryGetPrivateField(npc, "loadedEndOfRouteBehavior") as string;
                snapshot.SavedDrawOffset = TryGetPrivateField(npc, "drawOffset") as Vector2? ?? Vector2.Zero;
                snapshot.HasSavedRodLayerFields = true;
            }

            return snapshot;
        }

        public void Capture(NPC npc, PendingPrompt pending)
        {
            if (npc == null || pending == null || npc.Sprite == null || npc.currentLocation == null)
                return;
            if (pending.SpecialActionSnapshot != null || npc.isMoving())
                return;

            List<FarmerSprite.AnimationFrame> animation = null;
            if (npc.Sprite.CurrentAnimation != null && npc.Sprite.CurrentAnimation.Count > 0)
                animation = new List<FarmerSprite.AnimationFrame>(npc.Sprite.CurrentAnimation);

            bool hasSpecialAnimation = animation != null && animation.Count > 0;
            bool hasSpecialStaticFrame = npc.Sprite.CurrentFrame >= 16;
            if (!hasSpecialAnimation && !hasSpecialStaticFrame)
                return;

            pending.SpecialActionSnapshot = new NpcOutfitSpecialActionSnapshot
            {
                Npc = npc,
                Location = npc.currentLocation,
                FacingDirection = npc.FacingDirection,
                CurrentFrame = npc.Sprite.CurrentFrame,
                Flip = npc.flip,
                MovementPause = (int)npc.movementPause,
                AddedSpeed = (int)npc.addedSpeed,
                CurrentAnimation = animation
            };

            npc.Sprite.StopAnimation();
            npc.Sprite.ClearAnimation();
            npc.Sprite.CurrentAnimation = null;
            npc.flip = false;
            npc.Sprite.CurrentFrame = GetNpcIdleFrameForDirection(npc.FacingDirection);

            string endOfRouteBehaviorName = TryGetNetStringField(npc, "endOfRouteBehaviorName");
            bool isFishingBehavior = !string.IsNullOrEmpty(endOfRouteBehaviorName)
                && endOfRouteBehaviorName.IndexOf("fish", StringComparison.OrdinalIgnoreCase) >= 0;

            if (isFishingBehavior)
            {
                NpcOutfitSpecialActionSnapshot snapshot = pending.SpecialActionSnapshot;
                snapshot.SavedIgnoreSourceRectUpdates = TryGetPrivateField(npc.Sprite, "ignoreSourceRectUpdates") as bool? ?? false;
                snapshot.SavedSpriteWidth = TryGetPrivateField(npc.Sprite, "spriteWidth") as int? ?? npc.Sprite.SpriteWidth;
                snapshot.SavedTempSpriteHeight = TryGetPrivateField(npc.Sprite, "tempSpriteHeight") as int? ?? -1;
                snapshot.HasSavedSpriteDimensions = true;

                TrySetPrivateField(npc.Sprite, "ignoreSourceRectUpdates", false);
                TrySetPrivateField(npc.Sprite, "spriteWidth", 16);
                TrySetPrivateField(npc.Sprite, "tempSpriteHeight", -1);

                snapshot.SavedDoingEndOfRouteAnimation = TryGetNetBoolField(npc, "doingEndOfRouteAnimation");
                snapshot.SavedCurrentlyDoingEndOfRouteAnimation = TryGetPrivateField(npc, "currentlyDoingEndOfRouteAnimation") as bool?;
                snapshot.SavedStartedEndOfRouteBehavior = endOfRouteBehaviorName;

                TrySetNetBoolField(npc, "doingEndOfRouteAnimation", false);
                TrySetPrivateField(npc, "currentlyDoingEndOfRouteAnimation", false);

                snapshot.SavedYOffset = TryGetPrivateField(npc, "yOffset") as float? ?? 0f;
                snapshot.SavedLoadedEndOfRouteBehavior = TryGetPrivateField(npc, "loadedEndOfRouteBehavior") as string;
                snapshot.SavedDrawOffset = TryGetPrivateField(npc, "drawOffset") as Vector2? ?? Vector2.Zero;
                snapshot.HasSavedRodLayerFields = true;

                TrySetPrivateField(npc, "yOffset", 0f);
                TrySetPrivateField(npc, "loadedEndOfRouteBehavior", null);
                TrySetPrivateField(npc, "drawOffset", Vector2.Zero);
            }

            npc.Sprite.UpdateSourceRect();
            if (ModEntry.DebugLog)
                monitor?.Log($"[NPC OUTFIT] Saved special animation for {npc.Name} before outfit reaction. frame={pending.SpecialActionSnapshot.CurrentFrame} anim={(animation != null ? animation.Count : 0)} fishing={isFishingBehavior}", LogLevel.Info);
        }

        public bool TryRestore(NPC npc, PendingPrompt pending, bool force = false)
        {
            if (pending == null || pending.SpecialActionSnapshot == null)
                return false;

            NpcOutfitSpecialActionSnapshot snapshot = pending.SpecialActionSnapshot;
            npc ??= snapshot.Npc;
            if (npc == null || npc.Sprite == null || npc.currentLocation == null)
            {
                pending.SpecialActionSnapshot = null;
                return false;
            }
            if (npc != snapshot.Npc || npc.currentLocation != snapshot.Location)
            {
                pending.SpecialActionSnapshot = null;
                return false;
            }

            if (!force)
            {
                if (Game1.activeClickableMenu != null || Game1.dialogueUp)
                    return false;
                if (Game1.player != null && npc.currentLocation == Game1.player.currentLocation && DistanceToPlayer(npc) < RestoreDistance)
                    return false;
            }

            try
            {
                npc.FacingDirection = snapshot.FacingDirection;
                npc.flip = snapshot.Flip;
                npc.movementPause = snapshot.MovementPause;
                npc.addedSpeed = snapshot.AddedSpeed;

                if (snapshot.HasSavedSpriteDimensions)
                {
                    TrySetPrivateField(npc.Sprite, "spriteWidth", snapshot.SavedSpriteWidth);
                    TrySetPrivateField(npc.Sprite, "tempSpriteHeight", snapshot.SavedTempSpriteHeight);
                    TrySetPrivateField(npc.Sprite, "ignoreSourceRectUpdates", snapshot.SavedIgnoreSourceRectUpdates);
                    snapshot.HasSavedSpriteDimensions = false;
                }

                if (snapshot.SavedDoingEndOfRouteAnimation.HasValue)
                {
                    TrySetNetBoolField(npc, "doingEndOfRouteAnimation", snapshot.SavedDoingEndOfRouteAnimation.Value);
                    TrySetPrivateField(npc, "currentlyDoingEndOfRouteAnimation", snapshot.SavedCurrentlyDoingEndOfRouteAnimation ?? false);
                    snapshot.SavedDoingEndOfRouteAnimation = null;
                    snapshot.SavedCurrentlyDoingEndOfRouteAnimation = null;
                }

                if (snapshot.HasSavedRodLayerFields)
                {
                    TrySetPrivateField(npc, "yOffset", snapshot.SavedYOffset);
                    TrySetPrivateField(npc, "loadedEndOfRouteBehavior", snapshot.SavedLoadedEndOfRouteBehavior);
                    TrySetPrivateField(npc, "drawOffset", snapshot.SavedDrawOffset);
                    snapshot.HasSavedRodLayerFields = false;
                }

                if (snapshot.CurrentAnimation != null && snapshot.CurrentAnimation.Count > 0)
                {
                    npc.Sprite.CurrentAnimation = new List<FarmerSprite.AnimationFrame>(snapshot.CurrentAnimation);
                    TrySetPrivateField(npc.Sprite, "currentAnimationIndex", 0);
                    TrySetPrivateField(npc.Sprite, "timer", 0);
                }
                else
                {
                    npc.Sprite.StopAnimation();
                    npc.Sprite.ClearAnimation();
                    npc.Sprite.CurrentAnimation = null;
                }

                npc.Sprite.CurrentFrame = snapshot.CurrentFrame;
                npc.Sprite.UpdateSourceRect();
                if (ModEntry.DebugLog)
                    monitor?.Log($"[NPC OUTFIT] Restored special animation for {npc.Name} after outfit reaction. frame={snapshot.CurrentFrame} anim={(snapshot.CurrentAnimation != null ? snapshot.CurrentAnimation.Count : 0)}", LogLevel.Info);

                if (!string.IsNullOrEmpty(snapshot.SavedStartedEndOfRouteBehavior))
                {
                    string behaviorName = snapshot.SavedStartedEndOfRouteBehavior;
                    snapshot.SavedStartedEndOfRouteBehavior = null;
                    NPC npcForDelay = npc;
                    DelayedAction.functionAfterDelay(() => ResumeEndOfRouteBehavior(npcForDelay, behaviorName), 150);
                }

                pending.SpecialActionSnapshot = null;
                return true;
            }
            catch (Exception ex)
            {
                monitor?.Log($"[NPC OUTFIT] Could not restore special animation for {npc?.Name ?? "null"}: {ex.Message}", LogLevel.Warn);
                pending.SpecialActionSnapshot = null;
                return false;
            }
        }

        private void ResumeEndOfRouteBehavior(NPC npc, string behaviorName)
        {
            if (npc?.currentLocation == null || npc.Sprite == null)
                return;
            try
            {
                TrySetPrivateField(npc, "_startedEndOfRouteBehavior", behaviorName);
                MethodInfo method = npc.GetType().GetMethod("doMiddleAnimation", BindingFlags.Instance | BindingFlags.NonPublic);
                method?.Invoke(npc, new object[] { null });
            }
            catch (Exception ex)
            {
                monitor?.Log($"[NPC OUTFIT] Failed to re-run doMiddleAnimation for {npc.Name}: {ex}", LogLevel.Warn);
            }
        }

        private static float DistanceToPlayer(NPC npc)
        {
            return npc == null || Game1.player == null ? float.MaxValue : Vector2.Distance(npc.Position, Game1.player.Position);
        }

        private static int GetNpcIdleFrameForDirection(int facingDirection)
        {
            return facingDirection switch { 0 => 8, 1 => 4, 2 => 0, 3 => 12, _ => 0 };
        }

        private static void TrySetPrivateField(object target, string fieldName, object value)
        {
            if (target == null || string.IsNullOrWhiteSpace(fieldName))
                return;
            try
            {
                target.GetType().GetField(fieldName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)?.SetValue(target, value);
            }
            catch { }
        }

        private static object TryGetPrivateField(object target, string fieldName)
        {
            if (target == null || string.IsNullOrWhiteSpace(fieldName))
                return null;
            try
            {
                return target.GetType().GetField(fieldName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)?.GetValue(target);
            }
            catch { return null; }
        }

        private static void TrySetNetBoolField(object target, string fieldName, bool value)
        {
            object netField = TryGetPrivateField(target, fieldName);
            try
            {
                PropertyInfo valueProperty = netField?.GetType().GetProperty("Value");
                if (valueProperty?.CanWrite == true)
                    valueProperty.SetValue(netField, value);
            }
            catch { }
        }

        private static bool? TryGetNetBoolField(object target, string fieldName)
        {
            object netField = TryGetPrivateField(target, fieldName);
            try { return netField?.GetType().GetProperty("Value")?.GetValue(netField) as bool?; }
            catch { return null; }
        }

        private static string TryGetNetStringField(object target, string fieldName)
        {
            object netField = TryGetPrivateField(target, fieldName);
            try { return netField?.GetType().GetProperty("Value")?.GetValue(netField) as string; }
            catch { return null; }
        }
    }
}
