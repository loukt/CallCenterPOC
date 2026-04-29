using ContactCenterPOC.Hubs;
using ContactCenterPOC.Models;
using Microsoft.AspNetCore.SignalR;
using System.Diagnostics;
using System.Text.Json;

namespace ContactCenterPOC.Services.Tools
{
    public class PostCallReviewAgentTools
    {
        private readonly IServiceProvider _serviceProvider;
        private readonly CallHistoryService _callHistoryService;
        private readonly AgentActivityService _agentActivityService;
        private readonly ILogger<PostCallReviewAgentTools> _logger;

        // Lazily resolve to break circular dependency
        private OrchestrationService? _orchestrationService;
        private OrchestrationService Orchestration =>
            _orchestrationService ??= _serviceProvider.GetRequiredService<OrchestrationService>();

        // Set by RunPostCallReviewAsync before tool dispatch
        internal IHubContext<TranscriptHub>? HubContext { get; set; }
        internal string? ActiveCallId { get; set; }

        private static readonly string[] AgentOrder = new[]
        {
            "caseManagement", "qualityEvaluation", "knowledgeGapDetection", "summaryGeneration", "intentDiscovery"
        };
        private static readonly string[] AgentLabels = new[]
        {
            "Case Management", "Quality Evaluation", "Knowledge Gap Detection", "Summary Generation", "Intent Discovery"
        };

        private int _completedCount = 0;

        internal int CompletedCount => _completedCount;

        public PostCallReviewAgentTools(
            IServiceProvider serviceProvider,
            CallHistoryService callHistoryService,
            AgentActivityService agentActivityService,
            ILogger<PostCallReviewAgentTools> logger)
        {
            _serviceProvider = serviceProvider;
            _callHistoryService = callHistoryService;
            _agentActivityService = agentActivityService;
            _logger = logger;
        }

        private async Task LogStepAsync(string agentName, string actionType, AgentActionResult result, string? callConnectionId, string? detail, long durationMs)
        {
            try
            {
                await _agentActivityService.LogActivityAsync(agentName, actionType, result, detail, callConnectionId, durationMs);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to persist post-call agent activity for {AgentName}/{ActionType}", agentName, actionType);
            }
        }

        public void ResetProgress()
        {
            _completedCount = 0;
        }

        private async Task SendProgressAsync(string agentName, string agentLabel, string status)
        {
            if (HubContext == null || ActiveCallId == null) return;

            int index = Array.IndexOf(AgentOrder, agentName);
            int completed = status is "success" or "failed" ? _completedCount : _completedCount;
            int progress = (int)((completed / 5.0) * 100);

            await HubContext.Clients.Group(ActiveCallId).SendAsync("AgentProcessingUpdate", new
            {
                callConnectionId = ActiveCallId,
                agentName,
                agentLabel,
                status,
                progress,
                index,
                total = 5
            });
        }

        public async Task<string> RunCaseManagementAsync(string callConnectionId)
        {
            var sw = Stopwatch.StartNew();
            await SendProgressAsync("caseManagement", "Case Management", "processing");
            try
            {
                var callRecord = await _callHistoryService.GetByIdAsync(callConnectionId);
                if (callRecord == null)
                {
                    sw.Stop();
                    await LogStepAsync("CaseManagement", "PostCallReviewStep", AgentActionResult.Failure, callConnectionId, "Call record not found", sw.ElapsedMilliseconds);
                    return JsonSerializer.Serialize(new { status = "failed", error = "Call record not found" });
                }

                await Orchestration.ProcessCallOutcomeAsync(callRecord);
                _completedCount++;
                await SendProgressAsync("caseManagement", "Case Management", "success");
                sw.Stop();
                await LogStepAsync("CaseManagement", "PostCallReviewStep", AgentActionResult.Success, callConnectionId, "Case management completed", sw.ElapsedMilliseconds);
                return JsonSerializer.Serialize(new { status = "success" });
            }
            catch (Exception ex)
            {
                _completedCount++;
                await SendProgressAsync("caseManagement", "Case Management", "failed");
                sw.Stop();
                await LogStepAsync("CaseManagement", "PostCallReviewStep", AgentActionResult.Failure, callConnectionId, ex.Message, sw.ElapsedMilliseconds);
                _logger.LogWarning(ex, "PostCallReview: case management failed for {CallId}", callConnectionId);
                return JsonSerializer.Serialize(new { status = "failed", error = ex.Message });
            }
        }

        public async Task<string> RunQualityEvaluationAsync(string callConnectionId)
        {
            var sw = Stopwatch.StartNew();
            await SendProgressAsync("qualityEvaluation", "Quality Evaluation", "processing");
            try
            {
                var callRecord = await _callHistoryService.GetByIdAsync(callConnectionId);
                if (callRecord == null)
                {
                    sw.Stop();
                    await LogStepAsync("QualityAssurance", "PostCallReviewStep", AgentActionResult.Failure, callConnectionId, "Call record not found", sw.ElapsedMilliseconds);
                    return JsonSerializer.Serialize(new { status = "failed", error = "Call record not found" });
                }

                await Orchestration.EvaluateCallAsync(callRecord);
                _completedCount++;
                await SendProgressAsync("qualityEvaluation", "Quality Evaluation", "success");
                sw.Stop();
                await LogStepAsync("QualityAssurance", "PostCallReviewStep", AgentActionResult.Success, callConnectionId, "Quality evaluation completed", sw.ElapsedMilliseconds);
                return JsonSerializer.Serialize(new { status = "success" });
            }
            catch (Exception ex)
            {
                _completedCount++;
                await SendProgressAsync("qualityEvaluation", "Quality Evaluation", "failed");
                sw.Stop();
                await LogStepAsync("QualityAssurance", "PostCallReviewStep", AgentActionResult.Failure, callConnectionId, ex.Message, sw.ElapsedMilliseconds);
                _logger.LogWarning(ex, "PostCallReview: quality eval failed for {CallId}", callConnectionId);
                return JsonSerializer.Serialize(new { status = "failed", error = ex.Message });
            }
        }

        public async Task<string> RunKnowledgeGapDetectionAsync(string callConnectionId)
        {
            var sw = Stopwatch.StartNew();
            await SendProgressAsync("knowledgeGapDetection", "Knowledge Gap Detection", "processing");
            try
            {
                var callRecord = await _callHistoryService.GetByIdAsync(callConnectionId);
                if (callRecord == null)
                {
                    sw.Stop();
                    await LogStepAsync("KnowledgeManagement", "PostCallReviewStep", AgentActionResult.Failure, callConnectionId, "Call record not found", sw.ElapsedMilliseconds);
                    return JsonSerializer.Serialize(new { status = "failed", error = "Call record not found" });
                }

                await Orchestration.DetectKnowledgeGapsAsync(callRecord);
                _completedCount++;
                await SendProgressAsync("knowledgeGapDetection", "Knowledge Gap Detection", "success");
                sw.Stop();
                await LogStepAsync("KnowledgeManagement", "PostCallReviewStep", AgentActionResult.Success, callConnectionId, "Knowledge gap detection completed", sw.ElapsedMilliseconds);
                return JsonSerializer.Serialize(new { status = "success" });
            }
            catch (Exception ex)
            {
                _completedCount++;
                await SendProgressAsync("knowledgeGapDetection", "Knowledge Gap Detection", "failed");
                sw.Stop();
                await LogStepAsync("KnowledgeManagement", "PostCallReviewStep", AgentActionResult.Failure, callConnectionId, ex.Message, sw.ElapsedMilliseconds);
                _logger.LogWarning(ex, "PostCallReview: knowledge gap detection failed for {CallId}", callConnectionId);
                return JsonSerializer.Serialize(new { status = "failed", error = ex.Message });
            }
        }

        public async Task<string> RunSummaryGenerationAsync(string callConnectionId)
        {
            var sw = Stopwatch.StartNew();
            await SendProgressAsync("summaryGeneration", "Summary Generation", "processing");
            try
            {
                var callRecord = await _callHistoryService.GetByIdAsync(callConnectionId);
                if (callRecord == null)
                {
                    sw.Stop();
                    await LogStepAsync("CallSummary", "PostCallReviewStep", AgentActionResult.Failure, callConnectionId, "Call record not found", sw.ElapsedMilliseconds);
                    return JsonSerializer.Serialize(new { status = "failed", error = "Call record not found" });
                }

                var summary = await Orchestration.GenerateSummaryAsync(callRecord);
                if (!string.IsNullOrWhiteSpace(summary))
                {
                    callRecord.CallSummary = summary;
                    callRecord.SummarizedAt = DateTimeOffset.UtcNow;
                    await _callHistoryService.SaveCallRecordAsync(callRecord);
                }
                _completedCount++;
                await SendProgressAsync("summaryGeneration", "Summary Generation", "success");
                sw.Stop();
                await LogStepAsync("CallSummary", "PostCallReviewStep", AgentActionResult.Success, callConnectionId, "Summary generation completed", sw.ElapsedMilliseconds);
                return JsonSerializer.Serialize(new { status = "success", summaryLength = summary?.Length ?? 0 });
            }
            catch (Exception ex)
            {
                _completedCount++;
                await SendProgressAsync("summaryGeneration", "Summary Generation", "failed");
                sw.Stop();
                await LogStepAsync("CallSummary", "PostCallReviewStep", AgentActionResult.Failure, callConnectionId, ex.Message, sw.ElapsedMilliseconds);
                _logger.LogWarning(ex, "PostCallReview: summary generation failed for {CallId}", callConnectionId);
                return JsonSerializer.Serialize(new { status = "failed", error = ex.Message });
            }
        }

        public async Task<string> RunIntentDiscoveryAsync()
        {
            var sw = Stopwatch.StartNew();
            await SendProgressAsync("intentDiscovery", "Intent Discovery", "processing");
            try
            {
                var count = await Orchestration.DiscoverIntentsAsync();
                _completedCount++;
                await SendProgressAsync("intentDiscovery", "Intent Discovery", "success");
                sw.Stop();
                await LogStepAsync("CustomerIntent", "PostCallReviewStep", AgentActionResult.Success, ActiveCallId, $"Intent discovery completed; discovered {count} intents", sw.ElapsedMilliseconds);
                return JsonSerializer.Serialize(new { status = "success", intentsDiscovered = count });
            }
            catch (Exception ex)
            {
                _completedCount++;
                await SendProgressAsync("intentDiscovery", "Intent Discovery", "failed");
                sw.Stop();
                await LogStepAsync("CustomerIntent", "PostCallReviewStep", AgentActionResult.Failure, ActiveCallId, ex.Message, sw.ElapsedMilliseconds);
                _logger.LogWarning(ex, "PostCallReview: intent discovery failed");
                return JsonSerializer.Serialize(new { status = "failed", error = ex.Message });
            }
        }

        public List<AgentToolDefinition> GetToolDefinitions()
        {
            return new List<AgentToolDefinition>
            {
                new("run_case_management",
                    "Run case management agent to create or update cases based on the call outcome",
                    """{"type":"object","properties":{"callConnectionId":{"type":"string","description":"The call connection ID to process"}},"required":["callConnectionId"]}"""),
                new("run_quality_evaluation",
                    "Run quality evaluation agent to score the call against quality criteria",
                    """{"type":"object","properties":{"callConnectionId":{"type":"string","description":"The call connection ID to evaluate"}},"required":["callConnectionId"]}"""),
                new("run_knowledge_gap_detection",
                    "Run knowledge gap detection agent to find unanswered questions in the call",
                    """{"type":"object","properties":{"callConnectionId":{"type":"string","description":"The call connection ID to analyze"}},"required":["callConnectionId"]}"""),
                new("run_summary_generation",
                    "Run summary generation agent to create a contextual call summary",
                    """{"type":"object","properties":{"callConnectionId":{"type":"string","description":"The call connection ID to summarize"}},"required":["callConnectionId"]}"""),
                new("run_intent_discovery",
                    "Run intent discovery agent to discover new customer intents from recent calls",
                    """{"type":"object","properties":{},"required":[]}""")
            };
        }

        public async Task<string> DispatchToolCallAsync(string functionName, string arguments)
        {
            var args = JsonDocument.Parse(arguments).RootElement;
            return functionName switch
            {
                "run_case_management" => await RunCaseManagementAsync(args.GetProperty("callConnectionId").GetString()!),
                "run_quality_evaluation" => await RunQualityEvaluationAsync(args.GetProperty("callConnectionId").GetString()!),
                "run_knowledge_gap_detection" => await RunKnowledgeGapDetectionAsync(args.GetProperty("callConnectionId").GetString()!),
                "run_summary_generation" => await RunSummaryGenerationAsync(args.GetProperty("callConnectionId").GetString()!),
                "run_intent_discovery" => await RunIntentDiscoveryAsync(),
                _ => JsonSerializer.Serialize(new { error = $"Unknown tool: {functionName}" })
            };
        }
    }
}
