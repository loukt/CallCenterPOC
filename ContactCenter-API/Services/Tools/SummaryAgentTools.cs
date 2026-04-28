using ContactCenterPOC.Models;
using System.Text.Json;

namespace ContactCenterPOC.Services.Tools
{
    public class SummaryAgentTools
    {
        private readonly CallHistoryService _callHistoryService;
        private readonly CaseManagementService _caseManagementService;
        private readonly CallSummaryService _callSummaryService;

        public SummaryAgentTools(CallHistoryService callHistoryService, CaseManagementService caseManagementService, CallSummaryService callSummaryService)
        {
            _callHistoryService = callHistoryService;
            _caseManagementService = caseManagementService;
            _callSummaryService = callSummaryService;
        }

        public async Task<string> GetCallTranscriptAsync(string callConnectionId)
        {
            var record = await _callHistoryService.GetByIdAsync(callConnectionId);
            if (record == null)
                return JsonSerializer.Serialize(new { error = "Call record not found" });

            return JsonSerializer.Serialize(new
            {
                entries = record.TranscriptEntries?.Select(e => new
                {
                    speaker = e.Speaker.ToString(),
                    text = e.Text,
                    timestamp = e.Timestamp
                }).ToList(),
                phoneNumber = record.PhoneNumber,
                campaignName = record.CampaignTitle ?? ""
            });
        }

        public async Task<string> GetCallerCaseHistoryAsync(string phoneNumber)
        {
            var cases = await _caseManagementService.ListCasesAsync();
            var matched = cases
                .Where(c => c.CallerPhoneNumber == phoneNumber)
                .OrderByDescending(c => c.CreatedAt)
                .Take(10)
                .Select(c => new
                {
                    caseId = c.Id,
                    title = c.Title,
                    status = c.Status.ToString(),
                    createdAt = c.CreatedAt,
                    description = c.Description?.Length > 200 ? c.Description[..200] + "..." : c.Description ?? ""
                })
                .ToList();
            return JsonSerializer.Serialize(matched);
        }

        public async Task<string> SaveSummaryAsync(string summaryJson)
        {
            var dto = JsonSerializer.Deserialize<SaveSummaryDto>(summaryJson,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            if (dto == null) return JsonSerializer.Serialize(new { saved = false });

            var record = await _callHistoryService.GetByIdAsync(dto.CallConnectionId);
            if (record == null) return JsonSerializer.Serialize(new { saved = false });

            record.CallSummary = dto.Summary;
            record.SummarizedAt = DateTimeOffset.UtcNow;
            await _callHistoryService.SaveCallRecordAsync(record);

            return JsonSerializer.Serialize(new { saved = true });
        }

        public List<AgentToolDefinition> GetToolDefinitions()
        {
            return new List<AgentToolDefinition>
            {
                new("get_call_transcript",
                    "Get the full transcript of a call",
                    """{"type":"object","properties":{"callConnectionId":{"type":"string","description":"The call connection ID"}},"required":["callConnectionId"]}"""),
                new("get_caller_case_history",
                    "Get prior cases from the same caller",
                    """{"type":"object","properties":{"phoneNumber":{"type":"string","description":"Caller phone number"}},"required":["phoneNumber"]}"""),
                new("save_summary",
                    "Save the generated summary for a call",
                    """{"type":"object","properties":{"summary":{"type":"string","description":"JSON with callConnectionId and summary text"}},"required":["summary"]}""")
            };
        }

        public async Task<string> DispatchToolCallAsync(string functionName, string arguments)
        {
            var args = JsonDocument.Parse(arguments).RootElement;
            return functionName switch
            {
                "get_call_transcript" => await GetCallTranscriptAsync(args.GetProperty("callConnectionId").GetString()!),
                "get_caller_case_history" => await GetCallerCaseHistoryAsync(args.GetProperty("phoneNumber").GetString()!),
                "save_summary" => await SaveSummaryAsync(args.GetProperty("summary").GetString()!),
                _ => JsonSerializer.Serialize(new { error = $"Unknown tool: {functionName}" })
            };
        }

        private class SaveSummaryDto
        {
            public string CallConnectionId { get; set; } = string.Empty;
            public string Summary { get; set; } = string.Empty;
        }
    }
}
