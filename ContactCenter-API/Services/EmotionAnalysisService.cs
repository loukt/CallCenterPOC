using ContactCenterPOC.Models;
using System.Text.Json;

namespace ContactCenterPOC.Services
{
    public class EmotionAnalysisService : AzureOpenAIAnalysisBase
    {
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
            : base(configuration, logger, "EmotionAnalysisService", "AzureOpenAI:EmotionDeployment")
        {
        }

        public async Task<EmotionResult> AnalyzeAsync(string? text)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return new EmotionResult { Label = EmotionLabel.Neutral, Confidence = 0f };
            }

            if (!IsConfigured)
            {
                return new EmotionResult { Label = EmotionLabel.Neutral, Confidence = 0f };
            }

            try
            {
                var content = await CallChatCompletionAsync(
                    EmotionSystemPrompt,
                    text,
                    EmotionMaxCompletionTokens,
                    EmotionReasoningEffort,
                    EmotionLegacyMaxTokens,
                    EmotionLegacyTemperature);

                if (string.IsNullOrWhiteSpace(content))
                {
                    Logger.LogWarning("Emotion analysis returned empty content for text ({Length} chars).", text.Length);
                    return new EmotionResult { Label = EmotionLabel.Neutral, Confidence = 0f };
                }

                return ParseEmotionJson(content);
            }
            catch (Exception ex)
            {
                Logger.LogWarning(ex, "Emotion analysis failed for text ({Length} chars), returning Neutral", text.Length);
                return new EmotionResult { Label = EmotionLabel.Neutral, Confidence = 0f };
            }
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
