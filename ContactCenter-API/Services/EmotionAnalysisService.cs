using Azure.Core;
using Azure.Identity;
using ContactCenterPOC.Models;
using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;

namespace ContactCenterPOC.Services
{
    public class EmotionAnalysisService
    {
        private readonly Uri? _endpointUri;
        private readonly string? _apiKey;
        private readonly string? _deployment;
        private readonly TokenCredential? _credential;
        private readonly ILogger<EmotionAnalysisService> _logger;

        private static readonly HttpClient HttpClient = new()
        {
            Timeout = TimeSpan.FromSeconds(20)
        };

        private const string ChatCompletionsApiVersion = "2024-10-01-preview";
        private const int EmotionMaxCompletionTokens = 200;
        private const string EmotionReasoningEffort = "low";
        private const int EmotionLegacyMaxTokens = 50;
        private const float EmotionLegacyTemperature = 0f;

        private const string EmotionSystemPrompt =
            "You are an emotion classification service. Classify the emotion of the given text into exactly one of these labels: " +
            "Neutral, Happy, Frustrated, Angry, Sad, Anxious. " +
            "Respond ONLY with a JSON object: {\"label\":\"Neutral|Happy|Frustrated|Angry|Sad|Anxious\",\"confidence\":0.0-1.0}. " +
            "No other text or explanation.";

        public EmotionAnalysisService(IConfiguration configuration, ILogger<EmotionAnalysisService> logger)
        {
            _logger = logger;

            var endpointUri = configuration["AzureOpenAI:EndpointUri"];
            var apiKey = configuration["AzureOpenAI:Key"];
            var emotionDeployment = configuration["AzureOpenAI:EmotionDeployment"];
            var chatDeployment = emotionDeployment
                ?? configuration["AzureOpenAI:ChatDeployment"]
                ?? configuration["AzureOpenAI:DeploymentName"];

            if (string.IsNullOrEmpty(endpointUri) || string.IsNullOrEmpty(chatDeployment))
            {
                _logger.LogWarning("AzureOpenAI:EndpointUri or AzureOpenAI:ChatDeployment not configured. Emotion analysis disabled.");
                return;
            }

            if (!string.IsNullOrEmpty(emotionDeployment))
            {
                _logger.LogInformation("AzureOpenAI:EmotionDeployment is set; using '{Deployment}' for emotion.", chatDeployment);
            }

            try
            {
                if (!string.IsNullOrWhiteSpace(apiKey))
                {
                    _apiKey = apiKey;
                    _logger.LogInformation("EmotionAnalysisService using AzureOpenAI:Key authentication.");
                }
                else
                {
                    _credential = new DefaultAzureCredential();
                    _logger.LogInformation("EmotionAnalysisService using DefaultAzureCredential (Managed Identity / Entra ID).");
                }

                _endpointUri = new Uri(endpointUri);
                _deployment = chatDeployment;
                _logger.LogInformation(
                    "EmotionAnalysisService initialized (endpointHost={EndpointHost}, deployment={Deployment})",
                    new Uri(endpointUri).Host,
                    chatDeployment);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to initialize EmotionAnalysisService");
            }
        }

        public async Task<EmotionResult> AnalyzeAsync(string? text)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return new EmotionResult { Label = EmotionLabel.Neutral, Confidence = 0f };
            }

            if (_endpointUri == null || string.IsNullOrWhiteSpace(_deployment))
            {
                return new EmotionResult { Label = EmotionLabel.Neutral, Confidence = 0f };
            }

            try
            {
                var uri = new Uri(
                    _endpointUri,
                    $"openai/deployments/{Uri.EscapeDataString(_deployment)}/chat/completions?api-version={ChatCompletionsApiVersion}");

                var messages = new object[]
                {
                    new Dictionary<string, object?>
                    {
                        ["role"] = "system",
                        ["content"] = EmotionSystemPrompt
                    },
                    new Dictionary<string, object?>
                    {
                        ["role"] = "user",
                        ["content"] = text
                    }
                };

                // Attempt 1: gpt-5-nano-compatible parameters
                var requestBody = new Dictionary<string, object?>
                {
                    ["messages"] = messages,
                    ["max_completion_tokens"] = EmotionMaxCompletionTokens,
                    ["reasoning_effort"] = EmotionReasoningEffort
                };

                var (statusCode, responseBody) = await PostChatCompletionsAsync(uri, requestBody);

                // Retry with legacy parameters if needed
                if (statusCode == HttpStatusCode.BadRequest && LooksLikeUnsupportedParam(responseBody))
                {
                    var legacyBody = new Dictionary<string, object?>
                    {
                        ["messages"] = messages,
                        ["max_tokens"] = EmotionLegacyMaxTokens,
                        ["temperature"] = EmotionLegacyTemperature
                    };

                    (statusCode, responseBody) = await PostChatCompletionsAsync(uri, legacyBody);
                }

                if (statusCode != HttpStatusCode.OK)
                {
                    _logger.LogWarning(
                        "Emotion analysis HTTP {StatusCode} for text ({Length} chars). Body: {Body}",
                        (int)statusCode,
                        text.Length,
                        TruncateForLog(responseBody, 1000));

                    return new EmotionResult { Label = EmotionLabel.Neutral, Confidence = 0f };
                }

                var content = ExtractAssistantContent(responseBody);
                if (string.IsNullOrWhiteSpace(content))
                {
                    _logger.LogWarning(
                        "Emotion analysis returned empty content for text ({Length} chars).",
                        text.Length);
                    return new EmotionResult { Label = EmotionLabel.Neutral, Confidence = 0f };
                }

                return ParseEmotionJson(content);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Emotion analysis failed for text ({Length} chars), returning Neutral", text.Length);
                return new EmotionResult { Label = EmotionLabel.Neutral, Confidence = 0f };
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
                {
                    return null;
                }

                var message = choices[0].GetProperty("message");
                if (message.ValueKind != JsonValueKind.Object)
                {
                    return null;
                }

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

        public static EmotionResult ParseEmotionJson(string? json)
        {
            if (string.IsNullOrWhiteSpace(json))
            {
                return new EmotionResult { Label = EmotionLabel.Neutral, Confidence = 0f };
            }

            try
            {
                using var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;

                var labelStr = root.GetProperty("label").GetString()?.Trim() ?? "Neutral";
                var confidence = root.TryGetProperty("confidence", out var confProp)
                    ? confProp.GetSingle()
                    : 0f;

                var label = labelStr.ToLowerInvariant() switch
                {
                    "happy" => EmotionLabel.Happy,
                    "frustrated" => EmotionLabel.Frustrated,
                    "angry" => EmotionLabel.Angry,
                    "sad" => EmotionLabel.Sad,
                    "anxious" => EmotionLabel.Anxious,
                    _ => EmotionLabel.Neutral
                };

                return new EmotionResult { Label = label, Confidence = confidence };
            }
            catch
            {
                // Some models occasionally wrap JSON in extra text. Try extracting the first JSON object.
                try
                {
                    var start = json.IndexOf('{');
                    var end = json.LastIndexOf('}');
                    if (start >= 0 && end > start)
                    {
                        var slice = json.Substring(start, end - start + 1);
                        using var doc2 = JsonDocument.Parse(slice);
                        var root2 = doc2.RootElement;

                        var labelStr = root2.TryGetProperty("label", out var labelProp)
                            ? labelProp.GetString()?.Trim() ?? "Neutral"
                            : "Neutral";

                        var confidence = root2.TryGetProperty("confidence", out var confProp)
                            ? confProp.GetSingle()
                            : 0f;

                        var label = labelStr.ToLowerInvariant() switch
                        {
                            "happy" => EmotionLabel.Happy,
                            "frustrated" => EmotionLabel.Frustrated,
                            "angry" => EmotionLabel.Angry,
                            "sad" => EmotionLabel.Sad,
                            "anxious" => EmotionLabel.Anxious,
                            _ => EmotionLabel.Neutral
                        };

                        return new EmotionResult { Label = label, Confidence = confidence };
                    }
                }
                catch
                {
                    // ignore
                }

                return new EmotionResult { Label = EmotionLabel.Neutral, Confidence = 0f };
            }
        }
    }
}
