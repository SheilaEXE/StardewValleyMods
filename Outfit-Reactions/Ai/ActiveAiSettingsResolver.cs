using System;
using OutfitReactions;
using OutfitReactions.Ai.Providers;

namespace OutfitReactions.Ai
{
    /// <summary>Resolves the active provider configuration used by one AI request.</summary>
    internal static class ActiveAiSettingsResolver
    {
        public static bool IsLocal(ActiveAiSettings ai)
        {
            string endpoint = ai?.Endpoint ?? "";
            return AiProviderRegistry.Get(ai?.Provider).IsLocal
                || endpoint.StartsWith("http://localhost", StringComparison.OrdinalIgnoreCase)
                || endpoint.StartsWith("http://127.0.0.1", StringComparison.OrdinalIgnoreCase);
        }

        public static ActiveAiSettings Resolve(ModConfig config)
        {
            config ??= new ModConfig();
            config.ApplyAiDefaultsAndLimits();

            string provider = config.GetActiveProvider();
            if (provider.Equals("Gemini", StringComparison.OrdinalIgnoreCase))
                return Create(config, "Gemini", config.GeminiAiTemperaturePercent, config.GeminiAiTimeoutSeconds, config.GeminiAiMaxCharacters);
            if (provider.Equals("OpenAI", StringComparison.OrdinalIgnoreCase))
                return Create(config, "OpenAI", config.OpenAiAiTemperaturePercent, config.OpenAiAiTimeoutSeconds, config.OpenAiAiMaxCharacters);
            if (provider.Equals("OpenRouter", StringComparison.OrdinalIgnoreCase))
                return Create(config, "OpenRouter", config.OpenRouterAiTemperaturePercent, config.OpenRouterAiTimeoutSeconds, config.OpenRouterAiMaxCharacters);
            if (provider.Equals("Local", StringComparison.OrdinalIgnoreCase) || provider.Equals("OpenAI-Compatible", StringComparison.OrdinalIgnoreCase))
                return Create(config, "Local", config.LocalAiTemperaturePercent, config.LocalAiTimeoutSeconds, config.LocalAiMaxCharacters);
            if (provider.Equals("Mistral", StringComparison.OrdinalIgnoreCase))
                return Create(config, "Mistral", config.MistralAiTemperaturePercent, config.MistralAiTimeoutSeconds, config.MistralAiMaxCharacters);
            if (provider.Equals("Groq", StringComparison.OrdinalIgnoreCase))
                return Create(config, "Groq", config.GroqAiTemperaturePercent, config.GroqAiTimeoutSeconds, config.GroqAiMaxCharacters);
            if (provider.Equals("Together", StringComparison.OrdinalIgnoreCase) || provider.Equals("TogetherAI", StringComparison.OrdinalIgnoreCase))
                return Create(config, "Together", config.TogetherAiTemperaturePercent, config.TogetherAiTimeoutSeconds, config.TogetherAiMaxCharacters);
            if (provider.Equals("Anthropic", StringComparison.OrdinalIgnoreCase))
                return Create(config, "Anthropic", config.AnthropicAiTemperaturePercent, config.AnthropicAiTimeoutSeconds, config.AnthropicAiMaxCharacters);
            if (provider.Equals("xAI", StringComparison.OrdinalIgnoreCase))
                return Create(config, "xAI", config.XAiTemperaturePercent, config.XAiTimeoutSeconds, config.XAiMaxCharacters);
            if (provider.Equals("Cerebras", StringComparison.OrdinalIgnoreCase))
                return Create(config, "Cerebras", config.CerebrasAiTemperaturePercent, config.CerebrasAiTimeoutSeconds, config.CerebrasAiMaxCharacters);
            if (provider.Equals("DeepSeek", StringComparison.OrdinalIgnoreCase))
                return Create(config, "DeepSeek", config.DeepSeekAiTemperaturePercent, config.DeepSeekAiTimeoutSeconds, config.DeepSeekAiMaxCharacters);

            return Create(config, "Gemini", config.GeminiAiTemperaturePercent, config.GeminiAiTimeoutSeconds, config.GeminiAiMaxCharacters);
        }

        private static ActiveAiSettings Create(ModConfig config, string provider, int temperaturePercent, int timeoutSeconds, int maxCharacters)
        {
            int activeSlot = config.GetActiveAiProfileSlot();
            bool isOpenRouter = provider.Equals("OpenRouter", StringComparison.OrdinalIgnoreCase);
            return new ActiveAiSettings
            {
                Provider = provider,
                Model = config.GetResolvedAiModelForProvider(provider),
                ApiKey = config.GetResolvedAiApiKeyForProvider(provider),
                Endpoint = config.GetResolvedAiEndpointForProvider(provider),
                TemperaturePercent = temperaturePercent,
                TimeoutSeconds = timeoutSeconds,
                MaxCharacters = maxCharacters,
                OpenRouterMaxInputPrice = isOpenRouter ? config.GetOpenRouterMaxInputPrice(activeSlot) : "",
                OpenRouterMaxOutputPrice = isOpenRouter ? config.GetOpenRouterMaxOutputPrice(activeSlot) : "",
                OpenRouterAllowedProviders = isOpenRouter ? config.GetOpenRouterAllowedProviders(activeSlot) : ""
            };
        }
    }
}
