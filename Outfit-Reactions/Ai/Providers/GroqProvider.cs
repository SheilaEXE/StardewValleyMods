namespace OutfitReactions.Ai.Providers
{
    internal sealed class GroqProvider : AiProviderBase
    {
        public GroqProvider() : base("Groq", "Groq", AiTransportKind.OpenAiChat, supportsVision: true) { }
        public override string ResolveEndpoint(string customEndpoint, string model) =>
            ResolveRoute(customEndpoint, "https://api.groq.com/openai/v1/chat/completions", "/chat/completions");
    }
}
