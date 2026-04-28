using ContactCenterPOC.Services;
using Microsoft.AspNetCore.Mvc;

namespace ContactCenterPOC.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class WebRTCController : ControllerBase
    {
        private readonly WebRTCSignalingService _webRTCService;
        private readonly ILogger<WebRTCController> _logger;

        public WebRTCController(WebRTCSignalingService webRTCService, ILogger<WebRTCController> logger)
        {
            _webRTCService = webRTCService;
            _logger = logger;
        }

        [HttpPost("session")]
        public async Task<IActionResult> CreateSession([FromBody] CreateSessionRequest? request = null)
        {
            var session = await _webRTCService.CreateSessionAsync(
                request?.CampaignId, request?.ExpiryMinutes ?? 15);
            return Created($"/api/WebRTC/session/{session.SessionId}", session);
        }

        [HttpGet("session/{sessionId}")]
        public async Task<IActionResult> GetSession(string sessionId)
        {
            var session = await _webRTCService.GetSessionAsync(sessionId);
            if (session == null) return NotFound(new { error = "Session not found" });
            return Ok(session);
        }

        [HttpGet("session/{sessionId}/validate")]
        public async Task<IActionResult> ValidateSession(string sessionId)
        {
            var (valid, reason) = await _webRTCService.ValidateSessionAsync(sessionId);
            return Ok(new { valid, reason });
        }

        [HttpPost("session/{sessionId}/join")]
        public async Task<IActionResult> JoinSession(string sessionId, [FromBody] JoinRequest request)
        {
            if (string.IsNullOrWhiteSpace(request?.CallerName))
                return BadRequest(new { error = "Caller name is required" });

            try
            {
                var session = await _webRTCService.JoinSessionAsync(
                    sessionId, request.CallerName, request.CallerEmail, request.CallerPhone);
                if (session == null) return NotFound(new { error = "Session not found" });

                return Ok(new
                {
                    sessionId = session.SessionId,
                    signalRHubUrl = "/transcriptHub",
                    message = "Connected successfully"
                });
            }
            catch (InvalidOperationException ex)
            {
                return BadRequest(new { error = ex.Message });
            }
        }

        [HttpPost("session/{sessionId}/end")]
        public async Task<IActionResult> EndSession(string sessionId)
        {
            var session = await _webRTCService.EndSessionAsync(sessionId);
            if (session == null) return NotFound(new { error = "Session not found" });
            return Ok(session);
        }
    }

    public class CreateSessionRequest
    {
        public string? CampaignId { get; set; }
        public int ExpiryMinutes { get; set; } = 15;
    }

    public class JoinRequest
    {
        public string CallerName { get; set; } = string.Empty;
        public string? CallerEmail { get; set; }
        public string? CallerPhone { get; set; }
    }
}
