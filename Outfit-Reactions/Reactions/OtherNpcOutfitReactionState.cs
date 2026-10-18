using Microsoft.Xna.Framework;
using StardewValley;
using System.Collections.Generic;

namespace OutfitReactions
{
    /// <summary>Mutable state for one pending non-spouse outfit reaction.</summary>
    internal sealed class PendingPrompt
    {
        public int DialogueCountBeforePush { get; set; }
        public int DialogueCountAfterPush { get; set; }
        public int OriginalFacingDirection { get; set; }
        public Vector2 OriginalPosition { get; set; }
        public Point OriginalTile { get; set; }
        public int OriginalFrame { get; set; }
        public bool OriginalFlip { get; set; }
        public int OriginalMovementPause { get; set; }
        public bool OriginalFreezeMotion { get; set; }
        public int OriginalAddedSpeed { get; set; }
        public List<FarmerSprite.AnimationFrame> OriginalAnimation { get; set; }
        public bool HasOriginalVisualState { get; set; }
        public bool WasLookingAtPlayer { get; set; }
        public bool IsRomanticPartner { get; set; }
        public bool WasMovingWhenNoticed { get; set; }
        public bool RomanticApproachStarted { get; set; }
        public bool RomanticApproachFinished { get; set; }
        public bool RomanticHoldSuspendedForKiss { get; set; }
        public int ExternalKissProtectionTimer { get; set; }
        public bool IgnoreRomanticDistanceForPublicMultiKiss { get; set; }

        // Set when the walking peeking mechanic armed the reaction and when the player caught it.
        public bool CameFromPeeking { get; set; }
        public bool WasCaughtPeeking { get; set; }
        public int NoticeDelayTimer { get; set; }
        public bool DialogueQueued { get; set; }
        public bool NoticePauseActive { get; set; }
        public int PendingBubbleCooldown { get; set; }
        public bool PostDialogueRestoreApplied { get; set; }
        public bool PostDialogueLingerActive { get; set; }
        public int PostDialogueLingerTimer { get; set; }

        // Preserve the NPC's existing dialogue stack while the outfit line takes priority.
        public List<Dialogue> DialogueBackupBeforeOutfit { get; set; } = new();
        public bool HasDialogueBackup { get; set; }

        // Keep the outfit prompt alive across the temporary generation/final-dialogue handoff.
        public bool DialogueWasConsumed { get; set; }
        public bool SawDialogueMenuAfterConsumption { get; set; }
        public bool PromptClearedAfterFirstDialogueMenu { get; set; }
        public object FirstDialogueMenu { get; set; }
        public int FirstDialogueMenuTicks { get; set; }
        public int PromptKeepAliveTimer { get; set; }

        // Delay restoration because other dialogue systems may replace CurrentDialogue on close.
        public bool WaitingForPostDialogueRestore { get; set; }
        public int PostDialogueRestoreDelay { get; set; }
        public bool PostDialogueOutfitWasRead { get; set; }
        public bool WaitingForOwnAiFinalDialogue { get; set; }
        public bool EmoteFired { get; set; }
        public bool SuppressNoticeEmote { get; set; }

        // Restore the first-talk flag if the outfit reaction is cancelled before being read.
        public bool HasFriendshipEntry { get; set; }
        public bool OriginalTalkedToToday { get; set; }
        public bool ForcedTalkedToToday { get; set; }
        public NpcOutfitSpecialActionSnapshot SpecialActionSnapshot { get; set; }
    }

    /// <summary>Snapshot used to suspend and later restore a non-spouse NPC special action.</summary>
    internal sealed class NpcOutfitSpecialActionSnapshot
    {
        public NPC Npc { get; set; }
        public GameLocation Location { get; set; }
        public int FacingDirection { get; set; }
        public int CurrentFrame { get; set; }
        public bool Flip { get; set; }
        public int MovementPause { get; set; }
        public int AddedSpeed { get; set; }
        public List<FarmerSprite.AnimationFrame> CurrentAnimation { get; set; }

        // Fishing end-of-route animations stretch the source rectangle across two rows.
        // Save these dimensions so the hold pose can use a normal frame and restore fishing later.
        public bool HasSavedSpriteDimensions { get; set; }
        public bool SavedIgnoreSourceRectUpdates { get; set; }
        public int SavedSpriteWidth { get; set; }
        public int SavedTempSpriteHeight { get; set; }

        // These are NetBool-backed fields. They must be restored as such to avoid vanilla
        // repeatedly restarting the end-of-route animation over the held idle frame.
        public bool? SavedDoingEndOfRouteAnimation { get; set; }
        public bool? SavedCurrentlyDoingEndOfRouteAnimation { get; set; }

        // Used to re-invoke doMiddleAnimation so special actions such as fishing resume.
        public string SavedStartedEndOfRouteBehavior { get; set; }

        // The fishing rod is a separate layer; preserving these fields prevents a ghost rod
        // or shifted sprite while the NPC is temporarily held for the outfit reaction.
        public bool HasSavedRodLayerFields { get; set; }
        public float SavedYOffset { get; set; }
        public string SavedLoadedEndOfRouteBehavior { get; set; }
        public Vector2 SavedDrawOffset { get; set; }
    }

    /// <summary>State for an NPC temporarily glancing at the player while walking.</summary>
    internal sealed class SpyingState
    {
        public int OriginalFacingDirection { get; set; }
        // True while the player is looking at the NPC and has caught the glance.
        public bool IsBeingWatched { get; set; }
        // Brief look-away period before the NPC may peek again.
        public int PretendTimer { get; set; }
        public bool WasEverCaught { get; set; }
        // Lets the NPC walk normally for a while after the player looks away.
        public int WalkCooldownTimer { get; set; }
        // Prevents an immediate caught reaction on the same tick the glance begins.
        public int PeekGraceTimer { get; set; }
    }
}
