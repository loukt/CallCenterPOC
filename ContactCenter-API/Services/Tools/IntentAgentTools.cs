using ContactCenterPOC.Models;
using System.Text.Json;

namespace ContactCenterPOC.Services.Tools
{
    public class IntentAgentTools
    {
        private readonly CallHistoryService _callHistoryService;
        private readonly IntentDiscoveryService _intentService;

        public IntentAgentTools(CallHistoryService callHistoryService, IntentDiscoveryService intentService)
        {
            _callHistoryService = callHistoryService;
            _intentService = intentService;
        }

        public async Task<string> GetCallTranscriptsAsync(int limit)
        {
            var records = await _callHistoryService.GetRecentCallRecordsAsync(limit);
            var result = records
                .Where(r => r.TranscriptEntries?.Count > 0)
                .Select(r => new
                {
                    callId = r.CallConnectionId,
                    transcript = string.Join("\n", r.TranscriptEntries.Select(e => $"{e.Speaker}: {e.Text}")),
                    summary = r.CallSummary ?? ""
                })
                .ToList();
            return JsonSerializer.Serialize(result);
        }

        public async Task<string> GetExistingIntentsAsync()
        {
            var intents = await _intentService.GetAllIntentsAsync();
            var result = intents.Select(i => new
            {
                name = i.Name,
                groupName = i.GroupName,
                status = i.Status.ToString()
            }).ToList();
            return JsonSerializer.Serialize(result);
        }

        public async Task<string> SaveIntentsAsync(string intentsJson)
        {
            var intents = JsonSerializer.Deserialize<List<DiscoveredIntentDto>>(intentsJson,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? new();

            var existingIntents = await _intentService.GetAllIntentsAsync();
            var existingNames = new HashSet<string>(existingIntents.Select(i => i.Name), StringComparer.OrdinalIgnoreCase);

            int savedCount = 0;
            foreach (var di in intents)
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

                // Use the service's public upsert path
                await _intentService.SaveIntentAsync(intent);
                existingNames.Add(di.Name);
                savedCount++;
            }

            return JsonSerializer.Serialize(new { savedCount });
        }

        public List<AgentToolDefinition> GetToolDefinitions()
        {
            return new List<AgentToolDefinition>
            {
                new("get_call_transcripts",
                    "Fetch recent call transcripts for intent analysis",
                    """{"type":"object","properties":{"limit":{"type":"integer","description":"Max number of call records to fetch"}},"required":["limit"]}"""),
                new("get_existing_intents",
                    "Get all existing intents to avoid duplicates",
                    """{"type":"object","properties":{}}"""),
                new("save_intents",
                    "Save newly discovered intents",
                    """{"type":"object","properties":{"intents":{"type":"string","description":"JSON array of intents to save"}},"required":["intents"]}""")
            };
        }

        public async Task<string> DispatchToolCallAsync(string functionName, string arguments)
        {
            var args = JsonDocument.Parse(arguments).RootElement;
            return functionName switch
            {
                "get_call_transcripts" => await GetCallTranscriptsAsync(args.GetProperty("limit").GetInt32()),
                "get_existing_intents" => await GetExistingIntentsAsync(),
                "save_intents" => await SaveIntentsAsync(args.GetProperty("intents").GetString() ?? "[]"),
                _ => JsonSerializer.Serialize(new { error = $"Unknown tool: {functionName}" })
            };
        }

        private class DiscoveredIntentDto
        {
            public string Name { get; set; } = string.Empty;
            public string GroupName { get; set; } = string.Empty;
            public string Description { get; set; } = string.Empty;
            public List<string> SampleUtterances { get; set; } = new();
            public int Frequency { get; set; }
        }
    }
}
