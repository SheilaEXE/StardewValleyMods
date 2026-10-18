using System.Collections.Generic;

namespace OutfitReactions.Ai.Providers
{
    internal sealed class OpenAiProvider : AiProviderBase
    {
        public OpenAiProvider() : base("OpenAI", "OpenAI", AiTransportKind.OpenAiResponses, supportsVision: true) { }
        public override string ResolveEndpoint(string customEndpoint, string model) =>
            ResolveResponsesEndpoint(customEndpoint, "https://api.openai.com/v1/responses");
        public override void ConfigureRequestBody(Dictionary<string, object> body, string model) => body["store"] = false;
    }
}
