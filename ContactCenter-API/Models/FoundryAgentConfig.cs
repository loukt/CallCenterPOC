namespace ContactCenterPOC.Models
{
    public class FoundryAgentConfig
    {
        public string ProjectEndpoint { get; set; } = string.Empty;
        public string AgentModel { get; set; } = "gpt-5.4-nano";
        public string IntentAgentName { get; set; } = "ccpoc-intent-agent";
        public string CaseAgentName { get; set; } = "ccpoc-case-agent";
        public string QualityAgentName { get; set; } = "ccpoc-quality-agent";
        public string KnowledgeGapAgentName { get; set; } = "ccpoc-knowledgegap-agent";
        public string SummaryAgentName { get; set; } = "ccpoc-summary-agent";
        public string PostCallReviewAgentName { get; set; } = "ccpoc-postcallreview-agent";
        public string AudioEmotionModel { get; set; } = "gpt-4o-audio-preview";
    }
}
