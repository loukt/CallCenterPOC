using ContactCenterPOC.Services;
using Microsoft.AspNetCore.Mvc;

namespace ContactCenterPOC.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class IntentController : ControllerBase
    {
        private readonly IntentDiscoveryService _intentService;
        private readonly OrchestrationService _orchestrationService;
        private readonly ILogger<IntentController> _logger;

        public IntentController(IntentDiscoveryService intentService, OrchestrationService orchestrationService, ILogger<IntentController> logger)
        {
            _intentService = intentService;
            _orchestrationService = orchestrationService;
            _logger = logger;
        }

        [HttpGet]
        public async Task<IActionResult> ListIntents(
            [FromQuery] string? status = null,
            [FromQuery] string? groupName = null)
        {
            var intents = await _intentService.GetAllIntentsAsync(status, groupName);
            return Ok(intents);
        }

        [HttpGet("{id}")]
        public async Task<IActionResult> GetIntent(string id)
        {
            var intent = await _intentService.GetIntentAsync(id);
            if (intent == null) return NotFound(new { error = "Intent not found" });
            return Ok(intent);
        }

        [HttpPost("{id}/approve")]
        public async Task<IActionResult> ApproveIntent(string id)
        {
            var intent = await _intentService.ApproveIntentAsync(id);
            if (intent == null) return NotFound(new { error = "Intent not found or not in Pending status" });
            return Ok(intent);
        }

        [HttpPost("{id}/discard")]
        public async Task<IActionResult> DiscardIntent(string id)
        {
            var intent = await _intentService.DiscardIntentAsync(id);
            if (intent == null) return NotFound(new { error = "Intent not found or not in Pending status" });
            return Ok(intent);
        }

        [HttpPost("batch")]
        public async Task<IActionResult> BatchUpdate([FromBody] BatchIntentRequest request)
        {
            if (request?.Ids == null || request.Ids.Count == 0)
                return BadRequest(new { error = "No intent IDs provided" });

            if (string.IsNullOrEmpty(request.Action) ||
                (!request.Action.Equals("approve", StringComparison.OrdinalIgnoreCase) &&
                 !request.Action.Equals("discard", StringComparison.OrdinalIgnoreCase)))
                return BadRequest(new { error = "Action must be 'approve' or 'discard'" });

            var (approved, discarded) = await _intentService.BatchUpdateAsync(request.Ids, request.Action);
            return Ok(new { approved, discarded, processed = approved + discarded });
        }

        [HttpPost("discover")]
        public async Task<IActionResult> DiscoverIntents()
        {
            _ = Task.Run(async () =>
            {
                try
                {
                    await _orchestrationService.DiscoverIntentsAsync();
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Intent discovery failed");
                }
            });

            return Accepted(new { message = "Intent discovery started. Check Agent Activity for progress." });
        }
    }

    public class BatchIntentRequest
    {
        public List<string> Ids { get; set; } = new();
        public string Action { get; set; } = string.Empty;
    }
}
