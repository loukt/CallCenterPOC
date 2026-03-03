using Azure.AI.OpenAI;
using Azure.Communication.CallAutomation;
using Azure.Identity;
using ContactCenterPOC.Hubs;
using ContactCenterPOC.Models;
using Microsoft.AspNetCore.SignalR;
using OpenAI.RealtimeConversation;
using System.ClientModel;
using System.Collections.Concurrent;


namespace ContactCenterPOC.Services
{
    public class AzureOpenAIService
    {
        private CancellationTokenSource m_cts;
        private RealtimeConversationSession m_aiSession;
        private AcsMediaStreamingHandler m_mediaStreaming;
        private MemoryStream m_memoryStream;
        private ILogger<CallService> _logger;
        private readonly IHubContext<TranscriptHub> _hubContext;
        private readonly string _callConnectionId;
        private readonly Func<string, Task>? _hangUpCallback;
        private readonly SentimentAnalysisService? _sentimentService;
        private readonly EmotionAnalysisService? _emotionService;
        private readonly ConcurrentDictionary<string, ActiveCall>? _activeCalls;
        private bool _sessionReady = false;

        public AzureOpenAIService(
            AcsMediaStreamingHandler mediaStreaming,
            IConfiguration configuration,
            ILogger<CallService> logger,
            IHubContext<TranscriptHub> hubContext,
            string callConnectionId,
            Func<string, Task>? hangUpCallback = null,
            SentimentAnalysisService? sentimentService = null,
            ConcurrentDictionary<string, ActiveCall>? activeCalls = null,
            EmotionAnalysisService? emotionService = null)
        {
            m_mediaStreaming = mediaStreaming;
            m_cts = new CancellationTokenSource();
            m_memoryStream = new MemoryStream();
            _logger = logger;
            _hubContext = hubContext;
            _callConnectionId = callConnectionId;
            _hangUpCallback = hangUpCallback;
            _sentimentService = sentimentService;
            _emotionService = emotionService;
            _activeCalls = activeCalls;

            try
            {
                _logger.LogInformation("[AI-{CallId}] Creating AI session (no prompt)...", callConnectionId);
                m_aiSession = CreateAISessionAsync(configuration, null).GetAwaiter().GetResult();
                _sessionReady = true;
                _logger.LogInformation("[AI-{CallId}] AI session created successfully", callConnectionId);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[AI-{CallId}] FAILED to create AI session", callConnectionId);
                throw;
            }
        }

        public AzureOpenAIService(
            AcsMediaStreamingHandler mediaStreaming,
            string prompt,
            IConfiguration configuration,
            ILogger<CallService> logger,
            IHubContext<TranscriptHub> hubContext,
            string callConnectionId,
            Func<string, Task>? hangUpCallback = null,
            SentimentAnalysisService? sentimentService = null,
            ConcurrentDictionary<string, ActiveCall>? activeCalls = null,
            EmotionAnalysisService? emotionService = null)
        {
            m_mediaStreaming = mediaStreaming;
            m_cts = new CancellationTokenSource();
            m_memoryStream = new MemoryStream();
            _logger = logger;
            _hubContext = hubContext;
            _callConnectionId = callConnectionId;
            _hangUpCallback = hangUpCallback;
            _sentimentService = sentimentService;
            _emotionService = emotionService;
            _activeCalls = activeCalls;

            try
            {
                _logger.LogInformation("[AI-{CallId}] Creating AI session with prompt ({PromptLen} chars)...", callConnectionId, prompt?.Length ?? 0);
                m_aiSession = CreateAISessionAsync(configuration, prompt).GetAwaiter().GetResult();
                _sessionReady = true;
                _logger.LogInformation("[AI-{CallId}] AI session created successfully", callConnectionId);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[AI-{CallId}] FAILED to create AI session", callConnectionId);
                throw;
            }
        }



        private async Task<RealtimeConversationSession> CreateAISessionAsync(IConfiguration configuration, string prompt)
        {
            var openAiUri = configuration["AzureOpenAI:EndpointUri"];
            ArgumentNullException.ThrowIfNullOrEmpty(openAiUri);

            var openAiModelName = configuration["AzureOpenAI:DeploymentName"];
            ArgumentNullException.ThrowIfNullOrEmpty(openAiModelName);

            string systemPrompt = prompt;
            if (systemPrompt == null)
            {
                systemPrompt = configuration["AzureOpenAI:SystemPrompt"];
                ArgumentNullException.ThrowIfNullOrEmpty(systemPrompt);
            }

            _logger.LogInformation("[AI-{CallId}] Connecting to OpenAI Realtime: endpoint={Endpoint}, deployment={Deployment}",
                _callConnectionId, openAiUri, openAiModelName);

            // Use DefaultAzureCredential (Managed Identity in Azure, developer credentials locally)
            // because the Azure OpenAI resource has disableLocalAuth=true (API key auth disabled)
            var credential = new DefaultAzureCredential();
            _logger.LogInformation("[AI-{CallId}] Using DefaultAzureCredential (Managed Identity / Entra ID)", _callConnectionId);

            var aiClient = new AzureOpenAIClient(new Uri(openAiUri), credential);
            var realtimeClient = aiClient.GetRealtimeConversationClient(openAiModelName);
            
            _logger.LogInformation("[AI-{CallId}] Starting conversation session...", _callConnectionId);
            var session = await realtimeClient.StartConversationSessionAsync();
            _logger.LogInformation("[AI-{CallId}] Conversation session started, configuring...", _callConnectionId);

            // Session options control connection-wide behavior shared across all conversations,
            // including audio input format and voice activity detection settings.
            ConversationSessionOptions sessionOptions = new()
            {
                Instructions = systemPrompt,
                Voice = ConversationVoice.Alloy,
                InputAudioFormat = ConversationAudioFormat.Pcm16,
                OutputAudioFormat = ConversationAudioFormat.Pcm16,
                InputTranscriptionOptions = new()
                {
                    Model = "whisper-1",
                },
                TurnDetectionOptions = ConversationTurnDetectionOptions.CreateServerVoiceActivityTurnDetectionOptions(0.5f, TimeSpan.FromMilliseconds(500), TimeSpan.FromMilliseconds(500)),
            };

            await session.ConfigureSessionAsync(sessionOptions);
            _logger.LogInformation("[AI-{CallId}] Session configured (voice=Alloy, format=PCM16, VAD enabled)", _callConnectionId);
            return session;
        }

        // Loop and wait for the AI response
        private async Task GetOpenAiStreamResponseAsync()
        {
            try
            {
                _logger.LogInformation("[AI-{CallId}] Starting initial AI response...", _callConnectionId);
                await m_aiSession.StartResponseAsync();
                _logger.LogInformation("[AI-{CallId}] Listening for AI updates...", _callConnectionId);
                int audioChunkCount = 0;
                
                await foreach (ConversationUpdate update in m_aiSession.ReceiveUpdatesAsync(m_cts.Token))
                {
                    if (update is ConversationSessionStartedUpdate sessionStartedUpdate)
                    {
                        _logger.LogInformation("[AI-{CallId}] Session started. ID: {SessionId}", _callConnectionId, sessionStartedUpdate.SessionId);
                    }

                    if (update is ConversationInputSpeechStartedUpdate speechStartedUpdate)
                    {
                        _logger.LogInformation("[AI-{CallId}] Voice activity detection started at {AudioStartTime} ms", _callConnectionId, speechStartedUpdate.AudioStartTime);
                        // Barge-in, send stop audio
                        var jsonString = OutStreamingData.GetStopAudioForOutbound();
                        await m_mediaStreaming.SendMessageAsync(jsonString);
                    }

                    if (update is ConversationInputSpeechFinishedUpdate speechFinishedUpdate)
                    {
                        _logger.LogInformation("[AI-{CallId}] Voice activity detection ended at {AudioEndTime} ms", _callConnectionId, speechFinishedUpdate.AudioEndTime);
                    }

                    if (update is ConversationItemStreamingStartedUpdate itemStartedUpdate)
                    {
                        _logger.LogInformation("[AI-{CallId}] Begin streaming of new item", _callConnectionId);
                    }

                    // Audio transcript updates contain the incremental text matching the generated
                    // output audio.
                    if (update is ConversationItemStreamingAudioTranscriptionFinishedUpdate outputTranscriptDeltaUpdate)
                    {
                        _logger.LogInformation("[AI-{CallId}] AI transcript: {Transcript}", _callConnectionId, outputTranscriptDeltaUpdate.Transcript);
                        var aiEntry = new TranscriptEntry
                        {
                            CallConnectionId = _callConnectionId,
                            Speaker = SpeakerType.AI,
                            Text = outputTranscriptDeltaUpdate.Transcript,
                            Timestamp = DateTimeOffset.UtcNow
                        };

                        // Accumulate on ActiveCall for persistence
                        if (_activeCalls != null && _activeCalls.TryGetValue(_callConnectionId, out var activeCall))
                        {
                            activeCall.TranscriptEntries.Add(aiEntry);
                        }

                        await _hubContext.Clients.Group(_callConnectionId)
                            .SendAsync("TranscriptUpdate", aiEntry);

                        // Fire-and-forget sentiment analysis
                        FireAndForgetSentiment(aiEntry);
                        // Fire-and-forget emotion analysis
                        FireAndForgetEmotion(aiEntry);
                    }

                    // Audio delta updates contain the incremental binary audio data of the generated output
                    // audio, matching the output audio format configured for the session.
                    if (update is ConversationItemStreamingPartDeltaUpdate deltaUpdate)
                    {
                        if (deltaUpdate.AudioBytes != null)
                        {
                            audioChunkCount++;
                            if (audioChunkCount <= 3 || audioChunkCount % 50 == 0)
                            {
                                _logger.LogInformation("[AI-{CallId}] Sending audio chunk #{ChunkNum} ({ByteCount} bytes) to ACS", 
                                    _callConnectionId, audioChunkCount, deltaUpdate.AudioBytes.ToArray().Length);
                            }
                            var jsonString = OutStreamingData.GetAudioDataForOutbound(deltaUpdate.AudioBytes.ToArray());
                            await m_mediaStreaming.SendMessageAsync(jsonString);
                        }
                        else
                        {
                            _logger.LogWarning("[AI-{CallId}] Received delta update but AudioBytes is null", _callConnectionId);
                        }
                    }

                    if (update is ConversationItemStreamingTextFinishedUpdate itemFinishedUpdate)
                    {
                        _logger.LogInformation("[AI-{CallId}] Item streaming finished, response_id={ResponseId}", _callConnectionId, itemFinishedUpdate.ResponseId);
                    }

                    if (update is ConversationInputTranscriptionFinishedUpdate transcriptionCompletedUpdate)
                    {
                        _logger.LogInformation("[AI-{CallId}] User audio transcript: {Transcript}", _callConnectionId, transcriptionCompletedUpdate.Transcript);
                        var recipientEntry = new TranscriptEntry
                        {
                            CallConnectionId = _callConnectionId,
                            Speaker = SpeakerType.Recipient,
                            Text = transcriptionCompletedUpdate.Transcript,
                            Timestamp = DateTimeOffset.UtcNow
                        };

                        // Accumulate on ActiveCall for persistence
                        if (_activeCalls != null && _activeCalls.TryGetValue(_callConnectionId, out var activeCall2))
                        {
                            activeCall2.TranscriptEntries.Add(recipientEntry);
                        }

                        await _hubContext.Clients.Group(_callConnectionId)
                            .SendAsync("TranscriptUpdate", recipientEntry);

                        // Fire-and-forget sentiment analysis
                        FireAndForgetSentiment(recipientEntry);
                        // Fire-and-forget emotion analysis
                        FireAndForgetEmotion(recipientEntry);
                    }

                    if (update is ConversationResponseFinishedUpdate turnFinishedUpdate)
                    {
                        _logger.LogInformation("[AI-{CallId}] Model turn generation finished. Status: {Status}. Total audio chunks sent: {ChunkCount}", 
                            _callConnectionId, turnFinishedUpdate.Status, audioChunkCount);
                    }

                    if (update is ConversationErrorUpdate errorUpdate)
                    {
                        _logger.LogError("[AI-{CallId}] OpenAI Realtime error: {ErrorMessage}", _callConnectionId, errorUpdate.Message);
                        if (_hangUpCallback != null)
                        {
                            await _hangUpCallback(_callConnectionId);
                        }
                        break;
                    }
                }
                _logger.LogInformation("[AI-{CallId}] AI response loop ended. Total audio chunks: {ChunkCount}", _callConnectionId, audioChunkCount);
            }
            catch (OperationCanceledException e)
            {
                _logger.LogInformation("[AI-{CallId}] AI response loop cancelled: {Message}", _callConnectionId, e.Message);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[AI-{CallId}] Exception during AI streaming", _callConnectionId);
                if (_hangUpCallback != null)
                {
                    try { await _hangUpCallback(_callConnectionId); }
                    catch (Exception cbEx) { _logger.LogWarning(cbEx, "[AI-{CallId}] Hang-up callback failed after AI error", _callConnectionId); }
                }
            }
        }

        private void FireAndForgetSentiment(TranscriptEntry entry)
        {
            if (_sentimentService == null) return;

            _ = Task.Run(async () =>
            {
                try
                {
                    // Build rolling 5-second context: aggregate recent transcript for better sentiment
                    string textToAnalyze = entry.Text;
                    if (_activeCalls != null && _activeCalls.TryGetValue(_callConnectionId, out var call))
                    {
                        var cutoff = entry.Timestamp.AddSeconds(-5);
                        var recentEntries = call.TranscriptEntries
                            .Where(e => e.Timestamp >= cutoff && e.Timestamp <= entry.Timestamp)
                            .OrderBy(e => e.Timestamp)
                            .ToList();

                        if (recentEntries.Count > 1)
                        {
                            textToAnalyze = string.Join(" ", recentEntries.Select(e => e.Text));
                        }
                    }

                    var sentiment = await _sentimentService.AnalyzeAsync(textToAnalyze);
                    entry.Sentiment = sentiment;

                    // Send SentimentUpdate event to the frontend
                    await _hubContext.Clients.Group(_callConnectionId)
                        .SendAsync("SentimentUpdate", new
                        {
                            callConnectionId = _callConnectionId,
                            entryTimestamp = entry.Timestamp,
                            sentiment = sentiment
                        });
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "[AI-{CallId}] Sentiment analysis failed for entry", _callConnectionId);
                }
            });
        }

        private void FireAndForgetEmotion(TranscriptEntry entry)
        {
            if (_emotionService == null) return;

            _ = Task.Run(async () =>
            {
                try
                {
                    // Build rolling 5-second context (same as sentiment)
                    string textToAnalyze = entry.Text;
                    if (_activeCalls != null && _activeCalls.TryGetValue(_callConnectionId, out var call))
                    {
                        var cutoff = entry.Timestamp.AddSeconds(-5);
                        var recentEntries = call.TranscriptEntries
                            .Where(e => e.Timestamp >= cutoff && e.Timestamp <= entry.Timestamp)
                            .OrderBy(e => e.Timestamp)
                            .ToList();

                        if (recentEntries.Count > 1)
                        {
                            textToAnalyze = string.Join(" ", recentEntries.Select(e => e.Text));
                        }
                    }

                    var emotion = await _emotionService.AnalyzeAsync(textToAnalyze);
                    entry.Emotion = emotion;

                    // Send EmotionUpdate event to the frontend
                    await _hubContext.Clients.Group(_callConnectionId)
                        .SendAsync("EmotionUpdate", new
                        {
                            callConnectionId = _callConnectionId,
                            entryTimestamp = entry.Timestamp,
                            emotion = emotion
                        });
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "[AI-{CallId}] Emotion analysis failed for entry", _callConnectionId);
                }
            });
        }

        public void StartConversation()
        {
            _logger.LogInformation("[AI-{CallId}] StartConversation called, sessionReady={Ready}", _callConnectionId, _sessionReady);
            if (!_sessionReady)
            {
                _logger.LogError("[AI-{CallId}] Cannot start conversation - session not ready", _callConnectionId);
                return;
            }
            _ = Task.Run(async () =>
            {
                try
                {
                    await GetOpenAiStreamResponseAsync();
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "[AI-{CallId}] Unhandled exception in AI response task", _callConnectionId);
                }
            });
        }

        public async Task SendAudioToExternalAI(MemoryStream memoryStream)
        {
            try
            {
                await m_aiSession.SendInputAudioAsync(memoryStream);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[AI-{CallId}] Failed to send audio to OpenAI ({ByteCount} bytes)", _callConnectionId, memoryStream.Length);
            }
        }

        public void Close()
        {
            m_cts.Cancel();
            m_cts.Dispose();
            m_aiSession.Dispose();
        }
    }
}