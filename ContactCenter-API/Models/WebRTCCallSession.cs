using Newtonsoft.Json;

namespace ContactCenterPOC.Models
{
    public class WebRTCCallSession
    {
        [JsonProperty("id")]
        public string SessionId { get; set; } = Guid.NewGuid().ToString();

        [JsonProperty("sessionId")]
        public string SessionIdPartition => SessionId;

        [JsonProperty("shareableLink")]
        public string ShareableLink { get; set; } = string.Empty;

        [JsonProperty("expiresAt")]
        public DateTimeOffset ExpiresAt { get; set; }

        [JsonProperty("isUsed")]
        public bool IsUsed { get; set; }

        [JsonProperty("status")]
        public WebRTCSessionStatus Status { get; set; } = WebRTCSessionStatus.Waiting;

        [JsonProperty("callerName")]
        public string? CallerName { get; set; }

        [JsonProperty("callerEmail")]
        public string? CallerEmail { get; set; }

        [JsonProperty("callerPhone")]
        public string? CallerPhone { get; set; }

        [JsonProperty("callerConnectionId")]
        public string? CallerConnectionId { get; set; }

        [JsonProperty("operatorConnectionId")]
        public string OperatorConnectionId { get; set; } = string.Empty;

        [JsonProperty("campaignId")]
        public string? CampaignId { get; set; }

        [JsonProperty("createdAt")]
        public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

        [JsonProperty("entityType")]
        public string EntityType { get; set; } = "WebRTCCallSession";
    }

    public enum WebRTCSessionStatus { Waiting, Connected, Disconnected }
}
