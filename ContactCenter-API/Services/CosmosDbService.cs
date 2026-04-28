using Microsoft.Azure.Cosmos;
using Microsoft.Azure.Cosmos.Linq;

namespace ContactCenterPOC.Services
{
    public class CosmosDbService
    {
        private readonly CosmosClient _cosmosClient;
        private readonly string _databaseId;
        private readonly ILogger<CosmosDbService> _logger;

        public CosmosDbService(CosmosClient cosmosClient, IConfiguration configuration, ILogger<CosmosDbService> logger)
        {
            _cosmosClient = cosmosClient;
            _databaseId = configuration["CosmosDb:DatabaseId"] ?? "CallCenterPOC";
            _logger = logger;
        }

        private Container GetContainer(string containerName)
        {
            return _cosmosClient.GetContainer(_databaseId, containerName);
        }

        public virtual async Task<T?> GetAsync<T>(string containerName, string id, string partitionKey) where T : class
        {
            try
            {
                var container = GetContainer(containerName);
                var response = await container.ReadItemAsync<T>(id, new PartitionKey(partitionKey));
                return response.Resource;
            }
            catch (CosmosException ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
            {
                return null;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to get item {Id} from {Container}", id, containerName);
                return null;
            }
        }

        public virtual async Task<T> UpsertAsync<T>(string containerName, T item, string partitionKey) where T : class
        {
            try
            {
                var container = GetContainer(containerName);
                var response = await container.UpsertItemAsync(item, new PartitionKey(partitionKey));
                return response.Resource;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to upsert item in {Container} with partition key {PartitionKey}", containerName, partitionKey);
                return item;
            }
        }

        public virtual async Task DeleteAsync(string containerName, string id, string partitionKey)
        {
            try
            {
                var container = GetContainer(containerName);
                await container.DeleteItemAsync<object>(id, new PartitionKey(partitionKey));
            }
            catch (CosmosException ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
            {
                _logger.LogWarning("Item {Id} not found in {Container} for deletion", id, containerName);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to delete item {Id} from {Container}", id, containerName);
                throw;
            }
        }

        public virtual async Task<List<T>> QueryAsync<T>(string containerName, string query, Dictionary<string, object>? parameters = null) where T : class
        {
            try
            {
                var container = GetContainer(containerName);
                var queryDefinition = new QueryDefinition(query);

                if (parameters != null)
                {
                    foreach (var param in parameters)
                    {
                        queryDefinition = queryDefinition.WithParameter(param.Key, param.Value);
                    }
                }

                var results = new List<T>();
                using var iterator = container.GetItemQueryIterator<T>(queryDefinition);
                while (iterator.HasMoreResults)
                {
                    var response = await iterator.ReadNextAsync();
                    results.AddRange(response);
                }
                return results;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to query {Container}: {Query}", containerName, query);
                return new List<T>();
            }
        }

        public virtual async Task<(List<T> Items, string? ContinuationToken)> QueryPagedAsync<T>(
            string containerName, string query,
            Dictionary<string, object>? parameters = null,
            int maxItemCount = 50,
            string? continuationToken = null) where T : class
        {
            try
            {
                var container = GetContainer(containerName);
                var queryDefinition = new QueryDefinition(query);

                if (parameters != null)
                {
                    foreach (var param in parameters)
                    {
                        queryDefinition = queryDefinition.WithParameter(param.Key, param.Value);
                    }
                }

                var queryOptions = new QueryRequestOptions { MaxItemCount = maxItemCount };
                var results = new List<T>();

                using var iterator = container.GetItemQueryIterator<T>(
                    queryDefinition, continuationToken, queryOptions);

                if (iterator.HasMoreResults)
                {
                    var response = await iterator.ReadNextAsync();
                    results.AddRange(response);
                    return (results, response.ContinuationToken);
                }

                return (results, null);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to query paged {Container}: {Query}", containerName, query);
                return (new List<T>(), null);
            }
        }
    }
}
