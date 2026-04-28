using Azure.Communication.CallAutomation;
using ContactCenterPOC.Hubs;
using ContactCenterPOC.Services;
using Microsoft.AspNetCore.SignalR;
using System.Collections.Concurrent;
using System.Net.WebSockets;
using System.Text;

namespace ContactCenterPOC.Models
{
    public class AcsMediaStreamingHandler
    {
        private WebSocket m_webSocket;
        private CancellationTokenSource m_cts;
        private MemoryStream m_buffer;
        private AzureOpenAIService? m_aiServiceHandler;
        private VoiceLiveService? m_vlServiceHandler;
        private IConfiguration m_configuration;
        private readonly ILogger<CallService> _logger;
        private readonly IHubContext<TranscriptHub> _hubContext;
        private readonly string _callConnectionId;
        private readonly Func<string, Task>? _hangUpCallback;
        private readonly SentimentAnalysisService? _sentimentService;
        private readonly EmotionAnalysisService? _emotionService;
        private readonly ConcurrentDictionary<string, ActiveCall>? _activeCalls;
        private string _selectedVoice = "alloy";
        private readonly string _voiceApiMode;
        private readonly string? _voiceLiveModel;
        private readonly string? _selectedVoiceLiveVoice;
        private readonly VoiceLiveConfig? _voiceLiveConfig;

        // Constructor to inject dependencies and call connection ID
        public AcsMediaStreamingHandler(
            WebSocket webSocket,
            IConfiguration configuration,
            ILogger<CallService> logger,
            IHubContext<TranscriptHub> hubContext,
            string callConnectionId,
            Func<string, Task>? hangUpCallback = null,
            SentimentAnalysisService? sentimentService = null,
            ConcurrentDictionary<string, ActiveCall>? activeCalls = null,
            EmotionAnalysisService? emotionService = null,
            string? selectedVoice = null,
            string voiceApiMode = "ChatGPT",
            string? voiceLiveModel = null,
            string? selectedVoiceLiveVoice = null,
            VoiceLiveConfig? voiceLiveConfig = null)
        {
            m_webSocket = webSocket;
            m_configuration = configuration;
            m_buffer = new MemoryStream();
            m_cts = new CancellationTokenSource();
            _logger = logger;
            _hubContext = hubContext;
            _callConnectionId = callConnectionId;
            _hangUpCallback = hangUpCallback;
            _sentimentService = sentimentService;
            _emotionService = emotionService;
            _activeCalls = activeCalls;
            _selectedVoice = selectedVoice ?? "alloy";
            _voiceApiMode = voiceApiMode;
            _voiceLiveModel = voiceLiveModel;
            _selectedVoiceLiveVoice = selectedVoiceLiveVoice;
            _voiceLiveConfig = voiceLiveConfig;
        }

        // Method to receive messages from WebSocket — dispatches to VoiceLive or OpenAI
        public async Task ProcessWebSocketAsync(string callContextPrompt)
        {
            
            if (m_webSocket == null)
            {
                return;
            }

            // Dispatch to VoiceLive or OpenAI based on VoiceApiMode (simple if/switch per Constitution I)
            if (_voiceApiMode == "VoiceLive" && _voiceLiveConfig != null && _voiceLiveConfig.IsConfigured)
            {
                _logger.LogInformation("[MediaStream-{CallId}] Dispatching to VoiceLiveService (model={Model}, voice={Voice})",
                    _callConnectionId, _voiceLiveModel, _selectedVoiceLiveVoice);
                m_vlServiceHandler = new VoiceLiveService(this, callContextPrompt, _voiceLiveConfig, _logger, _hubContext,
                    _callConnectionId, _voiceLiveModel ?? "gpt-realtime-mini", _selectedVoiceLiveVoice,
                    _hangUpCallback, _sentimentService, _activeCalls, _emotionService);

                try
                {
                    m_vlServiceHandler.StartConversation();
                    await StartReceivingFromAcsMediaWebSocket();
                }
                catch (Exception ex)
                {
                    _logger.LogInformation($" at MediaStreamHandler Process websocket Exception -> {ex}");
                }
                finally
                {
                    m_vlServiceHandler.Close();
                    this.Close();

                    if (_hangUpCallback != null)
                    {
                        try
                        {
                            await _hangUpCallback(_callConnectionId);
                        }
                        catch (Exception ex)
                        {
                            _logger.LogError(ex, "Error during hang-up callback for call {CallConnectionId}", _callConnectionId);
                        }
                    }
                }
            }
            else
            {
                // Default: OpenAI Realtime path
                m_aiServiceHandler = new AzureOpenAIService(this, callContextPrompt, m_configuration, _logger, _hubContext, _callConnectionId, _hangUpCallback, _sentimentService, _activeCalls, _emotionService, _selectedVoice);

                try
                {
                    m_aiServiceHandler.StartConversation();
                    await StartReceivingFromAcsMediaWebSocket();
                }
                catch (Exception ex)
                {
                    _logger.LogInformation($" at MediaStreamHandler Process websocket Exception -> {ex}");
                }
                finally
                {
                    m_aiServiceHandler.Close();
                    this.Close();

                    if (_hangUpCallback != null)
                    {
                        try
                        {
                            await _hangUpCallback(_callConnectionId);
                        }
                        catch (Exception ex)
                        {
                            _logger.LogError(ex, "Error during hang-up callback for call {CallConnectionId}", _callConnectionId);
                        }
                    }
                }
            }
        }


        public async Task ProcessWebSocketAsync()
        {

            if (m_webSocket == null)
            {
                return;
            }

            // No-prompt overload always uses OpenAI path
            m_aiServiceHandler = new AzureOpenAIService(this, m_configuration, _logger, _hubContext, _callConnectionId, _hangUpCallback, _sentimentService, _activeCalls, _emotionService);

            try
            {
                m_aiServiceHandler.StartConversation();
                await StartReceivingFromAcsMediaWebSocket();
            }
            catch (Exception ex)
            {
                _logger.LogInformation($" at MediaStreamHandler Process websocket Exception -> {ex}");
            }
            finally
            {
                m_aiServiceHandler.Close();
                this.Close();

                if (_hangUpCallback != null)
                {
                    try
                    {
                        await _hangUpCallback(_callConnectionId);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Error during hang-up callback for call {CallConnectionId}", _callConnectionId);
                    }
                }
            }
        }

        public async Task SendMessageAsync(string message)
        {
            if (m_webSocket?.State == WebSocketState.Open)
            {
                byte[] jsonBytes = Encoding.UTF8.GetBytes(message);

                // Send the PCM audio chunk over WebSocket
                await m_webSocket.SendAsync(new ArraySegment<byte>(jsonBytes), WebSocketMessageType.Text, endOfMessage: true, CancellationToken.None);
            }
        }

        public async Task CloseWebSocketAsync(WebSocketReceiveResult result)
        {
            await m_webSocket.CloseAsync(result.CloseStatus.Value, result.CloseStatusDescription, CancellationToken.None);
        }

        public async Task CloseNormalWebSocketAsync()
        {
            await m_webSocket.CloseAsync(WebSocketCloseStatus.NormalClosure, "Stream completed", CancellationToken.None);
        }

        public void Close()
        {
            m_cts.Cancel();
            m_cts.Dispose();
            m_buffer.Dispose();
        }

        private async Task WriteToAzOpenAIServiceInputStream(string data)
        {
            var input = StreamingData.Parse(data);
            if (input is AudioData audioData)
            {
                if (!audioData.IsSilent)
                {
                    using (var ms = new MemoryStream(audioData.Data))
                    {
                        if (m_vlServiceHandler != null)
                        {
                            await m_vlServiceHandler.SendAudioToExternalAI(ms);
                        }
                        else if (m_aiServiceHandler != null)
                        {
                            await m_aiServiceHandler.SendAudioToExternalAI(ms);
                        }
                    }
                }
            }
            else
            {
                _logger.LogInformation("[MediaStream-{CallId}] Received non-audio streaming data: {Type}", _callConnectionId, input?.GetType().Name ?? "null");
            }
        }

        // receive messages from WebSocket
        private async Task StartReceivingFromAcsMediaWebSocket()
        {
            if (m_webSocket == null)
            {
                return;
            }
            try
            {
                _logger.LogInformation("[MediaStream-{CallId}] Starting to receive from ACS media WebSocket (state={State})", _callConnectionId, m_webSocket.State);
                var messageBuffer = new MemoryStream();
                int messageCount = 0;
                while (m_webSocket.State == WebSocketState.Open || m_webSocket.State == WebSocketState.Closed)
                {
                    byte[] receiveBuffer = new byte[4096];
                    WebSocketReceiveResult receiveResult = await m_webSocket.ReceiveAsync(new ArraySegment<byte>(receiveBuffer), m_cts.Token);

                    if (receiveResult.MessageType == WebSocketMessageType.Close)
                    {
                        _logger.LogInformation("[MediaStream-{CallId}] WebSocket close received after {Count} messages", _callConnectionId, messageCount);
                        break;
                    }

                    // Write received bytes to message buffer
                    messageBuffer.Write(receiveBuffer, 0, receiveResult.Count);

                    // Only process when the full message has been received
                    if (receiveResult.EndOfMessage)
                    {
                        messageCount++;
                        if (messageCount <= 3 || messageCount % 100 == 0)
                        {
                            _logger.LogInformation("[MediaStream-{CallId}] ACS message #{Count} received ({ByteCount} bytes)", 
                                _callConnectionId, messageCount, messageBuffer.Length);
                        }
                        string data = Encoding.UTF8.GetString(messageBuffer.ToArray()).TrimEnd('\0');
                        messageBuffer.SetLength(0); // Reset buffer for next message
                        await WriteToAzOpenAIServiceInputStream(data);
                    }
                }
                _logger.LogInformation("[MediaStream-{CallId}] ACS WebSocket receive loop ended. Total messages: {Count}", _callConnectionId, messageCount);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[MediaStream-{CallId}] Exception in ACS media socket receive loop", _callConnectionId);
            }
        }
    }

}


