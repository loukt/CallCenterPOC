using Azure.Communication.CallAutomation;
using Azure.Messaging;
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

        [HttpPost("events")]
        public async Task<IActionResult> HandleCallbackEvent([FromBody] CallbackEvent callbackEvent)
        {
            _logger.LogInformation($"Received event: {callbackEvent.EventType} for call {callbackEvent.CallConnectionId}");

            switch (callbackEvent.EventType)
            {
                case "Microsoft.Communication.CallConnected":
                    await HandleCallConnected(callbackEvent);
                    break;

                case "Microsoft.Communication.CallDisconnected":
                    await HandleCallDisconnected(callbackEvent);
                    break;

                case "Microsoft.Communication.PlayCompleted":
                    await HandlePlaybackCompleted(callbackEvent);
                    break;
            }

            return Ok();
        }

       

        [HttpPost]
        public async Task<IActionResult> CallbackEvent([FromBody] CloudEvent[] callbackEvents)
        {
            _logger.LogInformation($"WE HAVE AN EVENT A CALL BACK !!! !!!!");
            CallAutomationEventProcessor processor = _callService.CallAutomationClient.GetEventProcessor();
            processor.ProcessEvents(callbackEvents);

            foreach (var callbackEvent in callbackEvents)
            {

                CallAutomationEventBase @event = CallAutomationEventParser.Parse(callbackEvent);
                _logger.LogInformation($"Event received: {JsonConvert.SerializeObject(@event, Formatting.Indented)}");
                //_logger.LogInformation($"Received event Line 1: Id {callbackEvent.Id} for Subject {callbackEvent.Subject}");

                //               _logger.LogInformation($"Received event Line 2: Type {callbackEvent.Type} for datascehema {callbackEvent.DataSchema}");

                //             _logger.LogInformation($"Received event Line 3: Data Content Type {callbackEvent.DataContentType} for Data {callbackEvent.Data}");
            }

            

            return Ok();
        }

        [HttpGet("ws")]
        public async Task<IActionResult> Get()
        {
            
            if (HttpContext.WebSockets.IsWebSocketRequest)
            {
                _logger.LogInformation($"Callback triggered on WS It's rerouted to the websocket AND WORKING");

                await _callService.StartCallInteraction(HttpContext);
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

        private async Task HandleCallDisconnected(CallbackEvent callbackEvent)
        {
            // Cleanup resources
            await _callService.CleanupCall(callbackEvent.CallConnectionId);
        }

        private async Task HandlePlaybackCompleted(CallbackEvent callbackEvent)
        {
            await _callService.HandlePlaybackCompleted(callbackEvent.CallConnectionId);
        }



    }


}
