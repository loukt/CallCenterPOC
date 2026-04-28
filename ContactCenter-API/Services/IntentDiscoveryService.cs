using Azure.AI.OpenAI;
using Azure.Identity;
using ContactCenterPOC.Models;
using OpenAI.Chat;
using System.Text.Json;

namespace ContactCenterPOC.Services
{
    public class IntentDiscoveryService
    {
        private readonly CosmosDbService _cosmosDb;
        private readonly CallHistoryService _callHistoryService;
        private readonly AgentActivityService _agentActivityService;
        private readonly IConfiguration _configuration;
        private readonly ILogger<IntentDiscoveryService> _logger;
        private const string ContainerName = "Intents";

        public IntentDiscoveryService(
            CosmosDbService cosmosDb, CallHistoryService callHistoryService,
            AgentActivityService agentActivityService,
            IConfiguration configuration, ILogger<IntentDiscoveryService> logger)
        {
            _cosmosDb = cosmosDb;
            _callHistoryService = callHistoryService;
            _agentActivityService = agentActivityService;
            _configuration = configuration;
            _logger = logger;
        }

        public async Task<List<Intent>> GetAllIntentsAsync(string? status = null, string? groupName = null)
        {
            var conditions = new List<string> { "1=1" };
            var parameters = new Dictionary<string, object>();

            if (!string.IsNullOrEmpty(status) && Enum.TryParse<IntentStatus>(status, true, out var statusEnum))
            {
                conditions.Add("c.status = @status");
                parameters["@status"] = (int)statusEnum;
            }

            if (!string.IsNullOrEmpty(groupName))
            {
                conditions.Add("c.groupName = @groupName");
                parameters["@groupName"] = groupName;
            }

            var query = $"SELECT * FROM c WHERE {string.Join(" AND ", conditions)} ORDER BY c.frequency DESC";
            return await _cosmosDb.QueryAsync<Intent>(ContainerName, query, parameters);
        }

        public async Task<List<Intent>> GetApprovedIntentsAsync()
        {
            return await _cosmosDb.QueryAsync<Intent>(ContainerName,
                "SELECT * FROM c WHERE c.status = @status ORDER BY c.frequency DESC",
                new Dictionary<string, object> { ["@status"] = (int)IntentStatus.Approved });
        }

        public async Task<Intent?> GetIntentAsync(string id)
        {
            var results = await _cosmosDb.QueryAsync<Intent>(ContainerName,
                "SELECT * FROM c WHERE c.id = @id",
                new Dictionary<string, object> { ["@id"] = id });
            return results.FirstOrDefault();
        }

        public async Task<Intent?> ApproveIntentAsync(string id)
        {
            var intent = await GetIntentAsync(id);
            if (intent == null || intent.Status != IntentStatus.Pending) return null;

            intent.Status = IntentStatus.Approved;
            return await _cosmosDb.UpsertAsync(ContainerName, intent, intent.GroupName);
        }

        public async Task<Intent?> DiscardIntentAsync(string id)
        {
            var intent = await GetIntentAsync(id);
            if (intent == null || intent.Status != IntentStatus.Pending) return null;

            intent.Status = IntentStatus.Discarded;
            return await _cosmosDb.UpsertAsync(ContainerName, intent, intent.GroupName);
        }

        public async Task<(int approved, int discarded)> BatchUpdateAsync(List<string> ids, string action)
        {
            int approved = 0, discarded = 0;
            foreach (var id in ids)
            {
                if (action.Equals("approve", StringComparison.OrdinalIgnoreCase))
                {
                    if (await ApproveIntentAsync(id) != null) approved++;
                }
                else if (action.Equals("discard", StringComparison.OrdinalIgnoreCase))
                {
                    if (await DiscardIntentAsync(id) != null) discarded++;
                }
            }
            return (approved, discarded);
        }

        public async Task<Intent> SaveIntentAsync(Intent intent)
        {
            return await _cosmosDb.UpsertAsync(ContainerName, intent, intent.GroupName);
        }

        public async Task DiscoverIntentsAsync()
        {
            await _agentActivityService.ExecuteWithLoggingAsync("CustomerIntent", "IntentDiscovery", null, async () =>
            {
                var callRecords = await _callHistoryService.GetRecentCallRecordsAsync(100);
                if (callRecords.Count == 0)
                {
                    _logger.LogInformation("No call records found for intent discovery");
                    return 0;
                }

                // Build transcript summary for AI analysis
                var transcriptSummary = new List<string>();
                foreach (var record in callRecords.Take(50))
                {
                    if (record.TranscriptEntries?.Count > 0)
                    {
                        var transcript = string.Join("\n", record.TranscriptEntries.Select(e => $"{e.Speaker}: {e.Text}"));
                        if (transcript.Length > 2000) transcript = transcript[..2000];
                        transcriptSummary.Add(transcript);
                    }
                }

                if (transcriptSummary.Count == 0) return 0;

                var prompt = @"Analyze the following call transcripts and identify distinct customer intents.
For each intent, provide:
- name: A short label (e.g., ""Billing Dispute"", ""Product Return"")
- groupName: A category (e.g., ""Billing"", ""Support"", ""Sales"")
- description: One sentence explaining the intent
- sampleUtterances: 2-3 example phrases customers used
- frequency: Estimated count of how many calls had this intent

Return as JSON array: [{""name"":"""",""groupName"":"""",""description"":"""",""sampleUtterances"":[],""frequency"":1}]

Transcripts:
" + string.Join("\n---\n", transcriptSummary);

                var openAiUri = _configuration["AzureOpenAI:EndpointUri"];
                var chatDeployment = _configuration["AzureOpenAI:ChatDeployment"] ?? "gpt-4o-mini";
                if (string.IsNullOrEmpty(openAiUri)) return 0;

                var client = new AzureOpenAIClient(new Uri(openAiUri), new DefaultAzureCredential());
                var chatClient = client.GetChatClient(chatDeployment);
                var response = await chatClient.CompleteChatAsync(
                    new List<ChatMessage> { new SystemChatMessage(prompt) },
                    new ChatCompletionOptions());

                var responseText = response.Value.Content[0].Text;
                // Extract JSON from response
                var jsonStart = responseText.IndexOf('[');
                var jsonEnd = responseText.LastIndexOf(']');
                if (jsonStart < 0 || jsonEnd < 0) return 0;

                var json = responseText[jsonStart..(jsonEnd + 1)];
                var discoveredIntents = JsonSerializer.Deserialize<List<DiscoveredIntent>>(json,
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? new();

                // Deduplicate against existing intents
                var existingIntents = await GetAllIntentsAsync();
                var existingNames = new HashSet<string>(existingIntents.Select(i => i.Name), StringComparer.OrdinalIgnoreCase);

                int newCount = 0;
                foreach (var di in discoveredIntents)
                {
                    if (existingNames.Contains(di.Name)) continue;

                    var intent = new Intent
                    {
                        Name = di.Name,
                        GroupName = di.GroupName,
                        Description = di.Description,
                        SampleUtterances = di.SampleUtterances ?? new(),
                        Frequency = di.Frequency,
                        Status = IntentStatus.Pending
                    };

                    await _cosmosDb.UpsertAsync(ContainerName, intent, intent.GroupName);
                    existingNames.Add(di.Name);
                    newCount++;
                }

                _logger.LogInformation("Intent discovery completed: {NewCount} new intents from {TranscriptCount} transcripts",
                    newCount, transcriptSummary.Count);
                return newCount;
            });
        }

        private class DiscoveredIntent
        {
            public string Name { get; set; } = string.Empty;
            public string GroupName { get; set; } = string.Empty;
            public string Description { get; set; } = string.Empty;
            public List<string> SampleUtterances { get; set; } = new();
            public int Frequency { get; set; }
        }
    }
}
