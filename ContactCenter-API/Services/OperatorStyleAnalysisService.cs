using Azure.Core;
using Azure.Identity;
using ContactCenterPOC.Models;
using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;

namespace ContactCenterPOC.Services
{
    public class OperatorStyleAnalysisService
    {
        private readonly Uri? _endpointUri;
        private readonly string? _apiKey;
        private readonly string? _deployment;
        private readonly TokenCredential? _credential;
        private readonly ILogger<OperatorStyleAnalysisService> _logger;

        private static readonly HttpClient HttpClient = new()
        {
            Timeout = TimeSpan.FromSeconds(30)
        };

        private const string ChatCompletionsApiVersion = "2024-10-01-preview";
        private const int TraitsMaxCompletionTokens = 300;
        private const string TraitsReasoningEffort = "low";
        private const int TraitsLegacyMaxTokens = 100;
        private const float TraitsLegacyTemperature = 0f;

        private const string TraitsSystemPrompt =
            "You are an operator communication style analyzer. Analyze the operator's transcript from a call and score two traits on a 0.0 to 1.0 scale:\n" +
            "- Empathy: How empathetic, understanding, and compassionate is the operator's language? (0=cold/robotic, 1=highly empathetic)\n" +
            "- Energy: How energetic, enthusiastic, and engaged is the operator's tone? (0=monotone/flat, 1=highly energetic)\n" +
            "Respond ONLY with a JSON object: {\"empathy\":0.0-1.0,\"energy\":0.0-1.0}. No other text or explanation.";

        public OperatorStyleAnalysisService(IConfiguration configuration, ILogger<OperatorStyleAnalysisService> logger)
        {
            _logger = logger;

            var endpointUri = configuration["AzureOpenAI:EndpointUri"];
            var apiKey = configuration["AzureOpenAI:Key"];
            var chatDeployment = configuration["AzureOpenAI:ChatDeployment"]
                ?? configuration["AzureOpenAI:DeploymentName"];

            if (string.IsNullOrEmpty(endpointUri) || string.IsNullOrEmpty(chatDeployment))
            {
                _logger.LogWarning("AzureOpenAI:EndpointUri or AzureOpenAI:ChatDeployment not configured. Operator style analysis disabled.");
                return;
            }

            try
            {
                if (!string.IsNullOrWhiteSpace(apiKey))
                {
                    _apiKey = apiKey;
                }
                else
                {
                    _credential = new DefaultAzureCredential();
                }

                _endpointUri = new Uri(endpointUri);
                _deployment = chatDeployment;
                _logger.LogInformation(
                    "OperatorStyleAnalysisService initialized (endpointHost={EndpointHost}, deployment={Deployment})",
                    new Uri(endpointUri).Host,
                    chatDeployment);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to initialize OperatorStyleAnalysisService");
            }
        }

        public async Task<OperatorStyleTraits?> AnalyzeAsync(IEnumerable<TranscriptEntry> operatorEntries)
        {
            var texts = operatorEntries
                .Where(e => !string.IsNullOrWhiteSpace(e.Text))
                .Select(e => e.Text)
                .ToList();

            if (texts.Count == 0)
            {
                return null;
            }

            if (_endpointUri == null || string.IsNullOrWhiteSpace(_deployment))
            {
                return null;
            }

            try
            {
                var combinedText = string.Join("\n", texts);

                var uri = new Uri(
                    _endpointUri,
                    $"openai/deployments/{Uri.EscapeDataString(_deployment)}/chat/completions?api-version={ChatCompletionsApiVersion}");

                var messages = new object[]
                {
                    new Dictionary<string, object?>
                    {
                        ["role"] = "system",
                        ["content"] = TraitsSystemPrompt
                    },
                    new Dictionary<string, object?>
                    {
                        ["role"] = "user",
                        ["content"] = combinedText
                    }
                };

                var requestBody = new Dictionary<string, object?>
                {
                    ["messages"] = messages,
                    ["max_completion_tokens"] = TraitsMaxCompletionTokens,
                    ["reasoning_effort"] = TraitsReasoningEffort
                };

                var (statusCode, responseBody) = await PostChatCompletionsAsync(uri, requestBody);

                if (statusCode == HttpStatusCode.BadRequest && LooksLikeUnsupportedParam(responseBody))
                {
                    var legacyBody = new Dictionary<string, object?>
                    {
                        ["messages"] = messages,
                        ["max_tokens"] = TraitsLegacyMaxTokens,
                        ["temperature"] = TraitsLegacyTemperature
                    };

                    (statusCode, responseBody) = await PostChatCompletionsAsync(uri, legacyBody);
                }

                if (statusCode != HttpStatusCode.OK)
                {
                    _logger.LogWarning(
                        "Operator style analysis HTTP {StatusCode}. Body: {Body}",
                        (int)statusCode,
                        TruncateForLog(responseBody, 1000));
                    return null;
                }

                var content = ExtractAssistantContent(responseBody);
                if (string.IsNullOrWhiteSpace(content))
                {
                    return null;
                }

                return ParseTraitsJson(content);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Operator style analysis failed, returning null");
                return null;
            }
        }

        private async Task<(HttpStatusCode StatusCode, string Body)> PostChatCompletionsAsync(Uri uri, Dictionary<string, object?> requestBody)
        {
            var request = new HttpRequestMessage(HttpMethod.Post, uri)
            {
                Content = new StringContent(JsonSerializer.Serialize(requestBody))
            };

            request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/json");

            if (!string.IsNullOrWhiteSpace(_apiKey))
            {
                request.Headers.TryAddWithoutValidation("api-key", _apiKey);
            }
            else
            {
                if (_credential == null)
                {
                    return (HttpStatusCode.Unauthorized, "Missing credential");
                }

                var token = await _credential.GetTokenAsync(
                    new TokenRequestContext(["https://cognitiveservices.azure.com/.default"]),
                    CancellationToken.None);

                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token.Token);
            }

            using var response = await HttpClient.SendAsync(request);
            var responseBody = await response.Content.ReadAsStringAsync();
            return (response.StatusCode, responseBody);
        }

        private static bool LooksLikeUnsupportedParam(string responseBody)
        {
            if (string.IsNullOrWhiteSpace(responseBody)) return false;
            return responseBody.IndexOf("Unsupported parameter", StringComparison.OrdinalIgnoreCase) >= 0
                || responseBody.IndexOf("does not support", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static string? ExtractAssistantContent(string responseBody)
        {
            try
            {
                using var doc = JsonDocument.Parse(responseBody);
                var root = doc.RootElement;
                var choices = root.GetProperty("choices");
                if (choices.ValueKind != JsonValueKind.Array || choices.GetArrayLength() == 0)
                    return null;

                var message = choices[0].GetProperty("message");
                if (message.ValueKind != JsonValueKind.Object)
                    return null;

                return message.TryGetProperty("content", out var contentProp)
                    ? contentProp.GetString()
                    : null;
            }
            catch
            {
                return null;
            }
        }

        private static string TruncateForLog(string? value, int maxLength)
        {
            if (string.IsNullOrEmpty(value)) return string.Empty;
            value = value.Replace("\r", " ").Replace("\n", " ");
            return value.Length <= maxLength ? value : value.Substring(0, maxLength) + "…";
        }

        internal static OperatorStyleTraits? ParseTraitsJson(string? json)
        {
            if (string.IsNullOrWhiteSpace(json))
                return null;

            try
            {
                using var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;

                var empathy = root.TryGetProperty("empathy", out var empProp) ? empProp.GetSingle() : 0f;
                var energy = root.TryGetProperty("energy", out var enProp) ? enProp.GetSingle() : 0f;

                return new OperatorStyleTraits
                {
                    Empathy = Math.Clamp(empathy, 0f, 1f),
                    Energy = Math.Clamp(energy, 0f, 1f),
                    ComputedAt = DateTimeOffset.UtcNow
                };
            }
            catch
            {
                // Try extracting JSON from wrapped text
                try
                {
                    var start = json.IndexOf('{');
                    var end = json.LastIndexOf('}');
                    if (start >= 0 && end > start)
                    {
                        var slice = json.Substring(start, end - start + 1);
                        using var doc2 = JsonDocument.Parse(slice);
                        var root2 = doc2.RootElement;

                        var empathy = root2.TryGetProperty("empathy", out var empProp) ? empProp.GetSingle() : 0f;
                        var energy = root2.TryGetProperty("energy", out var enProp) ? enProp.GetSingle() : 0f;

                        return new OperatorStyleTraits
                        {
                            Empathy = Math.Clamp(empathy, 0f, 1f),
                            Energy = Math.Clamp(energy, 0f, 1f),
                            ComputedAt = DateTimeOffset.UtcNow
                        };
                    }
                }
                catch
                {
                    // ignore
                }

                return null;
            }
        }
    }
}
