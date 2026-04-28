using Newtonsoft.Json;

namespace ContactCenterPOC.Models
{
    public class KnowledgeDocument
    {
        [JsonProperty("id")]
        public string Id { get; set; } = Guid.NewGuid().ToString();

        [JsonProperty("fileName")]
        public string FileName { get; set; } = string.Empty;

        [JsonProperty("fileType")]
        public string FileType { get; set; } = string.Empty;

        [JsonProperty("fileSizeBytes")]
        public long FileSizeBytes { get; set; }

        [JsonProperty("status")]
        public DocumentStatus Status { get; set; } = DocumentStatus.Uploading;

        [JsonProperty("chunkCount")]
        public int ChunkCount { get; set; }

        [JsonProperty("indexName")]
        public string IndexName { get; set; } = "knowledge-base-index";

        [JsonProperty("blobUri")]
        public string BlobUri { get; set; } = string.Empty;

        [JsonProperty("uploadedBy")]
        public string UploadedBy { get; set; } = "operator";

        [JsonProperty("uploadedAt")]
        public DateTimeOffset UploadedAt { get; set; } = DateTimeOffset.UtcNow;

        [JsonProperty("errorMessage")]
        public string? ErrorMessage { get; set; }

        [JsonProperty("campaignId")]
        public string? CampaignId { get; set; }

        [JsonProperty("processingProgress")]
        public string? ProcessingProgress { get; set; }

        [JsonProperty("entityType")]
        public string EntityType { get; set; } = "KnowledgeDocument";
    }

    public enum DocumentStatus { Uploading, Processing, Indexed, Failed }
}
