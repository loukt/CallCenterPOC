using Microsoft.AspNetCore.SignalR;

namespace ContactCenterPOC.Hubs
{
    public class TranscriptHub : Hub
    {
        public async Task JoinCall(string callConnectionId)
        {
            await Groups.AddToGroupAsync(Context.ConnectionId, callConnectionId);
        }

        public async Task LeaveCall(string callConnectionId)
        {
            await Groups.RemoveFromGroupAsync(Context.ConnectionId, callConnectionId);
        }

        // Feature 003: WebRTC audio relay methods
        public async Task SendAudio(string sessionId, byte[] audioData)
        {
            await Clients.OthersInGroup(sessionId).SendAsync("ReceiveAudio", audioData);
        }

        public async Task JoinWebRTCSession(string sessionId)
        {
            await Groups.AddToGroupAsync(Context.ConnectionId, sessionId);
            await Clients.OthersInGroup(sessionId).SendAsync("SessionJoined", Context.ConnectionId);
        }

        public async Task LeaveWebRTCSession(string sessionId)
        {
            await Groups.RemoveFromGroupAsync(Context.ConnectionId, sessionId);
            await Clients.OthersInGroup(sessionId).SendAsync("SessionEnded", Context.ConnectionId);
        }

        // Feature 003: Escalation notifications
        public async Task NotifyEscalation(string callConnectionId, string message)
        {
            await Clients.All.SendAsync("EscalationRequested", new
            {
                callConnectionId,
                message,
                timestamp = DateTimeOffset.UtcNow
            });
        }

        public async Task JoinEscalation(string callConnectionId)
        {
            await Groups.AddToGroupAsync(Context.ConnectionId, $"escalation-{callConnectionId}");
        }
    }
}
