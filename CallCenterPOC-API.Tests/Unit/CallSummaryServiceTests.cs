using ContactCenterPOC.Models;
using ContactCenterPOC.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Moq;

namespace CallCenterPOC_API.Tests.Unit
{
    public class CallSummaryServiceTests
    {
        private static CallSummaryService CreateService()
        {
            var configValues = new Dictionary<string, string?>
            {
                ["AzureOpenAI:EndpointUri"] = "https://fake.openai.azure.com/",
                ["AzureOpenAI:ChatDeployment"] = "gpt-4o-mini",
                ["AzureOpenAI:Key"] = "fake-key-for-testing"
            };
            var configuration = new ConfigurationBuilder()
                .AddInMemoryCollection(configValues)
                .Build();

            var loggerMock = new Mock<ILogger<CallSummaryService>>();
            return new CallSummaryService(configuration, loggerMock.Object);
        }

        private static CallSummaryService CreateUnconfiguredService()
        {
            var configValues = new Dictionary<string, string?>
            {
                // No AzureOpenAI config
            };
            var configuration = new ConfigurationBuilder()
                .AddInMemoryCollection(configValues)
                .Build();

            var loggerMock = new Mock<ILogger<CallSummaryService>>();
            return new CallSummaryService(configuration, loggerMock.Object);
        }

        [Fact]
        public async Task GenerateSummaryAsync_NullEntries_ShouldReturnNull()
        {
            var service = CreateService();
            var result = await service.GenerateSummaryAsync(null!);
            Assert.Null(result);
        }

        [Fact]
        public async Task GenerateSummaryAsync_EmptyEntries_ShouldReturnNull()
        {
            var service = CreateService();
            var result = await service.GenerateSummaryAsync(new List<TranscriptEntry>());
            Assert.Null(result);
        }

        [Fact]
        public async Task GenerateSummaryAsync_Unconfigured_ShouldReturnNull()
        {
            var service = CreateUnconfiguredService();
            var entries = new List<TranscriptEntry>
            {
                new TranscriptEntry { Speaker = SpeakerType.AI, Text = "Hello, how are you?" }
            };

            var result = await service.GenerateSummaryAsync(entries);
            Assert.Null(result);
        }

        [Fact]
        public void Service_WithConfig_IsConfigured()
        {
            var service = CreateService();
            Assert.True(service.IsConfigured);
        }

        [Fact]
        public void Service_WithoutConfig_IsNotConfigured()
        {
            var service = CreateUnconfiguredService();
            Assert.False(service.IsConfigured);
        }
    }
}
