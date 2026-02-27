namespace ContactCenterPOC.Models
{
    public class CallStatusUpdate
    {
        public string CallConnectionId { get; set; } = string.Empty;
        public CallStatus Status { get; set; }
        public string? Message { get; set; }
        public DateTimeOffset Timestamp { get; set; } = DateTimeOffset.UtcNow;
    }
}
