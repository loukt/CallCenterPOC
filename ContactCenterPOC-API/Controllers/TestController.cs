using ContactCenterPOC.Services;
using Microsoft.AspNetCore.Mvc;
using System.Linq;

namespace ContactCenterPOC.Controllers
{
#if DEBUG
    [ApiController]
    [ApiExplorerSettings(IgnoreApi = true)]
    [Route("api/[controller]")]
    public class TestController : ControllerBase
    {
        private readonly CallService _callService;
        private readonly ILogger<TestController> _logger;

        public TestController(
            CallService callService,
            ILogger<TestController> logger)
        {
            _callService = callService;
            _logger = logger;
        }

        [HttpPost("test-call")]
        public async Task<IActionResult> TestCall([FromBody] string phoneNumber)
        {
            try
            {
                var results = await _callService.InitiateCall(
                    new[] { phoneNumber }, null, null, null, HttpContext);

                return Ok(new
                {
                    calls = results.Select(r => new { r.callConnectionId, r.phoneNumber }),
                    status = "Test call initiated successfully",
                    timestamp = DateTimeOffset.UtcNow
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during test call");
                return StatusCode(500, new
                {
                    error = "Failed to initiate test call",
                    message = ex.Message
                });
            }
        }
    }
#endif
}
