# Feature Specification: Outbound Call Center POC

**Feature Branch**: `001-outbound-callcenter-poc`  
**Created**: 2026-02-20  
**Updated**: 2026-02-21  
**Status**: Draft  
**Input**: User description: "A simple solution to simulate an outbound call center using Microsoft Azure services. An operator uses a web interface to place an outbound phone call to a real phone number. Once the call is connected, an AI-powered virtual agent conducts the conversation in real time using voice, following a user-defined script/prompt."

## Clarifications

### Session 2026-02-20

- Q: What does the operator see/do while a call is in progress? → A: Status indicator + hang-up button + live text transcript of the conversation.
- Q: Should the system notify the recipient that the call is being recorded? → A: No; recording consent/notification is out of scope for this POC.
- Q: Should there be a maximum call duration? → A: Yes, 5-minute maximum per call; system auto-terminates after 5 minutes.
- Q: Should the system enforce a concurrent call limit? → A: Yes, hard cap of 5 concurrent calls; new requests are rejected when 5 calls are active.

### Session 2026-02-21

- Q: Should the operator be able to call multiple numbers at once? → A: Yes, support up to 2 phone numbers per initiation. Calls are placed simultaneously with the same campaign/prompt.
- Q: Should prompt scenarios be replaced with a richer "campaign" concept? → A: Yes. A campaign has a title, description, and AI behavior instructions. 4 pre-defined campaigns ship by default, and the operator can create additional custom campaigns at runtime.
- Q: Should there be real-time sentiment analysis during calls? → A: Yes. Analyze each transcript segment for sentiment (positive/neutral/negative) in real time and display it alongside the live transcript.
- Q: Should there be a call history/review page? → A: Yes. A separate page that lists all previous calls with their transcripts, per-segment sentiment, speaker timeline, and overall call analytics. Transcript data is persisted (not just in-memory) for review after the call ends.
- Q: Where should transcripts be persisted? → A: Azure Table Storage or in-memory JSON files in Blob Storage alongside recordings — keep it POC-simple, no SQL database.
- Q: Should campaign creation be persisted? → A: Yes, campaigns persist across app restarts. Store in a JSON file in Blob Storage or local file for POC simplicity.
- Q: Should the UX be redesigned as a professional call operations center? → A: Yes. Three-panel layout: left panel for phone numbers + campaigns (with "New Campaign" button), center panel for live transcript + live sentiment graph, right panel for call history. Single-page dashboard with full-viewport height and independently scrollable panels. Call history moves from a separate page into the right panel.
- Q: Should the default campaigns be expanded with realistic outbound scenarios? → A: Yes. Replace the 4 generic defaults with 6 outbound-focused campaigns: Bank Loan Collection, New Product Marketing, Customer Satisfaction Survey, Appointment Reminder, Insurance Policy Renewal, and Subscription Renewal & Upsell. Each should have detailed, production-quality AI behavior instructions.
- Q: Should the dashboard UX be redesigned with a more professional, enterprise-grade look? → A: Yes. Redesign inspired by professional banking/financial services dashboards — deep navy theme, branded header, rich campaign cards with colored category badges, professional call controls (End Call, Mute, Hold), call timer, quick response suggestions, and KPI summary cards.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Initiate an Outbound AI Call (Priority: P1)

An operator opens the web application, enters one or two destination phone numbers and selects a campaign (or writes a custom prompt) that describes how the AI agent should behave during the call. The operator clicks "Make a Call." The system places outbound phone calls to all specified numbers simultaneously. When each recipient picks up, an independent AI virtual agent greets them and conducts a real-time voice conversation according to the campaign instructions.

**Why this priority**: This is the core value proposition of the POC — proving that an AI agent can autonomously conduct live phone conversations driven by a campaign configuration.

**Independent Test**: Can be fully tested by entering one or two valid phone numbers and selecting a campaign, clicking "Make a Call," answering on real phones, and verifying the AI speaks and responds in real time on each call independently.

**Acceptance Scenarios**:

1. **Given** the operator is on the home page, **When** they enter one valid international phone number (e.g., +6591234567) and select a campaign, then click "Make a Call," **Then** the system places an outbound call to that number and the operator sees a success confirmation.
2. **Given** the operator enters two valid phone numbers, **When** they click "Make a Call," **Then** the system places two simultaneous outbound calls, each with its own independent AI session using the same campaign prompt.
3. **Given** the outbound call is answered by the recipient, **When** the call connects, **Then** the AI virtual agent begins speaking according to the campaign prompt and responds to the recipient's voice in real time.
4. **Given** the operator enters an invalid phone number (missing country code, wrong format), **When** they click "Make a Call," **Then** the system displays a validation error and does not place any calls.
5. **Given** the outbound call is not answered or the number is unreachable, **When** the call attempt times out, **Then** the system returns an error status to the operator for that specific call (the other call, if any, continues independently).
6. **Given** one or more calls are in progress, **When** the operator views the page, **Then** they see a call panel for each active call showing: status indicator, live transcript with sentiment badges, and a "Hang Up" button.
7. **Given** a call is in progress, **When** the operator clicks "Hang Up" on a specific call, **Then** the system terminates that call, stops recording, cleans up resources, and updates the status to "Disconnected" — the other call (if any) continues independently.

---

### User Story 2 - Campaign Management (Priority: P2)

The operator can select from pre-defined campaigns or create custom campaigns. A campaign defines: a title, a description (what the campaign is about), and AI behavior instructions (the system prompt for the AI agent). The system ships with 4 pre-defined campaigns. The operator can also create new campaigns via a simple form, and custom campaigns persist across sessions. Selecting a campaign sets the AI prompt for the call.

**Why this priority**: Campaigns replace the ad-hoc prompt scenarios from the initial POC with a structured, reusable, and persistent concept that better represents real call center operations. This makes the POC immediately demonstrable and extensible.

**Independent Test**: Can be tested by selecting each pre-defined campaign, verifying the AI uses the correct behavior. Create a new custom campaign, verify it appears in the list and persists after page refresh. Initiate a call with a custom campaign and confirm the AI follows the instructions.

**Acceptance Scenarios**:

1. **Given** the operator is on the home page, **When** they view the campaign selector, **Then** they see 6 pre-defined outbound campaigns: "Bank Loan Collection," "New Product Marketing," "Customer Satisfaction Survey," "Appointment Reminder," "Insurance Policy Renewal," and "Subscription Renewal & Upsell."
2. **Given** the operator selects a campaign, **When** they click "Make a Call," **Then** the AI agent uses the campaign's AI behavior instructions as its system prompt.
3. **Given** the operator wants to create a new campaign, **When** they click "Create Campaign" and fill in the title, description, and AI behavior instructions, **Then** the new campaign is saved and appears in the campaign selector.
4. **Given** the operator created a custom campaign previously, **When** they reload the page, **Then** the custom campaign is still available in the campaign list (persisted).
5. **Given** a campaign is selected, **When** the operator also types in the prompt textarea, **Then** the typed text overrides the campaign's AI instructions for this call only (the campaign is not modified).

**Default Outbound Campaigns** (6 pre-defined):

| # | Title | Category Badge | Description | AI Behavior Summary |
|---|-------|---------------|-------------|---------------------|
| 1 | Bank Loan Collection | Collections (amber) | Follow up on overdue loan payments and negotiate repayment plans | Professional but firm. References outstanding balance, discusses payment arrangements, offers flexible repayment plans, records payment commitments. Handles objections empathetically. |
| 2 | New Product Marketing | Marketing (blue) | Introduce new products or services to potential customers and generate leads | Enthusiastic and informative. Highlights key benefits and differentiators, answers pricing and feature questions, gauges interest level, offers to schedule a follow-up or send materials. |
| 3 | Customer Satisfaction Survey | Survey (green) | Conduct post-service satisfaction surveys and gather structured feedback | Friendly and structured. Asks scaled rating questions (1–5), captures verbatim feedback on specific service aspects, thanks the customer, and summarizes key findings. |
| 4 | Appointment Reminder | Reminder (purple) | Remind customers of upcoming appointments, deadlines, or scheduled events | Warm and helpful. Confirms date, time, and location; offers to reschedule if the customer cannot attend; provides preparation instructions; sends a verbal confirmation summary. |
| 5 | Insurance Policy Renewal | Renewal (teal) | Contact customers about expiring insurance policies and present renewal options | Knowledgeable and consultative. Reviews current coverage details, presents renewal options and any premium changes, answers coverage questions, and helps initiate the renewal process. |
| 6 | Subscription Renewal & Upsell | Upsell (orange) | Follow up on expiring subscriptions with renewal pricing and premium upgrade offers | Conversational and persuasive. Confirms satisfaction with current service, presents renewal pricing, introduces premium tier features and benefits, and processes renewal decisions. |

---

### User Story 3 - Real-Time Bidirectional Voice Conversation (Priority: P1)

Once the call is connected, the system streams audio bidirectionally in real time: the recipient's voice is captured, transcribed, and sent to the AI model; the AI model generates a spoken response that is streamed back to the recipient. The conversation supports natural turn-taking with voice-activity detection, including barge-in (the recipient can interrupt the AI mid-sentence).

**Why this priority**: Without real-time bidirectional audio the POC has no value — this is the technical heart of the system.

**Independent Test**: Can be tested by placing a call, speaking to the AI, verifying it responds contextually, and confirming that interrupting the AI mid-sentence stops its current audio and lets it respond to the new input.

**Acceptance Scenarios**:

1. **Given** a call is connected, **When** the recipient speaks, **Then** their audio is streamed to the AI and the AI generates a contextual voice response within a natural conversational pause.
2. **Given** the AI is speaking, **When** the recipient interrupts (barge-in), **Then** the AI stops its current audio output and begins processing the new input.
3. **Given** the call is connected, **When** neither party speaks for an extended silence, **Then** the system maintains the connection without crashing or disconnecting prematurely.

---

### User Story 4 - Call Recording (Priority: P3)

When a call connects, the system automatically starts recording the conversation. When the call disconnects, the recording stops and is stored for later review.

**Why this priority**: Recording is valuable for post-call analysis and quality assurance but is not required for the core demonstration of AI-driven outbound calling.

**Independent Test**: Can be tested by placing a call, hanging up, and verifying that a recording file was stored.

**Acceptance Scenarios**:

1. **Given** a call has just connected, **When** the CallConnected event is received, **Then** the system starts audio recording automatically.
2. **Given** a call is being recorded, **When** the call disconnects, **Then** the recording stops and the audio file is persisted to storage.

---

### User Story 5 - Real-Time Sentiment Analysis (Priority: P2)

While a call is in progress, the system analyzes each transcript segment for sentiment (positive, neutral, negative) in real time. Sentiment indicators are displayed alongside each transcript entry in the live call panel, giving the operator immediate visibility into whether the conversation is going well or poorly.

**Why this priority**: Real-time sentiment transforms the operator from a passive observer into someone who can gauge call quality at a glance, which is essential for a call center demonstration.

**Independent Test**: Can be tested by placing a call, speaking with varying tones (happy, frustrated, neutral), and verifying that sentiment badges appear next to each transcript entry in real time.

**Acceptance Scenarios**:

1. **Given** a call is in progress and a transcript entry is received, **When** the system processes the text, **Then** a sentiment label (positive/neutral/negative) is assigned and displayed as a colored badge next to the transcript entry.
2. **Given** the recipient expresses frustration (e.g., "This is terrible, I want to cancel"), **When** the sentiment is analyzed, **Then** the entry is labeled as "Negative" with a red indicator.
3. **Given** the AI agent is speaking, **When** its transcript is analyzed, **Then** sentiment is also labeled for the AI's speech (useful for verifying the AI maintains a professional tone).
4. **Given** multiple transcript entries with mixed sentiments, **When** the operator views the live transcript, **Then** they can see the sentiment trend at a glance through color-coded badges (green=positive, gray=neutral, red=negative).

---

### User Story 6 - Simultaneous Multi-Number Calling (Priority: P2)

The operator can enter up to 2 phone numbers in a single call initiation. The system places all calls simultaneously, each with its own independent AI session, media WebSocket, and transcript stream. The call panel shows each call side by side so the operator can monitor both conversations at once.

**Why this priority**: Multi-number calling demonstrates batch outreach capability — a key differentiator for call center use cases like surveys, reminders, and collections campaigns.

**Independent Test**: Can be tested by entering two phone numbers, clicking "Make a Call," answering both phones, and verifying that each call has an independent AI conversation and transcript. Hanging up one call should not affect the other.

**Acceptance Scenarios**:

1. **Given** the operator enters 2 phone numbers, **When** they click "Make a Call," **Then** the system initiates 2 independent outbound calls simultaneously, each shown in its own call panel.
2. **Given** both calls are connected, **When** the operator views the page, **Then** they see two side-by-side call panels, each with its own status, transcript, sentiment, and hang-up button.
3. **Given** one of the two calls is disconnected by the recipient, **When** the operator views the page, **Then** the disconnected call shows "Disconnected" while the other call continues normally.
4. **Given** the operator enters only 1 phone number, **When** they click "Make a Call," **Then** the system initiates a single call as before (backward compatible).
5. **Given** the concurrent call limit would be exceeded by the new calls, **When** the operator clicks "Make a Call," **Then** the system rejects the request with a clear message indicating how many slots are available.

---

### User Story 7 - Call History & Analytics Review (Priority: P2)

The operator can navigate to a "Call History" page that displays a list of all previous calls. Each call entry shows: the phone number(s) called, the campaign used, call duration, overall sentiment score, and timestamp. Clicking a call entry expands it to show: the full transcript with per-segment sentiment badges, a speaker timeline visualization (showing who spoke when and for how long), and aggregate analytics (e.g., overall sentiment breakdown, talk-time ratio AI vs. recipient).

**Why this priority**: Post-call review and analytics close the loop on the call center workflow, enabling quality assurance, campaign effectiveness assessment, and AI behavior tuning.

**Independent Test**: Can be tested by making a few calls, navigating to the Call History page, verifying all calls appear with correct metadata, and clicking into a call to see the full transcript with sentiment and speaker timeline.

**Acceptance Scenarios**:

1. **Given** one or more calls have been completed, **When** the operator views the call history (right panel of the dashboard, or dedicated page if US8 is not yet implemented), **Then** they see a list of all previous calls with: phone number(s), campaign name, duration, overall sentiment, and timestamp.
2. **Given** the operator clicks on a call entry in the history, **When** the detail view expands, **Then** they see the full transcript with per-entry sentiment badges (green/gray/red) and speaker labels (AI/Recipient).
3. **Given** the operator views a call detail, **When** they look at the analytics section, **Then** they see: (a) overall sentiment breakdown (% positive/neutral/negative), (b) talk-time ratio (AI vs. recipient), (c) speaker timeline showing who spoke when during the call.
4. **Given** the operator has refreshed the page or restarted the application, **When** they view call history, **Then** all previous calls are still listed (data is persisted to storage, not just in-memory).
5. **Given** there are many calls in the history, **When** the operator views the list, **Then** calls are sorted by most recent first and the list is scrollable.

---

### User Story 8 - Professional Operations Center Dashboard (Priority: P2)

The web application is redesigned as a professional, enterprise-grade call operations center dashboard inspired by modern banking and financial services dashboards. The interface features a **deep navy/slate branded header** with the application name ("Contact Center Operations"), a subtitle ("AI-Powered Outbound Calling Platform"), and a right-aligned "Operations Dashboard" status badge with a live status dot. Below the header, the layout presents a **three-panel workspace** at full viewport height with independently scrollable panels.

**Left Panel — "CAMPAIGN LIBRARY"** (~22% width): Displays campaign cards as rich, visually distinct tiles — each card shows the campaign title in bold, a colored category badge (Collections=amber, Marketing=blue, Survey=green, Reminder=purple, Renewal=teal, Upsell=orange), and a short description. The panel includes a search/filter bar with a magnifying glass icon to find campaigns quickly. At the bottom of the campaign list, a "Create Campaign" button opens an inline creation form. The selected campaign is highlighted with a distinct border/accent. Below the campaign section, phone number input fields (1 and 2) with E.164 format hints and a prominent "Start Call" button complete the call setup flow. A "Prompt Override" textarea (collapsed by default) allows custom instructions.

**Center Panel — "LIVE CALL"** (~56% width): The primary operations workspace.

- **Idle State**: Displays a professional welcome screen with the application logo/icon, a brief instruction message ("Select a campaign and enter a phone number to begin"), and **KPI summary cards**: Calls Today (count), Average Duration (MM:SS), Overall Sentiment (gauge or percentage), Success Rate (%). The KPIs update in real time as calls complete.

- **Active Call State**: Transforms into a full call operations workspace:
  1. **Status Bar** (top): Call status badge (Connecting → Connected → Disconnected, color-coded), target phone number, campaign name badge (colored to match the campaign category), and a **live call timer** (MM:SS counting up from 00:00).
  2. **Call Controls Bar**: Professional icon buttons — **End Call** (red circular button with phone-down icon), **Mute** (gray toggle button with microphone/mic-off icon), **Hold** (gray toggle button with pause icon). Visual **microphone level indicator** (animated bar) and **volume slider** with mute toggle.
  3. **Live Transcript Area**: Scrollable chat-bubble style conversation feed. AI messages appear on the left with a bot icon and light blue background. Recipient messages appear on the right with a person icon and white/light background. Each message shows: speaker label, timestamp, and a **color-coded sentiment dot** (green=positive, gray=neutral, red=negative). Auto-scrolls to the latest message.
  4. **Live Sentiment Graph**: Rolling line chart below the transcript area. X-axis = entry index or elapsed time. Y-axis = sentiment value (Positive=+1, Neutral=0, Negative=−1). Green line segments for positive, gray for neutral, red for negative. Auto-scrolls as new entries arrive.
  5. **Quick Responses Panel** (collapsible section below the sentiment graph): Pre-built response suggestions relevant to the active campaign — displayed as clickable cards with an icon and quoted text. Examples for a Collections campaign: "I understand your concern, let me review your account," "We can set up a flexible payment plan," "Your payment is due by [date]." The operator can click a suggestion to copy or reference it. Campaign-specific suggestions are defined per campaign.

- **Multi-Call View**: When 2 calls are active simultaneously, the center panel shows **tabs** at the top — one per call, labeled with the phone number and campaign badge. Each tab has its own complete call workspace (status bar, controls, transcript, sentiment graph). Tab switching is instant without losing transcript state. A disconnected call's tab shows a "Disconnected" overlay and can be dismissed.

**Right Panel — "CALL ANALYTICS & HISTORY"** (~22% width):

- **Active Call Details** (top section, visible when a call is active): A contact information card showing the phone number, campaign name and category badge, call duration (updating live), and a truncated summary of the AI behavior instructions for the active campaign.
- **KPI Dashboard** (top section, visible when idle): Quick stats — calls completed today, average sentiment score, average duration, last call timestamp.
- **Call History List** (scrollable, always visible): Compact call record cards sorted by most recent first. Each card shows: phone number(s), campaign badge (colored), duration, overall sentiment indicator (colored dot), and timestamp. Clicking a card expands it inline to reveal: full transcript with per-entry sentiment badges, sentiment breakdown bars (% positive/neutral/negative), talk-time ratio bar (AI vs. Recipient), and speaker timeline.

**Visual Design System**:
- **Header**: Deep navy (#1a1a2e) or dark slate (#0f172a) background, white text, subtle gradient or border accent
- **Left/Right Panels**: Dark sidebar (#1e293b) with lighter card backgrounds (#334155), white text
- **Center Panel**: Light background (#f8fafc) for readability, dark text
- **Accent Colors**: Campaign category badges use distinct colors (amber, blue, green, purple, teal, orange). Sentiment colors: green (#22c55e), gray (#94a3b8), red (#ef4444). Call status: green=connected, amber=connecting, red=disconnected
- **Typography**: Professional sans-serif (Inter, Segoe UI, or system font stack), clear hierarchy with size/weight variations
- **Components**: Rounded corners (8px), subtle shadows, smooth transitions (200ms), hover states on interactive elements

**Responsive Behavior**: On narrow screens (<992px), the three panels collapse into a **tab-based navigation** with three tabs: "Campaigns," "Live Call," "History." The active tab fills the viewport. Tab transitions use smooth slide animations.

**Why this priority**: A professional, enterprise-grade operations center layout transforms the POC from a developer prototype into a credible call center demonstration. The visual quality and interaction design directly impact stakeholder perception and adoption confidence.

**Independent Test**: Open the application, verify the deep navy branded header and three-panel layout are visible. Verify campaign cards have colored category badges and the search bar filters them. Initiate a call and verify: call controls bar appears with End Call/Mute/Hold buttons, call timer counts up, transcript renders as chat bubbles with sentiment dots, sentiment graph updates in real time. While the call is active, verify the right panel shows active call details at top and call history below. After the call ends, verify the new call appears in the right panel history and KPIs update. Create a new campaign from the left panel and verify it appears with the correct category badge.

**Acceptance Scenarios**:

1. **Given** the operator opens the application, **When** the page loads, **Then** they see: (a) a deep navy branded header with "Contact Center Operations" title and "Operations Dashboard" badge, (b) a three-panel layout with campaign library on the left, live call workspace in the center, and call analytics/history on the right.
2. **Given** no call is active, **When** the operator views the center panel, **Then** it shows a professional idle state with the application logo, an instruction message, and KPI summary cards (Calls Today, Average Duration, Overall Sentiment, Success Rate).
3. **Given** the operator views the left panel, **When** they look at the campaign list, **Then** each campaign is displayed as a rich card with a bold title, colored category badge (e.g., "Collections" in amber, "Marketing" in blue), and a short description. The currently selected campaign has a highlighted border.
4. **Given** the operator types in the campaign search bar, **When** they enter a partial campaign title, **Then** the campaign list filters to show only matching campaigns.
5. **Given** the operator initiates a call, **When** the call connects, **Then** the center panel transforms to show: a status bar with "Connected" badge (green), phone number, campaign badge, and a live timer counting up from 00:00.
6. **Given** a call is active, **When** the operator views the call controls bar, **Then** they see End Call (red), Mute (gray toggle), and Hold (gray toggle) buttons with icons, plus a microphone level indicator.
7. **Given** a call is in progress and transcript entries arrive, **When** the operator views the live transcript, **Then** entries render as chat bubbles — AI messages on the left with a bot icon and light blue background, recipient messages on the right with a person icon — each with a timestamp and color-coded sentiment dot.
8. **Given** the operator clicks the Mute button, **When** the button is toggled, **Then** the button changes to a "mic-off" icon style and the microphone level indicator goes silent. Clicking again restores the original state.
9. **Given** a call is active, **When** the operator views the Quick Responses panel, **Then** they see pre-built response suggestions specific to the active campaign (e.g., for Bank Loan Collection: "We can set up a flexible payment plan").
10. **Given** a call is in progress, **When** the operator views the right panel, **Then** the top section shows a contact info card with the phone number, campaign name/badge, and live duration, while the bottom section still shows the call history list.
11. **Given** two calls are active simultaneously, **When** the operator views the center panel, **Then** they see tabs at the top (one per call with phone number and campaign badge) and can switch between call workspaces instantly.
12. **Given** the operator clicks a call entry in the right panel history, **When** the detail expands, **Then** they see the full transcript with sentiment badges, sentiment breakdown bars, talk-time ratio bar, and speaker timeline.
13. **Given** the application is on a narrow screen (<992px), **When** the viewport is too small for three panels, **Then** the layout switches to tab-based navigation with "Campaigns," "Live Call," and "History" tabs.

---

### Edge Cases

- What happens when the operator submits a phone number without a country code prefix? The system should attempt to normalize it (prepend "+") or reject it with a clear validation message.
- What happens when the AI service (OpenAI Realtime) is unavailable or returns an error mid-call? The system should log the error and gracefully end the call rather than leaving a silent open line.
- What happens when the WebSocket connection between the telephony service and the API drops during a call? The system should detect the disconnection and clean up resources (stop recording, release the call connection).
- What happens when the operator initiates a second call while another is still active? The system should handle concurrent calls independently (each call gets its own WebSocket and AI session).
- What happens when the prompt text is empty? The system should fall back to a default system prompt.
- What happens when a call reaches the 5-minute maximum duration? The system should auto-terminate the call gracefully, notify the operator, stop recording, and clean up all resources.
- What happens when the operator tries to start a 6th concurrent call? The system should reject the request immediately with a message indicating the concurrent call limit has been reached.
- What happens when the operator enters 2 phone numbers but one is invalid? The system should reject the entire request with a validation error — do not partially initiate calls.
- What happens when sentiment analysis fails or returns inconclusive results? The system should default to "neutral" sentiment and log the issue without disrupting the call.
- What happens when the operator creates a campaign with a duplicate title? The system should reject it with a clear validation message.
- What happens when call history data is missing from storage (e.g., blob deleted)? The system should show "No history available" rather than crashing.
- What happens when both simultaneous calls are placed to the same phone number? The system should allow it — each call is independent.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: The system MUST provide a web-based interface where an operator enters one or two destination phone numbers and selects a campaign, then initiates outbound calls with a single action.
- **FR-002**: The system MUST validate phone numbers against the E.164 international format (e.g., +6591234567) before attempting to place a call.
- **FR-003**: The system MUST place outbound phone calls from a provisioned caller number to the specified destination number(s) using a cloud telephony service.
- **FR-004**: The system MUST establish a real-time bidirectional audio stream between each phone call and its own AI model session once the call is connected.
- **FR-005**: The AI virtual agent MUST conduct the conversation following the campaign's AI behavior instructions provided at call initiation time.
- **FR-006**: The system MUST support voice-activity detection with barge-in — if the recipient starts speaking while the AI is outputting audio, the AI output MUST stop and the system MUST process the new input.
- **FR-007**: The system MUST provide a set of at least 6 pre-defined outbound campaigns with realistic business scenarios (Bank Loan Collection, New Product Marketing, Customer Satisfaction Survey, Appointment Reminder, Insurance Policy Renewal, Subscription Renewal & Upsell) that the operator can select from to quickly configure the AI agent's behavior. Each campaign MUST have detailed, production-quality AI behavior instructions that produce distinct and contextually appropriate conversations.
- **FR-008**: The operator MUST be able to create custom campaigns with a title, description, and AI behavior instructions, and these MUST persist across sessions.
- **FR-009**: The system MUST automatically start recording the call audio when the call connects and stop recording when the call disconnects.
- **FR-010**: The system MUST persist call recordings to cloud storage for later retrieval.
- **FR-011**: The system MUST clean up all active resources (audio streams, AI sessions, call connections, recordings) when a call ends, whether terminated by the recipient, the system, or an error.
- **FR-012**: The system MUST display a success or failure status to the operator after a call initiation attempt.
- **FR-013**: The system MUST log significant call lifecycle events (call initiated, connected, disconnected, errors) for debugging and observability.
- **FR-014**: While a call is in progress, the system MUST display to the operator: (a) a call status indicator, (b) a live text transcript showing both the AI agent's and the recipient's speech with per-segment sentiment badges, and (c) a "Hang Up" button that terminates the call immediately.
- **FR-015**: When the operator clicks "Hang Up," the system MUST terminate the active call, stop recording, and clean up all associated resources.
- **FR-016**: The system MUST enforce a maximum call duration of 5 minutes. When the limit is reached, the system MUST automatically terminate the call, stop recording, and clean up resources.
- **FR-017**: The system MUST enforce a maximum of 5 concurrent active calls. If an operator attempts to initiate calls that would exceed the limit, the system MUST reject the request with a clear message.
- **FR-018**: The system MUST support initiating up to 2 simultaneous outbound calls in a single action, each with an independent AI session and media stream.
- **FR-019**: The system MUST analyze each transcript segment for sentiment (positive, neutral, negative) in real time and stream the sentiment alongside the transcript to the operator.
- **FR-020**: The system MUST persist call transcripts with sentiment data to storage so they can be reviewed after the call ends.
- **FR-021**: The system MUST provide a call history view listing all previous calls with metadata (phone number(s), campaign, duration, overall sentiment, timestamp). *(Note: After US8, this is fulfilled by the right panel of the dashboard rather than a separate page.)*
- **FR-022**: The system MUST allow the operator to view a detailed call review including: full transcript with per-segment sentiment, speaker timeline, and aggregate analytics (sentiment breakdown, talk-time ratio).
- **FR-023**: The system MUST allow the operator to create, list, and select campaigns. Custom campaigns MUST persist across application restarts.
- **FR-024**: When multiple calls are active simultaneously, the system MUST display them in separate call panels so the operator can monitor each conversation independently.
- **FR-025**: The system MUST present a three-panel operations center dashboard layout: left panel for call controls and campaign management, center panel for live call operations (transcript + sentiment graph), and right panel for call history.
- **FR-026**: The center panel MUST display a live sentiment analysis graph that updates in real time as new transcript entries arrive during a call, showing the sentiment trend over the duration of the call.
- **FR-027**: The right panel MUST display the call history list inline (not on a separate page), with expandable entries for full call detail including transcript, sentiment breakdown, and talk-time analytics.
- **FR-028**: The dashboard layout MUST use fixed full-viewport height with independently scrollable panels, so the operator can view all panels simultaneously during a call.
- **FR-029**: The dashboard layout MUST gracefully adapt to narrow viewports by stacking panels vertically or providing tab-based navigation.
- **FR-030**: The system MUST display a professional branded header bar with application name, subtitle, and a status indicator badge, using a deep navy/slate color scheme.
- **FR-031**: The left panel MUST display campaigns as rich visual cards with colored category badges (e.g., Collections=amber, Marketing=blue, Survey=green, Reminder=purple, Renewal=teal, Upsell=orange), campaign title, and short description. A search/filter bar MUST allow the operator to find campaigns quickly.
- **FR-032**: The center panel MUST display professional call controls during an active call: End Call button (red, phone-down icon), Mute toggle button (microphone icon), and Hold toggle button (pause icon), along with a visual microphone level indicator.
- **FR-033**: The center panel MUST display a live call timer (MM:SS format, counting up from 00:00) when a call is connected, along with the target phone number and campaign name badge in the status bar.
- **FR-034**: The center panel MUST display a collapsible "Quick Responses" panel during active calls, showing pre-built response suggestions relevant to the active campaign that the operator can reference or inject into the conversation.
- **FR-035**: The center panel idle state MUST display KPI summary cards (Calls Today, Average Duration, Overall Sentiment) and a professional welcome message with the application logo.
- **FR-036**: The live transcript MUST render in a chat-bubble style with distinct visual styling for AI messages vs. recipient messages, including speaker icons, timestamps, and color-coded sentiment dots (green=positive, gray=neutral, red=negative).

### Key Entities

- **Operator**: A human user who accesses the web application to configure and trigger outbound calls. Attributes: none persisted (no login required for this POC).
- **Call**: A single outbound phone call session. Attributes: call connection ID, target phone number, assigned campaign, status (initiated / connected / disconnected), timestamp.
- **Campaign**: A reusable call configuration defining the AI agent's behavior. Attributes: ID, title, description, AI behavior instructions, isDefault flag, creation timestamp.
- **AI Session**: A real-time conversational AI session connected to a call. Attributes: linked call connection ID, system prompt (from campaign), voice configuration, audio format.
- **Recording**: An audio capture of a call. Attributes: recording ID, linked server call ID, storage location.
- **TranscriptEntry**: A single segment of call transcript. Attributes: call connection ID, speaker (AI/Recipient), text, sentiment (positive/neutral/negative), timestamp.
- **CallRecord**: A persisted summary of a completed call for history review. Attributes: call connection ID, phone number(s), campaign ID/title, duration, overall sentiment, transcript entries, timestamp.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: An operator can go from opening the web app to hearing the AI agent speak on a real phone in under 60 seconds.
- **SC-002**: The AI agent responds to the recipient's speech within 2 seconds of the recipient finishing their utterance (natural conversational latency).
- **SC-003**: Barge-in works reliably — when the recipient interrupts, the AI stops speaking and acknowledges the new input in 100% of test attempts.
- **SC-004**: 100% of completed calls produce a persisted recording that can be retrieved from storage.
- **SC-005**: The system handles at least 5 concurrent outbound calls independently without cross-talk or resource conflicts, and rejects the 6th with a clear error.
- **SC-006**: All 6 pre-defined outbound campaigns produce distinctly different AI agent behaviors that match their scenario descriptions when tested with a real call.
- **SC-007**: Call initiation with an invalid phone number is rejected with a user-visible validation error in 100% of attempts.
- **SC-008**: When 2 phone numbers are entered, both calls are placed within 3 seconds of each other and operate independently.
- **SC-009**: Sentiment labels appear on transcript entries within 1 second of the transcript being received.
- **SC-010**: The call history view (dashboard right panel) loads and displays all previous calls within 3 seconds.
- **SC-011**: Custom campaigns created by the operator persist and are available after page refresh and application restart.
- **SC-012**: Call detail view in history shows speaker timeline and sentiment breakdown that accurately reflect the actual call.
- **SC-013**: The three-panel dashboard loads within 2 seconds and all panels are visible simultaneously without page scrolling.
- **SC-014**: During a live call, the sentiment graph in the center panel updates within 1 second of each new transcript entry.
- **SC-015**: The branded header, campaign cards with colored badges, and professional call controls are visually consistent with the deep navy/slate design system across all viewport sizes.
- **SC-016**: Call control buttons (End Call, Mute, Hold) respond within 500ms of operator click and provide clear visual feedback of their toggle state.
- **SC-017**: The live transcript renders in chat-bubble style with distinct AI vs. recipient styling, and each entry displays a sentiment dot within 1 second of the transcript being received.
- **SC-018**: KPI summary cards (Calls Today, Average Duration, Overall Sentiment) update in real time as calls complete.

## Assumptions

- This is a proof-of-concept; there is no user authentication, role-based access, or multi-tenancy.
- The operator uses a modern desktop browser (Chrome, Edge) — mobile support is not required.
- The system requires pre-provisioned cloud telephony resources (phone number, connection string) configured via application settings.
- The system requires pre-provisioned AI resources (endpoint, key, model deployment) configured via application settings.
- Call recordings are stored in a cloud blob container whose URL is configured via application settings.
- The API must be publicly accessible (e.g., via a tunnel or cloud deployment) so the telephony service can deliver callbacks and WebSocket connections.
- Only outbound calls are in scope; inbound call handling is not supported.
- Call recording consent/notification is out of scope for this POC. Recording starts silently. Compliance with local recording-consent laws is the deployer's responsibility.
- Sentiment analysis uses Azure OpenAI (GPT chat completion) for simplicity — a dedicated sentiment service like Azure AI Language could be used in production but is over-engineered for POC.
- Call history and campaign data are persisted to Azure Blob Storage as JSON files — no SQL database is required for POC scope.
- The maximum number of simultaneous phone numbers per call initiation is 2 (hardcoded for POC; extensible later).
- ACS call recordings are stored in Azure Blob Storage and can be downloaded via the ACS Call Recording API using the recording ID or content location.
- Contact names are optional metadata attached to calls — the AI uses the name for personalized greetings when available.

---

## Phase 15 Additions

### User Story 9: Call Recording Playback (Priority: P2)

**As an** operator,  
**I want to** listen to call recordings by selecting historical calls from the call history panel,  
**so that** I can review the actual audio of past conversations for quality assurance and training.

#### Acceptance Scenarios

| # | Given | When | Then |
|---|-------|------|------|
| 1 | A completed call exists in call history with a recording | The operator clicks on the call in the right panel history list | The call detail view shows an audio player with playback controls |
| 2 | A completed call has a recording available | The operator clicks the play button on the audio player | The call recording audio plays in the browser |
| 3 | A completed call has no recording (recording failed or was not started) | The operator views the call detail | A "No recording available" message is shown instead of the audio player |
| 4 | The operator is playing a recording | The operator uses standard audio controls (play/pause, seek, volume) | The controls work as expected with the MP3 audio |

### User Story 10: Contact Name for Personalized Calls (Priority: P2)

**As an** operator,  
**I want to** optionally specify a contact name alongside each phone number before placing a call,  
**so that** the AI agent greets the recipient by name and the conversation feels more natural and personal.

#### Acceptance Scenarios

| # | Given | When | Then |
|---|-------|------|------|
| 1 | The operator enters a phone number and a contact name | The call is initiated | The AI agent greets the contact by name (e.g., "Hello John, ...") |
| 2 | The operator enters a phone number without a contact name | The call is initiated | The AI agent uses a generic greeting without a name |
| 3 | The operator enters 2 phone numbers, each with optional names | The calls are initiated | Each call's AI uses the respective contact name in its greeting |
| 4 | A call with a contact name completes | The operator views call history | The contact name is displayed alongside the phone number in the history list and detail view |

### Updates to Existing User Stories

**US5 — Real-Time Sentiment Analysis (Bug Fix)**:
- Sentiment analysis was not functioning on the deployed Azure instance because the `AzureOpenAI:ChatDeployment` app setting was missing from the Azure App Service configuration.
- Fix: Ensure the `AzureOpenAI__ChatDeployment` app setting is configured on Azure with value `gpt-4o-mini`.
- Validate: After fix, sentiment dots on chat bubbles must update from "loading" state to colored dots (green/gray/red), and the sentiment timeline graph must show a moving line plot during active calls.

### Additional Functional Requirements

- **FR-037**: The system MUST persist the recording content location URL in the `CallRecord` when a call recording is available, so recordings can be retrieved for playback.
- **FR-038**: The system MUST provide an API endpoint to download/stream a call recording by call connection ID, proxying the recording content from ACS/Blob Storage.
- **FR-039**: The call detail view in the right panel MUST display an HTML5 `<audio>` player when a recording is available for the selected historical call.
- **FR-040**: The call initiation form MUST support an optional contact name field alongside each phone number input.
- **FR-041**: When a contact name is provided, the system MUST prepend a name-aware instruction to the AI's system prompt so the AI greets the recipient by name before proceeding with the campaign script.
- **FR-042**: The `CallRecord` MUST include the contact name (if provided) and display it in call history views.

### Additional Success Criteria

- **SC-019**: Recordings for completed calls are playable via the audio player in the call detail view within 3 seconds of clicking play.
- **SC-020**: When a contact name is provided, the AI agent uses the name in its greeting within the first sentence of the conversation in 100% of test attempts.
- **SC-021**: After configuring `AzureOpenAI__ChatDeployment` on Azure, sentiment dots appear on all new transcript entries during live calls, and the sentiment graph line moves in real time.
