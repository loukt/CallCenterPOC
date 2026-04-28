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
        private readonly OrchestrationService _orchestrationService;
        private readonly IHubContext<TranscriptHub> _hubContext;

        public CallbackController(ILogger<CallbackController> logger, CallService callService, CallHistoryService callHistoryService, OrchestrationService orchestrationService, IHubContext<TranscriptHub> hubContext)
        {
            _logger = logger;
            _callService = callService;
            _callHistoryService = callHistoryService;
            _orchestrationService = orchestrationService;
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
                    await _callService.StartAudioEmotionAsync(@event.CallConnectionId);
                }
                else if (callbackEvent.Type.Contains("CreateCallFailed"))
                {
                    _logger.LogWarning("Call {CallConnectionId} creation failed", @event.CallConnectionId);

                    // Update ActiveCall status
                    if (_callService.ActiveCalls.TryGetValue(@event.CallConnectionId, out var failedCall))
                    {
                        failedCall.Status = CallStatus.Failed;
                    }

                    // Push failure status via SignalR so the UI stops showing "Initiating"
                    var failUpdate = new CallStatusUpdate
                    {
                        CallConnectionId = @event.CallConnectionId,
                        Status = CallStatus.Failed,
                        Timestamp = DateTimeOffset.UtcNow
                    };
                    await _hubContext.Clients.Group(@event.CallConnectionId)
                        .SendAsync("CallStatusChanged", failUpdate);

                    await _callService.CleanupCall(@event.CallConnectionId);
                }
                else if (callbackEvent.Type.Contains("CallDisconnected"))
                {
                    _logger.LogInformation("Call {CallConnectionId} disconnected", @event.CallConnectionId);

                    // Mark status if still present; persistence happens during cleanup
                    if (_callService.ActiveCalls.TryGetValue(@event.CallConnectionId, out var activeCall))
                    {
                        activeCall.Status = CallStatus.Disconnected;
                    }
                    else
                    {
                        // If we don't have in-memory state (e.g., scale-out or restart), update the persisted record.
                        try
                        {
                            var now = DateTimeOffset.UtcNow;
                            var record = await _callHistoryService.GetByIdAsync(@event.CallConnectionId);
                            if (record == null)
                            {
                                record = new CallRecord
                                {
                                    CallConnectionId = @event.CallConnectionId,
                                    PhoneNumber = string.Empty,
                                    Prompt = string.Empty,
                                    RecordingId = null,
                                    Duration = TimeSpan.Zero,
                                    OverallSentiment = SentimentLabel.Neutral,
                                    SentimentBreakdown = new SentimentBreakdown(),
                                    TalkTimeRatio = new TalkTimeRatio(),
                                    TranscriptEntries = new List<TranscriptEntry>(),
                                    StartedAt = now,
                                    EndedAt = now
                                };
                            }
                            else
                            {
                                record.EndedAt = now;
                                var duration = record.EndedAt - record.StartedAt;
                                record.Duration = duration < TimeSpan.Zero ? TimeSpan.Zero : duration;
                            }

                            await _callHistoryService.SaveCallRecordAsync(record);
                        }
                        catch (Exception ex)
                        {
                            _logger.LogWarning(ex, "Failed to update persisted call record on disconnect for {CallConnectionId}", @event.CallConnectionId);
                        }
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

                    // Fire-and-forget: trigger PostCallReview orchestrator agent for post-call analysis
                    var disconnectedCallId = @event.CallConnectionId;
                    _ = Task.Run(async () =>
                    {
                        try
                        {
                            // Small delay to ensure call record is fully persisted
                            await Task.Delay(2000);
                            var callRecord = await _callHistoryService.GetByIdAsync(disconnectedCallId);
                            if (callRecord != null)
                            {
                                _logger.LogInformation("Auto-triggering post-call review for {CallConnectionId}", disconnectedCallId);
                                await _orchestrationService.RunPostCallReviewAsync(callRecord, _hubContext);
                                _logger.LogInformation("Post-call review completed for {CallConnectionId}", disconnectedCallId);
                            }
                            else
                            {
                                _logger.LogWarning("No call record found for auto-analysis: {CallConnectionId}", disconnectedCallId);
                            }
                        }
                        catch (Exception ex)
                        {
                            _logger.LogError(ex, "Auto post-call review failed for {CallConnectionId}", disconnectedCallId);
                        }
                    });
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
