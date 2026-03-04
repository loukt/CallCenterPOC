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
        public async Task GetCallHistory_ShouldReturn200WithPaginatedResponse()
        {
            // Act
            var response = await _client.GetAsync("/api/CallHistory");

            // Assert
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);

            var json = await response.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            Assert.Equal(JsonValueKind.Object, root.ValueKind);
            Assert.True(root.TryGetProperty("totalCount", out _), "Response should have 'totalCount'");
            Assert.True(root.TryGetProperty("page", out _), "Response should have 'page'");
            Assert.True(root.TryGetProperty("pageSize", out _), "Response should have 'pageSize'");
            Assert.True(root.TryGetProperty("items", out var items), "Response should have 'items'");
            Assert.Equal(JsonValueKind.Array, items.ValueKind);
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

            Assert.Equal(JsonValueKind.Object, root.ValueKind);
            Assert.True(root.TryGetProperty("items", out var items), "Response should have 'items'");
            Assert.Equal(JsonValueKind.Array, items.ValueKind);

            // If there are any entries, validate schema
            foreach (var entry in items.EnumerateArray())
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

        [Fact]
        public async Task GetCallHistory_WithPaginationParams_ShouldReturn200()
        {
            // Act
            var response = await _client.GetAsync("/api/CallHistory?page=1&pageSize=5");

            // Assert
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);

            var json = await response.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            Assert.Equal(1, root.GetProperty("page").GetInt32());
            Assert.Equal(5, root.GetProperty("pageSize").GetInt32());
        }

        [Fact]
        public async Task GetCallHistory_PageSizeCappedAt100()
        {
            // Act
            var response = await _client.GetAsync("/api/CallHistory?pageSize=200");

            // Assert
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);

            var json = await response.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(json);
            Assert.Equal(100, doc.RootElement.GetProperty("pageSize").GetInt32());
        }
    }
}
