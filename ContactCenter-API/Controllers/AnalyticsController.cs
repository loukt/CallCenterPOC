using ContactCenterPOC.Models;
using ContactCenterPOC.Services;
using Microsoft.AspNetCore.Mvc;

namespace ContactCenterPOC.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class AnalyticsController : ControllerBase
    {
        private readonly CallHistoryService _callHistoryService;
        private readonly CaseManagementService _caseManagementService;
        private readonly QualityEvaluationService _qualityEvaluationService;
        private readonly OrchestrationService _orchestrationService;
        private readonly ILogger<AnalyticsController> _logger;

        public AnalyticsController(
            CallHistoryService callHistoryService,
            CaseManagementService caseManagementService,
            QualityEvaluationService qualityEvaluationService,
            OrchestrationService orchestrationService,
            ILogger<AnalyticsController> logger)
        {
            _callHistoryService = callHistoryService;
            _caseManagementService = caseManagementService;
            _qualityEvaluationService = qualityEvaluationService;
            _orchestrationService = orchestrationService;
            _logger = logger;
        }

        [HttpGet("dashboard")]
        public async Task<IActionResult> GetDashboard([FromQuery] string range = "today")
        {
            var (from, to) = ParseTimeRange(range);

            var callRecords = await _callHistoryService.GetByDateRangeAsync(from, to);

            var totalCalls = callRecords.Count;
            var avgDurationSeconds = totalCalls > 0
                ? callRecords.Average(r => r.Duration.TotalSeconds)
                : 0;

            var sentimentBreakdown = new
            {
                positive = totalCalls > 0 ? callRecords.Count(r => r.OverallSentiment == Models.SentimentLabel.Positive) * 100.0 / totalCalls : 0,
                neutral = totalCalls > 0 ? callRecords.Count(r => r.OverallSentiment == Models.SentimentLabel.Neutral) * 100.0 / totalCalls : 0,
                negative = totalCalls > 0 ? callRecords.Count(r => r.OverallSentiment == Models.SentimentLabel.Negative) * 100.0 / totalCalls : 0
            };

            var successRate = totalCalls > 0
                ? callRecords.Count(r => r.OverallSentiment == Models.SentimentLabel.Positive) * 100.0 / totalCalls
                : 0;

            var caseStats = await _caseManagementService.GetCaseCountsByStatusAsync(from, to);
            var avgQualityScore = await _qualityEvaluationService.GetAverageScoreAsync(from, to);

            return Ok(new
            {
                range,
                from,
                to,
                totalCalls,
                avgDurationSeconds = Math.Round(avgDurationSeconds, 1),
                sentimentBreakdown,
                successRate = Math.Round(successRate, 1),
                casesByStatus = caseStats,
                avgQualityScore = Math.Round(avgQualityScore, 2)
            });
        }

        [HttpPost("batch-analyze")]
        public async Task<IActionResult> BatchAnalyze([FromBody] BatchAnalyzeRequest request)
        {
            if (string.IsNullOrEmpty(request?.CallConnectionId))
                return BadRequest(new { error = "callConnectionId is required" });

            var callRecord = await _callHistoryService.GetByIdAsync(request.CallConnectionId);
            if (callRecord == null)
                return NotFound(new { error = "Call record not found" });

            var results = new Dictionary<string, object>();
            var tasks = new List<(string name, Func<Task> action)>
            {
                ("caseManagement", async () => { await _orchestrationService.ProcessCallOutcomeAsync(callRecord); }),
                ("qualityEvaluation", async () => { await _orchestrationService.EvaluateCallAsync(callRecord); }),
                ("knowledgeGapDetection", async () => { await _orchestrationService.DetectKnowledgeGapsAsync(callRecord); }),
                ("summaryGeneration", async () => { var s = await _orchestrationService.GenerateSummaryAsync(callRecord); results["summary"] = s ?? ""; }),
                ("intentDiscovery", async () => { var c = await _orchestrationService.DiscoverIntentsAsync(); results["intentsDiscovered"] = c; }),
            };

            bool anyFailure = false;
            foreach (var (name, action) in tasks)
            {
                try
                {
                    await action();
                    results[name] = new { status = "success" };
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Batch analyze: {Agent} failed for {CallConnectionId}", name, request.CallConnectionId);
                    results[name] = new { status = "failed", error = ex.Message };
                    anyFailure = true;
                }
            }

            return anyFailure ? StatusCode(207, results) : Ok(results);
        }

        private static (DateTimeOffset from, DateTimeOffset to) ParseTimeRange(string range)
        {
            var now = DateTimeOffset.UtcNow;
            return range?.ToLowerInvariant() switch
            {
                "yesterday" => (now.Date.AddDays(-1), now.Date),
                "7d" => (now.AddDays(-7), now),
                "30d" => (now.AddDays(-30), now),
                "year" => (now.AddYears(-1), now),
                _ => (now.Date, now) // "today" or default
            };
        }
    }

    public class BatchAnalyzeRequest
    {
        public string CallConnectionId { get; set; } = string.Empty;
    }
}
