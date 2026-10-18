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
{
	[HarmonyPatch]
	private static class NPCCheckActionPatch
	{
		private static bool firstRunLogged;

		private static MethodBase TargetMethod()
		{
			return AccessTools.Method(typeof(NPC), "checkAction", new Type[2]
			{
				typeof(Farmer),
				typeof(GameLocation)
			});
		}

		[HarmonyPriority(800)]
		private static bool Prefix(NPC __instance, Farmer who, GameLocation l, ref bool __result)
		{
			try
			{
				ModEntry activeInstance = Instance;
				if (activeInstance?.Config?.Enabled != true)
				{
					return true;
				}
				if (!firstRunLogged)
				{
					firstRunLogged = true;
					if (DebugLog)
					{
						ModEntry instance = Instance;
						if (instance != null)
						{
							IMonitor monitor = ((Mod)instance).Monitor;
							if (monitor != null)
							{
								monitor.Log("[CLOTHES PRIORITY] NPC.checkAction prefix ran for the first time.", (LogLevel)2);
							}
						}
					}
				}
				ModEntry instance2 = Instance;
				if (instance2 != null && instance2.TryHandleOutfitDialogueOrBlockNpcInteraction(__instance))
				{
					__result = true;
					return false;
				}
			}
			catch (Exception ex)
			{
				ModEntry instance3 = Instance;
				if (instance3 != null)
				{
					IMonitor monitor2 = ((Mod)instance3).Monitor;
					if (monitor2 != null)
					{
						monitor2.Log("[CLOTHES PRIORITY] Error while prioritizing/blocking outfit dialogue before NPC.checkAction: " + ex, (LogLevel)3);
					}
				}
			}
			return true;
		}
	}

	[HarmonyPatch(typeof(NPC), "doMiddleAnimation")]
	private static class NPCDoMiddleAnimationPatch
	{
		private static bool Prefix(NPC __instance)
		{
			try
			{
				if (__instance == null || Instance?.Config?.Enabled != true || Instance.otherNpcClothesReactionSystem == null)
				{
					return true;
				}
				if (!Instance.otherNpcClothesReactionSystem.IsHeldForFishingSpecialAction(__instance))
				{
					return true;
				}
				return false;
			}
			catch (Exception value)
			{
				ModEntry instance = Instance;
				if (instance != null)
				{
					IMonitor monitor = ((Mod)instance).Monitor;
					if (monitor != null)
					{
						monitor.Log($"[NPC OUTFIT] Error suppressing doMiddleAnimation: {value}", (LogLevel)3);
					}
				}
				return true;
			}
		}
	}

	private sealed class FashionSenseChangeInfo
	{
		public bool ChangedHair;

		public bool ChangedAccessory;

		public bool ChangedHat;

		public bool ChangedShirt;

		public bool ChangedPants;

		public bool ChangedSleeves;

		public bool ChangedShoes;

		public bool ChangedOutfit;

		public string NewHairId;

		public string NewAccessoryId;

		public string NewHatId;

		public string NewVanillaHatId;

		public bool VanillaHatChanged;

		public bool VanillaHatRemoved;

		public string PreviousVanillaHatId;

		public List<string> PreviousVanillaHatSpecialItemCandidates = new List<string>();

		public bool VanillaPantsChanged;

		public bool VanillaPantsRemoved;

		public string PreviousVanillaPantsName;

		public string NewVanillaPantsName;

		public List<string> PreviousVanillaPantsSpecialItemCandidates = new List<string>();

		public List<string> NewVanillaPantsSpecialItemCandidates = new List<string>();

		public bool VanillaShirtChanged;

		public bool VanillaShirtRemoved;

		public string PreviousVanillaShirtName;

		public string NewVanillaShirtName;

		public List<string> PreviousVanillaShirtSpecialItemCandidates = new List<string>();

		public List<string> NewVanillaShirtSpecialItemCandidates = new List<string>();

		public bool VanillaShoesChanged;

		public bool VanillaShoesRemoved;

		public string PreviousVanillaShoesName;

		public string NewVanillaShoesName;

		public List<string> PreviousVanillaShoesSpecialItemCandidates = new List<string>();

		public List<string> NewVanillaShoesSpecialItemCandidates = new List<string>();

		public string NewShirtId;

		public string NewPantsId;

		public string NewSleevesId;

		public string NewShoesId;

		public string NewOutfitId;

		public string PreviousOutfitId;

		public int CountChanges()
		{
			int num = 0;
			if (ChangedHair)
			{
				num++;
			}
			if (ChangedAccessory)
			{
				num++;
			}
			if (ChangedHat)
			{
				num++;
			}
			if (ChangedShirt)
			{
				num++;
			}
			if (ChangedPants)
			{
				num++;
			}
			if (VanillaPantsChanged)
			{
				num++;
			}
			if (VanillaShirtChanged)
			{
				num++;
			}
			if (VanillaShoesChanged)
			{
				num++;
			}
			if (ChangedSleeves)
			{
				num++;
			}
			if (ChangedOutfit)
			{
				num++;
			}
			if (ChangedShoes)
			{
				num++;
			}
			return num;
		}
	}

	private sealed class FashionSenseSnapshot
	{
		public string Hair;

		public string Accessory;

		public string AccessorySecondary;

		public string AccessoryTertiary;

		public string Hat;

		public bool FashionSenseHatCoversVanilla;

		public string VanillaHat;

		public List<string> VanillaHatSpecialItemCandidates = new List<string>();

		public string VanillaPants;

		public List<string> VanillaPantsSpecialItemCandidates = new List<string>();

		public string VanillaShirt;

		public List<string> VanillaShirtSpecialItemCandidates = new List<string>();

		public string VanillaShoes;

		public List<string> VanillaShoesSpecialItemCandidates = new List<string>();

		public string Shirt;

		public string Pants;

		public string Sleeves;

		public string Shoes;

		public string OutfitId;

		public string HairColor;

		public string AccessoryColor;

		public string AccessorySecondaryColor;

		public string AccessoryTertiaryColor;

		public string HatColor;

		public string ShirtColor;

		public string PantsColor;

		public string SleevesColor;

		public string ShoesColor;
	}

	private sealed class SpecialItemNoticeInfo
	{
		public string EntryId { get; set; } = "";

		public string DisplayName { get; set; } = "";

		public string ItemType { get; set; } = "";

		public string MatchedName { get; set; } = "";

		public string ReactionContext { get; set; } = "";

		public bool WasRemoved { get; set; }

		public string MemoryHint { get; set; } = "";

		public bool HasSecret { get; set; }

		public string SecretId { get; set; } = "";

		public bool NpcKnowsSecret { get; set; }

		public bool IsValid => !string.IsNullOrWhiteSpace(EntryId) && !string.IsNullOrWhiteSpace(ReactionContext);
	}

	private readonly Random random = new Random();

	private Harmony harmony;

	private IFashionSenseApi fsApi;

	private TileMarkerVisionIntegration tileMarkerVisionIntegration;

	private OtherNpcClothesReactionSystem otherNpcClothesReactionSystem;

	private LewisShortsChaseController lewisShortsChaseController;

	private OutfitAiService outfitAiService;

	private OutfitMemoryService outfitMemoryService;

	private HatMemoryService hatMemoryService;

	private string lastKnownVanillaHatId;

	private List<string> lastKnownVanillaHatSpecialItemCandidates = new List<string>();

	private string lastKnownVanillaPantsName;

	private List<string> lastKnownVanillaPantsSpecialItemCandidates = new List<string>();

	private string lastKnownVanillaShirtName;

	private List<string> lastKnownVanillaShirtSpecialItemCandidates = new List<string>();

	private string lastKnownVanillaShoesName;

	private List<string> lastKnownVanillaShoesSpecialItemCandidates = new List<string>();

	private readonly HashSet<string> loggedSpecialItemDebugKeys = new HashSet<string>(StringComparer.Ordinal);

	private bool vanillaClothingTrackingInitialized;

	private int vanillaClothingPollTimer;

	private const int DayStartFreeRoamConfirmationTicks = 60;

	private bool waitingForDayStartFreeRoam;

	private int dayStartFreeRoamTicks;

	private OutfitVisionService outfitVisionService;

	private FashionSenseVisualService fashionSenseVisualService;

	private SpecialHatReactionService specialHatReactionService;

	private VanillaShirtContextService vanillaShirtContextService;

	private SpecialItemReactionService specialItemReactionService;

	private bool changedClothes = false;

	private string lastEligibleSavedOutfitId = "";

	internal const string ReactionActiveModDataKey = "NatrollEXE.OutfitReactions/ReactionActive";
	internal const string RomanticApproachActiveModDataKey = "NatrollEXE.OutfitReactions/RomanticApproachActive";

	private const string AutoKissClickActiveModDataKey = "NatrollEXE.LotsOfKisses/AutoKissClickActive";
	internal const string PublicMultiKissInterruptionModDataKey = "NatrollEXE.LotsOfKisses/PublicMultiKissInterruption";

	private FashionSenseSnapshot fsSnapshotBefore = null;

	private bool fashionSenseMenuOpen = false;

	private FashionSenseChangeInfo lastFashionSenseChangeInfo = null;

	private readonly HashSet<string> npcsReactedToCurrentNotice = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

	private readonly HashSet<string> npcsReactedToPreviousNotice = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

	private readonly AiGenerationCoordinator aiGenerationCoordinator = new AiGenerationCoordinator();

	private readonly OutfitReplyConversationHistory outfitReplyConversationHistory = new OutfitReplyConversationHistory();

	private const string AssetPrefix = "Mods/NatrollEXE.OutfitReactions/Clothes";

	private const string OutfitNoticeModDataPrefix = "NatrollEXE.OutfitReactions.OutfitNotice.";

	private const string PlayerAccessoryDescriptionModDataPrefix = "NatrollEXE.OutfitReactions.PlayerAccessoryDescription.";
	private const string PlayerSelfDescriptionModDataKey = "NatrollEXE.OutfitReactions/PlayerSelfDescription";

	private const string LotsOfKissesBystanderWatchingModDataKey = "NatrollEXE.LotsOfKisses/BystanderWatching";

	private readonly SpouseOutfitReactionProgressState spouseOutfitReactionProgressState = new SpouseOutfitReactionProgressState();

	private readonly SpouseDialogueController spouseDialogueController = new SpouseDialogueController();

	private SpouseOutfitReactionCoordinator spouseOutfitReactionCoordinator;

	private readonly SpouseRouteController spouseRouteController = new SpouseRouteController();

	private readonly SpouseOutfitApproachController spouseOutfitApproachController = new SpouseOutfitApproachController();

	private readonly SpouseOutfitNoticeController spouseOutfitNoticeController = new SpouseOutfitNoticeController();

	private readonly SpouseProximityState spouseProximityState = new SpouseProximityState();

	private readonly SpouseSpecialActionController spouseSpecialActionController = new SpouseSpecialActionController();

	private const float OutfitSpecialActionRestoreDistance = 300f;

	private const float SpouseOutfitNoticePauseDistance = 96f;

	private const float SpouseOutfitNoticeReleaseDistance = 300f;

	private const string PantsSeenModDataPrefix = "NatrollEXE.OutfitReactions/PantsSeen/";

	private const string SpecialItemSeenModDataPrefix = "NatrollEXE.OutfitReactions/SpecialItemSeen/";

	internal static ModEntry Instance { get; private set; }

	internal ModConfig Config { get; set; } = new ModConfig();

	internal static bool DebugLog => Instance?.Config?.EnableDebugLogging == true;

	private bool isReactingToClothes
	{
		get
		{
			return spouseOutfitReactionProgressState.IsReacting;
		}
		set
		{
			spouseOutfitReactionProgressState.IsReacting = value;
		}
	}

	private int clothesInteractionCooldown
	{
		get
		{
			return spouseOutfitReactionProgressState.InteractionCooldown;
		}
		set
		{
			spouseOutfitReactionProgressState.InteractionCooldown = value;
		}
	}

	private bool clothesPathStarted
	{
		get
		{
			return spouseOutfitReactionProgressState.PathStarted;
		}
		set
		{
			spouseOutfitReactionProgressState.PathStarted = value;
		}
	}

	private bool clothesComplimentReady
	{
		get
		{
			return spouseOutfitReactionProgressState.ComplimentReady;
		}
		set
		{
			spouseOutfitReactionProgressState.ComplimentReady = value;
		}
	}

	private Point clothesPreferredOffset
	{
		get
		{
			//IL_0006: Unknown result type (might be due to invalid IL or missing references)
			return spouseOutfitReactionProgressState.PreferredOffset;
		}
		set
		{
			//IL_0006: Unknown result type (might be due to invalid IL or missing references)
			spouseOutfitReactionProgressState.PreferredOffset = value;
		}
	}

	private Point clothesLastPlayerTile
	{
		get
		{
			//IL_0006: Unknown result type (might be due to invalid IL or missing references)
			return spouseOutfitReactionProgressState.LastPlayerTile;
		}
		set
		{
			//IL_0006: Unknown result type (might be due to invalid IL or missing references)
			spouseOutfitReactionProgressState.LastPlayerTile = value;
		}
	}

	private Point clothesLastTargetTile
	{
		get
		{
			//IL_0006: Unknown result type (might be due to invalid IL or missing references)
			return spouseOutfitReactionProgressState.LastTargetTile;
		}
		set
		{
			//IL_0006: Unknown result type (might be due to invalid IL or missing references)
			spouseOutfitReactionProgressState.LastTargetTile = value;
		}
	}

	private bool clothesFirstNoticeDone
	{
		get
		{
			return spouseOutfitReactionProgressState.FirstNoticeDone;
		}
		set
		{
			spouseOutfitReactionProgressState.FirstNoticeDone = value;
		}
	}

	private bool clothesEmoteFired
	{
		get
		{
			return spouseOutfitReactionProgressState.EmoteFired;
		}
		set
		{
			spouseOutfitReactionProgressState.EmoteFired = value;
		}
	}

	private int clothesNoticePauseTimer
	{
		get
		{
			return spouseOutfitReactionProgressState.NoticePauseTimer;
		}
		set
		{
			spouseOutfitReactionProgressState.NoticePauseTimer = value;
		}
	}

	private bool playerWasInClothesNoticeRange
	{
		get
		{
			return spouseOutfitReactionProgressState.PlayerWasInNoticeRange;
		}
		set
		{
			spouseOutfitReactionProgressState.PlayerWasInNoticeRange = value;
		}
	}

	private int clothesSecondNoticeCooldown
	{
		get
		{
			return spouseOutfitReactionProgressState.SecondNoticeCooldown;
		}
		set
		{
			spouseOutfitReactionProgressState.SecondNoticeCooldown = value;
		}
	}

	private int clothesChaseTimer
	{
		get
		{
			return spouseOutfitReactionProgressState.ChaseTimer;
		}
		set
		{
			spouseOutfitReactionProgressState.ChaseTimer = value;
		}
	}

	private NPC clothesReactingNpc
	{
		get
		{
			return spouseOutfitReactionProgressState.ReactingNpc;
		}
		set
		{
			spouseOutfitReactionProgressState.ReactingNpc = value;
		}
	}

	private bool outfitSequenceActive
	{
		get
		{
			return spouseOutfitReactionProgressState.SequenceActive;
		}
		set
		{
			spouseOutfitReactionProgressState.SequenceActive = value;
		}
	}

	private SpouseOutfitReactionCoordinator SpouseOutfitReactionCoordinator => spouseOutfitReactionCoordinator ?? (spouseOutfitReactionCoordinator = new SpouseOutfitReactionCoordinator(spouseOutfitReactionProgressState, UpdateClothesReactionSystem, ShouldStartClothesReaction, ResetClothesState, UpdateSpousePostOutfitLinger, TryHandleOutfitDialogueOrBlockNpcInteractionCore));


	internal bool IsAnyOutfitReactionActive()
	{
		if (isReactingToClothes || outfitSequenceActive)
		{
			return true;
		}
		if (clothesComplimentReady || clothesPathStarted)
		{
			return true;
		}
		OtherNpcClothesReactionSystem obj = otherNpcClothesReactionSystem;
		if (obj != null && obj.HasAnyActivePendingReaction())
		{
			return true;
		}
		if (lewisShortsChaseController?.IsActive == true)
		{
			return true;
		}
		return false;
	}

	private void UpdateReactionActiveModDataFlag()
	{
		if (Game1.player != null)
		{
			bool flag = IsAnyOutfitReactionActive();
			bool flag2 = ((NetDictionary<string, string, NetString, SerializableDictionary<string, string>, NetStringDictionary<string, NetString>>)(object)((Character)Game1.player).modData).ContainsKey("NatrollEXE.OutfitReactions/ReactionActive");
			if (flag && !flag2)
			{
				((NetDictionary<string, string, NetString, SerializableDictionary<string, string>, NetStringDictionary<string, NetString>>)(object)((Character)Game1.player).modData)["NatrollEXE.OutfitReactions/ReactionActive"] = "1";
			}
			else if (!flag && flag2)
			{
				((NetDictionary<string, string, NetString, SerializableDictionary<string, string>, NetStringDictionary<string, NetString>>)(object)((Character)Game1.player).modData).Remove("NatrollEXE.OutfitReactions/ReactionActive");
			}
		}
	}

	internal void QueueAiConnectionTestFromConfigMenu()
	{
		outfitAiService?.QueueConnectionTestFromConfigMenu();
	}

	public override void Entry(IModHelper helper)
	{
		Instance = this;
		Config = helper.ReadConfig<ModConfig>();
		Config.MigrateLegacyAiSettings();
		outfitAiService = new OutfitAiService(helper, ((Mod)this).Monitor, () => Config);
		outfitAiService.IsRomanceableNpc = IsNpcRomanceable;
		outfitMemoryService = new OutfitMemoryService(helper, ((Mod)this).Monitor);
		hatMemoryService = new HatMemoryService(helper, ((Mod)this).Monitor);
		outfitVisionService = new OutfitVisionService(((Mod)this).Monitor, helper.DirectoryPath, () => DebugLog);
		fashionSenseVisualService = new FashionSenseVisualService(((Mod)this).Monitor, () => fsApi);
		tileMarkerVisionIntegration = new TileMarkerVisionIntegration(helper, ((Mod)this).Monitor, ((Mod)this).ModManifest);
		specialHatReactionService = new SpecialHatReactionService(helper, ((Mod)this).Monitor);
		vanillaShirtContextService = new VanillaShirtContextService(((Mod)this).Monitor);
		specialItemReactionService = new SpecialItemReactionService(helper, ((Mod)this).Monitor);
		lewisShortsChaseController = new LewisShortsChaseController(((Mod)this).Monitor, helper.Translation, () => Config.EnableLewisShortsChase, GetEquippedLewisShortsSlot, ConfiscateEquippedLewisShorts, GetLewisShortsChaseDialogueKey, MarkCurrentOutfitAsNoticed);
		otherNpcClothesReactionSystem = new OtherNpcClothesReactionSystem(((Mod)this).Monitor, () => Config, TryQueueOtherNpcOutfitDialogue, RefreshOtherNpcOutfitPrompt, ClearOutfitPrompt, HasNoticeableCurrentFashionSenseAppearance, CanNpcNoticeCurrentOutfitNotice, MarkCurrentOutfitAsNoticed, CanNpcReactToCurrentOutfitNotice, HasNpcSeenCurrentVisualBefore, IsRomanticOutfitPartner, () => IsActiveFestivalEventForOutfitReaction() || ShouldDeferAutomaticOutfitReaction(logDecision: false), (location, x, y) => tileMarkerVisionIntegration?.IsVisionIgnoredTile(location, x, y) == true, (npc, pending) => lewisShortsChaseController?.TryBeginAfterReaction(npc, pending) == true);
		helper.Events.GameLoop.GameLaunched += OnGameLaunched;
		helper.Events.GameLoop.SaveLoaded += OnSaveLoaded;
		helper.Events.GameLoop.DayStarted += OnDayStarted;
		helper.Events.GameLoop.ReturnedToTitle += OnReturnedToTitle;
		helper.Events.GameLoop.UpdateTicked += OnUpdateTicked;
		helper.Events.Display.MenuChanged += OnMenuChanged;
		helper.Events.Display.RenderedHud += OnRenderedHud;
		helper.Events.Input.ButtonPressed += OnButtonPressed;
		helper.Events.Player.Warped += OnWarped;
		helper.Events.Content.AssetRequested += OnAssetRequested;
		helper.Events.Content.AssetsInvalidated += OnAssetsInvalidated;
		helper.Events.GameLoop.Saving += OnSaving;
		helper.ConsoleCommands.Add("oc_debug_notice", "Outfit Compliments: print why the current outfit notice can/can't start for nearby NPCs.", (Action<string, string[]>)DebugOutfitNoticeCommand);
		helper.ConsoleCommands.Add("oc_clear_notice_memory", "Outfit Compliments: clear this save's outfit notice memory and reset pending notice state.", (Action<string, string[]>)ClearOutfitNoticeMemoryCommand);
		helper.ConsoleCommands.Add("oc_test_voicesamples", "Outfit Reactions: report how many real in-game voice-sample lines each NPC profile has (run after loading a save).", (Action<string, string[]>)VoiceSampleReportCommand);
		helper.ConsoleCommands.Add("oc_preview_voicesamples", "Outfit Reactions: show the exact voice-sample lines that would be injected into the prompt for ONE NPC. Usage: oc_preview_voicesamples <NpcName>", (Action<string, string[]>)VoiceSamplePreviewCommand);
		ApplyHarmonyPatches();
	}

	private void ApplyHarmonyPatches()
	{
		try
		{
			MethodBase methodBase = AccessTools.Method(typeof(NPC), "checkAction", new Type[2]
			{
				typeof(Farmer),
				typeof(GameLocation)
			});
			if (methodBase == null)
			{
				((Mod)this).Monitor.Log("[CLOTHES PRIORITY] NPC.checkAction target method was NOT found. Patch was not applied.", (LogLevel)3);
				return;
			}
			harmony = new Harmony(((Mod)this).ModManifest.UniqueID);
			harmony.PatchAll(typeof(ModEntry).Assembly);
			if (DebugLog)
			{
				((Mod)this).Monitor.Log("[CLOTHES PRIORITY] NPC.checkAction Harmony patch applied.", (LogLevel)2);
			}
		}
		catch (Exception ex)
		{
			((Mod)this).Monitor.Log("[CLOTHES PRIORITY] Failed to apply NPC.checkAction patch: " + ex, (LogLevel)3);
		}
	}

	internal bool PrioritizeOutfitDialogueBeforeNpcCheckAction(NPC npc)
	{
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
		return otherNpcClothesReactionSystem?.TryPrioritizePendingDialogueForClick(npc) ?? false;
	}

	internal bool TryHandleOutfitDialogueOrBlockNpcInteraction(NPC npc)
	{
		if (Config?.Enabled != true)
		{
			return false;
		}

		if (Game1.player?.modData != null && Game1.player.modData.ContainsKey(AutoKissClickActiveModDataKey))
		{
			// Lots of Kisses marks its simulated checkAction click with this flag.
			// Let the kiss pass through without starting/consuming the pending outfit dialogue.
			otherNpcClothesReactionSystem?.SuspendRomanticHoldForExternalKiss(npc);
			return false;
		}
		if (ShouldDeferAutomaticOutfitReaction())
			return false;
		return otherNpcClothesReactionSystem?.TryPrioritizePendingDialogueForClick(npc) ?? false;
	}

	internal void SetModEnabled(bool enabled)
	{
		if (Config == null || Config.Enabled == enabled)
		{
			return;
		}

		Config.Enabled = enabled;
		if (enabled)
		{
			if (Context.IsWorldReady)
			{
				BeginDayStartReactionGate();
			}
			return;
		}

		waitingForDayStartFreeRoam = false;
		dayStartFreeRoamTicks = 0;
		ResetClothesState(clearChangeFlag: true);
		lewisShortsChaseController?.Reset(restoreIfPossible: true);
		otherNpcClothesReactionSystem?.Reset();
		Game1.player?.modData?.Remove(ReactionActiveModDataKey);
	}

	private static bool IsActiveFestivalEventForOutfitReaction()
	{
		if (!Game1.eventUp || Game1.CurrentEvent == null)
			return false;

		if (Game1.CurrentEvent.isFestival)
			return true;

		string eventId = Game1.CurrentEvent.id ?? "";
		return eventId.StartsWith("festival_", StringComparison.OrdinalIgnoreCase);
	}

	private bool ShouldDeferAutomaticOutfitReaction(bool logDecision = true)
	{
		// DayStarted can run a few update ticks before a spouse morning event takes ownership
		// of the location. Keep both automatic discovery and manual click interception disabled
		// until the game has remained in normal free-roam control for a short, stable window.
		if (waitingForDayStartFreeRoam)
		{
			if (DebugLog && logDecision)
				((Mod)this).Monitor.Log("[NPC OUTFIT] Deferred outfit reaction while waiting for stable free-roam control after day start.", (LogLevel)2);
			return true;
		}

		// Some modded morning events populate CurrentEvent one or more ticks before (or without)
		// setting eventUp. Treat either signal as an active scripted event so a click can't slip
		// through that transition and arm an outfit reaction inside the scene.
		if (!Game1.eventUp && Game1.CurrentEvent == null)
			return false;

		// Ordinary scripted events own their actors, dialogue, and input. Outfit reactions are
		// allowed during free-roam festivals through the specialized festival path below, but
		// must never arm or capture clicks during cutscenes, spouse morning events, or load events.
		if (!IsActiveFestivalEventForOutfitReaction())
		{
			if (DebugLog && logDecision)
				((Mod)this).Monitor.Log("[NPC OUTFIT] Deferred outfit reaction because a scripted non-festival event is active.", (LogLevel)2);
			return true;
		}

		if (Game1.currentMinigame != null)
		{
			if (DebugLog && logDecision)
				((Mod)this).Monitor.Log("[NPC OUTFIT] Deferred pending festival outfit reaction because a minigame is active.", (LogLevel)2);
			return true;
		}

		object currentEvent = Game1.CurrentEvent;
		if (currentEvent == null)
			return false;

		try
		{
			Type eventType = currentEvent.GetType();
			string[] timerNames = new string[] { "festivalTimer", "FestivalTimer", "competitionTimer", "CompetitionTimer", "contestTimer", "ContestTimer", "eggHuntTimer", "EggHuntTimer" };
			foreach (string timerName in timerNames)
			{
				object value = eventType.GetProperty(timerName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)?.GetValue(currentEvent)
					?? eventType.GetField(timerName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)?.GetValue(currentEvent);
				if ((value is int intTimer && intTimer > 0) || (value is float floatTimer && floatTimer > 0f) || (value is double doubleTimer && doubleTimer > 0d))
				{
					if (DebugLog && logDecision)
						((Mod)this).Monitor.Log("[NPC OUTFIT] Deferred pending festival outfit reaction because a festival competition timer is active.", (LogLevel)2);
					return true;
				}
			}
		}
		catch (Exception ex)
		{
			if (DebugLog && logDecision)
				((Mod)this).Monitor.Log("[NPC OUTFIT] Could not inspect festival activity timer safely: " + ex.Message, (LogLevel)1);
		}

		return false;
	}

	private bool TryHandleOutfitDialogueOrBlockNpcInteractionCore(NPC npc)
	{
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
		if (((Character)Game1.player).modData != null && ((NetDictionary<string, string, NetString, SerializableDictionary<string, string>, NetStringDictionary<string, NetString>>)(object)((Character)Game1.player).modData).ContainsKey("NatrollEXE.LotsOfKisses/AutoKissClickActive"))
		{
			return false;
		}
		if (TryOpenPrioritizedOutfitDialogueFromCheckAction(npc))
		{
			return true;
		}
		if (ShouldBlockNpcInteractionUntilOutfitDialogueRead(npc))
		{
			ShowPendingOutfitBlockedInteractionFeedback(npc);
			if (DebugLog)
			{
				((Mod)this).Monitor.Log("[CLOTHES PRIORITY] Blocked normal interaction/kiss with " + ((Character)npc).Name + " because an unread outfit dialogue is pending.", (LogLevel)2);
			}
			return true;
		}
		return false;
	}

	private void OnGameLaunched(object sender, GameLaunchedEventArgs e)
	{
		fsApi = ((Mod)this).Helper.ModRegistry.GetApi<IFashionSenseApi>("PeacefulEnd.FashionSense");
		tileMarkerVisionIntegration?.Initialize();
		((Mod)this).Monitor.Log((fsApi != null) ? "Fashion Sense API loaded successfully." : "Fashion Sense API not found. Outfit compliments will not detect clothing changes.", (LogLevel)((fsApi != null) ? 1 : 3));
		outfitAiService?.LoadProfiles();
		try
		{
			IGenericModConfigMenuApi api = ((Mod)this).Helper.ModRegistry.GetApi<IGenericModConfigMenuApi>("spacechase0.GenericModConfigMenu");
			ModConfigMenu.Register(this, api);
		}
		catch (Exception ex)
		{
			((Mod)this).Monitor.Log("Failed to register GMCM options: " + ex.Message, (LogLevel)0);
		}
	}

	private void OnSaving(object sender, SavingEventArgs e)
	{
		outfitMemoryService?.Save();
		hatMemoryService?.Save();
	}

	private void OnSaveLoaded(object sender, SaveLoadedEventArgs e)
	{
		BeginDayStartReactionGate();
		outfitMemoryService?.Load();
		hatMemoryService?.Load();
		specialItemReactionService?.ResetModRegistryCache();
		vanillaClothingTrackingInitialized = false;
		lastKnownVanillaHatId = null;
		lastKnownVanillaPantsName = null;
		lastKnownVanillaShirtName = null;
		lastKnownVanillaShoesName = null;
		ResetClothesState(clearChangeFlag: true);
		lewisShortsChaseController?.Reset(restoreIfPossible: true);
		otherNpcClothesReactionSystem?.Reset();
		outfitAiService?.LoadProfiles(quiet: true);
		RearmCurrentAppearanceNoticeAfterLifecycleReset("loading the save");
	}

	private void OnDayStarted(object sender, DayStartedEventArgs e)
	{
		BeginDayStartReactionGate();
		CancelAllPendingOwnAiGenerations();
		ResetClothesState(clearChangeFlag: true);
		npcsReactedToCurrentNotice.Clear();
		npcsReactedToPreviousNotice.Clear();
		lewisShortsChaseController?.Reset(restoreIfPossible: true);
		otherNpcClothesReactionSystem?.Reset();
		RearmCurrentAppearanceNoticeAfterLifecycleReset("starting a new day");
		Farmer player = Game1.player;
		if (player != null)
		{
			((NetDictionary<string, string, NetString, SerializableDictionary<string, string>, NetStringDictionary<string, NetString>>)(object)((Character)player).modData)?.Remove("NatrollEXE.OutfitReactions/ReactionActive");
		}
		outfitAiService?.LoadProfiles(quiet: true);
	}

	private void OnReturnedToTitle(object sender, ReturnedToTitleEventArgs e)
	{
		waitingForDayStartFreeRoam = false;
		dayStartFreeRoamTicks = 0;
		CancelAllPendingOwnAiGenerations();
		ResetClothesState(clearChangeFlag: true);
		npcsReactedToCurrentNotice.Clear();
		npcsReactedToPreviousNotice.Clear();
		lewisShortsChaseController?.Reset(restoreIfPossible: false);
		otherNpcClothesReactionSystem?.Reset();
	}

	private void OnRenderedHud(object sender, RenderedHudEventArgs e)
	{
		if (!Context.IsWorldReady || Game1.player == null || !Config.Enabled)
		{
			return;
		}
		foreach (PendingAiGeneration item in aiGenerationCoordinator.GetOutfitSnapshot())
		{
			if (item != null && item.Task != null && !item.Task.IsCompleted)
			{
				NPC characterFromName = NpcContextResolver.Resolve(item.NpcName);
				if (characterFromName != null && ((Character)characterFromName).currentLocation == ((Character)Game1.player).currentLocation)
				{
					DrawOwnAiWaitingHudMessage(e.SpriteBatch, characterFromName, GetOwnAiWaitingDialogueText(characterFromName, item.WaitingDotCount));
					return;
				}
			}
		}
		foreach (PendingAiPlayerReplyGeneration item2 in aiGenerationCoordinator.GetReplySnapshot())
		{
			if (item2 != null && item2.Task != null && !item2.Task.IsCompleted)
			{
				NPC characterFromName2 = NpcContextResolver.Resolve(item2.NpcName);
				if (characterFromName2 != null && ((Character)characterFromName2).currentLocation == ((Character)Game1.player).currentLocation)
				{
					DrawOwnAiWaitingHudMessage(e.SpriteBatch, characterFromName2, GetOwnAiReplyWaitingDialogueText(characterFromName2, item2.WaitingDotCount));
					break;
				}
			}
		}
	}

	private void OnButtonPressed(object sender, ButtonPressedEventArgs e)
	{
		if (Context.IsWorldReady && Game1.player != null && Config.Enabled
			&& Config.PlayerPersonalityMenuKey != SButton.None
			&& e.Button == Config.PlayerPersonalityMenuKey
			&& Game1.activeClickableMenu == null && !Game1.eventUp)
		{
			Helper.Input.Suppress(e.Button);
			OpenPlayerSelfDescriptionMenu();
			return;
		}
		//IL_0043: Unknown result type (might be due to invalid IL or missing references)
		//IL_0050: Unknown result type (might be due to invalid IL or missing references)
		if (Context.IsWorldReady && Game1.player != null && Game1.currentLocation != null && Config.Enabled && Game1.activeClickableMenu == null && (SButtonExtensions.IsActionButton(e.Button) || SButtonExtensions.IsUseToolButton(e.Button)))
		{
			if (ShouldDeferAutomaticOutfitReaction())
			{
				return;
			}
			NPC npcBeingInteractedWith = GetNpcBeingInteractedWith();
			if (npcBeingInteractedWith != null)
			{
				bool festivalInteraction = IsActiveFestivalEventForOutfitReaction();
				bool outfitClickConsumed = festivalInteraction
					? otherNpcClothesReactionSystem?.TryPrioritizeFestivalDialogueForClick(npcBeingInteractedWith) ?? false
					: TryPrioritizeSpouseOutfitDialogueForClick(npcBeingInteractedWith);
				if (!outfitClickConsumed && !festivalInteraction)
				{
					outfitClickConsumed = otherNpcClothesReactionSystem?.TryPrioritizePendingDialogueForClick(npcBeingInteractedWith) ?? false;
				}

				// Festival dialogue can be opened directly by the event before NPC.checkAction runs.
				// Consume only the click claimed by an outfit reaction; the festival's original
				// dialogue stack stays untouched and remains available on the next interaction.
				if (outfitClickConsumed && festivalInteraction)
				{
					((Mod)this).Helper.Input.Suppress(e.Button);
					if (DebugLog)
					{
						((Mod)this).Monitor.Log($"[FESTIVAL OUTFIT] Suppressed {e.Button} for {((Character)npcBeingInteractedWith).Name} so the outfit reaction opens before the festival dialogue.", (LogLevel)2);
					}
				}
			}
			else if (DebugLog && IsActiveFestivalEventForOutfitReaction())
			{
				((Mod)this).Monitor.Log("[FESTIVAL OUTFIT] Action click did not resolve a festival NPC at the targeted tile.", (LogLevel)2);
			}
		}
	}

	private NPC GetNpcBeingInteractedWith()
	{
		//IL_002a: Unknown result type (might be due to invalid IL or missing references)
		//IL_002f: Unknown result type (might be due to invalid IL or missing references)
		if (Game1.player == null || Game1.currentLocation == null)
		{
			return null;
		}
		List<NPC> interactionCandidates = ((IEnumerable)Game1.currentLocation.characters).OfType<NPC>().ToList();
		if (IsActiveFestivalEventForOutfitReaction() && Game1.CurrentEvent?.actors != null)
		{
			foreach (NPC actor in Game1.CurrentEvent.actors)
			{
				if (actor != null && !interactionCandidates.Contains(actor))
				{
					interactionCandidates.Add(actor);
				}
			}
		}
		Vector2 grabTile = ((Character)Game1.player).GetGrabTile();
		NPC val = interactionCandidates.FirstOrDefault((NPC c) => c != null && !c.IsInvisible && ((Character)c).TilePoint.X == (int)grabTile.X && ((Character)c).TilePoint.Y == (int)grabTile.Y);
		if (val != null)
		{
			return val;
		}
		int mouseTileX = (Game1.getOldMouseX() + Game1.viewport.X) / 64;
		int mouseTileY = (Game1.getOldMouseY() + Game1.viewport.Y) / 64;
		val = (from c in interactionCandidates
			where c != null && !c.IsInvisible && ((Character)c).TilePoint.X == mouseTileX && ((Character)c).TilePoint.Y == mouseTileY
			orderby Vector2.Distance(((Character)c).Position, ((Character)Game1.player).Position)
			select c).FirstOrDefault((NPC c) => Vector2.Distance(((Character)c).Position, ((Character)Game1.player).Position) <= 192f);
		if (val != null)
		{
			return val;
		}
		return (from c in interactionCandidates
			where c != null && !c.IsInvisible && ((Character)c).currentLocation == ((Character)Game1.player).currentLocation
			where Vector2.Distance(((Character)c).Position, ((Character)Game1.player).Position) <= 112f
			orderby Vector2.Distance(((Character)c).Position, ((Character)Game1.player).Position)
			select c).FirstOrDefault();
	}

	private bool TryPrioritizeSpouseOutfitDialogueForClick(NPC npc)
	{
		if (npc == null || clothesReactingNpc == null)
		{
			return false;
		}
		if (!((Character)npc).Name.Equals(((Character)clothesReactingNpc).Name, StringComparison.OrdinalIgnoreCase))
		{
			return false;
		}
		if (!clothesComplimentReady || !outfitSequenceActive)
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
		if (!spouseDialogueController.HasBackup)
		{
			spouseDialogueController.Capture(npc, Game1.player, ((Mod)this).Monitor, DebugLog);
		}
		else
		{
			spouseDialogueController.TemporarilySkipFirstDailyDialogue(npc, Game1.player, ((Mod)this).Monitor, DebugLog);
		}
		bool flag = QueueSpouseOutfitDialogueOnly(npc);
		if (flag)
		{
			if (DebugLog)
			{
				((Mod)this).Monitor.Log("[CLOTHES SPOUSE] Re-prioritized outfit dialogue for " + ((Character)npc).Name + " at click time.", (LogLevel)2);
			}
		}
		else
		{
			KeepSpouseOutfitNoticePendingAfterAiFailure(npc, "AI queue was not available on click.");
		}
		return flag;
	}

	private void OnWarped(object sender, WarpedEventArgs e)
	{
		if (Context.IsWorldReady && e != null && e.IsLocalPlayer)
		{
			CancelAllPendingOwnAiGenerations();
			if (isReactingToClothes || outfitSequenceActive)
			{
				ResetClothesReactionState();
			}
			otherNpcClothesReactionSystem?.Reset();
		}
	}

	private void OnAssetRequested(object sender, AssetRequestedEventArgs e)
	{
		if (e.NameWithoutLocale.IsEquivalentTo("Mods/NatrollEXE.OutfitReactions/NpcCharacteristics", false))
		{
			e.LoadFrom((Func<object>)(() => outfitAiService?.LoadDefaultProfilesFromFiles() ?? new Dictionary<string, CharacterAiProfile>(StringComparer.OrdinalIgnoreCase)), (AssetLoadPriority)(-1000), (string)null);
		}
	}

	private void OnAssetsInvalidated(object sender, AssetsInvalidatedEventArgs e)
	{
		if (!Context.IsWorldReady)
		{
			return;
		}
		foreach (IAssetName item in e.NamesWithoutLocale)
		{
			string text = ((object)item).ToString();
			if (!text.StartsWith("Mods/NatrollEXE.OutfitReactions/Clothes", StringComparison.OrdinalIgnoreCase) && text.Equals("Mods/NatrollEXE.OutfitReactions/NpcCharacteristics", StringComparison.OrdinalIgnoreCase))
			{
				outfitAiService?.LoadProfiles(quiet: true);
			}
		}
	}

	private void OnMenuChanged(object sender, MenuChangedEventArgs e)
	{
		if (!Context.IsWorldReady || Game1.player == null || !Config.Enabled)
		{
			return;
		}
		bool flag = e.NewMenu != null && IsFashionSenseMenu(e.NewMenu);
		bool flag2 = e.OldMenu != null && IsFashionSenseMenu(e.OldMenu);
		if (flag && !fashionSenseMenuOpen)
		{
			fashionSenseMenuOpen = true;
			fsSnapshotBefore = CaptureFashionSenseSnapshot();
		}
		else
		{
			if ((fashionSenseMenuOpen && flag) || !(fashionSenseMenuOpen && flag2) || e.NewMenu != null)
			{
				return;
			}
			fashionSenseMenuOpen = false;
			DelayedAction.functionAfterDelay((Action)delegate
			{
				if (Context.IsWorldReady && Game1.player != null)
				{
					FashionSenseSnapshot fashionSenseSnapshot = CaptureFashionSenseSnapshot();
					FashionSenseChangeInfo fashionSenseChangeInfo = CompareFashionSenseSnapshots(fsSnapshotBefore, fashionSenseSnapshot);
					fsSnapshotBefore = null;
					lastKnownVanillaHatId = fashionSenseSnapshot?.VanillaHat ?? "";
					lastKnownVanillaPantsName = fashionSenseSnapshot?.VanillaPants ?? "";
					lastKnownVanillaPantsSpecialItemCandidates = CloneSpecialItemCandidates(fashionSenseSnapshot?.VanillaPantsSpecialItemCandidates);
					vanillaClothingTrackingInitialized = true;
					if (fashionSenseChangeInfo != null && fashionSenseChangeInfo.CountChanges() > 0)
					{
						ApplyDetectedClothesChange(fashionSenseChangeInfo);
					}
				}
			}, 500);
		}
	}
}
