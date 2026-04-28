using Azure.AI.VoiceLive;
using Azure.Communication.CallAutomation;
using Azure.Identity;
using ContactCenterPOC.Hubs;
using ContactCenterPOC.Models;
using Microsoft.AspNetCore.SignalR;
using System.Collections.Concurrent;

namespace ContactCenterPOC.Services
{
    public class VoiceLiveService
    {
        private CancellationTokenSource m_cts;
        private VoiceLiveSession? m_session;
        private AcsMediaStreamingHandler m_mediaStreaming;
        private readonly ILogger<CallService> _logger;
        private readonly IHubContext<TranscriptHub> _hubContext;
        private readonly string _callConnectionId;
        private readonly Func<string, Task>? _hangUpCallback;
        private readonly SentimentAnalysisService? _sentimentService;
        private readonly EmotionAnalysisService? _emotionService;
        private readonly ConcurrentDictionary<string, ActiveCall>? _activeCalls;
        private readonly VoiceLiveConfig _voiceLiveConfig;
        private readonly string _selectedVoice;
        private readonly string _model;
        private readonly string _prompt;
        private bool _sessionReady = false;

        // Reconnection constants (FR-018: exponential backoff 1s, 2s, 4s — max 3)
        private const int MaxReconnectAttempts = 3;
        private static readonly int[] ReconnectDelaysMs = { 1000, 2000, 4000 };

        public VoiceLiveService(
            AcsMediaStreamingHandler mediaStreaming,
            string prompt,
            VoiceLiveConfig voiceLiveConfig,
            ILogger<CallService> logger,
            IHubContext<TranscriptHub> hubContext,
            string callConnectionId,
            string model = "gpt-4o-realtime-preview",
            string? selectedVoice = null,
            Func<string, Task>? hangUpCallback = null,
            SentimentAnalysisService? sentimentService = null,
            ConcurrentDictionary<string, ActiveCall>? activeCalls = null,
            EmotionAnalysisService? emotionService = null)
        {
            m_mediaStreaming = mediaStreaming;
            m_cts = new CancellationTokenSource();
            _logger = logger;
            _hubContext = hubContext;
            _callConnectionId = callConnectionId;
            _hangUpCallback = hangUpCallback;
            _sentimentService = sentimentService;
            _emotionService = emotionService;
            _activeCalls = activeCalls;
            _voiceLiveConfig = voiceLiveConfig;
            _selectedVoice = selectedVoice ?? "en-US-Ava:DragonHDLatestNeural";
            _model = model;
            _prompt = prompt;

            try
            {
                _logger.LogInformation("[VL-{CallId}] Creating VoiceLive session (model={Model}, voice={Voice})...",
                    callConnectionId, _model, _selectedVoice);
                m_session = CreateSessionAsync().GetAwaiter().GetResult();
                _sessionReady = true;
                _logger.LogInformation("[VL-{CallId}] VoiceLive session created successfully", callConnectionId);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[VL-{CallId}] FAILED to create VoiceLive session", callConnectionId);
                throw;
            }
        }

        private async Task<VoiceLiveSession> CreateSessionAsync()
        {
            var endpoint = new Uri(_voiceLiveConfig.EndpointUri);

            VoiceLiveClient client;
            if (!string.IsNullOrWhiteSpace(_voiceLiveConfig.Key))
            {
                client = new VoiceLiveClient(endpoint, new Azure.AzureKeyCredential(_voiceLiveConfig.Key));
                _logger.LogInformation("[VL-{CallId}] Using API key authentication", _callConnectionId);
            }
            else
            {
                client = new VoiceLiveClient(endpoint, new DefaultAzureCredential());
                _logger.LogInformation("[VL-{CallId}] Using DefaultAzureCredential (Managed Identity)", _callConnectionId);
            }

            _logger.LogInformation("[VL-{CallId}] Starting session with model '{Model}'...", _callConnectionId, _model);
            var session = await client.StartSessionAsync(_model);

            _logger.LogInformation("[VL-{CallId}] Configuring session with voice '{Voice}'",
                _callConnectionId, _selectedVoice);

            // Configure session options — use the full Azure Speech voice name as-is
            // e.g. "en-US-Ava:DragonHDLatestNeural" or "en-US-Grant:MAI-Voice-1"
            var options = new VoiceLiveSessionOptions
            {
                Model = _model,
                Instructions = _prompt,
                Voice = new AzureStandardVoice(_selectedVoice),
                InputAudioFormat = InputAudioFormat.Pcm16,
                OutputAudioFormat = OutputAudioFormat.Pcm16,
                InputAudioNoiseReduction = new AudioNoiseReduction(AudioNoiseReductionType.AzureDeepNoiseSuppression),
                InputAudioEchoCancellation = new AudioEchoCancellation(),
                TurnDetection = new AzureSemanticVadTurnDetection
                {
                    Threshold = 0.75f,
                    SpeechDuration = TimeSpan.FromMilliseconds(250),
                    InterruptResponse = false,
                    RemoveFillerWords = true
                },
                InputAudioTranscription = new AudioInputTranscriptionOptions(AudioInputTranscriptionOptionsModel.Whisper1)
            };

            // Modalities MUST be set explicitly for audio output (SDK defaults to text-only)
            options.Modalities.Clear();
            options.Modalities.Add(InteractionModality.Text);
            options.Modalities.Add(InteractionModality.Audio);

            await session.ConfigureSessionAsync(options);
            _logger.LogInformation("[VL-{CallId}] Session configured (voice={Voice}, format=PCM16, echo cancel + noise reduction + semantic VAD)",
                _callConnectionId, _selectedVoice);

            return session;
        }

        public void StartConversation()
        {
            _logger.LogInformation("[VL-{CallId}] StartConversation called, sessionReady={Ready}", _callConnectionId, _sessionReady);
            if (!_sessionReady || m_session == null)
            {
                _logger.LogError("[VL-{CallId}] Cannot start conversation - session not ready", _callConnectionId);
                return;
            }

            _ = Task.Run(async () =>
            {
                try
                {
                    await GetVoiceLiveStreamResponseAsync();
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "[VL-{CallId}] Unhandled exception in VoiceLive response task", _callConnectionId);
                }
            });
        }

        private async Task GetVoiceLiveStreamResponseAsync()
        {
          bool shouldRetry = true;
          while (shouldRetry)
          {
            shouldRetry = false;
            try
            {
                if (m_session == null) return;

                _logger.LogInformation("[VL-{CallId}] Starting VoiceLive initial response...", _callConnectionId);
                await m_session.StartResponseAsync();
                _logger.LogInformation("[VL-{CallId}] Listening for VoiceLive updates...", _callConnectionId);
                int audioChunkCount = 0;

                await foreach (SessionUpdate update in m_session.GetUpdatesAsync(m_cts.Token))
                {
                    if (update is SessionUpdateSessionCreated sessionCreated)
                    {
                        _logger.LogInformation("[VL-{CallId}] Session created", _callConnectionId);
                    }

                    // User barge-in: speech started
                    if (update is SessionUpdateInputAudioBufferSpeechStarted speechStarted)
                    {
                        _logger.LogInformation("[VL-{CallId}] Voice activity detection started", _callConnectionId);
                        var jsonString = OutStreamingData.GetStopAudioForOutbound();
                        await m_mediaStreaming.SendMessageAsync(jsonString);
                    }

                    // AI audio output delta
                    if (update is SessionUpdateResponseAudioDelta audioDelta)
                    {
                        if (audioDelta.Delta != null)
                        {
                            audioChunkCount++;
                            if (audioChunkCount <= 3 || audioChunkCount % 50 == 0)
                            {
                                _logger.LogInformation("[VL-{CallId}] Sending audio chunk #{ChunkNum} to ACS",
                                    _callConnectionId, audioChunkCount);
                            }
                            var audioBytes = audioDelta.Delta.ToArray();
                            var jsonString = OutStreamingData.GetAudioDataForOutbound(audioBytes);
                            await m_mediaStreaming.SendMessageAsync(jsonString);
                        }
                    }

                    // AI transcript delta (incremental)
                    if (update is SessionUpdateResponseAudioTranscriptDelta transcriptDelta)
                    {
                        // Deltas are incremental; we accumulate and emit on "done"
                    }

                    // AI transcript done (complete)
                    if (update is SessionUpdateResponseAudioTranscriptDone transcriptDone)
                    {
                        _logger.LogInformation("[VL-{CallId}] AI transcript: {Transcript}", _callConnectionId, transcriptDone.Transcript);
                        var aiEntry = new TranscriptEntry
                        {
                            CallConnectionId = _callConnectionId,
                            Speaker = SpeakerType.AI,
                            Text = transcriptDone.Transcript,
                            Timestamp = DateTimeOffset.UtcNow
                        };

                        if (_activeCalls != null && _activeCalls.TryGetValue(_callConnectionId, out var activeCall))
                        {
                            lock (activeCall.TranscriptEntries)
                            {
                                activeCall.TranscriptEntries.Add(aiEntry);
                            }
                        }

                        await _hubContext.Clients.Group(_callConnectionId)
                            .SendAsync("TranscriptUpdate", aiEntry);

                        FireAndForgetSentiment(aiEntry);
                        FireAndForgetEmotion(aiEntry);
                    }

                    // User transcript completed
                    if (update is SessionUpdateConversationItemInputAudioTranscriptionCompleted userTranscript)
                    {
                        _logger.LogInformation("[VL-{CallId}] User audio transcript: {Transcript}", _callConnectionId, userTranscript.Transcript);
                        var recipientEntry = new TranscriptEntry
                        {
                            CallConnectionId = _callConnectionId,
                            Speaker = SpeakerType.Recipient,
                            Text = userTranscript.Transcript,
                            Timestamp = DateTimeOffset.UtcNow
                        };

                        if (_activeCalls != null && _activeCalls.TryGetValue(_callConnectionId, out var activeCall2))
                        {
                            lock (activeCall2.TranscriptEntries)
                            {
                                activeCall2.TranscriptEntries.Add(recipientEntry);
                            }
                        }

                        await _hubContext.Clients.Group(_callConnectionId)
                            .SendAsync("TranscriptUpdate", recipientEntry);

                        FireAndForgetSentiment(recipientEntry);
                        FireAndForgetEmotion(recipientEntry);
                    }

                    // Response done
                    if (update is SessionUpdateResponseDone responseDone)
                    {
                        _logger.LogInformation("[VL-{CallId}] Response turn finished. Total audio chunks: {ChunkCount}",
                            _callConnectionId, audioChunkCount);
                    }

                    // Error handling
                    if (update is SessionUpdateError errorUpdate)
                    {
                        var errorCode = errorUpdate.Error?.Code;
                        var errorMessage = errorUpdate.Error?.Message;
                        _logger.LogError("[VL-{CallId}] VoiceLive error: code={Code}, message={Message}",
                            _callConnectionId, errorCode, errorMessage);

                        // FR-024: Rate-limit / quota errors — notify operator, don't retry
                        if (IsRateLimitOrQuotaError(errorCode))
                        {
                            await _hubContext.Clients.Group(_callConnectionId)
                                .SendAsync("CallStatusChanged", new
                                {
                                    callConnectionId = _callConnectionId,
                                    status = "Error",
                                    message = $"VoiceLive service error: {errorMessage ?? "Rate limit or quota exceeded"}"
                                });
                            if (_hangUpCallback != null)
                            {
                                await _hangUpCallback(_callConnectionId);
                            }
                            break;
                        }

                        // Attempt reconnection for other errors
                        var reconnected = await AttemptReconnectionAsync();
                        if (!reconnected)
                        {
                            if (_hangUpCallback != null)
                            {
                                await _hangUpCallback(_callConnectionId);
                            }
                            break;
                        }
                        // If reconnected, the loop will continue with the new session
                        audioChunkCount = 0;
                        continue;
                    }
                }

                _logger.LogInformation("[VL-{CallId}] VoiceLive response loop ended. Total audio chunks: {ChunkCount}",
                    _callConnectionId, audioChunkCount);
            }
            catch (OperationCanceledException)
            {
                _logger.LogInformation("[VL-{CallId}] VoiceLive response loop cancelled", _callConnectionId);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[VL-{CallId}] Exception during VoiceLive streaming", _callConnectionId);

                // Attempt reconnection on unexpected exceptions
                var reconnected = await AttemptReconnectionAsync();
                if (!reconnected)
                {
                    if (_hangUpCallback != null)
                    {
                        try { await _hangUpCallback(_callConnectionId); }
                        catch (Exception cbEx) { _logger.LogWarning(cbEx, "[VL-{CallId}] Hang-up callback failed after VoiceLive error", _callConnectionId); }
                    }
                }
                // If reconnected, set shouldRetry to re-enter the while loop
                    if (reconnected) shouldRetry = true;
            }
          } // end while
        }

        /// <summary>
        /// Attempt reconnection with exponential backoff (1s, 2s, 4s — max 3 attempts).
        /// FR-018/019/020: Reconnection with status indicators and operator abort support.
        /// </summary>
        private async Task<bool> AttemptReconnectionAsync()
        {
            for (int attempt = 1; attempt <= MaxReconnectAttempts; attempt++)
            {
                // Update ActiveCall reconnect tracking
                if (_activeCalls != null && _activeCalls.TryGetValue(_callConnectionId, out var activeCall))
                {
                    activeCall.Status = CallStatus.Reconnecting;
                    activeCall.ReconnectAttempts = attempt;

                    // Check if operator has aborted (CancellationToken cancelled = hang up)
                    if (activeCall.CancellationTokenSource.IsCancellationRequested)
                    {
                        _logger.LogInformation("[VL-{CallId}] Operator aborted during reconnection", _callConnectionId);
                        return false;
                    }
                }

                // FR-019: Emit Reconnecting status via SignalR
                _logger.LogWarning("[VL-{CallId}] Reconnection attempt {Attempt}/{Max}...", _callConnectionId, attempt, MaxReconnectAttempts);
                await _hubContext.Clients.Group(_callConnectionId)
                    .SendAsync("CallStatusChanged", new
                    {
                        callConnectionId = _callConnectionId,
                        status = "Reconnecting",
                        message = $"Reconnecting\u2026 attempt {attempt}/{MaxReconnectAttempts}"
                    });

                // Exponential backoff delay
                var delay = ReconnectDelaysMs[attempt - 1];
                await Task.Delay(delay);

                try
                {
                    // Dispose old session
                    if (m_session != null)
                    {
                        try { m_session.Dispose(); } catch { }
                    }

                    // Create new session with same options
                    m_session = await CreateSessionAsync();
                    _sessionReady = true;
                    await m_session.StartResponseAsync();

                    _logger.LogInformation("[VL-{CallId}] Reconnection successful on attempt {Attempt}", _callConnectionId, attempt);

                    // Update status back to Connected
                    if (_activeCalls != null && _activeCalls.TryGetValue(_callConnectionId, out var callAfterReconnect))
                    {
                        callAfterReconnect.Status = CallStatus.Connected;
                    }

                    await _hubContext.Clients.Group(_callConnectionId)
                        .SendAsync("CallStatusChanged", new
                        {
                            callConnectionId = _callConnectionId,
                            status = "Connected",
                            message = "Reconnected successfully"
                        });

                    return true;
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "[VL-{CallId}] Reconnection attempt {Attempt} failed", _callConnectionId, attempt);
                }
            }

            // All attempts exhausted
            _logger.LogError("[VL-{CallId}] All {Max} reconnection attempts failed", _callConnectionId, MaxReconnectAttempts);

            // FR-019: Emit ReconnectFailed status
            await _hubContext.Clients.Group(_callConnectionId)
                .SendAsync("CallStatusChanged", new
                {
                    callConnectionId = _callConnectionId,
                    status = "ReconnectFailed",
                    message = "All reconnection attempts failed"
                });

            if (_activeCalls != null && _activeCalls.TryGetValue(_callConnectionId, out var failedCall))
            {
                failedCall.Status = CallStatus.Disconnected;
            }

            return false;
        }

        private static bool IsRateLimitOrQuotaError(string? errorCode)
        {
            if (string.IsNullOrEmpty(errorCode)) return false;
            return errorCode.Contains("rate_limit", StringComparison.OrdinalIgnoreCase)
                || errorCode.Contains("quota", StringComparison.OrdinalIgnoreCase)
                || errorCode.Contains("throttl", StringComparison.OrdinalIgnoreCase);
        }

        public async Task SendAudioToExternalAI(MemoryStream memoryStream)
        {
            try
            {
                if (m_session != null)
                {
                    memoryStream.Position = 0;
                    await m_session.SendInputAudioAsync(memoryStream);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[VL-{CallId}] Failed to send audio to VoiceLive ({ByteCount} bytes)",
                    _callConnectionId, memoryStream.Length);
            }
        }

        private void FireAndForgetSentiment(TranscriptEntry entry)
        {
            if (_sentimentService == null) return;

            _ = Task.Run(async () =>
            {
                try
                {
                    string textToAnalyze = entry.Text;
                    if (_activeCalls != null && _activeCalls.TryGetValue(_callConnectionId, out var call))
                    {
                        var cutoff = entry.Timestamp.AddSeconds(-5);
                        List<TranscriptEntry> snapshot;
                        lock (call.TranscriptEntries)
                        {
                            snapshot = call.TranscriptEntries.ToList();
                        }
                        var recentEntries = snapshot
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
                    _logger.LogWarning(ex, "[VL-{CallId}] Sentiment analysis failed for entry", _callConnectionId);
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
                    string textToAnalyze = entry.Text;
                    if (_activeCalls != null && _activeCalls.TryGetValue(_callConnectionId, out var call))
                    {
                        var cutoff = entry.Timestamp.AddSeconds(-5);
                        List<TranscriptEntry> snapshot;
                        lock (call.TranscriptEntries)
                        {
                            snapshot = call.TranscriptEntries.ToList();
                        }
                        var recentEntries = snapshot
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
                    _logger.LogWarning(ex, "[VL-{CallId}] Emotion analysis failed for entry", _callConnectionId);
                }
            });
        }

        public void Close()
        {
            m_cts.Cancel();
            m_cts.Dispose();
            if (m_session != null)
            {
                try { m_session.Dispose(); } catch { }
            }
        }
    }
}
