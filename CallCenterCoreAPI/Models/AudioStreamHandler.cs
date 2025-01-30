using Azure.Communication.CallAutomation;
using Azure.Communication;
using CallCenterCoreAPI.Configuration;
using System.Net.WebSockets;
using System.Text.Json;
using System.Text;

namespace CallCenterCoreAPI.Models
{
    public class AudioStreamHandler
    {
        private readonly ClientWebSocket _webSocket;
        private readonly byte[] _buffer = new byte[4096];
        private readonly ILogger _logger;

        public AudioStreamHandler(ClientWebSocket webSocket, ILogger<AudioStreamHandler> logger)
        {
            _webSocket = webSocket;
            _logger = logger;
        }

        public async Task SendAudioChunk(byte[] audioData)
        {
            var message = new
            {
                type = "audio.input",
                audio = new
                {
                    chunk = Convert.ToHexString(audioData)
                }
            };

            var bytes = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(message));
            await _webSocket.SendAsync(bytes, WebSocketMessageType.Text, true, CancellationToken.None);
        }

        public async Task StartReceivingResponses(Action<byte[]> handleAudioResponse)
        {
            try
            {
                while (_webSocket.State == WebSocketState.Open)
                {
                    var result = await _webSocket.ReceiveAsync(new ArraySegment<byte>(_buffer), CancellationToken.None);

                    if (result.MessageType == WebSocketMessageType.Binary)
                    {
                        handleAudioResponse(_buffer[..result.Count]);
                    }
                }
            }
            catch (WebSocketException ex)
            {
                _logger.LogError($"WebSocket error: {ex.Message}");
            }
        }
    }


}
