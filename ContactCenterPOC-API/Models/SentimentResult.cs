namespace ContactCenterPOC.Models
{
    public enum SentimentLabel
    {
        Positive,
        Neutral,
        Negative
    }

    public class SentimentResult
    {
        public SentimentLabel Label { get; set; } = SentimentLabel.Neutral;
        public float Confidence { get; set; } = 0f;
    }
}
