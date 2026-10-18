using System.Collections.Generic;
using System.Collections.Concurrent;
using System;
using System.Net.Http;

namespace OutfitReactions.Ai.Providers
{
    internal sealed class OpenRouterProvider : AiProviderBase
    {
        private readonly ConcurrentDictionary<string, byte> mandatoryReasoningModels = new(StringComparer.OrdinalIgnoreCase);

        public OpenRouterProvider() : base("OpenRouter", "OpenRouter", AiTransportKind.OpenAiChat, supportsVision: true) { }
        public override string ResolveEndpoint(string customEndpoint, string model) =>
            ResolveRoute(customEndpoint, "https://openrouter.ai/api/v1/chat/completions", "/chat/completions");
        public override void ConfigureRequestHeaders(HttpRequestMessage request)
        {
            request.Headers.TryAddWithoutValidation("HTTP-Referer", "https://www.nexusmods.com/stardewvalley/mods/");
            request.Headers.TryAddWithoutValidation("X-OpenRouter-Title", "Outfit Reactions");
        }
        public override void ConfigureRequestBody(Dictionary<string, object> body, string model)
        {
            body["reasoning"] = UsesMinimumReasoning(model)
                ? new Dictionary<string, object>
                {
                    ["effort"] = "minimal",
                    ["exclude"] = true
                }
                : new Dictionary<string, object> { ["enabled"] = false };
        }

        public bool UsesMinimumReasoning(string model)
        {
            string normalized = (model ?? "").Trim();
            return IsKnownMandatoryReasoningModel(normalized)
                || (!string.IsNullOrWhiteSpace(normalized) && mandatoryReasoningModels.ContainsKey(normalized));
        }

        public void MarkReasoningMandatory(string model)
        {
            string normalized = (model ?? "").Trim();
            if (!string.IsNullOrWhiteSpace(normalized))
                mandatoryReasoningModels.TryAdd(normalized, 0);
        }

        private static bool IsKnownMandatoryReasoningModel(string model)
        {
            string normalized = (model ?? "").Trim().ToLowerInvariant();
            return normalized.StartsWith("z-ai/glm-5.3", StringComparison.OrdinalIgnoreCase);
        }
    }
}
