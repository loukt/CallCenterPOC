using Azure.Core;
using Azure.Identity;
using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;

namespace ContactCenterPOC.Services
{
    /// <summary>
    /// Shared base class for Azure OpenAI analysis services (sentiment, emotion, operator style, summary).
    /// Handles HTTP client construction, authentication (API key or DefaultAzureCredential), chat completion
    /// requests with primary/legacy parameter retry, and JSON response extraction.
    /// </summary>
    public abstract class AzureOpenAIAnalysisBase
    {
        protected readonly Uri? EndpointUri;
        protected readonly string? ApiKey;
        protected readonly string? Deployment;
        protected readonly TokenCredential? Credential;
        protected readonly ILogger Logger;

        private static readonly HttpClient SharedHttpClient = new()
        {
            Timeout = TimeSpan.FromSeconds(30)
        };

        protected const string ChatCompletionsApiVersion = "2024-10-01-preview";

        public bool IsConfigured => EndpointUri != null && !string.IsNullOrWhiteSpace(Deployment);

        protected AzureOpenAIAnalysisBase(
            IConfiguration configuration,
            ILogger logger,
            string serviceName,
            string? deploymentConfigKey = null)
        {
            Logger = logger;

            var endpointUri = configuration["AzureOpenAI:EndpointUri"];
            var apiKey = configuration["AzureOpenAI:Key"];

            // Allow each service to specify a dedicated deployment config key (e.g., AzureOpenAI:SentimentDeployment)
            string? dedicatedDeployment = null;
            if (!string.IsNullOrEmpty(deploymentConfigKey))
            {
                dedicatedDeployment = configuration[deploymentConfigKey];
            }

            var chatDeployment = dedicatedDeployment
                ?? configuration["AzureOpenAI:ChatDeployment"]
                ?? configuration["AzureOpenAI:DeploymentName"];

            if (string.IsNullOrEmpty(endpointUri) || string.IsNullOrEmpty(chatDeployment))
            {
                logger.LogWarning("{ServiceName}: AzureOpenAI:EndpointUri or deployment not configured. Service disabled.", serviceName);
                return;
            }

            if (!string.IsNullOrEmpty(dedicatedDeployment))
            {
                logger.LogInformation("{ServiceName}: using dedicated deployment '{Deployment}'.", serviceName, chatDeployment);
            }

            try
            {
                if (!string.IsNullOrWhiteSpace(apiKey))
                {
                    ApiKey = apiKey;
                    logger.LogInformation("{ServiceName} using API key authentication.", serviceName);
                }
                else
                {
                    Credential = new DefaultAzureCredential();
                    logger.LogInformation("{ServiceName} using DefaultAzureCredential (Managed Identity / Entra ID).", serviceName);
                }

                EndpointUri = new Uri(endpointUri);
                Deployment = chatDeployment;
                logger.LogInformation(
                    "{ServiceName} initialized (endpointHost={EndpointHost}, deployment={Deployment})",
                    serviceName,
                    new Uri(endpointUri).Host,
                    chatDeployment);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to initialize {ServiceName}", serviceName);
            }
        }

        /// <summary>
        /// Send a chat completion request to Azure OpenAI with automatic primary/legacy parameter fallback.
        /// Returns the assistant's response content string, or null on failure.
        /// </summary>
        protected async Task<string?> CallChatCompletionAsync(
            string systemPrompt,
            string userMessage,
            int maxCompletionTokens = 200,
            string? reasoningEffort = "low",
            int legacyMaxTokens = 50,
            float legacyTemperature = 0f)
        {
            if (!IsConfigured) return null;

            var uri = new Uri(
                EndpointUri!,
                $"openai/deployments/{Uri.EscapeDataString(Deployment!)}/chat/completions?api-version={ChatCompletionsApiVersion}");

            var messages = new object[]
            {
                new Dictionary<string, object?>
                {
                    ["role"] = "system",
                    ["content"] = systemPrompt
                },
                new Dictionary<string, object?>
                {
                    ["role"] = "user",
                    ["content"] = userMessage
                }
            };

            // Attempt 1: gpt-5-nano-compatible parameters
            var requestBody = new Dictionary<string, object?>
            {
                ["messages"] = messages,
                ["max_completion_tokens"] = maxCompletionTokens
            };

            if (!string.IsNullOrEmpty(reasoningEffort))
            {
                requestBody["reasoning_effort"] = reasoningEffort;
            }

            var (statusCode, responseBody) = await PostChatCompletionsAsync(uri, requestBody);

            // Retry with legacy parameters if the deployment doesn't support newer params
            if (statusCode == HttpStatusCode.BadRequest && LooksLikeUnsupportedParam(responseBody))
            {
                var legacyBody = new Dictionary<string, object?>
                {
                    ["messages"] = messages,
                    ["max_tokens"] = legacyMaxTokens,
                    ["temperature"] = legacyTemperature
                };

                (statusCode, responseBody) = await PostChatCompletionsAsync(uri, legacyBody);
            }

            if (statusCode != HttpStatusCode.OK)
            {
                if ((int)statusCode == 404
                    && responseBody.IndexOf("DeploymentNotFound", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    Logger.LogWarning(
                        "Deployment not found (HTTP 404 DeploymentNotFound). Verify deployment name exists on the Azure OpenAI resource.");
                    return null;
                }

                Logger.LogWarning(
                    "Chat completion HTTP {StatusCode}. Body: {Body}",
                    (int)statusCode,
                    TruncateForLog(responseBody, 1000));
                return null;
            }

            return ExtractAssistantContent(responseBody);
        }

        private async Task<(HttpStatusCode StatusCode, string Body)> PostChatCompletionsAsync(
            Uri uri, Dictionary<string, object?> requestBody)
        {
            var request = new HttpRequestMessage(HttpMethod.Post, uri)
            {
                Content = new StringContent(JsonSerializer.Serialize(requestBody))
            };

            request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/json");

            if (!string.IsNullOrWhiteSpace(ApiKey))
            {
                request.Headers.TryAddWithoutValidation("api-key", ApiKey);
            }
            else
            {
                if (Credential == null)
                {
                    return (HttpStatusCode.Unauthorized, "Missing credential");
                }

                var token = await Credential.GetTokenAsync(
                    new TokenRequestContext(["https://cognitiveservices.azure.com/.default"]),
                    CancellationToken.None);

                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token.Token);
            }

            using var response = await SharedHttpClient.SendAsync(request);
            var responseBody = await response.Content.ReadAsStringAsync();
            return (response.StatusCode, responseBody);
        }

        protected static bool LooksLikeUnsupportedParam(string responseBody)
        {
            if (string.IsNullOrWhiteSpace(responseBody)) return false;
            return responseBody.IndexOf("Unsupported parameter", StringComparison.OrdinalIgnoreCase) >= 0
                || responseBody.IndexOf("does not support", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        protected static string? ExtractAssistantContent(string responseBody)
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

        protected static string TruncateForLog(string? value, int maxLength)
        {
            if (string.IsNullOrEmpty(value)) return string.Empty;
            value = value.Replace("\r", " ").Replace("\n", " ");
            return value.Length <= maxLength ? value : value.Substring(0, maxLength) + "…";
        }

        /// <summary>
        /// Extract the first JSON object from a string that may contain surrounding text.
        /// Returns the extracted JSON substring, or null if not found.
        /// </summary>
        protected static string? ExtractJsonObject(string text)
        {
            var start = text.IndexOf('{');
            var end = text.LastIndexOf('}');
            if (start >= 0 && end > start)
            {
                return text.Substring(start, end - start + 1);
            }
            return null;
        }
    }
}
