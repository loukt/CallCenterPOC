using Azure.Identity;
using Azure.Storage.Blobs;
using ContactCenterPOC.Hubs;
using ContactCenterPOC.Models;
using ContactCenterPOC.Services;
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
builder.Services.AddControllers();
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
