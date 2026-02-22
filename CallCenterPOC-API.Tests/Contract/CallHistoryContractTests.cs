using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using System.Net;
using System.Text.Json;

namespace CallCenterPOC_API.Tests.Contract
{
    public class CallHistoryContractTests : IClassFixture<WebApplicationFactory<Program>>
    {
        private readonly WebApplicationFactory<Program> _factory;
        private readonly HttpClient _client;

        public CallHistoryContractTests(WebApplicationFactory<Program> factory)
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
        public async Task GetCallHistory_ShouldReturn200WithArray()
        {
            // Act
            var response = await _client.GetAsync("/api/CallHistory");

            // Assert
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);

            var json = await response.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            Assert.Equal(JsonValueKind.Array, root.ValueKind);
        }

        [Fact]
        public async Task GetCallDetail_WithInvalidId_ShouldReturn404()
        {
            // Act
            var response = await _client.GetAsync("/api/CallHistory/nonexistent-call-id");

            // Assert
            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        }

        [Fact]
        public async Task GetCallHistory_ResponseContractsMatchSchema()
        {
            // Act
            var response = await _client.GetAsync("/api/CallHistory");

            // Assert
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);

            var json = await response.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            Assert.Equal(JsonValueKind.Array, root.ValueKind);

            // If there are any entries, validate schema
            foreach (var entry in root.EnumerateArray())
            {
                Assert.True(entry.TryGetProperty("callConnectionId", out _), "Entry should have 'callConnectionId'");
                Assert.True(entry.TryGetProperty("phoneNumber", out _), "Entry should have 'phoneNumber'");
                Assert.True(entry.TryGetProperty("startedAt", out _), "Entry should have 'startedAt'");
                Assert.True(entry.TryGetProperty("duration", out _), "Entry should have 'duration'");
                Assert.True(entry.TryGetProperty("overallSentiment", out _), "Entry should have 'overallSentiment'");
            }
        }

        [Fact]
        public async Task GetCallRecording_WithNonexistentId_ShouldReturn404()
        {
            // Act
            var response = await _client.GetAsync("/api/CallHistory/nonexistent-id/recording");

            // Assert
            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        }
    }
}
