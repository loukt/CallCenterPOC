using ContactCenterPOC.Models;
using System.Text.Json;

namespace ContactCenterPOC.Services.Tools
{
    public class QualityAgentTools
    {
        private readonly QualityEvaluationService _qualityService;

        public QualityAgentTools(QualityEvaluationService qualityService)
        {
            _qualityService = qualityService;
        }

        public async Task<string> GetQualityCriteriaAsync()
        {
            var criteria = await _qualityService.GetCriteriaAsync();
            return JsonSerializer.Serialize(criteria);
        }

        public async Task<string> SaveEvaluationAsync(string evalJson)
        {
            var dto = JsonSerializer.Deserialize<EvaluationDto>(evalJson,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            if (dto == null) return JsonSerializer.Serialize(new { saved = false });

            var evaluation = new QualityEvaluation
            {
                CallRecordId = dto.CallRecordId,
                OverallScore = dto.OverallScore,
                CriterionScores = dto.CriterionScores?.Select(cs => new CriterionScore
                {
                    CriterionName = cs.CriterionName,
                    Score = cs.Score,
                    Justification = cs.Justification
                }).ToList() ?? new(),
                Flagged = dto.Flagged,
                FlagReason = dto.FlagReason
            };

            await _qualityService.SaveEvaluationAsync(evaluation);
            return JsonSerializer.Serialize(new { saved = true });
        }

        public List<AgentToolDefinition> GetToolDefinitions()
        {
            return new List<AgentToolDefinition>
            {
                new("get_quality_criteria",
                    "Get quality evaluation criteria with weights and passing scores",
                    """{"type":"object","properties":{}}"""),
                new("save_evaluation",
                    "Save quality evaluation scores for a call",
                    """{"type":"object","properties":{"evaluation":{"type":"string","description":"JSON with callRecordId, overallScore, criterionScores, flagged, flagReason"}},"required":["evaluation"]}""")
            };
        }

        public async Task<string> DispatchToolCallAsync(string functionName, string arguments)
        {
            var args = JsonDocument.Parse(arguments).RootElement;
            return functionName switch
            {
                "get_quality_criteria" => await GetQualityCriteriaAsync(),
                "save_evaluation" => await SaveEvaluationAsync(args.GetProperty("evaluation").GetString()!),
                _ => JsonSerializer.Serialize(new { error = $"Unknown tool: {functionName}" })
            };
        }

        private class EvaluationDto
        {
            public string CallRecordId { get; set; } = string.Empty;
            public float OverallScore { get; set; }
            public List<CriterionScoreDto>? CriterionScores { get; set; }
            public bool Flagged { get; set; }
            public string? FlagReason { get; set; }
        }

        private class CriterionScoreDto
        {
            public string CriterionName { get; set; } = string.Empty;
            public float Score { get; set; }
            public string Justification { get; set; } = string.Empty;
        }
    }
}
