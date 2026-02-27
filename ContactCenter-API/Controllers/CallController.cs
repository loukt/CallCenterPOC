using ContactCenterPOC.Models;
using ContactCenterPOC.Services;
using Microsoft.AspNetCore.Mvc;
using System.Text.RegularExpressions;

namespace ContactCenterPOC.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class CallController : ControllerBase
    {
        private readonly CallService _callService;
        private readonly ILogger<CallController> _logger;
        private static readonly Regex E164Regex = new(@"^\+[1-9]\d{1,14}$");

        public CallController(CallService callService, ILogger<CallController> logger)
        {
            _callService = callService;
            _logger = logger;
        }

        [HttpPost("initiate")]
        public async Task<IActionResult> InitiateCall([FromBody] CallRequest callRequest)
        {
            if (!ModelState.IsValid)
            {
                var errors = ModelState.Values
                    .SelectMany(v => v.Errors)
                    .Select(e => e.ErrorMessage)
                    .ToList();
                return BadRequest(new
                {
                    error = "Validation failed",
                    message = string.Join("; ", errors)
                });
            }

            // Validate each phone number format
            if (callRequest.PhoneNumbers == null || callRequest.PhoneNumbers.Length == 0)
            {
                return BadRequest(new
                {
                    error = "Invalid phone number",
                    message = "At least one phone number is required"
                });
            }

            foreach (var pn in callRequest.PhoneNumbers)
            {
                if (!E164Regex.IsMatch(pn))
                {
                    return BadRequest(new
                    {
                        error = "Invalid phone number",
                        message = $"Phone number '{pn}' must be in E.164 format (e.g., +6591234567)"
                    });
                }
            }

            try
            {
                var results = await _callService.InitiateCall(
                    callRequest.PhoneNumbers,
                    callRequest.Prompt,
                    callRequest.CampaignId,
                    callRequest.ContactNames,
                    HttpContext);

                return Ok(new
                {
                    calls = results.Select(r => new { callConnectionId = r.callConnectionId, phoneNumber = r.phoneNumber }),
                    status = $"{results.Count} call(s) initiated successfully",
                    timestamp = DateTimeOffset.UtcNow
                });
            }
            catch (InvalidOperationException ex) when (ex.Message.Contains("concurrent") || ex.Message.Contains("Maximum"))
            {
                return StatusCode(429, new
                {
                    error = "Concurrent call limit reached",
                    message = ex.Message
                });
            }
            catch (ArgumentException ex)
            {
                return BadRequest(new
                {
                    error = "Invalid request",
                    message = ex.Message
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error initiating call");
                return StatusCode(500, new
                {
                    error = "Failed to initiate call",
                    message = ex.Message
                });
            }
        }

        [HttpPost("hangup/{callConnectionId}")]
        public async Task<IActionResult> HangUp(string callConnectionId)
        {
            if (!_callService.ActiveCalls.ContainsKey(callConnectionId))
            {
                return NotFound(new
                {
                    error = "Call not found",
                    message = $"No active call found with ID '{callConnectionId}'"
                });
            }

            await _callService.HangUpCall(callConnectionId);

            return Ok(new
            {
                status = "Call terminated",
                callConnectionId
            });
        }

        [HttpGet("active")]
        public IActionResult GetActiveCalls()
        {
            var calls = _callService.ActiveCalls.Values.Select(c => new
            {
                callConnectionId = c.CallConnectionId,
                targetPhoneNumber = c.TargetPhoneNumber,
                campaignTitle = c.CampaignTitle,
                status = c.Status.ToString(),
                startedAt = c.StartedAt
            }).ToList();

            return Ok(new
            {
                count = calls.Count,
                maxConcurrent = 5,
                calls
            });
        }
    }
}
