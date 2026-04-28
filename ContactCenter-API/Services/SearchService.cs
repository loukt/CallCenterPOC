using Azure;
using Azure.Identity;
using Azure.Search.Documents;
using Azure.Search.Documents.Indexes;
using Azure.Search.Documents.Indexes.Models;
using Azure.Search.Documents.Models;
using ContactCenterPOC.Models;

namespace ContactCenterPOC.Services
{
    public class SearchService
    {
        private readonly SearchIndexClient? _indexClient;
        private readonly IConfiguration _configuration;
        private readonly ILogger<SearchService> _logger;
        private readonly string _indexName;
        private SearchClient? _searchClient;
        private bool _indexEnsured = false;

        public SearchService(IConfiguration configuration, ILogger<SearchService> logger, SearchIndexClient? indexClient = null)
        {
            _indexClient = indexClient;
            _configuration = configuration;
            _logger = logger;
            _indexName = configuration["AzureAISearch:IndexName"] ?? "knowledge-base-index";
        }

        private async Task EnsureIndexAsync()
        {
            if (_indexEnsured || _indexClient == null) return;

            try
            {
                var vectorSearch = new VectorSearch();
                vectorSearch.Algorithms.Add(new HnswAlgorithmConfiguration("hnsw-config")
                {
                    Parameters = new HnswParameters { Metric = VectorSearchAlgorithmMetric.Cosine }
                });
                vectorSearch.Profiles.Add(new VectorSearchProfile("hybrid-profile", "hnsw-config"));

                var index = new SearchIndex(_indexName)
                {
                    VectorSearch = vectorSearch,
                    Fields = new FieldBuilder().Build(typeof(KnowledgeChunk))
                };

                await _indexClient.CreateOrUpdateIndexAsync(index);
                _searchClient = _indexClient.GetSearchClient(_indexName);
                _indexEnsured = true;
                _logger.LogInformation("AI Search index '{IndexName}' ensured", _indexName);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to ensure AI Search index");
            }
        }

        public async Task IndexChunksAsync(List<KnowledgeChunk> chunks)
        {
            await EnsureIndexAsync();
            if (_searchClient == null) return;

            var batch = IndexDocumentsBatch.Upload(chunks);
            await _searchClient.IndexDocumentsAsync(batch);
            _logger.LogInformation("Indexed {Count} chunks", chunks.Count);
        }

        public async Task DeleteDocumentChunksAsync(string documentId)
        {
            await EnsureIndexAsync();
            if (_searchClient == null) return;

            var searchOptions = new SearchOptions
            {
                Filter = $"DocumentId eq '{documentId}'",
                Select = { "Id" },
                Size = 1000
            };

            var results = await _searchClient.SearchAsync<KnowledgeChunk>("*", searchOptions);
            var idsToDelete = new List<string>();
            await foreach (var result in results.Value.GetResultsAsync())
            {
                idsToDelete.Add(result.Document.Id);
            }

            if (idsToDelete.Count > 0)
            {
                var batch = IndexDocumentsBatch.Delete("Id", idsToDelete);
                await _searchClient.IndexDocumentsAsync(batch);
                _logger.LogInformation("Deleted {Count} chunks for document {DocumentId}", idsToDelete.Count, documentId);
            }
        }

        public async Task<List<SearchResultItem>> HybridSearchAsync(string query, float[]? queryVector = null, int top = 5, string? campaignId = null)
        {
            await EnsureIndexAsync();
            if (_searchClient == null) return new List<SearchResultItem>();

            var searchOptions = new SearchOptions
            {
                Size = top,
                Select = { "Id", "DocumentId", "DocumentTitle", "Content", "ChunkIndex" }
            };

            if (!string.IsNullOrEmpty(campaignId))
            {
                searchOptions.Filter = $"CampaignId eq '{campaignId}' or CampaignId eq ''";
            }

            if (queryVector != null)
            {
                searchOptions.VectorSearch = new VectorSearchOptions
                {
                    Queries = { new VectorizedQuery(queryVector) { KNearestNeighborsCount = top, Fields = { "ContentVector" } } }
                };
            }

            var results = await _searchClient.SearchAsync<KnowledgeChunk>(query, searchOptions);
            var items = new List<SearchResultItem>();

            await foreach (var result in results.Value.GetResultsAsync())
            {
                items.Add(new SearchResultItem
                {
                    DocumentId = result.Document.DocumentId,
                    DocumentTitle = result.Document.DocumentTitle,
                    Content = result.Document.Content,
                    Score = (float)(result.Score ?? 0),
                    ChunkIndex = result.Document.ChunkIndex
                });
            }

            return items;
        }
    }

    public class SearchResultItem
    {
        public string DocumentId { get; set; } = string.Empty;
        public string DocumentTitle { get; set; } = string.Empty;
        public string Content { get; set; } = string.Empty;
        public float Score { get; set; }
        public int ChunkIndex { get; set; }
    }
}
