using Azure.Core;
using Azure.Identity;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using System.Net.Http.Headers;

namespace ContactCenterPOC.Services
{
    public class RecordingTranscriptionService
    {
        private static readonly string[] TokenScopes = ["https://cognitiveservices.azure.com/.default"];

        private readonly BlobServiceClient _blobServiceClient;
        private readonly IConfiguration _configuration;
        private readonly ILogger<RecordingTranscriptionService> _logger;
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly TokenCredential _credential;

        public RecordingTranscriptionService(
            BlobServiceClient blobServiceClient,
            IConfiguration configuration,
            ILogger<RecordingTranscriptionService> logger,
            IHttpClientFactory httpClientFactory)
        {
            _blobServiceClient = blobServiceClient;
            _configuration = configuration;
            _logger = logger;
            _httpClientFactory = httpClientFactory;
            _credential = new DefaultAzureCredential();
        }

        public async Task<string> TranscribeRecordingAsync(string recordingId, CancellationToken cancellationToken, string? serverCallId = null, DateTimeOffset? startedAt = null)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(recordingId);

            var endpoint = _configuration["AzureOpenAI:EndpointUri"];
            ArgumentException.ThrowIfNullOrWhiteSpace(endpoint);

            var deployment = _configuration["AzureOpenAI:TranscriptionDeployment"]
                ?? _configuration["AzureOpenAI:WhisperDeployment"]
                ?? _configuration["AzureOpenAI:AudioTranscriptionDeployment"];

            if (string.IsNullOrWhiteSpace(deployment))
            {
                throw new InvalidOperationException(
                    "Azure OpenAI transcription deployment is not configured. Set AzureOpenAI:TranscriptionDeployment (a deployed audio model like whisper-1 or gpt-4o-mini-transcribe)."
                );
            }

            var apiVersion = _configuration["AzureOpenAI:TranscriptionApiVersion"]
                ?? _configuration["AzureOpenAI:Version"]
                ?? "2024-10-21";

            var language = _configuration["AzureOpenAI:TranscriptionLanguage"]; // optional ISO-639-1 (e.g., 'en')

            await using var audioStream = await DownloadRecordingAsync(recordingId, cancellationToken, serverCallId, startedAt);
            var fileName = GuessFileName(recordingId);
            var contentType = GuessContentType(fileName);

            var url = $"{endpoint.TrimEnd('/')}/openai/deployments/{Uri.EscapeDataString(deployment)}/audio/transcriptions?api-version={Uri.EscapeDataString(apiVersion)}";

            var token = await _credential.GetTokenAsync(new TokenRequestContext(TokenScopes), cancellationToken);

            using var request = new HttpRequestMessage(HttpMethod.Post, url);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token.Token);
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/plain"));

            using var form = new MultipartFormDataContent();
            var fileContent = new StreamContent(audioStream);
            fileContent.Headers.ContentType = new MediaTypeHeaderValue(contentType);
            form.Add(fileContent, "file", fileName);

            // Ask for plain text to keep storage simple.
            form.Add(new StringContent("text"), "response_format");

            if (!string.IsNullOrWhiteSpace(language))
            {
                form.Add(new StringContent(language), "language");
            }

            request.Content = form;

            var client = _httpClientFactory.CreateClient("AzureOpenAITranscription");
            using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            var body = (await response.Content.ReadAsStringAsync(cancellationToken)).Trim();

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning(
                    "Azure OpenAI transcription failed: status={Status} body={Body}",
                    (int)response.StatusCode,
                    body.Length > 2000 ? body[..2000] : body
                );

                throw new InvalidOperationException($"Transcription failed ({(int)response.StatusCode}).");
            }

            return body;
        }

        private async Task<Stream> DownloadRecordingAsync(string recordingId, CancellationToken cancellationToken, string? serverCallId = null, DateTimeOffset? startedAt = null)
        {
            // If ACS ever provides an absolute blob URL (SAS or non-SAS), use it directly.
            if (Uri.TryCreate(recordingId, UriKind.Absolute, out var recordingUri) &&
                string.Equals(recordingUri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
            {
                try
                {
                    var hasSas = recordingUri.Query.Contains("sig=", StringComparison.OrdinalIgnoreCase);
                    var blobClient = hasSas
                        ? new BlobClient(recordingUri)
                        : new BlobClient(recordingUri, _credential);

                    var direct = await blobClient.DownloadStreamingAsync(cancellationToken: cancellationToken);
                    return direct.Value.Content;
                }
                catch (Exception ex)
                {
                    _logger.LogInformation(ex, "Direct download via recordingId URI failed; falling back to container search.");
                }
            }

            var blobContainerUrl = _configuration["BlobContainer"];
            if (string.IsNullOrWhiteSpace(blobContainerUrl))
            {
                throw new InvalidOperationException("BlobContainer URL is not configured; cannot locate recordings in storage.");
            }

            var containerUri = new Uri(blobContainerUrl);
            var containerName = containerUri.AbsolutePath.TrimStart('/').Split('/')[0];

            var containerClient = _blobServiceClient.GetBlobContainerClient(containerName);

            // Strategy 1: If ServerCallId is available, search by prefix (most efficient)
            if (!string.IsNullOrEmpty(serverCallId) && startedAt.HasValue)
            {
                var datePrefix = startedAt.Value.ToString("yyyyMMdd");
                var prefix = $"{datePrefix}/{serverCallId}";
                _logger.LogInformation("Searching for recording blob by prefix {Prefix}", prefix);

                await foreach (var blobItem in containerClient.GetBlobsAsync(prefix: prefix, cancellationToken: cancellationToken))
                {
                    if (blobItem.Name.EndsWith(".mp3", StringComparison.OrdinalIgnoreCase) ||
                        blobItem.Name.EndsWith(".wav", StringComparison.OrdinalIgnoreCase))
                    {
                        _logger.LogInformation("Found recording blob via prefix: {BlobName}", blobItem.Name);
                        var prefixBlob = containerClient.GetBlobClient(blobItem.Name);
                        var prefixDownload = await prefixBlob.DownloadStreamingAsync(cancellationToken: cancellationToken);
                        return prefixDownload.Value.Content;
                    }
                }
            }

            // Strategy 2: Search by recording ID (works for old format IDs that appear in blob names)
            // Also try decoded base64 components for new format IDs
            var searchTerms = new List<string> { recordingId };
            try
            {
                var padded = recordingId.PadRight((recordingId.Length + 3) / 4 * 4, '=');
                var decoded = System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(padded));
                using var doc = System.Text.Json.JsonDocument.Parse(decoded);
                if (doc.RootElement.TryGetProperty("ResourceSpecificId", out var rsid) && rsid.GetString() is string rsidStr)
                    searchTerms.Add(rsidStr);
                if (doc.RootElement.TryGetProperty("PlatformEndpointId", out var peid) && peid.GetString() is string peidStr)
                    searchTerms.Add(peidStr);
            }
            catch { /* Not base64/JSON — use original recording ID */ }

            BlobItem? best = null;

            await foreach (var blobItem in containerClient.GetBlobsAsync(cancellationToken: cancellationToken))
            {
                if (!searchTerms.Any(term => blobItem.Name.Contains(term, StringComparison.OrdinalIgnoreCase)))
                {
                    continue;
                }

                if (!blobItem.Name.EndsWith(".mp3", StringComparison.OrdinalIgnoreCase) &&
                    !blobItem.Name.EndsWith(".wav", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (best == null)
                {
                    best = blobItem;
                    continue;
                }

                var bestModified = best.Properties.LastModified ?? DateTimeOffset.MinValue;
                var candidateModified = blobItem.Properties.LastModified ?? DateTimeOffset.MinValue;

                if (candidateModified > bestModified)
                {
                    best = blobItem;
                }
            }

            if (best == null)
            {
                throw new FileNotFoundException($"No recording blob found for recordingId '{recordingId}'.");
            }

            _logger.LogInformation("Found recording blob for transcription: {BlobName}", best.Name);
            var chosenBlob = containerClient.GetBlobClient(best.Name);
            var download = await chosenBlob.DownloadStreamingAsync(cancellationToken: cancellationToken);
            return download.Value.Content;
        }

        private static string GuessFileName(string recordingId)
        {
            // Keep it deterministic for multipart/form-data; actual name doesn't matter to the API.
            if (recordingId.Contains(".wav", StringComparison.OrdinalIgnoreCase)) return "recording.wav";
            return "recording.mp3";
        }

        private static string GuessContentType(string fileName)
        {
            if (fileName.EndsWith(".wav", StringComparison.OrdinalIgnoreCase)) return "audio/wav";
            if (fileName.EndsWith(".mp3", StringComparison.OrdinalIgnoreCase)) return "audio/mpeg";
            return "application/octet-stream";
        }
    }
}
