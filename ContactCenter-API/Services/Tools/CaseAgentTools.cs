using ContactCenterPOC.Models;
using System.Text.Json;

namespace ContactCenterPOC.Services.Tools
{
    public class CaseAgentTools
    {
        private readonly CallHistoryService _callHistoryService;
        private readonly CaseManagementService _caseManagementService;

        public CaseAgentTools(CallHistoryService callHistoryService, CaseManagementService caseManagementService)
        {
            _callHistoryService = callHistoryService;
            _caseManagementService = caseManagementService;
        }

        public async Task<string> GetCallRecordAsync(string callConnectionId)
        {
            var record = await _callHistoryService.GetByIdAsync(callConnectionId);
            if (record == null)
                return JsonSerializer.Serialize(new { error = "Call record not found" });

            return JsonSerializer.Serialize(new
            {
                phoneNumber = record.PhoneNumber,
                summary = record.CallSummary ?? "",
                sentiment = record.OverallSentiment.ToString(),
                campaignId = record.CampaignId ?? "",
                transcriptEntries = record.TranscriptEntries?.Select(e => new
                {
                    speaker = e.Speaker.ToString(),
                    text = e.Text,
                    timestamp = e.Timestamp
                }).ToList()
            });
        }

        public async Task<string> FindCasesByCallerAsync(string phoneNumber, int sinceHours)
        {
            var cases = await _caseManagementService.ListCasesAsync();
            var since = DateTimeOffset.UtcNow.AddHours(-sinceHours);
            var matched = cases
                .Where(c => c.CallerPhoneNumber == phoneNumber && c.UpdatedAt >= since)
                .Select(c => new
                {
                    id = c.Id,
                    status = c.Status.ToString(),
                    title = c.Title,
                    updatedAt = c.UpdatedAt
                })
                .ToList();
            return JsonSerializer.Serialize(matched);
        }

        public async Task<string> CreateCaseAsync(string caseJson)
        {
            var dto = JsonSerializer.Deserialize<CreateCaseDto>(caseJson,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            if (dto == null) return JsonSerializer.Serialize(new { error = "Invalid case JSON" });

            var newCase = new Case
            {
                CallerPhoneNumber = dto.CallerPhone,
                Title = dto.Title,
                Description = dto.Description,
                Priority = Enum.TryParse<CasePriority>(dto.Priority, true, out var p) ? p : CasePriority.Medium,
                Status = CaseStatus.Open,
                CampaignId = dto.CampaignId,
                CallSource = dto.CallSource,
                LinkedCallRecords = string.IsNullOrEmpty(dto.CallRecordId) ? new() : new List<string> { dto.CallRecordId }
            };

            var saved = await _caseManagementService.CreateCaseAsync(newCase);
            return JsonSerializer.Serialize(new { caseId = saved.Id });
        }

        public async Task<string> UpdateCaseAsync(string updateJson)
        {
            var dto = JsonSerializer.Deserialize<UpdateCaseDto>(updateJson,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            if (dto == null) return JsonSerializer.Serialize(new { error = "Invalid update JSON" });

            var existingCase = await _caseManagementService.GetCaseAsync(dto.CaseId);
            if (existingCase == null) return JsonSerializer.Serialize(new { updated = false });

            if (!string.IsNullOrEmpty(dto.Status) && Enum.TryParse<CaseStatus>(dto.Status, true, out var status))
                existingCase.Status = status;
            if (!string.IsNullOrEmpty(dto.AppendDescription))
                existingCase.Description += $"\n\nFollow-up ({DateTimeOffset.UtcNow:g}): {dto.AppendDescription}";
            if (!string.IsNullOrEmpty(dto.CallRecordId))
                existingCase.LinkedCallRecords.Add(dto.CallRecordId);
            existingCase.UpdatedAt = DateTimeOffset.UtcNow;

            await _caseManagementService.UpdateCaseAsync(existingCase);
            return JsonSerializer.Serialize(new { updated = true });
        }

        public List<AgentToolDefinition> GetToolDefinitions()
        {
            return new List<AgentToolDefinition>
            {
                new("get_call_record",
                    "Get call record details for case processing",
                    """{"type":"object","properties":{"callConnectionId":{"type":"string","description":"The call connection ID"}},"required":["callConnectionId"]}"""),
                new("find_cases_by_caller",
                    "Find existing cases for a caller within a time window",
                    """{"type":"object","properties":{"phoneNumber":{"type":"string","description":"Caller phone number"},"sinceHours":{"type":"integer","description":"Look back window in hours"}},"required":["phoneNumber","sinceHours"]}"""),
                new("create_case",
                    "Create a new case from call outcome",
                    """{"type":"object","properties":{"case":{"type":"string","description":"JSON with callerPhone, title, description, priority, callRecordId, campaignId, callSource"}},"required":["case"]}"""),
                new("update_case",
                    "Update an existing case",
                    """{"type":"object","properties":{"update":{"type":"string","description":"JSON with caseId, status, appendDescription, callRecordId"}},"required":["update"]}""")
            };
        }

        public async Task<string> DispatchToolCallAsync(string functionName, string arguments)
        {
            var args = JsonDocument.Parse(arguments).RootElement;
            return functionName switch
            {
                "get_call_record" => await GetCallRecordAsync(args.GetProperty("callConnectionId").GetString()!),
                "find_cases_by_caller" => await FindCasesByCallerAsync(
                    args.GetProperty("phoneNumber").GetString()!,
                    args.GetProperty("sinceHours").GetInt32()),
                "create_case" => await CreateCaseAsync(args.GetProperty("case").GetString()!),
                "update_case" => await UpdateCaseAsync(args.GetProperty("update").GetString()!),
                _ => JsonSerializer.Serialize(new { error = $"Unknown tool: {functionName}" })
            };
        }

        private class CreateCaseDto
        {
            public string CallerPhone { get; set; } = string.Empty;
            public string Title { get; set; } = string.Empty;
            public string Description { get; set; } = string.Empty;
            public string Priority { get; set; } = "Medium";
            public string? CallRecordId { get; set; }
            public string? CampaignId { get; set; }
            public string? CallSource { get; set; }
        }

        private class UpdateCaseDto
        {
            public string CaseId { get; set; } = string.Empty;
            public string? Status { get; set; }
            public string? AppendDescription { get; set; }
            public string? CallRecordId { get; set; }
        }
    }
}
