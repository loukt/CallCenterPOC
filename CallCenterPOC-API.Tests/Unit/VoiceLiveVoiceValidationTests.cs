using ContactCenterPOC.Models;

namespace CallCenterPOC_API.Tests.Unit
{
    public class VoiceLiveVoiceValidationTests
    {
        [Fact]
        public void All_ShouldContainAtLeast22Voices()
        {
            Assert.True(VoiceLiveVoices.All.Count >= 22,
                $"Expected at least 22 voices, got {VoiceLiveVoices.All.Count}");
        }

        [Fact]
        public void ValidNames_ShouldMatchAllVoiceFullNames()
        {
            var allFullNames = VoiceLiveVoices.All.Select(v => v.FullName).ToHashSet();
            Assert.Equal(allFullNames, VoiceLiveVoices.ValidNames);
        }

        [Fact]
        public void ByLocale_ShouldContainAtLeast6Locales()
        {
            Assert.True(VoiceLiveVoices.ByLocale.Count >= 6,
                $"Expected at least 6 locales, got {VoiceLiveVoices.ByLocale.Count}");
        }

        [Fact]
        public void ByLocale_EnUS_ShouldHaveMostVoices()
        {
            Assert.True(VoiceLiveVoices.ByLocale.ContainsKey("en-US"),
                "en-US locale should exist");
            Assert.True(VoiceLiveVoices.ByLocale["en-US"].Count >= 10,
                "en-US should have at least 10 voices");
        }

        [Fact]
        public void Locales_ShouldMatchByLocaleKeys()
        {
            var expected = VoiceLiveVoices.ByLocale.Keys.ToHashSet();
            Assert.Equal(expected, VoiceLiveVoices.Locales);
        }

        [Fact]
        public void DefaultVoice_ShouldBeInValidNames()
        {
            Assert.Contains("en-US-Ava:DragonHDLatestNeural", VoiceLiveVoices.ValidNames);
        }

        [Fact]
        public void AllVoices_ShouldHaveNonEmptyDisplayName()
        {
            foreach (var voice in VoiceLiveVoices.All)
            {
                Assert.False(string.IsNullOrWhiteSpace(voice.DisplayName),
                    $"Voice {voice.FullName} has empty display name");
            }
        }

        [Fact]
        public void AllVoices_ShouldHaveNonEmptyLocale()
        {
            foreach (var voice in VoiceLiveVoices.All)
            {
                Assert.False(string.IsNullOrWhiteSpace(voice.Locale),
                    $"Voice {voice.FullName} has empty locale");
            }
        }

        [Fact]
        public void AllVoices_ShouldHaveFullNameContainingDragonHD()
        {
            foreach (var voice in VoiceLiveVoices.All)
            {
                Assert.Contains("DragonHD", voice.FullName,
                    StringComparison.OrdinalIgnoreCase);
            }
        }

        [Fact]
        public void ByLocale_EachGroupShouldContainMatchingLocaleVoices()
        {
            foreach (var kvp in VoiceLiveVoices.ByLocale)
            {
                foreach (var voice in kvp.Value)
                {
                    Assert.Equal(kvp.Key, voice.Locale);
                }
            }
        }
    }
}
