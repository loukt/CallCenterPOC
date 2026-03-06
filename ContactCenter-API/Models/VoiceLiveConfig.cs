namespace ContactCenterPOC.Models
{
    public class VoiceLiveConfig
    {
        public string EndpointUri { get; set; } = "";
        public string Key { get; set; } = "";
        public bool IsConfigured => !string.IsNullOrWhiteSpace(EndpointUri);
    }
}
