using System.ComponentModel.DataAnnotations;

namespace ContactCenterPOC.Models
{
    public class CallbackEvent
    {
        public string CallConnectionId { get; set; }
        public string EventType { get; set; }
        public DateTimeOffset Timestamp { get; set; }
        public CallbackEventData Data { get; set; }
    }

    public class CallbackEventData
    {
        public string CallState { get; set; }
        public string OperationContext { get; set; }
        public string ResultCode { get; set; }
        public string ResultSubcode { get; set; }
    }

    public class CallRequest
    {
        [Required(ErrorMessage = "At least one phone number is required")]
        [MinLength(1, ErrorMessage = "At least one phone number is required")]
        [MaxLength(2, ErrorMessage = "Maximum of 2 phone numbers allowed")]
        public string[] PhoneNumbers { get; set; } = Array.Empty<string>();

        public string[]? ContactNames { get; set; }

        public string? CampaignId { get; set; }

        public string? Prompt { get; set; }
    }
}
