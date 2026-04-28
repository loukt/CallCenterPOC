using ContactCenterPOC.Services;
using Microsoft.AspNetCore.Mvc;

namespace ContactCenterPOC.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class AgentActivityController : ControllerBase
    {
        private readonly AgentActivityService _agentActivityService;

        public AgentActivityController(AgentActivityService agentActivityService)
        {
            _agentActivityService = agentActivityService;
        }

        [HttpGet]
        public async Task<IActionResult> GetActivity(
            [FromQuery] string? agentName = null,
            [FromQuery] string? agentType = null,
            [FromQuery] string? result = null,
            [FromQuery] int max = 200,
            [FromQuery] int? limit = null)
        {
            agentName ??= agentType;
            if (limit.HasValue) max = limit.Value;
            if (max < 1) max = 1;
            if (max > 200) max = 200;

            var entries = await _agentActivityService.GetRecentAsync(agentName, result, max);
            return Ok(entries);
        }

        [HttpGet("summary")]
        public async Task<IActionResult> GetSummary([FromQuery] int hours = 24)
        {
            if (hours < 1) hours = 1;
            if (hours > 720) hours = 720; // max 30 days

            var summary = await _agentActivityService.GetSummaryAsync(hours);
            return Ok(summary);
        }
    }
}
