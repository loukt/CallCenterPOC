using Azure.Search.Documents.Indexes;
using Azure.Search.Documents.Indexes.Models;

namespace ContactCenterPOC.Models
{
    public class KnowledgeChunk
    {
        [SimpleField(IsKey = true)]
        public string Id { get; set; } = string.Empty;

        [SimpleField(IsFilterable = true)]
        public string DocumentId { get; set; } = string.Empty;

        [SearchableField(IsFilterable = true)]
        public string DocumentTitle { get; set; } = string.Empty;

        [SearchableField(AnalyzerName = LexicalAnalyzerName.Values.EnLucene)]
        public string Content { get; set; } = string.Empty;

        [SimpleField(IsFilterable = true, IsSortable = true)]
        public int ChunkIndex { get; set; }

        [SimpleField(IsFilterable = true)]
        public string FileType { get; set; } = string.Empty;

        [SimpleField(IsFilterable = true)]
        public string CampaignId { get; set; } = string.Empty;

        [VectorSearchField(VectorSearchDimensions = 1536, VectorSearchProfileName = "hybrid-profile")]
        public IReadOnlyList<float>? ContentVector { get; set; }
    }
}
