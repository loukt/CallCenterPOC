// ═══════════════════════════════════════════════════════════
// WebRTC Call Client — JoinCall page
// Handles getUserMedia, AudioContext PCM processing,
// and SignalR binary frame streaming
// ═══════════════════════════════════════════════════════════

(function () {
    "use strict";

    var apiBaseUrl = (document.getElementById("apiBaseUrl") || {}).value || "";
    var sessionId = (document.getElementById("sessionId") || {}).value || "";
    var connection = null;
    var audioContext = null;
    var mediaStream = null;
    var scriptProcessor = null;
    var callTimer = null;
    var callSeconds = 0;

    // Validate session on page load
    if (sessionId) {
        validateSession();
    }

    function validateSession() {
        fetch(apiBaseUrl + "/api/webrtc/sessions/" + sessionId + "/validate")
            .then(function (r) {
                if (!r.ok) throw new Error("Invalid session");
                return r.json();
            })
            .then(function (data) {
                if (!data.valid) {
                    showExpired();
                }
            })
            .catch(function () {
                showExpired();
            });
    }

    function showExpired() {
        document.getElementById("joinForm").classList.add("d-none");
        document.getElementById("expiredView").classList.remove("d-none");
    }

    window.joinCall = function () {
        var name = document.getElementById("callerName").value.trim();
        if (!name) {
            showError("Please enter your name.");
            return;
        }

        var email = (document.getElementById("callerEmail").value || "").trim();
        var phone = (document.getElementById("callerPhone").value || "").trim();
        var joinBtn = document.getElementById("joinBtn");
        joinBtn.disabled = true;
        joinBtn.textContent = "Connecting...";

        // Request microphone
        navigator.mediaDevices.getUserMedia({ audio: true })
            .then(function (stream) {
                mediaStream = stream;
                // Join the session via API
                return fetch(apiBaseUrl + "/api/webrtc/sessions/" + sessionId + "/join", {
                    method: "POST",
                    headers: { "Content-Type": "application/json" },
                    body: JSON.stringify({ callerName: name, callerEmail: email, callerPhone: phone })
                });
            })
            .then(function (r) {
                if (!r.ok) throw new Error("Join failed");
                return r.json();
            })
            .then(function () {
                // Switch to in-call view
                document.getElementById("joinForm").classList.add("d-none");
                document.getElementById("inCallView").classList.remove("d-none");
                document.getElementById("callStatusText").textContent = "Connected";
                document.getElementById("micIndicator").classList.add("active");

                // Start timer
                callTimer = setInterval(function () {
                    callSeconds++;
                    var m = Math.floor(callSeconds / 60);
                    var s = callSeconds % 60;
                    document.getElementById("callDuration").textContent =
                        (m < 10 ? "0" : "") + m + ":" + (s < 10 ? "0" : "") + s;
                }, 1000);

                // Start SignalR + audio streaming
                startAudioStreaming();
            })
            .catch(function (err) {
                joinBtn.disabled = false;
                joinBtn.textContent = "Join Call";
                if (mediaStream) {
                    mediaStream.getTracks().forEach(function (t) { t.stop(); });
                    mediaStream = null;
                }
                if (err.message === "Join failed") {
                    showError("This session is no longer available.");
                    showExpired();
                } else {
                    showError("Microphone access required. Please allow microphone access and try again.");
                }
            });
    };

    function showError(msg) {
        var el = document.getElementById("joinError");
        el.textContent = msg;
        el.classList.remove("d-none");
    }

    function startAudioStreaming() {
        // Connect to SignalR hub
        connection = new signalR.HubConnectionBuilder()
            .withUrl(apiBaseUrl + "/transcriptionHub")
            .withAutomaticReconnect()
            .build();

        connection.on("ReceiveTranscript", function (who, text) {
            addTranscriptEntry(who, text);
        });

        connection.on("SessionEnded", function () {
            endCall();
        });

        connection.start()
            .then(function () {
                connection.invoke("SessionJoined", sessionId);
                startCapture();
            })
            .catch(function () {
                document.getElementById("callStatusText").textContent = "Connection failed";
            });
    }

    function startCapture() {
        audioContext = new (window.AudioContext || window.webkitAudioContext)({ sampleRate: 16000 });
        var source = audioContext.createMediaStreamSource(mediaStream);

        // Use ScriptProcessorNode for PCM capture (deprecated but widely supported)
        scriptProcessor = audioContext.createScriptProcessor(4096, 1, 1);
        scriptProcessor.onaudioprocess = function (e) {
            var inputData = e.inputBuffer.getChannelData(0);
            // Convert float32 to int16 PCM
            var pcm16 = new Int16Array(inputData.length);
            for (var i = 0; i < inputData.length; i++) {
                var s = Math.max(-1, Math.min(1, inputData[i]));
                pcm16[i] = s < 0 ? s * 0x8000 : s * 0x7FFF;
            }
            // Send to SignalR as binary
            if (connection && connection.state === "Connected") {
                connection.invoke("SendAudio", sessionId, new Uint8Array(pcm16.buffer));
            }
        };

        source.connect(scriptProcessor);
        scriptProcessor.connect(audioContext.destination);
    }

    function addTranscriptEntry(who, text) {
        var container = document.getElementById("transcriptContainer");
        // Clear placeholder
        if (container.querySelector(".text-muted.text-center")) {
            container.innerHTML = "";
        }
        var entry = document.createElement("div");
        entry.className = "transcript-entry";
        entry.innerHTML = "<strong>" + (who === "AI" ? "Agent" : "You") + ":</strong> " + escapeHtml(text);
        container.appendChild(entry);
        container.scrollTop = container.scrollHeight;
    }

    function escapeHtml(text) {
        var div = document.createElement("div");
        div.appendChild(document.createTextNode(text || ""));
        return div.innerHTML;
    }

    window.endCall = function () {
        if (callTimer) clearInterval(callTimer);
        document.getElementById("callStatusText").textContent = "Call ended";
        document.getElementById("micIndicator").classList.remove("active");
        document.getElementById("endCallBtn").disabled = true;

        if (scriptProcessor) {
            scriptProcessor.disconnect();
            scriptProcessor = null;
        }
        if (audioContext) {
            audioContext.close();
            audioContext = null;
        }
        if (mediaStream) {
            mediaStream.getTracks().forEach(function (t) { t.stop(); });
            mediaStream = null;
        }
        if (connection) {
            connection.invoke("SessionEnded", sessionId).catch(function () {});
            connection.stop();
            connection = null;
        }

        // Notify server
        fetch(apiBaseUrl + "/api/webrtc/sessions/" + sessionId + "/end", { method: "POST" })
            .catch(function () {});
    };
})();
