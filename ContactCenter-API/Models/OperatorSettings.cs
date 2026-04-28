namespace ContactCenterPOC.Models
{
    public class OperatorSettings
    {
        public double MaxCallTimeMinutes { get; set; } = 2.0;
        public string VoiceApiMode { get; set; } = "ChatGPT";  // "ChatGPT" or "VoiceLive"
        public string SelectedVoice { get; set; } = "alloy";   // alloy, echo, fable, onyx, nova, shimmer

        // VoiceLive-specific fields
        public string TranscriptionMode { get; set; } = "BuiltIn";  // "BuiltIn" or "SeparateSTT"
        public string VoiceLiveModel { get; set; } = "gpt-realtime-mini";
        public string SelectedVoiceLiveVoice { get; set; } = "en-US-Ava:DragonHDLatestNeural";

        // Feature 003: Agentic contact center settings
        public bool CallOverInternet { get; set; }
        public string? DefaultEscalationNumber { get; set; }
        public string? InboundPhoneNumber { get; set; }
        public string? DefaultInboundCampaignId { get; set; }
        public List<QualityCriterion> QualityCriteria { get; set; } = new();
        public string? HoldMessage { get; set; }

        public static readonly HashSet<string> ValidVoices = new(StringComparer.OrdinalIgnoreCase)
        {
            "alloy", "echo", "fable", "onyx", "nova", "shimmer"
        };

        public static readonly HashSet<string> ValidVoiceLiveModels = new(StringComparer.OrdinalIgnoreCase)
        {
            "gpt-4o-realtime-preview", "gpt-4o-mini-realtime-preview", "phi4-mm-realtime",
            "gpt-realtime", "gpt-realtime-mini"
        };

        public static readonly HashSet<string> ValidTranscriptionModes = new(StringComparer.OrdinalIgnoreCase)
        {
            "BuiltIn", "SeparateSTT"
        };
    }

    public class QualityCriterion
    {
        public string Name { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public int Weight { get; set; } = 5;
        public float MinimumPassingScore { get; set; } = 3.0f;
    }
}
