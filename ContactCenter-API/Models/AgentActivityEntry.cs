using Newtonsoft.Json;

namespace ContactCenterPOC.Models
{
    public class AgentActivityEntry
    {
        [JsonProperty("id")]
        public string Id { get; set; } = Guid.NewGuid().ToString();

        [JsonProperty("agentName")]
        public string AgentName { get; set; } = string.Empty;

        [JsonProperty("actionType")]
        public string ActionType { get; set; } = string.Empty;

        [JsonProperty("callRecordId")]
        public string? CallRecordId { get; set; }

        [JsonProperty("result")]
        public AgentActionResult Result { get; set; }

        [JsonProperty("resultDetail")]
        public string? ResultDetail { get; set; }

        [JsonProperty("durationMs")]
        public long DurationMs { get; set; }

        [JsonProperty("timestamp")]
        public DateTimeOffset Timestamp { get; set; } = DateTimeOffset.UtcNow;

        [JsonProperty("entityType")]
        public string EntityType { get; set; } = "AgentActivityEntry";
    }

    public enum AgentActionResult { Success, Failure, Skipped }
}
