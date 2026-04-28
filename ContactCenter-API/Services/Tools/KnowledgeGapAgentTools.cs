using ContactCenterPOC.Models;
using System.Text.Json;

namespace ContactCenterPOC.Services.Tools
{
    public class KnowledgeGapAgentTools
    {
        private readonly KnowledgeGapService _knowledgeGapService;
        private readonly KnowledgeBaseService _knowledgeBaseService;

        public KnowledgeGapAgentTools(KnowledgeGapService knowledgeGapService, KnowledgeBaseService knowledgeBaseService)
        {
            _knowledgeGapService = knowledgeGapService;
            _knowledgeBaseService = knowledgeBaseService;
        }

        public async Task<string> SearchKnowledgeBaseAsync(string query)
        {
            var results = await _knowledgeBaseService.SearchAsync(query);
            var items = results.Select(r => new
            {
                title = r.DocumentTitle,
                snippet = r.Content?.Length > 200 ? r.Content[..200] + "..." : r.Content ?? "",
                score = r.Score
            }).ToList();
            return JsonSerializer.Serialize(items);
        }

        public async Task<string> GetExistingGapsAsync(string topic)
        {
            var gaps = await _knowledgeGapService.ListGapsAsync();
            var matched = gaps
                .Where(g => g.Topic.Contains(topic, StringComparison.OrdinalIgnoreCase))
                .Select(g => new
                {
                    id = g.Id,
                    topic = g.Topic,
                    status = g.Status.ToString(),
                    frequency = g.Frequency
                })
                .ToList();
            return JsonSerializer.Serialize(matched);
        }

        public async Task<string> CreateGapAsync(string gapJson)
        {
            var dto = JsonSerializer.Deserialize<CreateGapDto>(gapJson,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            if (dto == null) return JsonSerializer.Serialize(new { error = "Invalid gap JSON" });

            var gap = new KnowledgeGap
            {
                Topic = dto.Topic,
                SampleQuestions = string.IsNullOrEmpty(dto.Question) ? new() : new List<string> { dto.Question },
                SuggestedTitle = dto.SuggestedTitle,
                SuggestedArticle = dto.SuggestedArticle,
                Frequency = 1,
                Status = KnowledgeGapStatus.Identified,
                LinkedCallRecordIds = string.IsNullOrEmpty(dto.CallRecordId) ? new() : new List<string> { dto.CallRecordId }
            };

            var saved = await _knowledgeGapService.CreateGapAsync(gap);
            return JsonSerializer.Serialize(new { gapId = saved.Id });
        }

        public async Task<string> UpdateGapAsync(string updateJson)
        {
            var dto = JsonSerializer.Deserialize<UpdateGapDto>(updateJson,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            if (dto == null) return JsonSerializer.Serialize(new { error = "Invalid update JSON" });

            var gap = await _knowledgeGapService.GetGapAsync(dto.GapId);
            if (gap == null) return JsonSerializer.Serialize(new { updated = false });

            gap.Frequency++;
            if (!string.IsNullOrEmpty(dto.CallRecordId))
                gap.LinkedCallRecordIds.Add(dto.CallRecordId);
            if (!string.IsNullOrEmpty(dto.SuggestedTitle))
                gap.SuggestedTitle = dto.SuggestedTitle;
            if (!string.IsNullOrEmpty(dto.SuggestedArticle))
                gap.SuggestedArticle = dto.SuggestedArticle;

            await _knowledgeGapService.UpdateGapAsync(gap);
            return JsonSerializer.Serialize(new { updated = true });
        }

        public List<AgentToolDefinition> GetToolDefinitions()
        {
            return new List<AgentToolDefinition>
            {
                new("search_knowledge_base",
                    "Search the knowledge base for existing articles on a topic",
                    """{"type":"object","properties":{"query":{"type":"string","description":"Search query"}},"required":["query"]}"""),
                new("get_existing_gaps",
                    "Get existing knowledge gaps matching a topic",
                    """{"type":"object","properties":{"topic":{"type":"string","description":"Topic to search for"}},"required":["topic"]}"""),
                new("create_gap",
                    "Create a new knowledge gap entry",
                    """{"type":"object","properties":{"gap":{"type":"string","description":"JSON with topic, question, suggestedTitle, suggestedArticle, callRecordId"}},"required":["gap"]}"""),
                new("update_gap",
                    "Update an existing knowledge gap",
                    """{"type":"object","properties":{"update":{"type":"string","description":"JSON with gapId, question, suggestedArticle, suggestedTitle, callRecordId"}},"required":["update"]}""")
            };
        }

        public async Task<string> DispatchToolCallAsync(string functionName, string arguments)
        {
            var args = JsonDocument.Parse(arguments).RootElement;
            return functionName switch
            {
                "search_knowledge_base" => await SearchKnowledgeBaseAsync(args.GetProperty("query").GetString()!),
                "get_existing_gaps" => await GetExistingGapsAsync(args.GetProperty("topic").GetString()!),
                "create_gap" => await CreateGapAsync(args.GetProperty("gap").GetString()!),
                "update_gap" => await UpdateGapAsync(args.GetProperty("update").GetString()!),
                _ => JsonSerializer.Serialize(new { error = $"Unknown tool: {functionName}" })
            };
        }

        private class CreateGapDto
        {
            public string Topic { get; set; } = string.Empty;
            public string Question { get; set; } = string.Empty;
            public string SuggestedTitle { get; set; } = string.Empty;
            public string SuggestedArticle { get; set; } = string.Empty;
            public string? CallRecordId { get; set; }
        }

        private class UpdateGapDto
        {
            public string GapId { get; set; } = string.Empty;
            public string? Question { get; set; }
            public string? SuggestedArticle { get; set; }
            public string? SuggestedTitle { get; set; }
            public string? CallRecordId { get; set; }
        }
    }
}
