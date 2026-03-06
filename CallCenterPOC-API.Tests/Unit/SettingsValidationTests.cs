using ContactCenterPOC.Models;

namespace CallCenterPOC_API.Tests.Unit
{
    public class SettingsValidationTests
    {
        [Fact]
        public void OperatorSettings_DefaultVoiceApiMode_IsChatGPT()
        {
            var settings = new OperatorSettings();
            Assert.Equal("ChatGPT", settings.VoiceApiMode);
        }

        [Fact]
        public void OperatorSettings_DefaultVoiceLiveModel_IsGpt4o()
        {
            var settings = new OperatorSettings();
            Assert.Equal("gpt-4o", settings.VoiceLiveModel);
        }

        [Fact]
        public void OperatorSettings_DefaultSelectedVoiceLiveVoice_IsAva()
        {
            var settings = new OperatorSettings();
            Assert.Equal("en-US-Ava:DragonHDLatestNeural", settings.SelectedVoiceLiveVoice);
        }

        [Fact]
        public void OperatorSettings_DefaultTranscriptionMode_IsBuiltIn()
        {
            var settings = new OperatorSettings();
            Assert.Equal("BuiltIn", settings.TranscriptionMode);
        }

        [Fact]
        public void ValidVoiceLiveModels_ShouldContainExpectedModels()
        {
            Assert.Contains("gpt-4o", OperatorSettings.ValidVoiceLiveModels);
            Assert.Contains("gpt-realtime", OperatorSettings.ValidVoiceLiveModels);
            Assert.True(OperatorSettings.ValidVoiceLiveModels.Count >= 3,
                "Should have at least 3 valid VoiceLive models");
        }

        [Fact]
        public void ValidTranscriptionModes_ShouldContainBuiltInAndSeparateSTT()
        {
            Assert.Contains("BuiltIn", OperatorSettings.ValidTranscriptionModes);
            Assert.Contains("SeparateSTT", OperatorSettings.ValidTranscriptionModes);
            Assert.Equal(2, OperatorSettings.ValidTranscriptionModes.Count);
        }

        [Fact]
        public void VoiceLiveConfig_IsConfigured_FalseWhenEmpty()
        {
            var config = new VoiceLiveConfig();
            Assert.False(config.IsConfigured);
        }

        [Fact]
        public void VoiceLiveConfig_IsConfigured_TrueWhenOnlyEndpoint()
        {
            var config = new VoiceLiveConfig { EndpointUri = "https://example.com" };
            Assert.True(config.IsConfigured, "Managed Identity needs only EndpointUri (FR-010)");
        }

        [Fact]
        public void VoiceLiveConfig_IsConfigured_TrueWhenBothSet()
        {
            var config = new VoiceLiveConfig
            {
                EndpointUri = "https://example.com",
                Key = "test-key"
            };
            Assert.True(config.IsConfigured);
        }

        [Fact]
        public void ActiveCall_DefaultVoiceApiMode_IsChatGPT()
        {
            var call = new ActiveCall();
            Assert.Equal("ChatGPT", call.VoiceApiMode);
        }

        [Fact]
        public void ActiveCall_DefaultReconnectAttempts_IsZero()
        {
            var call = new ActiveCall();
            Assert.Equal(0, call.ReconnectAttempts);
        }

        [Fact]
        public void ActiveCall_VoiceLiveModel_DefaultsToNull()
        {
            var call = new ActiveCall();
            Assert.Null(call.VoiceLiveModel);
        }

        [Fact]
        public void CallRecord_VoiceLiveFields_DefaultToExpected()
        {
            var record = new CallRecord();
            Assert.Equal("ChatGPT", record.VoiceApiMode);
            Assert.Null(record.VoiceLiveModel);
            Assert.Null(record.VoiceLiveVoice);
        }
    }
}
