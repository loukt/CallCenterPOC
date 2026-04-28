# Feature Specification: Agentic Contact Center

**Feature Branch**: `003-agentic-contact-center`  
**Created**: 2026-03-27  
**Status**: Draft  
**Input**: Evolve the solution to an agentic architecture with specialized AI agents (Customer Intent Agent, Case Management Agent, Knowledge Management Agent, Quality Evaluation Agent), add knowledge base ingestion (PDF/DOC via Azure AI Search), support inbound calls, and enable non-ACS calling via browser-based WebRTC.

---

## Clarifications

### Session 2026-03-27

- Q: What should "escalation to a human operator" mean — live transfer, callback, dashboard join, or AI-guided? → A: Live call transfer — human operator joins the call first, AI verbally introduces the context and hands off, then AI drops off the call.
- Q: How should case data be persisted given relational query needs (caller matching, status filtering)? → A: Azure Cosmos DB for cases and intents — structured queries with no Blob Storage index workaround needed.
- Q: What protection should shareable WebRTC call links have against abuse? → A: Time-expiring (configurable, default 15 minutes) + single-use (invalidated after first join).
- Q: What level of observability should the autonomous agents have? → A: Structured agent activity log + dashboard "Agent Activity" panel showing recent actions and failures.
- Q: How should the AI behave when Azure AI Search is unavailable during a live call? → A: AI falls back to general model knowledge gracefully (no source attribution), unless the campaign explicitly restricts the agent to provided data only — in that case, the AI states it cannot answer and the failure is logged.
- Q: Where should the escalation phone number be configured? → A: Both — default escalation number in Settings, with optional per-campaign override. Fallback chain: campaign override → settings default → dashboard-notification-only (case + callback request). For internet calls, escalation sends a dashboard notification and the operator clicks "Join Escalation."
- Q: How should internet-based callers be identified for case matching when no phone number exists? → A: Caller self-identification — the Join Call page asks for name + optional email/phone before connecting; this is used for case matching and deduplication.
- Q: When does a case transition to "InProgress" status? → A: Automatically when a follow-up call begins on an existing open case, indicating active handling.
- Q: How should Success Criterion 7 (Escalation Success Rate) account for fallback paths where no live phone transfer occurs? → A: Broaden to cover all paths — live call transfer, WebRTC operator join, or dashboard notification with case creation. AI verbal summary applies to connected-transfer cases only.
- Q: Should the hold message when concurrent call limit is reached be hardcoded or configurable via Settings? → A: Configurable — add a HoldMessage string field to OperatorSettings so operators can customize the text via the Settings UI.
- Q: Should US3-AS5 (no operator available for escalation) include callback scheduling or just case creation? → A: Simplify — the AI takes a message and creates a case record with Escalation priority. No callback scheduling needed for POC.
- Q: What time bound should NFR-004 (intent discovery batch of 1,000 transcripts) have? → A: Within 10 minutes.

---

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Customer Intent Agent for Self-Service (Priority: P1)

As a contact center administrator, I want an AI agent that autonomously discovers customer intents from past call transcripts and conversations so that future calls (inbound and outbound) are handled with contextual understanding of what the customer likely needs.

**Why this priority**: Intent discovery is the foundation of the agentic system — all other agents depend on knowing why the customer is calling. Without intent, the system is just a generic chatbot.

**Independent Test**: Upload or accumulate 10+ call history records, trigger intent discovery, verify the system creates an intent library with categorized intents (e.g., "billing inquiry," "appointment reschedule," "complaint"). Initiate a new call and verify the AI references discovered intents to guide the conversation.

**Acceptance Scenarios**:

1. **Given** the system has 10+ historical call records, **When** the administrator triggers intent discovery, **Then** the system analyzes transcripts and produces a list of discovered intents grouped into intent categories.
2. **Given** discovered intents exist in the intent library, **When** an inbound or outbound call begins, **Then** the AI agent uses the intent library to identify the caller's likely intent within the first 30 seconds of conversation.
3. **Given** the AI identifies a caller's intent (e.g., "billing dispute"), **When** a matching knowledge article exists, **Then** the AI uses the knowledge article to provide an accurate resolution without human intervention.
4. **Given** the AI cannot resolve the caller's issue autonomously, **When** the confidence score falls below a configurable threshold, **Then** the system transfers the live call to a human operator — the operator joins the call first, the AI verbally introduces the caller, summarizes the conversation and detected intent, then the AI disconnects from the call.
5. **Given** intent discovery runs periodically, **When** new call records accumulate, **Then** the system discovers new intents and updates existing ones without losing previously approved intents.

---

### User Story 2 - Knowledge Base Integration via Azure AI Search (Priority: P1)

As an administrator, I want to upload documents (PDF, DOCX, TXT) to a knowledge base so that the AI agents can instantly reference accurate, company-specific information during conversations.

**Why this priority**: Without a knowledge base, the AI can only use its general training data. Real contact centers need domain-specific answers about products, policies, and procedures immediately available to the agent.

**Independent Test**: Upload a PDF product manual, verify it appears in the knowledge base list, initiate a call, ask a question that can only be answered from the uploaded document, and verify the AI provides the correct answer.

**Acceptance Scenarios**:

1. **Given** the administrator is on the Knowledge Base management page, **When** they upload a PDF document (up to 50 MB), **Then** the system extracts text, chunks it, generates embeddings, and indexes it in Azure AI Search within 2 minutes.
2. **Given** a DOCX file is uploaded, **When** the processing completes, **Then** the document content is searchable and available for AI agent reference.
3. **Given** multiple documents are in the knowledge base, **When** a caller asks a question related to the content, **Then** the AI performs a hybrid search (keyword + vector) and retrieves the most relevant passages to formulate its answer.
4. **Given** the administrator wants to remove outdated information, **When** they delete a document from the knowledge base, **Then** the corresponding index entries are removed and the AI no longer references that content.
5. **Given** a document is being processed, **When** the administrator views the knowledge base, **Then** they see the document's processing status (Uploading, Processing, Indexed, Failed).
6. **Given** the AI uses a knowledge base article to answer a question, **When** the transcript is reviewed, **Then** the system indicates which knowledge source was used (source attribution).

---

### User Story 3 - Inbound Call Handling (Priority: P1)

As a contact center operator, I want the system to receive and handle inbound phone calls so that customers can call in and be assisted by the AI agent or routed to a human operator.

**Why this priority**: The current system only supports outbound calls. A real contact center must handle inbound traffic — this is the primary use case for most contact centers.

**Independent Test**: Configure an ACS phone number for inbound routing, call the number from a real phone, verify the AI agent answers, conducts a conversation, and the call appears in the live dashboard.

**Acceptance Scenarios**:

1. **Given** an ACS phone number is configured for inbound routing, **When** a customer calls that number, **Then** the system accepts the call and connects the AI agent within 3 seconds.
2. **Given** an inbound call is answered by the AI agent, **When** the caller speaks, **Then** the AI agent responds using the configured default inbound campaign and references the knowledge base.
3. **Given** an inbound call is in progress, **When** the operator views the dashboard, **Then** the call appears in the live calls panel with an "Inbound" badge, showing real-time transcript and sentiment.
4. **Given** the AI agent cannot resolve the caller's issue, **When** escalation is triggered, **Then** the system adds a human operator to the live call, the AI verbally introduces the caller and summarizes the conversation (intent, key points, suggested actions), and then the AI disconnects — leaving the operator and caller on the line.
5. **Given** no operators are available for escalation, **When** the AI cannot help further, **Then** the system offers to take a message and creates a case record with "Escalation" priority.
6. **Given** an inbound call arrives, **When** a campaign with inbound routing rules is configured, **Then** the system routes the call to the appropriate AI agent behavior based on the called number and campaign mapping.
7. **Given** multiple inbound calls arrive simultaneously, **When** the concurrent call limit is reached, **Then** additional callers hear the hold message configured in OperatorSettings (default: "All agents are currently busy. Please try again later.") or are asked to call back.

---

### User Story 4 - Browser-Based Calling Without ACS (Priority: P2)

As an operator, I want to be able to make and receive calls directly from the browser using WebRTC so that the system can function without Azure Communication Services for testing, demos, or environments where ACS is not available.

**Why this priority**: ACS has per-minute telephony costs and requires PSTN number provisioning. A WebRTC option enables free browser-to-browser calls for testing, internal demos, and scenarios where real phone numbers aren't needed.

**Independent Test**: Enable "Call over Internet" in settings, verify the phone number fields disappear and a "Generate Call Link" button appears. Click it, verify a shareable link is generated, open the link in another browser tab, verify bidirectional voice works with the AI agent in the middle.

**Acceptance Scenarios**:

1. **Given** the operator enables "Call over Internet" in settings, **When** they return to the call panel, **Then** the phone number input fields are hidden and replaced with a "Generate Call Link" button.
2. **Given** a shareable call link is generated, **When** a user opens the link in any modern browser before expiry, **Then** a lightweight call page loads with a "Join Call" button that requests microphone permission.
3. **Given** the remote user joins the call via the link, **When** both parties are connected, **Then** the link is immediately invalidated (cannot be reused) and bidirectional audio streams through the server with the AI agent mediating the conversation (same as phone call mode).
4. **Given** an internet call is in progress, **When** the operator views the dashboard, **Then** the call appears with an "Internet" badge and all features work identically (transcript, sentiment, recording, history).
5. **Given** "Call over Internet" is enabled, **When** a call link is shared publicly, **Then** callers can reach the AI agent by clicking the link — functioning as a lightweight inbound channel.
6. **Given** the operator disables "Call over Internet" in settings, **When** they return to the call panel, **Then** the phone number input fields reappear and subsequent calls use phone network as before (no restart required).

---

### User Story 5 - Case Management Agent (Priority: P2)

As a contact center system, I want an AI agent that automatically creates, updates, and closes case records based on call outcomes so that operators don't have to manually manage case documentation.

**Why this priority**: Manual case management is the biggest time sink for operators after a call. Automating the case lifecycle directly improves operator productivity and ensures consistent documentation.

**Independent Test**: Complete a call where the customer reports an issue, verify a case is automatically created with the correct details. Complete a follow-up call about the same issue, verify the existing case is updated. Resolve the issue on a call, verify the case is closed.

**Acceptance Scenarios**:

1. **Given** a call ends where the customer reported a new issue, **When** the call record is saved, **Then** the Case Management Agent automatically creates a case with: title (derived from intent), description (call summary), priority (derived from sentiment and urgency), status ("Open"), and linked call record.
2. **Given** a customer calls back about an existing open case, **When** the system matches the caller's phone number or email to an existing case, **Then** the Case Management Agent transitions the case to "InProgress," updates it with the new interaction details, and appends the transcript.
3. **Given** a call ends where the issue was resolved, **When** the AI determines the customer confirmed resolution, **Then** the Case Management Agent sets the case status to "Resolved" and adds a resolution summary.
4. **Given** the administrator wants to review all open cases, **When** they navigate to the Cases view, **Then** they see a list of all cases with status, priority, linked calls, and time since creation.
5. **Given** a case has been in "Resolved" status for 48 hours without re-contact, **When** the auto-close window expires, **Then** the Case Management Agent changes the status to "Closed."

---

### User Story 6 - Knowledge Management Agent (Priority: P3)

As a contact center system, I want an AI agent that identifies knowledge gaps from call outcomes and suggests new knowledge articles so that the knowledge base continuously improves.

**Why this priority**: Static knowledge bases decay over time. An agent that learns from conversations ensures the knowledge base stays current and relevant, reducing unanswered queries over time.

**Independent Test**: Complete several calls where the AI could not answer a question due to missing knowledge, verify the Knowledge Management Agent identifies the gap and drafts a suggested article. Review and approve the article, verify subsequent calls can answer the question.

**Acceptance Scenarios**:

1. **Given** the AI agent could not answer a caller's question during a call, **When** the call record is analyzed post-call, **Then** the Knowledge Management Agent flags the topic as a knowledge gap.
2. **Given** knowledge gaps are identified, **When** the administrator views the Knowledge Gaps dashboard, **Then** they see a list of unanswered topics with frequency, most recent occurrence, and a suggested draft article.
3. **Given** the administrator approves a suggested knowledge article, **When** they click "Publish," **Then** the article is added to the knowledge base index and immediately available for AI reference.
4. **Given** a previously unanswerable question is now covered by a new article, **When** a caller asks the same question, **Then** the AI provides the correct answer with source attribution.

---

### User Story 7 - Quality Evaluation Agent (Priority: P3)

As a supervisor, I want an AI agent that automatically evaluates the quality of every call (handled by AI or human) against configurable criteria so that I can monitor service quality without manually reviewing recordings.

**Why this priority**: Quality assurance in contact centers is labor-intensive. Automated evaluation enables 100% call coverage instead of the typical 2-5% manual sample.

**Independent Test**: Define evaluation criteria (greeting quality, issue resolution, compliance phrases), complete several calls, verify each call receives an automated quality score with per-criterion breakdown. Flag a call as "needs review" and verify it appears in the supervisor's review queue.

**Acceptance Scenarios**:

1. **Given** evaluation criteria are configured (e.g., "greeting quality," "issue resolution," "compliance"), **When** a call ends, **Then** the Quality Evaluation Agent scores the call on each criterion (1-5 scale) and generates an overall quality score.
2. **Given** a call scores below the minimum quality threshold (configurable), **When** the evaluation completes, **Then** the system flags the call for supervisor review and sends a notification.
3. **Given** the supervisor views the Quality Dashboard, **When** they select a time period, **Then** they see average quality scores, trend charts, and a list of flagged calls sorted by severity.
4. **Given** the supervisor wants to review a flagged call, **When** they click on it, **Then** they see the full transcript, per-criterion scores with justifications, and the AI's reasoning for each score.
5. **Given** evaluation criteria are updated, **When** new calls are evaluated, **Then** the updated criteria are applied (existing evaluations are not retroactively changed).

---

## Functional Requirements *(mandatory)*

### Intent & Knowledge

- **FR-001**: The system shall analyze historical call transcripts to discover and categorize customer intents into an intent library.
- **FR-002**: The system shall support uploading documents in PDF, DOCX, and TXT formats (up to 50 MB each) to a knowledge base.
- **FR-003**: The system shall extract text from uploaded documents, chunk it, generate vector embeddings, and index it in Azure AI Search.
- **FR-004**: The system shall perform hybrid search (keyword + semantic vector) against the knowledge base when answering questions during calls.
- **FR-005**: The system shall attribute AI responses to specific knowledge sources when knowledge base content is used.
- **FR-006**: The system shall support creating, viewing, and deleting knowledge base documents through a management interface.

### Inbound Call Handling

- **FR-007**: The system shall accept inbound calls on configured ACS phone numbers and route them to the AI agent.
- **FR-008**: The system shall support configurable inbound routing rules that map phone numbers to specific campaigns or default AI agent behavior.
- **FR-009**: The system shall support AI-to-operator escalation during calls (inbound and outbound) by adding the operator to the live call, having the AI verbally introduce the context, and then removing the AI from the call.
- **FR-031**: The system shall support escalation number configuration at two levels: a global default escalation phone number in Settings, and an optional per-campaign escalation number override. When escalation is triggered, the system uses the campaign-level number if set, otherwise the Settings default. If no number is configured at either level, escalation creates a dashboard notification with a case and callback request instead of a live transfer. For internet-based calls, escalation sends a real-time dashboard notification and the operator clicks "Join Escalation" to be added via WebRTC.
- **FR-010**: The system shall distinguish inbound and outbound calls visually in the dashboard and call history.
- **FR-011**: The system shall handle concurrent inbound and outbound calls up to the configured maximum (default: 5 total).

### Browser-Based Calling (WebRTC)

- **FR-012**: The system shall support an alternative internet-based calling mode (WebRTC) that operates without phone network connectivity, activated via a "Call over Internet" checkbox in settings.
- **FR-013**: The system shall generate shareable call links when in WebRTC mode. Links expire after a configurable time window (default: 15 minutes) and are single-use (invalidated after the first user joins).
- **FR-014**: The system shall establish audio connections through a SignalR server relay, with the AI agent processing audio in the same pipeline as ACS calls.
- **FR-015**: The system shall support switching between phone and internet calling modes without application restart. When "Call over Internet" is enabled, phone number input fields are hidden and replaced with link generation controls.

### Case Management

- **FR-016**: The system shall automatically create case records from call outcomes, including derived title, description, priority, and linked call records.
- **FR-017**: The system shall match returning callers to existing open cases by phone number (for PSTN calls) or by self-provided email/phone (for internet-based calls). The Join Call page collects caller name and optional email/phone before connecting.
- **FR-018**: The system shall automatically update case records when follow-up calls occur on the same issue, transitioning the case status to "InProgress" when a follow-up call begins.
- **FR-019**: The system shall automatically close cases after a configurable period in "Resolved" status without re-contact (default: 48 hours).
- **FR-020**: The system shall provide a Cases view for operators and supervisors to manage open, resolved, and closed cases.

### Knowledge Management Agent

- **FR-021**: The system shall analyze call outcomes to identify questions the AI could not answer (knowledge gaps).
- **FR-022**: The system shall generate draft knowledge articles for identified knowledge gaps.
- **FR-023**: The system shall support a review-and-publish workflow for suggested knowledge articles.

### Quality Evaluation

- **FR-024**: The system shall automatically evaluate completed calls against configurable quality criteria.
- **FR-025**: The system shall generate per-criterion scores (1-5 scale) and an overall quality score for each call.
- **FR-026**: The system shall flag calls below a configurable quality threshold for supervisor review.
- **FR-027**: The system shall provide a Quality Dashboard with trend analytics, average scores, and flagged call lists.

### Agent Observability

- **FR-028**: Each agent action (intent discovery, case creation/update, knowledge gap detection, quality evaluation) shall produce a structured log entry containing: agent name, action type, input call record ID, result (success/failure), duration, and error details if applicable.
- **FR-029**: The system shall provide an "Agent Activity" panel in the dashboard showing the most recent agent actions, filterable by agent type, with failure highlighting.

### Knowledge & Campaign Behavior

- **FR-030**: Campaigns shall support a "restrict to provided data only" flag. When enabled, the AI agent must only answer from knowledge base content and shall not fall back to general model knowledge. If the knowledge base is unavailable or returns no results, the AI states it cannot answer and suggests escalation.
- **FR-032**: Knowledge documents shall support an optional `campaignId` field. When assigned, the document is only searchable during calls associated with that specific campaign. Documents without a `campaignId` are searchable across all campaigns (global KB).
- **FR-033**: The KB management UI shall display a campaign selector dropdown when uploading or editing a document. The default is "All Campaigns" (global). Only documents matching the active campaign (or global documents) shall be returned during AI hybrid search.
- **FR-034**: The KB search endpoint shall accept an optional `campaignId` filter parameter. When provided, results are limited to documents tagged with that campaign or untagged (global) documents.

### Dashboard Analytics

- **FR-035**: The dashboard shall provide a time-range filter for analytics (Today, Yesterday, Last 7 Days, Last 30 Days, Year) that applies to: call history counts, sentiment trends, case statistics, and quality evaluation scores.
- **FR-036**: The API shall expose a `/api/analytics/dashboard` endpoint that returns aggregated KPI data (total calls, average duration, sentiment breakdown, case counts by status, quality scores) filtered by the requested time range.
- **FR-037**: The dashboard KPI bar shall update dynamically when the time range is changed, without page reload.

### KB Processing UX

- **FR-038**: The Knowledge Base upload UI shall show real-time processing progress with the following states and visual indicators: Uploading (progress bar), Processing (animated spinner with "Extracting text..."), Indexing (spinner with "Building search index..."), Indexed (green checkmark with "Ready — searchable"), Failed (red X with error message and Retry button).
- **FR-039**: The system shall display an estimated processing time when a document upload begins (based on file size: <1MB ≈ 30s, 1-10MB ≈ 1min, 10-50MB ≈ 2min).

---

## Success Criteria *(mandatory)*

1. **Intent Recognition Accuracy**: The AI correctly identifies caller intent in at least 80% of calls where the knowledge base and historical transcripts contain relevant information for the caller's stated purpose.
2. **Knowledge Base Response Time**: Uploaded documents are searchable within 2 minutes of upload completion (see NFR-001).
3. **Inbound Call Answer Time**: The system answers inbound calls and connects the AI agent within 3 seconds of the call being received.
4. **Case Auto-Creation Rate**: At least 90% of calls that involve a customer issue result in an automatically created or updated case record.
5. **Quality Evaluation Coverage**: 100% of completed calls receive an automated quality evaluation within 60 seconds of call completion.
6. **WebRTC Call Quality**: Browser-to-browser calls maintain conversational audio quality (no perceptible lag or dropout) for calls up to 10 minutes.
7. **Escalation Success Rate**: 100% of escalation attempts result in either a live call transfer, a WebRTC operator join, or a dashboard notification with case creation. In all connected-transfer cases, the AI verbally summarizes the context before disconnecting.
8. **Knowledge Gap Detection**: The system identifies at least 70% of repeated unanswered questions within the first week of operation.
9. **Operator Efficiency**: Operators spend less than 1 minute on post-call documentation per call due to automated case management. Measured by observation during demos, not automated testing.
10. **All Existing Features Preserved**: All current capabilities continue to function without regression (see NFR-007).

---

## Key Entities *(mandatory)*

### Intent

| Field | Type | Description |
|-------|------|-------------|
| Id | string (GUID) | Unique identifier |
| Name | string | Human-readable intent name (e.g., "Billing Dispute") |
| GroupName | string | Intent category group |
| Description | string | What this intent represents |
| Status | enum | Pending, Approved, Discarded |
| Frequency | int | How often this intent has been detected |
| SampleUtterances | string[] | Example caller phrases that match this intent |
| LinkedKnowledgeArticles | string[] | Knowledge article IDs relevant to this intent |
| CreatedAt | DateTimeOffset | When the intent was first discovered |

### KnowledgeDocument

| Field | Type | Description |
|-------|------|-------------|
| Id | string (GUID) | Unique identifier |
| FileName | string | Original file name |
| FileType | string | PDF, DOCX, TXT |
| FileSizeBytes | long | File size |
| Status | enum | Uploading, Processing, Indexed, Failed |
| ChunkCount | int | Number of text chunks created |
| IndexName | string | Azure AI Search index name |
| UploadedBy | string | Operator who uploaded |
| UploadedAt | DateTimeOffset | Upload timestamp |
| ErrorMessage | string? | Error details if processing failed |
| BlobUri | string | Reference to uploaded file in Blob Storage |
| CampaignId | string? | Optional campaign ID — null means global (searchable by all campaigns) |
| ProcessingProgress | string? | Current processing stage description for UX display |

### Case

| Field | Type | Description |
|-------|------|-------------|
| Id | string (GUID) | Unique identifier |
| Title | string | Derived from intent or call summary |
| Description | string | Case details from call transcript |
| Status | enum | Open, InProgress, Resolved, Closed. Transitions: Open → InProgress (follow-up call begins), InProgress → Resolved (issue confirmed resolved), Resolved → Closed (48h auto-close) |
| Priority | enum | Low, Medium, High, Critical, Escalation |
| CallerPhoneNumber | string | Masked phone number |
| CallerName | string? | Self-provided caller name (WebRTC) or contact name (outbound) |
| CallerEmail | string? | Self-provided email (WebRTC, optional, for case matching) |
| CallSource | string | Call origin: "Phone", "Inbound", or "Internet" |
| CampaignId | string? | Associated campaign ID |
| LinkedCallRecords | string[] | Associated call record IDs |
| Intent | string? | Detected customer intent |
| ResolutionSummary | string? | How the issue was resolved |
| CreatedAt | DateTimeOffset | Case creation time |
| UpdatedAt | DateTimeOffset | Last update time |
| ResolvedAt | DateTimeOffset? | When marked resolved |
| ClosedAt | DateTimeOffset? | When auto-closed |

### QualityEvaluation

| Field | Type | Description |
|-------|------|-------------|
| Id | string (GUID) | Unique identifier |
| CallRecordId | string | Linked call record |
| OverallScore | float | Aggregate quality score (1-5) |
| CriterionScores | CriterionScore[] | Per-criterion breakdown |
| Flagged | bool | Whether below quality threshold |
| FlagReason | string? | Why the call was flagged |
| EvaluatedAt | DateTimeOffset | Evaluation timestamp |

### CriterionScore

| Field | Type | Description |
|-------|------|-------------|
| CriterionName | string | e.g., "Greeting Quality," "Issue Resolution" |
| Score | float | 1-5 scale |
| Justification | string | AI's reasoning for the score |

### WebRTCCallSession

| Field | Type | Description |
|-------|------|-------------|
| SessionId | string (GUID) | Unique session identifier |
| ShareableLink | string | URL for the remote party to join |
| ExpiresAt | DateTimeOffset | When the link becomes invalid |
| IsUsed | bool | Whether the link has been consumed by a join |
| Status | enum | Waiting, Connected, Disconnected |
| CreatedAt | DateTimeOffset | Session creation time |
| CallerName | string? | Self-provided caller name from Join Call page |
| CallerEmail | string? | Self-provided email (optional, for case matching) |
| CallerPhone | string? | Self-provided phone (optional, for case matching) |
| CallerConnectionId | string? | SignalR connection ID of the caller |
| OperatorConnectionId | string | SignalR connection ID of the operator |
| CampaignId | string? | Associated campaign ID |

### KnowledgeGap

| Field | Type | Description |
|-------|------|-------------|
| Id | string (GUID) | Unique identifier |
| Topic | string | The unanswered question or topic |
| Frequency | int | How many times this gap has been encountered |
| Status | enum | Identified, ArticleDrafted, Published, Dismissed |
| SampleQuestions | string[] | Example caller questions that triggered this gap |
| SuggestedTitle | string? | AI-generated title for the draft article |
| SuggestedArticle | string? | AI-generated draft article text |
| LinkedCallRecordIds | string[] | Call records where the gap was detected |
| LastOccurrence | DateTimeOffset | When the gap was most recently encountered |
| CreatedAt | DateTimeOffset | When the gap was first identified |

### AgentActivityEntry

| Field | Type | Description |
|-------|------|-------------|
| Id | string (GUID) | Unique identifier |
| AgentName | string | Name of the agent (e.g., "CaseManagement", "QualityEvaluation") |
| ActionType | string | Type of action performed (e.g., "CaseCreated", "IntentDiscovered") |
| CallRecordId | string? | Associated call record ID, if applicable |
| Result | enum | Success, Failure, Skipped |
| DurationMs | long | How long the action took in milliseconds |
| ResultDetail | string? | Additional context or error message |
| Timestamp | DateTimeOffset | When the action occurred |

---

## Non-Functional Requirements *(mandatory)*

- **NFR-001**: Knowledge base document processing (upload to searchable) shall complete within 2 minutes for documents up to 50 MB.
- **NFR-002**: Hybrid search queries against the knowledge base shall return results within 500 milliseconds.
- **NFR-003**: The system shall support at least 100 documents in the knowledge base simultaneously.
- **NFR-004**: Intent discovery shall complete analysis of up to 1,000 historical call transcripts within 10 minutes in a single run.
- **NFR-005**: Quality evaluations shall complete within 60 seconds of call termination.
- **NFR-006**: WebRTC audio latency shall not exceed 200 milliseconds round-trip for browser-to-server communication.
- **NFR-007**: The system shall maintain backward compatibility with all existing features (outbound calls, campaigns, sentiment, emotion, call history, VoiceLive, settings).
- **NFR-008**: All agent actions shall be logged with structured entries; the Agent Activity panel shall display the 50 most recent actions with sub-second rendering.

---

## Scope Boundaries *(mandatory)*

### In Scope

- Customer Intent Agent: intent discovery from historical transcripts, intent-driven conversation guidance
- Knowledge base management: upload, process, index, search, delete documents (PDF, DOCX, TXT)
- Azure AI Search integration: hybrid search (keyword + vector) with embeddings
- Inbound call handling via ACS with AI agent auto-answer
- WebRTC browser-based calling as an alternative to ACS
- Case Management Agent: automatic case lifecycle (create, update, resolve, close)
- Knowledge Management Agent: knowledge gap detection and article suggestion
- Quality Evaluation Agent: automated call quality scoring against configurable criteria
- UI/UX for all new features integrated into the existing dashboard
- Agent observability: structured activity logging and dashboard Agent Activity panel
- Source attribution for knowledge-based AI responses

### Out of Scope

- Multi-tenant architecture (single-tenant POC)
- User authentication and role-based access control (admin vs. operator vs. supervisor are informational labels, not enforced)
- SIP trunking or third-party telephony providers (only ACS and WebRTC)
- Video calling (audio only)
- Multi-language support for the UI (English only; AI conversation language is model-dependent)
- IVR menu trees (the AI agent handles routing conversationally)
- Workforce management (scheduling, shift planning)
- Real-time supervisor intervention (listen-in, whisper, barge-in on live calls)
- Payment processing or PCI compliance
- HIPAA or other regulatory compliance beyond basic data handling

---

## Dependencies *(mandatory)*

| Dependency | Purpose | Status |
|------------|---------|--------|
| Azure Communication Services | Telephony for inbound and outbound PSTN calls | Existing |
| Azure OpenAI Service | AI agent conversation (Realtime API), sentiment, emotion, summaries, intent discovery, quality evaluation | Existing |
| Azure AI VoiceLive | Alternative voice engine with Dragon HD voices | Existing |
| Azure AI Search | Knowledge base indexing and hybrid retrieval | New — must be provisioned (Free tier, Southeast Asia, RBAC auth via Managed Identity) |
| Azure Cosmos DB (NoSQL API) | Structured persistence for cases, intents, quality evaluations, and knowledge gap records | New — must be provisioned |
| Azure Blob Storage | Document storage, call recordings, case records, knowledge articles | Existing |
| Azure AI Document Intelligence (optional) | Enhanced PDF/DOCX text extraction for complex layouts | New — optional, can fallback to built-in extraction |
| SignalR | Real-time transcript, sentiment, call status, WebRTC signaling | Existing |

---

## Assumptions *(mandatory)*

1. **Azure AI Search** will be provisioned in the same region as the existing resources (Southeast Asia or compatible) with a Basic or Standard tier that supports vector search.
1b. **Azure Cosmos DB** will be provisioned in the same region with serverless or autoscale capacity mode to minimize cost at POC scale.
2. **Document extraction** for simple PDF/DOCX files will use standard text extraction. Azure AI Document Intelligence is used only for complex layouts with tables, images, or scanned content.
3. **Embeddings** will be generated using Azure OpenAI's `text-embedding-3-small` or equivalent deployed model.
4. **Intent discovery** runs as a batch process (on-demand or scheduled daily), not in real-time during calls. Real-time intent detection during calls uses the pre-built intent library.
5. **WebRTC signaling** will be handled through the existing SignalR infrastructure (no separate TURN/STUN servers for POC — uses public STUN servers like Google's for NAT traversal).
6. **Case and intent persistence** will use Azure Cosmos DB (NoSQL API) for structured querying (status, priority, phone number matching, intent lookups). Call recordings and documents remain in Blob Storage.
7. **Quality evaluation criteria** are configured by the administrator through a settings interface, with a set of default criteria provided out of the box.
8. **Inbound call routing** in ACS will use Event Grid webhook notifications to the existing callback controller pattern.
9. **The existing 5-call concurrent limit** applies to the total of inbound + outbound + WebRTC calls combined.
10. **Knowledge gap detection** is a post-call batch analysis, not real-time during the call.

---

## UX/UI Design Direction

> **UX Review Note (2026-03-27)**: The current dashboard is a 3-panel layout (left 22% / center flex / right 22%) with deep navy sidebars. The left panel handles campaign selection + phone input + call initiation. The center panel shows live call transcript/sentiment or call history detail. The right panel has 2 tabs: "Live calls" and "Historical calls". The settings overlay is a centered modal. The following design integrates new features while respecting the existing layout's constraints — particularly the right panel width (22%) which limits how much tab content can fit.

### Dashboard Evolution

The existing three-panel dashboard evolves to accommodate new features:

- **Left Panel**: Campaign selector remains at the top. Below it, an "Inbound" status indicator shows the configured inbound number and whether it's active (green dot) or inactive (gray dot). The phone number input section is **conditionally shown** — when "Call over Internet" is enabled in settings, the two phone number fields and contact name inputs are replaced with a "Generate Call Link" button and the resulting copyable link card + QR code. The campaign creation/edit form includes an optional "Escalation number" field (overrides the global default from settings).
- **Center Panel**: Gains an "Inbound" badge on inbound calls and an "Internet" badge on internet-based calls (replacing the phone number in the call status bar). Knowledge source attribution appears inline in transcript chat bubbles as a small document icon tooltip with the source name. The center panel also hosts the **Knowledge Base Manager**, **Intent Library**, **Quality Dashboard**, and **Agent Activity** views — accessible via a top navigation bar or header links (not crammed into the right panel tabs, which are too narrow for these data-dense views).
- **Right Panel**: Keeps the existing 2 tabs ("Live" and "History") plus adds one new tab: **"Cases"**. Cases are a compact list view (status badge, title, priority dot) that fits the narrow panel. Agent Activity and Quality Dashboard are too data-dense for the 22%-width right panel and are shown in the center panel instead.

### New Views (Center Panel)

1. **Knowledge Base Manager** (center panel view, accessible from header nav or settings link): Drag-and-drop upload area at top, document list below with processing status indicators (spinner for Processing, checkmark for Indexed, red X for Failed), search preview panel at bottom to test queries against indexed content.
2. **Intent Library** (center panel view, accessible from header nav): Table of discovered intents with columns: Name, Category, Frequency, Status (Pending/Approved/Discarded), Linked Articles. Bulk approve/discard actions. Search/filter bar.
3. **Quality Dashboard** (center panel view, accessible from header nav): Score trend line chart (7/30/90 day views), average score display, flagged calls queue with severity sort, criteria configuration section below.
4. **Agent Activity** (center panel view, accessible from header nav): Chronological feed of recent agent actions (e.g., "Case Management Agent created case #12 from call #45"). Red highlighting for failures. Filter by agent type dropdown at top.

### Right Panel Addition

5. **Cases Tab** (right panel, 3rd tab alongside Live and History): Compact case list sorted by last update. Each case row shows: status dot (color-coded: blue=Open, orange=InProgress, green=Resolved, gray=Closed), truncated title, priority indicator (Low/Med/High/Crit). Clicking a case opens the full case detail in the center panel (same pattern as clicking a history item).

### Internet Calling UX Flow

1. Operator enables "Call over Internet" checkbox in Settings.
2. On the call panel (left panel), the phone number input fields disappear. A "Generate Call Link" button appears instead.
3. Clicking it generates a unique URL (expires in 15 min, single-use) displayed in a copyable card with a QR code.
4. The operator shares the link via any external channel (email, chat, SMS — the system doesn't send it).
5. When the remote user joins, the dashboard behaves identically to a phone call — same transcript panel, sentiment indicators, recording controls.
6. A lightweight **"Join Call" page** loads for the remote user — minimal branded UI with:
   - Caller identification form: name (required) + email or phone (optional, used for case matching)
   - "Join Call" button (prominent, below the form)
   - Microphone permission prompt on click
   - Simple real-time transcript view (read-only) once connected
   - No login required

### Settings Additions

- **Call over Internet** (checkbox): "Enable internet-based calling instead of phone network. When enabled, calls are made via shareable links instead of phone numbers." Visual indicator showing current mode (phone icon or globe icon in the header bar).
- **Inbound Configuration**: Configured inbound phone number (read-only display), default inbound campaign selector dropdown.
- **Quality Criteria**: Editable list of evaluation criteria — each with: name, description, weight (1-10), minimum passing score (1-5). Add/remove/reorder.
- **Campaign Data Restriction**: Per-campaign checkbox: "Restrict AI to provided data only" — prevents the AI from using general knowledge, only knowledge base content.
- **Knowledge Base**: Quick-access link to Knowledge Base Manager.
- **Escalation**: Default escalation phone number field (E.164 format, e.g., +1234567890). Dashboard notification is always enabled for escalations regardless of whether a number is configured. When "Call over Internet" is enabled, phone-based escalation is unavailable — escalation instead sends a real-time notification to the dashboard and the operator clicks "Join Escalation" to be added to the live call via WebRTC.

---

## Edge Cases

- **Large document upload**: Documents exceeding 50 MB are rejected with a clear error message before upload begins.
- **Unsupported file format**: Only PDF, DOCX, and TXT are accepted; other formats show a validation error.
- **Knowledge base search returns no results**: The AI falls back to its general knowledge and indicates it couldn't find specific documentation — unless the campaign is configured with a "restrict to provided data only" flag, in which case the AI states it cannot answer that question and suggests escalation.
- **Azure AI Search unavailable during call**: If the search service is temporarily unreachable, the AI continues using general knowledge (for unrestricted campaigns) or declines to answer (for data-restricted campaigns). The failure is logged in the agent activity log.
- **Concurrent inbound + outbound at limit**: When 5 calls are active, additional inbound callers hear the hold message from OperatorSettings (default: "All agents are currently busy. Please try again later.").
- **WebRTC browser compatibility**: If the browser doesn't support WebRTC (getUserMedia), the join page shows a compatibility error with supported browser suggestions.
- **Caller hangs up during intent identification**: The system saves the partial transcript and any partially detected intent for future analysis.
- **Case deduplication**: If the same caller calls about the same intent within 24 hours, the system updates the existing case rather than creating a new one.
- **Quality evaluation of very short calls** (under 10 seconds): Calls shorter than 10 seconds are marked as "Too Short for Evaluation" and not scored.
- **WebRTC NAT traversal failure**: If peer connection cannot be established, the system shows a "Connection failed — try a different network" message.
- **Expired or used WebRTC link**: If a user opens an expired or already-used call link, the join page shows "This link has expired or has already been used" with no option to join.
- **Document processing failure**: Failed documents show the error reason and offer a "Retry" button.
- **Escalation when no operator is online**: If no operator is connected to the dashboard when escalation is triggered, the AI informs the caller, offers to take a message, and creates a case record with "Escalation" priority.
- **Escalation during WebRTC calls**: The operator joins the WebRTC call via the same signaling infrastructure; the AI verbal handoff and disconnect behavior is identical to ACS calls.
- **No escalation number configured**: If neither the campaign nor settings has an escalation phone number, the system cannot perform a live phone transfer. Instead, the AI informs the caller that a human will follow up, creates a case marked as "Escalation" priority, and sends a dashboard notification with a callback request.
