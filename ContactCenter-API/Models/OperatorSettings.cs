namespace ContactCenterPOC.Models
{
    public class OperatorSettings
    {
        public double MaxCallTimeMinutes { get; set; } = 2.0;
        public string VoiceApiMode { get; set; } = "ChatGPT";  // "ChatGPT" or "VoiceLive"
        public string SelectedVoice { get; set; } = "alloy";   // alloy, echo, fable, onyx, nova, shimmer

        // VoiceLive-specific fields
        public string TranscriptionMode { get; set; } = "BuiltIn";  // "BuiltIn" or "SeparateSTT"
        public string VoiceLiveModel { get; set; } = "gpt-4o";
        public string SelectedVoiceLiveVoice { get; set; } = "en-US-Ava:DragonHDLatestNeural";

        public static readonly HashSet<string> ValidVoices = new(StringComparer.OrdinalIgnoreCase)
        {
            "alloy", "echo", "fable", "onyx", "nova", "shimmer"
        };

        public static readonly HashSet<string> ValidVoiceLiveModels = new(StringComparer.OrdinalIgnoreCase)
        {
            "gpt-4o", "gpt-4.1", "gpt-5"
        };

        public static readonly HashSet<string> ValidTranscriptionModes = new(StringComparer.OrdinalIgnoreCase)
        {
            "BuiltIn", "SeparateSTT"
        };
    }
}
