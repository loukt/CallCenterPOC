using ContactCenterPOC.Models;
using ContactCenterPOC.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Moq;

namespace CallCenterPOC_API.Tests.Unit
{
    public class EmotionAnalysisTests
    {
        private static EmotionAnalysisService CreateService()
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

            var loggerMock = new Mock<ILogger<EmotionAnalysisService>>();
            return new EmotionAnalysisService(configuration, loggerMock.Object);
        }

        [Fact]
        public async Task AnalyzeAsync_NullText_ShouldReturnNeutral()
        {
            var service = CreateService();
            var result = await service.AnalyzeAsync(null);

            Assert.Equal(EmotionLabel.Neutral, result.Label);
            Assert.Equal(0f, result.Confidence);
        }

        [Fact]
        public async Task AnalyzeAsync_EmptyText_ShouldReturnNeutral()
        {
            var service = CreateService();
            var result = await service.AnalyzeAsync("");

            Assert.Equal(EmotionLabel.Neutral, result.Label);
            Assert.Equal(0f, result.Confidence);
        }

        [Fact]
        public async Task AnalyzeAsync_WhitespaceText_ShouldReturnNeutral()
        {
            var service = CreateService();
            var result = await service.AnalyzeAsync("   ");

            Assert.Equal(EmotionLabel.Neutral, result.Label);
            Assert.Equal(0f, result.Confidence);
        }

        [Fact]
        public void EmotionResult_DefaultValues_ShouldBeNeutral()
        {
            var result = new EmotionResult();
            Assert.Equal(EmotionLabel.Neutral, result.Label);
            Assert.Equal(0f, result.Confidence);
        }

        [Fact]
        public void EmotionLabel_HasExpectedValues()
        {
            Assert.Equal(0, (int)EmotionLabel.Neutral);
            Assert.Equal(1, (int)EmotionLabel.Happy);
            Assert.Equal(2, (int)EmotionLabel.Frustrated);
            Assert.Equal(3, (int)EmotionLabel.Angry);
            Assert.Equal(4, (int)EmotionLabel.Sad);
            Assert.Equal(5, (int)EmotionLabel.Anxious);
        }

        [Fact]
        public void ParseEmotionJson_ValidHappy_ShouldReturnHappy()
        {
            var result = EmotionAnalysisService.ParseEmotionJson("{\"label\":\"Happy\",\"confidence\":0.92}");
            Assert.Equal(EmotionLabel.Happy, result.Label);
            Assert.True(result.Confidence > 0.9f);
        }

        [Fact]
        public void ParseEmotionJson_ValidFrustrated_ShouldReturnFrustrated()
        {
            var result = EmotionAnalysisService.ParseEmotionJson("{\"label\":\"Frustrated\",\"confidence\":0.8}");
            Assert.Equal(EmotionLabel.Frustrated, result.Label);
            Assert.True(result.Confidence > 0.7f);
        }

        [Fact]
        public void ParseEmotionJson_ValidAngry_ShouldReturnAngry()
        {
            var result = EmotionAnalysisService.ParseEmotionJson("{\"label\":\"Angry\",\"confidence\":0.75}");
            Assert.Equal(EmotionLabel.Angry, result.Label);
        }

        [Fact]
        public void ParseEmotionJson_ValidSad_ShouldReturnSad()
        {
            var result = EmotionAnalysisService.ParseEmotionJson("{\"label\":\"Sad\",\"confidence\":0.6}");
            Assert.Equal(EmotionLabel.Sad, result.Label);
        }

        [Fact]
        public void ParseEmotionJson_ValidAnxious_ShouldReturnAnxious()
        {
            var result = EmotionAnalysisService.ParseEmotionJson("{\"label\":\"Anxious\",\"confidence\":0.7}");
            Assert.Equal(EmotionLabel.Anxious, result.Label);
        }

        [Fact]
        public void ParseEmotionJson_ValidNeutral_ShouldReturnNeutral()
        {
            var result = EmotionAnalysisService.ParseEmotionJson("{\"label\":\"Neutral\",\"confidence\":0.5}");
            Assert.Equal(EmotionLabel.Neutral, result.Label);
        }

        [Fact]
        public void ParseEmotionJson_InvalidJson_ShouldReturnNeutral()
        {
            var result = EmotionAnalysisService.ParseEmotionJson("not valid json");
            Assert.Equal(EmotionLabel.Neutral, result.Label);
            Assert.Equal(0f, result.Confidence);
        }

        [Fact]
        public void ParseEmotionJson_NullInput_ShouldReturnNeutral()
        {
            var result = EmotionAnalysisService.ParseEmotionJson(null);
            Assert.Equal(EmotionLabel.Neutral, result.Label);
        }

        [Fact]
        public void ParseEmotionJson_UnknownLabel_ShouldReturnNeutral()
        {
            var result = EmotionAnalysisService.ParseEmotionJson("{\"label\":\"Excited\",\"confidence\":0.9}");
            Assert.Equal(EmotionLabel.Neutral, result.Label);
        }

        [Fact]
        public void OperatorStyleTraits_DefaultValues()
        {
            var traits = new OperatorStyleTraits();
            Assert.Equal(0f, traits.Empathy);
            Assert.Equal(0f, traits.Energy);
            Assert.Equal(default, traits.ComputedAt);
        }
    }
}
