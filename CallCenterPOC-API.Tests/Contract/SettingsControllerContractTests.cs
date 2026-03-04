using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;

namespace CallCenterPOC_API.Tests.Contract
{
    public class SettingsControllerContractTests : IClassFixture<WebApplicationFactory<Program>>
    {
        private readonly WebApplicationFactory<Program> _factory;
        private readonly HttpClient _client;

        public SettingsControllerContractTests(WebApplicationFactory<Program> factory)
        {
            _factory = factory.WithWebHostBuilder(builder =>
            {
                builder.ConfigureAppConfiguration((context, config) =>
                {
                    config.AddInMemoryCollection(new Dictionary<string, string?>
                    {
                        ["AzureCommunicationServices:ConnectionString"] = "endpoint=https://fake.communication.azure.com/;accesskey=dGVzdGtleXRlc3RrZXl0ZXN0a2V5dGVzdGtleXRlcw==",
                        ["AzureCommunicationServices:PhoneNumber"] = "+15551234567",
                        ["CallbackUrl"] = "https://localhost:5001/api/Callback",
                        ["FrontendOrigin"] = "https://localhost:5002",
                        ["BlobStorage:ContainerName"] = "callcenter-data"
                    });
                });
            });
            _client = _factory.CreateClient();
        }

        [Fact]
        public async Task GetSettings_ShouldReturn200WithDefaults()
        {
            // Act
            var response = await _client.GetAsync("/api/Settings");

            // Assert
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);

            var json = await response.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            Assert.True(root.TryGetProperty("maxCallTimeMinutes", out var maxCall), "Should have 'maxCallTimeMinutes'");
            Assert.Equal(2.0, maxCall.GetDouble());

            Assert.True(root.TryGetProperty("voiceApiMode", out var voice), "Should have 'voiceApiMode'");
            Assert.Equal("ChatGPT", voice.GetString());
        }

        [Fact]
        public async Task PutSettings_WithValidBody_ShouldReturn200()
        {
            // Arrange
            var content = new StringContent(
                JsonSerializer.Serialize(new { maxCallTimeMinutes = 5.0, voiceApiMode = "ChatGPT" }),
                System.Text.Encoding.UTF8);
            content.Headers.ContentType = new MediaTypeHeaderValue("application/json");

            // Act
            var response = await _client.PutAsync("/api/Settings", content);

            // Assert
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);

            var json = await response.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(json);
            Assert.Equal(5.0, doc.RootElement.GetProperty("maxCallTimeMinutes").GetDouble());
        }

        [Fact]
        public async Task PutSettings_ClampsMaxCallTimeMin()
        {
            // Arrange — below minimum 0.5
            var content = new StringContent(
                JsonSerializer.Serialize(new { maxCallTimeMinutes = 0.1, voiceApiMode = "ChatGPT" }),
                System.Text.Encoding.UTF8);
            content.Headers.ContentType = new MediaTypeHeaderValue("application/json");

            // Act
            var response = await _client.PutAsync("/api/Settings", content);

            // Assert
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);

            var json = await response.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(json);
            Assert.Equal(0.5, doc.RootElement.GetProperty("maxCallTimeMinutes").GetDouble());
        }

        [Fact]
        public async Task PutSettings_ClampsMaxCallTimeMax()
        {
            // Arrange — above maximum 30
            var content = new StringContent(
                JsonSerializer.Serialize(new { maxCallTimeMinutes = 60.0, voiceApiMode = "ChatGPT" }),
                System.Text.Encoding.UTF8);
            content.Headers.ContentType = new MediaTypeHeaderValue("application/json");

            // Act
            var response = await _client.PutAsync("/api/Settings", content);

            // Assert
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);

            var json = await response.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(json);
            Assert.Equal(30.0, doc.RootElement.GetProperty("maxCallTimeMinutes").GetDouble());
        }

        [Fact]
        public async Task PutSettings_InvalidVoiceApi_DefaultsToChatGPT()
        {
            // Arrange
            var content = new StringContent(
                JsonSerializer.Serialize(new { maxCallTimeMinutes = 2.0, voiceApiMode = "InvalidMode" }),
                System.Text.Encoding.UTF8);
            content.Headers.ContentType = new MediaTypeHeaderValue("application/json");

            // Act
            var response = await _client.PutAsync("/api/Settings", content);

            // Assert
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);

            var json = await response.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(json);
            Assert.Equal("ChatGPT", doc.RootElement.GetProperty("voiceApiMode").GetString());
        }
    }
}
