using StardewModdingAPI;
using StardewValley;
using System;
using System.Collections.Generic;
using System.Linq;

namespace OutfitReactions
{
    /// <summary>
    /// Unified outfit reactions for villagers, including dating partners and spouses.
    /// Romantic partners may temporarily approach the player through the isolated approach controller;
    /// other villagers keep their schedules untouched. Saved outfits remain eligible until each NPC
    /// actually reads the current pending outfit compliment.
    /// </summary>
    internal sealed class OtherNpcClothesReactionSystem
    {
        private readonly IMonitor monitor;
        private readonly Func<ModConfig> getConfig;
        private readonly Func<NPC, bool> tryQueueOutfitDialogue;
        private readonly Func<NPC, bool> refreshOutfitPrompt;
        private readonly Action<NPC> clearOutfitPrompt;
        private readonly Func<bool> hasNoticeableCurrentFashionSenseAppearance;
        private readonly Func<NPC, bool> canNoticeCurrentOutfitNotice;
        private readonly Action<NPC> markCurrentOutfitAsNoticed;
        private readonly Func<NPC, bool> canNpcReactToOutfit;
        private readonly Func<NPC, bool> hasNpcSeenCurrentVisualBefore;
        private readonly Func<NPC, bool> isRomanticPartner;
        private readonly Func<bool> shouldDeferAutomaticNotices;
        private readonly Func<NPC, PendingPrompt, bool> tryBeginPostDialogueAction;
        private readonly NpcSpecialActionController specialActionController;
        private readonly NpcPeekingController peekingController;
        private readonly RomanticPartnerApproachController romanticApproachController;
        private readonly Random random = new();
        // The discovery scan (foreach over every NPC in the location, checking distance/facing/
        // line-of-sight for new notices) is the expensive part of Update(). It's throttled to run
        // a few times a second instead of every tick — noticing an outfit doesn't need 60fps
        // precision, and this is what scales with how many NPCs are in the location (e.g. the
        // village once schedules start filling it up), unlike the cheap per-tick bookkeeping below.
        private int discoveryScanTimer;
        private const int DiscoveryScanIntervalTicks = 6; // ~10 scans/sec

        private const int FailedRollCooldownTicks = 900;
        private const int CancelledReactionCooldownTicks = 600;
        private const float OutfitNoticePauseDistance = 96f;       // roughly one tile / adjacent interaction distance
        private const float OutfitNoticeReleaseDistance = 300f;    // stop refreshing movementPause after this
        private const float PostDialogueLingerDistance = 600f;
        private const int PostDialogueLingerTicks = 360;           // ~6 seconds
        private const float NpcSpecialActionRestoreDistance = 300f;
        private const int PendingBubbleCooldownTicks = 240;
        private const float RomanticWalkingHoldDistance = 300f;
        private const float RomanticPendingCancelDistance = RomanticPartnerApproachController.NoticeDistance;

        private readonly HashSet<string> reactedNpcsThisOutfit = new(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> festivalRollAttemptedNpcsThisOutfit = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, PendingPrompt> pendingPrompts = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, int> rollCooldowns = new(StringComparer.OrdinalIgnoreCase);

        public OtherNpcClothesReactionSystem(
            IMonitor monitor,
            Func<ModConfig> getConfig,
            Func<NPC, bool> tryQueueOutfitDialogue,
            Func<NPC, bool> refreshOutfitPrompt,
            Action<NPC> clearOutfitPrompt,
            Func<bool> hasNoticeableCurrentFashionSenseAppearance,
            Func<NPC, bool> canNoticeCurrentOutfitNotice,
            Action<NPC> markCurrentOutfitAsNoticed,
            Func<NPC, bool> canNpcReactToOutfit,
            Func<NPC, bool> hasNpcSeenCurrentVisualBefore,
            Func<NPC, bool> isRomanticPartner,
            Func<bool> shouldDeferAutomaticNotices,
            Func<GameLocation, int, int, bool> isVisionIgnoredTile,
            Func<NPC, PendingPrompt, bool> tryBeginPostDialogueAction = null)
        {
            this.monitor = monitor;
            this.getConfig = getConfig;
            this.tryQueueOutfitDialogue = tryQueueOutfitDialogue;
            this.refreshOutfitPrompt = refreshOutfitPrompt;
            this.clearOutfitPrompt = clearOutfitPrompt;
            this.hasNoticeableCurrentFashionSenseAppearance = hasNoticeableCurrentFashionSenseAppearance;
            this.canNoticeCurrentOutfitNotice = canNoticeCurrentOutfitNotice;
            this.markCurrentOutfitAsNoticed = markCurrentOutfitAsNoticed;
            this.canNpcReactToOutfit = canNpcReactToOutfit;
            this.hasNpcSeenCurrentVisualBefore = hasNpcSeenCurrentVisualBefore;
            this.isRomanticPartner = isRomanticPartner;
            this.shouldDeferAutomaticNotices = shouldDeferAutomaticNotices;
            this.tryBeginPostDialogueAction = tryBeginPostDialogueAction;
            this.specialActionController = new NpcSpecialActionController(monitor);
            this.peekingController = new NpcPeekingController(random, pendingPrompts, ArmPendingReactionForSpy, isVisionIgnoredTile);
            this.romanticApproachController = new RomanticPartnerApproachController(monitor);
        }

        public void Reset(bool clearPrompts = true)
        {
            romanticApproachController.Reset(restoreIfPossible: true);

            if (clearPrompts)
                ClearAllPendingPrompts(removeQueuedDialogues: true);

            reactedNpcsThisOutfit.Clear();
            festivalRollAttemptedNpcsThisOutfit.Clear();
            pendingPrompts.Clear();
            rollCooldowns.Clear();
            peekingController.Clear();
        }

        public void NotifyOutfitChanged()
        {
            romanticApproachController.Reset(restoreIfPossible: true);
            ClearAllPendingPrompts(removeQueuedDialogues: true);

            reactedNpcsThisOutfit.Clear();
            festivalRollAttemptedNpcsThisOutfit.Clear();
            pendingPrompts.Clear();
            rollCooldowns.Clear();
            peekingController.Clear();
            // Force the next Update() call to run the discovery scan immediately instead of
            // waiting out the throttle interval on top of the 200ms delay already applied before
            // this is called.
            discoveryScanTimer = 0;

            // The current saved outfit remains eligible until each NPC actually reads its
            // outfit compliment for the current notice. No short notice window is needed anymore.
        }

        public void Update()
        {
            if (!Context.IsWorldReady || Game1.player == null || Game1.currentLocation == null)
                return;

            ModConfig config = getConfig?.Invoke();
            if (config == null)
                return;

            romanticApproachController.Update();
            UpdateRollCooldowns();
            UpdatePendingPrompts(config);

            if (!config.EnableNpcOutfitReactions)
                return;

            // Do not let NPCs notice/react unless the player is actually wearing
            // a saved Fashion Sense outfit or an active Fashion Sense hairstyle.
            // This prevents vanilla/default clothing on a new save from triggering
            // emotes, looking, and outfit dialogue just because the NPC has hearts.
            if (hasNoticeableCurrentFashionSenseAppearance?.Invoke() != true)
                return;

            if (Game1.activeClickableMenu != null)
                return;

            float noticeDistance = Math.Max(64f, config.OutfitNoticeDistance);
            float cancelDistance = Math.Max(noticeDistance, config.OutfitCancelDistance);

            // Tick the peeping mechanic for all NPCs currently mid-spy. This only touches NPCs
            // already in the peeking controller (a small set), so it stays smooth every tick.
            peekingController.Update(noticeDistance, cancelDistance);

            int newVisualChance = Math.Clamp(config.NpcOutfitReactionChance, 0, 100);
            int repeatedVisualChance = Math.Clamp(config.NpcRepeatedVisualNoticeChance, 0, 100);
            if (newVisualChance <= 0 && repeatedVisualChance <= 0)
                return;

            // Throttle the expensive discovery scan below — it walks every NPC in the location on
            // every call, so running it 60x/sec cost scales directly with how many NPCs are around.
            if (discoveryScanTimer > 0)
            {
                discoveryScanTimer--;
                return;
            }
            discoveryScanTimer = DiscoveryScanIntervalTicks;

            // Keep existing pending reactions alive, but don't let a festival minigame or timed
            // competition create new notices while the player is actively participating.
            if (shouldDeferAutomaticNotices?.Invoke() == true)
                return;

            foreach (NPC npc in NpcContextResolver.GetCurrentLocationNpcs().ToList())
            {
                if (!IsValidNpc(npc))
                    continue;

                bool romanticPartner = isRomanticPartner?.Invoke(npc) == true;
                float npcNoticeDistance = romanticPartner
                    ? RomanticPartnerApproachController.NoticeDistance
                    : noticeDistance;

                if (pendingPrompts.ContainsKey(npc.Name))
                    continue;

                if (romanticPartner && romanticApproachController.IsHandling(npc))
                    continue;

                // Robust "is this NPC walking its schedule?" check. npc.isMoving() flickers false for
                // a tick at tile boundaries/turns, which previously let the standing % sneak in on a
                // walking NPC. We smooth it with a small grace window: track how long since they last
                // actually moved, and treat them as walking until they've been still for a while.
                // (NPCs mid-peek are paused on purpose and handled separately, so skip their tracking.)
                peekingController.UpdateMovementTracking(npc);
                bool npcIsWalking = peekingController.IsRecentlyMoving(npc.Name);

                // A route may remove its controller just before vanilla (or the schedule mod)
                // applies the final destination pose. Don't let that transition become the
                // protected stationary snapshot merely because the player approached quickly.
                if (romanticPartner
                    && npc.controller == null
                    && npc.temporaryController == null)
                {
                    if (!peekingController.HasStableStationaryState(npc.Name))
                        continue;

                    // Save the settled pose before distance, facing, line-of-sight, and random
                    // notice checks. Approaching later can no longer replace it with a pose that
                    // another system turned toward the player in response to their proximity.
                    romanticApproachController.ObserveStationaryOrigin(npc);
                }

                // ── Peeping mechanic (NPC is walking) ────────────────────────────
                // While an NPC is actively moving through their schedule and the player
                // is in range, give them a small chance on each throttled discovery scan to
                // stop to peek. Peeking IS the "notice" for a walking NPC: it arms the same
                // pending reaction, so if the player goes over and clicks, the NPC reacts.
                // Only movementPause is used — no controller/schedule interference.
                // Cheap checks first, expensive line-of-sight raycast last: the roll usually fails,
                // so the tile-by-tile raycast only runs when a peek may actually begin.
                if (!romanticPartner && npcIsWalking
                    && !peekingController.Contains(npc.Name)
                    && !reactedNpcsThisOutfit.Contains(npc.Name)
                    && DistanceToPlayer(npc) <= noticeDistance
                    // This runs on the throttled discovery scan (~8.5 scans/second), so 2%
                    // restores the intended average of roughly one peek every 5-7 seconds.
                    && random.Next(100) < 2
                    && HasLineOfSightToPlayer(npc))
                {
                    peekingController.Begin(npc);
                    // Peeking counts as noticing: arm the pending reaction (no bubble/pause here —
                    // the peeking visuals are driven by NpcPeekingController) so a click still reacts.
                    ArmPendingReactionForSpy(npc);
                    if (ModEntry.DebugLog)
                        monitor?.Log($"[NPC OUTFIT] {npc.Name} noticed the outfit while walking and started peeking.", LogLevel.Info);
                }

                // NPCs already in the spying state are handled by NpcPeekingController;
                // skip the normal standing-reaction logic while they're peeking.
                if (peekingController.Contains(npc.Name))
                    continue;
                // ─────────────────────────────────────────────────────────────────

                // Normal reaction only fires when the NPC is STANDING still (no movement and no
                // active walk route). Walking NPCs are handled exclusively by the peeping mechanic.
                if (!romanticPartner && npcIsWalking)
                    continue;

                // Notice the current look at most once until it changes (NotifyOutfitChanged clears
                // this) or the day resets. This prevents re-noticing the same continuous outfit over
                // and over. Whether they notice at all is decided by the new/repeated chance below.
                if (reactedNpcsThisOutfit.Contains(npc.Name))
                    continue;

                // Repeated visuals are no longer hard-blocked. Instead, an NPC that has already
                // seen the player's current look uses the (usually lower) "repeated visual" chance,
                // while a brand-new look uses the normal "new visual" chance.
                bool seenBefore = hasNpcSeenCurrentVisualBefore?.Invoke(npc) ?? false;
                int chance = romanticPartner && config.RomanticPartnersAlwaysNoticeOutfitChanges
                    ? 100
                    : (seenBefore ? repeatedVisualChance : newVisualChance);
                if (chance <= 0)
                    continue;

                if (DistanceToPlayer(npc) > npcNoticeDistance)
                    continue;

                // Only notice if the NPC is roughly facing the player AND has an unobstructed line of
                // sight — walls, closed doors, and solid tiles block the notice, so an NPC in a
                // closed room of the same location (e.g. Penny in her room in the trailer) won't
                // react even if they're within the notice distance.
                // Cheap facing check first: it eliminates every NPC facing away from the player
                // without touching the map, so the tile-by-tile line-of-sight raycast only runs
                // for NPCs that could plausibly notice.
                if (!IsNpcFacingPlayer(npc))
                    continue;

                if (!HasLineOfSightToPlayer(npc))
                    continue;

                if (rollCooldowns.TryGetValue(npc.Name, out int cooldown) && cooldown > 0)
                    continue;

                if (random.Next(100) >= chance)
                {
                    rollCooldowns[npc.Name] = FailedRollCooldownTicks;
                    continue;
                }

                TryStartReaction(npc, config, romanticPartner, npcIsWalking);
            }
        }

        private bool IsValidNpc(NPC npc)
        {
            if (npc == null || Game1.player == null)
                return false;

            if (npc.currentLocation != Game1.player.currentLocation)
                return false;

            if (!npc.IsVillager)
                return false;

            if (npc.IsInvisible || npc.isSleeping.Value)
                return false;

            if (canNpcReactToOutfit?.Invoke(npc) != true)
                return false;

            return true;
        }

        private void TryStartReaction(NPC npc, ModConfig config, bool romanticPartner, bool wasMovingWhenNoticed)
        {
            if (npc == null)
                return;

            PendingPrompt pending = CreatePendingPrompt(npc);
            pending.IsRomanticPartner = romanticPartner;
            pending.WasMovingWhenNoticed = wasMovingWhenNoticed;
            pending.NoticeDelayTimer = 75;

            reactedNpcsThisOutfit.Add(npc.Name);
            pendingPrompts[npc.Name] = pending;
            // If this NPC was peeping, end the spy state cleanly — the full reaction takes over.
            peekingController.Remove(npc.Name);

            if (romanticPartner)
                pending.RomanticApproachStarted = romanticApproachController.TryBegin(npc, pending, wasMovingWhenNoticed);

            ShowPendingDialogueBubbleIfNeeded(npc, pending, config, force: true);
            if (!pending.RomanticApproachStarted)
                UpdateNpcLookState(npc, pending, config);

            // Do NOT generate/queue the AI line here. The NPC has only noticed the outfit.
            // The expensive AI call starts when the player actually clicks this NPC.
            if (ModEntry.DebugLog) monitor?.Log($"[NPC OUTFIT] {npc.Name} noticed the outfit. Outfit dialogue is pending until player click.", LogLevel.Info);
        }

        /// <summary>
        /// Arms a pending outfit reaction for an NPC that just started peeking while walking, WITHOUT
        /// the normal notice bubble or movement pause. The peeking visuals (stop, turn, glance away)
        /// are driven entirely by NpcPeekingController; this only records the pending state
        /// so that if the player walks over and clicks, the full reaction plays just like a standing
        /// notice. The reaction is marked as already-noticed for this outfit to avoid double-noticing.
        /// </summary>
        private void ArmPendingReactionForSpy(NPC npc)
        {
            if (npc == null)
                return;

            PendingPrompt pending = CreatePendingPrompt(npc);
            pending.CameFromPeeking = true;
            pending.WasMovingWhenNoticed = true;
            pending.NoticeDelayTimer = 0;

            reactedNpcsThisOutfit.Add(npc.Name);
            pendingPrompts[npc.Name] = pending;
            // No ShowPendingDialogueBubbleIfNeeded / UpdateNpcLookState here on purpose: while peeking,
            // the NPC's stop/turn/glance is controlled by NpcPeekingController, not the standing-notice path.
        }

        private bool TryQueuePromptAfterNotice(NPC npc, PendingPrompt pending)
        {
            if (npc == null || pending == null)
                return false;

            if (pending.DialogueQueued)
                return true;

            CaptureQueuedDialoguesBeforeOutfit(npc, pending);
            TemporarilySkipFirstDailyDialogue(npc, pending);

            if (tryQueueOutfitDialogue?.Invoke(npc) != true)
            {
                RestoreQueuedDialoguesAfterOutfit(npc, pending, clearCurrentDialogue: true);
                RestoreTalkedToTodayIfUnread(npc, pending);
                rollCooldowns[npc.Name] = FailedRollCooldownTicks;
                return false;
            }

            int dialogueCountAfterPush = npc.CurrentDialogue.Count;
            if (dialogueCountAfterPush <= 0)
            {
                RestoreQueuedDialoguesAfterOutfit(npc, pending, clearCurrentDialogue: true);
                RestoreTalkedToTodayIfUnread(npc, pending);
                rollCooldowns[npc.Name] = FailedRollCooldownTicks;
                return false;
            }

            pending.DialogueQueued = true;
            pending.DialogueCountBeforePush = Math.Max(0, dialogueCountAfterPush - 1);
            pending.DialogueCountAfterPush = dialogueCountAfterPush;
            refreshOutfitPrompt?.Invoke(npc);

            if (ModEntry.DebugLog) monitor?.Log($"[NPC OUTFIT] {npc.Name}'s outfit dialogue is now ready after the notice beat.", LogLevel.Info);
            return true;
        }

        /// <summary>
        /// True if the given NPC's currently-pending outfit reaction was armed by the walking peeping
        /// mechanic AND the player caught them in the act. Lets the AI reaction add a "you caught me
        /// looking…" flavor. Returns false for normal (standing) notices or uncaught peeks.
        /// </summary>
        public bool WasNpcCaughtPeeking(NPC npc)
        {
            if (npc == null)
                return false;
            bool result = pendingPrompts.TryGetValue(npc.Name, out PendingPrompt pending)
                && pending != null
                && pending.CameFromPeeking
                && pending.WasCaughtPeeking;
            return result;
        }

        public bool TryPrioritizePendingDialogueForClick(NPC npc)
        {
            if (npc == null)
                return false;

            if (!pendingPrompts.TryGetValue(npc.Name, out PendingPrompt pending) || pending == null)
                return false;

            // The public multi-kiss interruption keeps an outfit reaction pending while the
            // partner resumes their route, even beyond the normal romantic 1000f boundary.
            // The exception ends only when the player deliberately clicks to read this reaction.
            pending.IgnoreRomanticDistanceForPublicMultiKiss = false;
            romanticApproachController.NotifyInteraction(npc);

            if (pending.DialogueWasConsumed)
            {
                // Once the click has started built-in AI generation, keep Stardew's normal
                // dialogue stack inaccessible until our final line has actually opened.
                // The stack itself remains untouched and will be available afterwards.
                return pending.WaitingForOwnAiFinalDialogue;
            }

            // The player engaged this NPC — the peeking "game" is over; the full reaction takes over.
            // First carry over whether they were ever caught peeking, so the AI can flavor the line.
            if (peekingController.TryGetState(npc.Name, out SpyingState spyState) && spyState != null)
            {
                pending.WasCaughtPeeking = spyState.WasEverCaught;
            }
            peekingController.Remove(npc.Name);

            if (pending.NoticeDelayTimer > 0)
                pending.NoticeDelayTimer = 0;

            ModConfig config = getConfig?.Invoke();
            bool likelyOwnAiClickWait = config != null && tryQueueOutfitDialogue != null;

            // This is called from the Harmony prefix just before Stardew handles the talk click.
            // Queue the outfit reaction only now, so AI generation never starts on the notice/emote tick.
            CaptureQueuedDialoguesBeforeOutfit(npc, pending);

            int originalDialogueCount = npc.CurrentDialogue?.Count ?? 0;

            // For built-in AI-on-click, do NOT clear CurrentDialogue or mark TalkedToToday here.
            // The final AI line will clear/push itself when ready, and this pending prompt will
            // restore the original dialogue afterwards. Clearing here made normal NPC dialogue
            // disappear while slow local models were still generating.
            if (!likelyOwnAiClickWait)
            {
                TemporarilySkipFirstDailyDialogue(npc, pending);
                npc.CurrentDialogue.Clear();
            }

            if (tryQueueOutfitDialogue?.Invoke(npc) != true)
            {
                if (!likelyOwnAiClickWait)
                    RestoreQueuedDialoguesAfterOutfit(npc, pending, clearCurrentDialogue: true);
                RestoreTalkedToTodayIfUnread(npc, pending);
                return false;
            }

            pending.DialogueQueued = true;

            // Built-in Outfit Compliments AI starts generation on click without pushing a
            // temporary DialogueBox. In that case CurrentDialogue either stays empty or keeps
            // the original NPC dialogue stack untouched while the lower-HUD waiting message is drawn.
            if (likelyOwnAiClickWait && (npc.CurrentDialogue == null || npc.CurrentDialogue.Count <= originalDialogueCount))
            {
                pending.DialogueWasConsumed = true;
                pending.WaitingForOwnAiFinalDialogue = true;
                pending.PromptKeepAliveTimer = Math.Max(pending.PromptKeepAliveTimer, 900);
                pending.PostDialogueOutfitWasRead = false;
                if (ModEntry.DebugLog) monitor?.Log($"[NPC OUTFIT] Started built-in AI outfit generation for {npc.Name} at click time; kept original dialogue stack untouched until final line is ready.", LogLevel.Info);
                return true;
            }

            if (npc.CurrentDialogue == null || npc.CurrentDialogue.Count <= 0)
            {
                pending.DialogueWasConsumed = true;
                pending.WaitingForOwnAiFinalDialogue = true;
                pending.PromptKeepAliveTimer = Math.Max(pending.PromptKeepAliveTimer, 900);
                pending.PostDialogueOutfitWasRead = false;
                if (ModEntry.DebugLog) monitor?.Log($"[NPC OUTFIT] Started built-in AI outfit generation for {npc.Name} at click time; no temporary dialogue box was queued.", LogLevel.Info);
                return true;
            }

            pending.DialogueCountBeforePush = Math.Max(0, npc.CurrentDialogue.Count - 1);
            pending.DialogueCountAfterPush = npc.CurrentDialogue.Count;
            refreshOutfitPrompt?.Invoke(npc);

            if (ModEntry.DebugLog) monitor?.Log($"[NPC OUTFIT] Queued outfit dialogue for {npc.Name} at click time.", LogLevel.Info);
            return true;
        }

        public bool TryPrioritizeFestivalDialogueForClick(NPC npc)
        {
            if (npc == null)
                return false;

            // A successful festival roll may already be generating its dialogue. Keep that
            // existing reaction prioritized without rolling again on subsequent input events.
            if (pendingPrompts.ContainsKey(npc.Name))
                return TryPrioritizePendingDialogueForClick(npc);

            ModConfig config = getConfig?.Invoke();
            if (config == null
                || !config.EnableNpcOutfitReactions
                || hasNoticeableCurrentFashionSenseAppearance?.Invoke() != true
                || !IsValidNpc(npc)
                || canNoticeCurrentOutfitNotice?.Invoke(npc) != true
                || reactedNpcsThisOutfit.Contains(npc.Name)
                || festivalRollAttemptedNpcsThisOutfit.Contains(npc.Name))
            {
                return false;
            }

            // Festivals get one independent attempt per NPC for the current visual. A failed
            // roll immediately gives the click back to Stardew's normal festival dialogue.
            festivalRollAttemptedNpcsThisOutfit.Add(npc.Name);
            int chance = Math.Clamp(config.FestivalNpcOutfitReactionChance, 0, 100);
            if (chance <= 0 || random.Next(100) >= chance)
            {
                if (ModEntry.DebugLog)
                    monitor?.Log($"[FESTIVAL OUTFIT] {npc.Name} did not notice the current visual on interaction ({chance}% chance).", LogLevel.Info);
                return false;
            }

            PendingPrompt pending = CreatePendingPrompt(npc);
            pending.IsRomanticPartner = isRomanticPartner?.Invoke(npc) == true;
            pending.NoticeDelayTimer = 0;
            pending.SuppressNoticeEmote = true;

            reactedNpcsThisOutfit.Add(npc.Name);
            pendingPrompts[npc.Name] = pending;

            if (ModEntry.DebugLog)
                monitor?.Log($"[FESTIVAL OUTFIT] {npc.Name} noticed the current visual on interaction without an emote; normal look-and-restore behavior remains active.", LogLevel.Info);

            return TryPrioritizePendingDialogueForClick(npc);
        }

        public void SuspendRomanticHoldForExternalKiss(NPC npc)
        {
            if (npc == null
                || !pendingPrompts.TryGetValue(npc.Name, out PendingPrompt pending)
                || pending == null
                || !pending.IsRomanticPartner)
            {
                return;
            }

            pending.RomanticHoldSuspendedForKiss = true;
            pending.ExternalKissProtectionTimer = 90;
            pending.NoticePauseActive = false;
            romanticApproachController.SetSuspended(npc, suspended: true);
            npc.movementPause = 0;

            if (ModEntry.DebugLog)
                monitor?.Log($"[NPC OUTFIT] Suspended romantic outfit hold for Lots of Kisses animation with {npc.Name}.", LogLevel.Info);
        }

        private void UpdateRollCooldowns()
        {
            if (rollCooldowns.Count <= 0)
                return;

            foreach (string npcName in rollCooldowns.Keys.ToList())
            {
                rollCooldowns[npcName]--;

                if (rollCooldowns[npcName] <= 0)
                    rollCooldowns.Remove(npcName);
            }
        }

        private void UpdatePendingPrompts(ModConfig config)
        {
            if (pendingPrompts.Count <= 0)
                return;

            float normalCancelDistance = Math.Max(
                Math.Max(64f, config.OutfitNoticeDistance) + 64f,
                config.OutfitCancelDistance
            );

            foreach (string npcName in pendingPrompts.Keys.ToList())
            {
                PendingPrompt pending = pendingPrompts[npcName];
                float cancelDistance = pending.IsRomanticPartner
                    ? RomanticPendingCancelDistance
                    : normalCancelDistance;
                NPC npc = NpcContextResolver.Resolve(npcName);

                if (npc == null)
                {
                    pendingPrompts.Remove(npcName);
                    continue;
                }

                if (pending.IsRomanticPartner
                    && npc.modData.ContainsKey(ModEntry.PublicMultiKissInterruptionModDataKey)
                    && !pending.IgnoreRomanticDistanceForPublicMultiKiss)
                {
                    // Latch the exception because Lots of Kisses removes its transient marker
                    // after the blush emote. Outfit Reactions keeps the pending dialogue until
                    // the player clicks it, but no longer holds or poses the walking partner.
                    pending.IgnoreRomanticDistanceForPublicMultiKiss = true;
                    pending.NoticePauseActive = false;
                    pending.RomanticHoldSuspendedForKiss = false;
                    pending.ExternalKissProtectionTimer = 0;
                    pending.WasLookingAtPlayer = false;
                    pending.SpecialActionSnapshot = null;
                    romanticApproachController.ReleaseForExternalAction(npc);
                }

                if (pending.IsRomanticPartner
                    && romanticApproachController.TryConsumeOutcome(npc, out RomanticPartnerApproachOutcome approachOutcome))
                {
                    pending.RomanticApproachStarted = false;
                    pending.RomanticApproachFinished = true;

                    if (approachOutcome == RomanticPartnerApproachOutcome.CancelPending)
                    {
                        CancelPendingPrompt(npc, pending);
                        pendingPrompts.Remove(npcName);
                        continue;
                    }
                }

                if (pending.PendingBubbleCooldown > 0)
                    pending.PendingBubbleCooldown--;

                if (pending.WaitingForPostDialogueRestore)
                {
                    UpdatePostDialogueRestore(npc, pending);

                    if (!pending.WaitingForPostDialogueRestore)
                        pendingPrompts.Remove(npcName);

                    continue;
                }

                // The NPC should visibly notice the outfit first (emote/look), then the
                // outfit dialogue becomes available. This prevents the click-priority patch
                // from feeling like an instant/direct dialogue with no reaction beat.
                if (!pending.DialogueQueued)
                {
                    bool invalidLocation = Game1.player == null || npc.currentLocation != Game1.player.currentLocation;
                    bool beyondCancelDistance = !invalidLocation
                        && !pending.IgnoreRomanticDistanceForPublicMultiKiss
                        && !romanticApproachController.IsHandling(npc)
                        && DistanceToPlayer(npc) > cancelDistance;
                    if (invalidLocation || beyondCancelDistance)
                    {
                        CancelPendingPrompt(npc, pending);
                        pendingPrompts.Remove(npcName);
                        continue;
                    }

                    UpdateNpcLookState(npc, pending, config);
                    ShowPendingDialogueBubbleIfNeeded(npc, pending, config);

                    if (pending.NoticeDelayTimer > 0)
                    {
                        pending.NoticeDelayTimer--;
                        continue;
                    }

                    // The outfit reaction is now intentionally queued on click, not here.
                    // Keep the NPC looking/interested while the pending click window is active.
                    continue;
                }

                if (pending.WaitingForOwnAiFinalDialogue)
                {
                    // The visible waiting line was opened, but the background AI result is not ready yet.
                    // Do not restore the old dialogue stack until ModEntry opens the final generated line.
                    UpdateNpcLookState(npc, pending, config);
                    ShowPendingDialogueBubbleIfNeeded(npc, pending, config);
                    continue;
                }

                // If the queued outfit AI generation tag was consumed, do NOT clear the
                // prompt override immediately. Outfit Compliments AI may still be turning that tag into
                // the final dialogue line, and clearing here can make it fall back to a casual
                // conversation. This mirrors the spouse flow, where the prompt is only cleared
                // after the dialogue is done.
                if (pending.DialogueWasConsumed || npc.CurrentDialogue.Count < pending.DialogueCountAfterPush)
                {
                    KeepConsumedDialoguePromptAlive(npc, pending);

                    if (ShouldClearConsumedPrompt(pending))
                    {
                        clearOutfitPrompt?.Invoke(npc);
                        SchedulePostDialogueRestore(pending, outfitWasRead: pending.SawDialogueMenuAfterConsumption);
                    }

                    continue;
                }

                // If the farmer moved too far away or changed maps, cancel this pending reaction.
                bool queuedInvalidLocation = Game1.player == null || npc.currentLocation != Game1.player.currentLocation;
                bool queuedBeyondCancelDistance = !queuedInvalidLocation
                    && !pending.IgnoreRomanticDistanceForPublicMultiKiss
                    && !romanticApproachController.IsHandling(npc)
                    && DistanceToPlayer(npc) > cancelDistance;
                if (queuedInvalidLocation || queuedBeyondCancelDistance)
                {
                    CancelPendingPrompt(npc, pending);
                    pendingPrompts.Remove(npcName);
                    continue;
                }

                // Keep the outfit-specific Outfit Compliments AI prompt alive until the player reads it.
                // Some Outfit Compliments AI flows rebuild their prompt right when the dialogue starts, so
                // registering only when the emote appears can fall back to a casual conversation.
                refreshOutfitPrompt?.Invoke(npc);

                UpdateNpcLookState(npc, pending, config);
                ShowPendingDialogueBubbleIfNeeded(npc, pending, config);
            }
        }


        private void KeepConsumedDialoguePromptAlive(NPC npc, PendingPrompt pending)
        {
            if (npc == null || pending == null)
                return;

            if (!pending.DialogueWasConsumed)
            {
                pending.DialogueWasConsumed = true;
                pending.PromptKeepAliveTimer = 900; // fallback while Outfit Compliments AI turns the generation tag into the first line

                if (pending.WasLookingAtPlayer)
                    FaceDirectionIfSafe(npc, pending.OriginalFacingDirection);

                if (ModEntry.DebugLog) monitor?.Log($"[NPC OUTFIT] {npc.Name}'s outfit prompt was consumed; keeping Outfit Compliments AI override alive until the first real reply/input step.", LogLevel.Info);
            }

            if (pending.PromptKeepAliveTimer > 0)
                pending.PromptKeepAliveTimer--;

            object currentMenu = Game1.activeClickableMenu;

            if (currentMenu != null)
            {
                pending.SawDialogueMenuAfterConsumption = true;

                if (pending.FirstDialogueMenu == null)
                {
                    pending.FirstDialogueMenu = currentMenu;
                    pending.FirstDialogueMenuTicks = 0;
                }
                else if (ReferenceEquals(pending.FirstDialogueMenu, currentMenu))
                {
                    pending.FirstDialogueMenuTicks++;
                }
                else if (!pending.PromptClearedAfterFirstDialogueMenu && LooksLikeReplyOrInputMenu(currentMenu))
                {
                    // Do NOT clear just because the menu object changed. Outfit Compliments AI can swap
                    // from its generation tag dialogue box to the final generated dialogue box,
                    // and clearing during that handoff makes the outfit compliment disappear.
                    // Only clear once Outfit Compliments AI has moved to a reply/input style menu, so the
                    // player's typed response is no longer forced to use the outfit-notice prompt.
                    clearOutfitPrompt?.Invoke(npc);
                    pending.PromptClearedAfterFirstDialogueMenu = true;
                    if (ModEntry.DebugLog) monitor?.Log($"[NPC OUTFIT] Cleared outfit prompt for {npc.Name} when Outfit Compliments AI opened a reply/input menu.", LogLevel.Info);
                    return;
                }
                else
                {
                    // Outfit Compliments AI changed from one non-reply dialogue menu to another. Treat
                    // the new one as the current first dialogue menu so the close-timing guard
                    // below can distinguish a real read from a short generation handoff.
                    pending.FirstDialogueMenu = currentMenu;
                    pending.FirstDialogueMenuTicks = 0;
                }

                if (!pending.PromptClearedAfterFirstDialogueMenu)
                    refreshOutfitPrompt?.Invoke(npc);

                return;
            }

            if (pending.SawDialogueMenuAfterConsumption && !pending.PromptClearedAfterFirstDialogueMenu)
            {
                // Outfit Compliments AI may briefly close the generation-tag menu before opening the
                // actual generated outfit line. If the first menu was only visible for a few
                // ticks, keep the prompt alive instead of clearing too early.
                if (pending.FirstDialogueMenuTicks < 15 && pending.PromptKeepAliveTimer > 0)
                {
                    refreshOutfitPrompt?.Invoke(npc);
                    return;
                }

                clearOutfitPrompt?.Invoke(npc);
                pending.PromptClearedAfterFirstDialogueMenu = true;
                if (ModEntry.DebugLog) monitor?.Log($"[NPC OUTFIT] Cleared outfit prompt for {npc.Name} after the first outfit dialogue menu closed.", LogLevel.Info);
                return;
            }

            // Before the dialogue menu appears, Outfit Compliments AI may still be generating/replacing
            // the tag. Keep refreshing during that handoff window, never after the first prompt
            // has been cleared for replies.
            if (!pending.PromptClearedAfterFirstDialogueMenu)
                refreshOutfitPrompt?.Invoke(npc);
        }

        private static bool LooksLikeReplyOrInputMenu(object menu)
        {
            if (menu == null)
                return false;

            string typeName = menu.GetType().Name ?? "";

            // Vanilla's normal spoken lines use DialogueBox. Keep the outfit prompt alive
            // for those, because Outfit Compliments AI may still be converting the generation tag.
            if (typeName.Equals("DialogueBox", StringComparison.OrdinalIgnoreCase))
                return false;

            string[] replyMarkers =
            {
                "Response", "Reply", "Input", "Text", "TextBox", "Keyboard",
                "Question", "Answer", "Choice"
            };

            return replyMarkers.Any(marker => typeName.IndexOf(marker, StringComparison.OrdinalIgnoreCase) >= 0);
        }

        private bool ShouldClearConsumedPrompt(PendingPrompt pending)
        {
            if (pending == null || !pending.DialogueWasConsumed)
                return false;

            // Once the first outfit menu has closed and the prompt was cleared, the prompt did its job.
            if (pending.SawDialogueMenuAfterConsumption && pending.PromptClearedAfterFirstDialogueMenu && Game1.activeClickableMenu == null)
                return true;

            // Safety fallback, so a prompt cannot stay forever if something consumes the tag
            // without opening/changing a visible dialogue menu.
            if (pending.PromptKeepAliveTimer <= 0)
            {
                pending.PromptClearedAfterFirstDialogueMenu = true;
                return true;
            }

            return false;
        }

        private void SchedulePostDialogueRestore(PendingPrompt pending, bool outfitWasRead)
        {
            if (pending == null || pending.WaitingForPostDialogueRestore)
                return;

            pending.WaitingForPostDialogueRestore = true;
            pending.PostDialogueRestoreDelay = 8;
            pending.PostDialogueOutfitWasRead = outfitWasRead;
        }

        public void NotifyPrioritizedDialogueOpenedByHarmony(NPC npc)
        {
            if (npc == null)
                return;

            if (!pendingPrompts.TryGetValue(npc.Name, out PendingPrompt pending) || pending == null)
                return;

            // The Harmony prefix opened the outfit dialogue directly and skipped the
            // original NPC.checkAction. In that path, CurrentDialogue.Count isn't a
            // reliable signal for when the outfit line was consumed, so mark it here
            // and let the normal delayed restore run after the dialogue box closes.
            pending.DialogueWasConsumed = true;
            pending.PromptKeepAliveTimer = Math.Max(pending.PromptKeepAliveTimer, 300);

            // Do not mark SawDialogueMenuAfterConsumption here. Game1.drawDialogue can open
            // a short-lived generation-tag menu before Outfit Compliments AI shows the final generated
            // line. If we mark the menu as seen too early, the next empty-menu tick can clear
            // the prompt before Outfit Compliments AI uses it. Let KeepConsumedDialoguePromptAlive mark
            // the first real menu when it actually sees Game1.activeClickableMenu.

            pending.PostDialogueOutfitWasRead = true;

            if (ModEntry.DebugLog) monitor?.Log($"[NPC OUTFIT] Harmony opened {npc.Name}'s outfit dialogue; waiting until the first Outfit Compliments AI menu is done before restoring their previous dialogue.", LogLevel.Info);
        }

        public void NotifyOwnAiWaitingDialogueOpened(NPC npc)
        {
            if (npc == null)
                return;

            if (!pendingPrompts.TryGetValue(npc.Name, out PendingPrompt pending) || pending == null)
                return;

            pending.DialogueWasConsumed = true;
            pending.WaitingForOwnAiFinalDialogue = true;
            pending.PromptKeepAliveTimer = Math.Max(pending.PromptKeepAliveTimer, 900);
            pending.PostDialogueOutfitWasRead = false;

            if (ModEntry.DebugLog) monitor?.Log($"[NPC OUTFIT] Opened AI waiting dialogue for {npc.Name}; keeping outfit reaction pending until final AI line is ready.", LogLevel.Info);
        }

        public void NotifyOwnAiFinalDialogueOpened(NPC npc)
        {
            if (npc == null)
                return;

            if (!pendingPrompts.TryGetValue(npc.Name, out PendingPrompt pending) || pending == null)
                return;

            pending.WaitingForOwnAiFinalDialogue = false;
            pending.DialogueWasConsumed = true;
            pending.PromptKeepAliveTimer = Math.Max(pending.PromptKeepAliveTimer, 300);
            pending.PostDialogueOutfitWasRead = true;

            if (ModEntry.DebugLog) monitor?.Log($"[NPC OUTFIT] Opened final AI outfit dialogue for {npc.Name}; it will be marked read after the menu closes.", LogLevel.Info);
        }

        public void NotifyOwnAiFinalDialogueFailed(NPC npc)
        {
            if (npc == null)
                return;

            if (!pendingPrompts.TryGetValue(npc.Name, out PendingPrompt pending) || pending == null)
                return;

            // The AI generation failed/timed out, so no dialogue was actually shown. Restore the
            // NPC's normal dialogue WITHOUT marking the outfit as read, and re-arm this pending
            // reaction so the farmer can click the NPC again to retry generating it. We deliberately
            // do NOT schedule the post-dialogue restore here, because that path removes the pending
            // prompt entirely (ending the reaction). Keeping the prompt alive but un-consumed is what
            // lets the click retry work, matching the spouse "keep pending after AI failure" behavior.
            try
            {
                RestoreQueuedDialoguesAfterOutfit(npc, pending, clearCurrentDialogue: true);
                RestoreTalkedToTodayIfUnread(npc, pending);
                clearOutfitPrompt?.Invoke(npc);
            }
            catch (System.Exception ex)
            {
                if (ModEntry.DebugLog) monitor?.Log($"[NPC OUTFIT] Could not restore dialogue after failed AI for {npc.Name}: {ex.Message}", LogLevel.Info);
            }

            pending.WaitingForOwnAiFinalDialogue = false;
            pending.DialogueWasConsumed = false;
            pending.DialogueQueued = false;
            pending.SawDialogueMenuAfterConsumption = false;
            pending.PromptClearedAfterFirstDialogueMenu = false;
            pending.PostDialogueOutfitWasRead = false;
            pending.DialogueCountAfterPush = 0;
            // Brief cooldown so the ellipsis/notice bubble does not immediately re-fire every tick,
            // but the reaction stays available for a manual click retry until the farmer walks away.
            pending.PendingBubbleCooldown = Math.Max(pending.PendingBubbleCooldown, 180);

            if (ModEntry.DebugLog) monitor?.Log($"[NPC OUTFIT] AI outfit dialogue failed for {npc.Name}; restored previous dialogue (outfit NOT marked read) and reopened it for a click retry.", LogLevel.Info);
        }

        /// <summary>Cancel a no-longer-relevant AI request and return this NPC to the normal chance roll flow.</summary>
        public void CancelPendingOwnAiGeneration(NPC npc)
        {
            if (npc == null || !pendingPrompts.TryGetValue(npc.Name, out PendingPrompt pending) || pending == null)
                return;

            CancelPendingPrompt(npc, pending);
            pendingPrompts.Remove(npc.Name);
            // Cancellation is not a failed chance roll. When the farmer approaches again, let the
            // configured notice probability decide normally instead of preserving a cooldown.
            rollCooldowns.Remove(npc.Name);
        }


        private void UpdatePostDialogueRestore(NPC npc, PendingPrompt pending)
        {
            if (npc == null || pending == null)
                return;

            if (pending.PostDialogueLingerActive)
            {
                UpdatePostDialogueLinger(npc, pending);
                return;
            }

            if (pending.PostDialogueRestoreApplied)
            {
                pending.WaitingForPostDialogueRestore = false;
                return;
            }

            // Do not restore while a dialogue/menu is still active. If we restore on the
            // same frame the dialogue closes, Stardew/Outfit Compliments AI can still clear the stack.
            if (Game1.activeClickableMenu != null)
                return;

            if (pending.PostDialogueRestoreDelay > 0)
            {
                pending.PostDialogueRestoreDelay--;
                return;
            }

            bool beginLinger = false;

            try
            {
                clearOutfitPrompt?.Invoke(npc);

                if (pending.PostDialogueOutfitWasRead)
                    markCurrentOutfitAsNoticed?.Invoke(npc);

                RestoreQueuedDialoguesAfterOutfit(npc, pending, clearCurrentDialogue: true);
                RestoreTalkedToTodayIfUnread(npc, pending);

                pending.PostDialogueRestoreApplied = true;

                if (pending.PostDialogueOutfitWasRead
                    && tryBeginPostDialogueAction?.Invoke(npc, pending) == true)
                {
                    pending.PostDialogueLingerActive = false;
                    pending.WaitingForPostDialogueRestore = false;
                    if (ModEntry.DebugLog)
                        monitor?.Log($"[NPC OUTFIT] {npc.Name}'s post-dialogue state was handed to a specialized action.", LogLevel.Info);
                    return;
                }

                beginLinger = pending.PostDialogueOutfitWasRead
                    && Game1.player != null
                    && npc.currentLocation == Game1.player.currentLocation;

                if (beginLinger)
                {
                    pending.PostDialogueLingerActive = true;
                    pending.PostDialogueLingerTimer = PostDialogueLingerTicks;
                    UpdatePostDialogueLinger(npc, pending);
                    if (ModEntry.DebugLog) monitor?.Log($"[NPC OUTFIT] {npc.Name} will linger after the outfit compliment until distance >= {PostDialogueLingerDistance:F0} or {PostDialogueLingerTicks} ticks.", LogLevel.Info);
                    return;
                }

                if (ModEntry.DebugLog) monitor?.Log($"[NPC OUTFIT] Finished delayed restore after outfit dialogue for {npc.Name}.", LogLevel.Info);
            }
            finally
            {
                if (!beginLinger)
                    pending.WaitingForPostDialogueRestore = false;
            }
        }

        private static PendingPrompt CreatePendingPrompt(NPC npc)
        {
            List<FarmerSprite.AnimationFrame> animation = null;
            if (npc?.Sprite?.CurrentAnimation != null && npc.Sprite.CurrentAnimation.Count > 0)
                animation = new List<FarmerSprite.AnimationFrame>(npc.Sprite.CurrentAnimation);

            return new PendingPrompt
            {
                OriginalFacingDirection = npc?.FacingDirection ?? 2,
                OriginalPosition = npc?.Position ?? Microsoft.Xna.Framework.Vector2.Zero,
                OriginalTile = npc?.TilePoint ?? Microsoft.Xna.Framework.Point.Zero,
                OriginalFrame = npc?.Sprite?.CurrentFrame ?? 0,
                OriginalFlip = npc?.flip ?? false,
                OriginalMovementPause = npc == null ? 0 : (int)npc.movementPause,
                OriginalFreezeMotion = ReadFreezeMotion(npc),
                OriginalAddedSpeed = npc == null ? 0 : (int)npc.addedSpeed,
                OriginalAnimation = animation,
                HasOriginalVisualState = npc != null,
                WasLookingAtPlayer = false,
                DialogueQueued = false,
                NoticePauseActive = false,
                PendingBubbleCooldown = 0
            };
        }

        private void UpdateNpcLookState(NPC npc, PendingPrompt pending, ModConfig config)
        {
            if (npc == null || pending == null || Game1.player == null)
                return;

            if (npc.currentLocation != Game1.player.currentLocation)
            {
                pending.NoticePauseActive = false;
                return;
            }

            if (pending.IgnoreRomanticDistanceForPublicMultiKiss)
            {
                // Lots of Kisses owns the interruption hold and blush sequence. Once it releases
                // the NPC, let the schedule continue without Outfit Reactions refreshing a pause,
                // facing direction, or animation while its dialogue remains pending.
                pending.NoticePauseActive = false;
                return;
            }

            if (pending.RomanticHoldSuspendedForKiss)
            {
                if (pending.ExternalKissProtectionTimer > 0)
                    pending.ExternalKissProtectionTimer--;

                // freezeControls can end before the partner's kiss pose does. Keep the
                // outfit hold suspended through both the grace window and the NPC's real
                // visual pause, so StopAnimation below can't overwrite the kiss frame.
                if (pending.ExternalKissProtectionTimer > 0
                    || Game1.freezeControls
                    || npc.movementPause > 6)
                    return;

                // The external kiss has ended. Resume the same pending outfit wait without
                // consuming it or touching the schedule controller.
                pending.RomanticHoldSuspendedForKiss = false;
                pending.NoticePauseActive = true;
                romanticApproachController.SetSuspended(npc, suspended: false);
            }

            // Vanilla's kiss animation temporarily freezes controls. Do not refresh the outfit
            // hold pose or call StopAnimation during that window; once the kiss ends, the pending
            // romantic reaction resumes waiting without having queued any dialogue.
            if (pending.IsRomanticPartner && Game1.freezeControls)
                return;

            if (pending.IsRomanticPartner
                && (romanticApproachController.IsHandling(npc) || pending.RomanticApproachFinished))
            {
                pending.NoticePauseActive = false;
                return;
            }

            float distance = DistanceToPlayer(npc);

            // Kiss-style notice pause: don't delete or replace npc.controller. Let the NPC keep
            // walking normally, but if they naturally get adjacent to the farmer while the outfit
            // compliment is pending, hold them briefly with movementPause and make them look at her.
            if (pending.IsRomanticPartner)
            {
                // A partner who noticed while already walking keeps their original controller
                // and schedule until naturally reaching the player. Once the close hold starts,
                // latch it until interaction or the romantic cancellation boundary.
                if (pending.NoticePauseActive)
                {
                    pending.NoticePauseActive = distance < RomanticPendingCancelDistance;
                }
                else if (!pending.WasMovingWhenNoticed || distance <= RomanticWalkingHoldDistance)
                {
                    pending.NoticePauseActive = true;
                }
            }
            else if (distance <= OutfitNoticePauseDistance)
                pending.NoticePauseActive = true;
            else if (distance >= OutfitNoticeReleaseDistance)
                pending.NoticePauseActive = false;

            if (pending.NoticePauseActive)
            {
                if (!pending.WasMovingWhenNoticed)
                    specialActionController.Capture(npc, pending);

                if (npc.movementPause < 6)
                    npc.movementPause = 6;

                // Capture() already pauses a special schedule animation only after it has
                // saved a restorable snapshot. Do not stop an animation unconditionally:
                // schedule actions like Sebastian's lake behavior may not expose a special
                // frame, and would otherwise be interrupted with nothing to restore later.
                bool facedPlayer = pending.IsRomanticPartner
                    ? FaceRomanticPartnerTowardPlayer(npc)
                    : FacePlayerIfSafe(npc);
                if (facedPlayer)
                    pending.WasLookingAtPlayer = true;

                return;
            }

            // Once released, restore whatever we forced (special action first, falling back to
            // facing direction) right now instead of just letting go of the flag. Walking NPCs
            // naturally reorient themselves once their controller/schedule resumes, but a plain
            // standing NPC with no special animation has nothing else to turn them back — without
            // an explicit restore here they're left facing the player indefinitely.
            if (pending.WasLookingAtPlayer)
            {
                bool restoredSpecialAction = specialActionController.TryRestore(npc, pending, force: true);
                if (!restoredSpecialAction)
                    FaceDirectionIfSafe(npc, pending.OriginalFacingDirection);
            }

            pending.WasLookingAtPlayer = false;
        }

        private void ShowPendingDialogueBubbleIfNeeded(NPC npc, PendingPrompt pending, ModConfig config, bool force = false)
        {
            if (npc == null || pending == null || config == null || Game1.player == null)
                return;

            if (pending.SuppressNoticeEmote)
                return;

            if (npc.currentLocation != Game1.player.currentLocation)
                return;

            if (Game1.activeClickableMenu != null)
                return;

            float noticeDistance = pending.IsRomanticPartner
                ? RomanticPartnerApproachController.NoticeDistance
                : Math.Max(64f, config.OutfitNoticeDistance);
            if (DistanceToPlayer(npc) > noticeDistance)
                return;

            if (!force && pending.PendingBubbleCooldown > 0)
                return;

            // Fire the ellipsis emote only ONCE per outfit notice — re-fires if the player
            // leaves and comes back (EmoteFired is reset when the pending prompt is cancelled).
            if (!pending.EmoteFired)
            {
                npc.doEmote(40);
                pending.EmoteFired = true;
            }
            pending.PendingBubbleCooldown = force ? 180 : PendingBubbleCooldownTicks;
        }

        /// <summary>
        /// True if ANY non-spouse NPC currently has an outfit reaction in progress that has not yet
        /// been fully read/closed (noticing, generating, or dialogue still open). Used to let other
        /// mods (e.g. the kiss mod) know an outfit reaction is active so they can hold off.
        /// </summary>
        // Deliberately narrow — true only for an NPC currently held mid-outfit-reaction AND whose
        // snapshot has a captured fishing end-of-route behavior name (i.e. genuinely a fishing
        // NPC, not just any NPC with a special pose). Used only by the doMiddleAnimation Harmony
        // patch in ModEntry, to suppress vanilla's own animation-rescheduling for these specific
        // NPCs during the hold window, without touching anything else about how this system works.
        public bool IsHeldForFishingSpecialAction(NPC npc)
        {
            if (npc == null)
                return false;

            return pendingPrompts.TryGetValue(npc.Name, out PendingPrompt pending)
                && pending?.SpecialActionSnapshot != null
                && !string.IsNullOrEmpty(pending.SpecialActionSnapshot.SavedStartedEndOfRouteBehavior);
        }

        public bool HasAnyActivePendingReaction()
        {
            foreach (PendingPrompt pending in pendingPrompts.Values)
            {
                if (pending == null)
                    continue;
                // Once the line has actually been read/opened and we're just in the delayed
                // restore/linger cleanup, the reaction no longer needs to block other interactions.
                if (pending.PostDialogueOutfitWasRead || pending.PostDialogueRestoreApplied || pending.PostDialogueLingerActive)
                    continue;
                // A pending that has not yet been triggered by a player click (DialogueQueued == false)
                // means an NPC merely noticed the outfit but the player hasn't approached them yet.
                // That passive state should NOT block cross-mod interactions like kisses — only
                // reactions actively in progress (AI generating, dialogue open, etc.) should.
                if (!pending.DialogueQueued)
                    continue;
                return true;
            }
            return false;
        }


        public bool HasUnreadPendingDialogueFor(NPC npc)
        {
            if (npc == null)
                return false;

            if (!pendingPrompts.TryGetValue(npc.Name, out PendingPrompt pending) || pending == null)
                return false;

            // After the outfit line has been opened/read, the player can use normal
            // interactions again while the delayed restore/linger cleanup finishes.
            if (pending.PostDialogueOutfitWasRead || pending.PostDialogueRestoreApplied || pending.PostDialogueLingerActive)
                return false;

            return true;
        }

        public IEnumerable<NPC> GetPendingDialogueIndicatorNpcs()
        {
            if (!Context.IsWorldReady || Game1.player == null || Game1.currentLocation == null)
                yield break;

            ModConfig config = getConfig?.Invoke();
            if (config == null)
                yield break;

            foreach (string npcName in pendingPrompts.Keys.ToList())
            {
                if (!pendingPrompts.TryGetValue(npcName, out PendingPrompt pending) || pending == null)
                    continue;

                if (pending.WaitingForPostDialogueRestore || pending.PostDialogueLingerActive || pending.WaitingForOwnAiFinalDialogue)
                    continue;

                if (pending.DialogueWasConsumed)
                    continue;

                NPC npc = NpcContextResolver.Resolve(npcName);
                if (npc == null || npc.currentLocation != Game1.player.currentLocation)
                    continue;

                float noticeDistance = pending.IsRomanticPartner
                    ? RomanticPartnerApproachController.NoticeDistance
                    : Math.Max(64f, config.OutfitNoticeDistance);
                if (DistanceToPlayer(npc) > noticeDistance)
                    continue;

                yield return npc;
            }
        }

        private void UpdatePostDialogueLinger(NPC npc, PendingPrompt pending)
        {
            if (npc == null || pending == null)
                return;

            if (pending.IsRomanticPartner && romanticApproachController.IsHandling(npc))
                return;

            if (pending.IsRomanticPartner && pending.RomanticApproachFinished)
            {
                pending.PostDialogueLingerActive = false;
                pending.WaitingForPostDialogueRestore = false;
                return;
            }

            bool sameLocation = Game1.player != null && npc.currentLocation == Game1.player.currentLocation;
            float distance = sameLocation ? DistanceToPlayer(npc) : PostDialogueLingerDistance;
            bool hasCapturedSpecialAction = pending.SpecialActionSnapshot != null;

            if (pending.PostDialogueLingerTimer > 0)
                pending.PostDialogueLingerTimer--;

            bool shouldFinish = pending.IsRomanticPartner
                ? (!sameLocation || distance >= RomanticPendingCancelDistance)
                : hasCapturedSpecialAction
                ? (!sameLocation || distance >= NpcSpecialActionRestoreDistance)
                : (!sameLocation || distance >= PostDialogueLingerDistance || pending.PostDialogueLingerTimer <= 0);

            if (!shouldFinish)
            {
                if (!pending.WasMovingWhenNoticed)
                    specialActionController.Capture(npc, pending);

                if (npc.movementPause < 6)
                    npc.movementPause = 6;

                // See the equivalent notice hold above. A failed capture must leave the
                // schedule's animation alone, or the NPC can remain out of its original pose.
                if (pending.IsRomanticPartner)
                    FaceRomanticPartnerTowardPlayer(npc);
                else
                    FacePlayerIfSafe(npc);
                return;
            }

            bool restoredSpecialAction = specialActionController.TryRestore(npc, pending, force: true);
            if (!restoredSpecialAction)
            {
                // NPC.update clears freezeMotion only when a positive movementPause expires.
                // Clearing the timer ourselves must also release our freeze, not external holds.
                if (npc.movementPause <= 6 && !pending.OriginalFreezeMotion)
                {
                    npc.movementPause = 1; // expire through NPC.update, including its unfreeze branch
                }
                FaceDirectionIfSafe(npc, pending.OriginalFacingDirection);
            }

            if (ModEntry.DebugLog)
                monitor?.Log($"[NPC OUTFIT] Released {npc.Name} after reaction: walkingAtNotice={pending.WasMovingWhenNoticed}, specialRestored={restoredSpecialAction}, pause={npc.movementPause}, frozenBeforeVanillaUpdate={ReadFreezeMotion(npc)}, controllerPresent={npc.controller != null}.", LogLevel.Info);

            pending.PostDialogueLingerActive = false;
            pending.WaitingForPostDialogueRestore = false;
        }

        private static bool ReadFreezeMotion(NPC npc)
        {
            return npc != null && (HarmonyLib.AccessTools.Field(typeof(Character), "freezeMotion")?.GetValue(npc) as bool? ?? false);
        }

        private void CaptureQueuedDialoguesBeforeOutfit(NPC npc, PendingPrompt pending)
        {
            if (npc == null || pending == null)
                return;

            try
            {
                // Stack<T>.ToList() enumerates from top to bottom. Store that order,
                // then restore by pushing bottom-to-top later so the original top
                // dialogue remains the next one the player sees.
                pending.DialogueBackupBeforeOutfit = npc.CurrentDialogue?.ToList() ?? new List<Dialogue>();
                pending.HasDialogueBackup = pending.DialogueBackupBeforeOutfit.Count > 0;
            }
            catch (Exception ex)
            {
                pending.DialogueBackupBeforeOutfit = new List<Dialogue>();
                pending.HasDialogueBackup = false;
                if (ModEntry.DebugLog) monitor?.Log($"[NPC OUTFIT] Could not capture existing dialogue for {npc.Name}: {ex.Message}", LogLevel.Info);
            }
        }

        private void RestoreQueuedDialoguesAfterOutfit(NPC npc, PendingPrompt pending, bool clearCurrentDialogue)
        {
            if (npc == null || pending == null)
                return;

            if (!pending.HasDialogueBackup || pending.DialogueBackupBeforeOutfit == null || pending.DialogueBackupBeforeOutfit.Count <= 0)
            {
                if (clearCurrentDialogue)
                    npc.CurrentDialogue.Clear();
                return;
            }

            try
            {
                if (clearCurrentDialogue)
                    npc.CurrentDialogue.Clear();

                for (int i = pending.DialogueBackupBeforeOutfit.Count - 1; i >= 0; i--)
                    npc.CurrentDialogue.Push(pending.DialogueBackupBeforeOutfit[i]);

                if (ModEntry.DebugLog) monitor?.Log($"[NPC OUTFIT] Restored {pending.DialogueBackupBeforeOutfit.Count} previous dialogue(s) for {npc.Name} after outfit reaction.", LogLevel.Info);
            }
            catch (Exception ex)
            {
                if (ModEntry.DebugLog) monitor?.Log($"[NPC OUTFIT] Could not restore previous dialogue for {npc.Name}: {ex.Message}", LogLevel.Info);
            }
        }

        private void TemporarilySkipFirstDailyDialogue(NPC npc, PendingPrompt pending)
        {
            if (npc == null || pending == null || Game1.player == null)
                return;

            try
            {
                if (!Game1.player.friendshipData.TryGetValue(npc.Name, out Friendship friendship) || friendship == null)
                    return;

                pending.HasFriendshipEntry = true;
                pending.OriginalTalkedToToday = friendship.TalkedToToday;

                if (!friendship.TalkedToToday)
                {
                    friendship.TalkedToToday = true;
                    pending.ForcedTalkedToToday = true;
                    if (ModEntry.DebugLog) monitor?.Log($"[NPC OUTFIT] Temporarily skipped first daily dialogue for {npc.Name} so the outfit reaction can play first.", LogLevel.Info);
                }
            }
            catch (Exception ex)
            {
                if (ModEntry.DebugLog) monitor?.Log($"[NPC OUTFIT] Could not temporarily skip first daily dialogue for {npc.Name}: {ex.Message}", LogLevel.Info);
            }
        }

        private void RestoreTalkedToTodayIfUnread(NPC npc, PendingPrompt pending)
        {
            if (npc == null || pending == null || Game1.player == null)
                return;

            if (!pending.HasFriendshipEntry || !pending.ForcedTalkedToToday)
                return;

            try
            {
                if (Game1.player.friendshipData.TryGetValue(npc.Name, out Friendship friendship) && friendship != null)
                    friendship.TalkedToToday = pending.OriginalTalkedToToday;
            }
            catch (Exception ex)
            {
                if (ModEntry.DebugLog) monitor?.Log($"[NPC OUTFIT] Could not restore first daily dialogue state for {npc.Name}: {ex.Message}", LogLevel.Info);
            }
        }

        private void CancelPendingPrompt(NPC npc, PendingPrompt pending)
        {
            if (npc == null || pending == null)
                return;

            romanticApproachController.ReleaseWithoutPending(npc);

            if (pending.DialogueQueued)
            {
                RemoveQueuedDialogueIfStillPending(npc, pending);
                RestoreQueuedDialoguesAfterOutfit(npc, pending, clearCurrentDialogue: true);
                clearOutfitPrompt?.Invoke(npc);
                RestoreTalkedToTodayIfUnread(npc, pending);
            }

            if (pending.IsRomanticPartner)
            {
                // Moving away cancels only this pending opportunity. Keep a short cooldown so the
                // same partner can't immediately interrupt their newly resumed route again on the
                // very same update cycle.
                reactedNpcsThisOutfit.Remove(npc.Name);
                rollCooldowns[npc.Name] = CancelledReactionCooldownTicks;
                if (ModEntry.DebugLog) monitor?.Log($"[NPC OUTFIT] Romantic partner {npc.Name} was released; a short notice cooldown now protects the resumed route.", LogLevel.Info);
            }
            else
            {
                rollCooldowns[npc.Name] = CancelledReactionCooldownTicks;
            }

            bool restoredSpecialAction = specialActionController.TryRestore(npc, pending, force: true);

            if (!restoredSpecialAction && pending.WasLookingAtPlayer)
                FaceDirectionIfSafe(npc, pending.OriginalFacingDirection);
        }

        private void ClearAllPendingPrompts(bool removeQueuedDialogues)
        {
            foreach (var pair in pendingPrompts.ToList())
            {
                string npcName = pair.Key;
                PendingPrompt pending = pair.Value;
                NPC npc = NpcContextResolver.Resolve(npcName);
                if (npc == null)
                    continue;

                if (removeQueuedDialogues && pending.DialogueQueued)
                {
                    RemoveQueuedDialogueIfStillPending(npc, pending);
                    RestoreQueuedDialoguesAfterOutfit(npc, pending, clearCurrentDialogue: true);
                }

                if (pending.DialogueQueued)
                {
                    clearOutfitPrompt?.Invoke(npc);
                    RestoreTalkedToTodayIfUnread(npc, pending);
                }

                bool restoredSpecialAction = specialActionController.TryRestore(npc, pending, force: true);

                if (!restoredSpecialAction && pending.WasLookingAtPlayer)
                    FaceDirectionIfSafe(npc, pending.OriginalFacingDirection);
            }

            pendingPrompts.Clear();
        }

        private void RemoveQueuedDialogueIfStillPending(NPC npc, PendingPrompt pending)
        {
            if (npc == null || pending == null || !pending.DialogueQueued)
                return;

            // Our Outfit Compliments AI dialogue is pushed last. Only pop it if the stack still has exactly the size
            // it had right after we pushed, so we don't accidentally remove another mod's dialogue.
            if (npc.CurrentDialogue.Count == pending.DialogueCountAfterPush &&
                pending.DialogueCountAfterPush == pending.DialogueCountBeforePush + 1)
            {
                npc.CurrentDialogue.Pop();
            }
        }

        /// <summary>
        /// True when there is an unobstructed tile path between the NPC and the player — i.e. no
        /// solid wall, door, or impassable tile blocks the line between them. Uses a simple
        /// tile-by-tile raycast along the line connecting the two positions. This prevents NPCs in
        /// closed-off rooms (same GameLocation but behind a wall/door) from noticing the player.
        /// </summary>
        private bool HasLineOfSightToPlayer(NPC npc)
        {
            return peekingController.HasLineOfSightToPlayer(npc);
        }

        private bool IsNpcFacingPlayer(NPC npc)
        {
            return peekingController.IsNpcFacingPlayer(npc);
        }

        private float DistanceToPlayer(NPC npc)
        {
            return peekingController.DistanceToPlayer(npc);
        }

        private bool FacePlayerIfSafe(NPC npc)
        {
            return peekingController.FacePlayerIfSafe(npc);
        }

        private static bool FaceRomanticPartnerTowardPlayer(NPC npc)
        {
            if (npc == null || Game1.player == null || npc.currentLocation != Game1.player.currentLocation)
                return false;

            // A preserved schedule controller can keep isMoving() true even while movementPause
            // holds the NPC in place. Romantic partners are already paused before this call, so
            // updating only their facing direction is safe and does not replace the controller.
            npc.faceGeneralDirection(Game1.player.getStandingPosition(), 0, false, false);
            return true;
        }

        private bool FaceDirectionIfSafe(NPC npc, int direction)
        {
            return peekingController.FaceDirectionIfSafe(npc, direction);
        }
    }
}
