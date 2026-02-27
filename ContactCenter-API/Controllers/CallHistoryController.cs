using Azure.Storage.Blobs;
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
        private readonly BlobServiceClient _blobServiceClient;
        private readonly IConfiguration _configuration;
        private readonly RecordingTranscriptionService _recordingTranscriptionService;
        private readonly ILogger<CallHistoryController> _logger;

        public CallHistoryController(CallHistoryService callHistoryService, CallService callService, BlobServiceClient blobServiceClient, IConfiguration configuration, RecordingTranscriptionService recordingTranscriptionService, ILogger<CallHistoryController> logger)
        {
            _callHistoryService = callHistoryService;
            _callService = callService;
            _blobServiceClient = blobServiceClient;
            _configuration = configuration;
            _recordingTranscriptionService = recordingTranscriptionService;
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
                // Download recording directly from the ACS recording Blob container
                var blobContainerUrl = _configuration["BlobContainer"];
                if (string.IsNullOrEmpty(blobContainerUrl))
                {
                    return StatusCode(500, new { error = "Configuration error", message = "BlobContainer URL is not configured" });
                }

                // Parse container name from the BlobContainer URL
                var containerUri = new Uri(blobContainerUrl);
                var containerName = containerUri.AbsolutePath.TrimStart('/').Split('/')[0];

                var containerClient = _blobServiceClient.GetBlobContainerClient(containerName);

                // Search for blobs matching the recording ID
                var recordingId = record.RecordingId;
                _logger.LogInformation("Searching for recording blobs with ID {RecordingId} in container {Container}", recordingId, containerName);

                await foreach (var blobItem in containerClient.GetBlobsAsync())
                {
                    if (blobItem.Name.Contains(recordingId, StringComparison.OrdinalIgnoreCase) ||
                        blobItem.Name.EndsWith(".mp3", StringComparison.OrdinalIgnoreCase) ||
                        blobItem.Name.EndsWith(".wav", StringComparison.OrdinalIgnoreCase))
                    {
                        // Check if the blob name contains the recording ID
                        if (blobItem.Name.Contains(recordingId, StringComparison.OrdinalIgnoreCase))
                        {
                            _logger.LogInformation("Found recording blob: {BlobName}", blobItem.Name);
                            var blobClient = containerClient.GetBlobClient(blobItem.Name);
                            var downloadResult = await blobClient.DownloadStreamingAsync();
                            var contentType = blobItem.Name.EndsWith(".wav", StringComparison.OrdinalIgnoreCase)
                                ? "audio/wav"
                                : "audio/mpeg";
                            return File(downloadResult.Value.Content, contentType, $"recording-{callConnectionId}.mp3");
                        }
                    }
                }

                // If exact match not found, try listing all blobs and find the most recent one
                // that could be our recording (ACS may use different naming conventions)
                _logger.LogWarning("No blob matching recording ID {RecordingId} found in container {Container}. Trying ACS download API as fallback.", recordingId, containerName);

                // Fallback: try ACS download API (may work if RecordingId happens to be a valid URI)
                try
                {
                    if (Uri.TryCreate(recordingId, UriKind.Absolute, out var recordingUri))
                    {
                        var recordingClient = _callService.CallAutomationClient.GetCallRecording();
                        var acsDownload = await recordingClient.DownloadStreamingAsync(recordingUri);
                        return File(acsDownload.Value, "audio/mpeg", $"recording-{callConnectionId}.mp3");
                    }
                }
                catch (Exception fallbackEx)
                {
                    _logger.LogWarning(fallbackEx, "ACS download fallback also failed for recording {RecordingId}", recordingId);
                }

                return NotFound(new { error = "Recording not found", message = "The recording file could not be located in storage. It may still be processing or has expired." });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to download recording for call {CallConnectionId}", callConnectionId);
                return StatusCode(500, new { error = "Recording download failed", message = "Unable to retrieve the recording. It may have expired or been deleted." });
            }
        }

        [HttpPost("{callConnectionId}/transcribe")]
        public async Task<IActionResult> TranscribeRecording(string callConnectionId, [FromQuery] bool force = false, CancellationToken cancellationToken = default)
        {
            var record = await _callHistoryService.GetByIdAsync(callConnectionId);
            if (record == null)
            {
                return NotFound(new { error = "Not found", message = $"No call record found for ID '{callConnectionId}'" });
            }

            if (string.IsNullOrWhiteSpace(record.RecordingId))
            {
                return NotFound(new { error = "No recording", message = "No recording is available for this call" });
            }

            if (!force && !string.IsNullOrWhiteSpace(record.RecordingTranscript))
            {
                return Ok(record);
            }

            try
            {
                var transcript = await _recordingTranscriptionService.TranscribeRecordingAsync(record.RecordingId, cancellationToken);
                await _callHistoryService.SaveRecordingTranscriptAsync(callConnectionId, transcript);

                record.RecordingTranscript = transcript;
                record.RecordingTranscribedAt = DateTimeOffset.UtcNow;

                return Ok(record);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to transcribe recording for call {CallConnectionId}", callConnectionId);
                return StatusCode(500, new { error = "Transcription failed", message = "Unable to transcribe this recording right now." });
            }
        }
    }
}
