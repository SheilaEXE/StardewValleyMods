using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using HarmonyLib;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Netcode;
using OutfitReactions.Ai;
using StardewModdingAPI;
using StardewModdingAPI.Events;
using StardewValley;
using StardewValley.Menus;
using StardewValley.Network;
using StardewValley.Objects;
using xTile;
using xTile.Dimensions;
using xTile.ObjectModel;

namespace OutfitReactions;

public sealed partial class ModEntry : Mod
{	private bool ShouldStartClothesReaction(NPC npc = null)
	{
		if (!changedClothes || lastFashionSenseChangeInfo == null)
		{
			return false;
		}
		FashionSenseChangeInfo effectiveFashionSenseChangeInfoForNpc = GetEffectiveFashionSenseChangeInfoForNpc(npc);
		if (effectiveFashionSenseChangeInfoForNpc == null)
		{
			return false;
		}
		if (npc != null && IsSavedOutfitNoticeChange(effectiveFashionSenseChangeInfoForNpc) && !CanNpcNoticeCurrentOutfitNotice(npc))
		{
			return false;
		}
		if (npc != null && IsSpecialItemRemovalOnlyNotice(effectiveFashionSenseChangeInfoForNpc))
		{
			if (!NpcRemembersRemovedSpecialItem(npc, effectiveFashionSenseChangeInfoForNpc))
			{
				return false;
			}
		}
		else if (npc != null && IsVanillaHatRemovalOnlyNotice(effectiveFashionSenseChangeInfoForNpc) && !NpcRemembersRemovedVanillaHat(npc))
		{
			return false;
		}
		string fashionSenseDialogueKey = GetFashionSenseDialogueKey(effectiveFashionSenseChangeInfoForNpc);
		return !string.IsNullOrEmpty(fashionSenseDialogueKey);
	}

	private void UpdateClothesReactionSystem(NPC npc)
	{
		//IL_029f: Unknown result type (might be due to invalid IL or missing references)
		if (changedClothes && !isReactingToClothes)
		{
			playerWasInClothesNoticeRange = false;
		}
		if (npc == null || !Context.IsWorldReady || Game1.player == null)
		{
			return;
		}
		float num = DistanceToPlayer(npc);
		bool flag = num < (float)Config.OutfitNoticeDistance && ((Character)npc).currentLocation == ((Character)Game1.player).currentLocation;
		bool flag2 = ShouldStartClothesReaction(npc);
		bool flag3 = spouseOutfitApproachController.ShouldApproach(npc);
		if (changedClothes && !isReactingToClothes && !flag2)
		{
			FashionSenseChangeInfo effectiveFashionSenseChangeInfoForNpc = GetEffectiveFashionSenseChangeInfoForNpc(npc);
			if (IsSavedOutfitNoticeChange(effectiveFashionSenseChangeInfoForNpc) && !CanNpcNoticeCurrentOutfitNotice(npc))
			{
				playerWasInClothesNoticeRange = flag;
			}
			else if (IsVanillaHatRemovalOnlyNotice(lastFashionSenseChangeInfo) && !NpcRemembersRemovedVanillaHat(npc))
			{
				playerWasInClothesNoticeRange = flag;
			}
			else if (IsSpecialItemRemovalOnlyNotice(lastFashionSenseChangeInfo) && !NpcRemembersRemovedSpecialItem(npc, lastFashionSenseChangeInfo))
			{
				playerWasInClothesNoticeRange = flag;
			}
			else
			{
				ResetClothesState();
			}
			return;
		}
		if (outfitSequenceActive && !isReactingToClothes && clothesFirstNoticeDone && (((Character)npc).currentLocation != ((Character)Game1.player).currentLocation || num > (float)Config.OutfitCancelDistance))
		{
			ResetClothesReactionState();
		}
		if (Config.Enabled && flag2 && !((NetFieldBase<bool, NetBool>)(object)npc.isSleeping).Value && !isReactingToClothes)
		{
			if (!clothesFirstNoticeDone && flag && IsNpcFacingPlayer(npc))
			{
				spouseOutfitReactionProgressState.BeginFirstNotice();
				if (flag3)
				{
					spouseRouteController.Stop(npc, ((Mod)this).Monitor, DebugLog);
				}
				ShowSpousePendingOutfitBubbleIfNeeded(npc, force: true);
				UpdateSpouseOutfitNoticeHold(npc, num);
			}
			if (clothesFirstNoticeDone && !isReactingToClothes && clothesNoticePauseTimer <= 0 && flag && clothesSecondNoticeCooldown <= 0)
			{
				outfitSequenceActive = true;
				if (flag3)
				{
					spouseRouteController.Stop(npc, ((Mod)this).Monitor, DebugLog);
					((Character)npc).faceGeneralDirection(((Character)Game1.player).getStandingPosition(), 0, false);
					spouseOutfitReactionProgressState.BeginApproach(npc);
				}
				else
				{
					spouseOutfitReactionProgressState.BeginClickReady(npc);
					if (DebugLog)
					{
						((Mod)this).Monitor.Log("[CLOTHES SPOUSE] " + ((Character)npc).Name + "'s outfit compliment is ready on click without pathing because they are outside the farmhouse.", (LogLevel)2);
					}
					ShowSpousePendingOutfitBubbleIfNeeded(npc);
					UpdateSpouseOutfitNoticeHold(npc, num);
				}
				clothesSecondNoticeCooldown = 300;
			}
		}
		if (isReactingToClothes && clothesReactingNpc == npc)
		{
			outfitSequenceActive = true;
			UpdateSpouseOutfitNoticeHold(npc, num);
			if (clothesComplimentReady)
			{
				ShowSpousePendingOutfitBubbleIfNeeded(npc);
			}
			if (((Character)npc).currentLocation != ((Character)Game1.player).currentLocation || num > (float)Config.OutfitCancelDistance)
			{
				ResetClothesState();
				return;
			}
			if (!clothesComplimentReady)
			{
				if (num <= 140f || clothesChaseTimer <= 0)
				{
					ShowOutfitCompliment(npc, flag);
					return;
				}
				if (((Character)npc).controller == null)
				{
					if (spouseOutfitApproachController.TryStartPath(npc, ((Mod)this).Monitor, DebugLog))
					{
						clothesPathStarted = true;
					}
					else
					{
						if (DebugLog)
						{
							((Mod)this).Monitor.Log("[CLOTHES SPOUSE] Could not find an approach path for " + ((Character)npc).Name + " inside the farmhouse; making the outfit compliment ready on click.", (LogLevel)2);
						}
						clothesComplimentReady = true;
					}
					if (clothesInteractionCooldown <= 0)
					{
						clothesInteractionCooldown = 180;
					}
				}
				playerWasInClothesNoticeRange = flag;
				return;
			}
		}
		playerWasInClothesNoticeRange = flag;
	}

	private void UpdateSpouseOutfitNoticeHold(NPC npc, float distance)
	{
		spouseOutfitNoticeController.UpdateHold(spouseProximityState, npc, Game1.player, distance, CaptureSpouseOutfitSpecialActionBeforeOutfit);
	}

	private void ShowSpousePendingOutfitBubbleIfNeeded(NPC npc, bool force = false)
	{
		if (spouseOutfitNoticeController.TryShowPendingBubble(spouseProximityState, npc, Game1.player, force, clothesEmoteFired, Config.OutfitNoticeDistance, Game1.activeClickableMenu != null || ShouldDeferAutomaticOutfitReaction(), DistanceToPlayer))
		{
			clothesEmoteFired = true;
		}
	}

	private void ShowOutfitCompliment(NPC npc, bool inClothesNoticeRange)
	{
		//IL_001e: Unknown result type (might be due to invalid IL or missing references)
		UpdateSpouseOutfitNoticeHold(npc, DistanceToPlayer(npc));
		CaptureSpouseOutfitSpecialActionBeforeOutfit(npc);
		((Character)npc).faceGeneralDirection(((Character)Game1.player).getStandingPosition(), 0, false);
		spouseDialogueController.Capture(npc, Game1.player, ((Mod)this).Monitor, DebugLog);
		if (!ShouldUseDeferredOwnAiForNpc(npc))
		{
			if (!QueueSpouseOutfitDialogueOnly(npc))
			{
				KeepSpouseOutfitNoticePendingAfterAiFailure(npc, "AI queue was not available during the spouse outfit reaction.");
				return;
			}
			InstallSpouseAfterOutfitDialogue(npc);
		}
		else
		{
			((Mod)this).Monitor.Log(" " + ((Character)npc).Name + "'s spouse outfit compliment is waiting for player click before AI generation starts.", (LogLevel)1);
		}
		spouseOutfitReactionProgressState.MarkComplimentStarted(npc, inClothesNoticeRange);
	}

	private void KeepSpouseOutfitNoticePendingAfterAiFailure(NPC npc, string reason = null)
	{
		if (npc != null)
		{
			spouseDialogueController.RestoreNormalDialogueAfterAiFailure(npc, ClearOutfitPrompt, delegate(NPC npcToRestore)
			{
				spouseDialogueController.Restore(npcToRestore, Game1.player, restoreTalkState: true, clearCurrentDialogue: false, ((Mod)this).Monitor, DebugLog);
			}, ((Mod)this).Monitor, DebugLog);
			spouseOutfitReactionProgressState.KeepPendingAfterAiFailure(npc);
			if (Game1.player != null && ((Character)npc).currentLocation == ((Character)Game1.player).currentLocation)
			{
				float num = DistanceToPlayer(npc);
				ShowSpousePendingOutfitBubbleIfNeeded(npc);
				UpdateSpouseOutfitNoticeHold(npc, num);
				playerWasInClothesNoticeRange = num < (float)Config.OutfitNoticeDistance;
			}
			string text = (string.IsNullOrWhiteSpace(reason) ? "" : (" Reason: " + reason));
			if (DebugLog)
			{
				((Mod)this).Monitor.Log("[CLOTHES SPOUSE] Outfit AI failed for " + ((Character)npc).Name + ", but the outfit was NOT marked as read. The current notice will stay pending until click retry or distance cancel." + text, (LogLevel)2);
			}
		}
	}

	private bool QueueSpouseOutfitDialogueOnly(NPC npc)
	{
		return spouseDialogueController.TryQueueOwnAiDialogue(npc, (NPC npcToQueue) => TryShowOwnAiOutfitDialogue(npcToQueue, isSpouseDialogue: true, clearExistingDialogue: false), ((Mod)this).Monitor);
	}

	private void RestoreSpouseDialogueBackupIfPending()
	{
		if (spouseDialogueController.HasBackup)
		{
			NPC characterFromName = Game1.getCharacterFromName(spouseDialogueController.Snapshot.NpcName, true, false);
			if (characterFromName == null)
			{
				spouseDialogueController.Clear();
				return;
			}
			ClearOutfitPrompt(characterFromName);
			spouseDialogueController.Restore(characterFromName, Game1.player, restoreTalkState: true, clearCurrentDialogue: true, ((Mod)this).Monitor, DebugLog);
		}
	}

	private int TryGetAnimationFrameIndex(FarmerSprite.AnimationFrame frame)
	{
		//IL_0002: Unknown result type (might be due to invalid IL or missing references)
		try
		{
			object obj = frame;
			FieldInfo field = obj.GetType().GetField("frame", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
			if (field != null && field.GetValue(obj) is int result)
			{
				return result;
			}
			PropertyInfo property = obj.GetType().GetProperty("Frame", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
			if (property != null && property.GetValue(obj) is int result2)
			{
				return result2;
			}
		}
		catch
		{
		}
		return -1;
	}

	private bool AnimationLooksLikeSpecialAction(List<FarmerSprite.AnimationFrame> animation)
	{
		//IL_0027: Unknown result type (might be due to invalid IL or missing references)
		//IL_002c: Unknown result type (might be due to invalid IL or missing references)
		//IL_002f: Unknown result type (might be due to invalid IL or missing references)
		if (animation == null || animation.Count <= 0)
		{
			return false;
		}
		foreach (FarmerSprite.AnimationFrame item in animation)
		{
			int num = TryGetAnimationFrameIndex(item);
			if (num >= 16)
			{
				return true;
			}
		}
		return false;
	}

	private void CaptureSpouseOutfitSpecialActionBeforeOutfit(NPC npc)
	{
		if (npc == null || ((Character)npc).Sprite == null || ((Character)npc).currentLocation == null || spouseSpecialActionController.HasSnapshotFor(npc) || ((Character)npc).isMoving())
		{
			return;
		}
		List<FarmerSprite.AnimationFrame> list = null;
		if (((Character)npc).Sprite.CurrentAnimation != null && ((Character)npc).Sprite.CurrentAnimation.Count > 0)
		{
			list = new List<FarmerSprite.AnimationFrame>(((Character)npc).Sprite.CurrentAnimation);
		}
		bool flag = list != null && list.Count > 0;
		bool flag2 = ((Character)npc).Sprite.CurrentFrame >= 16;
		if (flag || flag2)
		{
			spouseSpecialActionController.Capture(new SpouseOutfitSpecialActionSnapshot
			{
				Npc = npc,
				Location = ((Character)npc).currentLocation,
				FacingDirection = ((Character)npc).FacingDirection,
				CurrentFrame = ((Character)npc).Sprite.CurrentFrame,
				Flip = ((Character)npc).flip,
				MovementPause = ((Character)npc).movementPause,
				AddedSpeed = (int)((Character)npc).addedSpeed,
				CurrentAnimation = list
			});
			((Character)npc).Sprite.StopAnimation();
			((Character)npc).Sprite.ClearAnimation();
			((Character)npc).Sprite.CurrentAnimation = null;
			((Character)npc).flip = false;
			((Character)npc).Sprite.CurrentFrame = GetNpcIdleFrameForDirection(((Character)npc).FacingDirection);
			((Character)npc).Sprite.UpdateSourceRect();
			if (DebugLog)
			{
				((Mod)this).Monitor.Log($"[CLOTHES SPOUSE] Saved special animation for {((Character)npc).Name} before outfit reaction. frame={spouseSpecialActionController.Current.CurrentFrame} anim={list?.Count ?? 0}", (LogLevel)2);
			}
		}
	}

	private int GetNpcIdleFrameForDirection(int facingDirection)
	{
		return facingDirection switch
		{
			0 => 8, 
			1 => 4, 
			2 => 0, 
			3 => 12, 
			_ => 0, 
		};
	}

	private void DoClothesFinalEmotes(NPC npc)
	{
		if (npc != null && Game1.player != null)
		{
			int[] array = new int[2] { 20, 60 };
			((Character)npc).doEmote(array[random.Next(array.Length)], true);
			Game1.player.doEmote(array[random.Next(array.Length)]);
		}
	}

	private void ResetClothesReactionState()
	{
		spouseSpecialActionController.TryRestore(force: true, Game1.player, Game1.activeClickableMenu != null, Game1.dialogueUp, DistanceToPlayer, 300f, ((Mod)this).Monitor, DebugLog);
		spouseOutfitReactionProgressState.ClearCurrentReaction();
		spouseProximityState.ClearNotice();
	}

	private void ResetClothesState(bool clearChangeFlag = false)
	{
		RestoreSpouseDialogueBackupIfPending();
		spouseRouteController.Clear();
		spouseSpecialActionController.TryRestore(force: true, Game1.player, Game1.activeClickableMenu != null, Game1.dialogueUp, DistanceToPlayer, 300f, ((Mod)this).Monitor, DebugLog);
		ClearSpousePostOutfitLinger();
		spouseOutfitReactionProgressState.ClearAllProgress();
		spouseProximityState.ClearNotice();
		fashionSenseMenuOpen = false;
		fsSnapshotBefore = null;
		CancelAllPendingOwnAiGenerations();
		if (clearChangeFlag)
		{
			changedClothes = false;
			lastFashionSenseChangeInfo = null;
		}
	}
}
