using Azure.Storage.Blobs;
using ContactCenterPOC.Models;
using System.Text.Json;

namespace ContactCenterPOC.Services
{
    public class CampaignService
    {
        private readonly BlobServiceClient _blobServiceClient;
        private readonly IConfiguration _configuration;
        private readonly ILogger<CampaignService> _logger;
        private readonly string _containerName;
        private readonly string _blobName = "campaigns.json";
        private List<Campaign> _campaigns = new();
        private bool _initialized = false;
        private readonly SemaphoreSlim _initLock = new(1, 1);

        public CampaignService(BlobServiceClient blobServiceClient, IConfiguration configuration, ILogger<CampaignService> logger)
        {
            _blobServiceClient = blobServiceClient;
            _configuration = configuration;
            _logger = logger;
            _containerName = configuration["BlobStorage:ContainerName"] ?? "callcenter-data";
        }

        private async Task EnsureInitializedAsync()
        {
            if (_initialized) return;

            await _initLock.WaitAsync();
            try
            {
                if (_initialized) return;

                await LoadCampaignsAsync();
                _initialized = true;
            }
            finally
            {
                _initLock.Release();
            }
        }

        private async Task LoadCampaignsAsync()
        {
            try
            {
                var containerClient = _blobServiceClient.GetBlobContainerClient(_containerName);
                await containerClient.CreateIfNotExistsAsync();
                var blobClient = containerClient.GetBlobClient(_blobName);

                if (await blobClient.ExistsAsync())
                {
                    var response = await blobClient.DownloadContentAsync();
                    var json = response.Value.Content.ToString();
                    _campaigns = JsonSerializer.Deserialize<List<Campaign>>(json, new JsonSerializerOptions
                    {
                        PropertyNameCaseInsensitive = true
                    }) ?? new List<Campaign>();
                    _logger.LogInformation("Loaded {Count} campaigns from Blob Storage", _campaigns.Count);
                }
                else
                {
                    _logger.LogInformation("No campaigns blob found, loading defaults");
                    _campaigns = GetDefaultCampaigns();
                    await SaveCampaignsAsync();
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to load campaigns from Blob Storage, using defaults");
                _campaigns = GetDefaultCampaigns();
            }
        }

        private async Task SaveCampaignsAsync()
        {
            try
            {
                var containerClient = _blobServiceClient.GetBlobContainerClient(_containerName);
                await containerClient.CreateIfNotExistsAsync();
                var blobClient = containerClient.GetBlobClient(_blobName);

                var json = JsonSerializer.Serialize(_campaigns, new JsonSerializerOptions
                {
                    WriteIndented = true,
                    PropertyNamingPolicy = JsonNamingPolicy.CamelCase
                });

                using var stream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(json));
                await blobClient.UploadAsync(stream, overwrite: true);
                _logger.LogInformation("Saved {Count} campaigns to Blob Storage", _campaigns.Count);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to save campaigns to Blob Storage");
            }
        }

        public async Task<List<Campaign>> GetAllAsync()
        {
            await EnsureInitializedAsync();
            return _campaigns.ToList();
        }

        public async Task<Campaign?> GetByIdAsync(string id)
        {
            await EnsureInitializedAsync();
            return _campaigns.FirstOrDefault(c => c.Id == id);
        }

        public async Task<Campaign> CreateAsync(CreateCampaignRequest request)
        {
            await EnsureInitializedAsync();

            // Validate title uniqueness
            if (_campaigns.Any(c => c.Title.Equals(request.Title, StringComparison.OrdinalIgnoreCase)))
            {
                throw new InvalidOperationException($"A campaign with title '{request.Title}' already exists.");
            }

            var campaign = new Campaign
            {
                Id = Guid.NewGuid().ToString(),
                Title = request.Title,
                Description = request.Description,
                AiBehaviorInstructions = request.AiBehaviorInstructions,
                IsDefault = false,
                CreatedAt = DateTimeOffset.UtcNow
            };

            _campaigns.Add(campaign);
            await SaveCampaignsAsync();

            _logger.LogInformation("Created campaign '{Title}' with ID {Id}", campaign.Title, campaign.Id);
            return campaign;
        }

        private static List<Campaign> GetDefaultCampaigns()
        {
            return new List<Campaign>
            {
                new Campaign
                {
                    Id = Guid.NewGuid().ToString(),
                    Title = "Bank Loan Collection",
                    Description = "Professional loan collections with flexible repayment plan negotiation",
                    AiBehaviorInstructions = "You are a professional loan collections agent calling about an overdue loan payment. Be firm but empathetic. Reference the outstanding balance, ask about the customer's financial situation, offer flexible repayment plan options (weekly, bi-weekly, monthly installments), negotiate a realistic payment date, and record any payment commitments. If the customer is hostile, remain calm and professional. Always provide a callback number and reference number before ending the call.",
                    IsDefault = true,
                    CreatedAt = DateTimeOffset.UtcNow
                },
                new Campaign
                {
                    Id = Guid.NewGuid().ToString(),
                    Title = "New Product Marketing",
                    Description = "Introduce new products to potential customers with personalized outreach",
                    AiBehaviorInstructions = "You are an enthusiastic product marketing specialist introducing a new product to potential customers. Open with a personalized greeting, briefly explain why you're calling, and highlight 3 key benefits of the new product. Answer questions about pricing, features, and availability. Gauge the customer's interest level. If interested, offer to schedule a product demo or send a detailed brochure. If not interested, thank them politely and ask if they'd like to be removed from future calls.",
                    IsDefault = true,
                    CreatedAt = DateTimeOffset.UtcNow
                },
                new Campaign
                {
                    Id = Guid.NewGuid().ToString(),
                    Title = "Customer Satisfaction Survey",
                    Description = "Post-service satisfaction survey with structured 1-5 rating questions",
                    AiBehaviorInstructions = "You are a friendly survey agent conducting a post-service customer satisfaction survey. Thank the customer for their recent interaction with our company. Ask 5 structured questions using a 1-5 rating scale: overall satisfaction, service quality, response time, staff professionalism, and likelihood to recommend. After each rating, ask for brief feedback. Summarize their responses at the end, thank them for their time, and let them know their feedback helps improve our services.",
                    IsDefault = true,
                    CreatedAt = DateTimeOffset.UtcNow
                },
                new Campaign
                {
                    Id = Guid.NewGuid().ToString(),
                    Title = "Appointment Reminder",
                    Description = "Remind customers of upcoming appointments with rescheduling options",
                    AiBehaviorInstructions = "You are a helpful appointment reminder agent. Inform the customer of their upcoming appointment including the date, time, and location. Confirm whether they can still attend. If they need to reschedule, offer 2-3 alternative time slots. Provide any preparation instructions (e.g., bring ID, arrive 15 minutes early, fast for 12 hours). Send a verbal confirmation summary of the final appointment details before ending the call.",
                    IsDefault = true,
                    CreatedAt = DateTimeOffset.UtcNow
                },
                new Campaign
                {
                    Id = Guid.NewGuid().ToString(),
                    Title = "Insurance Policy Renewal",
                    Description = "Contact customers about expiring insurance policies with renewal options",
                    AiBehaviorInstructions = "You are a knowledgeable insurance renewal specialist contacting a customer about their expiring policy. Review their current coverage details, explain what happens if the policy lapses, present renewal options including any premium changes, highlight new coverage enhancements available this term, answer questions about deductibles and coverage limits, and help initiate the renewal process. If the customer wants to compare options, offer to schedule a detailed consultation with an underwriter.",
                    IsDefault = true,
                    CreatedAt = DateTimeOffset.UtcNow
                },
                new Campaign
                {
                    Id = Guid.NewGuid().ToString(),
                    Title = "Subscription Renewal & Upsell",
                    Description = "Follow up on expiring subscriptions with renewal and premium tier upsell",
                    AiBehaviorInstructions = "You are a customer success agent following up on an expiring subscription. Start by confirming the customer's satisfaction with the current service. Present the renewal pricing and any loyalty discounts available. Introduce the premium tier features: priority support, advanced analytics, increased limits, and exclusive content. Compare the value proposition of standard vs. premium plans. Process the renewal decision on the call. If the customer needs time to decide, schedule a follow-up call within 48 hours.",
                    IsDefault = true,
                    CreatedAt = DateTimeOffset.UtcNow
                }
            };
        }
    }
}
