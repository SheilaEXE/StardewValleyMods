namespace OutfitReactions.Ai.Providers
{
    internal sealed class CerebrasProvider : AiProviderBase
    {
        public CerebrasProvider() : base("Cerebras", "Cerebras", AiTransportKind.OpenAiChat) { }
        public override string ResolveEndpoint(string customEndpoint, string model) =>
            ResolveRoute(customEndpoint, "https://api.cerebras.ai/v1/chat/completions", "/chat/completions");
    }
}
