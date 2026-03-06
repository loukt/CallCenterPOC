namespace ContactCenterPOC.Models
{
    public enum CallStatus
    {
        Initiating,
        Ringing,
        Connected,
        Disconnected,
        Failed,
        Reconnecting
    }

    public class ActiveCall
    {
        public string CallConnectionId { get; set; } = string.Empty;
        public string? ServerCallId { get; set; }
        public string TargetPhoneNumber { get; set; } = string.Empty;
        public string? CampaignId { get; set; }
        public string? CampaignTitle { get; set; }
        public string? ContactName { get; set; }
        public string Prompt { get; set; } = string.Empty;
        public CallStatus Status { get; set; } = CallStatus.Initiating;
        public DateTimeOffset StartedAt { get; set; } = DateTimeOffset.UtcNow;
        public string? RecordingId { get; set; }
        public List<TranscriptEntry> TranscriptEntries { get; set; } = new();
        public CancellationTokenSource CancellationTokenSource { get; set; } = new();

        // VoiceLive-specific fields (frozen at call start per FR-014)
        public string VoiceApiMode { get; set; } = "ChatGPT";
        public string? VoiceLiveModel { get; set; }
        public string? VoiceLiveVoice { get; set; }
        public int ReconnectAttempts { get; set; } = 0;
    }
}
