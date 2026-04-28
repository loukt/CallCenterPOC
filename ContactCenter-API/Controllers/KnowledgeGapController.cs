using ContactCenterPOC.Services;
using Microsoft.AspNetCore.Mvc;

namespace ContactCenterPOC.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class KnowledgeGapController : ControllerBase
    {
        private readonly KnowledgeGapService _gapService;

        public KnowledgeGapController(KnowledgeGapService gapService)
        {
            _gapService = gapService;
        }

        [HttpGet]
        public async Task<IActionResult> ListGaps(
            [FromQuery] string? status = null,
            [FromQuery] int limit = 50)
        {
            if (limit < 1) limit = 1;
            if (limit > 200) limit = 200;

            var gaps = await _gapService.ListGapsAsync(status, limit);
            return Ok(gaps);
        }

        [HttpGet("{id}")]
        public async Task<IActionResult> GetGap(string id)
        {
            var gap = await _gapService.GetGapAsync(id);
            if (gap == null) return NotFound(new { error = "Knowledge gap not found" });
            return Ok(gap);
        }

        [HttpPost("{id}/publish")]
        public async Task<IActionResult> PublishGap(string id)
        {
            try
            {
                var published = await _gapService.PublishGapAsync(id);
                if (published == null) return NotFound(new { error = "Knowledge gap not found" });
                return Ok(published);
            }
            catch (InvalidOperationException ex)
            {
                return BadRequest(new { error = ex.Message });
            }
        }

        [HttpPost("{id}/dismiss")]
        public async Task<IActionResult> DismissGap(string id)
        {
            var dismissed = await _gapService.DismissGapAsync(id);
            if (dismissed == null) return NotFound(new { error = "Knowledge gap not found" });
            return Ok(dismissed);
        }
    }
}
