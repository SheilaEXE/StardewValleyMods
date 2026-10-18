namespace OutfitReactions.Ai.Providers
{
    internal sealed class AnthropicProvider : AiProviderBase
    {
        public AnthropicProvider() : base("Anthropic", "Anthropic", AiTransportKind.AnthropicMessages, supportsVision: true) { }
        public override string ResolveEndpoint(string customEndpoint, string model) =>
            ResolveRoute(customEndpoint, "https://api.anthropic.com/v1/messages", "/v1/messages");
    }
}
