using Azure.Communication.CallAutomation;
using ContactCenterPOC.Hubs;
using ContactCenterPOC.Models;
using Microsoft.AspNetCore.SignalR;

namespace ContactCenterPOC.Services
{
    public class EscalationService
    {
        private readonly SettingsService _settingsService;
        private readonly CampaignService _campaignService;
        private readonly IHubContext<TranscriptHub> _hubContext;
        private readonly AgentActivityService _agentActivityService;
        private readonly ILogger<EscalationService> _logger;

        public EscalationService(
            SettingsService settingsService, CampaignService campaignService,
            IHubContext<TranscriptHub> hubContext, AgentActivityService agentActivityService,
            ILogger<EscalationService> logger)
        {
            _settingsService = settingsService;
            _campaignService = campaignService;
            _hubContext = hubContext;
            _agentActivityService = agentActivityService;
            _logger = logger;
        }

        /// <summary>
        /// Resolves the escalation phone number using the priority chain:
        /// 1. Campaign-specific EscalationPhoneNumber
        /// 2. Global DefaultEscalationNumber from settings
        /// 3. null (notification-only fallback)
        /// </summary>
        public async Task<string?> ResolveEscalationNumberAsync(string? campaignId)
        {
            // Check campaign-level override
            if (!string.IsNullOrEmpty(campaignId))
            {
                var campaign = await _campaignService.GetByIdAsync(campaignId);
                if (campaign != null && !string.IsNullOrEmpty(campaign.EscalationPhoneNumber))
                {
                    _logger.LogInformation("Using campaign escalation number for campaign {CampaignId}", campaignId);
                    return campaign.EscalationPhoneNumber;
                }
            }

            // Check global settings
            var settings = await _settingsService.GetSettingsAsync();
            if (!string.IsNullOrEmpty(settings.DefaultEscalationNumber))
            {
                return settings.DefaultEscalationNumber;
            }

            // No number configured — notification-only fallback
            return null;
        }

        /// <summary>
        /// Triggers escalation for a call. If a phone number is available, initiates transfer.
        /// Otherwise, sends a dashboard notification.
        /// </summary>
        public async Task<EscalationResult> EscalateAsync(
            string callConnectionId, string? campaignId,
            CallAutomationClient? callAutomationClient = null)
        {
            var escalationNumber = await ResolveEscalationNumberAsync(campaignId);

            if (!string.IsNullOrEmpty(escalationNumber) && callAutomationClient != null)
            {
                // Phone transfer via ACS AddParticipant
                try
                {
                    var callConnection = callAutomationClient.GetCallConnection(callConnectionId);
                    var participant = new Azure.Communication.PhoneNumberIdentifier(escalationNumber);
                    await callConnection.TransferCallToParticipantAsync(participant);

                    await _agentActivityService.LogActivityAsync(
                        "CaseManagement", "Escalation", AgentActionResult.Success,
                        resultDetail: $"Transferred to {escalationNumber}", callRecordId: callConnectionId);

                    return new EscalationResult { Method = "PhoneTransfer", PhoneNumber = escalationNumber, Success = true };
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Phone transfer failed, falling back to dashboard notification");
                }
            }

            // Dashboard notification fallback
            await _hubContext.Clients.All.SendAsync("NotifyEscalation", new
            {
                callConnectionId,
                campaignId,
                message = "Customer is requesting to speak with a human operator",
                timestamp = DateTimeOffset.UtcNow
            });

            await _agentActivityService.LogActivityAsync(
                "CaseManagement", "Escalation", AgentActionResult.Success,
                resultDetail: "Dashboard notification sent (no escalation number configured)", callRecordId: callConnectionId);

            return new EscalationResult { Method = "DashboardNotification", Success = true };
        }
    }

    public class EscalationResult
    {
        public string Method { get; set; } = string.Empty;
        public string? PhoneNumber { get; set; }
        public bool Success { get; set; }
    }
}
