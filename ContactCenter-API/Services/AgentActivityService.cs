using ContactCenterPOC.Models;
using System.Diagnostics;

namespace ContactCenterPOC.Services
{
    public class AgentActivityService
    {
        private readonly CosmosDbService _cosmosDb;
        private readonly ILogger<AgentActivityService> _logger;
        private const string ContainerName = "AgentActivity";

        public AgentActivityService(CosmosDbService cosmosDb, ILogger<AgentActivityService> logger)
        {
            _cosmosDb = cosmosDb;
            _logger = logger;
        }

        public async Task<AgentActivityEntry> LogActivityAsync(
            string agentName, string actionType, AgentActionResult result,
            string? resultDetail = null, string? callRecordId = null, long durationMs = 0)
        {
            var entry = new AgentActivityEntry
            {
                AgentName = agentName,
                ActionType = actionType,
                Result = result,
                ResultDetail = resultDetail,
                CallRecordId = callRecordId,
                DurationMs = durationMs,
                Timestamp = DateTimeOffset.UtcNow
            };

            await _cosmosDb.UpsertAsync(ContainerName, entry, entry.AgentName);
            _logger.LogInformation("Agent activity logged: {Agent}/{Action} = {Result}", agentName, actionType, result);
            return entry;
        }

        public async Task<T> ExecuteWithLoggingAsync<T>(
            string agentName, string actionType, string? callRecordId,
            Func<Task<T>> action)
        {
            var sw = Stopwatch.StartNew();
            try
            {
                var result = await action();
                sw.Stop();
                await LogActivityAsync(agentName, actionType, AgentActionResult.Success,
                    callRecordId: callRecordId, durationMs: sw.ElapsedMilliseconds);
                return result;
            }
            catch (Exception ex)
            {
                sw.Stop();
                await LogActivityAsync(agentName, actionType, AgentActionResult.Failure,
                    resultDetail: ex.Message, callRecordId: callRecordId, durationMs: sw.ElapsedMilliseconds);
                throw;
            }
        }

        public async Task<List<AgentActivityEntry>> GetRecentAsync(
            string? agentName = null, string? result = null, int max = 200)
        {
            var conditions = new List<string> { "1=1" };
            var parameters = new Dictionary<string, object>();

            if (!string.IsNullOrEmpty(agentName))
            {
                conditions.Add("c.agentName = @agentName");
                parameters["@agentName"] = agentName;
            }

            if (!string.IsNullOrEmpty(result))
            {
                conditions.Add("c.result = @result");
                // Convert string result to enum int value
                if (Enum.TryParse<AgentActionResult>(result, true, out var resultEnum))
                {
                    parameters["@result"] = (int)resultEnum;
                }
                else
                {
                    parameters["@result"] = result;
                }
            }

            var query = $"SELECT TOP {max} * FROM c WHERE {string.Join(" AND ", conditions)} ORDER BY c.timestamp DESC";
            return await _cosmosDb.QueryAsync<AgentActivityEntry>(ContainerName, query, parameters);
        }

        public async Task<object> GetSummaryAsync(int hours = 24)
        {
            var since = DateTimeOffset.UtcNow.AddHours(-hours).ToString("o");
            var query = "SELECT * FROM c WHERE c.timestamp >= @since";
            var entries = await _cosmosDb.QueryAsync<AgentActivityEntry>(ContainerName, query,
                new Dictionary<string, object> { ["@since"] = since });

            var byAgent = entries.GroupBy(e => e.AgentName).Select(g => new
            {
                agentName = g.Key,
                totalActions = g.Count(),
                successCount = g.Count(e => e.Result == AgentActionResult.Success),
                failureCount = g.Count(e => e.Result == AgentActionResult.Failure),
                skippedCount = g.Count(e => e.Result == AgentActionResult.Skipped),
                averageDurationMs = g.Any() ? (long)g.Average(e => e.DurationMs) : 0
            }).ToList();

            return new
            {
                totalActions = entries.Count,
                successCount = entries.Count(e => e.Result == AgentActionResult.Success),
                failureCount = entries.Count(e => e.Result == AgentActionResult.Failure),
                skippedCount = entries.Count(e => e.Result == AgentActionResult.Skipped),
                byAgent
            };
        }
    }
}
