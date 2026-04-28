namespace ContactCenterPOC.Models
{
    public class AudioEmotionResult
    {
        /// <summary>Primary vocal emotion detected from audio</summary>
        public string AudioEmotion { get; set; } = string.Empty;

        /// <summary>Confidence score for the audio emotion (0.0–1.0)</summary>
        public float AudioConfidence { get; set; }

        /// <summary>Text-based overall sentiment (from existing SentimentAnalysisService)</summary>
        public string TextSentiment { get; set; } = string.Empty;

        /// <summary>Merged composite label</summary>
        public string Composite { get; set; } = string.Empty;

        /// <summary>Flags: sentiment-mismatch, escalation-risk, sarcasm-detected</summary>
        public List<string> Flags { get; set; } = new();

        /// <summary>Per-chunk vocal emotion breakdown</summary>
        public List<AudioChunkEmotion>? ChunkEmotions { get; set; }

        /// <summary>When the analysis was completed</summary>
        public DateTimeOffset AnalyzedAt { get; set; } = DateTimeOffset.UtcNow;
    }

    public class AudioChunkEmotion
    {
        public string Speaker { get; set; } = string.Empty;
        public float StartTime { get; set; }
        public float EndTime { get; set; }
        public string VocalEmotion { get; set; } = string.Empty;
        public float VocalConfidence { get; set; }
    }
}
