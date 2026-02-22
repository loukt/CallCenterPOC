using ContactCenterPOC.Services;
using Microsoft.AspNetCore.Mvc;

namespace ContactCenterPOC.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class CallHistoryController : ControllerBase
    {
        private readonly CallHistoryService _callHistoryService;
        private readonly CallService _callService;
        private readonly ILogger<CallHistoryController> _logger;

        public CallHistoryController(CallHistoryService callHistoryService, CallService callService, ILogger<CallHistoryController> logger)
        {
            _callHistoryService = callHistoryService;
            _callService = callService;
            _logger = logger;
        }

        [HttpGet]
        public async Task<IActionResult> GetCallHistory()
        {
            var history = await _callHistoryService.GetAllAsync();
            return Ok(history);
        }

        [HttpGet("{callConnectionId}")]
        public async Task<IActionResult> GetCallDetail(string callConnectionId)
        {
            var record = await _callHistoryService.GetByIdAsync(callConnectionId);
            if (record == null)
            {
                return NotFound(new { error = "Not found", message = $"No call record found for ID '{callConnectionId}'" });
            }

            return Ok(record);
        }

        [HttpGet("{callConnectionId}/recording")]
        public async Task<IActionResult> GetRecording(string callConnectionId)
        {
            var record = await _callHistoryService.GetByIdAsync(callConnectionId);
            if (record == null)
            {
                return NotFound(new { error = "Not found", message = $"No call record found for ID '{callConnectionId}'" });
            }

            if (string.IsNullOrEmpty(record.RecordingId))
            {
                return NotFound(new { error = "No recording", message = "No recording is available for this call" });
            }

            try
            {
                var recordingClient = _callService.CallAutomationClient.GetCallRecording();
                var downloadResult = await recordingClient.DownloadStreamingAsync(new Uri(record.RecordingId));

                return File(downloadResult.Value, "audio/mpeg", $"recording-{callConnectionId}.mp3");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to download recording for call {CallConnectionId}", callConnectionId);
                return StatusCode(500, new { error = "Recording download failed", message = "Unable to retrieve the recording. It may have expired or been deleted." });
            }
        }
    }
}
