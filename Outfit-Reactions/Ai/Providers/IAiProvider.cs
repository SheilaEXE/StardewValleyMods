using System.Collections.Generic;
using System.Net.Http;

namespace OutfitReactions.Ai.Providers
{
    internal enum AiTransportKind
    {
        OpenAiChat,
        OpenAiResponses,
        AnthropicMessages,
        GeminiGenerateContent
    }

    internal interface IAiProvider
    {
        string Id { get; }
        string DisplayName { get; }
        AiTransportKind Transport { get; }
        bool SupportsVision { get; }
        bool IsLocal { get; }

        string ResolveEndpoint(string customEndpoint, string model);
        void ConfigureRequestHeaders(HttpRequestMessage request);
        void ConfigureRequestBody(Dictionary<string, object> body, string model);
    }
}
