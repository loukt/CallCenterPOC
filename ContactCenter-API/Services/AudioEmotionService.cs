using Azure.AI.OpenAI;
using ContactCenterPOC.Hubs;
using ContactCenterPOC.Models;
using Microsoft.AspNetCore.SignalR;
using OpenAI.Chat;
using System.Collections.Concurrent;

#pragma warning disable OPENAI001

namespace ContactCenterPOC.Services
{
    public class AudioEmotionService
    {
        private readonly IConfiguration _configuration;
        private readonly SentimentAnalysisService? _sentimentService;
        private readonly EmotionAnalysisService? _emotionService;
        private readonly CallHistoryService _callHistoryService;
        private readonly IHubContext<TranscriptHub> _hubContext;
        private readonly ILogger<AudioEmotionService> _logger;

        private readonly ConcurrentDictionary<string, AudioAnalysisSession> _activeSessions = new();
        private readonly string? _audioModelDeployment;
        private readonly string? _openAiEndpoint;

        public AudioEmotionService(
            IConfiguration configuration,
            CallHistoryService callHistoryService,
            IHubContext<TranscriptHub> hubContext,
            ILogger<AudioEmotionService> logger,
            SentimentAnalysisService? sentimentService = null,
            EmotionAnalysisService? emotionService = null)
        {
            _configuration = configuration;
            _callHistoryService = callHistoryService;
            _hubContext = hubContext;
            _logger = logger;
            _sentimentService = sentimentService;
            _emotionService = emotionService;

            var foundryConfig = configuration.GetSection("AIFoundry").Get<FoundryAgentConfig>();
            _audioModelDeployment = foundryConfig?.AudioEmotionModel;
            _openAiEndpoint = configuration["AzureOpenAI:EndpointUri"];
        }

        public bool IsConfigured => !string.IsNullOrEmpty(_audioModelDeployment) && !string.IsNullOrEmpty(_openAiEndpoint);

        public Task StartAnalysisAsync(string callConnectionId)
        {
            if (!IsConfigured)
            {
                _logger.LogDebug("AudioEmotionService not configured — skipping for {CallConnectionId}", callConnectionId);
                return Task.CompletedTask;
            }

            var session = new AudioAnalysisSession(callConnectionId);
            _activeSessions[callConnectionId] = session;

            _logger.LogInformation("Started audio emotion analysis session for {CallConnectionId}", callConnectionId);
            return Task.CompletedTask;
        }

        public async Task BufferAudioChunkAsync(string callConnectionId, byte[] audioData, string speaker, float startTime, float endTime)
        {
            if (!_activeSessions.TryGetValue(callConnectionId, out var session))
                return;

            session.ChunkBuffer.Add(new AudioChunk
            {
                Data = audioData,
                Speaker = speaker,
                StartTime = startTime,
                EndTime = endTime
            });

            // Process when buffer reaches ~5-10 seconds of audio
            if (session.ChunkBuffer.Count >= 5)
            {
                await ProcessBufferedChunksAsync(session);
            }
        }

        public async Task<AudioEmotionResult?> StopAndAggregateAsync(string callConnectionId)
        {
            if (!_activeSessions.TryRemove(callConnectionId, out var session))
                return null;

            // Process any remaining buffered chunks
            if (session.ChunkBuffer.Count > 0)
            {
                await ProcessBufferedChunksAsync(session);
            }

            if (session.Results.Count == 0)
                return null;

            // Aggregate all chunk results
            var aggregated = AggregateResults(session.Results);

            // Save to call record
            try
            {
                var record = await _callHistoryService.GetByIdAsync(callConnectionId);
                if (record != null)
                {
                    record.AudioEmotionResult = aggregated;
                    await _callHistoryService.SaveCallRecordAsync(record);
                    _logger.LogInformation("Saved aggregated audio emotion result for {CallConnectionId}", callConnectionId);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to save audio emotion result for {CallConnectionId}", callConnectionId);
            }

            return aggregated;
        }

        private async Task ProcessBufferedChunksAsync(AudioAnalysisSession session)
        {
            var chunks = session.ChunkBuffer.ToList();
            session.ChunkBuffer.Clear();

            foreach (var chunk in chunks)
            {
                try
                {
                    var vocalEmotion = await AnalyzeAudioChunkAsync(chunk.Data);
                    var chunkResult = new AudioChunkEmotion
                    {
                        Speaker = chunk.Speaker,
                        StartTime = chunk.StartTime,
                        EndTime = chunk.EndTime,
                        VocalEmotion = vocalEmotion.emotion,
                        VocalConfidence = vocalEmotion.confidence
                    };
                    session.Results.Add(chunkResult);

                    // Get text sentiment for comparison
                    var textSentiment = "Neutral";
                    if (_sentimentService != null)
                    {
                        // Use the latest text sentiment from the session
                        textSentiment = session.LatestTextSentiment ?? "Neutral";
                    }

                    var composite = ComputeComposite(vocalEmotion.emotion, textSentiment);

                    // Push real-time update via SignalR
                    await _hubContext.Clients.Group(session.CallConnectionId)
                        .SendAsync("AudioEmotionUpdate", new
                        {
                            callConnectionId = session.CallConnectionId,
                            speaker = chunk.Speaker,
                            vocalEmotion = vocalEmotion.emotion,
                            vocalConfidence = vocalEmotion.confidence,
                            textSentiment,
                            composite = composite.label,
                            flags = composite.flags,
                            timestamp = DateTimeOffset.UtcNow
                        });
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to analyze audio chunk for {CallConnectionId}", session.CallConnectionId);
                }
            }
        }

        private async Task<(string emotion, float confidence)> AnalyzeAudioChunkAsync(byte[] audioData)
        {
            try
            {
                var client = new AzureOpenAIClient(new Uri(_openAiEndpoint!), new Azure.Identity.DefaultAzureCredential());
                var chatClient = client.GetChatClient(_audioModelDeployment!);

                var messages = new List<ChatMessage>
                {
                    new SystemChatMessage("Analyze the vocal emotion in this audio. Classify as one of: Calm, Happy, Frustrated, Angry, Sad, Anxious, Resigned, Sarcastic. Respond ONLY with JSON: {\"emotion\":\"...\",\"confidence\":0.0-1.0}"),
                    new UserChatMessage(
                        ChatMessageContentPart.CreateInputAudioPart(BinaryData.FromBytes(audioData), ChatInputAudioFormat.Wav))
                };

                var response = await chatClient.CompleteChatAsync(messages);
                var text = response.Value.Content[0].Text;
                var doc = System.Text.Json.JsonDocument.Parse(text);
                var emotion = doc.RootElement.GetProperty("emotion").GetString() ?? "Calm";
                var confidence = doc.RootElement.GetProperty("confidence").GetSingle();

                return (emotion, confidence);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Audio emotion analysis failed, defaulting to Calm");
                return ("Calm", 0f);
            }
        }

        internal static (string label, List<string> flags) ComputeComposite(string vocalEmotion, string textSentiment)
        {
            var flags = new List<string>();
            string label;

            var isPositiveText = textSentiment.Equals("Positive", StringComparison.OrdinalIgnoreCase);
            var isNegativeText = textSentiment.Equals("Negative", StringComparison.OrdinalIgnoreCase);
            var isNeutralText = textSentiment.Equals("Neutral", StringComparison.OrdinalIgnoreCase);

            var isFrustratedOrAngry = vocalEmotion is "Frustrated" or "Angry";
            var isAnxious = vocalEmotion == "Anxious";
            var isCalm = vocalEmotion == "Calm";
            var isHappy = vocalEmotion == "Happy";
            var isResigned = vocalEmotion == "Resigned";
            var isSarcastic = vocalEmotion == "Sarcastic";

            if (isPositiveText && isFrustratedOrAngry)
            {
                label = "Masked Frustration";
                flags.Add("sarcasm-detected");
            }
            else if (isPositiveText && isSarcastic)
            {
                label = "Sarcasm Detected";
                flags.Add("sarcasm-detected");
            }
            else if (isNegativeText && isCalm)
            {
                label = "Rational Complaint";
            }
            else if (isNegativeText && isFrustratedOrAngry)
            {
                label = "Escalation Risk";
                flags.Add("escalation-risk");
            }
            else if (isNeutralText && isAnxious)
            {
                label = "Hidden Anxiety";
                flags.Add("sentiment-mismatch");
            }
            else if (isPositiveText && isResigned)
            {
                label = "Resigned Acceptance";
                flags.Add("sentiment-mismatch");
            }
            else if (isPositiveText && (isCalm || isHappy))
            {
                label = "Genuinely Satisfied";
            }
            else if (isNegativeText && isResigned)
            {
                label = "Resigned Acceptance";
                flags.Add("sentiment-mismatch");
            }
            else if (isFrustratedOrAngry)
            {
                label = "Growing Frustration";
                flags.Add("escalation-risk");
            }
            else
            {
                label = "Calming Down";
            }

            return (label, flags);
        }

        private AudioEmotionResult AggregateResults(List<AudioChunkEmotion> chunks)
        {
            if (chunks.Count == 0)
                return new AudioEmotionResult { AudioEmotion = "Calm", AudioConfidence = 0f, Composite = "Calming Down" };

            // Find the dominant vocal emotion by weighted confidence
            var dominant = chunks
                .GroupBy(c => c.VocalEmotion)
                .OrderByDescending(g => g.Sum(c => c.VocalConfidence))
                .First();

            var dominantEmotion = dominant.Key;
            var avgConfidence = dominant.Average(c => c.VocalConfidence);

            var latestTextSentiment = "Neutral"; // Default
            var composite = ComputeComposite(dominantEmotion, latestTextSentiment);

            return new AudioEmotionResult
            {
                AudioEmotion = dominantEmotion,
                AudioConfidence = avgConfidence,
                TextSentiment = latestTextSentiment,
                Composite = composite.label,
                Flags = composite.flags,
                ChunkEmotions = chunks,
                AnalyzedAt = DateTimeOffset.UtcNow
            };
        }

        public void UpdateTextSentiment(string callConnectionId, string sentiment)
        {
            if (_activeSessions.TryGetValue(callConnectionId, out var session))
            {
                session.LatestTextSentiment = sentiment;
            }
        }

        private class AudioAnalysisSession
        {
            public string CallConnectionId { get; }
            public List<AudioChunk> ChunkBuffer { get; } = new();
            public List<AudioChunkEmotion> Results { get; } = new();
            public string? LatestTextSentiment { get; set; }

            public AudioAnalysisSession(string callConnectionId)
            {
                CallConnectionId = callConnectionId;
            }
        }

        private class AudioChunk
        {
            public byte[] Data { get; set; } = Array.Empty<byte>();
            public string Speaker { get; set; } = string.Empty;
            public float StartTime { get; set; }
            public float EndTime { get; set; }
        }
    }
}
