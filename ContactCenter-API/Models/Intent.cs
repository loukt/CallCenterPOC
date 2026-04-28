using Newtonsoft.Json;

namespace ContactCenterPOC.Models
{
    public class Intent
    {
        [JsonProperty("id")]
        public string Id { get; set; } = Guid.NewGuid().ToString();

        [JsonProperty("name")]
        public string Name { get; set; } = string.Empty;

        [JsonProperty("groupName")]
        public string GroupName { get; set; } = string.Empty;

        [JsonProperty("description")]
        public string Description { get; set; } = string.Empty;

        [JsonProperty("status")]
        public IntentStatus Status { get; set; } = IntentStatus.Pending;

        [JsonProperty("frequency")]
        public int Frequency { get; set; }

        [JsonProperty("sampleUtterances")]
        public List<string> SampleUtterances { get; set; } = new();

        [JsonProperty("linkedKnowledgeArticles")]
        public List<string> LinkedKnowledgeArticles { get; set; } = new();

        [JsonProperty("createdAt")]
        public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

        [JsonProperty("entityType")]
        public string EntityType { get; set; } = "Intent";
    }

    public enum IntentStatus { Pending, Approved, Discarded }
}
