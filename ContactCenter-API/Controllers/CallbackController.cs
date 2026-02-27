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
