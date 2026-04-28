using Azure.AI.OpenAI;
using Azure.Identity;
using ContactCenterPOC.Models;
using OpenAI.Chat;
using System.Text.Json;

namespace ContactCenterPOC.Services
{
    public class CaseManagementService
    {
        private readonly CosmosDbService _cosmosDb;
        private readonly AgentActivityService _agentActivityService;
        private readonly IConfiguration _configuration;
        private readonly ILogger<CaseManagementService> _logger;
        private const string ContainerName = "Cases";

        public CaseManagementService(
            CosmosDbService cosmosDb, AgentActivityService agentActivityService,
            IConfiguration configuration, ILogger<CaseManagementService> logger)
        {
            _cosmosDb = cosmosDb;
            _agentActivityService = agentActivityService;
            _configuration = configuration;
            _logger = logger;
        }

        public async Task<List<Case>> ListCasesAsync(string? status = null, string? priority = null, int limit = 50)
        {
            var conditions = new List<string> { "1=1" };
            var parameters = new Dictionary<string, object>();

            if (!string.IsNullOrEmpty(status) && Enum.TryParse<CaseStatus>(status, true, out var statusEnum))
            {
                conditions.Add("c.status = @status");
                parameters["@status"] = (int)statusEnum;
            }

            if (!string.IsNullOrEmpty(priority) && Enum.TryParse<CasePriority>(priority, true, out var priorityEnum))
            {
                conditions.Add("c.priority = @priority");
                parameters["@priority"] = (int)priorityEnum;
            }

            var query = $"SELECT TOP {limit} * FROM c WHERE {string.Join(" AND ", conditions)} ORDER BY c.updatedAt DESC";
            return await _cosmosDb.QueryAsync<Case>(ContainerName, query, parameters);
        }

        public async Task<Case?> GetCaseAsync(string id)
        {
            var results = await _cosmosDb.QueryAsync<Case>(ContainerName,
                "SELECT * FROM c WHERE c.id = @id",
                new Dictionary<string, object> { ["@id"] = id });
            return results.FirstOrDefault();
        }

        public async Task<Case?> ResolveAsync(string id, string? resolutionSummary = null)
        {
            var caseRecord = await GetCaseAsync(id);
            if (caseRecord == null) return null;

            caseRecord.Status = CaseStatus.Resolved;
            caseRecord.ResolutionSummary = resolutionSummary;
            caseRecord.ResolvedAt = DateTimeOffset.UtcNow;
            caseRecord.UpdatedAt = DateTimeOffset.UtcNow;

            return await _cosmosDb.UpsertAsync(ContainerName, caseRecord, caseRecord.CallerPhoneNumber);
        }

        public async Task<Case?> CloseAsync(string id)
        {
            var caseRecord = await GetCaseAsync(id);
            if (caseRecord == null || caseRecord.Status != CaseStatus.Resolved) return null;

            caseRecord.Status = CaseStatus.Closed;
            caseRecord.ClosedAt = DateTimeOffset.UtcNow;
            caseRecord.UpdatedAt = DateTimeOffset.UtcNow;

            return await _cosmosDb.UpsertAsync(ContainerName, caseRecord, caseRecord.CallerPhoneNumber);
        }

        public async Task<object> GetSummaryAsync()
        {
            var cases = await _cosmosDb.QueryAsync<Case>(ContainerName, "SELECT * FROM c");
            return new
            {
                total = cases.Count,
                open = cases.Count(c => c.Status == CaseStatus.Open),
                inProgress = cases.Count(c => c.Status == CaseStatus.InProgress),
                resolved = cases.Count(c => c.Status == CaseStatus.Resolved),
                closed = cases.Count(c => c.Status == CaseStatus.Closed)
            };
        }

        public async Task<object> GetCaseCountsByStatusAsync(DateTimeOffset from, DateTimeOffset to)
        {
            var cases = await _cosmosDb.QueryAsync<Case>(ContainerName, "SELECT * FROM c");
            var filtered = cases.Where(c => c.CreatedAt >= from && c.CreatedAt <= to).ToList();
            return new
            {
                total = filtered.Count,
                open = filtered.Count(c => c.Status == CaseStatus.Open),
                inProgress = filtered.Count(c => c.Status == CaseStatus.InProgress),
                resolved = filtered.Count(c => c.Status == CaseStatus.Resolved),
                closed = filtered.Count(c => c.Status == CaseStatus.Closed)
            };
        }

        public async Task<Case> CreateCaseAsync(Case newCase)
        {
            newCase.CreatedAt = DateTimeOffset.UtcNow;
            newCase.UpdatedAt = DateTimeOffset.UtcNow;
            return await _cosmosDb.UpsertAsync(ContainerName, newCase, newCase.CallerPhoneNumber);
        }

        public async Task<Case> UpdateCaseAsync(Case existingCase)
        {
            existingCase.UpdatedAt = DateTimeOffset.UtcNow;
            return await _cosmosDb.UpsertAsync(ContainerName, existingCase, existingCase.CallerPhoneNumber);
        }

        public async Task ProcessCallOutcomeAsync(CallRecord callRecord)
        {
            await _agentActivityService.ExecuteWithLoggingAsync("CaseManagement", "CaseCreated", callRecord.CallConnectionId, async () =>
            {
                var phoneNumber = callRecord.PhoneNumber;
                if (string.IsNullOrEmpty(phoneNumber)) return (Case?)null;

                // Check for existing open/in-progress cases from the same caller (24h dedup window)
                var existingCases = await _cosmosDb.QueryAsync<Case>(ContainerName,
                    "SELECT * FROM c WHERE c.callerPhoneNumber = @phone AND (c.status = 0 OR c.status = 1) AND c.updatedAt >= @since",
                    new Dictionary<string, object>
                    {
                        ["@phone"] = phoneNumber,
                        ["@since"] = DateTimeOffset.UtcNow.AddHours(-24).ToString("o")
                    });

                if (existingCases.Count > 0)
                {
                    // Update existing case
                    var existingCase = existingCases.First();
                    existingCase.Status = CaseStatus.InProgress;
                    existingCase.LinkedCallRecords.Add(callRecord.CallConnectionId);
                    existingCase.UpdatedAt = DateTimeOffset.UtcNow;

                    if (!string.IsNullOrEmpty(callRecord.CallSummary))
                        existingCase.Description += $"\n\nFollow-up ({DateTimeOffset.UtcNow:g}): {callRecord.CallSummary}";

                    await _cosmosDb.UpsertAsync(ContainerName, existingCase, existingCase.CallerPhoneNumber);
                    _logger.LogInformation("Updated existing case {CaseId} for caller {Phone}", existingCase.Id, phoneNumber);
                    return existingCase;
                }

                // Create new case
                var title = callRecord.CallSummary?.Length > 100
                    ? callRecord.CallSummary[..100] + "..."
                    : callRecord.CallSummary ?? "Call inquiry";

                var priority = callRecord.OverallSentiment == SentimentLabel.Negative
                    ? CasePriority.High
                    : CasePriority.Medium;

                var newCase = new Case
                {
                    CallerPhoneNumber = phoneNumber,
                    Title = title,
                    Description = callRecord.CallSummary ?? "Automatic case from call",
                    Priority = priority,
                    LinkedCallRecords = new List<string> { callRecord.CallConnectionId },
                    CampaignId = callRecord.CampaignId,
                    CallSource = "Phone" // Default — will be overridden for inbound/internet
                };

                await _cosmosDb.UpsertAsync(ContainerName, newCase, newCase.CallerPhoneNumber);
                _logger.LogInformation("Created case {CaseId} for caller {Phone}", newCase.Id, phoneNumber);
                return newCase;
            });
        }
    }
}
