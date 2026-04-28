using Azure.AI.OpenAI;
using Azure.Identity;
using Azure.Storage.Blobs;
using ContactCenterPOC.Models;
using DocumentFormat.OpenXml.Packaging;
using OpenAI.Embeddings;
using System.Text;
using UglyToad.PdfPig;

namespace ContactCenterPOC.Services
{
    public class KnowledgeBaseService
    {
        private readonly CosmosDbService _cosmosDb;
        private readonly SearchService _searchService;
        private readonly BlobServiceClient _blobServiceClient;
        private readonly IConfiguration _configuration;
        private readonly ILogger<KnowledgeBaseService> _logger;
        private readonly AgentActivityService _agentActivityService;
        private readonly string _containerName;
        private const string CosmosContainer = "KnowledgeDocuments";
        private const string BlobFolder = "knowledge-documents";
        private const long MaxFileSizeBytes = 50 * 1024 * 1024; // 50MB
        private static readonly HashSet<string> SupportedTypes = new(StringComparer.OrdinalIgnoreCase) { ".pdf", ".docx", ".txt" };
        private const int ChunkSizeChars = 4000; // ~1024 tokens
        private const int OverlapChars = 800; // ~20% overlap

        public KnowledgeBaseService(
            CosmosDbService cosmosDb, SearchService searchService,
            BlobServiceClient blobServiceClient, IConfiguration configuration,
            ILogger<KnowledgeBaseService> logger, AgentActivityService agentActivityService)
        {
            _cosmosDb = cosmosDb;
            _searchService = searchService;
            _blobServiceClient = blobServiceClient;
            _configuration = configuration;
            _logger = logger;
            _agentActivityService = agentActivityService;
            _containerName = configuration["BlobStorage:ContainerName"] ?? "callcenter-data";
        }

        public async Task<List<KnowledgeDocument>> ListDocumentsAsync(string? campaignId = null)
        {
            if (!string.IsNullOrEmpty(campaignId))
            {
                return await _cosmosDb.QueryAsync<KnowledgeDocument>(CosmosContainer,
                    $"SELECT * FROM c WHERE c.campaignId = '{campaignId}' OR NOT IS_DEFINED(c.campaignId) OR c.campaignId = null ORDER BY c.uploadedAt DESC");
            }
            return await _cosmosDb.QueryAsync<KnowledgeDocument>(CosmosContainer,
                "SELECT * FROM c ORDER BY c.uploadedAt DESC");
        }

        public async Task<KnowledgeDocument?> GetDocumentAsync(string id)
        {
            return await _cosmosDb.GetAsync<KnowledgeDocument>(CosmosContainer, id, id);
        }

        public async Task<KnowledgeDocument> UploadDocumentAsync(Stream fileStream, string fileName, long fileSize, string? campaignId = null)
        {
            var extension = Path.GetExtension(fileName).ToLowerInvariant();
            if (!SupportedTypes.Contains(extension))
                throw new ArgumentException($"Unsupported file type: {extension}. Supported: PDF, DOCX, TXT");

            if (fileSize > MaxFileSizeBytes)
                throw new ArgumentException($"File size exceeds maximum of {MaxFileSizeBytes / (1024 * 1024)}MB");

            var doc = new KnowledgeDocument
            {
                FileName = fileName,
                FileType = extension.TrimStart('.').ToUpperInvariant(),
                FileSizeBytes = fileSize,
                Status = DocumentStatus.Uploading,
                ProcessingProgress = "Uploading...",
                CampaignId = string.IsNullOrWhiteSpace(campaignId) ? null : campaignId
            };

            // Upload to blob storage
            var blobContainerClient = _blobServiceClient.GetBlobContainerClient(_containerName);
            await blobContainerClient.CreateIfNotExistsAsync();
            var blobName = $"{BlobFolder}/{doc.Id}/{fileName}";
            var blobClient = blobContainerClient.GetBlobClient(blobName);
            await blobClient.UploadAsync(fileStream, overwrite: true);
            doc.BlobUri = blobClient.Uri.ToString();

            // Save to Cosmos DB
            await _cosmosDb.UpsertAsync(CosmosContainer, doc, doc.Id);
            _logger.LogInformation("Document '{FileName}' uploaded: {Id}", fileName, doc.Id);

            // Process async (fire-and-forget)
            _ = Task.Run(async () => await ProcessDocumentAsync(doc.Id));

            return doc;
        }

        public async Task ProcessDocumentAsync(string documentId)
        {
            var doc = await _cosmosDb.GetAsync<KnowledgeDocument>(CosmosContainer, documentId, documentId);
            if (doc == null) return;

            try
            {
                doc.Status = DocumentStatus.Processing;
                doc.ProcessingProgress = "Extracting text...";
                await _cosmosDb.UpsertAsync(CosmosContainer, doc, doc.Id);

                // Extract text from blob
                var text = await ExtractTextAsync(doc);
                if (string.IsNullOrWhiteSpace(text))
                {
                    doc.Status = DocumentStatus.Failed;
                    doc.ErrorMessage = "No text could be extracted from document";
                    await _cosmosDb.UpsertAsync(CosmosContainer, doc, doc.Id);
                    return;
                }

                // Chunk text
                var textChunks = ChunkText(text);

                // Generate embeddings and create search chunks
                doc.ProcessingProgress = "Generating embeddings...";
                await _cosmosDb.UpsertAsync(CosmosContainer, doc, doc.Id);

                var searchChunks = new List<KnowledgeChunk>();
                var embeddings = await GenerateEmbeddingsAsync(textChunks);

                for (int i = 0; i < textChunks.Count; i++)
                {
                    searchChunks.Add(new KnowledgeChunk
                    {
                        Id = $"{doc.Id}-{i}",
                        DocumentId = doc.Id,
                        DocumentTitle = doc.FileName,
                        Content = textChunks[i],
                        ChunkIndex = i,
                        FileType = doc.FileType,
                        CampaignId = doc.CampaignId ?? string.Empty,
                        ContentVector = i < embeddings.Count ? embeddings[i] : null
                    });
                }

                // Index in AI Search
                doc.ProcessingProgress = "Building search index...";
                await _cosmosDb.UpsertAsync(CosmosContainer, doc, doc.Id);

                await _searchService.IndexChunksAsync(searchChunks);

                doc.ChunkCount = searchChunks.Count;
                doc.Status = DocumentStatus.Indexed;
                doc.ErrorMessage = null;
                doc.ProcessingProgress = null;
                await _cosmosDb.UpsertAsync(CosmosContainer, doc, doc.Id);

                await _agentActivityService.LogActivityAsync(
                    "KnowledgeManagement", "DocumentIndexed", AgentActionResult.Success,
                    resultDetail: $"Indexed {searchChunks.Count} chunks from '{doc.FileName}'");

                _logger.LogInformation("Document '{FileName}' processed: {ChunkCount} chunks", doc.FileName, searchChunks.Count);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to process document {Id}", documentId);
                doc.Status = DocumentStatus.Failed;
                doc.ErrorMessage = ex.Message;
                doc.ProcessingProgress = null;
                await _cosmosDb.UpsertAsync(CosmosContainer, doc, doc.Id);

                await _agentActivityService.LogActivityAsync(
                    "KnowledgeManagement", "DocumentIndexed", AgentActionResult.Failure,
                    resultDetail: ex.Message);
            }
        }

        public async Task DeleteDocumentAsync(string id)
        {
            var doc = await _cosmosDb.GetAsync<KnowledgeDocument>(CosmosContainer, id, id);
            if (doc == null) return;

            // Delete from AI Search
            await _searchService.DeleteDocumentChunksAsync(id);

            // Delete from Blob
            try
            {
                var blobContainerClient = _blobServiceClient.GetBlobContainerClient(_containerName);
                var blobName = $"{BlobFolder}/{id}/{doc.FileName}";
                var blobClient = blobContainerClient.GetBlobClient(blobName);
                await blobClient.DeleteIfExistsAsync();
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to delete blob for document {Id}", id);
            }

            // Delete from Cosmos
            await _cosmosDb.DeleteAsync(CosmosContainer, id, id);
            _logger.LogInformation("Document '{FileName}' deleted", doc.FileName);
        }

        public async Task<List<SearchResultItem>> SearchAsync(string query, int top = 5, string? campaignId = null)
        {
            float[]? vector = null;
            try
            {
                var embeddings = await GenerateEmbeddingsAsync(new List<string> { query });
                if (embeddings.Count > 0)
                    vector = embeddings[0];
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to generate query embedding, falling back to keyword-only search");
            }

            return await _searchService.HybridSearchAsync(query, vector, top, campaignId);
        }

        private async Task<string> ExtractTextAsync(KnowledgeDocument doc)
        {
            var blobContainerClient = _blobServiceClient.GetBlobContainerClient(_containerName);
            var blobName = $"{BlobFolder}/{doc.Id}/{doc.FileName}";
            var blobClient = blobContainerClient.GetBlobClient(blobName);

            using var stream = new MemoryStream();
            await blobClient.DownloadToAsync(stream);
            stream.Position = 0;

            return doc.FileType.ToUpperInvariant() switch
            {
                "PDF" => ExtractFromPdf(stream),
                "DOCX" => ExtractFromDocx(stream),
                "TXT" => await ExtractFromTxt(stream),
                _ => throw new NotSupportedException($"Unsupported file type: {doc.FileType}")
            };
        }

        private string ExtractFromPdf(Stream stream)
        {
            var sb = new StringBuilder();
            using var document = PdfDocument.Open(stream);
            foreach (var page in document.GetPages())
            {
                sb.AppendLine(page.Text);
            }
            return sb.ToString();
        }

        private string ExtractFromDocx(Stream stream)
        {
            var sb = new StringBuilder();
            using var doc = WordprocessingDocument.Open(stream, false);
            var body = doc.MainDocumentPart?.Document?.Body;
            if (body != null)
            {
                sb.Append(body.InnerText);
            }
            return sb.ToString();
        }

        private async Task<string> ExtractFromTxt(Stream stream)
        {
            using var reader = new StreamReader(stream, Encoding.UTF8);
            return await reader.ReadToEndAsync();
        }

        internal List<string> ChunkText(string text)
        {
            var chunks = new List<string>();
            if (string.IsNullOrWhiteSpace(text)) return chunks;

            int start = 0;
            while (start < text.Length)
            {
                int length = Math.Min(ChunkSizeChars, text.Length - start);
                chunks.Add(text.Substring(start, length));
                start += ChunkSizeChars - OverlapChars;
                if (start + OverlapChars >= text.Length) break;
            }

            return chunks;
        }

        private async Task<List<float[]>> GenerateEmbeddingsAsync(List<string> texts)
        {
            var results = new List<float[]>();
            var openAiUri = _configuration["AzureOpenAI:EndpointUri"];
            var embeddingDeployment = _configuration["AzureOpenAI:EmbeddingDeployment"] ?? "text-embedding-3-small";

            if (string.IsNullOrEmpty(openAiUri))
            {
                _logger.LogWarning("Azure OpenAI endpoint not configured, skipping embedding generation");
                return results;
            }

            var client = new AzureOpenAIClient(new Uri(openAiUri), new DefaultAzureCredential());
            var embeddingClient = client.GetEmbeddingClient(embeddingDeployment);

            // Process in batches of 16
            for (int i = 0; i < texts.Count; i += 16)
            {
                var batch = texts.Skip(i).Take(16).ToList();
                var response = await embeddingClient.GenerateEmbeddingsAsync(batch);

                foreach (var embedding in response.Value)
                {
                    results.Add(embedding.ToFloats().ToArray());
                }
            }

            return results;
        }

        // Called by knowledge gap publish workflow
        public async Task<KnowledgeDocument> IndexTextAsDocumentAsync(string title, string content)
        {
            var doc = new KnowledgeDocument
            {
                FileName = title,
                FileType = "TXT",
                FileSizeBytes = Encoding.UTF8.GetByteCount(content),
                Status = DocumentStatus.Processing,
                BlobUri = "generated-from-knowledge-gap"
            };

            await _cosmosDb.UpsertAsync(CosmosContainer, doc, doc.Id);

            var textChunks = ChunkText(content);
            var embeddings = await GenerateEmbeddingsAsync(textChunks);
            var searchChunks = new List<KnowledgeChunk>();

            for (int i = 0; i < textChunks.Count; i++)
            {
                searchChunks.Add(new KnowledgeChunk
                {
                    Id = $"{doc.Id}-{i}",
                    DocumentId = doc.Id,
                    DocumentTitle = title,
                    Content = textChunks[i],
                    ChunkIndex = i,
                    FileType = "TXT",
                    CampaignId = string.Empty,
                    ContentVector = i < embeddings.Count ? embeddings[i] : null
                });
            }

            await _searchService.IndexChunksAsync(searchChunks);

            doc.ChunkCount = searchChunks.Count;
            doc.Status = DocumentStatus.Indexed;
            await _cosmosDb.UpsertAsync(CosmosContainer, doc, doc.Id);

            return doc;
        }
    }
}
