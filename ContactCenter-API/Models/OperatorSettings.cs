namespace ContactCenterPOC.Models
{
    public class OperatorSettings
    {
        public double MaxCallTimeMinutes { get; set; } = 2.0;
        public string VoiceApiMode { get; set; } = "ChatGPT";  // "ChatGPT" or "VoiceLive" (future)
        public string SelectedVoice { get; set; } = "alloy";   // alloy, echo, fable, onyx, nova, shimmer

        public static readonly HashSet<string> ValidVoices = new(StringComparer.OrdinalIgnoreCase)
        {
            "alloy", "echo", "fable", "onyx", "nova", "shimmer"
        };
    }
}
