using Azure.AI.OpenAI;
using Azure.Identity;
using ContactCenterPOC.Models;
using OpenAI.Chat;
using System.ClientModel;
using System.Text.Json;

namespace ContactCenterPOC.Services
{
    public class SentimentAnalysisService
    {
        private readonly ChatClient? _chatClient;
        private readonly ILogger<SentimentAnalysisService> _logger;

        private const string SentimentSystemPrompt =
            "You are a sentiment analysis service. Classify the sentiment of the given text as Positive, Neutral, or Negative. " +
            "Respond ONLY with a JSON object: {\"label\":\"Positive|Neutral|Negative\",\"confidence\":0.0-1.0}. " +
            "No other text or explanation.";

        public SentimentAnalysisService(IConfiguration configuration, ILogger<SentimentAnalysisService> logger)
        {
            _logger = logger;

            var endpointUri = configuration["AzureOpenAI:EndpointUri"];
            var chatDeployment = configuration["AzureOpenAI:ChatDeployment"];

            if (string.IsNullOrEmpty(endpointUri) || string.IsNullOrEmpty(chatDeployment))
            {
                _logger.LogWarning("AzureOpenAI:EndpointUri or AzureOpenAI:ChatDeployment not configured. Sentiment analysis disabled.");
                _chatClient = null;
                return;
            }

            try
            {
                var apiKey = configuration["AzureOpenAI:Key"];
                AzureOpenAIClient aiClient;
                if (!string.IsNullOrEmpty(apiKey) && apiKey != "xxxx" && apiKey != "xx")
                {
                    aiClient = new AzureOpenAIClient(new Uri(endpointUri), new ApiKeyCredential(apiKey));
                }
                else
                {
                    aiClient = new AzureOpenAIClient(new Uri(endpointUri), new DefaultAzureCredential());
                }
                _chatClient = aiClient.GetChatClient(chatDeployment);
                _logger.LogInformation("SentimentAnalysisService initialized with deployment: {Deployment}", chatDeployment);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to initialize SentimentAnalysisService");
                _chatClient = null;
            }
        }

        public async Task<SentimentResult> AnalyzeAsync(string? text)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return new SentimentResult { Label = SentimentLabel.Neutral, Confidence = 0f };
            }

            if (_chatClient == null)
            {
                return new SentimentResult { Label = SentimentLabel.Neutral, Confidence = 0f };
            }

            try
            {
                var messages = new List<ChatMessage>
                {
                    new SystemChatMessage(SentimentSystemPrompt),
                    new UserChatMessage(text)
                };

                var options = new ChatCompletionOptions
                {
                    MaxOutputTokenCount = 50,
                    Temperature = 0f
                };

                var response = await _chatClient.CompleteChatAsync(messages, options);
                var content = response.Value.Content[0].Text;

                return ParseSentimentJson(content);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Sentiment analysis failed for text ({Length} chars), returning Neutral", text.Length);
                return new SentimentResult { Label = SentimentLabel.Neutral, Confidence = 0f };
            }
        }

        public static SentimentResult ParseSentimentJson(string? json)
        {
            if (string.IsNullOrWhiteSpace(json))
            {
                return new SentimentResult { Label = SentimentLabel.Neutral, Confidence = 0f };
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
                    "positive" => SentimentLabel.Positive,
                    "negative" => SentimentLabel.Negative,
                    _ => SentimentLabel.Neutral
                };

                return new SentimentResult { Label = label, Confidence = confidence };
            }
            catch
            {
                return new SentimentResult { Label = SentimentLabel.Neutral, Confidence = 0f };
            }
        }
    }
}
