using System;

namespace OutfitReactions.Ai.Providers
{
    internal sealed class GeminiProvider : AiProviderBase
    {
        public GeminiProvider() : base("Gemini", "Gemini", AiTransportKind.GeminiGenerateContent, supportsVision: true) { }

        public override string ResolveEndpoint(string customEndpoint, string model)
        {
            string custom = (customEndpoint ?? "").Trim().TrimEnd('/');
            if (string.IsNullOrWhiteSpace(custom))
                return $"https://generativelanguage.googleapis.com/v1beta/models/{Uri.EscapeDataString(model ?? "")}:generateContent";
            if (custom.Equals("https://generativelanguage.googleapis.com", StringComparison.OrdinalIgnoreCase))
                return $"{custom}/v1beta/models/{Uri.EscapeDataString(model ?? "")}:generateContent";
            if (custom.Contains(":generateContent", StringComparison.OrdinalIgnoreCase))
                return custom;
            if (custom.EndsWith("/v1beta", StringComparison.OrdinalIgnoreCase) || custom.EndsWith("/v1", StringComparison.OrdinalIgnoreCase))
                return $"{custom}/models/{Uri.EscapeDataString(model ?? "")}:generateContent";
            return custom;
        }
    }
}
