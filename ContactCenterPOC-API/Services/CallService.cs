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
        private readonly Dictionary<string, CallConnection> _activeConnections;
        private readonly Dictionary<string, string> _activeCallPrompt;
        private readonly Dictionary<string, string> _activeCallNumbers;
        private readonly Dictionary<string, string> _activeRecordings;
        private Response<CreateCallResult> _createCallResult;
        private readonly IConfiguration _configuration ;

        public CallService(IConfiguration configuration, ILogger<CallService> logger)
        {
            _logger = logger; 
            _configuration = configuration;
            var connectionString = configuration["AzureCommunicationServices:ConnectionString"];
            _callbackUri = _configuration["CallbackUrl"];
            _callAutomationClient = new CallAutomationClient(connectionString);
            _callerPhoneNumber = new PhoneNumberIdentifier( configuration["AzureCommunicationServices:PhoneNumber"]);
            _activeConnections = new Dictionary<string, CallConnection>();
            _activeRecordings = new Dictionary<string, string>();
            _activeCallPrompt = new Dictionary<string, string>();
            _activeCallNumbers = new Dictionary<string, string>();
        }

        public async Task<string> InitiateCall(string targetPhoneNumber,string callContextPrompt, HttpContext httpContext)
        {
            
            // Create the call participants
            _targetPhoneNumber = new PhoneNumberIdentifier(targetPhoneNumber);
            // Create the call invite
            var callInvite = new CallInvite(_targetPhoneNumber, _callerPhoneNumber);
            
            // Specify callback URI for events
            var callbackUri = new Uri(_callbackUri);
            // Initiate the call with correct parameters

            var callOptions = new CreateCallOptions(callInvite, callbackUri);
            var wssuri = new Uri(_callbackUri.Replace("https", "wss") + "/ws?targetNumber="+targetPhoneNumber);
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
            var callConnectionId = _createCallResult.Value.CallConnection.CallConnectionId;
            _activeConnections[callConnectionId] = _createCallResult.Value.CallConnection;
            _activeCallPrompt[callConnectionId] = callContextPrompt;
            _activeCallNumbers[targetPhoneNumber] = callConnectionId;
            return _createCallResult.Value.CallConnection.CallConnectionId;
        }


        public async Task StartCallInteraction(string callId, HttpContext httpContext)
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
                    _acsMediaStreamingHandler = new AcsMediaStreamingHandler(ws, _configuration, _logger);
                    await _acsMediaStreamingHandler.ProcessWebSocketAsync(_activeCallPrompt[callId]);
                }
            }
            else
            {
                _logger.LogInformation("Not a WebSocket request :" + httpContext.Response.StatusCode);
                _logger.LogInformation("Not a WebSocket request");

            }
        }

        public async Task StartCallInteraction( HttpContext httpContext, string targetNumber)
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
                    _acsMediaStreamingHandler = new AcsMediaStreamingHandler(ws, _configuration, _logger);
                    var callConnectionId = _activeCallNumbers[targetNumber];
                    await _acsMediaStreamingHandler.ProcessWebSocketAsync(_activeCallPrompt[callConnectionId]);
                }
            }
            else
            {
                _logger.LogInformation("Not a WebSocket request :" + httpContext.Response.StatusCode);
                _logger.LogInformation("Not a WebSocket request");

            }
        }


        public async Task StartCallInteractionToPlaySound(string callConnectionId)
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

        public async Task CleanupCall(string callConnectionId, string serverCallId)
        {
            // Implement cleanup logic
            _logger.LogInformation($"Call {callConnectionId} disconnected, cleaning up resources");
            await stopRecordingAsync(serverCallId);
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


        public async Task startRecordingAsync(String serverCallId)
        {
            var callConnection = _callAutomationClient.GetCallConnection(serverCallId);
            StartRecordingOptions recordingOptions = new StartRecordingOptions(new ServerCallLocator(serverCallId)) {
                RecordingChannel = RecordingChannel.Mixed,
                RecordingContent = RecordingContent.Audio,
                RecordingFormat = RecordingFormat.Mp3,
                RecordingStorage = RecordingStorage.CreateAzure
                RecordingStorage(new Uri(_configuration["BlobContainer"]))
            };
            
            var startRecordingResponse = await _callAutomationClient.GetCallRecording().StartAsync(recordingOptions).ConfigureAwait(false);
            _activeRecordings[serverCallId] = startRecordingResponse.Value.RecordingId;
        }

        public async Task stopRecordingAsync(String serverCallId)
        {
            try {

                await _callAutomationClient.GetCallRecording().StopAsync(_activeRecordings[serverCallId]);

            } catch (Exception e) { }
        }



    }
}
