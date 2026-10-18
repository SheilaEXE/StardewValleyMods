namespace OutfitReactions.Ai
{
    /// <summary>
    /// The resolved AI settings for a single generation: which provider/model/endpoint/key to use,
    /// plus temperature, timeout, and the visible character budget. Built by
    /// ActiveAiSettingsResolver and consumed by AiProviderClient.
    /// </summary>
    internal sealed class ActiveAiSettings
    {
        public string Provider { get; set; } = "DeepSeek";
        public string Model { get; set; } = "deepseek-v4-flash";
        public string ApiKey { get; set; } = "";
        public string Endpoint { get; set; } = "";
        public int TemperaturePercent { get; set; } = 75;
        public int TimeoutSeconds { get; set; } = 60;
        public int MaxCharacters { get; set; } = 120;
        public string OpenRouterMaxInputPrice { get; set; } = "";
        public string OpenRouterMaxOutputPrice { get; set; } = "";
        public string OpenRouterAllowedProviders { get; set; } = "";
    }
}
