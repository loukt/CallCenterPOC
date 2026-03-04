using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using System.Net;
using System.Text.Json;

namespace CallCenterPOC_API.Tests.Contract
{
    public class HealthCheckContractTests : IClassFixture<WebApplicationFactory<Program>>
    {
        private readonly HttpClient _client;

        public HealthCheckContractTests(WebApplicationFactory<Program> factory)
        {
            var customFactory = factory.WithWebHostBuilder(builder =>
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
            _client = customFactory.CreateClient();
        }

        [Fact]
        public async Task Healthz_ShouldReturn200WithStatusHealthy()
        {
            // Act
            var response = await _client.GetAsync("/healthz");

            // Assert
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);

            var json = await response.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            Assert.Equal("healthy", root.GetProperty("status").GetString());
            Assert.True(root.TryGetProperty("timestamp", out _), "Response should have 'timestamp'");
        }
    }
}
