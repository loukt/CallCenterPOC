using Newtonsoft.Json;

namespace ContactCenterPOC.Models
{
    public class Case
    {
        [JsonProperty("id")]
        public string Id { get; set; } = Guid.NewGuid().ToString();

        [JsonProperty("callerPhoneNumber")]
        public string CallerPhoneNumber { get; set; } = string.Empty;

        [JsonProperty("callerName")]
        public string? CallerName { get; set; }

        [JsonProperty("callerEmail")]
        public string? CallerEmail { get; set; }

        [JsonProperty("title")]
        public string Title { get; set; } = string.Empty;

        [JsonProperty("description")]
        public string Description { get; set; } = string.Empty;

        [JsonProperty("status")]
        public CaseStatus Status { get; set; } = CaseStatus.Open;

        [JsonProperty("priority")]
        public CasePriority Priority { get; set; } = CasePriority.Medium;

        [JsonProperty("linkedCallRecords")]
        public List<string> LinkedCallRecords { get; set; } = new();

        [JsonProperty("intent")]
        public string? Intent { get; set; }

        [JsonProperty("resolutionSummary")]
        public string? ResolutionSummary { get; set; }

        [JsonProperty("callSource")]
        public string CallSource { get; set; } = "Phone";

        [JsonProperty("campaignId")]
        public string? CampaignId { get; set; }

        [JsonProperty("createdAt")]
        public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

        [JsonProperty("updatedAt")]
        public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;

        [JsonProperty("resolvedAt")]
        public DateTimeOffset? ResolvedAt { get; set; }

        [JsonProperty("closedAt")]
        public DateTimeOffset? ClosedAt { get; set; }

        [JsonProperty("entityType")]
        public string EntityType { get; set; } = "Case";
    }

    public enum CaseStatus { Open, InProgress, Resolved, Closed }
    public enum CasePriority { Low, Medium, High, Critical, Escalation }
}
