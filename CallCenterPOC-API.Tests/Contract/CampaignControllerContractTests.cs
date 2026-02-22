using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using System.Net;
using System.Text;
using System.Text.Json;

namespace CallCenterPOC_API.Tests.Contract
{
    public class CampaignControllerContractTests : IClassFixture<WebApplicationFactory<Program>>
    {
        private readonly WebApplicationFactory<Program> _factory;
        private readonly HttpClient _client;

        public CampaignControllerContractTests(WebApplicationFactory<Program> factory)
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
        public async Task GetCampaigns_ShouldReturn200WithArray()
        {
            // Act
            var response = await _client.GetAsync("/api/Campaign");

            // Assert
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);

            var json = await response.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            Assert.Equal(JsonValueKind.Array, root.ValueKind);
            Assert.True(root.GetArrayLength() >= 4, "Should have at least 4 default campaigns");
        }

        [Fact]
        public async Task CreateCampaign_WithValidBody_ShouldReturn201()
        {
            // Arrange
            var content = new StringContent(
                JsonSerializer.Serialize(new
                {
                    title = "Contract Test Campaign",
                    description = "A test campaign for contract testing",
                    aiBehaviorInstructions = "Be a test agent"
                }),
                Encoding.UTF8,
                "application/json");

            // Act
            var response = await _client.PostAsync("/api/Campaign", content);

            // Assert
            Assert.Equal(HttpStatusCode.Created, response.StatusCode);

            var json = await response.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            Assert.True(root.TryGetProperty("id", out _), "Response should have 'id' property");
            Assert.True(root.TryGetProperty("title", out var title), "Response should have 'title' property");
            Assert.Equal("Contract Test Campaign", title.GetString());
        }

        [Fact]
        public async Task CreateCampaign_WithMissingTitle_ShouldReturn400()
        {
            // Arrange — missing title
            var content = new StringContent(
                JsonSerializer.Serialize(new
                {
                    description = "Missing title",
                    aiBehaviorInstructions = "Test"
                }),
                Encoding.UTF8,
                "application/json");

            // Act
            var response = await _client.PostAsync("/api/Campaign", content);

            // Assert
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        }

        [Fact]
        public async Task CreateCampaign_WithDuplicateTitle_ShouldReturn400()
        {
            // Arrange — "Bank Loan Collection" is a default campaign
            var content = new StringContent(
                JsonSerializer.Serialize(new
                {
                    title = "Bank Loan Collection",
                    description = "Duplicate title",
                    aiBehaviorInstructions = "Test"
                }),
                Encoding.UTF8,
                "application/json");

            // Act
            var response = await _client.PostAsync("/api/Campaign", content);

            // Assert
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        }
    }
}
