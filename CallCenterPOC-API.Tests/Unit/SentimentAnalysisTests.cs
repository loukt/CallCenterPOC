using ContactCenterPOC.Models;
using ContactCenterPOC.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Moq;

namespace CallCenterPOC_API.Tests.Unit
{
    public class SentimentAnalysisTests
    {
        private static SentimentAnalysisService CreateService()
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

            var loggerMock = new Mock<ILogger<SentimentAnalysisService>>();
            return new SentimentAnalysisService(configuration, loggerMock.Object);
        }

        [Fact]
        public async Task AnalyzeAsync_NullText_ShouldReturnNeutral()
        {
            var service = CreateService();
            var result = await service.AnalyzeAsync(null);

            Assert.Equal(SentimentLabel.Neutral, result.Label);
            Assert.Equal(0f, result.Confidence);
        }

        [Fact]
        public async Task AnalyzeAsync_EmptyText_ShouldReturnNeutral()
        {
            var service = CreateService();
            var result = await service.AnalyzeAsync("");

            Assert.Equal(SentimentLabel.Neutral, result.Label);
            Assert.Equal(0f, result.Confidence);
        }

        [Fact]
        public async Task AnalyzeAsync_WhitespaceText_ShouldReturnNeutral()
        {
            var service = CreateService();
            var result = await service.AnalyzeAsync("   ");

            Assert.Equal(SentimentLabel.Neutral, result.Label);
            Assert.Equal(0f, result.Confidence);
        }

        [Fact]
        public void SentimentResult_DefaultValues_ShouldBeNeutral()
        {
            var result = new SentimentResult();
            Assert.Equal(SentimentLabel.Neutral, result.Label);
            Assert.Equal(0f, result.Confidence);
        }

        [Fact]
        public void SentimentLabel_HasExpectedValues()
        {
            Assert.Equal(0, (int)SentimentLabel.Positive);
            Assert.Equal(1, (int)SentimentLabel.Neutral);
            Assert.Equal(2, (int)SentimentLabel.Negative);
        }

        [Fact]
        public void ParseSentimentJson_ValidPositive_ShouldReturnPositive()
        {
            var result = SentimentAnalysisService.ParseSentimentJson("{\"label\":\"Positive\",\"confidence\":0.95}");
            Assert.Equal(SentimentLabel.Positive, result.Label);
            Assert.True(result.Confidence > 0.9f);
        }

        [Fact]
        public void ParseSentimentJson_ValidNegative_ShouldReturnNegative()
        {
            var result = SentimentAnalysisService.ParseSentimentJson("{\"label\":\"Negative\",\"confidence\":0.8}");
            Assert.Equal(SentimentLabel.Negative, result.Label);
            Assert.True(result.Confidence > 0.7f);
        }

        [Fact]
        public void ParseSentimentJson_ValidNeutral_ShouldReturnNeutral()
        {
            var result = SentimentAnalysisService.ParseSentimentJson("{\"label\":\"Neutral\",\"confidence\":0.6}");
            Assert.Equal(SentimentLabel.Neutral, result.Label);
        }

        [Fact]
        public void ParseSentimentJson_InvalidJson_ShouldReturnNeutral()
        {
            var result = SentimentAnalysisService.ParseSentimentJson("not valid json");
            Assert.Equal(SentimentLabel.Neutral, result.Label);
            Assert.Equal(0f, result.Confidence);
        }

        [Fact]
        public void ParseSentimentJson_NullInput_ShouldReturnNeutral()
        {
            var result = SentimentAnalysisService.ParseSentimentJson(null);
            Assert.Equal(SentimentLabel.Neutral, result.Label);
        }
    }
}
