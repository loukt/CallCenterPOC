namespace ContactCenterPOC.Models
{
    public class OperatorSettings
    {
        public double MaxCallTimeMinutes { get; set; } = 2.0;
        public string VoiceApiMode { get; set; } = "ChatGPT";  // "ChatGPT" or "VoiceLive" (future)
    }
}
