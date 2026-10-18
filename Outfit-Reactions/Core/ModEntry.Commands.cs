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
{	private void ClearOutfitNoticeMemoryCommand(string command, string[] args)
	{
		//IL_0039: Unknown result type (might be due to invalid IL or missing references)
		if (!Context.IsWorldReady || Game1.player == null)
		{
			((Mod)this).Monitor.Log("[OC DEBUG] Load a save before clearing outfit notice memory.", (LogLevel)3);
			return;
		}
		List<string> list = ((IEnumerable<string>)(object)((NetDictionary<string, string, NetString, SerializableDictionary<string, string>, NetStringDictionary<string, NetString>>)(object)((Character)Game1.player).modData).Keys).Where((string key) => key.StartsWith("NatrollEXE.OutfitReactions.OutfitNotice.", StringComparison.OrdinalIgnoreCase) || key.StartsWith("NatrollEXE.OutfitReactions/SpecialItemSeen/", StringComparison.OrdinalIgnoreCase) || key.StartsWith("NatrollEXE.OutfitReactions/PantsSeen/", StringComparison.OrdinalIgnoreCase)).ToList();
		foreach (string item in list)
		{
			((NetDictionary<string, string, NetString, SerializableDictionary<string, string>, NetStringDictionary<string, NetString>>)(object)((Character)Game1.player).modData).Remove(item);
		}
		ResetClothesState(clearChangeFlag: true);
		npcsReactedToCurrentNotice.Clear();
		otherNpcClothesReactionSystem?.Reset();
		((Mod)this).Monitor.Log($"[OC DEBUG] Cleared {list.Count} outfit notice memory key(s) from this save and reset pending outfit notice state.", (LogLevel)2);
	}

	private void VoiceSampleReportCommand(string command, string[] args)
	{
		if (!Context.IsWorldReady)
		{
			((Mod)this).Monitor.Log("Load a save first, then run oc_test_voicesamples so NPC dialogue is available.", (LogLevel)3);
			return;
		}
		if (outfitAiService == null)
		{
			((Mod)this).Monitor.Log("The outfit AI service is not ready yet.", (LogLevel)3);
			return;
		}
		string text = outfitAiService.BuildVoiceSampleReport();
		((Mod)this).Monitor.Log(text, (LogLevel)2);
	}

	private void VoiceSamplePreviewCommand(string command, string[] args)
	{
		if (!Context.IsWorldReady)
		{
			((Mod)this).Monitor.Log("Load a save first, then run oc_preview_voicesamples <NpcName> so NPC dialogue is available.", (LogLevel)3);
			return;
		}
		if (outfitAiService == null)
		{
			((Mod)this).Monitor.Log("The outfit AI service is not ready yet.", (LogLevel)3);
			return;
		}
		if (args == null || args.Length == 0)
		{
			((Mod)this).Monitor.Log("Usage: oc_preview_voicesamples <NpcName>  (e.g. oc_preview_voicesamples Victoria)", (LogLevel)3);
			return;
		}
		string npcName = string.Join(" ", args).Trim();
		string text = outfitAiService.BuildVoiceSamplePreview(npcName, Game1.currentSeason);
		((Mod)this).Monitor.Log(text, (LogLevel)2);
	}

	private void DebugOutfitNoticeCommand(string command, string[] args)
	{
		if (!Context.IsWorldReady || Game1.player == null)
		{
			((Mod)this).Monitor.Log("[OC DEBUG] Load a save before running this command.", (LogLevel)3);
			return;
		}
		string outfitId;
		string value = (TryGetCurrentSavedFashionSenseOutfitId(out outfitId) ? outfitId : "<none>");
		string value2 = ((lastFashionSenseChangeInfo != null) ? GetFashionSenseDialogueKey(lastFashionSenseChangeInfo) : "");
		string value3 = ((lastFashionSenseChangeInfo == null) ? "<none>" : $"count={lastFashionSenseChangeInfo.CountChanges()} hair={lastFashionSenseChangeInfo.ChangedHair} accessory={lastFashionSenseChangeInfo.ChangedAccessory} hat={lastFashionSenseChangeInfo.ChangedHat} outfit={lastFashionSenseChangeInfo.ChangedOutfit} newHair={lastFashionSenseChangeInfo.NewHairId ?? ""} newAccessory={lastFashionSenseChangeInfo.NewAccessoryId ?? ""} newHat={lastFashionSenseChangeInfo.NewHatId ?? ""} newOutfit={lastFashionSenseChangeInfo.NewOutfitId ?? ""}");
		((Mod)this).Monitor.Log($"[OC DEBUG] Config: Enabled={Config.Enabled}, NPC reactions={Config.EnableNpcOutfitReactions}, NPC chance={Config.NpcOutfitReactionChance}, NPC distance={Config.OutfitNoticeDistance}, spouse distance={Config.OutfitNoticeDistance}.", (LogLevel)2);
		((Mod)this).Monitor.Log($"[OC DEBUG] Fashion Sense: fsApi={fsApi != null}, currentSavedOutfit={value}, changedClothes={changedClothes}, dialogueKey='{value2}', change={value3}.", (LogLevel)2);
		((Mod)this).Monitor.Log($"[OC DEBUG] NPC profile reactions: Sebastian={outfitAiService?.HasProfile("Sebastian")}, Penny={outfitAiService?.HasProfile("Penny")}, Robin={outfitAiService?.HasProfile("Robin")}. Any NPC with an enabled profile can react.", (LogLevel)2);
		NPC spouse = GetSpouse();
		if (spouse != null)
		{
			float value4 = DistanceToPlayer(spouse);
			((Mod)this).Monitor.Log($"[OC DEBUG] Spouse {((Character)spouse).Name}: sameLoc={((Character)spouse).currentLocation == ((Character)Game1.player).currentLocation}, dist={value4:0}, facing={IsNpcFacingPlayer(spouse)}, canNotice={CanNpcNoticeCurrentOutfitNotice(spouse)}, shouldStart={ShouldStartClothesReaction(spouse)}, firstNoticeDone={clothesFirstNoticeDone}, reacting={isReactingToClothes}, ready={clothesComplimentReady}.", (LogLevel)2);
		}
		if (Game1.currentLocation?.characters == null || Game1.currentLocation.characters.Count <= 0)
		{
			((Mod)this).Monitor.Log("[OC DEBUG] No NPCs are currently loaded in the player's location.", (LogLevel)2);
			return;
		}
		foreach (NPC item in ((IEnumerable<NPC>)Game1.currentLocation.characters).Where((NPC n) => n != null).OrderBy(DistanceToPlayer).Take(10))
		{
			float num = DistanceToPlayer(item);
			bool value5 = ((Character)item).currentLocation == ((Character)Game1.player).currentLocation;
			bool value6 = outfitAiService?.HasProfile(((Character)item).Name) ?? false;
			bool value7 = CanNpcReactToOutfit(item);
			bool value8 = CanNpcNoticeCurrentOutfitNotice(item);
			bool value9 = IsNpcFacingPlayer(item);
			bool value10 = num > Math.Max(64f, Config.OutfitNoticeDistance);
			((Mod)this).Monitor.Log($"[OC DEBUG] NPC {((Character)item).Name}: dist={num:0}, sameLoc={value5}, villager={((Character)item).IsVillager}, invisible={item.IsInvisible}, sleeping={((NetFieldBase<bool, NetBool>)(object)item.isSleeping).Value}, profile={value6}, canReact={value7}, canNotice={value8}, facing={value9}, tooFar={value10}.", (LogLevel)2);
		}
	}
}
