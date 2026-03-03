namespace ContactCenterPOC.Models
{
    public enum SpeakerType
    {
        AI,
        Recipient
    }

    public class TranscriptEntry
    {
        public string CallConnectionId { get; set; } = string.Empty;
        public SpeakerType Speaker { get; set; }
        public string Text { get; set; } = string.Empty;
        public SentimentResult Sentiment { get; set; } = new SentimentResult();
        public EmotionResult? Emotion { get; set; }
        public DateTimeOffset Timestamp { get; set; } = DateTimeOffset.UtcNow;
    }
}
