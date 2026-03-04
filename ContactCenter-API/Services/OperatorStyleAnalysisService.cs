using ContactCenterPOC.Models;
using System.Text.Json;

namespace ContactCenterPOC.Services
{
    public class OperatorStyleAnalysisService : AzureOpenAIAnalysisBase
    {
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
            : base(configuration, logger, "OperatorStyleAnalysisService")
        {
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

            if (!IsConfigured)
            {
                return null;
            }

            try
            {
                var combinedText = string.Join("\n", texts);

                var content = await CallChatCompletionAsync(
                    TraitsSystemPrompt,
                    combinedText,
                    TraitsMaxCompletionTokens,
                    TraitsReasoningEffort,
                    TraitsLegacyMaxTokens,
                    TraitsLegacyTemperature);

                if (string.IsNullOrWhiteSpace(content))
                {
                    return null;
                }

                return ParseTraitsJson(content);
            }
            catch (Exception ex)
            {
                Logger.LogWarning(ex, "Operator style analysis failed, returning null");
                return null;
            }
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
                try
                {
                    var extracted = ExtractJsonObject(json);
                    if (extracted != null)
                    {
                        using var doc2 = JsonDocument.Parse(extracted);
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
