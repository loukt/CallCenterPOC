using Azure.Communication.CallAutomation;
using Azure.Messaging;
using ContactCenterPOC.Hubs;
using ContactCenterPOC.Models;
using ContactCenterPOC.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;


namespace ContactCenterPOC.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class CallbackController : ControllerBase
    {
        private readonly ILogger<CallbackController> _logger;
        private readonly CallService _callService;
        private readonly CallHistoryService _callHistoryService;
        private readonly IHubContext<TranscriptHub> _hubContext;

        public CallbackController(ILogger<CallbackController> logger, CallService callService, CallHistoryService callHistoryService, IHubContext<TranscriptHub> hubContext)
        {
            _logger = logger;
            _callService = callService;
            _callHistoryService = callHistoryService;
            _hubContext = hubContext;
        }

        
       
        //For all callbacks goes through here
        [HttpPost]
        public async Task<IActionResult> CallbackEvent([FromBody] CloudEvent[] callbackEvents)
        {
            _logger.LogInformation("Callback event received");

            foreach (var callbackEvent in callbackEvents)
            {
                CallAutomationEventBase @event = CallAutomationEventParser.Parse(callbackEvent);
                _logger.LogInformation("Event received: {EventType} for call {CallConnectionId}", callbackEvent.Type, @event.CallConnectionId);

                if (callbackEvent.Type.Contains("CallConnected"))
                {
                    _logger.LogInformation("Call {CallConnectionId} connected, starting recording", @event.CallConnectionId);

                    // Update ActiveCall with ServerCallId and status
                    if (_callService.ActiveCalls.TryGetValue(@event.CallConnectionId, out var activeCall))
                    {
                        activeCall.ServerCallId = @event.ServerCallId;
                        activeCall.Status = CallStatus.Connected;
                    }

                    // Push status update via SignalR
                    var statusUpdate = new CallStatusUpdate
                    {
                        CallConnectionId = @event.CallConnectionId,
                        Status = CallStatus.Connected,
                        Timestamp = DateTimeOffset.UtcNow
                    };
                    await _hubContext.Clients.Group(@event.CallConnectionId)
                        .SendAsync("CallStatusChanged", statusUpdate);

                    await _callService.StartRecordingAsync(@event.ServerCallId, @event.CallConnectionId);
                }
                else if (callbackEvent.Type.Contains("CallDisconnected"))
                {
                    _logger.LogInformation("Call {CallConnectionId} disconnected", @event.CallConnectionId);

                    // Persist call record BEFORE cleanup removes the ActiveCall
                    if (_callService.ActiveCalls.TryGetValue(@event.CallConnectionId, out var activeCall))
                    {
                        activeCall.Status = CallStatus.Disconnected;

                        // Build CallRecord with analytics
                        var endedAt = DateTimeOffset.UtcNow;
                        var duration = endedAt - activeCall.StartedAt;
                        var entries = activeCall.TranscriptEntries;

                        // Overall sentiment = majority label among entries
                        var overallSentiment = SentimentLabel.Neutral;
                        if (entries.Count > 0)
                        {
                            var groups = entries.GroupBy(e => e.Sentiment.Label)
                                                .OrderByDescending(g => g.Count())
                                                .ToList();
                            overallSentiment = groups.First().Key;
                        }

                        // Sentiment breakdown percentages
                        var breakdown = new SentimentBreakdown();
                        if (entries.Count > 0)
                        {
                            float total = entries.Count;
                            breakdown.PositivePercent = entries.Count(e => e.Sentiment.Label == SentimentLabel.Positive) / total * 100f;
                            breakdown.NeutralPercent = entries.Count(e => e.Sentiment.Label == SentimentLabel.Neutral) / total * 100f;
                            breakdown.NegativePercent = entries.Count(e => e.Sentiment.Label == SentimentLabel.Negative) / total * 100f;
                        }

                        // Talk time ratio (count of entries per speaker as proxy)
                        var talkTime = new TalkTimeRatio();
                        if (entries.Count > 0)
                        {
                            float total = entries.Count;
                            var aiCount = entries.Count(e => e.Speaker == SpeakerType.AI);
                            var recipientCount = entries.Count(e => e.Speaker == SpeakerType.Recipient);
                            talkTime.AiPercent = aiCount / total * 100f;
                            talkTime.RecipientPercent = recipientCount / total * 100f;
                        }

                        var callRecord = new CallRecord
                        {
                            CallConnectionId = activeCall.CallConnectionId,
                            PhoneNumber = activeCall.TargetPhoneNumber,
                            CampaignId = activeCall.CampaignId,
                            CampaignTitle = activeCall.CampaignTitle,
                            ContactName = activeCall.ContactName,
                            Prompt = activeCall.Prompt,
                            RecordingId = activeCall.RecordingId,
                            Duration = duration,
                            OverallSentiment = overallSentiment,
                            SentimentBreakdown = breakdown,
                            TalkTimeRatio = talkTime,
                            TranscriptEntries = entries,
                            StartedAt = activeCall.StartedAt,
                            EndedAt = endedAt
                        };

                        await _callHistoryService.SaveCallRecordAsync(callRecord);
                    }

                    // Push status update via SignalR
                    var statusUpdate = new CallStatusUpdate
                    {
                        CallConnectionId = @event.CallConnectionId,
                        Status = CallStatus.Disconnected,
                        Timestamp = DateTimeOffset.UtcNow
                    };
                    await _hubContext.Clients.Group(@event.CallConnectionId)
                        .SendAsync("CallStatusChanged", statusUpdate);

                    await _callService.CleanupCall(@event.CallConnectionId);
                }
            }

            return Ok();
        }


        //websocket handling the live with agent
        [HttpGet("ws")]
        public async Task<IActionResult> Get(string targetNumber)
        {
            if(!targetNumber.Contains("+"))targetNumber = "+" + targetNumber.Replace(" ","");
            if (HttpContext.WebSockets.IsWebSocketRequest)
            {
                _logger.LogInformation($"Callback triggered on WS It's rerouted to the websocket AND WORKING");

                await _callService.StartCallInteraction(HttpContext,targetNumber);
            }
            else
            {
                _logger.LogInformation($"Callback triggered on WS It's rerouted to the websocket but doesn't look like it's working");

                // If it's not a WebSocket request, return a bad request
                HttpContext.Response.StatusCode = StatusCodes.Status400BadRequest;
            }

            return Ok();
        }



        

    }


}
