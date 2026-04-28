namespace ContactCenterPOC.Models
{
    public record VoiceLiveVoiceInfo(string FullName, string DisplayName, string Locale, string Gender, string VoiceType);

    public static class VoiceLiveVoices
    {
        public static readonly IReadOnlyList<VoiceLiveVoiceInfo> All = new List<VoiceLiveVoiceInfo>
        {
            // en-US (12 voices)
            new("en-US-Adam:DragonHDLatestNeural", "Adam", "en-US", "Male", "DragonHD"),
            new("en-US-Andrew:DragonHDLatestNeural", "Andrew", "en-US", "Male", "DragonHD"),
            new("en-US-Ava:DragonHDLatestNeural", "Ava", "en-US", "Female", "DragonHD"),
            new("en-US-Brian:DragonHDLatestNeural", "Brian", "en-US", "Male", "DragonHD"),
            new("en-US-Davis:DragonHDLatestNeural", "Davis", "en-US", "Male", "DragonHD"),
            new("en-US-Emma:DragonHDLatestNeural", "Emma", "en-US", "Female", "DragonHD"),
            new("en-US-Jenny:DragonHDLatestNeural", "Jenny", "en-US", "Female", "DragonHD"),
            new("en-US-Nova:DragonHDLatestNeural", "Nova", "en-US", "Female", "DragonHD"),
            new("en-US-Aria:DragonHDLatestNeural", "Aria", "en-US", "Female", "DragonHD"),
            new("en-US-Alloy:DragonHDLatestNeural", "Alloy", "en-US", "Male", "DragonHD"),
            new("en-US-Phoebe:DragonHDLatestNeural", "Phoebe", "en-US", "Female", "DragonHD"),
            new("en-US-Steffan:DragonHDLatestNeural", "Steffan", "en-US", "Male", "DragonHD"),

            // en-US MAI-Voice-1 (4 voices)
            new("en-US-Grant:MAI-Voice-1", "Grant", "en-US", "Male", "MAI-Voice-1"),
            new("en-US-Iris:MAI-Voice-1", "Iris", "en-US", "Female", "MAI-Voice-1"),
            new("en-US-Jasper:MAI-Voice-1", "Jasper", "en-US", "Male", "MAI-Voice-1"),
            new("en-US-June:MAI-Voice-1", "June", "en-US", "Female", "MAI-Voice-1"),

            // de-DE (2 voices)
            new("de-DE-Florian:DragonHDLatestNeural", "Florian", "de-DE", "Male", "DragonHD"),
            new("de-DE-Seraphina:DragonHDLatestNeural", "Seraphina", "de-DE", "Female", "DragonHD"),

            // es-ES (2 voices)
            new("es-ES-Tristan:DragonHDLatestNeural", "Tristan", "es-ES", "Male", "DragonHD"),
            new("es-ES-Ximena:DragonHDLatestNeural", "Ximena", "es-ES", "Female", "DragonHD"),

            // fr-FR (2 voices)
            new("fr-FR-Remy:DragonHDLatestNeural", "Remy", "fr-FR", "Male", "DragonHD"),
            new("fr-FR-Vivienne:DragonHDLatestNeural", "Vivienne", "fr-FR", "Female", "DragonHD"),

            // ja-JP (2 voices)
            new("ja-JP-Masaru:DragonHDLatestNeural", "Masaru", "ja-JP", "Male", "DragonHD"),
            new("ja-JP-Nanami:DragonHDLatestNeural", "Nanami", "ja-JP", "Female", "DragonHD"),

            // zh-CN (2 voices)
            new("zh-CN-Xiaochen:DragonHDLatestNeural", "Xiaochen", "zh-CN", "Female", "DragonHD"),
            new("zh-CN-Yunfan:DragonHDLatestNeural", "Yunfan", "zh-CN", "Male", "DragonHD"),
        }.AsReadOnly();

        public static readonly IReadOnlyDictionary<string, IReadOnlyList<VoiceLiveVoiceInfo>> ByLocale =
            All.GroupBy(v => v.Locale)
               .ToDictionary(g => g.Key, g => (IReadOnlyList<VoiceLiveVoiceInfo>)g.ToList().AsReadOnly());

        public static readonly HashSet<string> ValidNames =
            new(All.Select(v => v.FullName), StringComparer.OrdinalIgnoreCase);

        public static readonly IReadOnlyList<string> Locales =
            All.Select(v => v.Locale).Distinct().ToList().AsReadOnly();
    }
}
