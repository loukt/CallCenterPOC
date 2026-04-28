using Azure.AI.OpenAI;
using Azure.Identity;
using ContactCenterPOC.Models;
using OpenAI.Chat;
using System.Text.Json;

namespace ContactCenterPOC.Services
{
    public class QualityEvaluationService
    {
        private readonly CosmosDbService _cosmosDb;
        private readonly SettingsService _settingsService;
        private readonly AgentActivityService _agentActivityService;
        private readonly IConfiguration _configuration;
        private readonly ILogger<QualityEvaluationService> _logger;
        private const string ContainerName = "QualityEvaluations";

        private static readonly List<QualityCriterion> DefaultCriteria = new()
        {
            new() { Name = "Greeting Quality", Description = "How well did the agent greet the customer?", Weight = 5, MinimumPassingScore = 3.0f },
            new() { Name = "Issue Resolution", Description = "Was the customer's issue addressed effectively?", Weight = 8, MinimumPassingScore = 3.5f },
            new() { Name = "Communication Clarity", Description = "Was the agent's communication clear and professional?", Weight = 5, MinimumPassingScore = 3.0f }
        };

        public QualityEvaluationService(
            CosmosDbService cosmosDb, SettingsService settingsService,
            AgentActivityService agentActivityService,
            IConfiguration configuration, ILogger<QualityEvaluationService> logger)
        {
            _cosmosDb = cosmosDb;
            _settingsService = settingsService;
            _agentActivityService = agentActivityService;
            _configuration = configuration;
            _logger = logger;
        }

        public async Task<List<QualityEvaluation>> ListEvaluationsAsync(
            bool? flagged = null, float? minScore = null, float? maxScore = null, int limit = 50)
        {
            var conditions = new List<string> { "1=1" };
            var parameters = new Dictionary<string, object>();

            if (flagged.HasValue)
            {
                conditions.Add("c.flagged = @flagged");
                parameters["@flagged"] = flagged.Value;
            }

            if (minScore.HasValue)
            {
                conditions.Add("c.overallScore >= @minScore");
                parameters["@minScore"] = minScore.Value;
            }

            if (maxScore.HasValue)
            {
                conditions.Add("c.overallScore <= @maxScore");
                parameters["@maxScore"] = maxScore.Value;
            }

            var query = $"SELECT TOP {limit} * FROM c WHERE {string.Join(" AND ", conditions)} ORDER BY c.evaluatedAt DESC";
            return await _cosmosDb.QueryAsync<QualityEvaluation>(ContainerName, query, parameters);
        }

        public async Task<QualityEvaluation?> GetByCallRecordIdAsync(string callRecordId)
        {
            var results = await _cosmosDb.QueryAsync<QualityEvaluation>(ContainerName,
                "SELECT * FROM c WHERE c.callRecordId = @id",
                new Dictionary<string, object> { ["@id"] = callRecordId });
            return results.FirstOrDefault();
        }

        public async Task SaveEvaluationAsync(QualityEvaluation evaluation)
        {
            await _cosmosDb.UpsertAsync(ContainerName, evaluation, evaluation.CallRecordId);
        }

        public async Task<object> GetDashboardAsync()
        {
            var evaluations = await _cosmosDb.QueryAsync<QualityEvaluation>(ContainerName,
                "SELECT * FROM c ORDER BY c.evaluatedAt DESC");

            var now = DateTimeOffset.UtcNow;
            var trend = new[] { 7, 30, 90 }.Select(days =>
            {
                var periodEvals = evaluations.Where(e => e.EvaluatedAt >= now.AddDays(-days)).ToList();
                return new
                {
                    period = $"{days}d",
                    count = periodEvals.Count,
                    averageScore = periodEvals.Count > 0 ? periodEvals.Average(e => e.OverallScore) : 0f,
                    flaggedCount = periodEvals.Count(e => e.Flagged)
                };
            }).ToList();

            var criterionAverages = evaluations
                .SelectMany(e => e.CriterionScores)
                .GroupBy(c => c.CriterionName)
                .Select(g => new { criterionName = g.Key, averageScore = g.Average(c => c.Score) })
                .ToList();

            return new
            {
                averageScore = evaluations.Count > 0 ? evaluations.Average(e => e.OverallScore) : 0f,
                totalEvaluations = evaluations.Count,
                flaggedCount = evaluations.Count(e => e.Flagged),
                trend,
                criterionAverages
            };
        }

        public async Task<List<QualityCriterion>> GetCriteriaAsync()
        {
            var settings = await _settingsService.GetSettingsAsync();
            return settings.QualityCriteria?.Count > 0 ? settings.QualityCriteria : DefaultCriteria;
        }

        public async Task<float> GetAverageScoreAsync(DateTimeOffset from, DateTimeOffset to)
        {
            var evaluations = await _cosmosDb.QueryAsync<QualityEvaluation>(ContainerName,
                "SELECT * FROM c ORDER BY c.evaluatedAt DESC");
            var filtered = evaluations.Where(e => e.EvaluatedAt >= from && e.EvaluatedAt <= to).ToList();
            return filtered.Count > 0 ? filtered.Average(e => e.OverallScore) : 0f;
        }

        public async Task<List<QualityCriterion>> UpdateCriteriaAsync(List<QualityCriterion> criteria)
        {
            var settings = await _settingsService.GetSettingsAsync();
            settings.QualityCriteria = criteria;
            await _settingsService.SaveSettingsAsync(settings);
            return criteria;
        }

        public async Task<QualityEvaluation> EvaluateCallAsync(CallRecord callRecord)
        {
            return await _agentActivityService.ExecuteWithLoggingAsync("QualityEvaluation", "QualityScored", callRecord.CallConnectionId, async () =>
            {
                var criteria = await GetCriteriaAsync();
                var transcript = string.Join("\n", callRecord.TranscriptEntries?.Select(
                    e => $"{e.Speaker}: {e.Text}") ?? Enumerable.Empty<string>());

                // Fall back to recording transcript if real-time entries are empty
                if (string.IsNullOrWhiteSpace(transcript) && !string.IsNullOrWhiteSpace(callRecord.RecordingTranscript))
                {
                    transcript = callRecord.RecordingTranscript;
                }

                if (string.IsNullOrWhiteSpace(transcript))
                {
                    throw new InvalidOperationException("No transcript available for quality evaluation");
                }

                var criteriaJson = JsonSerializer.Serialize(criteria.Select(c => new { c.Name, c.Description }));
                var prompt = $@"Evaluate this call transcript against the quality criteria below.
For each criterion, provide a score from 1.0 to 5.0 and a brief justification.

Criteria: {criteriaJson}

Transcript:
{(transcript.Length > 4000 ? transcript[..4000] : transcript)}

Return JSON: {{""criterionScores"":[{{""criterionName"":"""",""score"":0.0,""justification"":""""}}]}}";

                var openAiUri = _configuration["AzureOpenAI:EndpointUri"];
                var chatDeployment = _configuration["AzureOpenAI:ChatDeployment"] ?? "gpt-4o-mini";
                if (string.IsNullOrEmpty(openAiUri))
                    throw new InvalidOperationException("Azure OpenAI endpoint not configured");

                var client = new AzureOpenAIClient(new Uri(openAiUri), new DefaultAzureCredential());
                var chatClient = client.GetChatClient(chatDeployment);
                var response = await chatClient.CompleteChatAsync(
                    new List<ChatMessage> { new SystemChatMessage(prompt) },
                    new ChatCompletionOptions());

                var responseText = response.Value.Content[0].Text;
                var jsonStart = responseText.IndexOf('{');
                var jsonEnd = responseText.LastIndexOf('}');
                if (jsonStart < 0 || jsonEnd < 0)
                    throw new InvalidOperationException("Invalid AI response format");

                var json = responseText[jsonStart..(jsonEnd + 1)];
                var parsed = JsonSerializer.Deserialize<ScoringResponse>(json,
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

                var scores = parsed?.CriterionScores ?? new();

                // Calculate weighted overall score
                float totalWeight = 0;
                float weightedSum = 0;
                foreach (var score in scores)
                {
                    var criterion = criteria.FirstOrDefault(c => c.Name.Equals(score.CriterionName, StringComparison.OrdinalIgnoreCase));
                    var weight = criterion?.Weight ?? 5;
                    totalWeight += weight;
                    weightedSum += score.Score * weight;
                }
                var overallScore = totalWeight > 0 ? weightedSum / totalWeight : 0f;

                // Determine if flagged
                bool flagged = false;
                string? flagReason = null;
                foreach (var score in scores)
                {
                    var criterion = criteria.FirstOrDefault(c => c.Name.Equals(score.CriterionName, StringComparison.OrdinalIgnoreCase));
                    if (criterion != null && score.Score < criterion.MinimumPassingScore)
                    {
                        flagged = true;
                        flagReason = $"Below threshold on '{score.CriterionName}' ({score.Score:F1} < {criterion.MinimumPassingScore:F1})";
                        break;
                    }
                }

                var evaluation = new QualityEvaluation
                {
                    CallRecordId = callRecord.CallConnectionId,
                    OverallScore = overallScore,
                    CriterionScores = scores,
                    Flagged = flagged,
                    FlagReason = flagReason
                };

                await _cosmosDb.UpsertAsync(ContainerName, evaluation, evaluation.CallRecordId);
                _logger.LogInformation("Quality evaluation for {CallId}: {Score:F1} (flagged={Flagged})",
                    callRecord.CallConnectionId, overallScore, flagged);

                return evaluation;
            });
        }

        private class ScoringResponse
        {
            public List<CriterionScore> CriterionScores { get; set; } = new();
        }
    }
}
