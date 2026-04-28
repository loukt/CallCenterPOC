using ContactCenterPOC.Models;

namespace ContactCenterPOC.Services
{
    public class WebRTCSignalingService
    {
        private readonly CosmosDbService _cosmosDb;
        private readonly AgentActivityService _agentActivityService;
        private readonly IConfiguration _configuration;
        private readonly ILogger<WebRTCSignalingService> _logger;
        private const string ContainerName = "WebRTCCallSessions";

        public WebRTCSignalingService(
            CosmosDbService cosmosDb, AgentActivityService agentActivityService,
            IConfiguration configuration, ILogger<WebRTCSignalingService> logger)
        {
            _cosmosDb = cosmosDb;
            _agentActivityService = agentActivityService;
            _configuration = configuration;
            _logger = logger;
        }

        public async Task<WebRTCCallSession> CreateSessionAsync(string? campaignId = null, int expiryMinutes = 15)
        {
            var session = new WebRTCCallSession
            {
                CampaignId = campaignId,
                ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(expiryMinutes),
                Status = WebRTCSessionStatus.Waiting
            };

            // Generate shareable link
            var baseUrl = _configuration["CallbackUrl"] ?? "https://localhost:5001";
            // Use the APP's URL for the join page, not the API
            var appUrl = _configuration["FrontendOrigin"] ?? "https://localhost:5002";
            session.ShareableLink = $"{appUrl}/JoinCall?sessionId={session.SessionId}";

            await _cosmosDb.UpsertAsync(ContainerName, session, session.SessionId);
            _logger.LogInformation("WebRTC session created: {SessionId}, expires at {ExpiresAt}", session.SessionId, session.ExpiresAt);

            return session;
        }

        public async Task<WebRTCCallSession?> GetSessionAsync(string sessionId)
        {
            return await _cosmosDb.GetAsync<WebRTCCallSession>(ContainerName, sessionId, sessionId);
        }

        public async Task<(bool Valid, string? Reason)> ValidateSessionAsync(string sessionId)
        {
            var session = await GetSessionAsync(sessionId);
            if (session == null)
                return (false, "Session not found");
            if (session.IsUsed)
                return (false, "Session link has already been used");
            if (session.ExpiresAt < DateTimeOffset.UtcNow)
                return (false, "Session link has expired");
            if (session.Status == WebRTCSessionStatus.Disconnected)
                return (false, "Session has ended");

            return (true, null);
        }

        public async Task<WebRTCCallSession?> JoinSessionAsync(string sessionId, string callerName, string? callerEmail, string? callerPhone)
        {
            var session = await GetSessionAsync(sessionId);
            if (session == null) return null;

            if (session.IsUsed)
                throw new InvalidOperationException("Session link has already been used");
            if (session.ExpiresAt < DateTimeOffset.UtcNow)
                throw new InvalidOperationException("Session link has expired");

            session.IsUsed = true;
            session.CallerName = callerName;
            session.CallerEmail = callerEmail;
            session.CallerPhone = callerPhone;
            session.Status = WebRTCSessionStatus.Connected;

            await _cosmosDb.UpsertAsync(ContainerName, session, session.SessionId);
            _logger.LogInformation("WebRTC session joined: {SessionId} by {CallerName}", sessionId, callerName);

            return session;
        }

        public async Task<WebRTCCallSession?> EndSessionAsync(string sessionId)
        {
            var session = await GetSessionAsync(sessionId);
            if (session == null) return null;

            session.Status = WebRTCSessionStatus.Disconnected;
            await _cosmosDb.UpsertAsync(ContainerName, session, session.SessionId);
            _logger.LogInformation("WebRTC session ended: {SessionId}", sessionId);

            return session;
        }
    }
}
