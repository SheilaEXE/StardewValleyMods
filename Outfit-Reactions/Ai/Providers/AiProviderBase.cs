using System;
using System.Collections.Generic;
using System.Net.Http;

namespace OutfitReactions.Ai.Providers
{
    internal abstract class AiProviderBase : IAiProvider
    {
        protected AiProviderBase(string id, string displayName, AiTransportKind transport, bool supportsVision = false, bool isLocal = false)
        {
            Id = id;
            DisplayName = displayName;
            Transport = transport;
            SupportsVision = supportsVision;
            IsLocal = isLocal;
        }

        public string Id { get; }
        public string DisplayName { get; }
        public AiTransportKind Transport { get; }
        public bool SupportsVision { get; }
        public bool IsLocal { get; }

        public abstract string ResolveEndpoint(string customEndpoint, string model);
        public virtual void ConfigureRequestHeaders(HttpRequestMessage request) { }
        public virtual void ConfigureRequestBody(Dictionary<string, object> body, string model) { }

        protected static string ResolveRoute(string customEndpoint, string defaultEndpoint, string route)
        {
            string custom = (customEndpoint ?? "").Trim().TrimEnd('/');
            if (string.IsNullOrWhiteSpace(custom))
                return defaultEndpoint;
            if (custom.EndsWith(route, StringComparison.OrdinalIgnoreCase))
                return custom;
            if (Uri.TryCreate(defaultEndpoint, UriKind.Absolute, out Uri official)
                && custom.Equals(official.GetLeftPart(UriPartial.Authority), StringComparison.OrdinalIgnoreCase))
                return defaultEndpoint;
            if (custom.EndsWith("/v1", StringComparison.OrdinalIgnoreCase)
                && route.StartsWith("/v1/", StringComparison.OrdinalIgnoreCase))
                return custom + route.Substring(3);
            return custom + route;
        }

        protected static string ResolveResponsesEndpoint(string customEndpoint, string defaultEndpoint)
        {
            string custom = (customEndpoint ?? "").Trim().TrimEnd('/');
            if (custom.EndsWith("/responses", StringComparison.OrdinalIgnoreCase)
                || custom.EndsWith("/chat/completions", StringComparison.OrdinalIgnoreCase))
                return custom;
            return ResolveRoute(custom, defaultEndpoint, "/v1/responses");
        }
    }
}
