
using OpenAI.RealtimeConversation;
using System.Net.WebSockets;
using System.Text.Json;
using System.Text;

namespace CallCenterCoreAPI.Services
{
    public class RealtimeAudioService
    {
        private readonly string _endpoint;
        private readonly string _deploymentName;
        private ClientWebSocket _webSocket;
        private readonly ILogger<RealtimeAudioService> _logger;

        public RealtimeAudioService(IConfiguration configuration, ILogger<RealtimeAudioService> logger)
        {
            _endpoint = configuration["AzureOpenAI:Endpoint"];
            _deploymentName = configuration["AzureOpenAI:DeploymentName"];
            _logger = logger;
            _webSocket = new ClientWebSocket();
        }

        public async Task InitializeSession()
        {
            try
            {
                var uri = new Uri($"wss://{_endpoint}/openai/realtime?api-version=2024-10-01-preview&deployment={_deploymentName}");
                await _webSocket.ConnectAsync(uri, CancellationToken.None);
            }
            catch (Exception ex) {
                _logger.LogInformation("damn we hit here ok we need to manage this ");
            }
            // Configure session
            var configMessage = new
            {
                type = "session.config",
                session = new
                {
                    audio = new
                    {
                        input = new { format = "pcm16" },
                        output = new { format = "pcm16" }
                    },
                    voice = "alloy",
                    turn_detection = new
                    {
                        type = "server_vad",
                        threshold = 0.5,
                        prefix_padding_ms = 300,
                        silence_duration_ms = 200
                    }
                }
            };

            var bytes = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(configMessage));
            await _webSocket.SendAsync(bytes, WebSocketMessageType.Text, true, CancellationToken.None);
        }
    
        public async Task<ClientWebSocket> GetWebSocket()
        {
            if (_webSocket.State != WebSocketState.Open)
            {
                await InitializeSession();
            }
            return _webSocket;
        }
}
}
