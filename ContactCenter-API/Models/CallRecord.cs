namespace ContactCenterPOC.Models
{
    public class CallRecord
    {
        public string CallConnectionId { get; set; } = string.Empty;
        public string? ServerCallId { get; set; }
        public string PhoneNumber { get; set; } = string.Empty;
        public string? CampaignId { get; set; }
        public string? CampaignTitle { get; set; }
        public string? ContactName { get; set; }
        public string Prompt { get; set; } = string.Empty;
        public string? RecordingId { get; set; }
        public string? RecordingTranscript { get; set; }
        public DateTimeOffset? RecordingTranscribedAt { get; set; }
        public TimeSpan Duration { get; set; }
        public SentimentLabel OverallSentiment { get; set; } = SentimentLabel.Neutral;
        public SentimentBreakdown SentimentBreakdown { get; set; } = new();
        public TalkTimeRatio TalkTimeRatio { get; set; } = new();
        public OperatorStyleTraits? OperatorStyleTraits { get; set; }
        public string? CallSummary { get; set; }
        public DateTimeOffset? SummarizedAt { get; set; }
        public List<TranscriptEntry> TranscriptEntries { get; set; } = new();
        public DateTimeOffset StartedAt { get; set; }
        public DateTimeOffset EndedAt { get; set; }

        // VoiceLive-specific fields for call history
        public string VoiceApiMode { get; set; } = "ChatGPT";
        public string? VoiceLiveModel { get; set; }
        public string? VoiceLiveVoice { get; set; }
    }

    public class SentimentBreakdown
    {
        public float PositivePercent { get; set; }
        public float NeutralPercent { get; set; }
        public float NegativePercent { get; set; }
    }

    public class TalkTimeRatio
    {
        public float AiPercent { get; set; }
        public float RecipientPercent { get; set; }
    }

    public class CallHistorySummary
    {
        public string CallConnectionId { get; set; } = string.Empty;
        public string PhoneNumber { get; set; } = string.Empty;
        public string? CampaignTitle { get; set; }
        public string? ContactName { get; set; }
        public string Duration { get; set; } = string.Empty;
        public string OverallSentiment { get; set; } = "Neutral";
        public bool HasRecording { get; set; }
        public DateTimeOffset StartedAt { get; set; }
    }

    public class OperatorStyleTraits
    {
        public float Empathy { get; set; }
        public float Energy { get; set; }
        public DateTimeOffset ComputedAt { get; set; }
    }
}
