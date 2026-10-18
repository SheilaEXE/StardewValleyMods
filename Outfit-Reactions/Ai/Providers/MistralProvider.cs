namespace OutfitReactions.Ai.Providers
{
    internal sealed class MistralProvider : AiProviderBase
    {
        public MistralProvider() : base("Mistral", "Mistral", AiTransportKind.OpenAiChat) { }
        public override string ResolveEndpoint(string customEndpoint, string model) =>
            ResolveRoute(customEndpoint, "https://api.mistral.ai/v1/chat/completions", "/chat/completions");
    }
}
