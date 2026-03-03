namespace ContactCenterPOC.Models
{
    public enum EmotionLabel
    {
        Neutral,
        Happy,
        Frustrated,
        Angry,
        Sad,
        Anxious
    }

    public class EmotionResult
    {
        public EmotionLabel Label { get; set; } = EmotionLabel.Neutral;
        public float Confidence { get; set; } = 0f;
    }
}
