using Azure.AI.Projects;
using ContactCenterPOC.Hubs;
using ContactCenterPOC.Models;
using ContactCenterPOC.Services.Tools;
using Microsoft.AspNetCore.SignalR;
using OpenAI.Assistants;
using System.Text.Json;

#pragma warning disable OPENAI001

namespace ContactCenterPOC.Services
{
    public class OrchestrationService
    {
        private readonly AIProjectClient? _projectClient;
        private readonly FoundryAgentConfig _config;
        private readonly IntentAgentTools _intentTools;
        private readonly CaseAgentTools _caseTools;
        private readonly QualityAgentTools _qualityTools;
        private readonly KnowledgeGapAgentTools _knowledgeGapTools;
        private readonly SummaryAgentTools _summaryTools;
        private readonly PostCallReviewAgentTools _postCallReviewTools;

        // Fallback services
        private readonly IntentDiscoveryService _intentService;
        private readonly CaseManagementService _caseManagementService;
        private readonly QualityEvaluationService _qualityEvaluationService;
        private readonly KnowledgeGapService _knowledgeGapService;
        private readonly CallSummaryService _callSummaryService;
        private readonly AgentActivityService _agentActivityService;
        private readonly ILogger<OrchestrationService> _logger;

        private const int MaxToolCallIterations = 10;

        // Cached Foundry agent IDs for all registered agents
        private readonly Dictionary<string, string> _agentIds = new();
        private readonly SemaphoreSlim _agentProvisionLock = new(1, 1);

        public bool IsFoundryConfigured => _projectClient != null && !string.IsNullOrEmpty(_config.ProjectEndpoint);

        public OrchestrationService(
            AIProjectClient? projectClient,
            FoundryAgentConfig config,
            IntentAgentTools intentTools,
            CaseAgentTools caseTools,
            QualityAgentTools qualityTools,
            KnowledgeGapAgentTools knowledgeGapTools,
            SummaryAgentTools summaryTools,
            PostCallReviewAgentTools postCallReviewTools,
            IntentDiscoveryService intentService,
            CaseManagementService caseManagementService,
            QualityEvaluationService qualityEvaluationService,
            KnowledgeGapService knowledgeGapService,
            CallSummaryService callSummaryService,
            AgentActivityService agentActivityService,
            ILogger<OrchestrationService> logger)
        {
            _projectClient = projectClient;
            _config = config;
            _intentTools = intentTools;
            _caseTools = caseTools;
            _qualityTools = qualityTools;
            _knowledgeGapTools = knowledgeGapTools;
            _summaryTools = summaryTools;
            _postCallReviewTools = postCallReviewTools;
            _intentService = intentService;
            _caseManagementService = caseManagementService;
            _qualityEvaluationService = qualityEvaluationService;
            _knowledgeGapService = knowledgeGapService;
            _callSummaryService = callSummaryService;
            _agentActivityService = agentActivityService;
            _logger = logger;
        }

        public async Task<T> ExecuteWithFallbackAsync<T>(
            string agentName, Func<Task<T>> foundryAction, Func<Task<T>> fallbackAction)
        {
            if (!IsFoundryConfigured)
            {
                _logger.LogDebug("Foundry not configured, using fallback for {Agent}", agentName);
                return await fallbackAction();
            }

            try
            {
                return await foundryAction();
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Foundry agent {Agent} failed, falling back to in-process service", agentName);
                return await fallbackAction();
            }
        }

        /// <summary>
        /// Ensures a named agent exists on AI Foundry. Creates it if missing.
        /// Thread-safe: only one agent provisioning runs at a time.
        /// </summary>
        public async Task<string> EnsureAgentAsync(
            string agentName,
            string description,
            string instructions,
            List<AgentToolDefinition> toolDefs)
        {
            // Fast path: already cached
            lock (_agentIds)
            {
                if (_agentIds.TryGetValue(agentName, out var cachedId))
                    return cachedId;
            }

            await _agentProvisionLock.WaitAsync();
            try
            {
                // Double-check after acquiring lock
                lock (_agentIds)
                {
                    if (_agentIds.TryGetValue(agentName, out var cachedId))
                        return cachedId;
                }

                var assistantClient = _projectClient!.ProjectOpenAIClient.GetAssistantClient();

                // Search for existing agent by name
                await foreach (var agent in assistantClient.GetAssistantsAsync())
                {
                    if (agent.Name == agentName)
                    {
                        lock (_agentIds) { _agentIds[agentName] = agent.Id; }
                        _logger.LogInformation("Found existing agent {Name} on Foundry: {AgentId}", agentName, agent.Id);
                        return agent.Id;
                    }
                }

                // Create the agent on AI Foundry
                var creationOptions = new AssistantCreationOptions
                {
                    Name = agentName,
                    Description = description,
                    Instructions = instructions,
                };
                foreach (var td in toolDefs)
                {
                    creationOptions.Tools.Add(new FunctionToolDefinition(td.Name)
                    {
                        Description = td.Description,
                        Parameters = BinaryData.FromString(td.ParametersJson),
                    });
                }

                var assistant = await assistantClient.CreateAssistantAsync(_config.AgentModel, creationOptions);
                var agentId = assistant.Value.Id;
                lock (_agentIds) { _agentIds[agentName] = agentId; }
                _logger.LogInformation("Created agent {Name} on Foundry: {AgentId} (model: {Model})",
                    agentName, agentId, _config.AgentModel);

                return agentId;
            }
            finally
            {
                _agentProvisionLock.Release();
            }
        }

        /// <summary>
        /// Generic Foundry agent runner — creates thread, sends message, polls for completion,
        /// handles tool calls via the provided dispatcher, and returns the agent's text output.
        /// </summary>
        public async Task<string?> RunAgentOnFoundryAsync(
            string agentName,
            string description,
            string instructions,
            List<AgentToolDefinition> toolDefs,
            string userMessage,
            Func<string, string, Task<string>> toolDispatcher)
        {
            var assistantClient = _projectClient!.ProjectOpenAIClient.GetAssistantClient();
            var agentId = await EnsureAgentAsync(agentName, description, instructions, toolDefs);

            var thread = await assistantClient.CreateThreadAsync();
            var threadId = thread.Value.Id;

            try
            {
                await assistantClient.CreateMessageAsync(threadId, OpenAI.Assistants.MessageRole.User,
                    new[] { MessageContent.FromText(userMessage) });

                var run = await assistantClient.CreateRunAsync(threadId, agentId);
                var runId = run.Value.Id;

                int iterations = 0;
                while (iterations < MaxToolCallIterations)
                {
                    var currentRun = (await assistantClient.GetRunAsync(threadId, runId)).Value;

                    if (currentRun.Status == RunStatus.Completed)
                    {
                        _logger.LogInformation("Agent {Name} run completed (thread: {ThreadId})", agentName, threadId);

                        // Retrieve the assistant's response text
                        string? responseText = null;
                        await foreach (var msg in assistantClient.GetMessagesAsync(threadId))
                        {
                            if (msg.Role == OpenAI.Assistants.MessageRole.Assistant)
                            {
                                responseText = string.Join("", msg.Content
                                    .Where(c => c.Text != null)
                                    .Select(c => c.Text));
                                break; // Most recent assistant message
                            }
                        }
                        return responseText;
                    }

                    if (currentRun.Status.IsTerminal)
                    {
                        throw new InvalidOperationException($"Agent {agentName} run ended with status {currentRun.Status}");
                    }

                    if (currentRun.Status == RunStatus.RequiresAction)
                    {
                        iterations++;
                        var toolOutputs = new List<ToolOutput>();

                        foreach (var action in currentRun.RequiredActions)
                        {
                            _logger.LogDebug("Agent {Name} calling tool {Tool}", agentName, action.FunctionName);
                            string result;
                            try
                            {
                                result = await toolDispatcher(action.FunctionName, action.FunctionArguments);
                            }
                            catch (Exception ex)
                            {
                                _logger.LogWarning(ex, "Tool {Tool} failed for agent {Name}", action.FunctionName, agentName);
                                result = JsonSerializer.Serialize(new { error = ex.Message });
                            }

                            toolOutputs.Add(new ToolOutput(action.ToolCallId, result));
                        }

                        await assistantClient.SubmitToolOutputsToRunAsync(threadId, runId, toolOutputs);
                    }
                    else
                    {
                        await Task.Delay(500);
                    }
                }

                throw new InvalidOperationException($"Agent {agentName} hit max tool call iterations ({MaxToolCallIterations})");
            }
            finally
            {
                try { await assistantClient.DeleteThreadAsync(threadId); }
                catch (Exception ex) { _logger.LogDebug(ex, "Failed to delete thread {ThreadId}", threadId); }
            }
        }

        // US1: Intent Discovery
        public async Task<int> DiscoverIntentsAsync()
        {
            return await ExecuteWithFallbackAsync(
                _config.IntentAgentName,
                async () =>
                {
                    var resultText = await RunAgentOnFoundryAsync(
                        _config.IntentAgentName,
                        "Intent Discovery Agent — analyzes call transcripts to discover customer intents",
                        IntentAgentInstructions,
                        _intentTools.GetToolDefinitions(),
                        "Analyze recent call transcripts and discover new customer intents. Fetch up to 50 transcripts.",
                        _intentTools.DispatchToolCallAsync);

                    await _agentActivityService.LogActivityAsync("CustomerIntent", "IntentDiscovery", AgentActionResult.Success,
                        $"Foundry agent completed intent discovery: {resultText}");

                    return int.TryParse(resultText, out var count) ? count : 0;
                },
                async () =>
                {
                    await _intentService.DiscoverIntentsAsync();
                    return 0;
                });
        }

        // US2: Case Management
        public async Task<Case?> ProcessCallOutcomeAsync(CallRecord callRecord)
        {
            return await ExecuteWithFallbackAsync<Case?>(
                _config.CaseAgentName,
                async () =>
                {
                    var resultText = await RunAgentOnFoundryAsync(
                        _config.CaseAgentName,
                        "Case Management Agent — creates or updates cases based on call outcomes",
                        CaseAgentInstructions,
                        _caseTools.GetToolDefinitions(),
                        $"Process call outcome for call {callRecord.CallConnectionId}. The caller phone is {callRecord.PhoneNumber}. " +
                        $"Overall sentiment: {callRecord.OverallSentiment}. Summary: {callRecord.CallSummary ?? "N/A"}",
                        _caseTools.DispatchToolCallAsync);

                    await _agentActivityService.LogActivityAsync("CaseManagement", "CaseCreated", AgentActionResult.Success,
                        $"Foundry agent processed call outcome: {resultText}", callRecord.CallConnectionId);

                    return null; // Case created/updated via tools
                },
                async () =>
                {
                    await _caseManagementService.ProcessCallOutcomeAsync(callRecord);
                    return null;
                });
        }

        // US3: Quality Evaluation
        public async Task EvaluateCallAsync(CallRecord callRecord)
        {
            await ExecuteWithFallbackAsync(
                _config.QualityAgentName,
                async () =>
                {
                    var transcript = string.Join("\n", callRecord.TranscriptEntries?.Select(
                        e => $"[{e.Speaker}]: {e.Text}") ?? Enumerable.Empty<string>());

                    var resultText = await RunAgentOnFoundryAsync(
                        _config.QualityAgentName,
                        "Quality Evaluation Agent — scores calls against quality criteria",
                        QualityAgentInstructions,
                        _qualityTools.GetToolDefinitions(),
                        $"Evaluate the quality of this call (ID: {callRecord.CallConnectionId}):\n\n{transcript}",
                        _qualityTools.DispatchToolCallAsync);

                    await _agentActivityService.LogActivityAsync("QualityAssurance", "QualityEvaluation", AgentActionResult.Success,
                        $"Foundry agent evaluated call quality: {resultText}", callRecord.CallConnectionId);

                    return true;
                },
                async () =>
                {
                    await _qualityEvaluationService.EvaluateCallAsync(callRecord);
                    return true;
                });
        }

        // US4: Knowledge Gap Detection
        public async Task DetectKnowledgeGapsAsync(CallRecord callRecord)
        {
            await ExecuteWithFallbackAsync(
                _config.KnowledgeGapAgentName,
                async () =>
                {
                    var transcript = string.Join("\n", callRecord.TranscriptEntries?.Select(
                        e => $"[{e.Speaker}]: {e.Text}") ?? Enumerable.Empty<string>());

                    var resultText = await RunAgentOnFoundryAsync(
                        _config.KnowledgeGapAgentName,
                        "Knowledge Gap Detection Agent — identifies unanswered questions from call transcripts",
                        KnowledgeGapAgentInstructions,
                        _knowledgeGapTools.GetToolDefinitions(),
                        $"Analyze this call transcript for knowledge gaps (call ID: {callRecord.CallConnectionId}):\n\n{transcript}",
                        _knowledgeGapTools.DispatchToolCallAsync);

                    await _agentActivityService.LogActivityAsync("KnowledgeManagement", "GapDetected", AgentActionResult.Success,
                        $"Foundry agent detected knowledge gaps: {resultText}", callRecord.CallConnectionId);

                    return true;
                },
                async () =>
                {
                    await _knowledgeGapService.DetectGapsAsync(callRecord);
                    return true;
                });
        }

        // US5: Call Summary Generation
        public async Task<string?> GenerateSummaryAsync(CallRecord callRecord)
        {
            return await ExecuteWithFallbackAsync<string?>(
                _config.SummaryAgentName,
                async () =>
                {
                    var resultText = await RunAgentOnFoundryAsync(
                        _config.SummaryAgentName,
                        "Call Summary Agent — generates concise contextual call summaries",
                        SummaryAgentInstructions,
                        _summaryTools.GetToolDefinitions(),
                        $"Generate a summary for call {callRecord.CallConnectionId}. " +
                        $"Caller: {callRecord.PhoneNumber}, Campaign: {callRecord.CampaignTitle ?? "N/A"}",
                        _summaryTools.DispatchToolCallAsync);

                    await _agentActivityService.LogActivityAsync("CallSummary", "SummaryGenerated", AgentActionResult.Success,
                        $"Foundry agent generated summary", callRecord.CallConnectionId);

                    return resultText;
                },
                async () =>
                {
                    if (callRecord.TranscriptEntries?.Count > 0)
                        return await _callSummaryService.GenerateSummaryAsync(callRecord.TranscriptEntries);
                    return null;
                });
        }

        /// <summary>
        /// Runs all 5 agents sequentially for a call, pushing per-agent SignalR progress updates.
        /// Designed to be called fire-and-forget after CallDisconnected.
        /// </summary>
        public async Task RunBatchAnalysisAsync(CallRecord callRecord, IHubContext<TranscriptHub> hubContext)
        {
            var callId = callRecord.CallConnectionId;
            var agents = new (string name, string label, Func<Task> action)[]
            {
                ("caseManagement", "Case Management", async () => { await ProcessCallOutcomeAsync(callRecord); }),
                ("qualityEvaluation", "Quality Evaluation", async () => { await EvaluateCallAsync(callRecord); }),
                ("knowledgeGapDetection", "Knowledge Gap Detection", async () => { await DetectKnowledgeGapsAsync(callRecord); }),
                ("summaryGeneration", "Summary Generation", async () => { await GenerateSummaryAsync(callRecord); }),
                ("intentDiscovery", "Intent Discovery", async () => { await DiscoverIntentsAsync(); }),
            };

            for (int i = 0; i < agents.Length; i++)
            {
                var (name, label, action) = agents[i];
                var progress = (int)((i / (double)agents.Length) * 100);

                await hubContext.Clients.Group(callId).SendAsync("AgentProcessingUpdate", new
                {
                    callConnectionId = callId,
                    agentName = name,
                    agentLabel = label,
                    status = "processing",
                    progress,
                    index = i,
                    total = agents.Length
                });

                try
                {
                    await action();
                    await hubContext.Clients.Group(callId).SendAsync("AgentProcessingUpdate", new
                    {
                        callConnectionId = callId,
                        agentName = name,
                        agentLabel = label,
                        status = "success",
                        progress = (int)(((i + 1) / (double)agents.Length) * 100),
                        index = i,
                        total = agents.Length
                    });
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "RunBatchAnalysis: {Agent} failed for {CallId}", name, callId);
                    await hubContext.Clients.Group(callId).SendAsync("AgentProcessingUpdate", new
                    {
                        callConnectionId = callId,
                        agentName = name,
                        agentLabel = label,
                        status = "failed",
                        progress = (int)(((i + 1) / (double)agents.Length) * 100),
                        index = i,
                        total = agents.Length,
                        error = ex.Message
                    });
                }
            }

            // Send completion event
            await hubContext.Clients.Group(callId).SendAsync("AgentProcessingUpdate", new
            {
                callConnectionId = callId,
                agentName = "all",
                agentLabel = "All Agents",
                status = "complete",
                progress = 100,
                index = agents.Length,
                total = agents.Length
            });
        }

        /// <summary>
        /// Ensures the PostCallReview orchestrator agent exists on AI Foundry.
        /// Delegates to the generic EnsureAgentAsync.
        /// </summary>
        public Task<string> EnsurePostCallReviewAgentAsync()
        {
            return EnsureAgentAsync(
                _config.PostCallReviewAgentName,
                "Post-Call Review Orchestrator — coordinates 5 sub-agents for call analysis",
                PostCallReviewAgentInstructions,
                _postCallReviewTools.GetToolDefinitions());
        }

        /// <summary>
        /// Runs the PostCallReview orchestrator agent via AI Foundry Agents API.
        /// All 6 agents (orchestrator + 5 sub-agents) are registered on Foundry.
        /// Falls back to RunBatchAnalysisAsync if Foundry is unavailable.
        /// </summary>
        public async Task RunPostCallReviewAsync(CallRecord callRecord, IHubContext<TranscriptHub> hubContext)
        {
            var callId = callRecord.CallConnectionId;

            // Send initial event
            await hubContext.Clients.Group(callId).SendAsync("AgentProcessingUpdate", new
            {
                callConnectionId = callId,
                agentName = "postCallReview",
                agentLabel = "Post-Call Review",
                status = "processing",
                progress = 0,
                index = 0,
                total = 5
            });

            var completed = false;

            try
            {
                await ExecuteWithFallbackAsync(
                    _config.PostCallReviewAgentName,
                    async () =>
                    {
                        // Wire up the tools with context
                        _postCallReviewTools.HubContext = hubContext;
                        _postCallReviewTools.ActiveCallId = callId;
                        _postCallReviewTools.ResetProgress();

                        var transcript = string.Join("\n", callRecord.TranscriptEntries?.Select(
                            e => $"[{e.Speaker}]: {e.Text}") ?? Enumerable.Empty<string>());

                        var userMessage =
                            $"Review this completed call and run the appropriate post-call analysis agents.\n\n" +
                            $"Call ID: {callId}\n" +
                            $"Caller: {callRecord.PhoneNumber}\n" +
                            $"Campaign: {callRecord.CampaignTitle ?? "N/A"}\n" +
                            $"Overall Sentiment: {callRecord.OverallSentiment}\n\n" +
                            $"Transcript:\n{transcript}";

                        await RunAgentOnFoundryAsync(
                            _config.PostCallReviewAgentName,
                            "Post-Call Review Orchestrator — coordinates 5 sub-agents for call analysis",
                            PostCallReviewAgentInstructions,
                            _postCallReviewTools.GetToolDefinitions(),
                            userMessage,
                            _postCallReviewTools.DispatchToolCallAsync);

                        if (_postCallReviewTools.CompletedCount == 0)
                        {
                            throw new InvalidOperationException("PostCallReview agent completed without invoking any post-call tools.");
                        }

                        await _agentActivityService.LogActivityAsync("PostCallReview", "OrchestratorComplete",
                            AgentActionResult.Success,
                            $"Foundry agent completed post-call review", callId);

                        return true;
                    },
                    async () =>
                    {
                        // Fallback: run batch analysis directly
                        _logger.LogInformation("PostCallReview fallback to RunBatchAnalysisAsync for {CallId}", callId);
                        await RunBatchAnalysisAsync(callRecord, hubContext);
                        return true;
                    });

                completed = true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "PostCallReview orchestrator failed for {CallId}", callId);
                await _agentActivityService.LogActivityAsync("PostCallReview", "OrchestratorFailed",
                    AgentActionResult.Failure, ex.Message, callId);
                await hubContext.Clients.Group(callId).SendAsync("AgentProcessingUpdate", new
                {
                    callConnectionId = callId,
                    agentName = "all",
                    agentLabel = "All Agents",
                    status = "failed",
                    progress = 100,
                    index = 5,
                    total = 5,
                    error = ex.Message
                });
                return;
            }

            // Send completion event
            if (completed)
            {
                await hubContext.Clients.Group(callId).SendAsync("AgentProcessingUpdate", new
                {
                    callConnectionId = callId,
                    agentName = "all",
                    agentLabel = "All Agents",
                    status = "complete",
                    progress = 100,
                    index = 5,
                    total = 5
                });
            }
        }

        #region Agent Instructions

        private const string IntentAgentInstructions = """
            You are a Customer Intent Discovery Agent for a contact center. Your job is to analyze call transcripts and discover distinct customer intents.

            For each intent you discover, provide:
            - name: A short label (e.g., "Billing Dispute", "Product Return")
            - groupName: A category (e.g., "Billing", "Support", "Sales")
            - description: One sentence explaining the intent
            - sampleUtterances: 2-3 example phrases from the transcripts
            - frequency: How many transcripts had this intent

            Workflow:
            1. Use get_call_transcripts to fetch recent transcripts
            2. Use get_existing_intents to check what intents already exist (avoid duplicates)
            3. Analyze the transcripts for new, distinct intents
            4. Use save_intents to persist each new intent

            Be thorough but avoid creating near-duplicate intents. Group related intents under the same groupName.
            """;

        private const string CaseAgentInstructions = """
            You are a Case Management Agent for a contact center. After each call, you decide whether to create a new case or update an existing one.

            Rules:
            - If the caller has an existing open/in-progress case from the last 24 hours, UPDATE it (set status to InProgress, append the new summary)
            - If no matching case exists, CREATE a new one
            - Derive the title from the call summary (max 100 chars)
            - Set priority based on sentiment: Negative → High, otherwise Medium
            - Always link the call record ID to the case

            Workflow:
            1. Use get_call_record to get the call details
            2. Use find_cases_by_caller to check for existing cases
            3. Either create_case or update_case based on your decision
            """;

        private const string QualityAgentInstructions = """
            You are a Quality Evaluation Agent for a contact center. You evaluate call transcripts against quality criteria.

            For each criterion, provide:
            - criterionName: Must match the criterion name exactly
            - score: 1.0 to 5.0
            - justification: Brief explanation of the score

            After scoring all criteria, determine if the call should be flagged:
            - If any score is below the criterion's minimum passing score, flag it
            - Provide the flag reason

            Workflow:
            1. Use get_quality_criteria to fetch current evaluation criteria
            2. Evaluate the provided transcript against each criterion
            3. Use save_evaluation to persist the scores
            """;

        private const string KnowledgeGapAgentInstructions = """
            You are a Knowledge Gap Detection Agent for a contact center. Your job is to analyze call transcripts and identify questions the AI agent could NOT answer or answered poorly.

            For each knowledge gap you find, provide:
            - topic: The general topic
            - question: The specific customer question
            - suggestedTitle: A title for a knowledge base article that would fill the gap
            - suggestedArticle: A draft article in markdown format

            Workflow:
            1. Analyze the provided transcript for unanswered or poorly answered questions
            2. Use search_knowledge_base to check if the topic is already covered
            3. Use get_existing_gaps to check for duplicate topics
            4. For existing gaps: use update_gap to increment frequency
            5. For new gaps: use create_gap with the topic, question, and suggested article

            Only flag genuine gaps where the AI did not have the information.
            """;

        private const string SummaryAgentInstructions = """
            You are a Call Summary Agent for a contact center. Your job is to produce concise, context-aware summaries of phone calls.

            Generate a 2-4 sentence summary that includes:
            - The main topic discussed
            - Key outcome (e.g., appointment scheduled, information provided, issue resolved)
            - The overall tone of the conversation
            - If the caller has prior history, reference it

            Workflow:
            1. Use get_call_transcript to get the full transcript
            2. Use get_caller_case_history to check for prior calls/cases from the same caller
            3. Generate a contextual summary incorporating caller history
            4. Use save_summary to persist the summary

            Respond with plain text only — no JSON, no bullet points, no markdown.
            """;

        private const string PostCallReviewAgentInstructions = """
            You are a Post-Call Review Orchestrator for a contact center. After each call ends, you decide which analysis agents to run and in what order.

            You have 5 sub-agent tools at your disposal:
            1. run_summary_generation — Generates a contextual call summary (run this FIRST since other agents may use the summary)
            2. run_case_management — Creates or updates a case based on the call outcome
            3. run_quality_evaluation — Scores the call against quality criteria
            4. run_knowledge_gap_detection — Identifies unanswered questions or knowledge gaps
            5. run_intent_discovery — Discovers new customer intents from recent call patterns

            Workflow:
            1. Always run run_summary_generation first — the summary enriches other agents' analysis
            2. Run run_case_management, run_quality_evaluation, and run_knowledge_gap_detection (all need callConnectionId)
            3. Run run_intent_discovery last (it analyzes across recent calls, not just this one)
            4. For all tools that require callConnectionId, pass the Call ID from the user message

            Run ALL 5 tools for every call. Report any failures but continue with the remaining tools.
            """;

        #endregion
    }
}
