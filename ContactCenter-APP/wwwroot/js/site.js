// ═══════════════════════════════════════════════════════════
// Contact Center Operations — Professional Dashboard
// Rich campaign cards, call controls, chat-bubble transcript,
// KPI tracking, quick responses, sentiment graph
// ═══════════════════════════════════════════════════════════

(function () {
    "use strict";

    // ─── State ───────────────────────────────────────────────
    var connection = null;
    // callId -> { phoneNumber, campaignTitle, status, transcriptEntries, sentimentData, timerInterval, timerSeconds }
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

        fetch(apiBaseUrl() + "/api/Campaign")
            .then(function (resp) { return resp.json(); })
            .then(function (campaigns) {
                list.innerHTML = "";
                window._allCampaigns = campaigns;
                renderCampaignCards(campaigns, list);
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

        updateTabs();
    }

    function updateTabs() {
        var tabContainer = document.getElementById("callTabs");
        var callIds = Object.keys(calls);

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
        } else {
            if (activeTabId === callConnectionId) {
                activeTabId = remaining[0];
            }
            updateTabs();
            renderCallView(activeTabId);
        }
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
            sentiment: entry.sentiment || null
        };
        c.transcriptEntries.push(entryData);

        if (entryData.sentiment && entryData.sentiment.label !== undefined) {
            c.sentimentData.push(sentimentToNumber(entryData.sentiment.label));
        } else {
            c.sentimentData.push(0);
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

    // ─── Call Status ─────────────────────────────────────────
    function handleCallStatus(callConnectionId, status) {
        var c = calls[callConnectionId];
        if (!c) return;

        var statusStr = typeof status === "number"
            ? ["Initiating", "Ringing", "Connected", "Disconnected", "Failed"][status]
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
            default: badge.classList.add("bg-secondary");
        }
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

    // ═══════════════════════════════════════════════════════════
    // RIGHT PANEL — Call History
    // ═══════════════════════════════════════════════════════════

    window.loadCallHistory = function () {
        var loading = document.getElementById("historyLoading");
        var empty = document.getElementById("historyEmpty");
        var list = document.getElementById("historyList");

        loading.classList.remove("d-none");
        empty.classList.add("d-none");
        list.classList.add("d-none");
        document.getElementById("callDetailPanel").classList.add("d-none");

        fetch(apiBaseUrl() + "/api/CallHistory")
            .then(function (resp) { return resp.json(); })
            .then(function (data) {
                loading.classList.add("d-none");

                if (!data || data.length === 0) {
                    empty.classList.remove("d-none");
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
        document.querySelectorAll(".history-item").forEach(function (el) {
            el.classList.remove("selected");
        });
        if (element) element.classList.add("selected");

        fetch(apiBaseUrl() + "/api/CallHistory/" + callConnectionId)
            .then(function (resp) {
                if (!resp.ok) throw new Error("Not found");
                return resp.json();
            })
            .then(function (record) {
                var panel = document.getElementById("callDetailPanel");

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

                var transcriptDiv = document.getElementById("detailTranscript");
                transcriptDiv.innerHTML = "";

                if (record.transcriptEntries && record.transcriptEntries.length > 0) {
                    record.transcriptEntries.forEach(function (entry) {
                        var div = document.createElement("div");
                        var speakerLabel = (entry.speaker === 0 || entry.speaker === "AI") ? "AI" : "Recipient";
                        var sentimentLabel = getSentimentLabel(entry.sentiment);
                        var sentimentCls = getSentimentClass(sentimentLabel);

                        div.className = "transcript-entry speaker-" + speakerLabel.toLowerCase();
                        div.innerHTML =
                            '<div class="d-flex justify-content-between align-items-center">' +
                            '  <strong class="' + (speakerLabel === "AI" ? "text-primary" : "text-info") + '">' + speakerLabel + '</strong>' +
                            '  <span class="sentiment-badge-large ' + sentimentCls + ' small">' + sentimentLabel + '</span>' +
                            '</div>' +
                            '<div>' + escapeHtml(entry.text || "") + '</div>' +
                            '<small class="text-muted">' + new Date(entry.timestamp).toLocaleTimeString() + '</small>';
                        transcriptDiv.appendChild(div);
                    });
                } else {
                    transcriptDiv.innerHTML = '<p class="text-muted small">No transcript recorded.</p>';
                }

                panel.classList.remove("d-none");
                panel.scrollIntoView({ behavior: "smooth", block: "start" });
            })
            .catch(function (err) {
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
        document.querySelectorAll(".history-item").forEach(function (el) {
            el.classList.remove("selected");
        });
    };

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
    });
})();
