using System;
using OutfitReactions.Ai.Providers;
using StardewModdingAPI;

namespace OutfitReactions
{
    public sealed class ModConfig
    {
        private const string DefaultGeminiModel = "gemini-3.1-flash-lite";

        public bool Enabled { get; set; } = true;
        public SButton PlayerPersonalityMenuKey { get; set; } = SButton.None;

        // --- Voice samples (MVP) ---------------------------------------------
        // When enabled, the mod reads a few of the NPC's REAL in-game dialogue lines
        // (base game and whatever a dialogue mod like Maggs' has replaced them with,
        // since those are what the game currently has loaded) and injects them into the
        // prompt purely as a VOICE/TONE reference, to make reactions sound more like that
        // specific character. The lines are labelled "reference only, do not copy", and the
        // NPC profile always stays the strongest authority above them.
        public bool UseVoiceSamples { get; set; } = true;

        // When enabled, the readable version of Fashion Sense internal IDs (outfit names,
        // accessory/hat change IDs) is sent to the AI as a theme/reference clue.
        // This helps when IDs are descriptive (e.g. "CuteFairyOutfit", "PikachuEars"),
        // but may confuse the AI when IDs are generic, numeric, or technical (e.g. "Set04").
        public bool UseFsInternalIdAsHint { get; set; } = true;

        // How many sample lines to inject (kept small so the prompt stays lean).
        public int VoiceSampleCount { get; set; } = 6;

        // Optional comma-separated list of NPC names to EXCLUDE from voice samples
        // (e.g. a heavily customized character you'd rather drive purely from its profile).
        // Example: "Sebastian, Abigail"
        public string VoiceSampleExcludedNpcs { get; set; } = "";
        // ---------------------------------------------------------------------


        // Built-in AI generation. Outfit Compliments currently uses AI-generated dialogue only.
        // Manual Content Patcher/JSON outfit dialogue fallback is disabled in this AI-only build.

        // Active provider. The provider-specific fields below stay saved separately,
        // so you can switch providers in GMCM without retyping keys/models/endpoints.
        public string AiProvider { get; set; } = "Gemini"; // OpenAI, DeepSeek, Gemini, OpenRouter, Local, Mistral, Groq, Together, Anthropic, xAI, Cerebras

        // Legacy fields kept for older config.json files. MigrateLegacyAiSettings() copies
        // them into the provider-specific fields when possible.
        public string AiModel { get; set; } = "";
        public string AiApiKey { get; set; } = "";
        public string AiCustomEndpoint { get; set; } = "";
        public int AiTemperaturePercent { get; set; } = 75;
        public int AiTimeoutSeconds { get; set; } = 60;
        public int AiMaxCharacters { get; set; } = 120;

        // Five reusable credential slots shown in GMCM. Each slot has its own provider, an
        // enabled checkbox, model, API key and (optional) endpoint. The mod uses the ENABLED
        // slot whose provider matches the active AiProvider. The old auto-guess (SlotMatches-
        // Provider) is kept only as a fallback when no enabled slot explicitly matches.
        public string AiModelSlot1 { get; set; } = DefaultGeminiModel;
        public string AiModelSlot2 { get; set; } = "";
        public string AiModelSlot3 { get; set; } = "";
        public string AiModelSlot4 { get; set; } = "";
        public string AiModelSlot5 { get; set; } = "";
        public string AiApiKeySlot1 { get; set; } = "";
        public string AiApiKeySlot2 { get; set; } = "";
        public string AiApiKeySlot3 { get; set; } = "";
        public string AiApiKeySlot4 { get; set; } = "";
        public string AiApiKeySlot5 { get; set; } = "";
        public string AiCustomEndpointSlot1 { get; set; } = "";
        public string AiCustomEndpointSlot2 { get; set; } = "";
        public string AiCustomEndpointSlot3 { get; set; } = "";
        public string AiCustomEndpointSlot4 { get; set; } = "";
        public string AiCustomEndpointSlot5 { get; set; } = "";
        // Each slot's chosen provider (DeepSeek/Gemini/OpenAI/OpenRouter/Local/Mistral/Groq/Together/Anthropic/xAI/Cerebras).
        public string AiProviderSlot1 { get; set; } = "Gemini";
        public string AiProviderSlot2 { get; set; } = "OpenAI";
        public string AiProviderSlot3 { get; set; } = "OpenRouter";
        public string AiProviderSlot4 { get; set; } = "Groq";
        public string AiProviderSlot5 { get; set; } = "Mistral";
        // Whether each slot is enabled/available for use.
        public bool AiSlot1Enabled { get; set; } = true;
        public bool AiSlot2Enabled { get; set; } = false;
        public bool AiSlot3Enabled { get; set; } = false;
        public bool AiSlot4Enabled { get; set; } = false;
        public bool AiSlot5Enabled { get; set; } = false;
        // Vision mode per slot: "Auto" (detect from model name), "On" (force send image),
        // "Off" (never send image). Auto is the safe default.
        public string AiVisionModeSlot1 { get; set; } = "Auto";
        public string AiVisionModeSlot2 { get; set; } = "Auto";
        public string AiVisionModeSlot3 { get; set; } = "Auto";
        public string AiVisionModeSlot4 { get; set; } = "Auto";
        public string AiVisionModeSlot5 { get; set; } = "Auto";
        // Optional OpenRouter price ceilings in USD per million tokens. These stay attached
        // to each reusable profile and are ignored when that profile uses another provider.
        // Empty or zero disables the corresponding ceiling.
        public string OpenRouterMaxInputPriceSlot1 { get; set; } = "";
        public string OpenRouterMaxInputPriceSlot2 { get; set; } = "";
        public string OpenRouterMaxInputPriceSlot3 { get; set; } = "";
        public string OpenRouterMaxInputPriceSlot4 { get; set; } = "";
        public string OpenRouterMaxInputPriceSlot5 { get; set; } = "";
        public string OpenRouterMaxOutputPriceSlot1 { get; set; } = "";
        public string OpenRouterMaxOutputPriceSlot2 { get; set; } = "";
        public string OpenRouterMaxOutputPriceSlot3 { get; set; } = "";
        public string OpenRouterMaxOutputPriceSlot4 { get; set; } = "";
        public string OpenRouterMaxOutputPriceSlot5 { get; set; } = "";
        // Comma-separated OpenRouter provider allowlist per profile. Empty allows any provider.
        // New property names intentionally discard legacy ignored lists; never reinterpret a ban as permission.
        public string OpenRouterAllowedProvidersSlot1 { get; set; } = "";
        public string OpenRouterAllowedProvidersSlot2 { get; set; } = "";
        public string OpenRouterAllowedProvidersSlot3 { get; set; } = "";
        public string OpenRouterAllowedProvidersSlot4 { get; set; } = "";
        public string OpenRouterAllowedProvidersSlot5 { get; set; } = "";

        public string DeepSeekAiModel { get; set; } = "";
        public string DeepSeekAiApiKey { get; set; } = "";
        public string DeepSeekAiCustomEndpoint { get; set; } = "";
        public int DeepSeekAiTemperaturePercent { get; set; } = 75;
        public int DeepSeekAiTimeoutSeconds { get; set; } = 60;
        public int DeepSeekAiMaxCharacters { get; set; } = 120;

        public string GeminiAiModel { get; set; } = "";
        public string GeminiAiApiKey { get; set; } = "";
        public string GeminiAiCustomEndpoint { get; set; } = "";
        public int GeminiAiTemperaturePercent { get; set; } = 75;
        public int GeminiAiTimeoutSeconds { get; set; } = 60;
        public int GeminiAiMaxCharacters { get; set; } = 120;

        public string OpenAiAiModel { get; set; } = "";
        public string OpenAiAiApiKey { get; set; } = "";
        public string OpenAiAiCustomEndpoint { get; set; } = "";
        public int OpenAiAiTemperaturePercent { get; set; } = 75;
        public int OpenAiAiTimeoutSeconds { get; set; } = 60;
        public int OpenAiAiMaxCharacters { get; set; } = 120;

        public string OpenRouterAiModel { get; set; } = "";
        public string OpenRouterAiApiKey { get; set; } = "";
        public string OpenRouterAiCustomEndpoint { get; set; } = "";
        public int OpenRouterAiTemperaturePercent { get; set; } = 75;
        public int OpenRouterAiTimeoutSeconds { get; set; } = 60;
        public int OpenRouterAiMaxCharacters { get; set; } = 120;

        public string LocalAiModel { get; set; } = "";
        public string LocalAiApiKey { get; set; } = "";
        public string LocalAiCustomEndpoint { get; set; } = "";
        public int LocalAiTemperaturePercent { get; set; } = 75;
        public int LocalAiTimeoutSeconds { get; set; } = 60;
        public int LocalAiMaxCharacters { get; set; } = 120;
        public bool LocalAiSafeMode { get; set; } = true;
        public bool LocalAiConservativePortraits { get; set; } = true;

        public string MistralAiModel { get; set; } = "";
        public string MistralAiApiKey { get; set; } = "";
        public string MistralAiCustomEndpoint { get; set; } = "";
        public int MistralAiTemperaturePercent { get; set; } = 75;
        public int MistralAiTimeoutSeconds { get; set; } = 60;
        public int MistralAiMaxCharacters { get; set; } = 120;

        public string GroqAiModel { get; set; } = "";
        public string GroqAiApiKey { get; set; } = "";
        public string GroqAiCustomEndpoint { get; set; } = "";
        public int GroqAiTemperaturePercent { get; set; } = 75;
        public int GroqAiTimeoutSeconds { get; set; } = 60;
        public int GroqAiMaxCharacters { get; set; } = 120;

        public string TogetherAiModel { get; set; } = "";
        public string TogetherAiApiKey { get; set; } = "";
        public string TogetherAiCustomEndpoint { get; set; } = "";
        public int TogetherAiTemperaturePercent { get; set; } = 75;
        public int TogetherAiTimeoutSeconds { get; set; } = 60;
        public int TogetherAiMaxCharacters { get; set; } = 120;

        public string AnthropicAiModel { get; set; } = "";
        public string AnthropicAiApiKey { get; set; } = "";
        public string AnthropicAiCustomEndpoint { get; set; } = "";
        public int AnthropicAiTemperaturePercent { get; set; } = 75;
        public int AnthropicAiTimeoutSeconds { get; set; } = 60;
        public int AnthropicAiMaxCharacters { get; set; } = 120;

        public string XAiModel { get; set; } = "";
        public string XAiApiKey { get; set; } = "";
        public string XAiCustomEndpoint { get; set; } = "";
        public int XAiTemperaturePercent { get; set; } = 75;
        public int XAiTimeoutSeconds { get; set; } = 60;
        public int XAiMaxCharacters { get; set; } = 120;

        public string CerebrasAiModel { get; set; } = "";
        public string CerebrasAiApiKey { get; set; } = "";
        public string CerebrasAiCustomEndpoint { get; set; } = "";
        public int CerebrasAiTemperaturePercent { get; set; } = 75;
        public int CerebrasAiTimeoutSeconds { get; set; } = 60;
        public int CerebrasAiMaxCharacters { get; set; } = 120;

        public bool UseAiCache { get; set; } = true;
        public bool EnableVisionOutfitAnalysis { get; set; } = false;
        public bool ShowOwnAiWaitingDialogue { get; set; } = true;
        public bool EnablePlayerReplyMenuAfterOutfitCompliment { get; set; } = true;
        public bool GenerateNpcFollowUpToPlayerOutfitReply { get; set; } = true;
        public bool EnableExpressiveAsteriskActions { get; set; } = true;
        public bool EnableProfanityFilter { get; set; } = true;

        public bool IncludeFestivalContextForAi { get; set; } = true;
        public bool IncludeFarmerBirthdayContextForAi { get; set; } = true;
        public string FarmerBirthdaySeason { get; set; } = "";
        public int FarmerBirthdayDay { get; set; } = 0;
        public bool IncludeDetailedLocationContextForAi { get; set; } = true;
        public bool IncludeDayPartContextForAi { get; set; } = true;
        public bool IncludeIndoorOutdoorContextForAi { get; set; } = true;
        public bool IncludeNpcRoomContextForAi { get; set; } = true;

        public bool UseJsonFallbackForOutfitReactions { get; set; } = false; // AI-only build: manual JSON fallback disabled


        // NPCs que não são cônjuges: reações leves via IA própria.
        public bool EnableNpcOutfitReactions { get; set; } = true;
        public int NpcOutfitReactionChance { get; set; } = 30;
        public int NpcRepeatedVisualNoticeChance { get; set; } = 15;
        public int FestivalNpcOutfitReactionChance { get; set; } = 30;
        public bool RomanticPartnersAlwaysNoticeOutfitChanges { get; set; } = true;
        public bool EnableLewisShortsChase { get; set; } = true;
        public bool EnableDebugLogging { get; set; } = false;

        // How NPCs react when the farmer is wearing a vanilla hat (Game1.player.hat):
        //  - "Combined": react to the current outfit/look AND the vanilla hat together (default, original behavior).
        //  - "HatOnly": react EXCLUSIVELY to the vanilla hat, ignoring the rest of the outfit/clothes context.
        //    This can produce funnier, more focused reactions to just the hat.
        public string VanillaHatReactionMode { get; set; } = "Combined";

        // How NPCs react when the farmer is wearing a special vanilla item (e.g. Mayor's Purple Shorts):
        //  - "Combined": react to the current outfit/look AND the special item together.
        //    The NPC can comment on both the item and how it clashes or fits with the outfit.
        //  - "ItemOnly": react EXCLUSIVELY to the special item, ignoring the rest of the outfit.
        //    This produces more focused, item-centric reactions.
        public string VanillaSpecialItemReactionMode { get; set; } = "ItemOnly";

        // Unified notice/cancel distances — apply to both the spouse and all other NPCs.
        public int OutfitNoticeDistance { get; set; } = 650;
        public int OutfitCancelDistance { get; set; } = 900;

        public void MigrateLegacyAiSettings()
        {
            string provider = NormalizeProvider(AiProvider);
            AiProvider = provider;

            // Copy the old single-provider fields into the matching new provider section.
            // This avoids making you retype the current key/model after updating the mod.
            if (provider.Equals("DeepSeek", StringComparison.OrdinalIgnoreCase))
            {
                if (string.IsNullOrWhiteSpace(DeepSeekAiModel) && !string.IsNullOrWhiteSpace(AiModel))
                    DeepSeekAiModel = AiModel;
                if (string.IsNullOrWhiteSpace(DeepSeekAiApiKey) && !string.IsNullOrWhiteSpace(AiApiKey))
                    DeepSeekAiApiKey = AiApiKey;
                if (string.IsNullOrWhiteSpace(DeepSeekAiCustomEndpoint) && !string.IsNullOrWhiteSpace(AiCustomEndpoint))
                    DeepSeekAiCustomEndpoint = AiCustomEndpoint;
                if (DeepSeekAiTemperaturePercent == 75 && AiTemperaturePercent != 75)
                    DeepSeekAiTemperaturePercent = AiTemperaturePercent;
                if (DeepSeekAiTimeoutSeconds == 60 && AiTimeoutSeconds != 45)
                    DeepSeekAiTimeoutSeconds = AiTimeoutSeconds;
                if (DeepSeekAiMaxCharacters == 200 && AiMaxCharacters != 200)
                    DeepSeekAiMaxCharacters = AiMaxCharacters;
            }
            else if (provider.Equals("Gemini", StringComparison.OrdinalIgnoreCase))
            {
                if (string.IsNullOrWhiteSpace(GeminiAiModel) && !string.IsNullOrWhiteSpace(AiModel))
                    GeminiAiModel = AiModel;
                if (string.IsNullOrWhiteSpace(GeminiAiApiKey) && !string.IsNullOrWhiteSpace(AiApiKey))
                    GeminiAiApiKey = AiApiKey;
                if (string.IsNullOrWhiteSpace(GeminiAiCustomEndpoint) && !string.IsNullOrWhiteSpace(AiCustomEndpoint))
                    GeminiAiCustomEndpoint = AiCustomEndpoint;
                if (GeminiAiTemperaturePercent == 75 && AiTemperaturePercent != 75)
                    GeminiAiTemperaturePercent = AiTemperaturePercent;
                if (GeminiAiTimeoutSeconds == 60 && AiTimeoutSeconds != 45)
                    GeminiAiTimeoutSeconds = AiTimeoutSeconds;
                if (GeminiAiMaxCharacters == 200 && AiMaxCharacters != 200)
                    GeminiAiMaxCharacters = AiMaxCharacters;
            }
            else if (provider.Equals("OpenAI", StringComparison.OrdinalIgnoreCase))
            {
                if (string.IsNullOrWhiteSpace(OpenAiAiModel) && !string.IsNullOrWhiteSpace(AiModel))
                    OpenAiAiModel = AiModel;
                if (string.IsNullOrWhiteSpace(OpenAiAiApiKey) && !string.IsNullOrWhiteSpace(AiApiKey))
                    OpenAiAiApiKey = AiApiKey;
                if (string.IsNullOrWhiteSpace(OpenAiAiCustomEndpoint) && !string.IsNullOrWhiteSpace(AiCustomEndpoint))
                    OpenAiAiCustomEndpoint = AiCustomEndpoint;
                if (OpenAiAiTemperaturePercent == 75 && AiTemperaturePercent != 75)
                    OpenAiAiTemperaturePercent = AiTemperaturePercent;
                if (OpenAiAiTimeoutSeconds == 60 && AiTimeoutSeconds != 45)
                    OpenAiAiTimeoutSeconds = AiTimeoutSeconds;
                if (OpenAiAiMaxCharacters == 200 && AiMaxCharacters != 200)
                    OpenAiAiMaxCharacters = AiMaxCharacters;
            }
            else if (provider.Equals("OpenRouter", StringComparison.OrdinalIgnoreCase))
            {
                if (string.IsNullOrWhiteSpace(OpenRouterAiModel) && !string.IsNullOrWhiteSpace(AiModel))
                    OpenRouterAiModel = AiModel;
                if (string.IsNullOrWhiteSpace(OpenRouterAiApiKey) && !string.IsNullOrWhiteSpace(AiApiKey))
                    OpenRouterAiApiKey = AiApiKey;
                if (string.IsNullOrWhiteSpace(OpenRouterAiCustomEndpoint) && !string.IsNullOrWhiteSpace(AiCustomEndpoint))
                    OpenRouterAiCustomEndpoint = AiCustomEndpoint;
                if (OpenRouterAiTemperaturePercent == 75 && AiTemperaturePercent != 75)
                    OpenRouterAiTemperaturePercent = AiTemperaturePercent;
                if (OpenRouterAiTimeoutSeconds == 60 && AiTimeoutSeconds != 45)
                    OpenRouterAiTimeoutSeconds = AiTimeoutSeconds;
                if (OpenRouterAiMaxCharacters == 200 && AiMaxCharacters != 200)
                    OpenRouterAiMaxCharacters = AiMaxCharacters;
            }
            else if (provider.Equals("Local", StringComparison.OrdinalIgnoreCase))
            {
                if (string.IsNullOrWhiteSpace(LocalAiModel) && !string.IsNullOrWhiteSpace(AiModel))
                    LocalAiModel = AiModel;
                if (string.IsNullOrWhiteSpace(LocalAiApiKey) && !string.IsNullOrWhiteSpace(AiApiKey))
                    LocalAiApiKey = AiApiKey;
                if (string.IsNullOrWhiteSpace(LocalAiCustomEndpoint) && !string.IsNullOrWhiteSpace(AiCustomEndpoint))
                    LocalAiCustomEndpoint = AiCustomEndpoint;
                if (LocalAiTemperaturePercent == 75 && AiTemperaturePercent != 75)
                    LocalAiTemperaturePercent = AiTemperaturePercent;
                if (LocalAiTimeoutSeconds == 60 && AiTimeoutSeconds != 45)
                    LocalAiTimeoutSeconds = AiTimeoutSeconds;
                if (LocalAiMaxCharacters == 200 && AiMaxCharacters != 200)
                    LocalAiMaxCharacters = AiMaxCharacters;
            }


            ApplyAiDefaultsAndLimits();
        }

        public void ApplyAiDefaultsAndLimits()
        {
            AiProvider = NormalizeProvider(AiProvider);

            // These options are no longer exposed in GMCM. Keep their runtime behavior fixed.
            // Manual outfit dialogue JSON fallback stays disabled so the mod focuses on AI-generated lines.
            UseJsonFallbackForOutfitReactions = false;
            EnableNpcOutfitReactions = true;

            // Backfill the default only when Profile 1 is still Gemini and has no model.
            // Never replace a model the player already selected.
            if (NormalizeProvider(AiProviderSlot1).Equals("Gemini", StringComparison.OrdinalIgnoreCase)
                && string.IsNullOrWhiteSpace(AiModelSlot1))
            {
                AiModelSlot1 = DefaultGeminiModel;
            }

            // Profile 1 ships enabled with Gemini 3.1 Flash-Lite, whose official endpoint
            // is resolved automatically. Other providers intentionally keep their model and
            // endpoint fields empty so players can configure them explicitly if desired.
            // Alternative defaults left commented for reference only:
            //
            // if (string.IsNullOrWhiteSpace(DeepSeekAiModel))
            //     DeepSeekAiModel = "deepseek-v4-flash";
            // if (string.IsNullOrWhiteSpace(GeminiAiModel))
            //     GeminiAiModel = "gemini-2.5-flash";
            // if (string.IsNullOrWhiteSpace(OpenAiAiModel))
            //     OpenAiAiModel = "gpt-4.1-mini";
            // if (string.IsNullOrWhiteSpace(OpenRouterAiModel))
            //     OpenRouterAiModel = "openrouter/free";
            // if (string.IsNullOrWhiteSpace(OpenRouterAiCustomEndpoint))
            //     OpenRouterAiCustomEndpoint = "https://openrouter.ai/api/v1";
            // if (string.IsNullOrWhiteSpace(LocalAiModel))
            //     LocalAiModel = "llama3.2";
            // if (string.IsNullOrWhiteSpace(LocalAiCustomEndpoint))
            //     LocalAiCustomEndpoint = "http://localhost:11434/v1";
            // if (string.IsNullOrWhiteSpace(MistralAiModel))
            //     MistralAiModel = "mistral-small-latest";
            // if (string.IsNullOrWhiteSpace(GroqAiModel))
            //     GroqAiModel = "llama-3.3-70b-versatile";
            // if (string.IsNullOrWhiteSpace(TogetherAiModel))
            //     TogetherAiModel = "meta-llama/Llama-3.3-70B-Instruct-Turbo";
            // if (string.IsNullOrWhiteSpace(AnthropicAiModel))
            //     AnthropicAiModel = "claude-haiku-4-5-20251001";
            // if (string.IsNullOrWhiteSpace(XAiModel))
            //     XAiModel = "grok-3-mini";
            // if (string.IsNullOrWhiteSpace(CerebrasAiModel))
            //     CerebrasAiModel = "llama-3.3-70b";

            BackfillAiCredentialSlots();

            DeepSeekAiTemperaturePercent = Clamp(DeepSeekAiTemperaturePercent, 0, 200);
            GeminiAiTemperaturePercent = Clamp(GeminiAiTemperaturePercent, 0, 200);
            OpenAiAiTemperaturePercent = Clamp(OpenAiAiTemperaturePercent, 0, 200);
            OpenRouterAiTemperaturePercent = Clamp(OpenRouterAiTemperaturePercent, 0, 200);
            LocalAiTemperaturePercent = Clamp(LocalAiTemperaturePercent, 0, 200);
            MistralAiTemperaturePercent = Clamp(MistralAiTemperaturePercent, 0, 200);
            GroqAiTemperaturePercent = Clamp(GroqAiTemperaturePercent, 0, 200);
            TogetherAiTemperaturePercent = Clamp(TogetherAiTemperaturePercent, 0, 200);
            AnthropicAiTemperaturePercent = Clamp(AnthropicAiTemperaturePercent, 0, 200);
            XAiTemperaturePercent = Clamp(XAiTemperaturePercent, 0, 200);
            CerebrasAiTemperaturePercent = Clamp(CerebrasAiTemperaturePercent, 0, 200);

            DeepSeekAiTimeoutSeconds = Clamp(DeepSeekAiTimeoutSeconds, 3, 120);
            GeminiAiTimeoutSeconds = Clamp(GeminiAiTimeoutSeconds, 3, 120);
            OpenAiAiTimeoutSeconds = Clamp(OpenAiAiTimeoutSeconds, 3, 120);
            OpenRouterAiTimeoutSeconds = Clamp(OpenRouterAiTimeoutSeconds, 3, 120);
            LocalAiTimeoutSeconds = Clamp(LocalAiTimeoutSeconds, 3, 120);
            MistralAiTimeoutSeconds = Clamp(MistralAiTimeoutSeconds, 3, 120);
            GroqAiTimeoutSeconds = Clamp(GroqAiTimeoutSeconds, 3, 120);
            TogetherAiTimeoutSeconds = Clamp(TogetherAiTimeoutSeconds, 3, 120);
            AnthropicAiTimeoutSeconds = Clamp(AnthropicAiTimeoutSeconds, 3, 120);
            XAiTimeoutSeconds = Clamp(XAiTimeoutSeconds, 3, 120);
            CerebrasAiTimeoutSeconds = Clamp(CerebrasAiTimeoutSeconds, 3, 120);

            DeepSeekAiMaxCharacters = Clamp(DeepSeekAiMaxCharacters, 80, 400);
            GeminiAiMaxCharacters = Clamp(GeminiAiMaxCharacters, 80, 400);
            OpenAiAiMaxCharacters = Clamp(OpenAiAiMaxCharacters, 80, 400);
            OpenRouterAiMaxCharacters = Clamp(OpenRouterAiMaxCharacters, 80, 400);
            LocalAiMaxCharacters = Clamp(LocalAiMaxCharacters, 80, 400);
            MistralAiMaxCharacters = Clamp(MistralAiMaxCharacters, 80, 400);
            GroqAiMaxCharacters = Clamp(GroqAiMaxCharacters, 80, 400);
            TogetherAiMaxCharacters = Clamp(TogetherAiMaxCharacters, 80, 400);
            AnthropicAiMaxCharacters = Clamp(AnthropicAiMaxCharacters, 80, 400);
            XAiMaxCharacters = Clamp(XAiMaxCharacters, 80, 400);
            CerebrasAiMaxCharacters = Clamp(CerebrasAiMaxCharacters, 80, 400);

            AiMaxCharacters = Clamp(AiMaxCharacters, 80, 400);
            AiTimeoutSeconds = Clamp(AiTimeoutSeconds, 3, 120);
            AiTemperaturePercent = Clamp(AiTemperaturePercent, 0, 200);
            NpcOutfitReactionChance = Clamp(NpcOutfitReactionChance, 0, 100);
            NpcRepeatedVisualNoticeChance = Clamp(NpcRepeatedVisualNoticeChance, 0, 100);
            FestivalNpcOutfitReactionChance = Clamp(FestivalNpcOutfitReactionChance, 0, 100);
            OutfitNoticeDistance = Clamp(OutfitNoticeDistance, 64, 3000);
            OutfitCancelDistance = Clamp(OutfitCancelDistance, 64, 5000);

            FarmerBirthdayDay = Clamp(FarmerBirthdayDay, 0, 28);
            FarmerBirthdaySeason = NormalizeSeason(FarmerBirthdaySeason);
        }


        private void BackfillAiCredentialSlots()
        {
            if (!string.IsNullOrWhiteSpace(AiModelSlot1) || !string.IsNullOrWhiteSpace(AiApiKeySlot1) || !string.IsNullOrWhiteSpace(AiCustomEndpointSlot1)
                || !string.IsNullOrWhiteSpace(AiModelSlot2) || !string.IsNullOrWhiteSpace(AiApiKeySlot2) || !string.IsNullOrWhiteSpace(AiCustomEndpointSlot2)
                || !string.IsNullOrWhiteSpace(AiModelSlot3) || !string.IsNullOrWhiteSpace(AiApiKeySlot3) || !string.IsNullOrWhiteSpace(AiCustomEndpointSlot3))
            {
                return;
            }

            string provider = NormalizeProvider(AiProvider);
            AiModelSlot1 = GetProviderFallbackModel(provider);
            AiApiKeySlot1 = GetProviderFallbackApiKey(provider);
            AiCustomEndpointSlot1 = GetProviderFallbackEndpoint(provider);
        }

        public string GetResolvedAiModelForProvider(string provider)
        {
            provider = NormalizeProvider(provider);
            int slot = GetAiCredentialSlotForProvider(provider);
            string value = slot > 0 ? GetSlotModel(slot) : "";
            return !string.IsNullOrWhiteSpace(value) ? value : GetProviderFallbackModel(provider);
        }

        public string GetResolvedAiApiKeyForProvider(string provider)
        {
            provider = NormalizeProvider(provider);
            int slot = GetAiCredentialSlotForProvider(provider);
            string value = slot > 0 ? GetSlotApiKey(slot) : "";
            return !string.IsNullOrWhiteSpace(value) ? value : GetProviderFallbackApiKey(provider);
        }

        public string GetResolvedAiEndpointForProvider(string provider)
        {
            provider = NormalizeProvider(provider);
            int slot = GetAiCredentialSlotForProvider(provider);
            string value = slot > 0 ? GetSlotEndpoint(slot) : "";
            if (!string.IsNullOrWhiteSpace(value))
                return value;

            // An empty value means automatic. The provider resolves its official endpoint only
            // when a request is sent; an explicit value remains a custom override.
            return GetProviderFallbackEndpoint(provider);
        }

        public int GetAiCredentialSlotForProvider(string provider)
        {
            provider = NormalizeProvider(provider);

            // 1. PRIMARY: an enabled slot whose chosen provider matches (checkbox wins).
            for (int slot = 1; slot <= 5; slot++)
            {
                if (IsSlotEnabled(slot)
                    && NormalizeProvider(GetSlotProvider(slot)).Equals(provider, StringComparison.OrdinalIgnoreCase)
                    && (!string.IsNullOrWhiteSpace(GetSlotModel(slot)) || !string.IsNullOrWhiteSpace(GetSlotApiKey(slot)) || !string.IsNullOrWhiteSpace(GetSlotEndpoint(slot))))
                    return slot;
            }

            // 2. FALLBACK: the old auto-guess based on model/endpoint hints.
            for (int slot = 1; slot <= 5; slot++)
            {
                if (SlotMatchesProvider(slot, provider))
                    return slot;
            }

            return 0;
        }

        // The active provider is the provider of the enabled profile.
        // The AI service refuses to generate if more than one profile is enabled,
        // so this method only falls back to the first enabled profile for compatibility.
        public string GetActiveProvider()
        {
            for (int slot = 1; slot <= 5; slot++)
            {
                if (IsSlotEnabled(slot))
                    return NormalizeProvider(GetSlotProvider(slot));
            }

            return NormalizeProvider(GetSlotProvider(1));
        }

        public int GetActiveAiProfileSlot()
        {
            for (int slot = 1; slot <= 5; slot++)
            {
                if (IsSlotEnabled(slot))
                    return slot;
            }

            return 1;
        }

        public string GetOpenRouterMaxInputPrice(int slot)
        {
            return slot switch
            {
                1 => OpenRouterMaxInputPriceSlot1,
                2 => OpenRouterMaxInputPriceSlot2,
                3 => OpenRouterMaxInputPriceSlot3,
                4 => OpenRouterMaxInputPriceSlot4,
                5 => OpenRouterMaxInputPriceSlot5,
                _ => ""
            };
        }

        public string GetOpenRouterMaxOutputPrice(int slot)
        {
            return slot switch
            {
                1 => OpenRouterMaxOutputPriceSlot1,
                2 => OpenRouterMaxOutputPriceSlot2,
                3 => OpenRouterMaxOutputPriceSlot3,
                4 => OpenRouterMaxOutputPriceSlot4,
                5 => OpenRouterMaxOutputPriceSlot5,
                _ => ""
            };
        }

        public string GetOpenRouterAllowedProviders(int slot)
        {
            return slot switch
            {
                1 => OpenRouterAllowedProvidersSlot1,
                2 => OpenRouterAllowedProvidersSlot2,
                3 => OpenRouterAllowedProvidersSlot3,
                4 => OpenRouterAllowedProvidersSlot4,
                5 => OpenRouterAllowedProvidersSlot5,
                _ => ""
            };
        }

        private string GetSlotVisionMode(int slot)
        {
            string mode = slot switch
            {
                1 => AiVisionModeSlot1,
                2 => AiVisionModeSlot2,
                3 => AiVisionModeSlot3,
                4 => AiVisionModeSlot4,
                5 => AiVisionModeSlot5,
                _ => "Auto"
            };
            if (string.IsNullOrWhiteSpace(mode)) return "Auto";
            if (mode.Equals("On", StringComparison.OrdinalIgnoreCase)) return "On";
            if (mode.Equals("Off", StringComparison.OrdinalIgnoreCase)) return "Off";
            return "Auto";
        }

        // Detects whether a model is multimodal (accepts images) from its NAME. This catches the
        // common cases where the model advertises vision in its name; it can't know every model,
        // which is why the per-profile vision mode can force it On/Off.
        public static bool ModelNameLooksMultimodal(string model)
        {
            if (string.IsNullOrWhiteSpace(model)) return false;
            string m = model.ToLowerInvariant();
            string[] hints =
            {
                "vl", "vision", "multimodal", "-mm", "llava", "pixtral",
                "gpt-4o", "4o-", "gpt-5", "gemini",
                "claude-3", "claude-4", "claude-haiku", "claude-opus", "claude-sonnet",
                "qwen2-vl", "qwen2.5-vl", "qwen3-vl", "qwen-vl", "qwen-omni", "qwen3-omni",
                "qwen3.7", "qwen3-7", "qwen/qwen3.7", "qwen/qwen3-7",
                "llama-3.2", "llama3.2", "internvl",
                "molmo", "phi-3.5-vision", "phi-4-multimodal", "maverick", "scout"
            };
            foreach (string h in hints)
                if (m.Contains(h)) return true;
            return false;
        }

        // Final decision: should the rendered farmer image be sent to the active profile's model?
        public bool ShouldSendImageToActiveModel()
        {
            int slot = 0;
            string provider = GetActiveProvider();
            for (int s = 1; s <= 5; s++)
            {
                if (IsSlotEnabled(s)) { slot = s; break; }
            }
            if (slot == 0) slot = 1;

            string mode = GetSlotVisionMode(slot);
            if (mode.Equals("On", StringComparison.OrdinalIgnoreCase)) return true;
            if (mode.Equals("Off", StringComparison.OrdinalIgnoreCase)) return false;

            // Auto: decide from the model name.
            string model = GetResolvedAiModelForProvider(provider);
            return ModelNameLooksMultimodal(model);
        }

        private bool SlotMatchesProvider(int slot, string provider)
        {
            string model = (GetSlotModel(slot) ?? "").Trim().ToLowerInvariant();
            string endpoint = (GetSlotEndpoint(slot) ?? "").Trim().ToLowerInvariant();
            if (string.IsNullOrWhiteSpace(model) && string.IsNullOrWhiteSpace(endpoint))
                return false;

            provider = NormalizeProvider(provider);

            if (provider.Equals("Local", StringComparison.OrdinalIgnoreCase))
                return endpoint.StartsWith("http://localhost") || endpoint.StartsWith("http://127.0.0.1") || endpoint.Contains("localhost:") || endpoint.Contains("127.0.0.1:");

            if (provider.Equals("OpenRouter", StringComparison.OrdinalIgnoreCase))
                return endpoint.Contains("openrouter.ai") || model.Equals("openrouter/free") || model.Contains(":free") || (model.Contains("/") && !endpoint.Contains("deepseek.com") && !endpoint.Contains("googleapis.com") && !endpoint.Contains("openai.com"));

            if (provider.Equals("Gemini", StringComparison.OrdinalIgnoreCase))
                return endpoint.Contains("generativelanguage") || endpoint.Contains("googleapis.com") || model.Contains("gemini");

            if (provider.Equals("OpenAI", StringComparison.OrdinalIgnoreCase))
                return endpoint.Contains("api.openai.com") || model.StartsWith("gpt-") || model.StartsWith("o1") || model.StartsWith("o3") || model.StartsWith("o4");

            return endpoint.Contains("deepseek.com") || (model.StartsWith("deepseek") && !endpoint.Contains("openrouter.ai"));
        }

        private string GetSlotModel(int slot)
        {
            return slot switch
            {
                1 => AiModelSlot1,
                2 => AiModelSlot2,
                3 => AiModelSlot3,
                4 => AiModelSlot4,
                5 => AiModelSlot5,
                _ => ""
            };
        }

        private string GetSlotApiKey(int slot)
        {
            return slot switch
            {
                1 => AiApiKeySlot1,
                2 => AiApiKeySlot2,
                3 => AiApiKeySlot3,
                4 => AiApiKeySlot4,
                5 => AiApiKeySlot5,
                _ => ""
            };
        }

        private string GetSlotEndpoint(int slot)
        {
            return slot switch
            {
                1 => AiCustomEndpointSlot1,
                2 => AiCustomEndpointSlot2,
                3 => AiCustomEndpointSlot3,
                4 => AiCustomEndpointSlot4,
                5 => AiCustomEndpointSlot5,
                _ => ""
            };
        }

        public int CountEnabledAiProfiles()
        {
            int count = 0;
            for (int slot = 1; slot <= 5; slot++)
            {
                if (IsSlotEnabled(slot))
                    count++;
            }

            return count;
        }

        public bool HasMultipleEnabledAiProfiles()
        {
            return CountEnabledAiProfiles() > 1;
        }

        private string GetSlotProvider(int slot)
        {
            return slot switch
            {
                1 => AiProviderSlot1,
                2 => AiProviderSlot2,
                3 => AiProviderSlot3,
                4 => AiProviderSlot4,
                5 => AiProviderSlot5,
                _ => ""
            };
        }

        private bool IsSlotEnabled(int slot)
        {
            return slot switch
            {
                1 => AiSlot1Enabled,
                2 => AiSlot2Enabled,
                3 => AiSlot3Enabled,
                4 => AiSlot4Enabled,
                5 => AiSlot5Enabled,
                _ => false
            };
        }

        private string GetProviderFallbackModel(string provider)
        {
            provider = NormalizeProvider(provider);
            if (provider.Equals("Gemini", StringComparison.OrdinalIgnoreCase))
                return GeminiAiModel;
            if (provider.Equals("OpenAI", StringComparison.OrdinalIgnoreCase))
                return OpenAiAiModel;
            if (provider.Equals("OpenRouter", StringComparison.OrdinalIgnoreCase))
                return OpenRouterAiModel;
            if (provider.Equals("Mistral", StringComparison.OrdinalIgnoreCase))
                return MistralAiModel;
            if (provider.Equals("Groq", StringComparison.OrdinalIgnoreCase))
                return GroqAiModel;
            if (provider.Equals("Together", StringComparison.OrdinalIgnoreCase))
                return TogetherAiModel;
            if (provider.Equals("Local", StringComparison.OrdinalIgnoreCase))
                return LocalAiModel;
            if (provider.Equals("Anthropic", StringComparison.OrdinalIgnoreCase))
                return AnthropicAiModel;
            if (provider.Equals("xAI", StringComparison.OrdinalIgnoreCase))
                return XAiModel;
            if (provider.Equals("Cerebras", StringComparison.OrdinalIgnoreCase))
                return CerebrasAiModel;
            return DeepSeekAiModel;
        }

        private string GetProviderFallbackApiKey(string provider)
        {
            provider = NormalizeProvider(provider);
            if (provider.Equals("Gemini", StringComparison.OrdinalIgnoreCase))
                return GeminiAiApiKey;
            if (provider.Equals("OpenAI", StringComparison.OrdinalIgnoreCase))
                return OpenAiAiApiKey;
            if (provider.Equals("OpenRouter", StringComparison.OrdinalIgnoreCase))
                return OpenRouterAiApiKey;
            if (provider.Equals("Mistral", StringComparison.OrdinalIgnoreCase))
                return MistralAiApiKey;
            if (provider.Equals("Groq", StringComparison.OrdinalIgnoreCase))
                return GroqAiApiKey;
            if (provider.Equals("Together", StringComparison.OrdinalIgnoreCase))
                return TogetherAiApiKey;
            if (provider.Equals("Local", StringComparison.OrdinalIgnoreCase))
                return LocalAiApiKey;
            if (provider.Equals("Anthropic", StringComparison.OrdinalIgnoreCase))
                return AnthropicAiApiKey;
            if (provider.Equals("xAI", StringComparison.OrdinalIgnoreCase))
                return XAiApiKey;
            if (provider.Equals("Cerebras", StringComparison.OrdinalIgnoreCase))
                return CerebrasAiApiKey;
            return DeepSeekAiApiKey;
        }

        private string GetProviderFallbackEndpoint(string provider)
        {
            provider = NormalizeProvider(provider);
            string custom;
            if (provider.Equals("Gemini", StringComparison.OrdinalIgnoreCase)) custom = GeminiAiCustomEndpoint;
            else if (provider.Equals("OpenAI", StringComparison.OrdinalIgnoreCase)) custom = OpenAiAiCustomEndpoint;
            else if (provider.Equals("OpenRouter", StringComparison.OrdinalIgnoreCase)) custom = OpenRouterAiCustomEndpoint;
            else if (provider.Equals("Mistral", StringComparison.OrdinalIgnoreCase)) custom = MistralAiCustomEndpoint;
            else if (provider.Equals("Groq", StringComparison.OrdinalIgnoreCase)) custom = GroqAiCustomEndpoint;
            else if (provider.Equals("Together", StringComparison.OrdinalIgnoreCase)) custom = TogetherAiCustomEndpoint;
            else if (provider.Equals("Local", StringComparison.OrdinalIgnoreCase)) custom = LocalAiCustomEndpoint;
            else if (provider.Equals("Anthropic", StringComparison.OrdinalIgnoreCase)) custom = AnthropicAiCustomEndpoint;
            else if (provider.Equals("xAI", StringComparison.OrdinalIgnoreCase)) custom = XAiCustomEndpoint;
            else if (provider.Equals("Cerebras", StringComparison.OrdinalIgnoreCase)) custom = CerebrasAiCustomEndpoint;
            else custom = DeepSeekAiCustomEndpoint;

            return custom ?? "";
        }

        private static string NormalizeProvider(string provider)
        {
            return AiProviderRegistry.Normalize(provider);
        }

        private static string NormalizeSeason(string season)
        {
            if (string.IsNullOrWhiteSpace(season))
                return "";

            string value = season.Trim().ToLowerInvariant();
            return value switch
            {
                "spring" or "primavera" => "spring",
                "summer" or "verao" or "verão" => "summer",
                "fall" or "autumn" or "outono" => "fall",
                "winter" or "inverno" => "winter",
                _ => value
            };
        }

        private static int Clamp(int value, int min, int max)
        {
            if (value < min)
                return min;
            if (value > max)
                return max;
            return value;
        }
    }
}
