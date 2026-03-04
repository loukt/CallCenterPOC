using ContactCenterPOC.Models;

namespace ContactCenterPOC.Services
{
    public class CallSummaryService : AzureOpenAIAnalysisBase
    {
        private const int SummaryMaxCompletionTokens = 500;
        private const string SummaryReasoningEffort = "low";
        private const int SummaryLegacyMaxTokens = 200;
        private const float SummaryLegacyTemperature = 0.3f;

        private const string SummarySystemPrompt =
            "You are a call summarization service for a contact center. " +
            "Given a transcript of a phone call between an AI operator and a recipient, " +
            "produce a concise summary of 2-4 sentences. Include the main topic discussed, " +
            "key outcome (e.g., appointment scheduled, information provided, issue resolved), " +
            "and the overall tone of the conversation. " +
            "Respond ONLY with the plain text summary, no JSON, no bullet points.";

        public CallSummaryService(IConfiguration configuration, ILogger<CallSummaryService> logger)
            : base(configuration, logger, "CallSummaryService")
        {
        }

        public async Task<string?> GenerateSummaryAsync(List<TranscriptEntry> entries)
        {
            if (entries == null || entries.Count == 0)
            {
                return null;
            }

            if (!IsConfigured)
            {
                return null;
            }

            try
            {
                var transcript = string.Join("\n", entries.Select(e =>
                    $"[{e.Speaker}]: {e.Text}"));

                var content = await CallChatCompletionAsync(
                    SummarySystemPrompt,
                    transcript,
                    SummaryMaxCompletionTokens,
                    SummaryReasoningEffort,
                    SummaryLegacyMaxTokens,
                    SummaryLegacyTemperature);

                if (string.IsNullOrWhiteSpace(content))
                {
                    Logger.LogWarning("Call summary generation returned empty content for {EntryCount} entries.", entries.Count);
                    return null;
                }

                return content.Trim();
            }
            catch (Exception ex)
            {
                Logger.LogWarning(ex, "Call summary generation failed for {EntryCount} entries", entries.Count);
                return null;
            }
        }
    }
}
