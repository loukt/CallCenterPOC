using Newtonsoft.Json;

namespace ContactCenterPOC.Models
{
    public class QualityEvaluation
    {
        [JsonProperty("id")]
        public string Id { get; set; } = Guid.NewGuid().ToString();

        [JsonProperty("callRecordId")]
        public string CallRecordId { get; set; } = string.Empty;

        [JsonProperty("overallScore")]
        public float OverallScore { get; set; }

        [JsonProperty("criterionScores")]
        public List<CriterionScore> CriterionScores { get; set; } = new();

        [JsonProperty("flagged")]
        public bool Flagged { get; set; }

        [JsonProperty("flagReason")]
        public string? FlagReason { get; set; }

        [JsonProperty("evaluatedAt")]
        public DateTimeOffset EvaluatedAt { get; set; } = DateTimeOffset.UtcNow;

        [JsonProperty("entityType")]
        public string EntityType { get; set; } = "QualityEvaluation";
    }
}
