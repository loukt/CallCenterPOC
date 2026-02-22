using Azure.Identity;
using Azure.Storage.Blobs;
using ContactCenterPOC.Hubs;
using ContactCenterPOC.Services;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddSingleton<CallService>();

// Register BlobServiceClient for campaign + call history persistence
var blobConnectionString = builder.Configuration["BlobStorage:ConnectionString"];
var blobAccountUri = builder.Configuration["BlobStorage:AccountUri"];
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
builder.Services.AddSingleton<CallHistoryService>();
builder.Services.AddControllers();
// Learn more about configuring Swagger/OpenAPI at https://aka.ms/aspnetcore/swashbuckle
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
builder.Services.AddSignalR();

// CORS policy for frontend SignalR connections
var frontendOrigin = builder.Configuration["FrontendOrigin"] ?? "https://localhost:5002";
builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowFrontend", policy =>
    {
        policy.WithOrigins(frontendOrigin)
              .AllowAnyHeader()
              .AllowAnyMethod()
              .AllowCredentials();
    });
});

var app = builder.Build();

// Configure the HTTP request pipeline.
//if (app.Environment.IsDevelopment())
//{
    app.UseSwagger();
    app.UseSwaggerUI();
//}

app.UseWebSockets();

app.UseStaticFiles();

app.UseHttpsRedirection();

app.UseCors("AllowFrontend");

app.UseAuthorization();

app.MapControllers();
app.MapHub<TranscriptHub>("/transcriptHub");

app.Run();

// Required for WebApplicationFactory<Program> in integration tests
public partial class Program { }
