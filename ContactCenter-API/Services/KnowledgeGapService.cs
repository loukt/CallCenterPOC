using Azure.AI.OpenAI;
using Azure.Identity;
using ContactCenterPOC.Models;
using OpenAI.Chat;
using System.Text.Json;

namespace ContactCenterPOC.Services
{
    public class KnowledgeGapService
    {
        private readonly CosmosDbService _cosmosDb;
        private readonly KnowledgeBaseService _knowledgeBaseService;
        private readonly AgentActivityService _agentActivityService;
        private readonly IConfiguration _configuration;
        private readonly ILogger<KnowledgeGapService> _logger;
        private const string ContainerName = "KnowledgeGaps";

        public KnowledgeGapService(
            CosmosDbService cosmosDb, KnowledgeBaseService knowledgeBaseService,
            AgentActivityService agentActivityService,
            IConfiguration configuration, ILogger<KnowledgeGapService> logger)
        {
            _cosmosDb = cosmosDb;
            _knowledgeBaseService = knowledgeBaseService;
            _agentActivityService = agentActivityService;
            _configuration = configuration;
            _logger = logger;
        }

        public async Task<List<KnowledgeGap>> ListGapsAsync(string? status = null, int limit = 50)
        {
            var conditions = new List<string> { "1=1" };
            var parameters = new Dictionary<string, object>();

            if (!string.IsNullOrEmpty(status) && Enum.TryParse<KnowledgeGapStatus>(status, true, out var statusEnum))
            {
                conditions.Add("c.status = @status");
                parameters["@status"] = (int)statusEnum;
            }

            var query = $"SELECT TOP {limit} * FROM c WHERE {string.Join(" AND ", conditions)} ORDER BY c.frequency DESC";
            return await _cosmosDb.QueryAsync<KnowledgeGap>(ContainerName, query, parameters);
        }

        public async Task<KnowledgeGap?> GetGapAsync(string id)
        {
            var results = await _cosmosDb.QueryAsync<KnowledgeGap>(ContainerName,
                "SELECT * FROM c WHERE c.id = @id",
                new Dictionary<string, object> { ["@id"] = id });
            return results.FirstOrDefault();
        }

        public async Task<KnowledgeGap?> PublishGapAsync(string id)
        {
            var gap = await GetGapAsync(id);
            if (gap == null) return null;

            if (string.IsNullOrEmpty(gap.SuggestedArticle))
                throw new InvalidOperationException("No suggested article to publish");

            // Publish as a KB document
            await _knowledgeBaseService.IndexTextAsDocumentAsync(
                gap.SuggestedTitle ?? gap.Topic, gap.SuggestedArticle);

            gap.Status = KnowledgeGapStatus.Published;
            await _cosmosDb.UpsertAsync(ContainerName, gap, gap.Topic);

            await _agentActivityService.LogActivityAsync(
                "KnowledgeManagement", "GapPublished", AgentActionResult.Success,
                resultDetail: $"Published article for '{gap.Topic}'");

            return gap;
        }

        public async Task<KnowledgeGap?> DismissGapAsync(string id)
        {
            var gap = await GetGapAsync(id);
            if (gap == null) return null;

            gap.Status = KnowledgeGapStatus.Dismissed;
            return await _cosmosDb.UpsertAsync(ContainerName, gap, gap.Topic);
        }

        public async Task<KnowledgeGap> CreateGapAsync(KnowledgeGap gap)
        {
            gap.CreatedAt = DateTimeOffset.UtcNow;
            gap.LastOccurrence = DateTimeOffset.UtcNow;
            return await _cosmosDb.UpsertAsync(ContainerName, gap, gap.Topic);
        }

        public async Task<KnowledgeGap> UpdateGapAsync(KnowledgeGap gap)
        {
            return await _cosmosDb.UpsertAsync(ContainerName, gap, gap.Topic);
        }

        public async Task DetectGapsAsync(CallRecord callRecord)
        {
            await _agentActivityService.ExecuteWithLoggingAsync("KnowledgeManagement", "GapDetected", callRecord.CallConnectionId, async () =>
            {
                var transcript = string.Join("\n", callRecord.TranscriptEntries?.Select(
                    e => $"{e.Speaker}: {e.Text}") ?? Enumerable.Empty<string>());

                if (string.IsNullOrWhiteSpace(transcript)) return 0;

                var prompt = @"Analyze this call transcript and identify any questions the AI agent could NOT answer or answered poorly.
For each knowledge gap found, provide:
- topic: The general topic (e.g., ""Return Policy"", ""Product Warranty"")
- question: The specific customer question
- suggestedTitle: A title for a knowledge base article
- suggestedArticle: A draft article in markdown format that would answer the question

Only include genuine gaps where the AI did not have the information.
If no gaps are found, return an empty array.

Return JSON array: [{""topic"":"""",""question"":"""",""suggestedTitle"":"""",""suggestedArticle"":""""}]

Transcript:
" + (transcript.Length > 4000 ? transcript[..4000] : transcript);

                var openAiUri = _configuration["AzureOpenAI:EndpointUri"];
                var chatDeployment = _configuration["AzureOpenAI:ChatDeployment"] ?? "gpt-4o-mini";
                if (string.IsNullOrEmpty(openAiUri)) return 0;

                var client = new AzureOpenAIClient(new Uri(openAiUri), new DefaultAzureCredential());
                var chatClient = client.GetChatClient(chatDeployment);
                var response = await chatClient.CompleteChatAsync(
                    new List<ChatMessage> { new SystemChatMessage(prompt) },
                    new ChatCompletionOptions());

                var responseText = response.Value.Content[0].Text;
                var jsonStart = responseText.IndexOf('[');
                var jsonEnd = responseText.LastIndexOf(']');
                if (jsonStart < 0 || jsonEnd < 0) return 0;

                var json = responseText[jsonStart..(jsonEnd + 1)];
                var gaps = JsonSerializer.Deserialize<List<DetectedGap>>(json,
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? new();

                int newCount = 0;
                foreach (var detectedGap in gaps)
                {
                    // Check for existing gap on same topic
                    var existingGaps = await _cosmosDb.QueryAsync<KnowledgeGap>(ContainerName,
                        "SELECT * FROM c WHERE c.topic = @topic AND (c.status = 0 OR c.status = 1)",
                        new Dictionary<string, object> { ["@topic"] = detectedGap.Topic });

                    if (existingGaps.Count > 0)
                    {
                        // Update frequency
                        var existing = existingGaps.First();
                        existing.Frequency++;
                        existing.LastOccurrence = DateTimeOffset.UtcNow;
                        existing.LinkedCallRecordIds.Add(callRecord.CallConnectionId);
                        if (!existing.SampleQuestions.Contains(detectedGap.Question))
                            existing.SampleQuestions.Add(detectedGap.Question);
                        if (!string.IsNullOrEmpty(detectedGap.SuggestedArticle))
                        {
                            existing.SuggestedArticle = detectedGap.SuggestedArticle;
                            existing.SuggestedTitle = detectedGap.SuggestedTitle;
                            existing.Status = KnowledgeGapStatus.ArticleDrafted;
                        }
                        await _cosmosDb.UpsertAsync(ContainerName, existing, existing.Topic);
                    }
                    else
                    {
                        // Create new gap
                        var gap = new KnowledgeGap
                        {
                            Topic = detectedGap.Topic,
                            Frequency = 1,
                            SampleQuestions = new List<string> { detectedGap.Question },
                            SuggestedArticle = detectedGap.SuggestedArticle,
                            SuggestedTitle = detectedGap.SuggestedTitle,
                            Status = string.IsNullOrEmpty(detectedGap.SuggestedArticle)
                                ? KnowledgeGapStatus.Identified
                                : KnowledgeGapStatus.ArticleDrafted,
                            LastOccurrence = DateTimeOffset.UtcNow,
                            LinkedCallRecordIds = new List<string> { callRecord.CallConnectionId }
                        };
                        await _cosmosDb.UpsertAsync(ContainerName, gap, gap.Topic);
                        newCount++;
                    }
                }

                _logger.LogInformation("Knowledge gap detection: {NewCount} new gaps from call {CallId}",
                    newCount, callRecord.CallConnectionId);
                return newCount;
            });
        }

        private class DetectedGap
        {
            public string Topic { get; set; } = string.Empty;
            public string Question { get; set; } = string.Empty;
            public string? SuggestedTitle { get; set; }
            public string? SuggestedArticle { get; set; }
        }
    }
}
