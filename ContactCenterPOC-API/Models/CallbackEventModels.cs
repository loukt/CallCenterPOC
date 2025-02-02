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

    public class CallRequest {
        public string PhoneNumber { get; set; }
        public string Prompt { get; set; }

    }
}
