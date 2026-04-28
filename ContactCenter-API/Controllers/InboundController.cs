using Azure.Messaging.EventGrid;
using ContactCenterPOC.Services;
using Microsoft.AspNetCore.Mvc;
using System.Text.Json;

namespace ContactCenterPOC.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class InboundController : ControllerBase
    {
        private readonly CallService _callService;
        private readonly SettingsService _settingsService;
        private readonly CampaignService _campaignService;
        private readonly EscalationService _escalationService;
        private readonly ILogger<InboundController> _logger;

        public InboundController(
            CallService callService, SettingsService settingsService,
            CampaignService campaignService, EscalationService escalationService,
            ILogger<InboundController> logger)
        {
            _callService = callService;
            _settingsService = settingsService;
            _campaignService = campaignService;
            _escalationService = escalationService;
            _logger = logger;
        }

        [HttpPost("events")]
        public async Task<IActionResult> HandleEventGridEvents()
        {
            using var reader = new StreamReader(Request.Body);
            var requestBody = await reader.ReadToEndAsync();
            var events = JsonSerializer.Deserialize<JsonElement[]>(requestBody);

            if (events == null || events.Length == 0)
                return BadRequest();

            foreach (var eventData in events)
            {
                var eventType = eventData.GetProperty("eventType").GetString();

                // Handle Event Grid subscription validation
                if (eventType == "Microsoft.EventGrid.SubscriptionValidationEvent")
                {
                    var validationCode = eventData.GetProperty("data")
                        .GetProperty("validationCode").GetString();
                    return Ok(new { validationResponse = validationCode });
                }

                // Handle ACS IncomingCall event
                if (eventType == "Microsoft.Communication.IncomingCall")
                {
                    await HandleIncomingCallAsync(eventData);
                }
            }

            return Ok();
        }

        [HttpGet("status")]
        public async Task<IActionResult> GetInboundStatus()
        {
            var settings = await _settingsService.GetSettingsAsync();
            var activeCalls = _callService.ActiveCalls.Values;

            string? campaignTitle = null;
            if (!string.IsNullOrEmpty(settings.DefaultInboundCampaignId))
            {
                var campaign = await _campaignService.GetByIdAsync(settings.DefaultInboundCampaignId);
                campaignTitle = campaign?.Title;
            }

            return Ok(new
            {
                configured = !string.IsNullOrEmpty(settings.InboundPhoneNumber),
                phoneNumber = settings.InboundPhoneNumber,
                defaultCampaignId = settings.DefaultInboundCampaignId,
                defaultCampaignTitle = campaignTitle,
                activeInboundCalls = activeCalls.Count(c => c.CallSource == "Inbound"),
                activeTotalCalls = activeCalls.Count,
                maxConcurrentCalls = 5
            });
        }

        [HttpPost("escalate/{callConnectionId}")]
        public async Task<IActionResult> TriggerEscalation(string callConnectionId)
        {
            var activeCall = _callService.ActiveCalls.Values
                .FirstOrDefault(c => c.CallConnectionId == callConnectionId);
            if (activeCall == null)
                return NotFound(new { error = "Call not found" });

            var result = await _escalationService.EscalateAsync(
                callConnectionId, activeCall.CampaignId, _callService.CallAutomationClient);

            return Ok(result);
        }

        private async Task HandleIncomingCallAsync(JsonElement eventData)
        {
            try
            {
                var data = eventData.GetProperty("data");
                var fromNumber = data.GetProperty("from").GetProperty("phoneNumber").GetProperty("value").GetString();
                var incomingCallContext = data.GetProperty("incomingCallContext").GetString();

                _logger.LogInformation("Incoming call from {PhoneNumber}", fromNumber);

                // Resolve campaign
                var settings = await _settingsService.GetSettingsAsync();
                Models.Campaign? campaign = null;
                if (!string.IsNullOrEmpty(settings.DefaultInboundCampaignId))
                {
                    campaign = await _campaignService.GetByIdAsync(settings.DefaultInboundCampaignId);
                }

                // Accept the call
                if (!string.IsNullOrEmpty(incomingCallContext))
                {
                    var answerOptions = new Azure.Communication.CallAutomation.AnswerCallOptions(
                        incomingCallContext,
                        new Uri(_callService.CallAutomationClient.ToString() ?? ""));

                    // Answer within 3 seconds
                    var result = await _callService.CallAutomationClient.AnswerCallAsync(answerOptions);
                    _logger.LogInformation("Inbound call answered: {CallConnectionId}", result.Value.CallConnection.CallConnectionId);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to handle incoming call");
            }
        }
    }
}
