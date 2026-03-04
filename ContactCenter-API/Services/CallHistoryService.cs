using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using ContactCenterPOC.Models;
using System.Text.Json;

namespace ContactCenterPOC.Services
{
    public class CallHistoryService
    {
        private readonly BlobServiceClient _blobServiceClient;
        private readonly IConfiguration _configuration;
        private readonly ILogger<CallHistoryService> _logger;
        private readonly string _containerName;
        private readonly string _historyPrefix = "call-history/";
        private const string AcsMetadataFileName = "0-acsmetadata.json";
        private readonly List<CallHistorySummary> _summaryCache = new();
        private bool _cacheLoaded = false;
        private DateTimeOffset _cacheLoadedAtUtc = DateTimeOffset.MinValue;
        private readonly TimeSpan _cacheTtl;
        private readonly SemaphoreSlim _cacheLock = new(1, 1);

        private static readonly JsonSerializerOptions _writeOptions = new()
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        };

        private static readonly JsonSerializerOptions _readOptions = new()
        {
            PropertyNameCaseInsensitive = true
        };

        public CallHistoryService(BlobServiceClient blobServiceClient, IConfiguration configuration, ILogger<CallHistoryService> logger)
        {
            _blobServiceClient = blobServiceClient;
            _configuration = configuration;
            _logger = logger;
            _containerName = configuration["BlobStorage:ContainerName"] ?? "callcenter-data";

            _cacheTtl = TimeSpan.FromSeconds(5);
            if (int.TryParse(configuration["CallHistory:CacheTtlSeconds"], out var cacheTtlSeconds) && cacheTtlSeconds > 0)
            {
                _cacheTtl = TimeSpan.FromSeconds(cacheTtlSeconds);
            }

            var blobContainerUrl = configuration["BlobContainer"];
            if (!string.IsNullOrWhiteSpace(blobContainerUrl))
            {
                _logger.LogInformation("CallHistoryService using container '{ContainerName}' (BlobContainer configured)", _containerName);
            }
            else
            {
                _logger.LogInformation("CallHistoryService using container '{ContainerName}'", _containerName);
            }
        }

        public async Task SaveCallRecordAsync(CallRecord record)
        {
            try
            {
                var containerClient = _blobServiceClient.GetBlobContainerClient(_containerName);

                // Saving a record should not attempt to create the container.
                // Container creation can fail under constrained permissions/network rules.
                var exists = await containerClient.ExistsAsync();
                if (!exists.Value)
                {
                    _logger.LogWarning("Blob container '{ContainerName}' does not exist; cannot save call record {CallConnectionId}", _containerName, record.CallConnectionId);
                    return;
                }

                var blobName = $"{_historyPrefix}{record.CallConnectionId}.json";
                var blobClient = containerClient.GetBlobClient(blobName);

                var json = JsonSerializer.Serialize(record, _writeOptions);
                using var stream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(json));
                await blobClient.UploadAsync(stream, overwrite: true);

                // Update summary cache
                await _cacheLock.WaitAsync();
                try
                {
                    UpsertSummaryLocked(record);
                    _cacheLoaded = true;
                    _cacheLoadedAtUtc = DateTimeOffset.UtcNow;
                }
                finally
                {
                    _cacheLock.Release();
                }

                _logger.LogInformation("Saved call record for {CallConnectionId}", record.CallConnectionId);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to save call record for {CallConnectionId}", record.CallConnectionId);
            }
        }

        public async Task<bool> SaveRecordingTranscriptAsync(string callConnectionId, string transcript)
        {
            if (string.IsNullOrWhiteSpace(callConnectionId)) return false;
            if (string.IsNullOrWhiteSpace(transcript)) return false;

            var record = await GetByIdAsync(callConnectionId);
            if (record == null) return false;

            record.RecordingTranscript = transcript;
            record.RecordingTranscribedAt = DateTimeOffset.UtcNow;
            await SaveCallRecordAsync(record);
            return true;
        }

        public async Task<List<CallHistorySummary>> GetAllAsync()
        {
            await EnsureCacheLoadedAsync();

            await _cacheLock.WaitAsync();
            try
            {
                return _summaryCache.ToList();
            }
            finally
            {
                _cacheLock.Release();
            }
        }

        public async Task<PagedResult<CallHistorySummary>> GetPagedAsync(int page = 1, int pageSize = 20)
        {
            await EnsureCacheLoadedAsync();

            await _cacheLock.WaitAsync();
            try
            {
                var totalCount = _summaryCache.Count;
                var items = _summaryCache
                    .Skip((page - 1) * pageSize)
                    .Take(pageSize)
                    .ToList();

                return new PagedResult<CallHistorySummary>
                {
                    TotalCount = totalCount,
                    Page = page,
                    PageSize = pageSize,
                    Items = items
                };
            }
            finally
            {
                _cacheLock.Release();
            }
        }

        public async Task<CallRecord?> GetByIdAsync(string callConnectionId)
        {
            try
            {
                var containerClient = _blobServiceClient.GetBlobContainerClient(_containerName);
                var blobName = $"{_historyPrefix}{callConnectionId}.json";
                var blobClient = containerClient.GetBlobClient(blobName);

                if (!await blobClient.ExistsAsync())
                    return null;

                var response = await blobClient.DownloadContentAsync();
                var json = response.Value.Content.ToString();
                return JsonSerializer.Deserialize<CallRecord>(json, _readOptions);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to load call record {CallConnectionId}", callConnectionId);
                return null;
            }
        }

        private async Task EnsureCacheLoadedAsync()
        {
            if (_cacheLoaded && (DateTimeOffset.UtcNow - _cacheLoadedAtUtc) < _cacheTtl) return;

            await _cacheLock.WaitAsync();
            try
            {
                if (_cacheLoaded && (DateTimeOffset.UtcNow - _cacheLoadedAtUtc) < _cacheTtl) return;

                _cacheLoaded = await LoadSummaryCacheAsync();
                if (_cacheLoaded)
                {
                    _cacheLoadedAtUtc = DateTimeOffset.UtcNow;
                }
            }
            finally
            {
                _cacheLock.Release();
            }
        }

        private async Task<bool> LoadSummaryCacheAsync()
        {
            try
            {
                var containerClient = _blobServiceClient.GetBlobContainerClient(_containerName);

                // Loading history should be a read-only operation. Avoid creating the container here,
                // since container creation can fail under constrained permissions/network rules.
                var exists = await containerClient.ExistsAsync();
                if (!exists.Value)
                {
                    _summaryCache.Clear();
                    _logger.LogInformation("Blob container '{ContainerName}' does not exist; call history is empty", _containerName);
                    return true;
                }

                var records = new List<CallRecord>();

                await LoadCallHistoryRecordsAsync(containerClient, records);

                // If there are no saved call records, attempt to reconstruct history from ACS recording metadata.
                // This helps populate history for calls made before proper call-history persistence was available.
                if (records.Count == 0)
                {
                    var rebuilt = await TryRebuildFromAcsRecordingMetadataAsync(containerClient);
                    if (rebuilt.Count > 0)
                    {
                        records.AddRange(rebuilt);
                    }
                }

                // Sort by most recent first
                _summaryCache.Clear();
                _summaryCache.AddRange(
                    records.OrderByDescending(r => r.StartedAt)
                           .Select(ToSummary));

                _logger.LogInformation("Loaded {Count} call history records from Blob Storage", _summaryCache.Count);
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to load call history from Blob Storage");
                return false;
            }
        }

        private async Task LoadCallHistoryRecordsAsync(BlobContainerClient containerClient, List<CallRecord> records)
        {
            await foreach (var blobItem in containerClient.GetBlobsAsync(prefix: _historyPrefix))
            {
                try
                {
                    var blobClient = containerClient.GetBlobClient(blobItem.Name);
                    var response = await blobClient.DownloadContentAsync();
                    var json = response.Value.Content.ToString();
                    var record = JsonSerializer.Deserialize<CallRecord>(json, _readOptions);
                    if (record != null)
                    {
                        records.Add(record);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to load call record blob {BlobName}", blobItem.Name);
                }
            }
        }

        private async Task<List<CallRecord>> TryRebuildFromAcsRecordingMetadataAsync(BlobContainerClient containerClient)
        {
            var rebuilt = new List<CallRecord>();

            try
            {
                var metadataBlobNames = new List<string>();
                await foreach (BlobItem blobItem in containerClient.GetBlobsAsync())
                {
                    // ACS recording metadata for the first chunk is consistently named "0-acsmetadata.json".
                    // We only use chunk 0 to avoid duplicates.
                    if (blobItem.Name.EndsWith("/" + AcsMetadataFileName, StringComparison.OrdinalIgnoreCase))
                    {
                        metadataBlobNames.Add(blobItem.Name);
                    }
                }

                if (metadataBlobNames.Count == 0)
                {
                    return rebuilt;
                }

                foreach (var metadataBlobName in metadataBlobNames)
                {
                    try
                    {
                        var parsed = ParseAcsMetadataBlobName(metadataBlobName);
                        if (parsed == null)
                        {
                            continue;
                        }

                        var (callId, recordingId) = parsed.Value;
                        var blobClient = containerClient.GetBlobClient(metadataBlobName);
                        var response = await blobClient.DownloadContentAsync();
                        var json = response.Value.Content.ToString();

                        var meta = JsonSerializer.Deserialize<AcsRecordingChunkMetadata>(json, _readOptions);
                        if (meta == null || string.IsNullOrWhiteSpace(meta.CallId))
                        {
                            continue;
                        }

                        var startedAt = meta.ChunkStartTime ?? DateTimeOffset.UtcNow;
                        var duration = TimeSpan.FromMilliseconds(meta.ChunkDurationMs ?? 0);
                        var endedAt = startedAt + duration;

                        var phoneNumber = ExtractPhoneNumber(meta);

                        var record = new CallRecord
                        {
                            CallConnectionId = callId,
                            PhoneNumber = phoneNumber ?? string.Empty,
                            Prompt = string.Empty,
                            RecordingId = recordingId,
                            Duration = duration,
                            OverallSentiment = SentimentLabel.Neutral,
                            SentimentBreakdown = new SentimentBreakdown(),
                            TalkTimeRatio = new TalkTimeRatio(),
                            TranscriptEntries = new List<TranscriptEntry>(),
                            StartedAt = startedAt,
                            EndedAt = endedAt
                        };

                        // Persist into call-history/ so the UI can fetch details and recordings by ID.
                        await UploadCallRecordBlobAsync(containerClient, record);
                        rebuilt.Add(record);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Failed to rebuild call history from ACS metadata blob {BlobName}", metadataBlobName);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to rebuild call history from ACS recording metadata");
            }

            return rebuilt;
        }

        private async Task UploadCallRecordBlobAsync(BlobContainerClient containerClient, CallRecord record)
        {
            var blobName = $"{_historyPrefix}{record.CallConnectionId}.json";
            var blobClient = containerClient.GetBlobClient(blobName);

            var json = JsonSerializer.Serialize(record, _writeOptions);
            using var stream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(json));
            await blobClient.UploadAsync(stream, overwrite: false);
        }

        private static (string callId, string recordingId)? ParseAcsMetadataBlobName(string blobName)
        {
            // Expected shape: {date}/{callId}/{recordingId}/0-acsmetadata.json
            // Example: 20250131/<callId>/<recordingId>/0-acsmetadata.json
            var parts = blobName.Split('/', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 4)
            {
                return null;
            }

            if (!string.Equals(parts[^1], AcsMetadataFileName, StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }

            var callId = parts[^3];
            var recordingId = parts[^2];
            if (string.IsNullOrWhiteSpace(callId) || string.IsNullOrWhiteSpace(recordingId))
            {
                return null;
            }

            return (callId, recordingId);
        }

        private static string? ExtractPhoneNumber(AcsRecordingChunkMetadata meta)
        {
            if (meta.Participants == null || meta.Participants.Count == 0) return null;

            // ACS metadata participant ids for PSTN often look like: "4:+15551234567"
            var pstn = meta.Participants
                .Select(p => p.ParticipantId)
                .FirstOrDefault(id => !string.IsNullOrWhiteSpace(id) && id.StartsWith("4:+", StringComparison.OrdinalIgnoreCase));

            if (!string.IsNullOrWhiteSpace(pstn))
            {
                return pstn.Substring(2);
            }

            return null;
        }

        private sealed class AcsRecordingChunkMetadata
        {
            public string? CallId { get; set; }
            public DateTimeOffset? ChunkStartTime { get; set; }
            public double? ChunkDuration { get; set; }
            public List<AcsParticipant>? Participants { get; set; }

            // Some payloads use different naming. Keep a computed helper to interpret duration.
            public double? ChunkDurationMs => ChunkDuration;
        }

        private sealed class AcsParticipant
        {
            public string? ParticipantId { get; set; }
        }

        private void UpsertSummaryLocked(CallRecord record)
        {
            // Callers must hold _cacheLock.
            _summaryCache.RemoveAll(s => string.Equals(s.CallConnectionId, record.CallConnectionId, StringComparison.OrdinalIgnoreCase));
            _summaryCache.Insert(0, ToSummary(record));
        }

        private static CallHistorySummary ToSummary(CallRecord record)
        {
            return new CallHistorySummary
            {
                CallConnectionId = record.CallConnectionId,
                PhoneNumber = PhoneNumberMasker.Mask(record.PhoneNumber),
                ContactName = record.ContactName,
                CampaignTitle = record.CampaignTitle,
                Duration = record.Duration.ToString(@"hh\:mm\:ss"),
                OverallSentiment = record.OverallSentiment.ToString(),
                HasRecording = !string.IsNullOrEmpty(record.RecordingId),
                StartedAt = record.StartedAt
            };
        }
    }
}
