using System.Text;
using System.Text.Json;
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
        private readonly SentimentAnalysisService _sentimentAnalysisService;
        private readonly ILogger<CallHistoryController> _logger;

        public CallHistoryController(CallHistoryService callHistoryService, CallService callService, BlobServiceClient blobServiceClient, IConfiguration configuration, RecordingTranscriptionService recordingTranscriptionService, SentimentAnalysisService sentimentAnalysisService, ILogger<CallHistoryController> logger)
        {
            _callHistoryService = callHistoryService;
            _callService = callService;
            _blobServiceClient = blobServiceClient;
            _configuration = configuration;
            _recordingTranscriptionService = recordingTranscriptionService;
            _sentimentAnalysisService = sentimentAnalysisService;
            _logger = logger;
        }

        [HttpGet]
        public async Task<IActionResult> GetCallHistory([FromQuery] int page = 1, [FromQuery] int pageSize = 20)
        {
            if (page < 1) page = 1;
            if (pageSize < 1) pageSize = 1;
            if (pageSize > 100) pageSize = 100;

            var result = await _callHistoryService.GetPagedAsync(page, pageSize);
            return Ok(result);
        }

        [HttpGet("{callConnectionId}")]
        public async Task<IActionResult> GetCallDetail(string callConnectionId)
        {
            var record = await _callHistoryService.GetByIdAsync(callConnectionId);
            if (record == null)
            {
                return NotFound(new { error = "Not found", message = $"No call record found for ID '{callConnectionId}'" });
            }

            record.PhoneNumber = PhoneNumberMasker.Mask(record.PhoneNumber);
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
                var recordingId = record.RecordingId;

                // Strategy 1: If ServerCallId is available, search by prefix (most efficient)
                // Blob path structure: <date>/<serverCallId>/<blobRecordingId>/0-audiomp3.mp3
                if (!string.IsNullOrEmpty(record.ServerCallId))
                {
                    var datePrefix = record.StartedAt.ToString("yyyyMMdd");
                    var prefix = $"{datePrefix}/{record.ServerCallId}";
                    _logger.LogInformation("Searching for recording by ServerCallId prefix {Prefix}", prefix);

                    await foreach (var blobItem in containerClient.GetBlobsAsync(prefix: prefix))
                    {
                        if (IsAudioBlob(blobItem.Name, out var contentType))
                        {
                            _logger.LogInformation("Found recording blob via ServerCallId: {BlobName}", blobItem.Name);
                            return await StreamBlobAsync(containerClient, blobItem.Name, contentType);
                        }
                    }
                }

                // Strategy 2: Search by recording ID / extracted IDs (works for old format)
                var searchTerms = new List<string> { recordingId };
                try
                {
                    var padded = recordingId.PadRight((recordingId.Length + 3) / 4 * 4, '=');
                    var decoded = Encoding.UTF8.GetString(Convert.FromBase64String(padded));
                    using var doc = JsonDocument.Parse(decoded);
                    if (doc.RootElement.TryGetProperty("ResourceSpecificId", out var rsid) && rsid.GetString() is string rsidStr)
                        searchTerms.Add(rsidStr);
                    if (doc.RootElement.TryGetProperty("PlatformEndpointId", out var peid) && peid.GetString() is string peidStr)
                        searchTerms.Add(peidStr);
                }
                catch { /* Not base64/JSON — use original recording ID */ }

                _logger.LogInformation("Searching for recording blobs with {SearchTermCount} search terms in container {Container}", searchTerms.Count, containerName);

                await foreach (var blobItem in containerClient.GetBlobsAsync())
                {
                    if (!searchTerms.Any(term => blobItem.Name.Contains(term, StringComparison.OrdinalIgnoreCase)))
                        continue;
                    if (IsAudioBlob(blobItem.Name, out var contentType))
                    {
                        _logger.LogInformation("Found recording blob via search terms: {BlobName}", blobItem.Name);
                        return await StreamBlobAsync(containerClient, blobItem.Name, contentType);
                    }
                }

                // Strategy 3: Scan metadata files by date to find matching call by phone number
                // This handles existing records where ServerCallId was not persisted
                if (string.IsNullOrEmpty(record.ServerCallId) && !string.IsNullOrEmpty(record.PhoneNumber))
                {
                    var datePrefix = record.StartedAt.ToString("yyyyMMdd");
                    _logger.LogInformation("Falling back to metadata scan for date {DatePrefix}, phone {Phone}", datePrefix, record.PhoneNumber);

                    // Collect all metadata files for the date
                    await foreach (var blobItem in containerClient.GetBlobsAsync(prefix: datePrefix))
                    {
                        if (!blobItem.Name.EndsWith("acsmetadata.json", StringComparison.OrdinalIgnoreCase))
                            continue;

                        try
                        {
                            var metaClient = containerClient.GetBlobClient(blobItem.Name);
                            var metaResponse = await metaClient.DownloadContentAsync();
                            var metaJson = metaResponse.Value.Content.ToString();
                            using var metaDoc = JsonDocument.Parse(metaJson);

                            // Check if any participant matches this call's phone number
                            if (metaDoc.RootElement.TryGetProperty("participants", out var participants))
                            {
                                foreach (var participant in participants.EnumerateArray())
                                {
                                    var participantId = participant.GetProperty("participantId").GetString() ?? "";
                                    // ACS participant format: "4:+<phone>" — check if it contains the phone number
                                    if (participantId.Contains(record.PhoneNumber.TrimStart('+'), StringComparison.OrdinalIgnoreCase))
                                    {
                                        // Found matching metadata! The audio file is in the same directory
                                        var directory = blobItem.Name.Substring(0, blobItem.Name.LastIndexOf('/'));
                                        _logger.LogInformation("Matched recording via metadata for phone {Phone}: directory {Dir}", record.PhoneNumber, directory);

                                        // Also backfill the ServerCallId from metadata
                                        if (metaDoc.RootElement.TryGetProperty("callId", out var callIdProp) && callIdProp.GetString() is string metaCallId)
                                        {
                                            record.ServerCallId = metaCallId;
                                            await _callHistoryService.SaveCallRecordAsync(record);
                                            _logger.LogInformation("Backfilled ServerCallId {ServerCallId} for call {CallConnectionId}", metaCallId, callConnectionId);
                                        }

                                        // Find audio file in same directory
                                        await foreach (var audioBlobItem in containerClient.GetBlobsAsync(prefix: directory))
                                        {
                                            if (IsAudioBlob(audioBlobItem.Name, out var ct))
                                            {
                                                _logger.LogInformation("Found recording blob via metadata match: {BlobName}", audioBlobItem.Name);
                                                return await StreamBlobAsync(containerClient, audioBlobItem.Name, ct);
                                            }
                                        }
                                    }
                                }
                            }
                        }
                        catch (Exception metaEx)
                        {
                            _logger.LogWarning(metaEx, "Failed to read metadata blob {BlobName}", blobItem.Name);
                        }
                    }
                }

                _logger.LogWarning("No recording found for call {CallConnectionId} with recording ID {RecordingId}", callConnectionId, recordingId);

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
                record.PhoneNumber = PhoneNumberMasker.Mask(record.PhoneNumber);
                return Ok(record);
            }

            try
            {
                var transcript = await _recordingTranscriptionService.TranscribeRecordingAsync(record.RecordingId, cancellationToken);
                await _callHistoryService.SaveRecordingTranscriptAsync(callConnectionId, transcript);

                record.RecordingTranscript = transcript;
                record.RecordingTranscribedAt = DateTimeOffset.UtcNow;

                record.PhoneNumber = PhoneNumberMasker.Mask(record.PhoneNumber);
                return Ok(record);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to transcribe recording for call {CallConnectionId}", callConnectionId);
                return StatusCode(500, new { error = "Transcription failed", message = "Unable to transcribe this recording right now." });
            }
        }

        [HttpPost("batch-process")]
        public async Task<IActionResult> BatchProcess([FromQuery] bool force = false, CancellationToken cancellationToken = default)
        {
            var summaries = await _callHistoryService.GetAllAsync();
            var results = new List<object>();
            int transcribed = 0, sentimentAnalyzed = 0, skipped = 0, failed = 0;

            _logger.LogInformation("Batch processing {Count} call records (force={Force})", summaries.Count, force);

            foreach (var summary in summaries)
            {
                if (cancellationToken.IsCancellationRequested) break;

                var record = await _callHistoryService.GetByIdAsync(summary.CallConnectionId);
                if (record == null)
                {
                    skipped++;
                    continue;
                }

                bool changed = false;
                string? transcriptError = null;
                string? sentimentError = null;

                // Step 1: Transcribe recording if we have a recording but no transcript
                if (!string.IsNullOrWhiteSpace(record.RecordingId) && (force || string.IsNullOrWhiteSpace(record.RecordingTranscript)))
                {
                    try
                    {
                        var transcript = await _recordingTranscriptionService.TranscribeRecordingAsync(
                            record.RecordingId, cancellationToken, record.ServerCallId, record.StartedAt);
                        record.RecordingTranscript = transcript;
                        record.RecordingTranscribedAt = DateTimeOffset.UtcNow;
                        changed = true;
                        transcribed++;
                        _logger.LogInformation("Transcribed recording for call {CallConnectionId}", record.CallConnectionId);
                    }
                    catch (Exception ex)
                    {
                        transcriptError = ex.Message;
                        _logger.LogWarning(ex, "Failed to transcribe recording for call {CallConnectionId}", record.CallConnectionId);
                        failed++;
                    }
                }

                // Step 2: Analyze sentiment if we have a transcript but no meaningful sentiment data
                var hasTranscript = !string.IsNullOrWhiteSpace(record.RecordingTranscript);
                var hasSentiment = record.OverallSentiment != Models.SentimentLabel.Neutral ||
                    (record.SentimentBreakdown != null &&
                     (record.SentimentBreakdown.PositivePercent > 0 || record.SentimentBreakdown.NegativePercent > 0));

                if (hasTranscript && (force || !hasSentiment))
                {
                    try
                    {
                        var sentimentResult = await _sentimentAnalysisService.AnalyzeAsync(record.RecordingTranscript);
                        record.OverallSentiment = sentimentResult.Label;

                        // For a single-text analysis, set breakdown based on the overall result
                        record.SentimentBreakdown = new Models.SentimentBreakdown
                        {
                            PositivePercent = sentimentResult.Label == Models.SentimentLabel.Positive ? sentimentResult.Confidence * 100f : 0f,
                            NeutralPercent = sentimentResult.Label == Models.SentimentLabel.Neutral ? sentimentResult.Confidence * 100f : 0f,
                            NegativePercent = sentimentResult.Label == Models.SentimentLabel.Negative ? sentimentResult.Confidence * 100f : 0f
                        };

                        // Normalize percentages to sum to 100
                        var total = record.SentimentBreakdown.PositivePercent + record.SentimentBreakdown.NeutralPercent + record.SentimentBreakdown.NegativePercent;
                        if (total > 0)
                        {
                            var remaining = 100f - (record.SentimentBreakdown.PositivePercent + record.SentimentBreakdown.NegativePercent);
                            if (sentimentResult.Label != Models.SentimentLabel.Neutral)
                            {
                                record.SentimentBreakdown.NeutralPercent = remaining > 0f ? remaining : 0f;
                            }
                        }

                        changed = true;
                        sentimentAnalyzed++;
                        _logger.LogInformation("Analyzed sentiment for call {CallConnectionId}: {Sentiment}", record.CallConnectionId, sentimentResult.Label);
                    }
                    catch (Exception ex)
                    {
                        sentimentError = ex.Message;
                        _logger.LogWarning(ex, "Failed to analyze sentiment for call {CallConnectionId}", record.CallConnectionId);
                    }
                }

                if (changed)
                {
                    await _callHistoryService.SaveCallRecordAsync(record);
                }
                else if (transcriptError == null && sentimentError == null)
                {
                    skipped++;
                }

                results.Add(new
                {
                    callConnectionId = record.CallConnectionId,
                    phoneNumber = PhoneNumberMasker.Mask(record.PhoneNumber),
                    hasRecording = !string.IsNullOrEmpty(record.RecordingId),
                    transcribed = changed && transcriptError == null && !string.IsNullOrWhiteSpace(record.RecordingTranscript),
                    sentiment = record.OverallSentiment.ToString(),
                    transcriptError,
                    sentimentError
                });
            }

            _logger.LogInformation("Batch processing complete: {Transcribed} transcribed, {SentimentAnalyzed} sentiment analyzed, {Skipped} skipped, {Failed} failed",
                transcribed, sentimentAnalyzed, skipped, failed);

            return Ok(new
            {
                total = summaries.Count,
                transcribed,
                sentimentAnalyzed,
                skipped,
                failed,
                results
            });
        }

        private static bool IsAudioBlob(string name, out string contentType)
        {
            if (name.EndsWith(".mp3", StringComparison.OrdinalIgnoreCase))
            {
                contentType = "audio/mpeg";
                return true;
            }
            if (name.EndsWith(".wav", StringComparison.OrdinalIgnoreCase))
            {
                contentType = "audio/wav";
                return true;
            }
            contentType = string.Empty;
            return false;
        }

        private async Task<FileStreamResult> StreamBlobAsync(Azure.Storage.Blobs.BlobContainerClient containerClient, string blobName, string contentType)
        {
            var blobClient = containerClient.GetBlobClient(blobName);
            var stream = await blobClient.OpenReadAsync(new Azure.Storage.Blobs.Models.BlobOpenReadOptions(allowModifications: false));
            return new FileStreamResult(stream, contentType)
            {
                EnableRangeProcessing = true
            };
        }
    }
}
