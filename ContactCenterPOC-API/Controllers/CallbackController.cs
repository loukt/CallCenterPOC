using Azure.Communication.CallAutomation;
using Azure.Messaging;
using Azure.Messaging.EventGrid;
using ContactCenterPOC.Models;
using ContactCenterPOC.Services;
using Microsoft.AspNetCore.Mvc;
using Newtonsoft.Json;


namespace ContactCenterPOC.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class CallbackController : ControllerBase
    {
        private readonly ILogger<CallbackController> _logger;
        private readonly CallService _callService;

        public CallbackController(ILogger<CallbackController> logger, CallService callService)
        {
            _logger = logger;
            _callService = callService;
        }

        
       
        //For all callbacks goes through here
        [HttpPost]
        public async Task<IActionResult> CallbackEvent([FromBody] CloudEvent[] callbackEvents)
        {
            _logger.LogInformation($"WE HAVE AN EVENT A CALL BACK !!! !!!!");
            //CallAutomationEventProcessor processor = _callService.CallAutomationClient.GetEventProcessor();
            //processor.ProcessEvents(callbackEvents);

            
            foreach (var callbackEvent in callbackEvents)
            {
                CallAutomationEventBase @event = CallAutomationEventParser.Parse(callbackEvent);
                _logger.LogInformation($"Event received: {JsonConvert.SerializeObject(@event, Formatting.Indented)}");

                if (callbackEvent.Type.Contains("CallConnected"))
                {
                    _logger.LogInformation($"Activating recording");
                    await _callService.startRecordingAsync(@event.ServerCallId);
                }
                else if (callbackEvent.Type.Contains("CallDisconnected"))
                {
                    _logger.LogInformation($"CallDisconnecting");
                    await HandleCallDisconnected(@event.CallConnectionId,@event.ServerCallId);
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

        private async Task HandleCallConnected(CallbackEvent callbackEvent)
        {
            // Start the call interaction
            await _callService.StartCallInteraction(callbackEvent.CallConnectionId,HttpContext);
        }

        private async Task HandleCallDisconnected(string callId, string servercallId)
        {
            // Cleanup resources
            await _callService.CleanupCall(callId, servercallId);
        }

        private async Task HandlePlaybackCompleted(CallbackEvent callbackEvent)
        {
            await _callService.HandlePlaybackCompleted(callbackEvent.CallConnectionId);
        }

        

    }


}
