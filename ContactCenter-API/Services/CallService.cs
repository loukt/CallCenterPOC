using Azure;
using Azure.Communication;
using Azure.Communication.CallAutomation;
using ContactCenterPOC.Hubs;
using ContactCenterPOC.Models;
using Microsoft.AspNetCore.SignalR;
using System.Collections.Concurrent;
using System.Net.WebSockets;
using System.Text.RegularExpressions;


namespace ContactCenterPOC.Services
{
    public class CallService
    {
        private readonly CallAutomationClient _callAutomationClient;
        public CallAutomationClient CallAutomationClient { get { return _callAutomationClient; } }
        private readonly PhoneNumberIdentifier _callerPhoneNumber;
        private readonly string _callbackUri;
        private readonly ILogger<CallService> _logger;
        private readonly IConfiguration _configuration;
        private readonly IHubContext<TranscriptHub> _hubContext;
        private readonly CampaignService _campaignService;
        private readonly CallHistoryService _callHistoryService;
        private readonly SentimentAnalysisService? _sentimentService;
        private readonly EmotionAnalysisService? _emotionService;
        private readonly OperatorStyleAnalysisService? _operatorStyleService;
        private readonly CallSummaryService? _callSummaryService;
        private readonly SettingsService? _settingsService;
        private readonly VoiceLiveConfig _voiceLiveConfig;

        // Thread-safe dictionaries for concurrent call handling
        private readonly ConcurrentDictionary<string, ActiveCall> _activeCalls = new();
        private readonly ConcurrentDictionary<string, AcsMediaStreamingHandler> _mediaHandlers = new();

        public ConcurrentDictionary<string, ActiveCall> ActiveCalls => _activeCalls;

        public CallService(IConfiguration configuration, ILogger<CallService> logger, IHubContext<TranscriptHub> hubContext, CampaignService campaignService, CallHistoryService callHistoryService, VoiceLiveConfig voiceLiveConfig, SentimentAnalysisService? sentimentService = null, EmotionAnalysisService? emotionService = null, OperatorStyleAnalysisService? operatorStyleService = null, CallSummaryService? callSummaryService = null, SettingsService? settingsService = null)
        {
            _logger = logger;
            _configuration = configuration;
            _hubContext = hubContext;
            _campaignService = campaignService;
            _callHistoryService = callHistoryService;
            _sentimentService = sentimentService;
            _emotionService = emotionService;
            _operatorStyleService = operatorStyleService;
            _callSummaryService = callSummaryService;
            _settingsService = settingsService;
            _voiceLiveConfig = voiceLiveConfig;
            var connectionString = configuration["AzureCommunicationServices:ConnectionString"];
            _callbackUri = configuration["CallbackUrl"] ?? throw new InvalidOperationException("CallbackUrl not configured");
            _callAutomationClient = new CallAutomationClient(connectionString);
            _callerPhoneNumber = new PhoneNumberIdentifier(configuration["AzureCommunicationServices:PhoneNumber"]);
        }

        private const int MaxConcurrentCalls = 5;
        private static readonly Regex E164Regex = new(@"^\+[1-9]\d{1,14}$");

        public async Task<List<(string callConnectionId, string phoneNumber)>> InitiateCall(string[] phoneNumbers, string? callContextPrompt, string? campaignId, string[]? contactNames, HttpContext httpContext)
        {
            // Validate phone numbers
            if (phoneNumbers == null || phoneNumbers.Length == 0)
                throw new ArgumentException("At least one phone number is required.");
            if (phoneNumbers.Length > 2)
                throw new ArgumentException("Maximum of 2 phone numbers allowed.");
            foreach (var pn in phoneNumbers)
            {
                if (!E164Regex.IsMatch(pn))
                    throw new ArgumentException($"Phone number '{pn}' is not in E.164 format.");
            }

            // Check concurrent limit
            if (_activeCalls.Count + phoneNumbers.Length > MaxConcurrentCalls)
            {
                var available = MaxConcurrentCalls - _activeCalls.Count;
                throw new InvalidOperationException($"Maximum of {MaxConcurrentCalls} concurrent calls allowed. {available} slot(s) available.");
            }

            // Resolve prompt from campaign or direct prompt
            string effectivePrompt;
            string? resolvedCampaignId = null;
            string? resolvedCampaignTitle = null;

            if (!string.IsNullOrWhiteSpace(callContextPrompt))
            {
                // Direct prompt takes precedence
                effectivePrompt = callContextPrompt;
            }
            else if (!string.IsNullOrWhiteSpace(campaignId))
            {
                // Look up campaign
                var campaign = await _campaignService.GetByIdAsync(campaignId);
                if (campaign != null)
                {
                    effectivePrompt = campaign.AiBehaviorInstructions;
                    resolvedCampaignId = campaign.Id;
                    resolvedCampaignTitle = campaign.Title;
                }
                else
                {
                    _logger.LogWarning("Campaign {CampaignId} not found, using default prompt", campaignId);
                    effectivePrompt = _configuration["AzureOpenAI:SystemPrompt"] ?? "You are an AI assistant that helps people find information.";
                }
            }
            else
            {
                // Default prompt fallback (FR-008)
                effectivePrompt = _configuration["AzureOpenAI:SystemPrompt"] ?? "You are an AI assistant that helps people find information.";
            }

            var results = new List<(string callConnectionId, string phoneNumber)>();

            for (int i = 0; i < phoneNumbers.Length; i++)
            {
                var targetPhoneNumber = phoneNumbers[i];
                var contactName = (contactNames != null && i < contactNames.Length && !string.IsNullOrWhiteSpace(contactNames[i]))
                    ? contactNames[i].Trim()
                    : null;

                // Prepend contact name instruction to prompt if provided
                var callPrompt = effectivePrompt;
                if (contactName != null)
                {
                    callPrompt = $"IMPORTANT: The person you are calling is named {contactName}. You MUST greet them by name at the start of the conversation, for example: 'Hello {contactName}'. " + callPrompt;
                    _logger.LogInformation("Call to {PhoneNumber} will greet contact as '{ContactName}'", targetPhoneNumber, contactName);
                }

                var targetPhone = new PhoneNumberIdentifier(targetPhoneNumber);
                var callInvite = new CallInvite(targetPhone, _callerPhoneNumber);

                var callbackUri = new Uri(_callbackUri);
                var callOptions = new CreateCallOptions(callInvite, callbackUri);

                var wssUri = new Uri(_callbackUri.Replace("https", "wss") + "/ws?targetNumber=" + targetPhoneNumber);
                var mediaStreamingOptions = new MediaStreamingOptions(
                    wssUri,
                    MediaStreamingContent.Audio,
                    MediaStreamingAudioChannel.Mixed,
                    startMediaStreaming: true)
                {
                    EnableBidirectional = true,
                    AudioFormat = AudioFormat.Pcm24KMono
                };
                callOptions.MediaStreamingOptions = mediaStreamingOptions;

                var createCallResult = await _callAutomationClient.CreateCallAsync(callOptions);
                var callConnectionId = createCallResult.Value.CallConnection.CallConnectionId;

                var activeCall = new ActiveCall
                {
                    CallConnectionId = callConnectionId,
                    TargetPhoneNumber = targetPhoneNumber,
                    CampaignId = resolvedCampaignId,
                    CampaignTitle = resolvedCampaignTitle,
                    ContactName = contactName,
                    Prompt = callPrompt,
                    Status = CallStatus.Initiating,
                    StartedAt = DateTimeOffset.UtcNow
                };

                // Set up auto-terminate timeout from settings (default 2 minutes)
                var maxCallMinutes = 2.0;
                // Freeze VoiceApiMode and VoiceLiveModel from current settings (FR-014)
                var frozenVoiceApiMode = "ChatGPT";
                var frozenVoiceLiveModel = "gpt-4o-realtime-preview";
                var frozenVoiceLiveVoice = "en-US-Ava:DragonHDLatestNeural";
                var frozenSelectedVoice = "alloy";
                if (_settingsService != null)
                {
                    try
                    {
                        var settings = await _settingsService.GetSettingsAsync();
                        maxCallMinutes = settings.MaxCallTimeMinutes;
                        frozenVoiceApiMode = settings.VoiceApiMode;
                        frozenVoiceLiveModel = settings.VoiceLiveModel;
                        frozenVoiceLiveVoice = settings.SelectedVoiceLiveVoice;
                        frozenSelectedVoice = settings.SelectedVoice;
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Failed to load settings for max call time, using default {Default}min", maxCallMinutes);
                    }
                }

                // Freeze engine settings on the ActiveCall for the call's duration
                activeCall.VoiceApiMode = frozenVoiceApiMode;
                activeCall.VoiceLiveModel = frozenVoiceApiMode == "VoiceLive" ? frozenVoiceLiveModel : null;
                activeCall.VoiceLiveVoice = frozenVoiceApiMode == "VoiceLive" ? frozenVoiceLiveVoice : null;

                // FR-015: Log engine type, voice, and model when call starts
                _logger.LogInformation("Call {CallConnectionId} initiated: engine={Engine}, voice={Voice}, model={Model}",
                    callConnectionId, frozenVoiceApiMode,
                    frozenVoiceApiMode == "VoiceLive" ? frozenVoiceLiveVoice : frozenSelectedVoice,
                    frozenVoiceApiMode == "VoiceLive" ? frozenVoiceLiveModel : "N/A");
                activeCall.CancellationTokenSource.CancelAfter(TimeSpan.FromMinutes(maxCallMinutes));
                activeCall.CancellationTokenSource.Token.Register(async () =>
                {
                    _logger.LogInformation("Call {CallConnectionId} auto-terminated after {MaxMinutes} minutes", callConnectionId, maxCallMinutes);
                    await HangUpCall(callConnectionId);
                });

                _activeCalls[callConnectionId] = activeCall;
                results.Add((callConnectionId, targetPhoneNumber));

                // Persist an initial record immediately so the call appears in history even if
                // the app restarts or callbacks are handled by a different instance.
                await _callHistoryService.SaveCallRecordAsync(new CallRecord
                {
                    CallConnectionId = callConnectionId,
                    PhoneNumber = targetPhoneNumber,
                    CampaignId = resolvedCampaignId,
                    CampaignTitle = resolvedCampaignTitle,
                    ContactName = contactName,
                    Prompt = callPrompt,
                    RecordingId = null,
                    Duration = TimeSpan.Zero,
                    OverallSentiment = SentimentLabel.Neutral,
                    SentimentBreakdown = new SentimentBreakdown(),
                    TalkTimeRatio = new TalkTimeRatio(),
                    TranscriptEntries = new List<TranscriptEntry>(),
                    StartedAt = activeCall.StartedAt,
                    EndedAt = activeCall.StartedAt
                });
            }

            return results;
        }

        public async Task StartCallInteraction(HttpContext httpContext, string targetNumber)
        {
            if (httpContext.WebSockets.IsWebSocketRequest)
            {
                var ws = await httpContext.WebSockets.AcceptWebSocketAsync();
                _logger.LogInformation("WebSocket connected for target {TargetNumber}", targetNumber);

                if (ws.State == WebSocketState.Open)
                {
                    // Find the callConnectionId by target number
                    var activeCall = _activeCalls.Values.FirstOrDefault(c => c.TargetPhoneNumber == targetNumber);
                    if (activeCall == null)
                    {
                        _logger.LogWarning("No active call found for target number {TargetNumber}", targetNumber);
                        return;
                    }

                    var callConnectionId = activeCall.CallConnectionId;

                    // FR-010/FR-013: Check VoiceLive configuration before proceeding
                    if (activeCall.VoiceApiMode == "VoiceLive" && !_voiceLiveConfig.IsConfigured)
                    {
                        _logger.LogWarning("VoiceLive call attempted but endpoint not configured for {CallConnectionId}", callConnectionId);
                        await _hubContext.Clients.Group(callConnectionId)
                            .SendAsync("CallStatusChanged", new
                            {
                                callConnectionId = callConnectionId,
                                status = "Failed",
                                message = "VoiceLive is not configured. Please contact your administrator."
                            });
                        await HangUpCall(callConnectionId);
                        return;
                    }

                    // Read voice and VoiceLive settings from frozen ActiveCall state
                    var selectedVoice = "alloy";
                    var voiceApiMode = activeCall.VoiceApiMode;
                    var voiceLiveModel = activeCall.VoiceLiveModel ?? "gpt-4o-realtime-preview";
                    var voiceLiveVoice = activeCall.VoiceLiveVoice ?? "en-US-Ava:DragonHDLatestNeural";
                    if (_settingsService != null)
                    {
                        try
                        {
                            var settings = await _settingsService.GetSettingsAsync();
                            selectedVoice = settings.SelectedVoice ?? "alloy";
                        }
                        catch (Exception ex)
                        {
                            _logger.LogWarning(ex, "Failed to read voice setting, using defaults");
                        }
                    }

                    var handler = new AcsMediaStreamingHandler(
                        ws, _configuration, _logger, _hubContext,
                        callConnectionId, async (id) => await HangUpCall(id),
                        _sentimentService, _activeCalls, _emotionService, selectedVoice,
                        voiceApiMode, voiceLiveModel, voiceLiveVoice, _voiceLiveConfig);
                    _mediaHandlers[callConnectionId] = handler;
                    await handler.ProcessWebSocketAsync(activeCall.Prompt);
                }
            }
            else
            {
                _logger.LogWarning("Non-WebSocket request received on WS endpoint");
            }
        }

        public async Task HangUpCall(string callConnectionId)
        {
            try
            {
                var callConnection = _callAutomationClient.GetCallConnection(callConnectionId);
                await callConnection.HangUpAsync(forEveryone: true);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error hanging up call {CallConnectionId}", callConnectionId);
            }
            finally
            {
                await CleanupCall(callConnectionId);
            }
        }

        public async Task CleanupCall(string callConnectionId)
        {
            _logger.LogInformation("Cleaning up call {CallConnectionId}", callConnectionId);

            if (_activeCalls.TryRemove(callConnectionId, out var activeCall))
            {
                await PersistHistoryIfNeededAsync(activeCall);

                // Stop recording if active
                if (!string.IsNullOrEmpty(activeCall.RecordingId))
                {
                    await StopRecordingAsync(activeCall.RecordingId);
                }

                // Dispose CTS
                activeCall.CancellationTokenSource.Dispose();
            }

            // Remove media handler
            _mediaHandlers.TryRemove(callConnectionId, out _);
        }

        private async Task PersistHistoryIfNeededAsync(ActiveCall activeCall)
        {
            try
            {
                // Only persist completed calls (matches original behavior which saved on CallDisconnected)
                if (activeCall.Status != CallStatus.Connected && activeCall.Status != CallStatus.Disconnected)
                {
                    return;
                }

                var endedAt = DateTimeOffset.UtcNow;
                var duration = endedAt - activeCall.StartedAt;
                var entries = activeCall.TranscriptEntries;

                // Overall sentiment = majority label among entries
                var overallSentiment = SentimentLabel.Neutral;
                if (entries.Count > 0)
                {
                    overallSentiment = entries
                        .GroupBy(e => e.Sentiment.Label)
                        .OrderByDescending(g => g.Count())
                        .First().Key;
                }

                // Sentiment breakdown percentages
                var breakdown = new SentimentBreakdown();
                if (entries.Count > 0)
                {
                    float total = entries.Count;
                    breakdown.PositivePercent = entries.Count(e => e.Sentiment.Label == SentimentLabel.Positive) / total * 100f;
                    breakdown.NeutralPercent = entries.Count(e => e.Sentiment.Label == SentimentLabel.Neutral) / total * 100f;
                    breakdown.NegativePercent = entries.Count(e => e.Sentiment.Label == SentimentLabel.Negative) / total * 100f;
                }

                // Talk time ratio (count of entries per speaker as proxy)
                var talkTime = new TalkTimeRatio();
                if (entries.Count > 0)
                {
                    float total = entries.Count;
                    var aiCount = entries.Count(e => e.Speaker == SpeakerType.AI);
                    var recipientCount = entries.Count(e => e.Speaker == SpeakerType.Recipient);
                    talkTime.AiPercent = aiCount / total * 100f;
                    talkTime.RecipientPercent = recipientCount / total * 100f;
                }

                // Compute operator style traits if service is available
                OperatorStyleTraits? operatorTraits = null;
                if (_operatorStyleService != null)
                {
                    var operatorEntries = entries.Where(e => e.Speaker == SpeakerType.AI).ToList();
                    if (operatorEntries.Count > 0)
                    {
                        try
                        {
                            operatorTraits = await _operatorStyleService.AnalyzeAsync(operatorEntries);
                        }
                        catch (Exception ex)
                        {
                            _logger.LogWarning(ex, "Failed to compute operator style traits for {CallConnectionId}", activeCall.CallConnectionId);
                        }
                    }
                }

                var callRecord = new CallRecord
                {
                    CallConnectionId = activeCall.CallConnectionId,
                    ServerCallId = activeCall.ServerCallId,
                    PhoneNumber = activeCall.TargetPhoneNumber,
                    CampaignId = activeCall.CampaignId,
                    CampaignTitle = activeCall.CampaignTitle,
                    ContactName = activeCall.ContactName,
                    Prompt = activeCall.Prompt,
                    RecordingId = activeCall.RecordingId,
                    Duration = duration,
                    OverallSentiment = overallSentiment,
                    SentimentBreakdown = breakdown,
                    TalkTimeRatio = talkTime,
                    OperatorStyleTraits = operatorTraits,
                    TranscriptEntries = entries,
                    StartedAt = activeCall.StartedAt,
                    EndedAt = endedAt,
                    VoiceApiMode = activeCall.VoiceApiMode,
                    VoiceLiveModel = activeCall.VoiceLiveModel,
                    VoiceLiveVoice = activeCall.VoiceLiveVoice
                };

                await _callHistoryService.SaveCallRecordAsync(callRecord);

                // Fire-and-forget: generate post-call summary in the background
                if (_callSummaryService != null && entries.Count > 0)
                {
                    _ = Task.Run(async () =>
                    {
                        try
                        {
                            var summary = await _callSummaryService.GenerateSummaryAsync(entries);
                            if (!string.IsNullOrWhiteSpace(summary))
                            {
                                callRecord.CallSummary = summary;
                                callRecord.SummarizedAt = DateTimeOffset.UtcNow;
                                await _callHistoryService.SaveCallRecordAsync(callRecord);
                                _logger.LogInformation("Post-call summary generated for {CallConnectionId}", activeCall.CallConnectionId);
                            }
                        }
                        catch (Exception summaryEx)
                        {
                            _logger.LogWarning(summaryEx, "Failed to generate post-call summary for {CallConnectionId}", activeCall.CallConnectionId);
                        }
                    });
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to persist call history during cleanup for {CallConnectionId}", activeCall.CallConnectionId);
            }
        }

        public async Task StartRecordingAsync(string serverCallId, string callConnectionId)
        {
            try
            {
                StartRecordingOptions recordingOptions = new StartRecordingOptions(new ServerCallLocator(serverCallId))
                {
                    RecordingChannel = RecordingChannel.Mixed,
                    RecordingContent = RecordingContent.Audio,
                    RecordingFormat = RecordingFormat.Mp3,
                    RecordingStorage = RecordingStorage.CreateAzureBlobContainerRecordingStorage(new Uri(_configuration["BlobContainer"] ?? ""))
                };

                var startRecordingResponse = await _callAutomationClient.GetCallRecording().StartAsync(recordingOptions).ConfigureAwait(false);

                if (_activeCalls.TryGetValue(callConnectionId, out var activeCall))
                {
                    activeCall.RecordingId = startRecordingResponse.Value.RecordingId;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to start recording for call {CallConnectionId}", callConnectionId);
            }
        }

        public async Task StopRecordingAsync(string recordingId)
        {
            try
            {
                await _callAutomationClient.GetCallRecording().StopAsync(recordingId);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to stop recording {RecordingId}", recordingId);
            }
        }
    }
}
