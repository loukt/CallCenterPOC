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
    }
}
