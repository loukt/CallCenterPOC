using System.Text.Json.Serialization;
using Azure.AI.Projects;
using Azure.Identity;
using Azure.Search.Documents;
using Azure.Storage.Blobs;
using ContactCenterPOC.Hubs;
using ContactCenterPOC.Models;
using ContactCenterPOC.Services;
using ContactCenterPOC.Services.Tools;
using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.Options;

var builder = WebApplication.CreateBuilder(args);

// Bind VoiceLive configuration
builder.Services.Configure<VoiceLiveConfig>(builder.Configuration.GetSection("VoiceLive"));
builder.Services.AddSingleton(sp => sp.GetRequiredService<IOptions<VoiceLiveConfig>>().Value);

// Add services to the container.
builder.Services.AddSingleton<CallService>();

// Register BlobServiceClient for campaign + call history persistence
var blobConnectionString = builder.Configuration["BlobStorage:ConnectionString"];
var blobAccountUri = builder.Configuration["BlobStorage:AccountUri"];
var configuredContainerName = builder.Configuration["BlobStorage:ContainerName"];

static bool IsPlaceholderContainerName(string? containerName)
{
    return string.IsNullOrWhiteSpace(containerName) ||
           string.Equals(containerName, "callcenter-data", StringComparison.OrdinalIgnoreCase);
}

// Ensure container name is set when a BlobContainer URL is provided (common in Azure)
// Example: https://account.blob.core.windows.net/container
var blobContainerUrlForDerivation = builder.Configuration["BlobContainer"];
if (!string.IsNullOrEmpty(blobContainerUrlForDerivation) && Uri.TryCreate(blobContainerUrlForDerivation, UriKind.Absolute, out var derivedContainerUri))
{
    // Extract container name from path (e.g. /callsstorage → callsstorage)
    var derivedContainerName = derivedContainerUri.AbsolutePath.Trim('/');
    if (!string.IsNullOrEmpty(derivedContainerName) && IsPlaceholderContainerName(configuredContainerName))
    {
        builder.Configuration["BlobStorage:ContainerName"] = derivedContainerName;
        configuredContainerName = derivedContainerName;
    }

    // If account URI is not explicitly set, derive it from the container URL
    if (string.IsNullOrEmpty(blobAccountUri))
    {
        blobAccountUri = $"{derivedContainerUri.Scheme}://{derivedContainerUri.Host}";
    }
}

// Derive BlobStorage:AccountUri and ContainerName from BlobContainer URL if not explicitly set
if (string.IsNullOrEmpty(blobConnectionString) && string.IsNullOrEmpty(blobAccountUri))
{
    var blobContainerUrl = builder.Configuration["BlobContainer"];
    if (!string.IsNullOrEmpty(blobContainerUrl) && Uri.TryCreate(blobContainerUrl, UriKind.Absolute, out var containerUri))
    {
        // Extract storage account base URL: https://account.blob.core.windows.net
        blobAccountUri = $"{containerUri.Scheme}://{containerUri.Host}";

        // Extract container name from path (e.g. /callsstorage → callsstorage)
        var containerName = containerUri.AbsolutePath.Trim('/');
        if (!string.IsNullOrEmpty(containerName) && IsPlaceholderContainerName(configuredContainerName))
        {
            builder.Configuration["BlobStorage:ContainerName"] = containerName;
            configuredContainerName = containerName;
        }
    }
}

if (!string.IsNullOrEmpty(blobConnectionString))
{
    // Local dev: use connection string
    builder.Services.AddSingleton(new BlobServiceClient(blobConnectionString));
}
else if (!string.IsNullOrEmpty(blobAccountUri))
{
    // Azure deployment: use DefaultAzureCredential (Managed Identity)
    builder.Services.AddSingleton(new BlobServiceClient(new Uri(blobAccountUri), new DefaultAzureCredential()));
}
else
{
    // Fallback: in-memory BlobServiceClient (for testing / no blob config)
    builder.Services.AddSingleton(new BlobServiceClient("UseDevelopmentStorage=true"));
}

builder.Services.AddSingleton<CampaignService>();
builder.Services.AddSingleton<SentimentAnalysisService>();
builder.Services.AddSingleton<EmotionAnalysisService>();
builder.Services.AddSingleton<OperatorStyleAnalysisService>();
builder.Services.AddSingleton<CallSummaryService>();
builder.Services.AddSingleton<SettingsService>();
builder.Services.AddSingleton<CallHistoryService>();
builder.Services.AddHttpClient("AzureOpenAITranscription", client =>
{
    client.Timeout = TimeSpan.FromMinutes(10);
});
builder.Services.AddSingleton<RecordingTranscriptionService>();

// Feature 003: Cosmos DB client
var cosmosEndpoint = builder.Configuration["CosmosDb:Endpoint"];
if (!string.IsNullOrEmpty(cosmosEndpoint))
{
    builder.Services.AddSingleton(new CosmosClient(cosmosEndpoint, new DefaultAzureCredential(),
        new CosmosClientOptions { SerializerOptions = new CosmosSerializationOptions { PropertyNamingPolicy = CosmosPropertyNamingPolicy.CamelCase } }));
}
else
{
    // Fallback for local dev / emulator — use connection string if set via user secrets
    var cosmosConnectionString = builder.Configuration["CosmosDb:ConnectionString"];
    if (!string.IsNullOrEmpty(cosmosConnectionString))
    {
        builder.Services.AddSingleton(new CosmosClient(cosmosConnectionString,
            new CosmosClientOptions { SerializerOptions = new CosmosSerializationOptions { PropertyNamingPolicy = CosmosPropertyNamingPolicy.CamelCase } }));
    }
    else
    {
        // Register a null-safe placeholder — services will fail gracefully at runtime
        builder.Services.AddSingleton(new CosmosClient("AccountEndpoint=https://localhost:8081/;AccountKey=C2y6yDjf5/R+ob0N8A7Cgv30VRDJIWEHLM+4QDU5DE2nQ9nDuVTqobD4b8mGGyPMbIZnqyMsEcaGQy67XIw/Jw==",
            new CosmosClientOptions { SerializerOptions = new CosmosSerializationOptions { PropertyNamingPolicy = CosmosPropertyNamingPolicy.CamelCase } }));
    }
}
builder.Services.AddSingleton<CosmosDbService>();

// Feature 003: Azure AI Search client
var searchEndpoint = builder.Configuration["AzureAISearch:Endpoint"];
if (!string.IsNullOrEmpty(searchEndpoint))
{
    builder.Services.AddSingleton(new Azure.Search.Documents.Indexes.SearchIndexClient(
        new Uri(searchEndpoint), new DefaultAzureCredential()));
}

// Feature 003: New services
builder.Services.AddSingleton<AgentActivityService>();
builder.Services.AddSingleton<SearchService>();
builder.Services.AddSingleton<KnowledgeBaseService>();
builder.Services.AddSingleton<IntentDiscoveryService>();
builder.Services.AddSingleton<EscalationService>();
builder.Services.AddSingleton<CaseManagementService>();
builder.Services.AddSingleton<QualityEvaluationService>();
builder.Services.AddSingleton<KnowledgeGapService>();
builder.Services.AddSingleton<WebRTCSignalingService>();

// Feature 004: AI Foundry Agent Migration
var foundryConfig = builder.Configuration.GetSection("AIFoundry").Get<FoundryAgentConfig>() ?? new FoundryAgentConfig();
builder.Services.AddSingleton(foundryConfig);

var foundryEndpoint = foundryConfig.ProjectEndpoint;
if (!string.IsNullOrEmpty(foundryEndpoint))
{
    builder.Services.AddSingleton(new AIProjectClient(new Uri(foundryEndpoint), new DefaultAzureCredential()));
}
else
{
    builder.Services.AddSingleton<AIProjectClient?>(sp => null);
}

// Feature 004: Foundry agent tool classes
builder.Services.AddSingleton<IntentAgentTools>();
builder.Services.AddSingleton<CaseAgentTools>();
builder.Services.AddSingleton<QualityAgentTools>();
builder.Services.AddSingleton<KnowledgeGapAgentTools>();
builder.Services.AddSingleton<SummaryAgentTools>();
builder.Services.AddSingleton<PostCallReviewAgentTools>();

// Feature 004: Orchestration + Audio Emotion services
builder.Services.AddSingleton<OrchestrationService>();
builder.Services.AddSingleton<AudioEmotionService>();

builder.Services.AddControllers()
    .AddJsonOptions(options =>
    {
        options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter());
    });
// Learn more about configuring Swagger/OpenAPI at https://aka.ms/aspnetcore/swashbuckle
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
builder.Services.AddSignalR();

// CORS policy for frontend SignalR connections
static string NormalizeOrigin(string origin)
{
    return origin.Trim().TrimEnd('/');
}

static string[] SplitOrigins(string origins)
{
    return origins
        .Split(new[] { ',', ';', ' ' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
        .Select(NormalizeOrigin)
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .ToArray();
}

var configuredOrigins = builder.Configuration.GetSection("FrontendOrigins").Get<string[]>();
var configuredOrigin = builder.Configuration["FrontendOrigin"];

var frontendOrigins = (configuredOrigins is { Length: > 0 })
    ? configuredOrigins.Select(NormalizeOrigin).ToArray()
    : (!string.IsNullOrWhiteSpace(configuredOrigin)
        ? SplitOrigins(configuredOrigin)
        : new[]
        {
            "http://localhost:5002",
            "https://localhost:5002",
            "http://127.0.0.1:5002",
            "https://127.0.0.1:5002"
        });

builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowFrontend", policy =>
    {
        policy.WithOrigins(frontendOrigins)
              .AllowAnyHeader()
              .AllowAnyMethod()
              .AllowCredentials();
    });
});

var app = builder.Build();

// T032: Startup validation — warn if VoiceLive not configured
var vlStartupConfig = app.Services.GetRequiredService<VoiceLiveConfig>();
if (!vlStartupConfig.IsConfigured)
{
    app.Logger.LogInformation("VoiceLive endpoint not configured — VoiceLive mode will be unavailable.");
}
else
{
    app.Logger.LogInformation("VoiceLive configured with endpoint: {Endpoint}", vlStartupConfig.EndpointUri);
}

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseWebSockets();

app.UseStaticFiles();

app.UseHttpsRedirection();

app.UseCors("AllowFrontend");

app.UseAuthorization();

app.MapControllers();
app.MapHub<TranscriptHub>("/transcriptHub");

// VoiceLive diagnostic endpoint — tests actual session connectivity
app.MapGet("/api/voicelive/diagnose", async (VoiceLiveConfig vlConfig, ILogger<Program> logger) =>
{
    if (!vlConfig.IsConfigured)
        return Results.Ok(new { success = false, error = "VoiceLive not configured (no EndpointUri)" });

    try
    {
        var endpoint = new Uri(vlConfig.EndpointUri);
        Azure.AI.VoiceLive.VoiceLiveClient client;
        string authMethod;
        if (!string.IsNullOrWhiteSpace(vlConfig.Key))
        {
            client = new Azure.AI.VoiceLive.VoiceLiveClient(endpoint, new Azure.AzureKeyCredential(vlConfig.Key));
            authMethod = "ApiKey";
        }
        else
        {
            client = new Azure.AI.VoiceLive.VoiceLiveClient(endpoint, new Azure.Identity.DefaultAzureCredential());
            authMethod = "ManagedIdentity";
        }

        // Try multiple models - GA first, then preview
        var modelsToTry = new[] { "gpt-realtime-mini", "gpt-4o-mini-realtime-preview" };
        string model = modelsToTry[0];
        var errors = new List<string>();
        
        foreach (var tryModel in modelsToTry)
        {
            model = tryModel;
            logger.LogInformation("[VL-Diagnose] Trying model={Model}, endpoint={Endpoint}, auth={Auth}",
                model, vlConfig.EndpointUri, authMethod);
            try
            {
                var session2 = await client.StartSessionAsync(model);
                logger.LogInformation("[VL-Diagnose] Session started OK with model={Model}", model);
                // If we get here, the model worked
                session2.Dispose();
                break;
            }
            catch (Exception ex2)
            {
                errors.Add($"{tryModel}: {ex2.GetType().Name} - {ex2.Message}");
                logger.LogWarning(ex2, "[VL-Diagnose] Model {Model} failed", tryModel);
                if (tryModel == modelsToTry[^1])
                {
                    return Results.Ok(new { success = false, error = "All models failed", details = errors, authMethod });
                }
            }
        }

        var session = await client.StartSessionAsync(model);
        logger.LogInformation("[VL-Diagnose] Session started OK, configuring...");

        var options = new Azure.AI.VoiceLive.VoiceLiveSessionOptions
        {
            Model = model,
            Instructions = "Say hello briefly.",
            Voice = new Azure.AI.VoiceLive.AzureStandardVoice("en-US-AvaNeural"),
            InputAudioFormat = Azure.AI.VoiceLive.InputAudioFormat.Pcm16,
            OutputAudioFormat = Azure.AI.VoiceLive.OutputAudioFormat.Pcm16,
        };
        options.Modalities.Clear();
        options.Modalities.Add(Azure.AI.VoiceLive.InteractionModality.Text);
        options.Modalities.Add(Azure.AI.VoiceLive.InteractionModality.Audio);

        await session.ConfigureSessionAsync(options);
        logger.LogInformation("[VL-Diagnose] Session configured OK");

        // Send a text message to trigger a response
        await session.AddItemAsync(new Azure.AI.VoiceLive.UserMessageItem("Hello"));
        await session.StartResponseAsync();

        // Collect a few updates to confirm audio comes back
        var events = new List<string>();
        int audioChunks = 0;
        var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        try
        {
            await foreach (var update in session.GetUpdatesAsync(cts.Token))
            {
                var typeName = update.GetType().Name;
                if (!events.Contains(typeName)) events.Add(typeName);
                if (update is Azure.AI.VoiceLive.SessionUpdateResponseAudioDelta) audioChunks++;
                if (update is Azure.AI.VoiceLive.SessionUpdateResponseDone) break;
            }
        }
        catch (OperationCanceledException) { events.Add("Timeout(10s)"); }

        session.Dispose();

        logger.LogInformation("[VL-Diagnose] Done. Events: {Events}, AudioChunks: {AudioChunks}",
            string.Join(", ", events), audioChunks);

        return Results.Ok(new
        {
            success = true,
            authMethod,
            model,
            eventsReceived = events,
            audioChunksReceived = audioChunks,
            audioWorking = audioChunks > 0
        });
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "[VL-Diagnose] Failed");
        return Results.Ok(new { success = false, error = ex.Message, exceptionType = ex.GetType().Name, inner = ex.InnerException?.Message });
    }
});

// Health check endpoint for load balancer probes and monitoring
app.MapGet("/healthz", (VoiceLiveConfig vlConfig) =>
{
    var maskedEndpoint = "";
    if (!string.IsNullOrEmpty(vlConfig.EndpointUri) && Uri.TryCreate(vlConfig.EndpointUri, UriKind.Absolute, out var uri))
    {
        var hostParts = uri.Host.Split('.');
        maskedEndpoint = hostParts.Length > 0 ? uri.Host.Replace(hostParts[0], "*") : uri.Host;
    }
    return Results.Ok(new
    {
        status = "healthy",
        timestamp = DateTimeOffset.UtcNow,
        voiceLive = new
        {
            configured = vlConfig.IsConfigured,
            endpoint = maskedEndpoint
        }
    });
});

app.Run();

// Required for WebApplicationFactory<Program> in integration tests
public partial class Program { }
