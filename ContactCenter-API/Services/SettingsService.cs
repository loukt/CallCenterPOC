using Azure.Storage.Blobs;
using ContactCenterPOC.Models;
using System.Text.Json;

namespace ContactCenterPOC.Services
{
    public class SettingsService
    {
        private readonly BlobServiceClient _blobServiceClient;
        private readonly string _containerName;
        private readonly ILogger<SettingsService> _logger;
        private const string SettingsBlobName = "settings.json";

        private OperatorSettings? _cached;
        private DateTimeOffset _cachedAt = DateTimeOffset.MinValue;
        private readonly TimeSpan _cacheTtl = TimeSpan.FromSeconds(30);
        private readonly SemaphoreSlim _lock = new(1, 1);

        private static readonly JsonSerializerOptions _writeOptions = new()
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        };

        private static readonly JsonSerializerOptions _readOptions = new()
        {
            PropertyNameCaseInsensitive = true
        };

        public SettingsService(BlobServiceClient blobServiceClient, IConfiguration configuration, ILogger<SettingsService> logger)
        {
            _blobServiceClient = blobServiceClient;
            _containerName = configuration["BlobStorage:ContainerName"] ?? "callcenter-data";
            _logger = logger;
        }

        public async Task<OperatorSettings> GetSettingsAsync()
        {
            if (_cached != null && (DateTimeOffset.UtcNow - _cachedAt) < _cacheTtl)
            {
                return _cached;
            }

            await _lock.WaitAsync();
            try
            {
                if (_cached != null && (DateTimeOffset.UtcNow - _cachedAt) < _cacheTtl)
                {
                    return _cached;
                }

                var settings = await LoadFromBlobAsync();
                _cached = settings ?? new OperatorSettings();
                _cachedAt = DateTimeOffset.UtcNow;
                return _cached;
            }
            finally
            {
                _lock.Release();
            }
        }

        public async Task<OperatorSettings> SaveSettingsAsync(OperatorSettings settings)
        {
            await _lock.WaitAsync();
            try
            {
                // Validate
                if (settings.MaxCallTimeMinutes < 0.5)
                    settings.MaxCallTimeMinutes = 0.5;
                if (settings.MaxCallTimeMinutes > 30)
                    settings.MaxCallTimeMinutes = 30;

                if (settings.VoiceApiMode != "ChatGPT" && settings.VoiceApiMode != "VoiceLive")
                    settings.VoiceApiMode = "ChatGPT";

                if (!OperatorSettings.ValidVoices.Contains(settings.SelectedVoice))
                    settings.SelectedVoice = "alloy";

                // VoiceLive-specific validation (mode-conditional)
                if (settings.VoiceApiMode == "VoiceLive")
                {
                    if (!OperatorSettings.ValidVoiceLiveModels.Contains(settings.VoiceLiveModel))
                    {
                        _logger.LogWarning("Unknown VoiceLiveModel '{Model}', defaulting to 'gpt-4o'", settings.VoiceLiveModel);
                        settings.VoiceLiveModel = "gpt-4o";
                    }

                    if (!VoiceLiveVoices.ValidNames.Contains(settings.SelectedVoiceLiveVoice))
                    {
                        _logger.LogWarning("Unknown VoiceLive voice '{Voice}', defaulting to 'en-US-Ava:DragonHDLatestNeural'", settings.SelectedVoiceLiveVoice);
                        settings.SelectedVoiceLiveVoice = "en-US-Ava:DragonHDLatestNeural";
                    }

                    if (!OperatorSettings.ValidTranscriptionModes.Contains(settings.TranscriptionMode))
                    {
                        settings.TranscriptionMode = "BuiltIn";
                    }
                }

                try
                {
                    var containerClient = _blobServiceClient.GetBlobContainerClient(_containerName);
                    var exists = await containerClient.ExistsAsync();
                    if (exists.Value)
                    {
                        var blobClient = containerClient.GetBlobClient(SettingsBlobName);
                        var json = JsonSerializer.Serialize(settings, _writeOptions);
                        using var stream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(json));
                        await blobClient.UploadAsync(stream, overwrite: true);
                        _logger.LogInformation("Settings saved (MaxCallTime={MaxCallTime}min, VoiceApi={VoiceApi}, Voice={Voice})", settings.MaxCallTimeMinutes, settings.VoiceApiMode, settings.SelectedVoice);
                    }
                    else
                    {
                        _logger.LogWarning("Blob container '{ContainerName}' does not exist; settings saved in-memory only", _containerName);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to persist settings to blob storage; saved in-memory only");
                }

                _cached = settings;
                _cachedAt = DateTimeOffset.UtcNow;
                return settings;
            }
            finally
            {
                _lock.Release();
            }
        }

        private async Task<OperatorSettings?> LoadFromBlobAsync()
        {
            try
            {
                var containerClient = _blobServiceClient.GetBlobContainerClient(_containerName);
                var exists = await containerClient.ExistsAsync();
                if (!exists.Value) return null;

                var blobClient = containerClient.GetBlobClient(SettingsBlobName);
                if (!await blobClient.ExistsAsync()) return null;

                var response = await blobClient.DownloadContentAsync();
                var json = response.Value.Content.ToString();
                return JsonSerializer.Deserialize<OperatorSettings>(json, _readOptions);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to load settings from blob storage");
                return null;
            }
        }
    }
}
