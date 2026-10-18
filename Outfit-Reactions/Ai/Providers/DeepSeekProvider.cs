using System.Collections.Generic;

namespace OutfitReactions.Ai.Providers
{
    internal sealed class DeepSeekProvider : AiProviderBase
    {
        public DeepSeekProvider() : base("DeepSeek", "DeepSeek", AiTransportKind.OpenAiChat) { }
        public override string ResolveEndpoint(string customEndpoint, string model) =>
            ResolveRoute(customEndpoint, "https://api.deepseek.com/chat/completions", "/chat/completions");
        public override void ConfigureRequestBody(Dictionary<string, object> body, string model)
        {
            body["thinking"] = new Dictionary<string, object> { ["type"] = "disabled" };
            body["response_format"] = new { type = "json_object" };
        }
    }
}
