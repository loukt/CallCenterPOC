using ContactCenterPOC.Services;
using Microsoft.AspNetCore.Mvc;

namespace ContactCenterPOC.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class CaseController : ControllerBase
    {
        private readonly CaseManagementService _caseService;
        private readonly CallHistoryService _callHistoryService;
        private readonly ILogger<CaseController> _logger;

        public CaseController(CaseManagementService caseService, CallHistoryService callHistoryService, ILogger<CaseController> logger)
        {
            _caseService = caseService;
            _callHistoryService = callHistoryService;
            _logger = logger;
        }

        [HttpGet]
        public async Task<IActionResult> ListCases(
            [FromQuery] string? status = null,
            [FromQuery] string? priority = null,
            [FromQuery] int limit = 50)
        {
            if (limit < 1) limit = 1;
            if (limit > 200) limit = 200;

            var cases = await _caseService.ListCasesAsync(status, priority, limit);
            return Ok(cases);
        }

        [HttpGet("{id}")]
        public async Task<IActionResult> GetCase(string id)
        {
            var caseRecord = await _caseService.GetCaseAsync(id);
            if (caseRecord == null) return NotFound(new { error = "Case not found" });
            return Ok(caseRecord);
        }

        [HttpPost("{id}/resolve")]
        public async Task<IActionResult> ResolveCase(string id, [FromBody] ResolveCaseRequest? request = null)
        {
            var resolved = await _caseService.ResolveAsync(id, request?.ResolutionSummary);
            if (resolved == null) return NotFound(new { error = "Case not found" });
            return Ok(resolved);
        }

        [HttpPost("{id}/close")]
        public async Task<IActionResult> CloseCase(string id)
        {
            var closed = await _caseService.CloseAsync(id);
            if (closed == null) return NotFound(new { error = "Case not found or not in Resolved status" });
            return Ok(closed);
        }

        [HttpGet("summary")]
        public async Task<IActionResult> GetSummary()
        {
            var summary = await _caseService.GetSummaryAsync();
            return Ok(summary);
        }

        [HttpPost("batch-populate")]
        public async Task<IActionResult> BatchPopulate()
        {
            var callSummaries = await _callHistoryService.GetAllAsync();
            int created = 0, updated = 0, skipped = 0, failed = 0;

            _logger.LogInformation("Batch populating cases from {Count} call records", callSummaries.Count);

            foreach (var summary in callSummaries)
            {
                try
                {
                    var record = await _callHistoryService.GetByIdAsync(summary.CallConnectionId);
                    if (record == null || string.IsNullOrEmpty(record.PhoneNumber))
                    {
                        skipped++;
                        continue;
                    }

                    var existingCases = await _caseService.ListCasesAsync(limit: 200);
                    var alreadyLinked = existingCases.Any(c => c.LinkedCallRecords.Contains(record.CallConnectionId));
                    if (alreadyLinked)
                    {
                        skipped++;
                        continue;
                    }

                    await _caseService.ProcessCallOutcomeAsync(record);
                    created++;
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to create case for call {CallConnectionId}", summary.CallConnectionId);
                    failed++;
                }
            }

            _logger.LogInformation("Batch populate complete: {Created} created, {Updated} updated, {Skipped} skipped, {Failed} failed",
                created, updated, skipped, failed);

            return Ok(new { total = callSummaries.Count, created, updated, skipped, failed });
        }
    }

    public class ResolveCaseRequest
    {
        public string? ResolutionSummary { get; set; }
    }
}
