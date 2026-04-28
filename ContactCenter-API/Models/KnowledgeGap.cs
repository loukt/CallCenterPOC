using Newtonsoft.Json;

namespace ContactCenterPOC.Models
{
    public class KnowledgeGap
    {
        [JsonProperty("id")]
        public string Id { get; set; } = Guid.NewGuid().ToString();

        [JsonProperty("topic")]
        public string Topic { get; set; } = string.Empty;

        [JsonProperty("frequency")]
        public int Frequency { get; set; }

        [JsonProperty("sampleQuestions")]
        public List<string> SampleQuestions { get; set; } = new();

        [JsonProperty("suggestedArticle")]
        public string? SuggestedArticle { get; set; }

        [JsonProperty("suggestedTitle")]
        public string? SuggestedTitle { get; set; }

        [JsonProperty("status")]
        public KnowledgeGapStatus Status { get; set; } = KnowledgeGapStatus.Identified;

        [JsonProperty("lastOccurrence")]
        public DateTimeOffset LastOccurrence { get; set; }

        [JsonProperty("linkedCallRecordIds")]
        public List<string> LinkedCallRecordIds { get; set; } = new();

        [JsonProperty("createdAt")]
        public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

        [JsonProperty("entityType")]
        public string EntityType { get; set; } = "KnowledgeGap";
    }

    public enum KnowledgeGapStatus { Identified, ArticleDrafted, Published, Dismissed }
}
