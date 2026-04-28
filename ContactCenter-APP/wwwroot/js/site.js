// ═══════════════════════════════════════════════════════════
// Contact Center Operations — Professional Dashboard
// Rich campaign cards, call controls, chat-bubble transcript,
// KPI tracking, quick responses, sentiment graph
// ═══════════════════════════════════════════════════════════

(function () {
    "use strict";

    // ─── State ───────────────────────────────────────────────
    var connection = null;
    // callId -> { phoneNumber, campaignTitle, status, transcriptEntries, sentimentData, operatorEmotionData, customerEmotionData, timerInterval, timerSeconds }
    var calls = {};
    var activeTabId = null;
    var selectedCampaign = null; // { id, title, description, ... }
    var isMuted = false;
    var isOnHold = false;
    var micActivityTimeout = null;
    var promptManuallyEdited = false; // Track if user explicitly typed in prompt override

    // KPI state (persisted to sessionStorage)
    var kpiState = loadKpiState();

    var apiBaseUrl = function () {
        return (document.getElementById("apiBaseUrl") || {}).value || "";
    };

    // ═══════════════════════════════════════════════════════════
    // CAMPAIGN CATEGORY MAPPING
    // ═══════════════════════════════════════════════════════════

    var categoryMap = {
        "collection": { css: "category-collections", label: "Collections" },
        "marketing": { css: "category-marketing", label: "Marketing" },
        "survey": { css: "category-survey", label: "Survey" },
        "reminder": { css: "category-reminder", label: "Reminder" },
        "insurance": { css: "category-renewal", label: "Renewal" },
        "renewal": { css: "category-renewal", label: "Renewal" },
        "upsell": { css: "category-upsell", label: "Upsell" },
        "subscription": { css: "category-upsell", label: "Upsell" }
    };

    function getCampaignCategory(title) {
        var t = (title || "").toLowerCase();
        var keys = Object.keys(categoryMap);
        for (var i = 0; i < keys.length; i++) {
            if (t.indexOf(keys[i]) !== -1) return categoryMap[keys[i]];
        }
        return { css: "category-default", label: "General" };
    }

    // ═══════════════════════════════════════════════════════════
    // QUICK RESPONSES
    // ═══════════════════════════════════════════════════════════

    var quickResponsesMap = {
        "collection": [
            "I understand your concern, let me review your account.",
            "We can set up a flexible payment plan.",
            "Your next payment is due by the scheduled date.",
            "I can transfer you to a payment specialist."
        ],
        "marketing": [
            "Would you like to hear about the key benefits?",
            "I can send you a detailed brochure.",
            "We're offering a limited-time introductory price.",
            "Shall I schedule a product demo?"
        ],
        "survey": [
            "On a scale of 1 to 5, how would you rate...?",
            "Thank you for that feedback.",
            "Your responses help us improve.",
            "We appreciate your time today."
        ],
        "reminder": [
            "Your appointment is scheduled for the confirmed date and time.",
            "Would you like to reschedule?",
            "Please remember to bring the required items.",
            "I'll confirm your updated appointment."
        ],
        "renewal": [
            "Your current policy/subscription expires soon.",
            "I'd like to review your coverage options.",
            "We have some new features available this term.",
            "I can process that renewal for you right now."
        ],
        "upsell": [
            "Are you satisfied with your current plan?",
            "Our premium tier includes advanced features.",
            "We have a loyalty discount available.",
            "Would you like to upgrade today?"
        ]
    };
    // Alias
    quickResponsesMap["insurance"] = quickResponsesMap["renewal"];
    quickResponsesMap["subscription"] = quickResponsesMap["upsell"];

    function getQuickResponses(campaignTitle) {
        var t = (campaignTitle || "").toLowerCase();
        var keys = Object.keys(quickResponsesMap);
        for (var i = 0; i < keys.length; i++) {
            if (t.indexOf(keys[i]) !== -1) return quickResponsesMap[keys[i]];
        }
        return [
            "How can I help you today?",
            "Let me look into that for you.",
            "Is there anything else you need?",
            "Thank you for your time."
        ];
    }

    // ═══════════════════════════════════════════════════════════
    // LEFT PANEL — Campaign Cards & Call Initiation
    // ═══════════════════════════════════════════════════════════

    window.loadCampaigns = function () {
        var list = document.getElementById("campaignList");
        if (!list) return;

        // Show loading spinner
        list.innerHTML = '<div class="text-center py-3" id="campaignLoading"><div class="spinner-border spinner-border-sm" role="status"></div><span class="ms-2 small text-muted">Loading campaigns...</span></div>';

        fetch(apiBaseUrl() + "/api/Campaign")
            .then(function (resp) { return resp.json(); })
            .then(function (campaigns) {
                list.innerHTML = "";
                window._allCampaigns = campaigns;
                renderCampaignCards(campaigns, list);

                // Auto-select the first campaign if none is selected
                if (!selectedCampaign && campaigns.length > 0) {
                    selectCampaign(campaigns[0]);
                }
            })
            .catch(function (err) {
                list.innerHTML = '<div class="text-danger small py-2 text-center">Failed to load campaigns</div>';
                console.error("Error loading campaigns:", err);
            });
    };

    function renderCampaignCards(campaigns, container) {
        container.innerHTML = "";
        if (campaigns.length === 0) {
            container.innerHTML = '<div class="text-muted small text-center py-2">No campaigns found</div>';
            return;
        }
        campaigns.forEach(function (c) {
            var cat = getCampaignCategory(c.title);
            var card = document.createElement("div");
            card.className = "campaign-card" + (selectedCampaign && selectedCampaign.id === c.id ? " selected" : "");
            card.setAttribute("data-campaign-id", c.id);
            card.innerHTML =
                '<span class="category-badge ' + cat.css + '">' + escapeHtml(cat.label) + '</span>' +
                '<div class="campaign-card-title">' + escapeHtml(c.title) + '</div>' +
                '<div class="campaign-card-desc">' + escapeHtml(c.description) + '</div>';
            card.onclick = function () { selectCampaign(c); };
            container.appendChild(card);
        });
    }

    function selectCampaign(campaign) {
        selectedCampaign = campaign;
        document.getElementById("campaignId").value = campaign.id;

        // Update card selection
        document.querySelectorAll(".campaign-card").forEach(function (el) {
            el.classList.remove("selected");
            if (el.getAttribute("data-campaign-id") === campaign.id) {
                el.classList.add("selected");
            }
        });

        // Show campaign prompt in preview area
        var previewEl = document.getElementById("campaignPromptPreview");
        var previewText = document.getElementById("campaignPromptText");
        if (previewEl && previewText) {
            previewText.textContent = campaign.aiBehaviorInstructions || "";
            previewEl.classList.remove("d-none");
        }

        // Clear the prompt override textarea when selecting a campaign
        var promptEl = document.getElementById("prompt");
        if (promptEl) {
            promptEl.value = "";
            promptManuallyEdited = false;
        }
    }

    // Campaign search/filter
    function setupCampaignSearch() {
        var searchInput = document.getElementById("campaignSearch");
        if (!searchInput) return;
        searchInput.addEventListener("input", function () {
            var query = searchInput.value.toLowerCase().trim();
            var allCampaigns = window._allCampaigns || [];
            var filtered = allCampaigns;
            if (query) {
                filtered = allCampaigns.filter(function (c) {
                    return c.title.toLowerCase().indexOf(query) !== -1 ||
                           (c.description || "").toLowerCase().indexOf(query) !== -1;
                });
            }
            var list = document.getElementById("campaignList");
            renderCampaignCards(filtered, list);
        });
    }

    // ─── Create Campaign ─────────────────────────────────────
    window.createCampaign = function () {
        var title = document.getElementById("newCampaignTitle").value.trim();
        var description = document.getElementById("newCampaignDescription").value.trim();
        var instructions = document.getElementById("newCampaignInstructions").value.trim();
        var errorEl = document.getElementById("createCampaignError");

        if (!title || !instructions) {
            errorEl.textContent = "Title and AI Behavior Instructions are required.";
            errorEl.classList.remove("d-none");
            return;
        }
        errorEl.classList.add("d-none");

        fetch(apiBaseUrl() + "/api/Campaign", {
            method: "POST",
            headers: { "Content-Type": "application/json" },
            body: JSON.stringify({
                title: title,
                description: description || title,
                aiBehaviorInstructions: instructions
            })
        })
            .then(function (resp) {
                if (resp.ok) {
                    document.getElementById("newCampaignTitle").value = "";
                    document.getElementById("newCampaignDescription").value = "";
                    document.getElementById("newCampaignInstructions").value = "";
                    document.getElementById("createCampaignForm").classList.add("d-none");
                    window.loadCampaigns();
                    return resp.json();
                } else {
                    return resp.json().then(function (err) {
                        throw new Error(err.message || "Failed to create campaign");
                    });
                }
            })
            .catch(function (err) {
                errorEl.textContent = err.message;
                errorEl.classList.remove("d-none");
            });
    };

    // ─── Phone Validation ────────────────────────────────────
    var e164Regex = /^\+[1-9]\d{1,14}$/;

    function validatePhoneInputs() {
        var phone1 = document.getElementById("phoneNumber");
        var phone2 = document.getElementById("phoneNumber2");
        var valid = true;

        if (!phone1.value || !e164Regex.test(phone1.value)) {
            phone1.classList.add("is-invalid");
            valid = false;
        } else {
            phone1.classList.remove("is-invalid");
        }

        if (phone2.value && !e164Regex.test(phone2.value)) {
            phone2.classList.add("is-invalid");
            valid = false;
        } else {
            phone2.classList.remove("is-invalid");
        }

        return valid;
    }

    // ─── Initiate Call ───────────────────────────────────────
    function initiateCall() {
        if (!validatePhoneInputs()) return;

        var phone1 = document.getElementById("phoneNumber").value.trim();
        var phone2 = (document.getElementById("phoneNumber2").value || "").trim();
        var name1 = (document.getElementById("contactName1").value || "").trim();
        var name2 = (document.getElementById("contactName2").value || "").trim();
        var prompt = (document.getElementById("prompt").value || "").trim();
        var campaignId = (document.getElementById("campaignId").value || "").trim();
        var campaignTitle = selectedCampaign ? selectedCampaign.title : "";

        var phoneNumbers = [phone1];
        var contactNames = [name1];
        if (phone2) {
            phoneNumbers.push(phone2);
            contactNames.push(name2);
        }

        var btn = document.getElementById("initiateCallBtn");
        var btnText = document.getElementById("callBtnText");
        var spinner = document.getElementById("callSpinner");
        var errorEl = document.getElementById("callError");

        btn.disabled = true;
        btnText.textContent = "Calling...";
        spinner.classList.remove("d-none");
        errorEl.classList.add("d-none");

        var body = { phoneNumbers: phoneNumbers };
        if (prompt && promptManuallyEdited) body.prompt = prompt;
        if (campaignId) body.campaignId = campaignId;
        // Only include contactNames if at least one name is provided
        if (contactNames.some(function (n) { return n.length > 0; })) {
            body.contactNames = contactNames;
        }

        fetch(apiBaseUrl() + "/api/Call/initiate", {
            method: "POST",
            headers: { "Content-Type": "application/json" },
            body: JSON.stringify(body)
        })
            .then(function (resp) {
                if (resp.ok) return resp.json();
                if (resp.status === 429) throw new Error("Concurrent call limit reached (max 5).");
                if (resp.status === 400) {
                    return resp.json().then(function (err) {
                        throw new Error(err.message || "Validation error.");
                    });
                }
                throw new Error("API error: " + resp.status);
            })
            .then(function (data) {
                var callList = data.calls || [{ callConnectionId: data.callConnectionId, phoneNumber: phone1 }];

                ensureSignalR().then(function () {
                    callList.forEach(function (c) {
                        registerCall(c.callConnectionId, c.phoneNumber, campaignTitle);
                        connection.invoke("JoinCall", c.callConnectionId);
                    });
                    if (callList.length > 0) {
                        switchTab(callList[0].callConnectionId);
                    }
                    // Switch to Live Call tab on mobile
                    activateMobileTab("panelCenter");
                });
            })
            .catch(function (err) {
                errorEl.textContent = err.message;
                errorEl.classList.remove("d-none");
            })
            .finally(function () {
                btn.disabled = false;
                btnText.textContent = "Start Call";
                spinner.classList.add("d-none");
            });
    }

    // ═══════════════════════════════════════════════════════════
    // CENTER PANEL — SignalR, Transcript, Controls, KPIs
    // ═══════════════════════════════════════════════════════════

    // ─── SignalR Connection ──────────────────────────────────
    function ensureSignalR() {
        if (connection && connection.state === signalR.HubConnectionState.Connected) {
            return Promise.resolve();
        }
        return initSignalR();
    }

    function initSignalR() {
        var hubUrl = apiBaseUrl() + "/transcriptHub";
        connection = new signalR.HubConnectionBuilder()
            .withUrl(hubUrl)
            .withAutomaticReconnect()
            .configureLogging(signalR.LogLevel.Information)
            .build();

        connection.on("TranscriptUpdate", function (entry) {
            handleTranscriptUpdate(entry);
        });

        connection.on("CallStatusChanged", function (update) {
            handleCallStatus(update.callConnectionId, update.status);
        });

        connection.on("SentimentUpdate", function (data) {
            handleSentimentUpdate(data.callConnectionId, data.entryTimestamp, data.sentiment);
        });

        connection.on("EmotionUpdate", function (data) {
            handleEmotionUpdate(data.callConnectionId, data.entryTimestamp, data.emotion);
        });

        connection.on("AgentProcessingUpdate", function (data) {
            handleAgentProcessingUpdate(data);
        });

        connection.onreconnecting(function () {
            console.warn("SignalR reconnecting...");
        });

        connection.onreconnected(function () {
            console.info("SignalR reconnected");
            Object.keys(calls).forEach(function (id) {
                if (calls[id].status !== "Disconnected" && calls[id].status !== "Failed") {
                    connection.invoke("JoinCall", id);
                }
            });
        });

        connection.onclose(function () {
            console.warn("SignalR connection closed");
        });

        return connection.start().then(function () {
            console.info("SignalR connected to", hubUrl);
        }).catch(function (err) {
            console.error("SignalR connection error:", err);
        });
    }

    // ─── Call Registration & Tabs ────────────────────────────
    function registerCall(callConnectionId, phoneNumber, campaignTitle) {
        calls[callConnectionId] = {
            phoneNumber: phoneNumber,
            campaignTitle: campaignTitle || "",
            status: "Initiating",
            transcriptEntries: [],
            sentimentData: [],
            operatorEmotionData: [],
            customerEmotionData: [],
            timerInterval: null,
            timerSeconds: 0
        };

        // Show active call UI
        document.getElementById("centerIdle").classList.add("d-none");
        document.getElementById("activeCallArea").classList.remove("d-none");

        // Show right panel active call info
        var activeInfo = document.getElementById("activeCallInfo");
        if (activeInfo) activeInfo.classList.remove("d-none");

        // Reset mute/hold
        isMuted = false;
        isOnHold = false;
        updateMuteUI();
        updateHoldUI();

        // Render quick responses for this campaign
        renderQuickResponses(campaignTitle);

        ensureLiveOperationsVisible();
        updateTabs();
    }

    function ensureLiveOperationsVisible() {
        var detail = document.getElementById("callDetailPanel");
        if (detail) detail.classList.add("d-none");

        var hasAnyLiveCall = Object.keys(calls).length > 0;
        var idle = document.getElementById("centerIdle");
        var active = document.getElementById("activeCallArea");

        if (hasAnyLiveCall) {
            if (idle) idle.classList.add("d-none");
            if (active) active.classList.remove("d-none");
        } else {
            if (active) active.classList.add("d-none");
            if (idle) idle.classList.remove("d-none");
        }
    }

    function updateTabs() {
        var tabContainer = document.getElementById("callTabs");
        var callIds = Object.keys(calls);

        updateLiveCallsList();

        if (callIds.length <= 1) {
            tabContainer.classList.add("d-none");
            if (callIds.length === 1) {
                activeTabId = callIds[0];
                renderCallView(activeTabId);
            }
            return;
        }

        tabContainer.classList.remove("d-none");
        tabContainer.innerHTML = "";

        callIds.forEach(function (id) {
            var c = calls[id];
            var tab = document.createElement("button");
            tab.className = "call-tab" + (id === activeTabId ? " active" : "") +
                (c.status === "Disconnected" || c.status === "Failed" ? " disconnected" : "");
            tab.textContent = c.phoneNumber || id.substring(0, 8);

            if (c.status === "Disconnected" || c.status === "Failed") {
                var dismiss = document.createElement("span");
                dismiss.className = "tab-dismiss";
                dismiss.textContent = "×";
                dismiss.onclick = function (e) {
                    e.stopPropagation();
                    dismissCall(id);
                };
                tab.appendChild(dismiss);
            }

            tab.onclick = function () { switchTab(id); };
            tabContainer.appendChild(tab);
        });

    }

    function switchTab(callConnectionId) {
        ensureLiveOperationsVisible();
        activeTabId = callConnectionId;
        updateTabs();
        renderCallView(callConnectionId);
    }

    function dismissCall(callConnectionId) {
        // Stop timer
        var c = calls[callConnectionId];
        if (c && c.timerInterval) clearInterval(c.timerInterval);
        delete calls[callConnectionId];

        var remaining = Object.keys(calls);
        if (remaining.length === 0) {
            document.getElementById("centerIdle").classList.remove("d-none");
            document.getElementById("activeCallArea").classList.add("d-none");
            document.getElementById("callTabs").classList.add("d-none");
            var activeInfo = document.getElementById("activeCallInfo");
            if (activeInfo) activeInfo.classList.add("d-none");
            activeTabId = null;
            updateLiveCallsList();
        } else {
            if (activeTabId === callConnectionId) {
                activeTabId = remaining[0];
            }
            updateTabs();
            renderCallView(activeTabId);
        }
    }

    function updateLiveCallsList() {
        var listEl = document.getElementById("liveCallsList");
        var emptyEl = document.getElementById("liveCallsEmpty");
        if (!listEl || !emptyEl) return;

        var callIds = Object.keys(calls);
        if (callIds.length === 0) {
            emptyEl.classList.remove("d-none");
            listEl.classList.add("d-none");
            listEl.innerHTML = "";
            return;
        }

        emptyEl.classList.add("d-none");
        listEl.classList.remove("d-none");
        listEl.innerHTML = "";

        callIds.forEach(function (id) {
            var c = calls[id];
            var item = document.createElement("div");
            item.className = "history-item live-call-item" + (id === activeTabId ? " selected" : "");
            item.onclick = function () { switchTab(id); };

            var phone = c.phoneNumber || id.substring(0, 8);
            var status = c.status || "";

            item.innerHTML =
                '<div class="history-phone">' + escapeHtml(phone) +
                '  <span class="badge bg-secondary ms-2">' + escapeHtml(status) + '</span>' +
                '</div>' +
                '<div class="history-meta"><span>' + escapeHtml(c.campaignTitle || "") + '</span></div>';

            listEl.appendChild(item);
        });
    }

    // ─── Render Active Call View ─────────────────────────────
    function renderCallView(callConnectionId) {
        var c = calls[callConnectionId];
        if (!c) return;

        // Status bar
        document.getElementById("callPhoneLabel").textContent = c.phoneNumber || "";
        var badge = document.getElementById("callStatusBadge");
        setStatusBadge(badge, c.status);

        // Campaign badge
        var campBadge = document.getElementById("callCampaignBadge");
        if (campBadge && c.campaignTitle) {
            var cat = getCampaignCategory(c.campaignTitle);
            campBadge.className = "call-campaign-badge category-badge " + cat.css;
            campBadge.textContent = c.campaignTitle;
        } else if (campBadge) {
            campBadge.textContent = "";
            campBadge.className = "call-campaign-badge";
        }

        // Timer
        document.getElementById("callTimer").textContent = formatTimer(c.timerSeconds);

        // End call button
        var endBtn = document.getElementById("endCallBtn");
        endBtn.disabled = (c.status === "Disconnected" || c.status === "Failed");
        endBtn.onclick = function () { hangUpCall(callConnectionId); };

        // Transcript (chat bubbles)
        var area = document.getElementById("transcriptArea");
        area.innerHTML = "";
        if (c.transcriptEntries.length === 0) {
            area.innerHTML =
                '<div class="transcript-placeholder">' +
                '<svg xmlns="http://www.w3.org/2000/svg" width="24" height="24" fill="currentColor" viewBox="0 0 16 16">' +
                '<path d="M14 1a1 1 0 0 1 1 1v8a1 1 0 0 1-1 1H4.414A2 2 0 0 0 3 11.586l-2 2V2a1 1 0 0 1 1-1zM2 0a2 2 0 0 0-2 2v12.793a.5.5 0 0 0 .854.353l2.853-2.853A1 1 0 0 1 4.414 12H14a2 2 0 0 0 2-2V2a2 2 0 0 0-2-2z"/>' +
                '</svg>' +
                '<span>Waiting for conversation...</span></div>';
        } else {
            c.transcriptEntries.forEach(function (entry) {
                area.appendChild(createChatBubble(entry));
            });
            area.scrollTop = area.scrollHeight;
        }

        // Sentiment graph
        drawSentimentGraph(callConnectionId);

        // Quick responses
        renderQuickResponses(c.campaignTitle);

        // Right panel active call info
        updateRightPanelCallInfo(c);
    }

    // ─── Chat Bubble Transcript ──────────────────────────────
    function createChatBubble(entry) {
        var isAI = entry.speaker === 0 || entry.speaker === "AI";
        var bubble = document.createElement("div");
        bubble.className = "chat-bubble " + (isAI ? "ai-bubble" : "caller-bubble");

        var ts = entry.timestamp ? new Date(entry.timestamp) : new Date();
        bubble.setAttribute("data-timestamp", ts.toISOString());

        // Avatar
        var avatar = document.createElement("div");
        avatar.className = "bubble-avatar";
        if (isAI) {
            avatar.innerHTML = '<svg xmlns="http://www.w3.org/2000/svg" width="14" height="14" fill="currentColor" viewBox="0 0 16 16"><path d="M6 12.5a.5.5 0 0 1 .5-.5h3a.5.5 0 0 1 0 1h-3a.5.5 0 0 1-.5-.5M3 8.062C3 6.76 4.235 5.765 5.53 5.886a26.6 26.6 0 0 0 4.94 0C11.765 5.765 13 6.76 13 8.062v1.157a.93.93 0 0 1-.765.935c-.845.147-2.34.346-4.235.346s-3.39-.2-4.235-.346A.93.93 0 0 1 3 9.219zm4.542-.827a.25.25 0 0 0-.217.068l-.92.9a25 25 0 0 1-1.871-.183.25.25 0 0 0-.068.495c.55.076 1.232.149 2.02.193a.25.25 0 0 0 .189-.071l.754-.736.847 1.71a.25.25 0 0 0 .404.062l.932-.97a25 25 0 0 0 1.922-.188.25.25 0 0 0-.068-.495c-.538.074-1.207.145-1.98.189a.25.25 0 0 0-.166.076l-.754.785-.842-1.7a.25.25 0 0 0-.182-.135"/><path d="M8.5 1.866a1 1 0 1 0-1 0V3h-2A4.5 4.5 0 0 0 1 7.5V8a1 1 0 0 0-1 1v2a1 1 0 0 0 1 1v1a2 2 0 0 0 2 2h10a2 2 0 0 0 2-2v-1a1 1 0 0 0 1-1V9a1 1 0 0 0-1-1v-.5A4.5 4.5 0 0 0 10.5 3h-2zM14 7.5V13a1 1 0 0 1-1 1H3a1 1 0 0 1-1-1V7.5A3.5 3.5 0 0 1 5.5 4h5A3.5 3.5 0 0 1 14 7.5"/></svg>';
        } else {
            avatar.innerHTML = '<svg xmlns="http://www.w3.org/2000/svg" width="14" height="14" fill="currentColor" viewBox="0 0 16 16"><path d="M8 8a3 3 0 1 0 0-6 3 3 0 0 0 0 6m2-3a2 2 0 1 1-4 0 2 2 0 0 1 4 0m4 8c0 1-1 1-1 1H3s-1 0-1-1 1-4 6-4 6 3 6 4m-1-.004c-.001-.246-.154-.986-.832-1.664C11.516 10.68 10.289 10 8 10s-3.516.68-4.168 1.332c-.678.678-.83 1.418-.832 1.664z"/></svg>';
        }

        // Content
        var content = document.createElement("div");
        content.className = "bubble-content";
        content.textContent = entry.text || "";

        // Meta (speaker, time, sentiment dot)
        var meta = document.createElement("div");
        meta.className = "bubble-meta";

        var speaker = document.createElement("span");
        speaker.className = "bubble-speaker";
        speaker.textContent = isAI ? "AI" : "Caller";

        var time = document.createElement("span");
        time.className = "bubble-time";
        time.textContent = ts.toLocaleTimeString([], { hour: "2-digit", minute: "2-digit", second: "2-digit" });

        var dot = document.createElement("span");
        dot.className = "sentiment-dot";
        if (entry.sentiment && entry.sentiment.label !== undefined) {
            dot.classList.add(getSentimentDotClass(entry.sentiment.label));
        } else {
            dot.classList.add("sentiment-dot-loading");
        }

        meta.appendChild(speaker);
        meta.appendChild(time);
        meta.appendChild(dot);
        content.appendChild(meta);

        bubble.appendChild(avatar);
        bubble.appendChild(content);

        return bubble;
    }

    function getSentimentDotClass(label) {
        if (label === 0 || label === "Positive") return "sentiment-dot-positive";
        if (label === 2 || label === "Negative") return "sentiment-dot-negative";
        return "sentiment-dot-neutral";
    }

    // ─── Transcript Handling ─────────────────────────────────
    function handleTranscriptUpdate(entry) {
        var id = entry.callConnectionId;
        var c = calls[id];
        if (!c) return;

        var entryData = {
            speaker: entry.speaker,
            text: entry.text || "",
            timestamp: entry.timestamp,
            sentiment: entry.sentiment || null,
            emotion: entry.emotion || null
        };
        c.transcriptEntries.push(entryData);

        if (entryData.sentiment && entryData.sentiment.label !== undefined) {
            c.sentimentData.push(sentimentToNumber(entryData.sentiment.label));
        } else {
            c.sentimentData.push(0);
        }

        // Push emotion data to BOTH arrays on every entry (aligned with sentiment timeline)
        var emotionVal = entryData.emotion ? emotionToNumber(entryData.emotion.label) : 0;
        var isOperator = (entryData.speaker === 0 || entryData.speaker === "AI");
        if (isOperator) {
            c.operatorEmotionData.push(emotionVal);
            // Carry forward last customer value so timelines stay aligned
            var lastCust = c.customerEmotionData.length > 0 ? c.customerEmotionData[c.customerEmotionData.length - 1] : 0;
            c.customerEmotionData.push(lastCust);
        } else {
            c.customerEmotionData.push(emotionVal);
            // Carry forward last operator value so timelines stay aligned
            var lastOp = c.operatorEmotionData.length > 0 ? c.operatorEmotionData[c.operatorEmotionData.length - 1] : 0;
            c.operatorEmotionData.push(lastOp);
        }

        // Mic activity pulse
        pulseMicActivity();

        if (id === activeTabId) {
            var area = document.getElementById("transcriptArea");
            var placeholder = area.querySelector(".transcript-placeholder");
            if (placeholder) area.innerHTML = "";
            area.appendChild(createChatBubble(entryData));
            area.scrollTop = area.scrollHeight;
            drawSentimentGraph(id);
            drawEmotionGraph(id, "operatorEmotionCanvas", c.operatorEmotionData);
            drawEmotionGraph(id, "customerEmotionCanvas", c.customerEmotionData);
        }
    }

    // ─── Sentiment Updates ───────────────────────────────────
    function handleSentimentUpdate(callConnectionId, entryTimestamp, sentiment) {
        var c = calls[callConnectionId];
        if (!c) return;

        var ts = new Date(entryTimestamp).toISOString();
        for (var i = c.transcriptEntries.length - 1; i >= 0; i--) {
            var entryTs = new Date(c.transcriptEntries[i].timestamp).toISOString();
            if (entryTs === ts) {
                c.transcriptEntries[i].sentiment = sentiment;
                c.sentimentData[i] = sentimentToNumber(sentiment.label);
                break;
            }
        }

        if (callConnectionId === activeTabId) {
            var area = document.getElementById("transcriptArea");
            var bubbles = area.querySelectorAll(".chat-bubble");
            for (var j = bubbles.length - 1; j >= 0; j--) {
                if (bubbles[j].getAttribute("data-timestamp") === ts) {
                    var dot = bubbles[j].querySelector(".sentiment-dot");
                    if (dot) {
                        dot.className = "sentiment-dot " + getSentimentDotClass(sentiment.label);
                    }
                    break;
                }
            }
            drawSentimentGraph(callConnectionId);
        }
    }

    function sentimentToNumber(label) {
        var labelMap = { 0: 1, 1: 0, 2: -1 };
        if (typeof label === "number") return labelMap[label] !== undefined ? labelMap[label] : 0;
        if (label === "Positive") return 1;
        if (label === "Negative") return -1;
        return 0;
    }

    // ─── Emotion Helpers ─────────────────────────────────────
    // Maps EmotionLabel enum to a numeric value for graphing (0 to 5 scale)
    // Neutral=0, Happy=1, Frustrated=2, Angry=3, Sad=4, Anxious=5
    function emotionToNumber(label) {
        var labelMap = { 0: 0, 1: 1, 2: 2, 3: 3, 4: 4, 5: 5 };
        var stringMap = { "Neutral": 0, "Happy": 1, "Frustrated": 2, "Angry": 3, "Sad": 4, "Anxious": 5 };
        if (typeof label === "number") return labelMap[label] !== undefined ? labelMap[label] : 0;
        return stringMap[label] !== undefined ? stringMap[label] : 0;
    }

    var emotionColors = {
        0: "#9E9E9E", // Neutral - grey
        1: "#4CAF50", // Happy - green
        2: "#FF9800", // Frustrated - orange
        3: "#f44336", // Angry - red
        4: "#2196F3", // Sad - blue
        5: "#9C27B0"  // Anxious - purple
    };

    var emotionLabels = ["Neutral", "Happy", "Frustrated", "Angry", "Sad", "Anxious"];

    function getEmotionLabel(val) {
        return emotionLabels[val] || "Neutral";
    }

    // ─── Emotion Updates ─────────────────────────────────────
    function handleEmotionUpdate(callConnectionId, entryTimestamp, emotion) {
        var c = calls[callConnectionId];
        if (!c) return;

        var ts = new Date(entryTimestamp).toISOString();
        for (var i = c.transcriptEntries.length - 1; i >= 0; i--) {
            var entryTs = new Date(c.transcriptEntries[i].timestamp).toISOString();
            if (entryTs === ts) {
                c.transcriptEntries[i].emotion = emotion;
                var emotionVal = emotionToNumber(emotion.label);
                var isOperator = (c.transcriptEntries[i].speaker === 0 || c.transcriptEntries[i].speaker === "AI");

                // Update emotion data — arrays are 1:1 with transcript entries
                if (i < c.operatorEmotionData.length && isOperator) {
                    c.operatorEmotionData[i] = emotionVal;
                } else if (i < c.customerEmotionData.length && !isOperator) {
                    c.customerEmotionData[i] = emotionVal;
                }
                break;
            }
        }

        if (callConnectionId === activeTabId) {
            drawEmotionGraph(callConnectionId, "operatorEmotionCanvas", c.operatorEmotionData);
            drawEmotionGraph(callConnectionId, "customerEmotionCanvas", c.customerEmotionData);
        }
    }

    // ─── Call Status ─────────────────────────────────────────
    function handleCallStatus(callConnectionId, status) {
        var c = calls[callConnectionId];
        if (!c) return;

        var statusStr = typeof status === "number"
            ? ["Initiating", "Ringing", "Connected", "Disconnected", "Failed", "Reconnecting"][status]
            : status;

        c.status = statusStr;

        // Start timer on Connected
        if (statusStr === "Connected" && !c.timerInterval) {
            c.timerSeconds = 0;
            c.timerInterval = setInterval(function () {
                c.timerSeconds++;
                if (callConnectionId === activeTabId) {
                    document.getElementById("callTimer").textContent = formatTimer(c.timerSeconds);
                    var rpTimer = document.getElementById("rightPanelTimer");
                    if (rpTimer) rpTimer.textContent = formatTimer(c.timerSeconds);
                }
            }, 1000);

            // Track successful connection
            kpiState.successfulCalls++;

            // Clear reconnection banner on successful connect
            removeReconnectBanner(callConnectionId);
        }

        // Handle VoiceLive reconnection status
        if (statusStr === "Reconnecting") {
            showReconnectBanner(callConnectionId, status);
        } else if (statusStr === "ReconnectFailed") {
            showReconnectBanner(callConnectionId, status);
        } else {
            removeReconnectBanner(callConnectionId);
        }

        // Stop timer on Disconnected or Failed
        if (statusStr === "Disconnected" || statusStr === "Failed") {
            if (c.timerInterval) {
                clearInterval(c.timerInterval);
                c.timerInterval = null;
            }

            // Update KPIs
            kpiState.callsToday++;
            kpiState.totalDuration += c.timerSeconds;

            // Final sentiment score
            if (c.sentimentData.length > 0) {
                var avg = c.sentimentData.reduce(function (a, b) { return a + b; }, 0) / c.sentimentData.length;
                kpiState.sentimentScores.push(avg);
            }

            saveKpiState();
            renderKPIs();

            setTimeout(function () { loadCallHistory(); }, 1500);
        }

        if (callConnectionId === activeTabId) {
            var badge = document.getElementById("callStatusBadge");
            setStatusBadge(badge, statusStr);
            if (statusStr === "Disconnected" || statusStr === "Failed") {
                document.getElementById("endCallBtn").disabled = true;
            }
            updateRightPanelCallInfo(c);
        }

        updateTabs();
    }

    function setStatusBadge(badge, statusStr) {
        badge.textContent = statusStr;
        badge.className = "status-badge badge";
        switch (statusStr) {
            case "Initiating": badge.classList.add("bg-secondary"); break;
            case "Ringing": badge.classList.add("bg-warning", "text-dark"); break;
            case "Connected": badge.classList.add("bg-success"); break;
            case "Disconnected": badge.classList.add("bg-danger"); break;
            case "Failed": badge.classList.add("bg-danger"); break;
            case "Reconnecting": badge.classList.add("bg-warning", "text-dark"); break;
            case "ReconnectFailed": badge.classList.add("bg-danger"); break;
            default: badge.classList.add("bg-secondary");
        }
    }

    // ─── Reconnection Banner ─────────────────────────────────
    function showReconnectBanner(callConnectionId, status) {
        if (callConnectionId !== activeTabId) return;
        var container = document.getElementById("activeCallArea");
        if (!container) return;

        var existing = document.getElementById("reconnectBanner");
        if (!existing) {
            existing = document.createElement("div");
            existing.id = "reconnectBanner";
            existing.className = "reconnect-banner";
            // Insert after call status bar
            var statusBar = document.getElementById("callStatusBar");
            if (statusBar && statusBar.nextSibling) {
                container.insertBefore(existing, statusBar.nextSibling);
            } else {
                container.appendChild(existing);
            }
        }

        if (typeof status === "string" && status.startsWith("Reconnecting")) {
            // Extract attempt info from message if available (e.g., "Reconnecting… attempt 2/3")
            existing.className = "reconnect-banner";
            existing.innerHTML = '<span>⟳</span><span>Reconnecting to VoiceLive…</span><span style="margin-left:auto;font-size:0.75rem;opacity:0.7">Hang up to abort</span>';
        } else if (status === "ReconnectFailed") {
            existing.className = "reconnect-banner failed";
            existing.innerHTML = '<span>✕</span><span>All reconnection attempts failed. Call ended.</span>';
            setTimeout(function () { removeReconnectBanner(callConnectionId); }, 8000);
        }
    }

    function removeReconnectBanner(callConnectionId) {
        if (callConnectionId !== activeTabId) return;
        var banner = document.getElementById("reconnectBanner");
        if (banner) banner.remove();
    }

    // ─── Call Controls ───────────────────────────────────────
    function hangUpCall(callConnectionId) {
        if (!callConnectionId) return;
        var endBtn = document.getElementById("endCallBtn");
        if (endBtn && callConnectionId === activeTabId) endBtn.disabled = true;

        fetch(apiBaseUrl() + "/api/Call/hangup/" + callConnectionId, { method: "POST" })
            .then(function (resp) {
                if (resp.ok) {
                    handleCallStatus(callConnectionId, "Disconnected");
                } else {
                    console.error("Hang-up failed:", resp.status);
                }
            })
            .catch(function (err) {
                console.error("Hang-up error:", err);
            });
    }
    window.hangUpCall = hangUpCall;

    function toggleMute() {
        isMuted = !isMuted;
        updateMuteUI();
        console.info("Mute toggled:", isMuted ? "MUTED" : "UNMUTED");
    }

    function updateMuteUI() {
        var btn = document.getElementById("muteBtn");
        if (!btn) return;
        var onIcon = btn.querySelector(".mic-on-icon");
        var offIcon = btn.querySelector(".mic-off-icon");
        if (isMuted) {
            btn.classList.add("active");
            if (onIcon) onIcon.classList.add("d-none");
            if (offIcon) offIcon.classList.remove("d-none");
        } else {
            btn.classList.remove("active");
            if (onIcon) onIcon.classList.remove("d-none");
            if (offIcon) offIcon.classList.add("d-none");
        }
    }

    function toggleHold() {
        isOnHold = !isOnHold;
        updateHoldUI();
        console.info("Hold toggled:", isOnHold ? "ON HOLD" : "RESUMED");
    }

    function updateHoldUI() {
        var btn = document.getElementById("holdBtn");
        if (!btn) return;
        var pauseIcon = btn.querySelector(".hold-pause-icon");
        var playIcon = btn.querySelector(".hold-play-icon");
        if (isOnHold) {
            btn.classList.add("active");
            if (pauseIcon) pauseIcon.classList.add("d-none");
            if (playIcon) playIcon.classList.remove("d-none");
        } else {
            btn.classList.remove("active");
            if (pauseIcon) pauseIcon.classList.remove("d-none");
            if (playIcon) playIcon.classList.add("d-none");
        }
    }

    function pulseMicActivity() {
        var container = document.getElementById("micLevelContainer");
        if (!container) return;
        container.classList.add("mic-active");
        if (micActivityTimeout) clearTimeout(micActivityTimeout);
        micActivityTimeout = setTimeout(function () {
            container.classList.remove("mic-active");
        }, 800);
    }

    function formatTimer(seconds) {
        var m = Math.floor(seconds / 60);
        var s = seconds % 60;
        return (m < 10 ? "0" + m : m) + ":" + (s < 10 ? "0" + s : s);
    }

    // ─── Right Panel Call Info ────────────────────────────────
    function updateRightPanelCallInfo(call) {
        var phone = document.getElementById("rightPanelPhone");
        var campaign = document.getElementById("rightPanelCampaign");
        var status = document.getElementById("rightPanelStatus");
        var timer = document.getElementById("rightPanelTimer");

        if (phone) phone.textContent = call.phoneNumber || "—";
        if (campaign) campaign.textContent = call.campaignTitle || "—";
        if (status) {
            status.textContent = call.status;
            setStatusBadge(status, call.status);
        }
        if (timer) timer.textContent = formatTimer(call.timerSeconds);
    }

    // ═══════════════════════════════════════════════════════════
    // QUICK RESPONSES
    // ═══════════════════════════════════════════════════════════

    function renderQuickResponses(campaignTitle) {
        var container = document.getElementById("quickResponsesList");
        if (!container) return;

        var responses = getQuickResponses(campaignTitle);
        container.innerHTML = "";
        responses.forEach(function (text) {
            var card = document.createElement("div");
            card.className = "quick-response-card";
            card.innerHTML =
                '<svg class="qr-icon" xmlns="http://www.w3.org/2000/svg" width="14" height="14" fill="currentColor" viewBox="0 0 16 16">' +
                '<path d="M14 1a1 1 0 0 1 1 1v8a1 1 0 0 1-1 1h-2.5a2 2 0 0 0-1.6.8L8 14.333 6.1 11.8a2 2 0 0 0-1.6-.8H2a1 1 0 0 1-1-1V2a1 1 0 0 1 1-1z"/>' +
                '</svg>' +
                '<span>"' + escapeHtml(text) + '"</span>';
            card.onclick = function () {
                copyToClipboard(text);
                showToast("Copied to clipboard!");
            };
            container.appendChild(card);
        });
    }

    function copyToClipboard(text) {
        if (navigator.clipboard) {
            navigator.clipboard.writeText(text);
        } else {
            var ta = document.createElement("textarea");
            ta.value = text;
            document.body.appendChild(ta);
            ta.select();
            document.execCommand("copy");
            document.body.removeChild(ta);
        }
    }

    function showToast(message) {
        var container = document.getElementById("toastContainer");
        if (!container) return;
        var toast = document.createElement("div");
        toast.className = "toast-msg";
        toast.textContent = message;
        container.appendChild(toast);
        setTimeout(function () {
            if (toast.parentNode) toast.parentNode.removeChild(toast);
        }, 2200);
    }

    // ═══════════════════════════════════════════════════════════
    // KPI TRACKING
    // ═══════════════════════════════════════════════════════════

    var kpiTimeRange = sessionStorage.getItem("callcenter_kpi_range") || "today";

    function loadKpiState() {
        try {
            var saved = sessionStorage.getItem("callcenter_kpi");
            if (saved) return JSON.parse(saved);
        } catch (e) { /* ignore */ }
        return {
            callsToday: 0,
            totalDuration: 0,
            sentimentScores: [],
            successfulCalls: 0
        };
    }

    function saveKpiState() {
        try {
            sessionStorage.setItem("callcenter_kpi", JSON.stringify(kpiState));
        } catch (e) { /* ignore */ }
    }

    window.setKpiTimeRange = function (range) {
        kpiTimeRange = range;
        sessionStorage.setItem("callcenter_kpi_range", range);
        document.querySelectorAll(".kpi-range-pill").forEach(function (pill) {
            pill.classList.toggle("active", pill.dataset.range === range);
        });
        fetchAnalyticsDashboard(range);
    };

    function fetchAnalyticsDashboard(range) {
        fetch(apiBaseUrl() + "/api/analytics/dashboard?range=" + encodeURIComponent(range))
            .then(function (r) { return r.json(); })
            .then(function (data) {
                var callsEl = document.getElementById("kpiCallsToday");
                var durationEl = document.getElementById("kpiAvgDuration");
                var sentimentEl = document.getElementById("kpiSentiment");
                var successEl = document.getElementById("kpiSuccessRate");
                var labelEl = document.getElementById("kpiCallsLabel");

                if (callsEl) callsEl.textContent = data.totalCalls;
                if (durationEl) {
                    var avg = Math.round(data.avgDurationSeconds);
                    durationEl.textContent = formatTimer(avg);
                }
                if (sentimentEl) {
                    sentimentEl.textContent = data.sentimentBreakdown.positive > 0
                        ? Math.round(data.sentimentBreakdown.positive) + "%"
                        : "—";
                }
                if (successEl) {
                    successEl.textContent = data.successRate > 0
                        ? Math.round(data.successRate) + "%"
                        : "—";
                }
                if (labelEl) {
                    var rangeLabels = { today: "Calls Today", yesterday: "Calls Yesterday", "7d": "Calls (7 Days)", "30d": "Calls (30 Days)", year: "Calls (Year)" };
                    labelEl.textContent = rangeLabels[range] || "Calls Today";
                }
            })
            .catch(function () { /* fall back to session KPIs */ renderKPIs(); });
    }

    function renderKPIs() {
        var callsEl = document.getElementById("kpiCallsToday");
        var durationEl = document.getElementById("kpiAvgDuration");
        var sentimentEl = document.getElementById("kpiSentiment");
        var successEl = document.getElementById("kpiSuccessRate");

        if (callsEl) callsEl.textContent = kpiState.callsToday;

        if (durationEl) {
            if (kpiState.callsToday > 0) {
                var avg = Math.round(kpiState.totalDuration / kpiState.callsToday);
                durationEl.textContent = formatTimer(avg);
            } else {
                durationEl.textContent = "0:00";
            }
        }

        if (sentimentEl) {
            if (kpiState.sentimentScores.length > 0) {
                var avgSent = kpiState.sentimentScores.reduce(function (a, b) { return a + b; }, 0) / kpiState.sentimentScores.length;
                var pctPos = Math.round(((avgSent + 1) / 2) * 100);
                sentimentEl.textContent = pctPos + "%";
            } else {
                sentimentEl.textContent = "—";
            }
        }

        if (successEl) {
            if (kpiState.callsToday > 0) {
                var rate = Math.round((kpiState.successfulCalls / kpiState.callsToday) * 100);
                successEl.textContent = rate + "%";
            } else {
                successEl.textContent = "—";
            }
        }
    }

    // ═══════════════════════════════════════════════════════════
    // SENTIMENT GRAPH — Canvas-based rolling line chart
    // ═══════════════════════════════════════════════════════════

    function drawSentimentGraph(callConnectionId) {
        var c = calls[callConnectionId];
        if (!c) return;

        var canvas = document.getElementById("sentimentCanvas");
        if (!canvas) return;
        var ctx = canvas.getContext("2d");

        var rect = canvas.parentElement.getBoundingClientRect();
        var dpr = window.devicePixelRatio || 1;
        canvas.width = rect.width * dpr;
        canvas.height = 80 * dpr;
        canvas.style.height = "80px";
        ctx.scale(dpr, dpr);

        var w = rect.width;
        var h = 80;
        var data = c.sentimentData;

        ctx.clearRect(0, 0, w, h);

        if (data.length === 0) {
            ctx.fillStyle = "#94a3b8";
            ctx.font = "12px sans-serif";
            ctx.textAlign = "center";
            ctx.fillText("No sentiment data yet", w / 2, h / 2);
            return;
        }

        var padding = { top: 8, bottom: 8, left: 4, right: 4 };
        var plotW = w - padding.left - padding.right;
        var plotH = h - padding.top - padding.bottom;
        var midY = padding.top + plotH / 2;

        // Grid
        ctx.strokeStyle = "#e2e8f0";
        ctx.lineWidth = 0.5;
        [padding.top, midY, padding.top + plotH].forEach(function (y) {
            ctx.beginPath();
            ctx.moveTo(padding.left, y);
            ctx.lineTo(w - padding.right, y);
            ctx.stroke();
        });

        // Labels
        ctx.fillStyle = "#94a3b8";
        ctx.font = "9px sans-serif";
        ctx.textAlign = "left";
        ctx.fillText("+", 0, padding.top + 4);
        ctx.fillText("0", 0, midY + 3);
        ctx.fillText("−", 0, padding.top + plotH + 1);

        var maxPoints = 40;
        var visibleData = data.length > maxPoints ? data.slice(data.length - maxPoints) : data;
        var step = visibleData.length > 1 ? plotW / (visibleData.length - 1) : plotW;

        // Area fill
        if (visibleData.length > 1) {
            ctx.beginPath();
            ctx.moveTo(padding.left, midY);
            visibleData.forEach(function (v, i) {
                var x = padding.left + i * step;
                var y = midY - v * (plotH / 2);
                ctx.lineTo(x, y);
            });
            ctx.lineTo(padding.left + (visibleData.length - 1) * step, midY);
            ctx.closePath();
            ctx.fillStyle = "rgba(59,130,246,0.08)";
            ctx.fill();
        }

        // Line
        ctx.beginPath();
        ctx.strokeStyle = "#3b82f6";
        ctx.lineWidth = 1.5;
        ctx.lineJoin = "round";
        visibleData.forEach(function (v, i) {
            var x = padding.left + i * step;
            var y = midY - v * (plotH / 2);
            if (i === 0) ctx.moveTo(x, y);
            else ctx.lineTo(x, y);
        });
        ctx.stroke();

        // Dots
        visibleData.forEach(function (v, i) {
            var x = padding.left + i * step;
            var y = midY - v * (plotH / 2);
            ctx.beginPath();
            ctx.arc(x, y, 3, 0, Math.PI * 2);
            if (v > 0) ctx.fillStyle = "#22c55e";
            else if (v < 0) ctx.fillStyle = "#ef4444";
            else ctx.fillStyle = "#94a3b8";
            ctx.fill();
        });
    }

    // ─── Emotion Graph Drawing ───────────────────────────────
    function drawEmotionGraph(callConnectionId, canvasId, data) {
        var canvas = document.getElementById(canvasId);
        if (!canvas) return;
        var ctx = canvas.getContext("2d");

        var rect = canvas.parentElement.getBoundingClientRect();
        var dpr = window.devicePixelRatio || 1;
        canvas.width = rect.width * dpr;
        canvas.height = 80 * dpr;
        canvas.style.height = "80px";
        ctx.scale(dpr, dpr);

        var w = rect.width;
        var h = 80;

        ctx.clearRect(0, 0, w, h);

        if (!data || data.length === 0) {
            ctx.fillStyle = "#94a3b8";
            ctx.font = "12px sans-serif";
            ctx.textAlign = "center";
            ctx.fillText("No emotion data yet", w / 2, h / 2);
            return;
        }

        var padding = { top: 8, bottom: 8, left: 4, right: 4 };
        var plotW = w - padding.left - padding.right;
        var plotH = h - padding.top - padding.bottom;
        var maxEmotionVal = 5; // Anxious = 5 is the max

        // Grid lines for each emotion level
        ctx.strokeStyle = "#e2e8f0";
        ctx.lineWidth = 0.5;
        for (var g = 0; g <= maxEmotionVal; g++) {
            var gy = padding.top + plotH - (g / maxEmotionVal) * plotH;
            ctx.beginPath();
            ctx.moveTo(padding.left, gy);
            ctx.lineTo(w - padding.right, gy);
            ctx.stroke();
        }

        var maxPoints = 40;
        var visibleData = data.length > maxPoints ? data.slice(data.length - maxPoints) : data;
        var step = visibleData.length > 1 ? plotW / (visibleData.length - 1) : plotW;

        // Dots — each colored by emotion type
        visibleData.forEach(function (v, i) {
            var x = padding.left + i * step;
            var y = padding.top + plotH - (v / maxEmotionVal) * plotH;
            ctx.beginPath();
            ctx.arc(x, y, 4, 0, Math.PI * 2);
            ctx.fillStyle = emotionColors[v] || "#9E9E9E";
            ctx.fill();
            ctx.strokeStyle = "#fff";
            ctx.lineWidth = 1;
            ctx.stroke();
        });

        // Connecting line (subtle)
        if (visibleData.length > 1) {
            ctx.beginPath();
            ctx.strokeStyle = "rgba(100,100,100,0.3)";
            ctx.lineWidth = 1;
            ctx.lineJoin = "round";
            visibleData.forEach(function (v, i) {
                var x = padding.left + i * step;
                var y = padding.top + plotH - (v / maxEmotionVal) * plotH;
                if (i === 0) ctx.moveTo(x, y);
                else ctx.lineTo(x, y);
            });
            ctx.stroke();
        }
    }

    // ═══════════════════════════════════════════════════════════
    // RIGHT PANEL — Call History
    // ═══════════════════════════════════════════════════════════

    var historyPage = 1;
    var historyPageSize = 20;
    var historyTotalCount = 0;

    window.loadCallHistory = function (page) {
        if (typeof page === "number" && page >= 1) {
            historyPage = page;
        } else {
            historyPage = 1;
        }

        var loading = document.getElementById("historyLoading");
        var empty = document.getElementById("historyEmpty");
        var list = document.getElementById("historyList");

        loading.classList.remove("d-none");
        empty.classList.add("d-none");
        list.classList.add("d-none");

        fetch(apiBaseUrl() + "/api/CallHistory?page=" + historyPage + "&pageSize=" + historyPageSize)
            .then(function (resp) { return resp.json(); })
            .then(function (result) {
                loading.classList.add("d-none");

                // Support paginated response {totalCount, page, pageSize, items}
                var data = result.items || result;
                historyTotalCount = result.totalCount || (Array.isArray(data) ? data.length : 0);
                historyPage = result.page || historyPage;

                if (!data || data.length === 0) {
                    if (historyPage <= 1) {
                        empty.classList.remove("d-none");
                    }
                    return;
                }

                list.innerHTML = "";
                data.forEach(function (item) {
                    var div = document.createElement("div");
                    div.className = "history-item";
                    div.onclick = function () { loadCallDetail(item.callConnectionId, div); };

                    var sentimentClass = getSentimentClass(item.overallSentiment);
                    var dateStr = new Date(item.startedAt).toLocaleString([], {
                        month: "short", day: "numeric", hour: "2-digit", minute: "2-digit"
                    });

                    var campCat = getCampaignCategory(item.campaignTitle);

                    var displayName = item.contactName
                        ? escapeHtml(item.contactName) + ' <span style="opacity:0.5;font-size:0.75rem;">(' + escapeHtml(item.phoneNumber) + ')</span>'
                        : escapeHtml(item.phoneNumber);

                    var recordingIcon = item.hasRecording
                        ? '<span class="recording-indicator" title="Recording available">🎙</span>'
                        : '';

                    div.innerHTML =
                        '<div class="history-phone">' + displayName + recordingIcon +
                        '  <span class="sentiment-badge-large ' + sentimentClass + '">' +
                        escapeHtml(getSentimentLabel(item.overallSentiment)) + '</span></div>' +
                        '<div class="history-campaign"><span class="category-badge ' + campCat.css + '" style="font-size:0.6rem;">' +
                        escapeHtml(item.campaignTitle || "—") + '</span></div>' +
                        '<div class="history-meta"><span>' + escapeHtml(item.duration || "") + '</span><span>' + dateStr + '</span></div>';

                    list.appendChild(div);
                });

                // Pagination controls
                var totalPages = Math.ceil(historyTotalCount / historyPageSize);
                if (totalPages > 1) {
                    var paginationDiv = document.createElement("div");
                    paginationDiv.className = "history-pagination";
                    paginationDiv.innerHTML =
                        '<button class="btn btn-sm btn-outline-light" ' + (historyPage <= 1 ? 'disabled' : '') +
                        ' onclick="loadCallHistory(' + (historyPage - 1) + ')">&laquo; Prev</button>' +
                        '<span class="pagination-info">Page ' + historyPage + ' of ' + totalPages + '</span>' +
                        '<button class="btn btn-sm btn-outline-light" ' + (historyPage >= totalPages ? 'disabled' : '') +
                        ' onclick="loadCallHistory(' + (historyPage + 1) + ')">Next &raquo;</button>';
                    list.appendChild(paginationDiv);
                }

                list.classList.remove("d-none");
            })
            .catch(function (err) {
                loading.classList.add("d-none");
                empty.classList.remove("d-none");
                empty.textContent = "Failed to load history.";
                console.error("Error loading call history:", err);
            });
    };

    function loadCallDetail(callConnectionId, element) {
        var historyList = document.getElementById("historyList");
        if (historyList) {
            historyList.querySelectorAll(".history-item").forEach(function (el) {
                el.classList.remove("selected");
            });
        }
        if (element) element.classList.add("selected");

        // Show loading indicator
        var detailLoading = document.getElementById("callDetailLoading");
        var detailPanel = document.getElementById("callDetailPanel");
        if (detailLoading) {
            detailLoading.classList.remove("d-none");
            document.getElementById("centerIdle").classList.add("d-none");
            document.getElementById("activeCallArea").classList.add("d-none");
        }
        if (detailPanel) detailPanel.classList.add("d-none");

        fetch(apiBaseUrl() + "/api/CallHistory/" + callConnectionId)
            .then(function (resp) {
                if (!resp.ok) throw new Error("Not found");
                return resp.json();
            })
            .then(function (record) {
                // Hide loading indicator
                if (detailLoading) detailLoading.classList.add("d-none");
                var panel = document.getElementById("callDetailPanel");

                // Show history detail in center panel (hide live operations until user switches back)
                document.getElementById("centerIdle").classList.add("d-none");
                document.getElementById("activeCallArea").classList.add("d-none");

                // Show phone number with contact name if available
                var phoneDisplay = record.phoneNumber;
                if (record.contactName) {
                    phoneDisplay = record.contactName + " (" + record.phoneNumber + ")";
                }
                document.getElementById("detailPhoneNumber").textContent = phoneDisplay;
                var badge = document.getElementById("detailSentimentBadge");
                var sentLabel = getSentimentLabel(record.overallSentiment);
                badge.textContent = sentLabel;
                badge.className = "sentiment-badge-large " + getSentimentClass(record.overallSentiment);

                document.getElementById("detailCampaign").textContent = record.campaignTitle || "—";
                document.getElementById("detailContactName").textContent = record.contactName || "—";
                document.getElementById("detailDuration").textContent = record.duration || "—";
                document.getElementById("detailDate").textContent = new Date(record.startedAt).toLocaleString();

                // Recording player
                var recordingSection = document.getElementById("recordingSection");
                var recordingPlayer = document.getElementById("recordingPlayer");
                var noRecordingMsg = document.getElementById("noRecordingMsg");
                var recordingTranscriptSection = document.getElementById("recordingTranscriptSection");
                var recordingTranscriptText = document.getElementById("recordingTranscriptText");
                var recordingTranscriptLoading = document.getElementById("recordingTranscriptLoading");
                var transcribeRecordingBtn = document.getElementById("transcribeRecordingBtn");
                if (recordingSection) {
                    recordingSection.style.display = "block";
                    if (record.recordingId) {
                        recordingPlayer.src = apiBaseUrl() + "/api/CallHistory/" + record.callConnectionId + "/recording";
                        recordingPlayer.load();
                        recordingPlayer.style.display = "block";
                        noRecordingMsg.style.display = "none";

                        if (recordingTranscriptSection) {
                            recordingTranscriptSection.style.display = "block";
                            if (recordingTranscriptLoading) recordingTranscriptLoading.classList.add("d-none");

                            if (record.recordingTranscript) {
                                if (recordingTranscriptText) recordingTranscriptText.textContent = record.recordingTranscript;
                                if (transcribeRecordingBtn) transcribeRecordingBtn.style.display = "none";
                            } else {
                                if (recordingTranscriptText) recordingTranscriptText.textContent = "";
                                if (transcribeRecordingBtn) {
                                    transcribeRecordingBtn.style.display = "inline-block";
                                    transcribeRecordingBtn.disabled = false;
                                    transcribeRecordingBtn.onclick = function () {
                                        transcribeRecording(callConnectionId);
                                    };
                                }
                            }
                        }
                    } else {
                        recordingPlayer.style.display = "none";
                        recordingPlayer.src = "";
                        noRecordingMsg.style.display = "block";

                        if (recordingTranscriptSection) recordingTranscriptSection.style.display = "none";
                    }
                }

                if (record.sentimentBreakdown) {
                    setBar("positiveBar", record.sentimentBreakdown.positivePercent);
                    setBar("neutralBar", record.sentimentBreakdown.neutralPercent);
                    setBar("negativeBar", record.sentimentBreakdown.negativePercent);
                }

                if (record.talkTimeRatio) {
                    setBar("aiTalkBar", record.talkTimeRatio.aiPercent);
                    setBar("recipientTalkBar", record.talkTimeRatio.recipientPercent);
                }

                // Operator style traits
                var traitsSection = document.getElementById("operatorTraitsSection");
                if (traitsSection) {
                    if (record.operatorStyleTraits) {
                        traitsSection.style.display = "block";
                        setBar("empathyBar", record.operatorStyleTraits.empathy * 100);
                        setBar("energyBar", record.operatorStyleTraits.energy * 100);
                    } else {
                        traitsSection.style.display = "none";
                    }
                }

                // Post-call summary
                var summarySection = document.getElementById("callSummarySection");
                var summaryText = document.getElementById("callSummaryText");
                if (summarySection) {
                    if (record.callSummary) {
                        summarySection.style.display = "block";
                        if (summaryText) summaryText.textContent = record.callSummary;
                    } else {
                        summarySection.style.display = "none";
                    }
                }

                var transcriptDiv = document.getElementById("detailTranscript");
                transcriptDiv.innerHTML = "";

                if (record.transcriptEntries && record.transcriptEntries.length > 0) {
                    record.transcriptEntries.forEach(function (entry) {
                        var bubble = createChatBubble(entry);
                        transcriptDiv.appendChild(bubble);
                    });
                } else {
                    if (record.recordingTranscript) {
                        transcriptDiv.innerHTML = '<div class="transcript-entry"><div>' + escapeHtml(record.recordingTranscript) + '</div></div>';
                    } else {
                        transcriptDiv.innerHTML = '<p class="text-muted small">No transcript recorded.</p>';
                    }
                }

                panel.classList.remove("d-none");
                panel.scrollIntoView({ behavior: "smooth", block: "start" });
            })
            .catch(function (err) {
                // Hide loading indicator on error
                var detailLoading = document.getElementById("callDetailLoading");
                if (detailLoading) detailLoading.classList.add("d-none");
                showToast("Failed to load call details.");
                console.error("Error loading call detail:", err);
            });
    }

    function transcribeRecording(callConnectionId) {
        var recordingTranscriptText = document.getElementById("recordingTranscriptText");
        var recordingTranscriptLoading = document.getElementById("recordingTranscriptLoading");
        var transcribeRecordingBtn = document.getElementById("transcribeRecordingBtn");

        if (transcribeRecordingBtn) {
            transcribeRecordingBtn.disabled = true;
        }
        if (recordingTranscriptLoading) {
            recordingTranscriptLoading.classList.remove("d-none");
        }

        fetch(apiBaseUrl() + "/api/CallHistory/" + callConnectionId + "/transcribe", {
            method: "POST"
        })
            .then(function (resp) {
                if (!resp.ok) throw new Error("Transcription failed");
                return resp.json();
            })
            .then(function (record) {
                if (recordingTranscriptLoading) recordingTranscriptLoading.classList.add("d-none");
                if (recordingTranscriptText) recordingTranscriptText.textContent = record.recordingTranscript || "";
                if (transcribeRecordingBtn) transcribeRecordingBtn.style.display = "none";
                showToast("Transcript saved.");
            })
            .catch(function (err) {
                if (recordingTranscriptLoading) recordingTranscriptLoading.classList.add("d-none");
                if (transcribeRecordingBtn) transcribeRecordingBtn.disabled = false;
                showToast("Transcription failed.");
                console.error("Error transcribing recording:", err);
            });
    }

    window.closeDetail = function () {
        document.getElementById("callDetailPanel").classList.add("d-none");

        var historyList = document.getElementById("historyList");
        if (historyList) {
            historyList.querySelectorAll(".history-item").forEach(function (el) {
                el.classList.remove("selected");
            });
        }

        ensureLiveOperationsVisible();
        if (activeTabId && calls[activeTabId]) {
            renderCallView(activeTabId);
        }
    };

    // ─── Batch Process All Calls ─────────────────────────────
    (function () {
        var btn = document.getElementById("batchProcessBtn");
        if (!btn) return;
        btn.addEventListener("click", function () {
            btn.disabled = true;
            var statusDiv = document.getElementById("batchProcessStatus");
            var msgSpan = document.getElementById("batchProcessMsg");
            if (statusDiv) statusDiv.classList.remove("d-none");
            if (msgSpan) msgSpan.textContent = "Processing all calls... This may take a few minutes.";

            fetch(apiBaseUrl() + "/api/CallHistory/batch-process", { method: "POST" })
                .then(function (resp) {
                    if (!resp.ok) throw new Error("Batch process failed: " + resp.status);
                    return resp.json();
                })
                .then(function (result) {
                    btn.disabled = false;
                    if (statusDiv) statusDiv.classList.add("d-none");
                    var msg = "Batch complete: " + result.transcribed + " transcribed, " +
                              result.sentimentAnalyzed + " sentiment analyzed, " +
                              result.skipped + " skipped";
                    if (result.failed > 0) msg += ", " + result.failed + " failed";
                    showToast(msg);
                    // Refresh history list to reflect updated data
                    loadCallHistory();
                })
                .catch(function (err) {
                    btn.disabled = false;
                    if (statusDiv) statusDiv.classList.add("d-none");
                    showToast("Batch processing failed.");
                    console.error("Batch process error:", err);
                });
        });
    })();

    // ═══════════════════════════════════════════════════════════
    // MOBILE RESPONSIVE — Tab switching
    // ═══════════════════════════════════════════════════════════

    function initMobileTabs() {
        var tabs = document.querySelectorAll(".mobile-tab");
        tabs.forEach(function (tab) {
            tab.addEventListener("click", function () {
                var panelId = tab.getAttribute("data-panel");
                activateMobileTab(panelId);
            });
        });

        // Default: show left panel on mobile
        var leftPanel = document.getElementById("panelLeft");
        if (leftPanel) leftPanel.classList.add("mobile-active");
    }

    function activateMobileTab(panelId) {
        var tabs = document.querySelectorAll(".mobile-tab");
        tabs.forEach(function (t) {
            t.classList.remove("active");
            if (t.getAttribute("data-panel") === panelId) t.classList.add("active");
        });

        document.querySelectorAll(".panel").forEach(function (p) {
            p.classList.remove("mobile-active");
        });
        var target = document.getElementById(panelId);
        if (target) target.classList.add("mobile-active");
    }

    // ═══════════════════════════════════════════════════════════
    // UI TOGGLES
    // ═══════════════════════════════════════════════════════════

    function setupUIToggles() {
        // Create Campaign toggle
        var createToggle = document.getElementById("createCampaignToggle");
        var createForm = document.getElementById("createCampaignForm");
        var cancelBtn = document.getElementById("cancelCreateCampaign");
        if (createToggle && createForm) {
            createToggle.onclick = function () {
                createForm.classList.toggle("d-none");
                createToggle.classList.toggle("d-none");
            };
        }
        if (cancelBtn && createForm && createToggle) {
            cancelBtn.onclick = function () {
                createForm.classList.add("d-none");
                createToggle.classList.remove("d-none");
            };
        }

        // Prompt override toggle
        var promptToggle = document.getElementById("promptToggle");
        var promptArea = document.getElementById("promptOverrideArea");
        if (promptToggle && promptArea) {
            promptToggle.onclick = function () {
                promptArea.classList.toggle("d-none");
                promptToggle.classList.toggle("expanded");
            };
        }

        // Track manual prompt editing
        var promptInput = document.getElementById("prompt");
        if (promptInput) {
            promptInput.addEventListener("input", function () {
                promptManuallyEdited = promptInput.value.trim().length > 0;
            });
        }

        // Quick responses toggle
        var qrToggle = document.getElementById("quickResponsesToggle");
        var qrList = document.getElementById("quickResponsesList");
        if (qrToggle && qrList) {
            qrToggle.onclick = function () {
                qrList.classList.toggle("d-none");
                qrToggle.classList.toggle("expanded");
            };
        }

        // Call controls
        var muteBtn = document.getElementById("muteBtn");
        if (muteBtn) muteBtn.onclick = toggleMute;

        var holdBtn = document.getElementById("holdBtn");
        if (holdBtn) holdBtn.onclick = toggleHold;

        var endBtn = document.getElementById("endCallBtn");
        if (endBtn) {
            endBtn.onclick = function () {
                if (activeTabId) hangUpCall(activeTabId);
            };
        }
    }

    // ═══════════════════════════════════════════════════════════
    // UTILITIES
    // ═══════════════════════════════════════════════════════════

    function escapeHtml(str) {
        var div = document.createElement("div");
        div.appendChild(document.createTextNode(str || ""));
        return div.innerHTML;
    }

    function setBar(elementId, value) {
        var bar = document.getElementById(elementId);
        if (!bar) return;
        var pct = Math.round(value || 0);
        bar.style.width = pct + "%";
        bar.textContent = pct + "%";
    }

    function getSentimentClass(sentiment) {
        if (!sentiment) return "neutral";
        var s = sentiment.toString().toLowerCase();
        if (s === "positive" || s === "0") return "positive";
        if (s === "negative" || s === "2") return "negative";
        return "neutral";
    }

    function getSentimentLabel(sentiment) {
        if (!sentiment) return "Neutral";
        if (typeof sentiment === "object") {
            var label = sentiment.label;
            if (label === 0 || label === "Positive") return "Positive";
            if (label === 2 || label === "Negative") return "Negative";
            return "Neutral";
        }
        return sentiment.toString();
    }

    // ═══════════════════════════════════════════════════════════
    // INITIALIZATION
    // ═══════════════════════════════════════════════════════════

    document.addEventListener("DOMContentLoaded", function () {
        // Load campaigns
        window.loadCampaigns();

        // Load call history
        window.loadCallHistory();

        // Setup campaign search
        setupCampaignSearch();

        // Bind call initiation button
        var callBtn = document.getElementById("initiateCallBtn");
        if (callBtn) {
            callBtn.addEventListener("click", initiateCall);
        }

        // Setup UI toggles
        setupUIToggles();

        // Initialize mobile tab switching
        initMobileTabs();

        // Render KPIs from session
        renderKPIs();

        // Fetch analytics dashboard from API
        fetchAnalyticsDashboard(kpiTimeRange);

        // Load settings into overlay
        loadSettings();
    });

    // ═══════════════════════════════════════════════════════════
    // SETTINGS OVERLAY
    // ═══════════════════════════════════════════════════════════

    window.toggleSettingsOverlay = function () {
        var overlay = document.getElementById("settingsOverlay");
        if (!overlay) return;
        if (overlay.classList.contains("d-none")) {
            loadSettings();
            overlay.classList.remove("d-none");
        } else {
            overlay.classList.add("d-none");
        }
    };

    function loadSettings() {
        fetch(apiBaseUrl() + "/api/Settings")
            .then(function (resp) { return resp.json(); })
            .then(function (settings) {
                var maxCallInput = document.getElementById("settingsMaxCallTime");
                if (maxCallInput) maxCallInput.value = settings.maxCallTimeMinutes || 2;

                // Voice API mode radio
                var radios = document.querySelectorAll('input[name="voiceApiMode"]');
                radios.forEach(function (r) {
                    r.checked = (r.value === settings.voiceApiMode);
                });

                // VoiceLive configuration status
                var vlOption = document.getElementById("voiceLiveOption");
                var vlRadio = document.querySelector('input[name="voiceApiMode"][value="VoiceLive"]');
                var notConfiguredBadge = document.getElementById("voiceLiveNotConfiguredBadge");
                if (settings.voiceLiveConfigured === false) {
                    if (vlRadio) vlRadio.disabled = true;
                    if (vlOption) vlOption.classList.add("disabled-option");
                    if (notConfiguredBadge) notConfiguredBadge.classList.remove("d-none");
                } else {
                    if (vlRadio) vlRadio.disabled = false;
                    if (vlOption) vlOption.classList.remove("disabled-option");
                    if (notConfiguredBadge) notConfiguredBadge.classList.add("d-none");
                }

                // OpenAI voice dropdown
                var voiceSelect = document.getElementById("settingsVoice");
                if (voiceSelect && settings.selectedVoice) {
                    voiceSelect.value = settings.selectedVoice;
                }

                // VoiceLive model dropdown
                var modelSelect = document.getElementById("settingsVoiceLiveModel");
                if (modelSelect && settings.availableVoiceLiveModels) {
                    modelSelect.innerHTML = "";
                    settings.availableVoiceLiveModels.forEach(function (m) {
                        var opt = document.createElement("option");
                        opt.value = m;
                        opt.textContent = m;
                        modelSelect.appendChild(opt);
                    });
                    if (settings.voiceLiveModel) {
                        modelSelect.value = settings.voiceLiveModel;
                    }
                }

                // Cache available voices for mode switching
                window._vlAvailableVoices = settings.availableVoiceLiveVoices || [];
                window._vlSettings = settings;

                // Transcription source
                var transcriptionSelect = document.getElementById("transcriptionSource");
                if (transcriptionSelect && settings.transcriptionMode) {
                    transcriptionSelect.value = settings.transcriptionMode;
                }

                // Toggle VoiceLive-specific sections
                updateVoiceLiveSections(settings.voiceApiMode);

                // Update the active badge to match the saved mode
                updateActiveModeBadge(settings.voiceApiMode);
            })
            .catch(function (err) {
                console.error("Failed to load settings:", err);
            });
    }

    function updateActiveModeBadge(mode) {
        // Remove active class and badge from all options
        var allOptions = document.querySelectorAll('.settings-radio-option');
        allOptions.forEach(function (opt) {
            opt.classList.remove('active');
            var badge = opt.querySelector('.active-badge');
            if (badge) badge.remove();
        });
        // Add active class and badge to the selected option
        var selectedRadio = document.querySelector('input[name="voiceApiMode"][value="' + mode + '"]');
        if (selectedRadio) {
            var label = selectedRadio.closest('.settings-radio-option');
            if (label) {
                label.classList.add('active');
                var badge = document.createElement('span');
                badge.className = 'radio-badge active-badge';
                badge.textContent = 'Active';
                label.appendChild(badge);
            }
        }
    }

    function updateVoiceLiveSections(mode) {
        var modelGroup = document.getElementById("voiceLiveModelGroup");
        var transcriptionGroup = document.getElementById("transcriptionSourceGroup");
        var voiceSelect = document.getElementById("settingsVoice");
        var voiceGroup = voiceSelect ? voiceSelect.closest(".settings-group") : null;
        var voiceLabel = voiceGroup ? voiceGroup.querySelector(".settings-label") : null;
        var voiceDesc = voiceGroup ? voiceGroup.querySelector(".settings-description") : null;

        if (mode === "VoiceLive") {
            // Show VoiceLive model dropdown
            if (modelGroup) modelGroup.classList.remove("d-none");
            // Show transcription source
            if (transcriptionGroup) transcriptionGroup.classList.remove("d-none");

            // Update voice label and description for VoiceLive
            if (voiceLabel) voiceLabel.textContent = "AI Voice";
            if (voiceDesc) voiceDesc.textContent = "Choose the voice used by the AI agent during VoiceLive calls.";

            // Swap voice dropdown to VoiceLive voices grouped by voice type then locale
            if (voiceSelect && window._vlAvailableVoices && window._vlAvailableVoices.length > 0) {
                voiceSelect.innerHTML = "";
                // Separate voices by voice type
                var voiceTypes = {};
                window._vlAvailableVoices.forEach(function (v) {
                    var vtype = v.voiceType || "DragonHD";
                    if (!voiceTypes[vtype]) voiceTypes[vtype] = [];
                    voiceTypes[vtype].push(v);
                });
                // Render each voice type section (DragonHD first, then MAI-Voice-1, etc.)
                var typeOrder = Object.keys(voiceTypes).sort(function (a, b) {
                    if (a === "DragonHD") return -1;
                    if (b === "DragonHD") return 1;
                    return a.localeCompare(b);
                });
                typeOrder.forEach(function (vtype) {
                    var voices = voiceTypes[vtype];
                    // Group by locale within each voice type
                    var grouped = {};
                    voices.forEach(function (v) {
                        var locale = v.locale || "Unknown";
                        if (!grouped[locale]) grouped[locale] = [];
                        grouped[locale].push(v);
                    });
                    var locales = Object.keys(grouped).sort(function (a, b) {
                        if (a.startsWith("en-US")) return -1;
                        if (b.startsWith("en-US")) return 1;
                        return a.localeCompare(b);
                    });
                    locales.forEach(function (locale) {
                        var optgroup = document.createElement("optgroup");
                        var typeLabel = vtype === "DragonHD" ? "Dragon HD" : vtype;
                        optgroup.label = typeLabel + " — " + locale;
                        grouped[locale].sort(function (a, b) {
                            return (a.displayName || "").localeCompare(b.displayName || "");
                        }).forEach(function (v) {
                            var opt = document.createElement("option");
                            opt.value = v.fullName;
                            opt.textContent = v.displayName + " — " + v.gender;
                            optgroup.appendChild(opt);
                        });
                        voiceSelect.appendChild(optgroup);
                    });
                });
                // Set selected
                if (window._vlSettings && window._vlSettings.selectedVoiceLiveVoice) {
                    voiceSelect.value = window._vlSettings.selectedVoiceLiveVoice;
                }
            }
        } else {
            // Hide VoiceLive model dropdown
            if (modelGroup) modelGroup.classList.add("d-none");
            // Hide transcription source
            if (transcriptionGroup) transcriptionGroup.classList.add("d-none");

            // Restore voice label and description for ChatGPT
            if (voiceLabel) voiceLabel.textContent = "AI Voice";
            if (voiceDesc) voiceDesc.textContent = "Choose the voice used by the AI agent during calls.";

            // Restore OpenAI voices
            if (voiceSelect) {
                voiceSelect.innerHTML = "";
                var openAIVoices = ["alloy", "echo", "fable", "onyx", "nova", "shimmer"];
                openAIVoices.forEach(function (v) {
                    var opt = document.createElement("option");
                    opt.value = v;
                    opt.textContent = v.charAt(0).toUpperCase() + v.slice(1);
                    voiceSelect.appendChild(opt);
                });
                if (window._vlSettings && window._vlSettings.selectedVoice) {
                    voiceSelect.value = window._vlSettings.selectedVoice;
                }
            }
        }
    }

    // Radio change handler for voice API mode
    document.addEventListener("change", function (e) {
        if (e.target && e.target.name === "voiceApiMode") {
            updateVoiceLiveSections(e.target.value);
            updateActiveModeBadge(e.target.value);
        }
    });

    window.saveSettings = function () {
        var maxCallInput = document.getElementById("settingsMaxCallTime");
        var voiceRadio = document.querySelector('input[name="voiceApiMode"]:checked');
        var voiceSelect = document.getElementById("settingsVoice");
        var modelSelect = document.getElementById("settingsVoiceLiveModel");
        var transcriptionSelect = document.getElementById("transcriptionSource");
        var mode = voiceRadio ? voiceRadio.value : "ChatGPT";

        var payload = {
            maxCallTimeMinutes: parseFloat(maxCallInput.value) || 2,
            voiceApiMode: mode,
            selectedVoice: mode === "ChatGPT" ? (voiceSelect ? voiceSelect.value : "alloy") : undefined,
            voiceLiveModel: mode === "VoiceLive" ? (modelSelect ? modelSelect.value : "gpt-4o") : undefined,
            selectedVoiceLiveVoice: mode === "VoiceLive" ? (voiceSelect ? voiceSelect.value : undefined) : undefined,
            transcriptionMode: mode === "VoiceLive" ? (transcriptionSelect ? transcriptionSelect.value : "BuiltIn") : undefined
        };

        var saveBtn = document.getElementById("saveSettingsBtn");
        if (saveBtn) saveBtn.disabled = true;

        fetch(apiBaseUrl() + "/api/Settings", {
            method: "PUT",
            headers: { "Content-Type": "application/json" },
            body: JSON.stringify(payload)
        })
            .then(function (resp) { return resp.json(); })
            .then(function (saved) {
                if (saveBtn) saveBtn.disabled = false;
                showToast("Settings saved.");
                updateActiveModeBadge(mode);
                toggleSettingsOverlay();
            })
            .catch(function (err) {
                if (saveBtn) saveBtn.disabled = false;
                showToast("Failed to save settings.");
                console.error("Error saving settings:", err);
            });
    };

    // ═══════════════════════════════════════════════════════════
    // CENTER PANEL VIEW SWITCHING
    // ═══════════════════════════════════════════════════════════

    window.switchCenterView = function (viewName) {
        // Hide all center views
        var views = ["centerIdle", "activeCallArea", "callDetailPanel", "callDetailLoading",
            "kbManagerView", "intentLibraryView", "qualityDashboardView",
            "agentActivityView", "knowledgeGapsView", "casesListView", "caseDetailView"];
        views.forEach(function (id) {
            var el = document.getElementById(id);
            if (el) el.classList.add("d-none");
        });

        // Update nav pills
        document.querySelectorAll(".center-nav-pill").forEach(function (btn) {
            btn.classList.remove("active");
        });
        var activeBtn = document.querySelector('.center-nav-pill[data-view="' + viewName + '"]');
        // For caseDetailView, highlight the Cases pill
        if (!activeBtn && viewName === "caseDetailView") {
            activeBtn = document.querySelector('.center-nav-pill[data-view="casesListView"]');
        }
        if (activeBtn) activeBtn.classList.add("active");

        if (viewName === "dashboard") {
            // Restore dashboard: show idle or active call
            document.querySelector('.center-nav-pill[data-view="dashboard"]').classList.add("active");
            var hasLiveCalls = Object.keys(calls).length > 0;
            if (hasLiveCalls) {
                document.getElementById("activeCallArea").classList.remove("d-none");
            } else {
                document.getElementById("centerIdle").classList.remove("d-none");
            }
        } else {
            var viewEl = document.getElementById(viewName);
            if (viewEl) {
                viewEl.classList.remove("d-none");
                // Load data on first show
                if (viewName === "kbManagerView") { loadKBCampaignDropdown(); loadKBDocuments(); }
                else if (viewName === "intentLibraryView") loadIntents();
                else if (viewName === "qualityDashboardView") loadQualityDashboard();
                else if (viewName === "agentActivityView") loadAgentActivity();
                else if (viewName === "knowledgeGapsView") loadKnowledgeGaps();
                else if (viewName === "casesListView") loadCenterCases();
            }
        }
    };

    // ═══════════════════════════════════════════════════════════
    // KNOWLEDGE BASE MANAGER
    // ═══════════════════════════════════════════════════════════

    var kbCampaignFilter = "";
    var kbPollingTimer = null;
    var kbCampaigns = [];

    window.setKBCampaignFilter = function (value) {
        kbCampaignFilter = value;
        loadKBDocuments();
    };

    function loadKBCampaignDropdown() {
        var select = document.getElementById("kbCampaignSelect");
        if (!select) return;
        fetch(apiBaseUrl() + "/api/Campaign")
            .then(function (r) { return r.json(); })
            .then(function (campaigns) {
                kbCampaigns = campaigns || [];
                select.innerHTML = '<option value="">All Campaigns</option>';
                kbCampaigns.forEach(function (c) {
                    var opt = document.createElement("option");
                    opt.value = c.id;
                    opt.textContent = c.title;
                    select.appendChild(opt);
                });
                select.value = kbCampaignFilter;
                loadKBDocuments();
            })
            .catch(function () { /* ignore */ });
    }

    function loadKBDocuments() {
        var listEl = document.getElementById("kbDocumentList");
        listEl.innerHTML = '<div class="text-center py-3"><div class="spinner-border spinner-border-sm"></div> Loading...</div>';
        var url = apiBaseUrl() + "/api/knowledgebase";
        if (kbCampaignFilter) url += "?campaignId=" + encodeURIComponent(kbCampaignFilter);
        fetch(url)
            .then(function (r) { return r.json(); })
            .then(function (docs) {
                if (!docs || docs.length === 0) {
                    listEl.innerHTML = '<div class="text-center py-3 text-muted small">No documents uploaded yet.</div>';
                    stopKBPolling();
                    return;
                }
                var hasProcessing = false;
                listEl.innerHTML = "";
                docs.forEach(function (doc) {
                    var statusClass = doc.status === "Indexed" ? "text-success" : doc.status === "Failed" ? "text-danger" : "text-warning";
                    var statusIcon = doc.status === "Indexed" ? "✓" : doc.status === "Failed" ? "✗" : "";
                    var progressText = "";
                    if (doc.status === "Processing" || doc.status === "Uploading") {
                        hasProcessing = true;
                        statusIcon = '<span class="spinner-border spinner-border-sm"></span>';
                        if (doc.processingProgress) {
                            progressText = '<div class="text-warning small">' + escapeHtml(doc.processingProgress) + '</div>';
                        }
                        var est = estimateProcessingTime(doc.fileSizeBytes);
                        progressText += '<div class="text-muted small">Estimated: ~' + est + '</div>';
                    } else if (doc.status === "Failed" && doc.errorMessage) {
                        progressText = '<div class="text-danger small">' + escapeHtml(doc.errorMessage) + '</div>';
                    }
                    var campaignName = getKBCampaignName(doc.campaignId);
                    var campaignLabel = doc.campaignId
                        ? '<span class="badge bg-info ms-1" title="Campaign-specific">' + escapeHtml(campaignName) + '</span>'
                        : '<span class="badge bg-secondary ms-1" title="Available to all campaigns">Global</span>';
                    var campaignControl = renderKBCampaignControl(doc);
                    var card = document.createElement("div");
                    card.className = "document-card";
                    card.innerHTML = '<div class="d-flex justify-content-between align-items-start gap-2">' +
                        '<div><strong>' + escapeHtml(doc.fileName) + '</strong>' +
                        '<span class="badge ms-2 ' + statusClass + '">' + statusIcon + ' ' + doc.status + '</span>' + campaignLabel + '</div>' +
                        '<div class="d-flex gap-1">' +
                        (doc.status === "Failed" ? '<button class="btn btn-sm btn-outline-warning" onclick="retryDocument(\'' + doc.id + '\')">Retry</button>' : '') +
                        '<button class="btn btn-sm btn-outline-danger" onclick="deleteDocument(\'' + doc.id + '\')">Delete</button></div></div>' +
                        progressText +
                        campaignControl +
                        '<div class="text-muted small">' + doc.fileType + ' · ' + formatBytes(doc.fileSizeBytes) +
                        (doc.chunkCount > 0 ? ' · ' + doc.chunkCount + ' chunks' : '') + '</div>';
                    listEl.appendChild(card);
                });
                if (hasProcessing) { startKBPolling(); } else { stopKBPolling(); }
            })
            .catch(function () {
                listEl.innerHTML = '<div class="text-center py-3 text-danger small">Failed to load documents.</div>';
            });
    }

    function estimateProcessingTime(fileSizeBytes) {
        var mb = fileSizeBytes / (1024 * 1024);
        if (mb < 1) return "30s";
        if (mb < 10) return "1 min";
        return "2 min";
    }

    function getKBCampaignName(campaignId) {
        if (!campaignId) return "Global";
        var match = kbCampaigns.find(function (c) { return c.id === campaignId; });
        return match ? match.title : "Campaign";
    }

    function renderKBCampaignControl(doc) {
        var disabled = doc.status !== "Indexed" ? " disabled" : "";
        var title = doc.status !== "Indexed" ? "Campaign can be changed after processing completes" : "Link this document to a campaign";
        var html = '<div class="d-flex align-items-center gap-2 mt-2 kb-document-campaign">' +
            '<span class="text-muted small">Linked to</span>' +
            '<select class="form-select form-select-sm" style="max-width:220px;" title="' + title + '" onchange="updateKBDocumentCampaign(\'' + doc.id + '\', this.value)"' + disabled + '>' +
            '<option value=""' + (!doc.campaignId ? ' selected' : '') + '>Global</option>';

        kbCampaigns.forEach(function (campaign) {
            html += '<option value="' + escapeHtml(campaign.id) + '"' + (doc.campaignId === campaign.id ? ' selected' : '') + '>' + escapeHtml(campaign.title) + '</option>';
        });

        return html + '</select></div>';
    }

    function startKBPolling() {
        if (kbPollingTimer) return;
        kbPollingTimer = setInterval(function () { loadKBDocuments(); }, 3000);
    }

    function stopKBPolling() {
        if (kbPollingTimer) { clearInterval(kbPollingTimer); kbPollingTimer = null; }
    }

    function formatBytes(bytes) {
        if (bytes === 0) return "0 B";
        var k = 1024, sizes = ["B", "KB", "MB", "GB"];
        var i = Math.floor(Math.log(bytes) / Math.log(k));
        return parseFloat((bytes / Math.pow(k, i)).toFixed(1)) + " " + sizes[i];
    }

    window.deleteDocument = function (id) {
        if (!confirm("Delete this document?")) return;
        fetch(apiBaseUrl() + "/api/knowledgebase/" + id, { method: "DELETE" })
            .then(function () { loadKBDocuments(); });
    };

    window.retryDocument = function (id) {
        fetch(apiBaseUrl() + "/api/knowledgebase/" + id + "/retry", { method: "POST" })
            .then(function () { loadKBDocuments(); });
    };

    window.updateKBDocumentCampaign = function (id, campaignId) {
        fetch(apiBaseUrl() + "/api/knowledgebase/" + id + "/campaign", {
            method: "PATCH",
            headers: { "Content-Type": "application/json" },
            body: JSON.stringify({ campaignId: campaignId || null })
        })
            .then(function (response) {
                if (!response.ok) {
                    return response.json().catch(function () { return {}; }).then(function (body) {
                        throw new Error(body.error || body.message || "Failed to update campaign");
                    });
                }
                return response.json();
            })
            .then(function () {
                showToast("Document campaign update started", "success");
                loadKBDocuments();
                startKBPolling();
            })
            .catch(function (err) {
                showToast(err.message, "error");
                loadKBDocuments();
            });
    };

    // File upload via button
    document.getElementById("kbFileUpload").addEventListener("change", function (e) {
        var files = e.target.files;
        if (!files.length) return;
        uploadKBFiles(files);
        e.target.value = "";
    });

    // Drag and drop
    var dropZone = document.getElementById("kbDropZone");
    var kbManagerView = document.getElementById("kbManagerView");
    if (kbManagerView) {
        kbManagerView.addEventListener("dragover", function (e) {
            e.preventDefault();
            dropZone.classList.remove("d-none");
        });
        kbManagerView.addEventListener("dragleave", function (e) {
            if (!kbManagerView.contains(e.relatedTarget)) dropZone.classList.add("d-none");
        });
        kbManagerView.addEventListener("drop", function (e) {
            e.preventDefault();
            dropZone.classList.add("d-none");
            uploadKBFiles(e.dataTransfer.files);
        });
    }

    function uploadKBFiles(files) {
        for (var i = 0; i < files.length; i++) {
            var formData = new FormData();
            formData.append("file", files[i]);
            if (kbCampaignFilter) formData.append("campaignId", kbCampaignFilter);
            fetch(apiBaseUrl() + "/api/knowledgebase", { method: "POST", body: formData })
                .then(function () { loadKBDocuments(); })
                .catch(function (err) { showToast("Upload failed: " + err.message, "error"); });
        }
    }

    // KB Search
    var kbSearchTimer = null;
    document.getElementById("kbSearchInput").addEventListener("input", function () {
        var query = this.value.trim();
        clearTimeout(kbSearchTimer);
        if (!query) {
            document.getElementById("kbSearchResults").classList.add("d-none");
            document.getElementById("kbDocumentList").classList.remove("d-none");
            return;
        }
        kbSearchTimer = setTimeout(function () {
            var body = JSON.stringify({ query: query, top: 10, campaignId: kbCampaignFilter || null });
            fetch(apiBaseUrl() + "/api/knowledgebase/search", {
                method: "POST",
                headers: { "Content-Type": "application/json" },
                body: body
            })
                .then(function (r) { return r.json(); })
                .then(function (data) {
                    var results = data.results || data || [];
                    var resultsEl = document.getElementById("kbSearchResultsList");
                    document.getElementById("kbDocumentList").classList.add("d-none");
                    document.getElementById("kbSearchResults").classList.remove("d-none");
                    if (results.length === 0) {
                        resultsEl.innerHTML = '<div class="text-muted small p-2">No results found.</div>';
                        return;
                    }
                    resultsEl.innerHTML = "";
                    results.forEach(function (r) {
                        var div = document.createElement("div");
                        div.className = "search-result-card";
                        div.innerHTML = '<div class="fw-bold small">' + escapeHtml(r.documentTitle || "Untitled") + '</div>' +
                            '<div class="text-muted small">' + escapeHtml((r.content || "").substring(0, 200)) + '</div>';
                        resultsEl.appendChild(div);
                    });
                });
        }, 300);
    });

    // ═══════════════════════════════════════════════════════════
    // INTENT LIBRARY
    // ═══════════════════════════════════════════════════════════

    var selectedIntentIds = [];

    function loadIntents() {
        var listEl = document.getElementById("intentList");
        listEl.innerHTML = '<div class="text-center py-3"><div class="spinner-border spinner-border-sm"></div> Loading...</div>';
        var statusFilter = document.getElementById("intentStatusFilter");
        var status = statusFilter ? statusFilter.value : "";
        var url = apiBaseUrl() + "/api/intent?limit=100";
        if (status) url += "&status=" + status;
        fetch(url)
            .then(function (r) {
                if (!r.ok) throw new Error("HTTP " + r.status);
                return r.json();
            })
            .then(function (intents) {
                if (!intents || intents.length === 0) {
                    listEl.innerHTML = '<div class="text-center py-3 text-muted small">No intents found. Use "Discover Intents" to analyze call transcripts.</div>';
                    return;
                }
                selectedIntentIds = [];
                updateIntentBulkBar();
                listEl.innerHTML = "";
                intents.forEach(function (intent) {
                    var statusBadge = intent.status === "Approved" ? "bg-success" : intent.status === "Discarded" ? "bg-secondary" : "bg-info";
                    var row = document.createElement("div");
                    row.className = "intent-row";
                    row.innerHTML = '<div class="d-flex align-items-center gap-2">' +
                        '<input type="checkbox" class="intent-checkbox" data-id="' + intent.id + '">' +
                        '<div class="flex-grow-1">' +
                        '<div class="fw-bold small">' + escapeHtml(intent.name) + '</div>' +
                        '<div class="text-muted small">' + escapeHtml(intent.description || "") + '</div>' +
                        '<div class="small"><span class="badge ' + statusBadge + '">' + intent.status + '</span>' +
                        ' <span class="text-muted">Category: ' + escapeHtml(intent.category || "General") + '</span></div></div></div>';
                    listEl.appendChild(row);
                });
                // Wire checkboxes
                document.querySelectorAll(".intent-checkbox").forEach(function (cb) {
                    cb.addEventListener("change", function () {
                        var id = this.getAttribute("data-id");
                        if (this.checked) { selectedIntentIds.push(id); }
                        else { selectedIntentIds = selectedIntentIds.filter(function (x) { return x !== id; }); }
                        updateIntentBulkBar();
                    });
                });
            })
            .catch(function (err) {
                console.error("Failed to load intents:", err);
                listEl.innerHTML = '<div class="text-center py-3 text-danger small">Failed to load intents. Check console for details.</div>';
            });
    }

    function updateIntentBulkBar() {
        var bar = document.getElementById("intentBulkBar");
        if (selectedIntentIds.length > 0) {
            bar.classList.remove("d-none");
            document.getElementById("intentSelectedCount").textContent = selectedIntentIds.length + " selected";
        } else {
            bar.classList.add("d-none");
        }
    }

    document.getElementById("intentStatusFilter").addEventListener("change", function () { loadIntents(); });

    document.getElementById("discoverIntentsBtn").addEventListener("click", function () {
        this.disabled = true;
        this.textContent = "Discovering...";
        var btn = this;
        fetch(apiBaseUrl() + "/api/intent/discover", { method: "POST" })
            .then(function () { btn.disabled = false; btn.textContent = "Discover Intents"; showToast("Intent discovery started", "success"); })
            .catch(function () { btn.disabled = false; btn.textContent = "Discover Intents"; showToast("Discovery failed", "error"); });
    });

    document.getElementById("bulkApproveBtn").addEventListener("click", function () {
        fetch(apiBaseUrl() + "/api/intent/batch", {
            method: "PUT",
            headers: { "Content-Type": "application/json" },
            body: JSON.stringify({ ids: selectedIntentIds, action: "approve" })
        }).then(function () { loadIntents(); });
    });

    document.getElementById("bulkDiscardBtn").addEventListener("click", function () {
        fetch(apiBaseUrl() + "/api/intent/batch", {
            method: "PUT",
            headers: { "Content-Type": "application/json" },
            body: JSON.stringify({ ids: selectedIntentIds, action: "discard" })
        }).then(function () { loadIntents(); });
    });

    // ═══════════════════════════════════════════════════════════
    // QUALITY DASHBOARD
    // ═══════════════════════════════════════════════════════════

    function loadQualityDashboard() {
        // Load dashboard stats
        fetch(apiBaseUrl() + "/api/quality/dashboard")
            .then(function (r) { return r.json(); })
            .then(function (d) {
                document.getElementById("qAvgScore").textContent = d.averageScore != null ? d.averageScore.toFixed(1) : "—";
                document.getElementById("qTotalEvals").textContent = d.totalEvaluations || "0";
                document.getElementById("qFlagged").textContent = d.flaggedCount || "0";
                document.getElementById("qPassRate").textContent = d.totalEvaluations > 0 ? Math.round(((d.totalEvaluations - (d.flaggedCount || 0)) / d.totalEvaluations) * 100) + "%" : "—";
            })
            .catch(function () {});

        // Load evaluations
        var listEl = document.getElementById("qualityEvalList");
        listEl.innerHTML = '<div class="text-center py-3"><div class="spinner-border spinner-border-sm"></div> Loading...</div>';
        fetch(apiBaseUrl() + "/api/quality?limit=50")
            .then(function (r) { return r.json(); })
            .then(function (evals) {
                if (!evals || evals.length === 0) {
                    listEl.innerHTML = '<div class="text-center py-3 text-muted small">No evaluations yet. Quality evaluations are created automatically after calls.</div>';
                    return;
                }
                listEl.innerHTML = "";
                evals.forEach(function (ev) {
                    var scoreClass = ev.overallScore >= 4 ? "text-success" : ev.overallScore >= 3 ? "text-warning" : "text-danger";
                    var card = document.createElement("div");
                    card.className = "eval-card" + (ev.flagged ? " flagged" : "");
                    card.style.cursor = "pointer";
                    card.onclick = function () { showEvalDetail(ev.callRecordId); };
                    card.innerHTML = '<div class="d-flex justify-content-between">' +
                        '<div><span class="fw-bold ' + scoreClass + '">' + (ev.overallScore || 0).toFixed(1) + '</span>/5' +
                        (ev.flagged ? ' <span class="badge bg-danger">Flagged</span>' : '') + '</div>' +
                        '<div class="text-muted small">' + new Date(ev.evaluatedAt).toLocaleDateString() + '</div></div>' +
                        '<div class="text-muted small">Call: ' + (ev.callRecordId || "Unknown").substring(0, 8) + '... <span class="text-info" style="font-size:0.7rem;">Click for details</span></div>';
                    listEl.appendChild(card);
                });
            })
            .catch(function () {
                listEl.innerHTML = '<div class="text-center py-3 text-danger small">Failed to load evaluations.</div>';
            });
    }

    document.getElementById("qualityPeriod").addEventListener("change", function () { loadQualityDashboard(); });

    // ─── Quality Eval Detail View ────────────────────────────
    function showEvalDetail(callRecordId) {
        var detailEl = document.getElementById("qualityEvalDetail");
        var listEl = document.getElementById("qualityEvalList");
        var statsEl = document.getElementById("qualityStatsGrid");
        if (!detailEl) return;

        listEl.classList.add("d-none");
        statsEl.classList.add("d-none");
        detailEl.classList.remove("d-none");
        document.getElementById("evalCriteriaList").innerHTML = '<div class="text-center py-3"><div class="spinner-border spinner-border-sm"></div> Loading details...</div>';

        fetch(apiBaseUrl() + "/api/quality/" + encodeURIComponent(callRecordId))
            .then(function (r) {
                if (!r.ok) throw new Error("HTTP " + r.status);
                return r.json();
            })
            .then(function (ev) {
                // Overall score
                var score = ev.overallScore || 0;
                var scoreClass = score >= 4 ? "score-good" : score >= 3 ? "score-ok" : "score-bad";
                document.getElementById("evalDetailTitle").innerHTML =
                    '<button onclick="closeEvalDetail()" class="btn btn-sm btn-outline-secondary me-2">&larr; Back</button>' +
                    'Evaluation Detail';
                document.getElementById("evalDetailSummary").innerHTML =
                    '<div class="d-flex align-items-center gap-3">' +
                    '<div class="eval-detail-score ' + scoreClass + '">' + score.toFixed(1) + '</div>' +
                    '<div><div style="font-size:0.85rem;color:#c9d1d9;">Overall Score / 5.0</div>' +
                    '<div class="eval-detail-meta">Call: ' + (ev.callRecordId || "Unknown").substring(0, 12) + '...' +
                    (ev.flagged ? ' &bull; <span class="text-danger">Flagged</span>' : '') +
                    ' &bull; ' + new Date(ev.evaluatedAt).toLocaleString() + '</div></div></div>';

                // Criteria list
                var criteriaEl = document.getElementById("evalCriteriaList");
                if (!ev.criterionScores || ev.criterionScores.length === 0) {
                    criteriaEl.innerHTML = '<div class="text-muted small">No criterion scores available.</div>';
                    return;
                }
                criteriaEl.innerHTML = "";
                ev.criterionScores.forEach(function (cs) {
                    var s = cs.score || 0;
                    var cls = s >= 4 ? "good" : s >= 3 ? "ok" : "bad";
                    var pct = Math.min((s / 5) * 100, 100);
                    var card = document.createElement("div");
                    card.className = "criterion-card criterion-" + cls;
                    card.innerHTML =
                        '<div class="criterion-header">' +
                        '<span class="criterion-name">' + (cs.criterionName || "Unknown") + '</span>' +
                        '<span class="criterion-score-badge score-' + cls + '">' + s.toFixed(1) + '/5</span></div>' +
                        '<div class="criterion-bar"><div class="criterion-bar-fill bar-' + cls + '" style="width:' + pct + '%"></div></div>' +
                        '<div class="criterion-justification">' + (cs.justification || "No justification provided.") + '</div>';
                    criteriaEl.appendChild(card);
                });
            })
            .catch(function (err) {
                document.getElementById("evalCriteriaList").innerHTML = '<div class="text-danger small">Failed to load details: ' + err.message + '</div>';
            });
    }

    window.closeEvalDetail = function () {
        document.getElementById("qualityEvalDetail").classList.add("d-none");
        document.getElementById("qualityEvalList").classList.remove("d-none");
        document.getElementById("qualityStatsGrid").classList.remove("d-none");
    };

    // Criteria editor
    document.getElementById("editCriteriaBtn").addEventListener("click", function () {
        document.getElementById("criteriaEditor").classList.remove("d-none");
        document.getElementById("qualityStatsGrid").classList.add("d-none");
        document.getElementById("qualityEvalList").classList.add("d-none");
        fetch(apiBaseUrl() + "/api/quality/criteria")
            .then(function (r) { return r.json(); })
            .then(function (criteria) {
                var listEl = document.getElementById("criteriaList");
                listEl.innerHTML = "";
                criteria.forEach(function (c, i) {
                    listEl.innerHTML += '<div class="criteria-item mb-2 p-2 border rounded">' +
                        '<input class="form-control form-control-sm mb-1 criteria-name" value="' + escapeHtml(c.name) + '" placeholder="Criterion name">' +
                        '<input class="form-control form-control-sm mb-1 criteria-desc" value="' + escapeHtml(c.description) + '" placeholder="Description">' +
                        '<div class="d-flex gap-2"><input type="number" class="form-control form-control-sm criteria-weight" value="' + c.weight + '" min="1" max="10" placeholder="Weight">' +
                        '<input type="number" class="form-control form-control-sm criteria-min" value="' + c.minimumPassingScore + '" min="1" max="5" step="0.5" placeholder="Min score"></div></div>';
                });
            });
    });

    document.getElementById("closeCriteriaBtn").addEventListener("click", function () {
        document.getElementById("criteriaEditor").classList.add("d-none");
        document.getElementById("qualityStatsGrid").classList.remove("d-none");
        document.getElementById("qualityEvalList").classList.remove("d-none");
    });

    document.getElementById("saveCriteriaBtn").addEventListener("click", function () {
        var criteria = [];
        document.querySelectorAll(".criteria-item").forEach(function (item) {
            criteria.push({
                name: item.querySelector(".criteria-name").value,
                description: item.querySelector(".criteria-desc").value,
                weight: parseInt(item.querySelector(".criteria-weight").value) || 5,
                minimumPassingScore: parseFloat(item.querySelector(".criteria-min").value) || 3
            });
        });
        fetch(apiBaseUrl() + "/api/quality/criteria", {
            method: "PUT",
            headers: { "Content-Type": "application/json" },
            body: JSON.stringify(criteria)
        }).then(function () {
            document.getElementById("closeCriteriaBtn").click();
            showToast("Criteria saved", "success");
        });
    });

    // ═══════════════════════════════════════════════════════════
    // AGENT ACTIVITY
    // ═══════════════════════════════════════════════════════════

    window.loadAgentActivity = function () {
        var listEl = document.getElementById("agentActivityList");
        listEl.innerHTML = '<div class="text-center py-3"><div class="spinner-border spinner-border-sm"></div> Loading...</div>';
        var agentType = document.getElementById("agentTypeFilter").value;
        var url = apiBaseUrl() + "/api/agentactivity?limit=100";
        if (agentType) url += "&agentType=" + agentType;
        fetch(url)
            .then(function (r) { return r.json(); })
            .then(function (activities) {
                if (!activities || activities.length === 0) {
                    listEl.innerHTML = '<div class="text-center py-3 text-muted small">No agent activity recorded yet.</div>';
                    document.getElementById("agentTotalActions").textContent = "0";
                    return;
                }
                document.getElementById("agentTotalActions").textContent = activities.length;
                var failures = activities.filter(function (a) { return a.result === "Failure" || a.result === 2; }).length;
                document.getElementById("agentFailures").textContent = failures;
                document.getElementById("agentSuccessRate").textContent = Math.round(((activities.length - failures) / activities.length) * 100) + "%";
                listEl.innerHTML = "";
                activities.forEach(function (a) {
                    var resultClass = (a.result === "Success" || a.result === 0) ? "text-success" : (a.result === "Failure" || a.result === 2) ? "text-danger" : "text-warning";
                    var resultLabel = (a.result === 0 || a.result === "Success") ? "Success" : (a.result === 2 || a.result === "Failure") ? "Failure" : "Partial";
                    var card = document.createElement("div");
                    card.className = "activity-card";
                    card.innerHTML = '<div class="d-flex justify-content-between">' +
                        '<div><strong class="small">' + escapeHtml(a.agentType || a.agentName || "Agent") + '</strong>' +
                        ' <span class="' + resultClass + ' small">' + resultLabel + '</span></div>' +
                        '<div class="text-muted small">' + new Date(a.timestamp || a.executedAt).toLocaleTimeString() + '</div></div>' +
                        '<div class="text-muted small">' + escapeHtml(a.action || a.actionDescription || "") + '</div>' +
                        (a.errorMessage ? '<div class="text-danger small">' + escapeHtml(a.errorMessage) + '</div>' : '');
                    listEl.appendChild(card);
                });
            })
            .catch(function () {
                listEl.innerHTML = '<div class="text-center py-3 text-danger small">Failed to load agent activity.</div>';
            });
    };

    document.getElementById("agentTypeFilter").addEventListener("change", function () { loadAgentActivity(); });

    // ─── Live Agent Processing Handler ───────────────────────
    var processingFirstEvent = true;

    function handleAgentProcessingUpdate(data) {
        var card = document.getElementById("agentProcessingCard");
        if (!card) return;

        // On first event, switch to Agent Activity view and show card
        if (processingFirstEvent && data.status !== "complete") {
            processingFirstEvent = false;
            switchCenterView("agentActivityView");
            card.classList.remove("d-none");
            card.classList.remove("processing-card-complete");
            document.getElementById("processingCallId").textContent = (data.callConnectionId || "").substring(0, 12) + "...";
            // Reset all rows
            ["caseManagement", "qualityEvaluation", "knowledgeGapDetection", "summaryGeneration", "intentDiscovery"].forEach(function (name) {
                var row = card.querySelector('[data-agent="' + name + '"]');
                if (row) {
                    row.className = "processing-agent-row";
                    document.getElementById("icon-" + name).textContent = "⏳";
                    document.getElementById("status-" + name).textContent = "Pending";
                }
            });
        }

        if (data.status === "complete") {
            // All done
            card.classList.add("processing-card-complete");
            document.getElementById("processingProgressFill").style.width = "100%";
            document.getElementById("processingProgressLabel").textContent = "Complete";
            processingFirstEvent = true; // Reset for next call
            // Refresh agent activity list
            setTimeout(function () { loadAgentActivity(); }, 500);
            // Auto-hide after 8 seconds
            setTimeout(function () {
                card.classList.add("d-none");
            }, 8000);
            return;
        }

        var agentName = data.agentName;
        var row = card.querySelector('[data-agent="' + agentName + '"]');
        if (!row) return;

        var iconEl = document.getElementById("icon-" + agentName);
        var statusEl = document.getElementById("status-" + agentName);

        if (data.status === "processing") {
            row.className = "processing-agent-row active";
            iconEl.textContent = "🔄";
            statusEl.textContent = "Processing...";
        } else if (data.status === "success") {
            row.className = "processing-agent-row done";
            iconEl.textContent = "✅";
            statusEl.textContent = "Done";
        } else if (data.status === "failed") {
            row.className = "processing-agent-row failed";
            iconEl.textContent = "❌";
            statusEl.textContent = "Failed";
        }

        // Update progress bar
        document.getElementById("processingProgressFill").style.width = data.progress + "%";
        document.getElementById("processingProgressLabel").textContent = data.progress + "% complete";
    }

    // ═══════════════════════════════════════════════════════════
    // KNOWLEDGE GAPS
    // ═══════════════════════════════════════════════════════════

    function loadKnowledgeGaps() {
        var listEl = document.getElementById("gapList");
        listEl.innerHTML = '<div class="text-center py-3"><div class="spinner-border spinner-border-sm"></div> Loading...</div>';
        var status = document.getElementById("gapStatusFilter").value;
        var url = apiBaseUrl() + "/api/knowledgegap?limit=100";
        if (status) url += "&status=" + status;
        fetch(url)
            .then(function (r) { return r.json(); })
            .then(function (gaps) {
                if (!gaps || gaps.length === 0) {
                    listEl.innerHTML = '<div class="text-center py-3 text-muted small">No knowledge gaps detected yet.</div>';
                    return;
                }
                listEl.innerHTML = "";
                gaps.forEach(function (g) {
                    var statusBadge = g.status === "Published" ? "bg-success" : g.status === "Dismissed" ? "bg-secondary" : g.status === "ArticleDrafted" ? "bg-info" : "bg-warning";
                    var card = document.createElement("div");
                    card.className = "gap-card";
                    card.style.cursor = "pointer";
                    card.onclick = function () { showGapDetail(g); };
                    card.innerHTML = '<div class="d-flex justify-content-between">' +
                        '<div class="fw-bold small">' + escapeHtml(g.topic) + '</div>' +
                        '<span class="badge ' + statusBadge + '">' + g.status + '</span></div>' +
                        '<div class="text-muted small">Frequency: ' + g.frequency + ' · ' + (g.sampleQuestions || []).length + ' samples</div>';
                    listEl.appendChild(card);
                });
            })
            .catch(function () {
                listEl.innerHTML = '<div class="text-center py-3 text-danger small">Failed to load knowledge gaps.</div>';
            });
    }

    document.getElementById("gapStatusFilter").addEventListener("change", function () { loadKnowledgeGaps(); });

    function showGapDetail(gap) {
        document.getElementById("gapList").classList.add("d-none");
        document.getElementById("gapDetail").classList.remove("d-none");
        document.getElementById("gapDetailTitle").textContent = gap.topic;
        var content = document.getElementById("gapDetailContent");
        content.innerHTML = '<div class="mb-2"><strong>Status:</strong> ' + gap.status + '</div>' +
            '<div class="mb-2"><strong>Frequency:</strong> ' + gap.frequency + '</div>' +
            '<div class="mb-2"><strong>Sample Questions:</strong><ul>' +
            (gap.sampleQuestions || []).map(function (q) { return '<li class="small">' + escapeHtml(q) + '</li>'; }).join('') + '</ul></div>' +
            (gap.suggestedTitle ? '<div class="mb-2"><strong>Suggested Title:</strong> ' + escapeHtml(gap.suggestedTitle) + '</div>' : '') +
            (gap.suggestedArticle ? '<div class="mb-2"><strong>Draft Article:</strong><div class="border rounded p-2 small">' + escapeHtml(gap.suggestedArticle) + '</div></div>' : '') +
            '<div class="d-flex gap-2 mt-3">' +
            (gap.status !== "Published" && gap.status !== "Dismissed" ?
                '<button class="btn btn-sm btn-success" onclick="publishGap(\'' + gap.id + '\')">Publish</button>' +
                '<button class="btn btn-sm btn-outline-secondary" onclick="dismissGap(\'' + gap.id + '\')">Dismiss</button>' : '') +
            '</div>';
    }

    window.closeGapDetail = function () {
        document.getElementById("gapDetail").classList.add("d-none");
        document.getElementById("gapList").classList.remove("d-none");
    };

    window.publishGap = function (id) {
        fetch(apiBaseUrl() + "/api/knowledgegap/" + id + "/publish", { method: "POST" })
            .then(function () { loadKnowledgeGaps(); closeGapDetail(); showToast("Gap published to KB", "success"); });
    };

    window.dismissGap = function (id) {
        fetch(apiBaseUrl() + "/api/knowledgegap/" + id + "/dismiss", { method: "POST" })
            .then(function () { loadKnowledgeGaps(); closeGapDetail(); });
    };

    // ═══════════════════════════════════════════════════════════
    // CASES (Center Panel View)
    // ═══════════════════════════════════════════════════════════

    window.loadCenterCases = function () {
        var listEl = document.getElementById("centerCasesList");
        var filterEl = document.getElementById("casesStatusFilter");
        var statusParam = filterEl && filterEl.value ? "&status=" + encodeURIComponent(filterEl.value) : "";
        listEl.innerHTML = '<div class="text-center py-3"><div class="spinner-border spinner-border-sm"></div> Loading...</div>';
        fetch(apiBaseUrl() + "/api/case?limit=100" + statusParam)
            .then(function (r) {
                if (!r.ok) throw new Error("HTTP " + r.status);
                return r.json();
            })
            .then(function (cases) {
                // Update stats
                var total = cases.length;
                var open = cases.filter(function (c) { return c.status === "Open" || c.status === 0; }).length;
                var resolved = cases.filter(function (c) { return c.status === "Resolved" || c.status === 2; }).length;
                var totalEl = document.getElementById("casesTotal");
                var openEl = document.getElementById("casesOpen");
                var resolvedEl = document.getElementById("casesResolved");
                if (totalEl) totalEl.textContent = total;
                if (openEl) openEl.textContent = open;
                if (resolvedEl) resolvedEl.textContent = resolved;

                if (!cases || cases.length === 0) {
                    listEl.innerHTML = '<div class="text-center py-4 text-muted small">No cases found.</div>';
                    return;
                }
                listEl.innerHTML = "";
                cases.forEach(function (c) {
                    var statusLabel = typeof c.status === "number" ? ["Open", "InProgress", "Resolved", "Closed"][c.status] || c.status : c.status;
                    var priorityLabel = typeof c.priority === "number" ? ["Low", "Medium", "High", "Critical", "Escalation"][c.priority] || c.priority : c.priority;
                    var statusClass = statusLabel === "Open" ? "bg-primary" : statusLabel === "InProgress" ? "bg-warning text-dark" : statusLabel === "Resolved" ? "bg-success" : "bg-secondary";
                    var priorityClass = priorityLabel === "Critical" || priorityLabel === "Escalation" ? "text-danger fw-bold" : priorityLabel === "High" ? "text-warning" : "";
                    var card = document.createElement("div");
                    card.className = "case-card";
                    card.onclick = function () { showCaseDetail(c.id); };
                    card.innerHTML = '<div class="d-flex justify-content-between align-items-start">' +
                        '<div class="flex-grow-1">' +
                        '<div class="fw-bold small">' + escapeHtml(c.title || "Untitled Case") + '</div>' +
                        '<div class="text-muted small mt-1">' + escapeHtml(c.description || "").substring(0, 120) + '</div>' +
                        (c.callerName ? '<div class="text-muted small mt-1">Caller: ' + escapeHtml(c.callerName) + '</div>' : '') +
                        '</div>' +
                        '<div class="d-flex flex-column align-items-end gap-1 ms-2">' +
                        '<span class="badge ' + statusClass + '" style="font-size:0.65rem;">' + statusLabel + '</span>' +
                        (priorityLabel ? '<span class="small ' + priorityClass + '">' + priorityLabel + '</span>' : '') +
                        '</div></div>';
                    listEl.appendChild(card);
                });
            })
            .catch(function () {
                listEl.innerHTML = '<div class="text-center py-3 text-danger small">Failed to load cases. Check API connection.</div>';
            });
    };

    // Filter change triggers reload
    var casesFilterEl = document.getElementById("casesStatusFilter");
    if (casesFilterEl) {
        casesFilterEl.addEventListener("change", function () { loadCenterCases(); });
    }

    // ═══════════════════════════════════════════════════════════
    // CASES (Right Panel Tab + Detail View)
    // ═══════════════════════════════════════════════════════════

    function loadCases() {
        var listEl = document.getElementById("casesList");
        var loadingEl = document.getElementById("casesListLoading");
        var emptyEl = document.getElementById("casesListEmpty");
        loadingEl.classList.remove("d-none");
        listEl.classList.add("d-none");
        emptyEl.classList.add("d-none");
        fetch(apiBaseUrl() + "/api/case?limit=50")
            .then(function (r) { return r.json(); })
            .then(function (cases) {
                loadingEl.classList.add("d-none");
                if (!cases || cases.length === 0) {
                    emptyEl.classList.remove("d-none");
                    return;
                }
                listEl.classList.remove("d-none");
                listEl.innerHTML = "";
                cases.forEach(function (c) {
                    var statusDot = c.status === "Open" ? "bg-primary" : c.status === "InProgress" ? "bg-warning" : c.status === "Resolved" ? "bg-success" : "bg-secondary";
                    var priorityBadge = c.priority === "Critical" || c.priority === "Escalation" ? "text-danger" : c.priority === "High" ? "text-warning" : "";
                    var card = document.createElement("div");
                    card.className = "case-card";
                    card.style.cursor = "pointer";
                    card.onclick = function () { showCaseDetail(c.id); };
                    card.innerHTML = '<div class="d-flex align-items-center gap-2">' +
                        '<span class="status-dot ' + statusDot + '"></span>' +
                        '<div class="flex-grow-1"><div class="fw-bold small">' + escapeHtml(c.title) + '</div>' +
                        '<div class="text-muted small">' + c.status + (c.priority ? ' · <span class="' + priorityBadge + '">' + c.priority + '</span>' : '') + '</div></div></div>';
                    listEl.appendChild(card);
                });
            })
            .catch(function () {
                loadingEl.classList.add("d-none");
                listEl.innerHTML = '<div class="text-center py-3 text-danger small">Failed to load cases.</div>';
                listEl.classList.remove("d-none");
            });
    }

    function showCaseDetail(caseId) {
        switchCenterView("caseDetailView");
        var content = document.getElementById("caseDetailContent");
        content.innerHTML = '<div class="text-center py-3"><div class="spinner-border spinner-border-sm"></div> Loading...</div>';
        fetch(apiBaseUrl() + "/api/case/" + caseId)
            .then(function (r) { return r.json(); })
            .then(function (c) {
                document.getElementById("caseDetailTitle").textContent = c.title || "Case Detail";
                content.innerHTML = '<div class="mb-3">' +
                    '<div class="mb-2"><strong>Status:</strong> <span class="badge bg-' + (c.status === "Open" ? "primary" : c.status === "Resolved" ? "success" : "secondary") + '">' + c.status + '</span></div>' +
                    '<div class="mb-2"><strong>Priority:</strong> ' + (c.priority || "Medium") + '</div>' +
                    '<div class="mb-2"><strong>Description:</strong> ' + escapeHtml(c.description || "—") + '</div>' +
                    (c.callerName ? '<div class="mb-2"><strong>Caller:</strong> ' + escapeHtml(c.callerName) + '</div>' : '') +
                    (c.intent ? '<div class="mb-2"><strong>Intent:</strong> ' + escapeHtml(c.intent) + '</div>' : '') +
                    '<div class="mb-2"><strong>Created:</strong> ' + new Date(c.createdAt).toLocaleString() + '</div>' +
                    (c.resolutionSummary ? '<div class="mb-2"><strong>Resolution:</strong> ' + escapeHtml(c.resolutionSummary) + '</div>' : '') +
                    '<div class="mb-2"><strong>Linked Calls:</strong> ' + (c.linkedCallRecords || []).length + '</div>' +
                    '</div>' +
                    '<div class="d-flex gap-2">' +
                    (c.status === "Open" || c.status === "InProgress" ? '<button class="btn btn-sm btn-success" onclick="resolveCase(\'' + c.id + '\')">Resolve</button>' : '') +
                    (c.status === "Resolved" ? '<button class="btn btn-sm btn-secondary" onclick="closeCase(\'' + c.id + '\')">Close</button>' : '') +
                    '</div>';
            })
            .catch(function () {
                content.innerHTML = '<div class="text-danger">Failed to load case.</div>';
            });
    }

    window.closeCaseDetail = function () {
        switchCenterView("casesListView");
    };

    window.resolveCase = function (id) {
        var summary = prompt("Resolution summary:");
        if (summary === null) return;
        fetch(apiBaseUrl() + "/api/case/" + id + "/resolve", {
            method: "POST",
            headers: { "Content-Type": "application/json" },
            body: JSON.stringify({ resolutionSummary: summary })
        }).then(function () { showCaseDetail(id); loadCases(); });
    };

    window.closeCase = function (id) {
        fetch(apiBaseUrl() + "/api/case/" + id + "/close", { method: "POST" })
            .then(function () { showCaseDetail(id); loadCases(); });
    };

    // Load cases when Cases tab is shown
    document.getElementById("cases-tab").addEventListener("shown.bs.tab", function () { loadCases(); });

    // ═══════════════════════════════════════════════════════════
    // INBOUND STATUS + WEBRTC CALL LINK
    // ═══════════════════════════════════════════════════════════

    function checkInboundStatus() {
        fetch(apiBaseUrl() + "/api/inbound/status")
            .then(function (r) { return r.json(); })
            .then(function (data) {
                var section = document.getElementById("inboundStatusSection");
                if (data.configured) {
                    section.style.display = "";
                    document.getElementById("inboundStatusDot").className = "inbound-status-dot active";
                    document.getElementById("inboundStatusText").textContent = "Inbound: Active";
                } else {
                    section.style.display = "none";
                }
            })
            .catch(function () {});
    }

    function checkCallOverInternet() {
        fetch(apiBaseUrl() + "/api/settings")
            .then(function (r) { return r.json(); })
            .then(function (settings) {
                var section = document.getElementById("callLinkSection");
                if (settings.callOverInternet) {
                    section.style.display = "";
                } else {
                    section.style.display = "none";
                }
            })
            .catch(function () {});
    }

    document.getElementById("generateCallLinkBtn").addEventListener("click", function () {
        var btn = this;
        btn.disabled = true;
        var campaignId = (selectedCampaign || {}).id || "";
        fetch(apiBaseUrl() + "/api/webrtc/sessions", {
            method: "POST",
            headers: { "Content-Type": "application/json" },
            body: JSON.stringify({ campaignId: campaignId })
        })
            .then(function (r) { return r.json(); })
            .then(function (session) {
                btn.disabled = false;
                var linkCard = document.getElementById("callLinkCard");
                linkCard.classList.remove("d-none");
                var url = window.location.origin + "/JoinCall?sessionId=" + session.sessionId;
                document.getElementById("callLinkUrl").value = url;
                document.getElementById("callLinkExpiry").textContent = "Expires: " + new Date(session.expiresAt).toLocaleString();
            })
            .catch(function () { btn.disabled = false; showToast("Failed to generate link", "error"); });
    });

    document.getElementById("copyCallLinkBtn").addEventListener("click", function () {
        var urlInput = document.getElementById("callLinkUrl");
        navigator.clipboard.writeText(urlInput.value).then(function () { showToast("Link copied!", "success"); });
    });

    // Knowledge source attribution on transcript bubbles
    function addKBAttribution(bubble, sources) {
        if (!sources || sources.length === 0) return;
        var tooltip = document.createElement("span");
        tooltip.className = "kb-attribution";
        tooltip.title = "Sources: " + sources.map(function(s) { return s.documentTitle || s; }).join(", ");
        tooltip.textContent = " 📄";
        bubble.appendChild(tooltip);
    }

    // Check inbound/internet status on load
    setTimeout(function() {
        checkInboundStatus();
        checkCallOverInternet();
    }, 1000);
})();
