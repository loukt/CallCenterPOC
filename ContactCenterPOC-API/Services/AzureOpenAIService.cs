using Azure.AI.OpenAI;
using Azure.Communication.CallAutomation;
using ContactCenterPOC.Models;
using OpenAI.RealtimeConversation;
using System.ClientModel;
using System.Net.WebSockets;


namespace ContactCenterPOC.Services
{
    public class AzureOpenAIService
    {
        private CancellationTokenSource m_cts;
        private RealtimeConversationSession m_aiSession;
        private AcsMediaStreamingHandler m_mediaStreaming;
        private MemoryStream m_memoryStream;
        private ILogger<CallService> _logger;
        private string m_answerPromptSystemTemplate = "You are an AI assistant that helps people find information.";
        private HttpContext _httpContext;
        private WebSocket _webSocket;

        public AzureOpenAIService(AcsMediaStreamingHandler mediaStreaming, IConfiguration configuration, ILogger<CallService> logger)
        {
            m_mediaStreaming = mediaStreaming;
            m_cts = new CancellationTokenSource();
            m_aiSession = CreateAISessionAsync(configuration).GetAwaiter().GetResult();
            m_memoryStream = new MemoryStream();
            _logger = logger;
        }

        

        private async Task<RealtimeConversationSession> CreateAISessionAsync(IConfiguration configuration)
        {

            var openAiKey = configuration["AzureOpenAI:Key"];
            ArgumentNullException.ThrowIfNullOrEmpty(openAiKey);

            var openAiUri = configuration["AzureOpenAI:EndpointUri"];
            ArgumentNullException.ThrowIfNullOrEmpty(openAiUri);

            var openAiModelName = configuration["AzureOpenAI:DeploymentName"];// configuration.GetValue<string>("AzureOpenAIDeploymentModelName");
            ArgumentNullException.ThrowIfNullOrEmpty(openAiModelName);
            var systemPrompt = configuration["AzureOpenAI:SystemPrompt"];// configuration.GetValue<string>("SystemPrompt") ?? m_answerPromptSystemTemplate;
            ArgumentNullException.ThrowIfNullOrEmpty(systemPrompt);

            var aiClient = new AzureOpenAIClient(new Uri(openAiUri), new ApiKeyCredential(openAiKey));
            var RealtimeCovnClient = aiClient.GetRealtimeConversationClient(openAiModelName);
            var session = await RealtimeCovnClient.StartConversationSessionAsync();

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
            return session;
        }

        // Loop and wait for the AI response
        private async Task GetOpenAiStreamResponseAsync()
        {
            try
            {
                await m_aiSession.StartResponseAsync();
                await foreach (ConversationUpdate update in m_aiSession.ReceiveUpdatesAsync(m_cts.Token))
                {
                    if (update is ConversationSessionStartedUpdate sessionStartedUpdate)
                    {
                        _logger.LogInformation($"<<< Session started. ID: {sessionStartedUpdate.SessionId}");
                    }

                    if (update is ConversationInputSpeechStartedUpdate speechStartedUpdate)
                    {
                        _logger.LogInformation($"  -- Voice activity detection started at {speechStartedUpdate.AudioStartTime} ms");
                        // Barge-in, send stop audio
                        var jsonString = OutStreamingData.GetStopAudioForOutbound();
                        await m_mediaStreaming.SendMessageAsync(jsonString);
                    }

                    if (update is ConversationInputSpeechFinishedUpdate speechFinishedUpdate)
                    {
                        _logger.LogInformation(  $"  -- Voice activity detection ended at {speechFinishedUpdate.AudioEndTime} ms");
                    }

                    if (update is ConversationItemStreamingStartedUpdate itemStartedUpdate)
                    {
                        _logger.LogInformation($"  -- Begin streaming of new item");
                    }

                    // Audio transcript  updates contain the incremental text matching the generated
                    // output audio.
                    if (update is ConversationItemStreamingAudioTranscriptionFinishedUpdate outputTranscriptDeltaUpdate)
                    {
                        _logger.LogInformation(outputTranscriptDeltaUpdate.Transcript);
                    }

                    // Audio delta updates contain the incremental binary audio data of the generated output
                    // audio, matching the output audio format configured for the session.
                    if (update is ConversationItemStreamingPartDeltaUpdate deltaUpdate)
                    {
                        if (deltaUpdate.AudioBytes != null)
                        {
                            var jsonString = OutStreamingData.GetAudioDataForOutbound(deltaUpdate.AudioBytes.ToArray());
                            await m_mediaStreaming.SendMessageAsync(jsonString);
                        }
                    }

                    if (update is ConversationItemStreamingTextFinishedUpdate itemFinishedUpdate)
                    {
                        _logger.LogInformation($"  -- Item streaming finished, response_id={itemFinishedUpdate.ResponseId}");
                    }

                    if (update is ConversationInputTranscriptionFinishedUpdate transcriptionCompletedUpdate)
                    {
                        _logger.LogInformation($"  -- User audio transcript: {transcriptionCompletedUpdate.Transcript}");
                        
                    }

                    if (update is ConversationResponseFinishedUpdate turnFinishedUpdate)
                    {
                        _logger.LogInformation($"  -- Model turn generation finished. Status: {turnFinishedUpdate.Status}");
                    }

                    if (update is ConversationErrorUpdate errorUpdate)
                    {
                        _logger.LogInformation($"la on est en methode ERROR: {errorUpdate.Message}");
                        break;
                    }
                }
            }
            catch (OperationCanceledException e)
            {
                _logger.LogInformation($"{nameof(OperationCanceledException)} thrown with message: {e.Message}");
            }
            catch (Exception ex)
            {
                _logger.LogInformation($"Exception during ai streaming -> {ex}");
            }
        }

        public void StartConversation()
        {
            _ = Task.Run(async () => await GetOpenAiStreamResponseAsync());
        }

        public async Task SendAudioToExternalAI(MemoryStream memoryStream)
        {
            await m_aiSession.SendInputAudioAsync(memoryStream);
        }

        public void Close()
        {
            m_cts.Cancel();
            m_cts.Dispose();
            m_aiSession.Dispose();
        }
    }
}