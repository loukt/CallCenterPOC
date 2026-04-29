using ContactCenterPOC.Models;
using ContactCenterPOC.Services;
using Microsoft.AspNetCore.Mvc;

namespace ContactCenterPOC.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class QualityController : ControllerBase
    {
        private readonly QualityEvaluationService _qualityService;
        private readonly CallHistoryService _callHistoryService;
        private readonly ILogger<QualityController> _logger;

        public QualityController(QualityEvaluationService qualityService, CallHistoryService callHistoryService, ILogger<QualityController> logger)
        {
            _qualityService = qualityService;
            _callHistoryService = callHistoryService;
            _logger = logger;
        }

        [HttpGet]
        public async Task<IActionResult> ListEvaluations(
            [FromQuery] bool? flagged = null,
            [FromQuery] float? minScore = null,
            [FromQuery] float? maxScore = null,
            [FromQuery] int limit = 50)
        {
            if (limit < 1) limit = 1;
            if (limit > 200) limit = 200;

            var evaluations = await _qualityService.ListEvaluationsAsync(flagged, minScore, maxScore, limit);
            return Ok(evaluations);
        }

        [HttpGet("{callRecordId}")]
        public async Task<IActionResult> GetByCallRecord(string callRecordId)
        {
            var evaluation = await _qualityService.GetByCallRecordIdAsync(callRecordId);
            if (evaluation == null) return NotFound(new { error = "Evaluation not found for this call" });
            return Ok(evaluation);
        }

        [HttpGet("dashboard")]
        public async Task<IActionResult> GetDashboard()
        {
            var dashboard = await _qualityService.GetDashboardAsync();
            return Ok(dashboard);
        }

        [HttpGet("criteria")]
        public async Task<IActionResult> GetCriteria()
        {
            var criteria = await _qualityService.GetCriteriaAsync();
            return Ok(criteria);
        }

        [HttpPut("criteria")]
        public async Task<IActionResult> UpdateCriteria([FromBody] List<QualityCriterion> criteria)
        {
            if (criteria == null || criteria.Count == 0)
                return BadRequest(new { error = "At least one criterion is required" });

            var validationErrors = ValidateCriteria(criteria);
            if (validationErrors.Count > 0)
                return BadRequest(new { errors = validationErrors });

            var updated = await _qualityService.UpdateCriteriaAsync(criteria);
            return Ok(updated);
        }

        internal static List<string> ValidateCriteria(List<QualityCriterion> criteria)
        {
            var errors = new List<string>();

            for (var i = 0; i < criteria.Count; i++)
            {
                var criterion = criteria[i];
                var label = string.IsNullOrWhiteSpace(criterion.Name) ? $"Criterion {i + 1}" : criterion.Name;

                if (string.IsNullOrWhiteSpace(criterion.Name))
                    errors.Add($"Criterion {i + 1} requires a name.");

                if (criterion.Weight < 1 || criterion.Weight > 100)
                    errors.Add($"{label} weight must be between 1% and 100%.");

                if (criterion.MinimumPassingScore < 1f || criterion.MinimumPassingScore > 5f)
                    errors.Add($"{label} minimum passing score must be between 1.0 and 5.0.");
            }

            var totalWeight = criteria.Sum(c => c.Weight);
            if (totalWeight > 100)
                errors.Add($"Total quality criteria weight must be 100% or less. Current total is {totalWeight}%.");

            return errors;
        }

        [HttpPost("batch-evaluate")]
        public async Task<IActionResult> BatchEvaluate([FromQuery] int limit = 0)
        {
            try
            {
                var callSummaries = await _callHistoryService.GetAllAsync();
                int evaluated = 0, skipped = 0, failed = 0;
                var errors = new List<string>();
                int attempted = 0;

                _logger.LogInformation("Batch quality evaluation starting for {Count} call records (limit={Limit})", callSummaries.Count, limit);

                foreach (var summary in callSummaries)
                {
                    try
                    {
                        // Skip if already evaluated
                        var existing = await _qualityService.GetByCallRecordIdAsync(summary.CallConnectionId);
                        if (existing != null)
                        {
                            skipped++;
                            continue;
                        }

                        var record = await _callHistoryService.GetByIdAsync(summary.CallConnectionId);
                        if (record == null)
                        {
                            skipped++;
                            continue;
                        }

                        bool hasTranscript = (record.TranscriptEntries != null && record.TranscriptEntries.Count > 0)
                            || !string.IsNullOrWhiteSpace(record.RecordingTranscript);
                        if (!hasTranscript)
                        {
                            skipped++;
                            continue;
                        }

                        await _qualityService.EvaluateCallAsync(record);
                        evaluated++;
                        attempted++;
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Failed to evaluate call {CallConnectionId}", summary.CallConnectionId);
                        failed++;
                        attempted++;
                        if (errors.Count < 5)
                            errors.Add($"{summary.CallConnectionId}: {ex.GetType().Name}: {ex.Message}");
                    }

                    if (limit > 0 && attempted >= limit) break;
                }

                _logger.LogInformation("Batch quality evaluation complete: {Evaluated} evaluated, {Skipped} skipped, {Failed} failed",
                    evaluated, skipped, failed);

                return Ok(new { total = callSummaries.Count, evaluated, skipped, failed, errors });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Batch quality evaluation crashed");
                return StatusCode(500, new { error = ex.GetType().Name, message = ex.Message, stack = ex.StackTrace?.Substring(0, Math.Min(500, ex.StackTrace?.Length ?? 0)) });
            }
        }

    }
}
