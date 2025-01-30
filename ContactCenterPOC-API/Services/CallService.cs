using Azure;
using Azure.Communication;
using Azure.Communication.CallAutomation;
using ContactCenterPOC.Models;
using System.Net.WebSockets;


namespace ContactCenterPOC.Services
{
    public class CallService
    {
        private readonly CallAutomationClient _callAutomationClient;
        public CallAutomationClient CallAutomationClient {get {return _callAutomationClient;} }
        private readonly PhoneNumberIdentifier _callerPhoneNumber;
        private PhoneNumberIdentifier _targetPhoneNumber;
        private readonly string _callbackUri;
        private readonly ILogger<CallService> _logger;
        private AcsMediaStreamingHandler _acsMediaStreamingHandler;
        //private readonly RealtimeAudioService _realtimeAudioService;
        private readonly Dictionary<string, CallConnection> _activeConnections;
        private Response<CreateCallResult> _createCallResult;
        private readonly IConfiguration _configuration ;

        public CallService(IConfiguration configuration, ILogger<CallService> logger)
        {
            _logger = logger; 
            _configuration = configuration;
            var connectionString = configuration["AzureCommunicationServices:ConnectionString"];
            _callAutomationClient = new CallAutomationClient(connectionString);
            _callerPhoneNumber = new PhoneNumberIdentifier( configuration["AzureCommunicationServices:PhoneNumber"]);
            _callbackUri = configuration["CallbackUrl"];
            _activeConnections = new Dictionary<string, CallConnection>();
        }

        public async Task<string> InitiateCall(string targetPhoneNumber, HttpContext httpContext)
        {
            // Create the call participants
            _targetPhoneNumber = new PhoneNumberIdentifier(targetPhoneNumber);
            // Create the call invite
            var callInvite = new CallInvite(_targetPhoneNumber, _callerPhoneNumber);
            
            // Specify callback URI for events
            var callbackUri = new Uri(_callbackUri);
            // Initiate the call with correct parameters

            var callOptions = new CreateCallOptions(callInvite, callbackUri) {
                //CallIntelligenceOptions = new CallIntelligenceOptions() {  CognitiveServicesEndpoint = new Uri(_cogServiceUri) }, //"https://ys-aoai-sweden.openai.azure.com") },
                //TranscriptionOptions = new TranscriptionOptions(new Uri(_cogServiceUri), "en-US", false, TranscriptionTransport.Websocket)
            };
            var wssuri = new Uri(_callbackUri.Replace("https", "wss") + "/ws");
            var mediaStreamingOptions = new MediaStreamingOptions(
                wssuri,
                MediaStreamingContent.Audio,
                MediaStreamingAudioChannel.Mixed,
                startMediaStreaming: true
                )
            {
                EnableBidirectional = true,
                AudioFormat = AudioFormat.Pcm24KMono
            };
            callOptions.MediaStreamingOptions = mediaStreamingOptions;
            _createCallResult = await _callAutomationClient.CreateCallAsync(callOptions);
            return _createCallResult.Value.CallConnection.CallConnectionId;
        }
        


        public async Task StartCallInteraction(string callConnectionId, HttpContext context)
        {
            var callConnection = _callAutomationClient.GetCallConnection(callConnectionId);
            _activeConnections[callConnectionId] = callConnection;


            //trigger with call create Result
            //var eventResult = await _createCallResult.Value.WaitForEventProcessorAsync();
            //CallConnected returnedEvent = eventResult.SuccessResult;

            // Initialize GPT-4o Realtime session

            //if (context.WebSockets.IsWebSocketRequest)
            //  StartCallInteraction4(context).Wait();  

            // Start capturing audio from the call
            //var mediaSession = callConnection.GetCallMedia();

        }


        public async Task StartCallInteraction(HttpContext httpContext)
        {
            if (httpContext.WebSockets.IsWebSocketRequest)
            {
                var ws = await httpContext.WebSockets.AcceptWebSocketAsync();
                // Accept the WebSocket connection
                _logger.LogInformation("There is WebSocket connected");

                // Handle incoming messages (or process streaming)
                if (ws.State == WebSocketState.Open)
                {
                    //var result = await webSocket.ReceiveAsync(new ArraySegment<byte>(buffer), CancellationToken.None);

                
                    _acsMediaStreamingHandler = new AcsMediaStreamingHandler(ws, _configuration,_logger );
                    await _acsMediaStreamingHandler.ProcessWebSocketAsync();
                }
            }
            else
            {
                _logger.LogInformation("Not a WebSocket request :" +httpContext.Response.StatusCode ); 
                _logger.LogInformation("Not a WebSocket request");
            
            }
        }

        


        public async Task StartCallInteraction2(string callConnectionId)
        {
            
            // Create a call connection client
            var callConnection = _callAutomationClient.GetCallConnection(callConnectionId);

            _logger.LogInformation("We are here 1 ////////////////");
            var eventResult = await _createCallResult.Value.WaitForEventProcessorAsync();
            CallConnected returnedEvent= eventResult.SuccessResult;
            // Play initial greeting
            //var playsourceuri = new Uri("file:///C:/Users/serha/OneDrive/Documents/DevProjects/CallCenterPOC/CallCenterCoreAPI/Media/YassineMagicienwav.wav");


            //string filePath = "/sound/mysound.wav"; // Relative to wwwroot
            
            //var playsourceuri = new Uri("https://f8f5-167-220-255-162.ngrok-free.app/Media/YassineMagicienwav.wav");
            //var playSource = new FileSource(playsourceuri);
            var ssml = "<speak version='1.0' xml:lang='en-US'><voice name='en-US-JennyNeural'>Hello! Is this working?</voice></speak>";
            var playSource = new SsmlSource(ssml);
            var playSources = new List<PlaySource> { playSource };
            var playTo = new List<CommunicationIdentifier> { _targetPhoneNumber };
            var playOptions = new PlayOptions(playSources, playTo)
            {
                Loop = false,
                OperationContext = "InitialGreeting",
                OperationCallbackUri = new Uri(_callbackUri)
            };

            _logger.LogInformation("We are here 2 ////////////////");

            var playResult = await callConnection.GetCallMedia().PlayAsync(playOptions);


            PlayEventResult playEventResult = await playResult.Value.WaitForEventProcessorAsync();

            _logger.LogInformation("We are here 3 ////////////////");

            // check if the play was completed successfully
            if (playEventResult.IsSuccess)
            {
                _logger.LogInformation("We are here SUCCESS ////////////////");
                // success play!
                PlayCompleted playCompleted = playEventResult.SuccessResult;
            }
            else
            {

                PlayFailed playFailed = playEventResult.FailureResult;
                _logger.LogError($"Play failed. Reason: {playFailed.ResultInformation?.Message}");

                // Log additional details
                _logger.LogError($"Error Code: {playFailed.ResultInformation?.Code}");
                _logger.LogError($"Subcode: {playFailed.ResultInformation?.SubCode}");

                _logger.LogInformation("We are here FAIL ////////////////");
                // failed to play the audio.
            }
            _logger.LogInformation("We are here Finished ////////////////");

        }

        public async Task CleanupCall(string callConnectionId)
        {
            // Implement cleanup logic
            _logger.LogInformation($"Call {callConnectionId} disconnected, cleaning up resources");
        }


        public async Task HandlePlaybackCompleted(string callConnectionId)
        {
            var callConnection = _callAutomationClient.GetCallConnection(callConnectionId);

            // Start recognizing speech after playback completes
            var recognizeOptions = new CallMediaRecognizeSpeechOptions(_targetPhoneNumber)
            {
                InterruptPrompt = true,
                OperationContext = "MainConversation",
                EndSilenceTimeout = TimeSpan.FromSeconds(5)
            };

            await callConnection.GetCallMedia().StartRecognizingAsync(recognizeOptions);
        }
    }
}
