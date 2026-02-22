using ContactCenterPOC.Hubs;
using ContactCenterPOC.Models;
using ContactCenterPOC.Services;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Moq;
using System.Net;
using System.Text;
using System.Text.Json;

namespace CallCenterPOC_API.Tests.Contract
{
    public class CallControllerContractTests : IClassFixture<WebApplicationFactory<Program>>
    {
        private readonly WebApplicationFactory<Program> _factory;
        private readonly HttpClient _client;

        public CallControllerContractTests(WebApplicationFactory<Program> factory)
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
                        ["FrontendOrigin"] = "https://localhost:5002"
                    });
                });
            });
            _client = _factory.CreateClient();
        }

        [Fact]
        public async Task InitiateCall_WithInvalidPhone_ShouldReturn400()
        {
            // Arrange
            var content = new StringContent(
                JsonSerializer.Serialize(new { phoneNumber = "invalid-number", prompt = "Test" }),
                Encoding.UTF8,
                "application/json");

            // Act
            var response = await _client.PostAsync("/api/Call/initiate", content);

            // Assert
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        }

        [Fact]
        public async Task InitiateCall_WithMissingBody_ShouldReturn400()
        {
            // Arrange — empty JSON body
            var content = new StringContent("{}", Encoding.UTF8, "application/json");

            // Act
            var response = await _client.PostAsync("/api/Call/initiate", content);

            // Assert
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        }

        [Fact]
        public async Task GetActiveCalls_ShouldReturn200WithExpectedShape()
        {
            // Act
            var response = await _client.GetAsync("/api/Call/active");

            // Assert
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);

            var json = await response.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            Assert.True(root.TryGetProperty("count", out _), "Response should have 'count' property");
            Assert.True(root.TryGetProperty("maxConcurrent", out _), "Response should have 'maxConcurrent' property");
            Assert.True(root.TryGetProperty("calls", out var calls), "Response should have 'calls' property");
            Assert.Equal(JsonValueKind.Array, calls.ValueKind);
        }

        [Fact]
        public async Task HangUp_WithNonexistentId_ShouldReturn404()
        {
            // Act
            var response = await _client.PostAsync("/api/Call/hangup/nonexistent-id", null);

            // Assert
            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        }

        [Fact]
        public async Task InitiateCall_WithContactNames_ShouldAcceptRequest()
        {
            // Arrange — valid shape with contactNames
            var body = new
            {
                phoneNumbers = new[] { "+15559990001" },
                prompt = "Hello test",
                contactNames = new[] { "John Doe" }
            };
            var content = new StringContent(
                JsonSerializer.Serialize(body),
                Encoding.UTF8,
                "application/json");

            // Act
            var response = await _client.PostAsync("/api/Call/initiate", content);

            // Assert — should not be 400 (contactNames accepted)
            Assert.NotEqual(HttpStatusCode.BadRequest, response.StatusCode);
        }
    }
}
