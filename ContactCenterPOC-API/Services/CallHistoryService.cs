using Azure.Storage.Blobs;
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
        private readonly List<CallHistorySummary> _summaryCache = new();
        private bool _cacheLoaded = false;
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
        }

        public async Task SaveCallRecordAsync(CallRecord record)
        {
            try
            {
                var containerClient = _blobServiceClient.GetBlobContainerClient(_containerName);
                await containerClient.CreateIfNotExistsAsync();

                var blobName = $"{_historyPrefix}{record.CallConnectionId}.json";
                var blobClient = containerClient.GetBlobClient(blobName);

                var json = JsonSerializer.Serialize(record, _writeOptions);
                using var stream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(json));
                await blobClient.UploadAsync(stream, overwrite: true);

                // Update summary cache
                await _cacheLock.WaitAsync();
                try
                {
                    _summaryCache.Insert(0, ToSummary(record));
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
            if (_cacheLoaded) return;

            await _cacheLock.WaitAsync();
            try
            {
                if (_cacheLoaded) return;

                await LoadSummaryCacheAsync();
                _cacheLoaded = true;
            }
            finally
            {
                _cacheLock.Release();
            }
        }

        private async Task LoadSummaryCacheAsync()
        {
            try
            {
                var containerClient = _blobServiceClient.GetBlobContainerClient(_containerName);
                await containerClient.CreateIfNotExistsAsync();

                var records = new List<CallRecord>();

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

                // Sort by most recent first
                _summaryCache.Clear();
                _summaryCache.AddRange(
                    records.OrderByDescending(r => r.StartedAt)
                           .Select(ToSummary));

                _logger.LogInformation("Loaded {Count} call history records from Blob Storage", _summaryCache.Count);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to load call history from Blob Storage");
            }
        }

        private static CallHistorySummary ToSummary(CallRecord record)
        {
            return new CallHistorySummary
            {
                CallConnectionId = record.CallConnectionId,
                PhoneNumber = record.PhoneNumber,
                ContactName = record.ContactName,
                CampaignTitle = record.CampaignTitle,
                Duration = record.Duration.ToString(@"hh\:mm\:ss"),
                OverallSentiment = record.OverallSentiment.ToString(),
                StartedAt = record.StartedAt
            };
        }
    }
}
