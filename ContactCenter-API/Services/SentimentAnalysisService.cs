using ContactCenterPOC.Models;
using System.Text.Json;

namespace ContactCenterPOC.Services
{
    public class SentimentAnalysisService : AzureOpenAIAnalysisBase
    {
        private const int SentimentMaxCompletionTokens = 200;
        private const string SentimentReasoningEffort = "low";
        private const int SentimentLegacyMaxTokens = 50;
        private const float SentimentLegacyTemperature = 0f;

        private const string SentimentSystemPrompt =
            "You are a sentiment analysis service. Classify the sentiment of the given text as Positive, Neutral, or Negative. " +
            "Respond ONLY with a JSON object: {\"label\":\"Positive|Neutral|Negative\",\"confidence\":0.0-1.0}. " +
            "No other text or explanation.";

        public SentimentAnalysisService(IConfiguration configuration, ILogger<SentimentAnalysisService> logger)
            : base(configuration, logger, "SentimentAnalysisService", "AzureOpenAI:SentimentDeployment")
        {
        }

        public async Task<SentimentResult> AnalyzeAsync(string? text)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return new SentimentResult { Label = SentimentLabel.Neutral, Confidence = 0f };
            }

            if (!IsConfigured)
            {
                return new SentimentResult { Label = SentimentLabel.Neutral, Confidence = 0f };
            }

            try
            {
                var content = await CallChatCompletionAsync(
                    SentimentSystemPrompt,
                    text,
                    SentimentMaxCompletionTokens,
                    SentimentReasoningEffort,
                    SentimentLegacyMaxTokens,
                    SentimentLegacyTemperature);

                if (string.IsNullOrWhiteSpace(content))
                {
                    Logger.LogWarning("Sentiment analysis returned empty content for text ({Length} chars).", text.Length);
                    return new SentimentResult { Label = SentimentLabel.Neutral, Confidence = 0f };
                }

                return ParseSentimentJson(content);
            }
            catch (Exception ex)
            {
                Logger.LogWarning(ex, "Sentiment analysis failed for text ({Length} chars), returning Neutral", text.Length);
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
                try
                {
                    var extracted = ExtractJsonObject(json);
                    if (extracted != null)
                    {
                        using var doc2 = JsonDocument.Parse(extracted);
                        var root2 = doc2.RootElement;

                        var labelStr = root2.TryGetProperty("label", out var labelProp)
                            ? labelProp.GetString()?.Trim() ?? "Neutral"
                            : "Neutral";

                        var confidence = root2.TryGetProperty("confidence", out var confProp)
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
                }
                catch
                {
                    // ignore
                }

                return new SentimentResult { Label = SentimentLabel.Neutral, Confidence = 0f };
            }
        }
    }
}
