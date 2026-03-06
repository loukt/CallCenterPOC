namespace ContactCenterPOC.Models
{
    public record VoiceLiveVoiceInfo(string FullName, string DisplayName, string Locale, string Gender);

    public static class VoiceLiveVoices
    {
        public static readonly IReadOnlyList<VoiceLiveVoiceInfo> All = new List<VoiceLiveVoiceInfo>
        {
            // en-US (12 voices)
            new("en-US-Adam:DragonHDLatestNeural", "Adam", "en-US", "Male"),
            new("en-US-Andrew:DragonHDLatestNeural", "Andrew", "en-US", "Male"),
            new("en-US-Ava:DragonHDLatestNeural", "Ava", "en-US", "Female"),
            new("en-US-Brian:DragonHDLatestNeural", "Brian", "en-US", "Male"),
            new("en-US-Davis:DragonHDLatestNeural", "Davis", "en-US", "Male"),
            new("en-US-Emma:DragonHDLatestNeural", "Emma", "en-US", "Female"),
            new("en-US-Jenny:DragonHDLatestNeural", "Jenny", "en-US", "Female"),
            new("en-US-Nova:DragonHDLatestNeural", "Nova", "en-US", "Female"),
            new("en-US-Aria:DragonHDLatestNeural", "Aria", "en-US", "Female"),
            new("en-US-Alloy:DragonHDLatestNeural", "Alloy", "en-US", "Male"),
            new("en-US-Phoebe:DragonHDLatestNeural", "Phoebe", "en-US", "Female"),
            new("en-US-Steffan:DragonHDLatestNeural", "Steffan", "en-US", "Male"),

            // de-DE (2 voices)
            new("de-DE-Florian:DragonHDLatestNeural", "Florian", "de-DE", "Male"),
            new("de-DE-Seraphina:DragonHDLatestNeural", "Seraphina", "de-DE", "Female"),

            // es-ES (2 voices)
            new("es-ES-Tristan:DragonHDLatestNeural", "Tristan", "es-ES", "Male"),
            new("es-ES-Ximena:DragonHDLatestNeural", "Ximena", "es-ES", "Female"),

            // fr-FR (2 voices)
            new("fr-FR-Remy:DragonHDLatestNeural", "Remy", "fr-FR", "Male"),
            new("fr-FR-Vivienne:DragonHDLatestNeural", "Vivienne", "fr-FR", "Female"),

            // ja-JP (2 voices)
            new("ja-JP-Masaru:DragonHDLatestNeural", "Masaru", "ja-JP", "Male"),
            new("ja-JP-Nanami:DragonHDLatestNeural", "Nanami", "ja-JP", "Female"),

            // zh-CN (2 voices)
            new("zh-CN-Xiaochen:DragonHDLatestNeural", "Xiaochen", "zh-CN", "Female"),
            new("zh-CN-Yunfan:DragonHDLatestNeural", "Yunfan", "zh-CN", "Male"),
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
