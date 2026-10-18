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
{	internal bool TryOpenPrioritizedOutfitDialogueFromCheckAction(NPC npc)
	{
		//IL_00c9: Unknown result type (might be due to invalid IL or missing references)
		if (!Context.IsWorldReady || Game1.player == null || Game1.currentLocation == null || !Config.Enabled)
		{
			return false;
		}
		if (npc == null || ((Character)npc).currentLocation != ((Character)Game1.player).currentLocation)
		{
			return false;
		}
		if (ShouldDeferAutomaticOutfitReaction())
		{
			return false;
		}
		if (!IsOwnAiWaitingStateActiveFor(npc) && !PrioritizeOutfitDialogueBeforeNpcCheckAction(npc))
		{
			return false;
		}
		bool flag = IsOwnAiWaitingStateActiveFor(npc);
		if ((npc.CurrentDialogue == null || npc.CurrentDialogue.Count <= 0) && !flag)
		{
			return false;
		}
		try
		{
			((Character)npc).faceGeneralDirection(((Character)Game1.player).getStandingPosition(), 0, false);
			((Character)Game1.player).Halt();
			if (flag)
			{
				if (DebugLog)
				{
					((Mod)this).Monitor.Log("[CLOTHES PRIORITY] Holding " + ((Character)npc).Name + "'s normal dialogue behind the prioritized outfit AI wait.", (LogLevel)2);
				}
				return true;
			}
			Game1.drawDialogue(npc);
			otherNpcClothesReactionSystem?.NotifyPrioritizedDialogueOpenedByHarmony(npc);
			if (DebugLog)
			{
				((Mod)this).Monitor.Log("[CLOTHES PRIORITY] Opened prioritized outfit dialogue for " + ((Character)npc).Name + " and skipped original NPC.checkAction.", (LogLevel)2);
			}
			return true;
		}
		catch (Exception value)
		{
			((Mod)this).Monitor.Log($"[CLOTHES PRIORITY] Failed to open prioritized outfit dialogue for {((Character)npc).Name}: {value}", (LogLevel)3);
			return false;
		}
	}

	private bool ShouldBlockNpcInteractionUntilOutfitDialogueRead(NPC npc)
	{
		if (npc == null || Game1.player == null || ((Character)npc).currentLocation != ((Character)Game1.player).currentLocation)
		{
			return false;
		}
		if (IsOwnAiWaitingStateActiveFor(npc))
		{
			return true;
		}
		if (IsUnreadSpouseOutfitDialoguePendingFor(npc))
		{
			return true;
		}
		return otherNpcClothesReactionSystem?.HasUnreadPendingDialogueFor(npc) ?? false;
	}

	private bool IsUnreadSpouseOutfitDialoguePendingFor(NPC npc)
	{
		if (npc == null || Game1.player == null)
		{
			return false;
		}
		if (!IsPlayerSpouse(npc))
		{
			return false;
		}
		if (lastFashionSenseChangeInfo == null)
		{
			return false;
		}
		if (!CanNpcNoticeCurrentOutfitNotice(npc))
		{
			return false;
		}
		if (clothesReactingNpc != null && ((Character)npc).Name.Equals(((Character)clothesReactingNpc).Name, StringComparison.OrdinalIgnoreCase) && (outfitSequenceActive || isReactingToClothes))
		{
			return true;
		}
		if (outfitSequenceActive && clothesFirstNoticeDone)
		{
			return true;
		}
		return false;
	}

	private bool IsPlayerSpouse(NPC npc)
	{
		return npc != null && Game1.player != null && !string.IsNullOrWhiteSpace(Game1.player.spouse) && ((Character)npc).Name.Equals(Game1.player.spouse, StringComparison.OrdinalIgnoreCase);
	}

	private void ShowPendingOutfitBlockedInteractionFeedback(NPC npc)
	{
		//IL_0053: Unknown result type (might be due to invalid IL or missing references)
		if (npc == null || Game1.player == null || ((Character)npc).currentLocation != ((Character)Game1.player).currentLocation)
		{
			return;
		}
		((Character)Game1.player).Halt();
		try
		{
			if (!((Character)npc).isMoving() && ((Character)npc).controller == null)
			{
				((Character)npc).faceGeneralDirection(((Character)Game1.player).getStandingPosition(), 0, false, false);
			}
		}
		catch
		{
		}
		if (IsPlayerSpouse(npc))
		{
			ShowSpousePendingOutfitBubbleIfNeeded(npc, force: true);
			UpdateSpouseOutfitNoticeHold(npc, DistanceToPlayer(npc));
		}
		else
		{
			((Character)npc).doEmote(40, true);
		}
	}

	private static bool IsNpcRomanceable(string name)
	{
		if (string.IsNullOrWhiteSpace(name))
		{
			return false;
		}
		try
		{
			if (Game1.characterData != null && Game1.characterData.TryGetValue(name, out var value) && value != null)
			{
				return value.CanBeRomanced;
			}
		}
		catch
		{
		}
		return false;
	}

	private bool IsFashionSenseMenu(IClickableMenu menu)
	{
		string text = ((object)menu)?.GetType().FullName ?? "";
		return text.Contains("FashionSense", StringComparison.OrdinalIgnoreCase);
	}

	private static string NormalizeOutfitText(string text)
	{
		if (string.IsNullOrWhiteSpace(text))
		{
			return "";
		}
		string text2 = text.ToLowerInvariant().Trim().Normalize(NormalizationForm.FormD);
		StringBuilder stringBuilder = new StringBuilder();
		string text3 = text2;
		foreach (char c in text3)
		{
			UnicodeCategory unicodeCategory = CharUnicodeInfo.GetUnicodeCategory(c);
			if (unicodeCategory != UnicodeCategory.NonSpacingMark)
			{
				stringBuilder.Append(c);
			}
		}
		return stringBuilder.ToString().Normalize(NormalizationForm.FormC);
	}

	private string GetFashionSenseDialogueKey(FashionSenseChangeInfo changeInfo)
	{
		if (changeInfo == null)
		{
			return null;
		}
		int num = changeInfo.CountChanges();
		if (num <= 0)
		{
			return null;
		}
		if (TryResolveSpecialItemNoticeForNpc(null, changeInfo, requireNpcMemoryForRemoval: false, out var _))
		{
			return "Clothes";
		}
		if ((changeInfo.ChangedOutfit && !string.IsNullOrWhiteSpace(changeInfo.NewOutfitId)) || ShouldTreatGenericHeadwearAsSavedOutfitPart(changeInfo))
		{
			return "Clothes";
		}
		bool flag = AreVisionOnlyFashionSenseTriggersEnabled();
		if (changeInfo.ChangedAccessory && ShouldTreatAccessoryAsCurrentComboFocus(changeInfo.NewAccessoryId, flag))
		{
			return "Accessory";
		}
		if (changeInfo.VanillaHatChanged)
		{
			return "Hat";
		}
		// A vanilla shirt changed under a visible vanilla hat: in combined mode this
		// is a new look, even when the hat itself did not change again.
		if (changeInfo.VanillaShirtChanged
			&& !changeInfo.VanillaShirtRemoved
			&& !string.IsNullOrWhiteSpace(GetVisibleVanillaHatId())
			&& ModConfigMenu.NormalizeVanillaHatReactionMode(Config?.VanillaHatReactionMode) == "Combined")
		{
			return "Clothes";
		}
		if (changeInfo.ChangedHat && !FashionSenseVisualService.IsUnhelpfulInternalAppearanceId(changeInfo.NewHatId) && (flag || ItemNameRevealsShape(changeInfo.NewHatId)))
		{
			return "Hat";
		}
		if (changeInfo.ChangedHair && !string.IsNullOrWhiteSpace(changeInfo.NewHairId))
		{
			return "Hair";
		}
		return null;
	}

	private bool AreVisionOnlyFashionSenseTriggersEnabled()
	{
		return ShouldTryVisionForCurrentAiProvider();
	}

	private bool ItemNameRevealsShape(string itemId)
	{
		if (string.IsNullOrWhiteSpace(itemId))
		{
			return false;
		}
		if (IsIgnoredFashionSenseAccessoryId(itemId))
		{
			return false;
		}
		if (FashionSenseVisualService.IsUnhelpfulInternalAppearanceId(itemId))
		{
			return false;
		}
		string text = FashionSenseVisualService.HumanizeAppearanceId(itemId);
		if (string.IsNullOrWhiteSpace(text))
		{
			return false;
		}
		string[] array = text.Split(' ');
		foreach (string text2 in array)
		{
			string text3 = text2.Trim('\'', '"', '.', ',', '(', ')');
			if (text3.Length < 3)
			{
				continue;
			}
			bool flag = false;
			bool flag2 = false;
			string text4 = text3;
			foreach (char c in text4)
			{
				if (char.IsDigit(c))
				{
					flag = true;
				}
				else if (char.IsLetter(c))
				{
					flag2 = true;
				}
			}
			if (flag2 && !flag)
			{
				return true;
			}
		}
		return false;
	}

	private bool IsFarmHouseLocation(GameLocation location)
	{
		if (location == null)
		{
			return false;
		}
		string text = location.Name ?? "";
		string text2 = location.NameOrUniqueName ?? "";
		string text3 = ((object)location).GetType().Name ?? "";
		string text4 = ((object)location).GetType().FullName ?? "";
		return text.Equals("FarmHouse", StringComparison.OrdinalIgnoreCase) || text2.Equals("FarmHouse", StringComparison.OrdinalIgnoreCase) || text3.Equals("FarmHouse", StringComparison.OrdinalIgnoreCase) || text.IndexOf("FarmHouse", StringComparison.OrdinalIgnoreCase) >= 0 || text2.IndexOf("FarmHouse", StringComparison.OrdinalIgnoreCase) >= 0 || text3.IndexOf("FarmHouse", StringComparison.OrdinalIgnoreCase) >= 0 || text4.IndexOf("FarmHouse", StringComparison.OrdinalIgnoreCase) >= 0;
	}

	private bool IsBeachOrIslandLocation(GameLocation location)
	{
		if (location == null)
		{
			return false;
		}
		string text = location.Name ?? "";
		string text2 = location.NameOrUniqueName ?? "";
		return text.Equals("Beach", StringComparison.OrdinalIgnoreCase)
			|| text2.Equals("Beach", StringComparison.OrdinalIgnoreCase)
			|| text.Equals("ElliottHouse", StringComparison.OrdinalIgnoreCase)
			|| text2.Equals("ElliottHouse", StringComparison.OrdinalIgnoreCase)
			|| text.StartsWith("Island", StringComparison.OrdinalIgnoreCase)
			|| text2.StartsWith("Island", StringComparison.OrdinalIgnoreCase);
	}

	private bool IsMarriageCandidateNpcRoom(NPC npc, GameLocation location)
	{
		if (npc == null || location == null)
		{
			return false;
		}
		if (!IsMarriageCandidate(npc))
		{
			return false;
		}
		string npcName = NormalizeOutfitText(((Character)npc).Name);
		string displayName = NormalizeOutfitText(((Character)npc).displayName);
		string text = NormalizeOutfitText(location.Name + " " + location.NameOrUniqueName);
		if (LooksLikeNpcRoomText(text) && TextMentionsNpc(text, npcName, displayName))
		{
			return true;
		}
		return MapPropertiesSuggestNpcRoom(location, npcName, displayName);
	}

	private bool IsMarriageCandidatePersonalLocation(NPC npc, GameLocation location)
	{
		if (npc == null || location == null || !IsMarriageCandidate(npc))
		{
			return false;
		}
		if (location.IsOutdoors || IsFarmHouseLocation(location))
		{
			return false;
		}
		string npcName = NormalizeOutfitText(((Character)npc).Name);
		string displayName = NormalizeOutfitText(((Character)npc).displayName);
		string text = NormalizeOutfitText(location.Name + " " + location.NameOrUniqueName);
		if (TextMentionsNpc(text, npcName, displayName))
		{
			return true;
		}
		Dictionary<string, string[]> dictionary = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase);
		dictionary["Abigail"] = new string[3] { "seedshop", "pierres", "pierre" };
		dictionary["Alex"] = new string[2] { "joshhouse", "alexhouse" };
		dictionary["Elliott"] = new string[2] { "elliotthouse", "elliottcabin" };
		dictionary["Emily"] = new string[2] { "haleyhouse", "emilyhouse" };
		dictionary["Haley"] = new string[1] { "haleyhouse" };
		dictionary["Harvey"] = new string[3] { "harveyroom", "harveyclinic", "hospital" };
		dictionary["Leah"] = new string[2] { "leahhouse", "leahcottage" };
		dictionary["Maru"] = new string[2] { "sciencehouse", "robinhouse" };
		dictionary["Penny"] = new string[1] { "trailer" };
		dictionary["Sam"] = new string[1] { "samhouse" };
		dictionary["Sebastian"] = new string[4] { "sciencehouse", "sebastianbasement", "sebastianroom", "robinhouse" };
		dictionary["Shane"] = new string[3] { "animalshop", "marnieranch", "ranch" };
		Dictionary<string, string[]> dictionary2 = dictionary;
		if (dictionary2.TryGetValue(((Character)npc).Name ?? "", out var value))
		{
			string[] array = value;
			foreach (string text2 in array)
			{
				if (!string.IsNullOrWhiteSpace(text2) && text.Contains(NormalizeOutfitText(text2)))
				{
					return true;
				}
			}
		}
		return false;
	}

	private bool IsMarriageCandidate(NPC npc)
	{
		if (npc == null)
		{
			return false;
		}
		try
		{
			object obj = ((object)npc).GetType().GetField("datable", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)?.GetValue(npc) ?? ((object)npc).GetType().GetProperty("datable", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)?.GetValue(npc);
			if (obj == null)
			{
				return false;
			}
			object obj2 = obj.GetType().GetProperty("Value", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)?.GetValue(obj);
			bool flag = default(bool);
			int num;
			if (obj2 is bool)
			{
				flag = (bool)obj2;
				num = 1;
			}
			else
			{
				num = 0;
			}
			return (byte)((uint)num & (flag ? 1u : 0u)) != 0;
		}
		catch
		{
			return false;
		}
	}

	private bool MapPropertiesSuggestNpcRoom(GameLocation location, string npcName, string displayName)
	{
		try
		{
			object obj;
			if (location == null)
			{
				obj = null;
			}
			else
			{
				Map map = location.map;
				obj = ((map != null) ? ((Component)map).Properties : null);
			}
			if (obj == null)
			{
				return false;
			}
			foreach (KeyValuePair<string, PropertyValue> item in (IEnumerable<KeyValuePair<string, PropertyValue>>)((Component)location.map).Properties)
			{
				object obj2 = item;
				string text = "";
				string text2 = "";
				if (obj2 is DictionaryEntry dictionaryEntry)
				{
					text = dictionaryEntry.Key?.ToString() ?? "";
					text2 = dictionaryEntry.Value?.ToString() ?? "";
				}
				else if (obj2 != null)
				{
					Type type = obj2.GetType();
					text = type.GetProperty("Key")?.GetValue(obj2)?.ToString() ?? "";
					text2 = type.GetProperty("Value")?.GetValue(obj2)?.ToString() ?? "";
				}
				string text3 = NormalizeOutfitText(text);
				string text4 = NormalizeOutfitText(text2);
				string text5 = text3 + " " + text4;
				if (LooksLikeNpcRoomText(text5) && TextMentionsNpc(text5, npcName, displayName))
				{
					return true;
				}
			}
		}
		catch
		{
		}
		return false;
	}

	private bool LooksLikeNpcRoomText(string text)
	{
		if (string.IsNullOrWhiteSpace(text))
		{
			return false;
		}
		return text.Contains("room") || text.Contains("bedroom") || text.Contains("bed room") || text.Contains("npcroom") || text.Contains("npc room") || text.Contains("quarto") || text.Contains("suite") || text.Contains("basement") || text.Contains("cellar") || text.Contains("porão") || text.Contains("porao");
	}

	private bool TextMentionsNpc(string text, string npcName, string displayName)
	{
		if (string.IsNullOrWhiteSpace(text))
		{
			return false;
		}
		return (!string.IsNullOrWhiteSpace(npcName) && text.Contains(npcName)) || (!string.IsNullOrWhiteSpace(displayName) && text.Contains(displayName));
	}

	private bool TryShowOwnAiOutfitDialogue(NPC npc, bool isSpouseDialogue, bool clearExistingDialogue)
	{
		return TryQueueOwnAiWaitingDialogue(npc, isSpouseDialogue, clearExistingDialogue);
	}

	private bool CanUseOwnAiForOutfitDialogue(NPC npc)
	{
		if (outfitAiService == null || npc == null || lastFashionSenseChangeInfo == null || !NpcCompatibilityPolicy.Allows(npc))
		{
			return false;
		}
		return outfitAiService.HasProfile(((Character)npc).Name);
	}

	private bool ShouldUseDeferredOwnAiForNpc(NPC npc)
	{
		return CanUseOwnAiForOutfitDialogue(npc);
	}

	private bool TryQueueOwnAiWaitingDialogue(NPC npc, bool isSpouseDialogue, bool clearExistingDialogue)
	{
		if (!CanUseOwnAiForOutfitDialogue(npc))
		{
			return false;
		}
		OutfitAiContext context = BuildOutfitAiContext(npc, isSpouseDialogue);
		if (context == null)
		{
			return false;
		}
		outfitAiService.PrepareVoiceSamplesForNpc(((Character)npc).Name);
		if (clearExistingDialogue)
		{
			npc.CurrentDialogue.Clear();
		}
		Game1.activeClickableMenu = null;
		Game1.afterDialogues = null;
		if (!aiGenerationCoordinator.TryGetOutfit(((Character)npc).Name, out var pending) || pending == null || pending.Task == null || pending.Task.IsCompleted)
		{
			pending = new PendingAiGeneration
			{
				NpcName = ((Character)npc).Name,
				IsSpouseDialogue = isSpouseDialogue,
				ClearExistingDialogue = clearExistingDialogue,
				WaitingDotCount = 1,
				WaitingDotTimer = 30,
				SafetyTimer = Math.Max(600, GetActiveAiTimeoutSecondsForSafety() * 120),
				Cancellation = new CancellationTokenSource()
			};
			aiGenerationCoordinator.StartOutfit(pending, delegate(CancellationToken cancellationToken)
			{
				try
				{
					string dialogue;
					return outfitAiService.TryGenerateCompliment(context, out dialogue, cancellationToken) ? dialogue : null;
				}
				catch (OperationCanceledException)
				{
					return (string)null;
				}
				catch (Exception ex2)
				{
					((Mod)this).Monitor.Log(" Background outfit generation crashed: " + ex2.Message, (LogLevel)3);
					return (string)null;
				}
			});
			if (DebugLog)
			{
				((Mod)this).Monitor.Log(" Started background outfit compliment generation for " + ((Character)npc).Name + ". HUD waiting message is active.", (LogLevel)2);
			}
		}
		else
		{
			((Mod)this).Monitor.Log(" " + ((Character)npc).Name + " already has a background outfit compliment generation in progress.", (LogLevel)0);
		}
		return true;
	}

	private int GetActiveAiTimeoutSecondsForSafety()
	{
		ActiveAiSettings settings = ActiveAiSettingsResolver.Resolve(Config);
		return Math.Clamp(settings.TimeoutSeconds, 3, 120);
	}

	private bool IsOwnAiWaitingStateActiveFor(NPC npc)
	{
		PendingAiGeneration pending;
		return npc != null && aiGenerationCoordinator.TryGetOutfit(((Character)npc).Name, out pending) && pending != null && pending.Task != null && !pending.Task.IsCompleted;
	}

	private string GetOwnAiWaitingDialogueText(NPC npc, int dotCount)
	{
		int count = Math.Clamp(dotCount, 1, 3);
		string text = new string('.', count);
		string name = ((!string.IsNullOrWhiteSpace((npc != null) ? ((Character)npc).displayName : null)) ? ((Character)npc).displayName : (((npc != null) ? ((Character)npc).Name : null) ?? "NPC"));
		return ((object)((Mod)this).Helper.Translation.Get("hud.npc-noticing", (object)new { name })).ToString() + text;
	}

	private string GetOwnAiReplyWaitingDialogueText(NPC npc, int dotCount)
	{
		int count = Math.Clamp(dotCount, 1, 3);
		string text = new string('.', count);
		string name = ((!string.IsNullOrWhiteSpace((npc != null) ? ((Character)npc).displayName : null)) ? ((Character)npc).displayName : (((npc != null) ? ((Character)npc).Name : null) ?? "NPC"));
		return ((object)((Mod)this).Helper.Translation.Get("hud.npc-thinking", (object)new { name })).ToString() + text;
	}

	private void DrawOwnAiWaitingHudMessage(SpriteBatch spriteBatch, NPC npc, string text)
	{
		//IL_0029: Unknown result type (might be due to invalid IL or missing references)
		//IL_002e: Unknown result type (might be due to invalid IL or missing references)
		//IL_0046: Unknown result type (might be due to invalid IL or missing references)
		//IL_005f: Unknown result type (might be due to invalid IL or missing references)
		//IL_0069: Unknown result type (might be due to invalid IL or missing references)
		//IL_0073: Unknown result type (might be due to invalid IL or missing references)
		//IL_007d: Unknown result type (might be due to invalid IL or missing references)
		//IL_0092: Unknown result type (might be due to invalid IL or missing references)
		//IL_0093: Unknown result type (might be due to invalid IL or missing references)
		//IL_009d: Unknown result type (might be due to invalid IL or missing references)
		//IL_00af: Unknown result type (might be due to invalid IL or missing references)
		//IL_00ba: Unknown result type (might be due to invalid IL or missing references)
		//IL_00bf: Unknown result type (might be due to invalid IL or missing references)
		//IL_00c4: Unknown result type (might be due to invalid IL or missing references)
		//IL_00ce: Unknown result type (might be due to invalid IL or missing references)
		//IL_00e0: Unknown result type (might be due to invalid IL or missing references)
		//IL_00e1: Unknown result type (might be due to invalid IL or missing references)
		if (spriteBatch != null && npc != null && !string.IsNullOrWhiteSpace(text) && Game1.smallFont != null)
		{
			Vector2 val = Game1.smallFont.MeasureString(text);
			Vector2 val2 = default(Vector2);
			val2 = new Vector2(32f, Math.Max(32f, (float)Game1.uiViewport.Height - val.Y - 72f));
			Microsoft.Xna.Framework.Rectangle val3 = default(Microsoft.Xna.Framework.Rectangle);
			val3 = new Microsoft.Xna.Framework.Rectangle((int)val2.X - 16, (int)val2.Y - 10, (int)val.X + 32, (int)val.Y + 20);
			spriteBatch.Draw(Game1.staminaRect, val3, Color.Black * 0.55f);
			spriteBatch.DrawString(Game1.smallFont, text, val2 + new Vector2(2f, 2f), Color.Black * 0.75f);
			spriteBatch.DrawString(Game1.smallFont, text, val2, Color.White);
		}
	}

	private void UpdatePendingOwnAiGenerations()
	{
		if (!aiGenerationCoordinator.HasOutfitGenerations)
		{
			return;
		}
		foreach (string outfitNpcName in aiGenerationCoordinator.GetOutfitNpcNames())
		{
			if (!aiGenerationCoordinator.TryGetOutfit(outfitNpcName, out var pending))
			{
				continue;
			}
			NPC characterFromName = NpcContextResolver.Resolve(outfitNpcName);
			if (pending == null || characterFromName == null || pending.Task == null)
			{
				aiGenerationCoordinator.RemoveOutfit(outfitNpcName);
				continue;
			}
			switch (AiDialogueLifecycle.Advance(pending))
			{
			case AiGenerationLifecycleState.Completed:
				if (!pending.CompletionHandled)
				{
					pending.CompletionHandled = true;
					string generated = null;
					try
					{
						if (pending.Task.Status == TaskStatus.RanToCompletion)
						{
							generated = pending.Task.Result;
						}
					}
					catch (Exception ex)
					{
						((Mod)this).Monitor.Log(" Could not read background AI result: " + ex.Message, (LogLevel)3);
					}
					OpenGeneratedOrFallbackOutfitDialogue(characterFromName, pending, generated);
				}
				aiGenerationCoordinator.RemoveOutfit(outfitNpcName);
				break;
			case AiGenerationLifecycleState.TimedOut:
				((Mod)this).Monitor.Log(" Background generation for " + outfitNpcName + " exceeded the safety timer. Removing pending waiting state.", (LogLevel)3);
				AiRequestLifecycle.Cancel(pending.Cancellation);
				if (pending.IsSpouseDialogue && characterFromName != null)
				{
					ResetClothesState();
					aiGenerationCoordinator.RemoveOutfit(outfitNpcName);
				}
				else if (characterFromName != null)
				{
					otherNpcClothesReactionSystem?.CancelPendingOwnAiGeneration(characterFromName);
					aiGenerationCoordinator.RemoveOutfit(outfitNpcName);
				}
				else
				{
					aiGenerationCoordinator.RemoveOutfit(outfitNpcName);
				}
				break;
			default:
				UpdateOwnAiWaitingVisual(characterFromName, pending);
				break;
			}
		}
	}

	private void UpdateOwnAiWaitingVisual(NPC npc, PendingAiGeneration pending)
	{
		if (npc == null || pending == null || Game1.player == null || ((Character)npc).currentLocation != ((Character)Game1.player).currentLocation)
		{
			return;
		}
		if (pending.WaitingDotTimer > 0)
		{
			pending.WaitingDotTimer--;
			return;
		}
		pending.WaitingDotTimer = 30;
		pending.WaitingDotCount++;
		if (pending.WaitingDotCount > 3)
		{
			pending.WaitingDotCount = 1;
		}
	}

	private void OpenGeneratedOrFallbackOutfitDialogue(NPC npc, PendingAiGeneration pending, string generated)
	{
		//IL_0102: Unknown result type (might be due to invalid IL or missing references)
		//IL_010c: Expected O, but got Unknown
		//IL_03f0: Unknown result type (might be due to invalid IL or missing references)
		if (npc == null || pending == null)
		{
			return;
		}
		if (Game1.player == null || ((Character)npc).currentLocation != ((Character)Game1.player).currentLocation)
		{
			((Mod)this).Monitor.Log(" AI outfit compliment for " + pending.NpcName + " finished, but the player is no longer nearby. Discarding it.", (LogLevel)0);
			if (pending.IsSpouseDialogue)
			{
				KeepSpouseOutfitNoticePendingAfterAiFailure(npc, "the player was no longer nearby when background AI generation finished.");
			}
			else
			{
				otherNpcClothesReactionSystem?.NotifyOwnAiFinalDialogueFailed(npc);
			}
			return;
		}
		bool flag = false;
		bool flag2 = false;
		string text = null;
		if (!string.IsNullOrWhiteSpace(generated))
		{
			if (generated.StartsWith("{{OUTFIT_COMPLIMENTS_ACCESSORY_CLARIFICATION}}", StringComparison.Ordinal))
			{
				flag2 = true;
				generated = generated.Substring("{{OUTFIT_COMPLIMENTS_ACCESSORY_CLARIFICATION}}".Length).Trim();
			}
			npc.CurrentDialogue.Clear();
			string text2 = (pending.IsSpouseDialogue ? "OutfitReactions_SpouseOwnAiOutfitReaction" : "OutfitReactions_GlobalOwnAiOutfitReaction");
			npc.CurrentDialogue.Push(new Dialogue(npc, text2, generated));
			text = generated;
			flag = true;
			if (DebugLog)
			{
				((Mod)this).Monitor.Log(" Background outfit compliment for " + ((Character)npc).Name + " is ready and queued.", (LogLevel)2);
			}
		}
		else
		{
			((Mod)this).Monitor.Log(" Background outfit generation did not produce a usable line for " + ((Character)npc).Name + ". Trying configured fallbacks.", (LogLevel)3);
			flag = TryQueueNonAiOutfitFallback(npc, pending.IsSpouseDialogue, clearExistingDialogue: true);
		}
		if (!flag || npc.CurrentDialogue.Count <= 0)
		{
			Game1.activeClickableMenu = null;
			if (pending.IsSpouseDialogue)
			{
				KeepSpouseOutfitNoticePendingAfterAiFailure(npc, "background AI generation did not produce a usable line.");
			}
			else
			{
				otherNpcClothesReactionSystem?.NotifyOwnAiFinalDialogueFailed(npc);
			}
			return;
		}
		bool hasHatReactionMemoryDraft = !string.IsNullOrWhiteSpace(text) && BeginVanillaHatReactionMemoryDraft(npc, text);
		Action onFinished = null;
		if (pending.IsSpouseDialogue)
		{
			onFinished = delegate
			{
				CompleteSpouseAfterOutfitDialogue(npc);
			};
		}
		bool flag3 = false;
		FashionSenseChangeInfo effectiveFashionSenseChangeInfoForNpc = GetEffectiveFashionSenseChangeInfoForNpc(npc);
		if (TryResolveSpecialItemNoticeForNpc(npc, effectiveFashionSenseChangeInfoForNpc, requireNpcMemoryForRemoval: true, out var notice) && notice != null && notice.WasRemoved)
		{
			flag3 = true;
		}
		bool flag4 = false;
		string secretId = null;
		if (!flag3 && !string.IsNullOrWhiteSpace(text) && !flag2 && specialItemReactionService != null)
		{
			FashionSenseChangeInfo effectiveFashionSenseChangeInfoForNpc2 = GetEffectiveFashionSenseChangeInfoForNpc(npc);
			if (TryResolveSpecialItemNoticeForNpc(npc, effectiveFashionSenseChangeInfoForNpc2, requireNpcMemoryForRemoval: false, out var notice2) && notice2 != null && notice2.HasSecret && !notice2.WasRemoved && !specialItemReactionService.NpcAlreadyKnowsSecret(notice2.SecretId, ((Character)npc).Name))
			{
				flag4 = true;
				secretId = notice2.SecretId;
			}
		}
		if (!string.IsNullOrWhiteSpace(text) && flag2 && Config.EnablePlayerReplyMenuAfterOutfitCompliment)
		{
			InstallAccessoryClarificationInputAfterOutfitDialogue(npc, pending.IsSpouseDialogue, text, onFinished);
		}
		else if (flag4)
		{
			InstallSecretRevealChoiceMenu(npc, pending.IsSpouseDialogue, text, secretId, onFinished);
		}
		else if (!flag3 && !string.IsNullOrWhiteSpace(text) && Config.EnablePlayerReplyMenuAfterOutfitCompliment)
		{
			InstallPlayerReplyMenuAfterOutfitDialogue(npc, pending.IsSpouseDialogue, text, onFinished);
		}
		else
		{
			if (pending.IsSpouseDialogue)
			{
				InstallSpouseAfterOutfitDialogue(npc, hasHatReactionMemoryDraft);
			}
			else if (hasHatReactionMemoryDraft)
			{
				InstallHatReactionMemoryCommitAfterDialogue(npc);
			}
		}
		if (!pending.IsSpouseDialogue)
		{
			otherNpcClothesReactionSystem?.NotifyOwnAiFinalDialogueOpened(npc);
		}
		if (!pending.IsSpouseDialogue)
		{
			MarkCurrentOutfitAsNoticed(npc);
		}
		Game1.activeClickableMenu = null;
		((Character)npc).faceGeneralDirection(((Character)Game1.player).getStandingPosition(), 0, false);
		Game1.drawDialogue(npc);
	}

	private bool TryQueueNonAiOutfitFallback(NPC npc, bool isSpouseDialogue, bool clearExistingDialogue)
	{
		if (npc != null)
		{
			((Mod)this).Monitor.Log(" No AI outfit dialogue was queued for " + ((Character)npc).Name + ". Manual JSON outfit dialogue is disabled in this AI-only build.", (LogLevel)3);
		}
		return false;
	}

	private void InstallSpouseAfterOutfitDialogue(NPC npc, bool commitHatReactionMemory = false)
	{
		//IL_001c: Unknown result type (might be due to invalid IL or missing references)
		//IL_0026: Expected O, but got Unknown
		Game1.afterDialogues = delegate
		{
			CompleteSpouseAfterOutfitDialogue(npc);
			if (commitHatReactionMemory)
			{
				hatMemoryService?.CommitReactionDraft(((Character)npc)?.Name);
			}
		};
	}

	private void InstallHatReactionMemoryCommitAfterDialogue(NPC npc)
	{
		string npcName = ((Character)npc)?.Name;
		Game1.afterDialogues = delegate
		{
			hatMemoryService?.CommitReactionDraft(npcName);
		};
	}

	private void CompleteSpouseAfterOutfitDialogue(NPC npc)
	{
		//IL_0057: Unknown result type (might be due to invalid IL or missing references)
		MarkCurrentOutfitAsNoticed(npc);
		ClearOutfitPrompt(npc);
		bool flag = npc != null && Game1.player != null && ((Character)npc).currentLocation == ((Character)Game1.player).currentLocation;
		if (flag)
		{
			CaptureSpouseOutfitSpecialActionBeforeOutfit(npc);
			AnimatedSprite sprite = ((Character)npc).Sprite;
			if (sprite != null)
			{
				sprite.StopAnimation();
			}
			((Character)npc).faceGeneralDirection(((Character)Game1.player).getStandingPosition(), 0, false);
			DoClothesFinalEmotes(npc);
			if (spouseRouteController.HasRoute)
			{
				spouseRouteController.Restore(npc, ((Mod)this).Monitor, DebugLog);
			}
			else
			{
				spouseRouteController.Clear();
			}
		}
		else
		{
			spouseRouteController.Clear();
		}
		spouseDialogueController.Restore(npc, Game1.player, restoreTalkState: true, clearCurrentDialogue: true, ((Mod)this).Monitor, DebugLog);
		ResetClothesState();
		if (flag)
		{
			BeginSpousePostOutfitLinger(npc);
		}
	}

	private void BeginSpousePostOutfitLinger(NPC npc)
	{
		if (npc == null || Game1.player == null || ((Character)npc).currentLocation != ((Character)Game1.player).currentLocation)
		{
			ClearSpousePostOutfitLinger();
			return;
		}
		SpousePostOutfitLingerController.Begin(spouseProximityState, npc);
		CaptureSpouseOutfitSpecialActionBeforeOutfit(npc);
		SpousePostOutfitLingerController.ApplyHoldPose(spouseProximityState, npc, Game1.player);
		if (DebugLog)
		{
			((Mod)this).Monitor.Log($"[CLOTHES SPOUSE] {((Character)npc).Name} will linger after the outfit compliment until distance >= {600f:F0} or {360} ticks.", (LogLevel)2);
		}
	}

	private void UpdateSpousePostOutfitLinger()
	{
		if (!spouseProximityState.LingerActive)
		{
			return;
		}
		NPC lingerNpc = spouseProximityState.LingerNpc;
		if (lingerNpc == null || Game1.player == null || !Context.IsWorldReady)
		{
			ClearSpousePostOutfitLinger();
			return;
		}
		bool flag = ((Character)lingerNpc).currentLocation == ((Character)Game1.player).currentLocation;
		float distance = (flag ? DistanceToPlayer(lingerNpc) : 600f);
		bool hasCapturedSpecialAction = spouseSpecialActionController.HasSnapshotFor(lingerNpc);
		if (!SpousePostOutfitLingerController.TickAndShouldResume(spouseProximityState, flag, distance, hasCapturedSpecialAction, 300f))
		{
			SpousePostOutfitLingerController.ApplyHoldPose(spouseProximityState, lingerNpc, Game1.player);
			return;
		}
		if (!spouseSpecialActionController.TryRestore(force: true, Game1.player, Game1.activeClickableMenu != null, Game1.dialogueUp, DistanceToPlayer, 300f, ((Mod)this).Monitor, DebugLog))
		{
			((Character)lingerNpc).movementPause = 0;
		}
		ClearSpousePostOutfitLinger();
	}

	private void ClearSpousePostOutfitLinger()
	{
		SpousePostOutfitLingerController.Clear(spouseProximityState);
	}

	private void InstallPlayerReplyMenuAfterOutfitDialogue(NPC npc, bool isSpouseDialogue, string npcCompliment, Action onFinished)
	{
		//IL_0056: Unknown result type (might be due to invalid IL or missing references)
		//IL_0060: Expected O, but got Unknown
		OutfitReplyConversationHistory obj = outfitReplyConversationHistory;
		NPC obj2 = npc;
		obj.Start((obj2 != null) ? ((Character)obj2).Name : null, npcCompliment);
		Game1.afterDialogues = delegate
		{
			ShowPlayerReplyChoiceMenu(npc, isSpouseDialogue, npcCompliment, onFinished);
		};
	}

	private void InstallSecretRevealChoiceMenu(NPC npc, bool isSpouseDialogue, string npcCompliment, string secretId, Action onFinished)
	{
		//IL_003a: Unknown result type (might be due to invalid IL or missing references)
		//IL_0044: Expected O, but got Unknown
		Game1.afterDialogues = delegate
		{
			//IL_006b: Unknown result type (might be due to invalid IL or missing references)
			//IL_0071: Invalid comparison between Unknown and I4
			if (npc == null || Game1.player == null || ((Character)npc).currentLocation != ((Character)Game1.player).currentLocation)
			{
				ModEntry modEntry = this;
				Action onFinished2 = onFinished;
				NPC obj = npc;
				modEntry.FinishPlayerReplyInteraction(onFinished2, (obj != null) ? ((Character)obj).Name : null);
			}
			else
			{
				bool isPt = (int)LocalizedContentManager.CurrentLanguageCode == 4;
				string text = ((Character)npc).displayName ?? ((Character)npc).Name;
				string title = (isPt ? ("Contar o segredo a " + text + "?") : ("Tell " + text + " the secret?"));
				string replyLabel = (isPt ? "Contar" : "Tell them");
				string leaveLabel = (isPt ? "Não" : "Not now");
				ShowNativeOutfitReplyChoice(title, replyLabel, leaveLabel, delegate
				{
					specialItemReactionService?.RevealSecret(secretId, ((Character)npc).Name);
					OutfitAiContext outfitAiContext = BuildOutfitAiContext(npc, isSpouseDialogue);
					if (outfitAiContext == null)
					{
						ModEntry modEntry2 = this;
						Action onFinished3 = onFinished;
						NPC obj2 = npc;
						modEntry2.FinishPlayerReplyInteraction(onFinished3, (obj2 != null) ? ((Character)obj2).Name : null);
					}
					else
					{
						string text2 = specialItemReactionService?.GetSecretRevealMessage(secretId) ?? "";
						string playerReply = ((!string.IsNullOrWhiteSpace(text2)) ? text2 : (isPt ? "[O jogador contou ao NPC sobre a origem secreta do item.]" : "[The player just told the NPC about the item's secret origin.]"));
						outfitAiContext.ConversationTranscript = null;
						StartPlayerReplyFollowUpGeneration(npc, isSpouseDialogue, npcCompliment, playerReply, onFinished, outfitAiContext);
					}
				}, delegate
				{
					ModEntry modEntry2 = this;
					Action onFinished3 = onFinished;
					NPC obj2 = npc;
					modEntry2.FinishPlayerReplyInteraction(onFinished3, (obj2 != null) ? ((Character)obj2).Name : null);
				});
			}
		};
	}

	private void InstallAccessoryClarificationInputAfterOutfitDialogue(NPC npc, bool isSpouseDialogue, string npcLine, Action onFinished)
	{
		//IL_0032: Unknown result type (might be due to invalid IL or missing references)
		//IL_003c: Expected O, but got Unknown
		Game1.afterDialogues = delegate
		{
			//IL_0001: Unknown result type (might be due to invalid IL or missing references)
			//IL_0007: Invalid comparison between Unknown and I4
			string titleOverride = (((int)LocalizedContentManager.CurrentLanguageCode == 4) ? "Responder:" : "Reply:");
			OpenPlayerOutfitReplyInputMenu(npc, isSpouseDialogue, npcLine, onFinished, titleOverride, delegate
			{
				ModEntry modEntry = this;
				Action onFinished2 = onFinished;
				NPC obj = npc;
				modEntry.FinishPlayerReplyInteraction(onFinished2, (obj != null) ? ((Character)obj).Name : null);
			}, saveAccessoryClarification: true);
		};
	}

	private void ShowPlayerReplyChoiceMenu(NPC npc, bool isSpouseDialogue, string npcCompliment, Action onFinished)
	{
		//IL_008c: Unknown result type (might be due to invalid IL or missing references)
		//IL_0092: Invalid comparison between Unknown and I4
		//IL_00a1: Unknown result type (might be due to invalid IL or missing references)
		//IL_00a7: Invalid comparison between Unknown and I4
		//IL_00b6: Unknown result type (might be due to invalid IL or missing references)
		//IL_00bc: Invalid comparison between Unknown and I4
		if (!Config.EnablePlayerReplyMenuAfterOutfitCompliment || npc == null || Game1.player == null || ((Character)npc).currentLocation != ((Character)Game1.player).currentLocation)
		{
			Action onFinished2 = onFinished;
			NPC obj = npc;
			FinishPlayerReplyInteraction(onFinished2, (obj != null) ? ((Character)obj).Name : null, commitHatReactionMemory: true);
			return;
		}
		string title = (((int)LocalizedContentManager.CurrentLanguageCode == 4) ? "Responder ao comentário?" : "Reply to the comment?");
		string replyLabel = (((int)LocalizedContentManager.CurrentLanguageCode == 4) ? "Responder" : "Reply");
		string leaveLabel = (((int)LocalizedContentManager.CurrentLanguageCode == 4) ? "Ir embora" : "Leave");
		ShowNativeOutfitReplyChoice(title, replyLabel, leaveLabel, delegate
		{
			OpenPlayerOutfitReplyInputMenu(npc, isSpouseDialogue, npcCompliment, onFinished);
		}, delegate
		{
			ModEntry modEntry = this;
			Action onFinished3 = onFinished;
			NPC obj2 = npc;
			modEntry.FinishPlayerReplyInteraction(onFinished3, (obj2 != null) ? ((Character)obj2).Name : null, commitHatReactionMemory: true);
		});
	}

	private void ShowNativeOutfitReplyChoice(string title, string replyLabel, string leaveLabel, Action respond, Action leave)
	{
		const string ReplyKey = "OutfitReactions_Reply";
		Response[] responses = new Response[2]
		{
			new Response(ReplyKey, replyLabel),
			new Response("OutfitReactions_Leave", leaveLabel)
		};
		Game1.currentLocation.createQuestionDialogue(title, responses, delegate(Farmer _, string answer)
		{
			Action selectedAction = string.Equals(answer, ReplyKey, StringComparison.Ordinal)
				? respond
				: leave;
			Game1.afterDialogues = delegate
			{
				selectedAction?.Invoke();
			};
		});
	}

	private void OpenPlayerOutfitReplyInputMenu(NPC npc, bool isSpouseDialogue, string npcCompliment, Action onFinished, string titleOverride = null, Action cancelOverride = null, bool saveAccessoryClarification = false)
	{
		//IL_00a4: Unknown result type (might be due to invalid IL or missing references)
		//IL_00aa: Invalid comparison between Unknown and I4
		//IL_00c1: Unknown result type (might be due to invalid IL or missing references)
		//IL_00c7: Invalid comparison between Unknown and I4
		//IL_00d6: Unknown result type (might be due to invalid IL or missing references)
		//IL_00dc: Invalid comparison between Unknown and I4
		if (npc == null || Game1.player == null || ((Character)npc).currentLocation != ((Character)Game1.player).currentLocation)
		{
			Action onFinished2 = onFinished;
			NPC obj = npc;
			FinishPlayerReplyInteraction(onFinished2, (obj != null) ? ((Character)obj).Name : null);
			return;
		}
		string title = ((!string.IsNullOrWhiteSpace(titleOverride)) ? titleOverride : (((int)LocalizedContentManager.CurrentLanguageCode == 4) ? "Escreva sua resposta:" : "Write your reply:"));
		string sendLabel = (((int)LocalizedContentManager.CurrentLanguageCode == 4) ? "Enviar" : "Send");
		string cancelLabel = (((int)LocalizedContentManager.CurrentLanguageCode == 4) ? "Cancelar" : "Cancel");
		Game1.activeClickableMenu = (IClickableMenu)(object)new OutfitPlayerReplyTextInputMenu(title, sendLabel, cancelLabel, delegate(string replyText)
		{
			string text = CleanPlayerOutfitReplyText(replyText);
			if (string.IsNullOrWhiteSpace(text))
			{
				OpenPlayerOutfitReplyInputMenu(npc, isSpouseDialogue, npcCompliment, onFinished, titleOverride, cancelOverride, saveAccessoryClarification);
			}
			else
			{
				if (saveAccessoryClarification)
				{
					SavePlayerProvidedAccessoryDescriptionForCurrentChange(text);
				}
				if (!CanUseOwnAiForOutfitDialogue(npc))
				{
					ModEntry modEntry = this;
					Action onFinished3 = onFinished;
					NPC obj2 = npc;
					modEntry.FinishPlayerReplyInteraction(onFinished3, (obj2 != null) ? ((Character)obj2).Name : null);
				}
				else
				{
					OutfitReplyConversationHistory obj3 = outfitReplyConversationHistory;
					NPC obj4 = npc;
					obj3.Append((obj4 != null) ? ((Character)obj4).Name : null, "Player", text);
					StartPlayerReplyFollowUpGeneration(npc, isSpouseDialogue, npcCompliment, text, onFinished);
				}
			}
		}, delegate
		{
			if (cancelOverride != null)
			{
				cancelOverride();
			}
			else
			{
				ShowPlayerReplyChoiceMenu(npc, isSpouseDialogue, npcCompliment, onFinished);
			}
		});
	}

	private static string CleanPlayerOutfitReplyText(string text)
	{
		if (string.IsNullOrWhiteSpace(text))
		{
			return "";
		}
		text = Regex.Replace(text, "\\s+", " ").Trim();
		if (text.Length > 800)
		{
			text = text.Substring(0, 800).Trim();
		}
		return text;
	}

	private void StartPlayerReplyFollowUpGeneration(NPC npc, bool isSpouseDialogue, string npcCompliment, string playerReply, Action onFinished, OutfitAiContext prebuiltContext = null)
	{
		if (npc == null || Game1.player == null || ((Character)npc).currentLocation != ((Character)Game1.player).currentLocation)
		{
			FinishPlayerReplyInteraction(onFinished, (npc != null) ? ((Character)npc).Name : null);
			return;
		}
		OutfitAiContext context = prebuiltContext ?? BuildOutfitAiContext(npc, isSpouseDialogue);
		if (context == null)
		{
			FinishPlayerReplyInteraction(onFinished, (npc != null) ? ((Character)npc).Name : null);
			return;
		}
		if (prebuiltContext == null)
		{
			context.ConversationTranscript = outfitReplyConversationHistory.BuildTranscript(((Character)npc).Name);
		}
		hatMemoryService?.SetDraftPlayerReply(((Character)npc).Name, playerReply);
		Game1.activeClickableMenu = null;
		Game1.afterDialogues = null;
		PendingAiPlayerReplyGeneration pending = new PendingAiPlayerReplyGeneration
		{
			NpcName = ((Character)npc).Name,
			IsSpouseDialogue = isSpouseDialogue,
			NpcCompliment = (npcCompliment ?? ""),
			PlayerReply = (playerReply ?? ""),
			WaitingDotCount = 1,
			WaitingDotTimer = 30,
			SafetyTimer = Math.Max(600, GetActiveAiTimeoutSecondsForSafety() * 120),
			Cancellation = new CancellationTokenSource(),
			OnFinished = onFinished
		};
		aiGenerationCoordinator.StartReply(pending, delegate(CancellationToken cancellationToken)
		{
			try
			{
				string dialogue;
				return outfitAiService.TryGenerateFollowUp(context, pending.NpcCompliment, pending.PlayerReply, out dialogue, cancellationToken) ? dialogue : null;
			}
			catch (OperationCanceledException)
			{
				return (string)null;
			}
			catch (Exception ex2)
			{
				((Mod)this).Monitor.Log(" Background player-reply follow-up crashed: " + ex2.Message, (LogLevel)3);
				return (string)null;
			}
		});
		if (DebugLog)
		{
			((Mod)this).Monitor.Log(" Started background player-reply follow-up generation for " + ((Character)npc).Name + ".", (LogLevel)2);
		}
	}

	private void UpdatePendingOwnAiPlayerReplyGenerations()
	{
		if (!aiGenerationCoordinator.HasReplyGenerations)
		{
			return;
		}
		foreach (string replyNpcName in aiGenerationCoordinator.GetReplyNpcNames())
		{
			if (!aiGenerationCoordinator.TryGetReply(replyNpcName, out var pending))
			{
				continue;
			}
			NPC characterFromName = NpcContextResolver.Resolve(replyNpcName);
			if (pending == null || characterFromName == null || pending.Task == null)
			{
				FinishPlayerReplyInteraction(pending?.OnFinished, pending?.NpcName);
				aiGenerationCoordinator.RemoveReply(replyNpcName);
				continue;
			}
			switch (AiDialogueLifecycle.Advance(pending))
			{
			case AiGenerationLifecycleState.Completed:
				if (!pending.CompletionHandled)
				{
					pending.CompletionHandled = true;
					string generated = null;
					try
					{
						if (pending.Task.Status == TaskStatus.RanToCompletion)
						{
							generated = pending.Task.Result;
						}
					}
					catch (Exception ex)
					{
						((Mod)this).Monitor.Log(" Could not read player-reply follow-up result: " + ex.Message, (LogLevel)3);
					}
					OpenGeneratedPlayerReplyFollowUp(characterFromName, pending, generated);
				}
				aiGenerationCoordinator.RemoveReply(replyNpcName);
				break;
			case AiGenerationLifecycleState.TimedOut:
				((Mod)this).Monitor.Log(" Player-reply follow-up generation for " + replyNpcName + " exceeded the safety timer.", (LogLevel)3);
				AiRequestLifecycle.Cancel(pending.Cancellation);
				FinishPlayerReplyInteraction(pending.OnFinished, pending.NpcName);
				aiGenerationCoordinator.RemoveReply(replyNpcName);
				break;
			default:
				UpdateOwnAiPlayerReplyWaitingVisual(pending);
				break;
			}
		}
	}

	private void UpdateOwnAiPlayerReplyWaitingVisual(PendingAiPlayerReplyGeneration pending)
	{
		if (pending == null)
		{
			return;
		}
		if (pending.WaitingDotTimer > 0)
		{
			pending.WaitingDotTimer--;
			return;
		}
		pending.WaitingDotTimer = 30;
		pending.WaitingDotCount++;
		if (pending.WaitingDotCount > 3)
		{
			pending.WaitingDotCount = 1;
		}
	}

	private void OpenGeneratedPlayerReplyFollowUp(NPC npc, PendingAiPlayerReplyGeneration pending, string generated)
	{
		//IL_016d: Unknown result type (might be due to invalid IL or missing references)
		//IL_0177: Expected O, but got Unknown
		//IL_01a8: Unknown result type (might be due to invalid IL or missing references)
		//IL_01b2: Expected O, but got Unknown
		//IL_01bd: Unknown result type (might be due to invalid IL or missing references)
		if (npc == null || pending == null)
		{
			FinishPlayerReplyInteraction(pending?.OnFinished, pending?.NpcName);
			return;
		}
		if (Game1.player == null || ((Character)npc).currentLocation != ((Character)Game1.player).currentLocation || string.IsNullOrWhiteSpace(generated))
		{
			FinishPlayerReplyInteraction(pending.OnFinished, pending.NpcName);
			return;
		}
		if (generated.StartsWith("{{OUTFIT_COMPLIMENTS_ACCESSORY_CLARIFICATION}}", StringComparison.Ordinal))
		{
			generated = generated.Substring("{{OUTFIT_COMPLIMENTS_ACCESSORY_CLARIFICATION}}".Length).Trim();
		}
		if (string.IsNullOrWhiteSpace(generated))
		{
			FinishPlayerReplyInteraction(pending.OnFinished, pending.NpcName);
			return;
		}
		string text = (pending.IsSpouseDialogue ? "OutfitReactions_SpousePlayerReplyFollowUp" : "OutfitReactions_GlobalPlayerReplyFollowUp");
		npc.CurrentDialogue.Push(new Dialogue(npc, text, generated));
		outfitReplyConversationHistory.Append(pending.NpcName, "NPC", generated);
		hatMemoryService?.SetDraftNpcFollowUp(pending.NpcName, generated);
		Game1.activeClickableMenu = null;
		Game1.afterDialogues = delegate
		{
			// The generated follow-up is the final line of this outfit interaction.
			// Finish only our temporary reply flow; any game/mod dialogue already
			// queued beneath this line remains available for a later interaction.
			FinishPlayerReplyInteraction(pending.OnFinished, pending.NpcName, commitHatReactionMemory: true);
		};
		((Character)npc).faceGeneralDirection(((Character)Game1.player).getStandingPosition(), 0, false);
		Game1.drawDialogue(npc);
	}

	private void CancelAllPendingOwnAiGenerations()
	{
		IReadOnlyList<PendingAiPlayerReplyGeneration> readOnlyList = aiGenerationCoordinator.CancelAll();
		foreach (PendingAiPlayerReplyGeneration item in readOnlyList)
		{
			FinishPlayerReplyInteraction(item?.OnFinished, item?.NpcName);
		}
		hatMemoryService?.DiscardAllReactionDrafts();
	}

	private void FinishPlayerReplyInteraction(Action onFinished, string npcName = null, bool commitHatReactionMemory = false)
	{
		outfitReplyConversationHistory.Reset(npcName);
		Game1.activeClickableMenu = null;
		Game1.afterDialogues = null;
		onFinished?.Invoke();
		if (commitHatReactionMemory)
		{
			hatMemoryService?.CommitReactionDraft(npcName);
		}
		else
		{
			hatMemoryService?.DiscardReactionDraft(npcName);
		}
	}

	private bool TryQueueOtherNpcOutfitDialogue(NPC npc)
	{
		if (!Config.EnableNpcOutfitReactions || npc == null)
		{
			return false;
		}
		if (TryShowOwnAiOutfitDialogue(npc, isSpouseDialogue: false, clearExistingDialogue: false))
		{
			return true;
		}
		((Mod)this).Monitor.Log(" No AI outfit dialogue was queued for " + ((Character)npc).Name + ". Manual JSON outfit dialogue is disabled in this AI-only build.", (LogLevel)3);
		return false;
	}

	private bool RefreshOtherNpcOutfitPrompt(NPC npc)
	{
		return npc != null;
	}

	private void ClearOutfitPrompt(NPC npc)
	{
	}
}
