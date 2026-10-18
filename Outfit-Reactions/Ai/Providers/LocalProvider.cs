using System;

namespace OutfitReactions.Ai.Providers
{
    internal sealed class LocalProvider : AiProviderBase
    {
        public LocalProvider() : base("Local", "Local / OpenAI Compatible", AiTransportKind.OpenAiChat, supportsVision: true, isLocal: true) { }

        public override string ResolveEndpoint(string customEndpoint, string model)
        {
            string custom = (customEndpoint ?? "").Trim().TrimEnd('/');
            if (string.IsNullOrWhiteSpace(custom))
                return "http://localhost:1234/v1/chat/completions";
            if (custom.EndsWith("/chat/completions", StringComparison.OrdinalIgnoreCase))
                return custom;
            if (custom.EndsWith("/v1", StringComparison.OrdinalIgnoreCase))
                return custom + "/chat/completions";
            return custom + "/v1/chat/completions";
        }
    }
}
