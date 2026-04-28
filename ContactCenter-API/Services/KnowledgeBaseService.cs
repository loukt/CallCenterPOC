using Azure;
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
        private const int MaxProcessingAttempts = 3;

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
                doc.ErrorMessage = null;
                doc.ProcessingProgress = "Extracting text...";
                await _cosmosDb.UpsertAsync(CosmosContainer, doc, doc.Id);

                // Extract text from blob
                var text = await ExecuteWithTransientRetriesAsync("extract document text", () => ExtractTextAsync(doc));
                if (string.IsNullOrWhiteSpace(text))
                {
                    doc.Status = DocumentStatus.Failed;
                    doc.ErrorMessage = "No text could be extracted from document";
                    await _cosmosDb.UpsertAsync(CosmosContainer, doc, doc.Id);
                    return;
                }

                // Chunk text
                var textChunks = ChunkText(text);
                if (textChunks.Count == 0)
                {
                    doc.Status = DocumentStatus.Failed;
                    doc.ErrorMessage = "No indexable text chunks could be created from document";
                    await _cosmosDb.UpsertAsync(CosmosContainer, doc, doc.Id);
                    return;
                }

                // Generate embeddings and create search chunks
                doc.ProcessingProgress = "Generating embeddings...";
                await _cosmosDb.UpsertAsync(CosmosContainer, doc, doc.Id);

                var searchChunks = new List<KnowledgeChunk>();
                var embeddings = await TryGenerateEmbeddingsForIndexingAsync(textChunks, doc.Id);

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
                        ContentVector = KnowledgeChunk.EnsureVector(i < embeddings.Count ? embeddings[i] : null)
                    });
                }

                // Index in AI Search
                doc.ProcessingProgress = "Building search index...";
                await _cosmosDb.UpsertAsync(CosmosContainer, doc, doc.Id);

                await ExecuteWithTransientRetriesAsync("index document chunks", () => _searchService.IndexChunksAsync(searchChunks));

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
                doc.ErrorMessage = TruncateForStatus(ex.Message, 500);
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

        public async Task<KnowledgeDocument?> UpdateDocumentCampaignAsync(string id, string? campaignId)
        {
            var doc = await _cosmosDb.GetAsync<KnowledgeDocument>(CosmosContainer, id, id);
            if (doc == null) return null;

            if (doc.Status != DocumentStatus.Indexed)
            {
                throw new InvalidOperationException("Document campaign can be changed after processing completes.");
            }

            var normalizedCampaignId = string.IsNullOrWhiteSpace(campaignId) ? null : campaignId.Trim();
            if (string.Equals(doc.CampaignId, normalizedCampaignId, StringComparison.Ordinal))
            {
                return doc;
            }

            doc.CampaignId = normalizedCampaignId;
            doc.Status = DocumentStatus.Processing;
            doc.ErrorMessage = null;
            doc.ProcessingProgress = "Updating campaign link...";
            await _cosmosDb.UpsertAsync(CosmosContainer, doc, doc.Id);

            _ = Task.Run(() => ReprocessDocumentForCampaignChangeAsync(doc.Id));

            return doc;
        }

        private async Task ReprocessDocumentForCampaignChangeAsync(string documentId)
        {
            try
            {
                await ExecuteWithTransientRetriesAsync("delete previous document chunks", () => _searchService.DeleteDocumentChunksAsync(documentId));
                await ProcessDocumentAsync(documentId);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to reprocess document {DocumentId} after campaign update", documentId);
                var doc = await _cosmosDb.GetAsync<KnowledgeDocument>(CosmosContainer, documentId, documentId);
                if (doc == null) return;

                doc.Status = DocumentStatus.Failed;
                doc.ErrorMessage = TruncateForStatus(ex.Message, 500);
                doc.ProcessingProgress = null;
                await _cosmosDb.UpsertAsync(CosmosContainer, doc, doc.Id);
            }
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
            for (int pageNumber = 1; pageNumber <= document.NumberOfPages; pageNumber++)
            {
                try
                {
                    var page = document.GetPage(pageNumber);
                    sb.AppendLine(page.Text);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to extract text from PDF page {PageNumber}; continuing with remaining pages", pageNumber);
                }
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
            var apiKey = _configuration["AzureOpenAI:Key"];
            var embeddingDeployment = _configuration["AzureOpenAI:EmbeddingDeployment"] ?? "text-embedding-3-small";

            if (string.IsNullOrEmpty(openAiUri))
            {
                _logger.LogWarning("Azure OpenAI endpoint not configured, skipping embedding generation");
                return results;
            }

            var client = string.IsNullOrWhiteSpace(apiKey)
                ? new AzureOpenAIClient(new Uri(openAiUri), new DefaultAzureCredential())
                : new AzureOpenAIClient(new Uri(openAiUri), new AzureKeyCredential(apiKey));
            var embeddingClient = client.GetEmbeddingClient(embeddingDeployment);

            // Process in batches of 16
            for (int i = 0; i < texts.Count; i += 16)
            {
                var batch = texts.Skip(i).Take(16).ToList();
                var response = await ExecuteWithTransientRetriesAsync(
                    $"generate embeddings batch {i / 16 + 1}",
                    () => embeddingClient.GenerateEmbeddingsAsync(batch));

                foreach (var embedding in response.Value)
                {
                    results.Add(embedding.ToFloats().ToArray());
                }
            }

            return results;
        }

        private async Task<List<float[]>> TryGenerateEmbeddingsForIndexingAsync(List<string> textChunks, string documentId)
        {
            try
            {
                var embeddings = await GenerateEmbeddingsAsync(textChunks);
                if (embeddings.Count != textChunks.Count)
                {
                    _logger.LogWarning(
                        "Generated {EmbeddingCount} embeddings for {ChunkCount} chunks in document {DocumentId}; missing vectors will be indexed as keyword-only chunks",
                        embeddings.Count,
                        textChunks.Count,
                        documentId);
                }

                return embeddings;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(
                    ex,
                    "Embedding generation unavailable for document {DocumentId}; indexing keyword-only chunks instead",
                    documentId);
                return new List<float[]>();
            }
        }

        private async Task ExecuteWithTransientRetriesAsync(string operationName, Func<Task> operation)
        {
            await ExecuteWithTransientRetriesAsync(operationName, async () =>
            {
                await operation();
                return true;
            });
        }

        private async Task<T> ExecuteWithTransientRetriesAsync<T>(string operationName, Func<Task<T>> operation)
        {
            for (int attempt = 1; ; attempt++)
            {
                try
                {
                    return await operation();
                }
                catch (Exception ex) when (attempt < MaxProcessingAttempts && IsTransientProcessingException(ex))
                {
                    var delay = TimeSpan.FromSeconds(Math.Pow(2, attempt));
                    _logger.LogWarning(
                        ex,
                        "Transient failure during {OperationName}; retrying attempt {NextAttempt}/{MaxAttempts} after {DelaySeconds}s",
                        operationName,
                        attempt + 1,
                        MaxProcessingAttempts,
                        delay.TotalSeconds);
                    await Task.Delay(delay);
                }
            }
        }

        internal static bool IsTransientProcessingException(Exception ex)
        {
            if (ex is RequestFailedException requestFailed)
            {
                return requestFailed.Status == 408
                    || requestFailed.Status == 409
                    || requestFailed.Status == 429
                    || requestFailed.Status >= 500;
            }

            if (ex is HttpRequestException || ex is TimeoutException || ex is TaskCanceledException || ex is IOException)
            {
                return true;
            }

            var statusProperty = ex.GetType().GetProperty("Status");
            if (statusProperty?.GetValue(ex) is int status)
            {
                return status == 408 || status == 409 || status == 429 || status >= 500;
            }

            return false;
        }

        private static string TruncateForStatus(string value, int maxLength)
        {
            if (string.IsNullOrEmpty(value) || value.Length <= maxLength) return value;
            return value.Substring(0, maxLength);
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
            var embeddings = await TryGenerateEmbeddingsForIndexingAsync(textChunks, doc.Id);
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
                    ContentVector = KnowledgeChunk.EnsureVector(i < embeddings.Count ? embeddings[i] : null)
                });
            }

            await ExecuteWithTransientRetriesAsync("index generated knowledge document chunks", () => _searchService.IndexChunksAsync(searchChunks));

            doc.ChunkCount = searchChunks.Count;
            doc.Status = DocumentStatus.Indexed;
            await _cosmosDb.UpsertAsync(CosmosContainer, doc, doc.Id);

            return doc;
        }
    }
}
