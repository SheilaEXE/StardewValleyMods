using System;
using System.Collections.Generic;
using System.Linq;

namespace OutfitReactions.Ai.Providers
{
    internal static class AiProviderRegistry
    {
        private static readonly IReadOnlyList<IAiProvider> Providers = new IAiProvider[]
        {
            new GeminiProvider(),
            new OpenAiProvider(),
            new OpenRouterProvider(),
            new MistralProvider(),
            new GroqProvider(),
            new TogetherProvider(),
            new AnthropicProvider(),
            new XAiProvider(),
            new CerebrasProvider(),
            new DeepSeekProvider(),
            new LocalProvider()
        };

        private static readonly Dictionary<string, IAiProvider> ById = Providers
            .ToDictionary(provider => provider.Id, StringComparer.OrdinalIgnoreCase);

        public static IReadOnlyList<string> ProviderIds { get; } = Providers.Select(provider => provider.Id).ToArray();

        public static IAiProvider Get(string provider)
        {
            string normalized = Normalize(provider);
            return ById.TryGetValue(normalized, out IAiProvider value) ? value : ById["Gemini"];
        }

        public static string Normalize(string provider)
        {
            string value = (provider ?? "").Trim();
            if (value.Equals("TogetherAI", StringComparison.OrdinalIgnoreCase) || value.Equals("Together AI", StringComparison.OrdinalIgnoreCase))
                return "Together";
            if (value.Equals("OpenAI-Compatible", StringComparison.OrdinalIgnoreCase))
                return "Local";
            if (value.Equals("Claude", StringComparison.OrdinalIgnoreCase))
                return "Anthropic";
            if (value.Equals("Grok", StringComparison.OrdinalIgnoreCase) || value.Equals("x.ai", StringComparison.OrdinalIgnoreCase))
                return "xAI";
            return ById.TryGetValue(value, out IAiProvider providerDefinition) ? providerDefinition.Id : "Gemini";
        }
    }
}
