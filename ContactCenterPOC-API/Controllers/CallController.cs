using ContactCenterPOC.Models;
using ContactCenterPOC.Services;
using Microsoft.AspNetCore.Mvc;

namespace ContactCenterPOC.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class CallController : ControllerBase
    {
        private readonly CallService _callService;
        private readonly ILogger<CallController> _logger;

        public CallController(CallService callService, ILogger<CallController> logger)
        {
            _callService = callService;
            _logger = logger;
        }

        [HttpPost("initiate")]
        public async Task<IActionResult> InitiateCall([FromBody] CallRequest callRequest)
        {
            try
            {
                // Initiate test call
                var callId = await _callService.InitiateCall(callRequest.PhoneNumber,callRequest.Prompt, HttpContext);
                
                // Start realtime conversation
                await _callService.StartCallInteraction(callId, HttpContext);

                return Ok(new
                {
                    CallId = callId,
                    Status = "Call initiated successfully",
                    Timestamp = DateTime.UtcNow
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during test call");
                return StatusCode(500, new
                {
                    Error = "Failed to initiate test call",
                    Message = ex.Message
                });
            }
        }
    }


}
