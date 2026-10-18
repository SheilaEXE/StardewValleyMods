using System.Collections.Generic;

namespace OutfitReactions.Ai.Providers
{
    internal sealed class XAiProvider : AiProviderBase
    {
        public XAiProvider() : base("xAI", "xAI (Grok)", AiTransportKind.OpenAiResponses, supportsVision: true) { }
        public override string ResolveEndpoint(string customEndpoint, string model) =>
            ResolveResponsesEndpoint(customEndpoint, "https://api.x.ai/v1/responses");
        public override void ConfigureRequestBody(Dictionary<string, object> body, string model)
        {
            body["store"] = false;
            body["include"] = new[] { "no_inline_citations" };
        }
    }
}
