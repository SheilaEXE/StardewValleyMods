using System.Collections.Generic;

namespace OutfitReactions.Ai.Providers
{
    internal sealed class TogetherProvider : AiProviderBase
    {
        public TogetherProvider() : base("Together", "Together AI", AiTransportKind.OpenAiChat, supportsVision: true) { }
        public override string ResolveEndpoint(string customEndpoint, string model) =>
            ResolveRoute(customEndpoint, "https://api.together.ai/v1/chat/completions", "/chat/completions");
        public override void ConfigureRequestBody(Dictionary<string, object> body, string model) =>
            body["reasoning"] = new Dictionary<string, object> { ["enabled"] = false };
    }
}
