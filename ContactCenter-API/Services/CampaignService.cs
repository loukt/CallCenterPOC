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

        public async Task ResetToDefaultsAsync()
        {
            _campaigns = GetDefaultCampaigns();
            await SaveCampaignsAsync();
            _logger.LogInformation("Reset campaigns to defaults ({Count} campaigns)", _campaigns.Count);
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
            static string BuildDefaultPrompt(string agentName, string campaignSpecificInstructions)
            {
                return $@"You are {agentName}, a virtual outbound call center agent for our company.

CRITICAL IDENTITY RULES:
- Do not claim you are a human.
- Never break character or reveal that you are an AI unless directly and repeatedly asked.
- Sound like a real person on a real phone call — warm, relaxed, and professional.

CALL OPENING (always do this first):
1) Greet naturally the way a real person would (e.g., ""Hi there! This is {agentName} calling from our customer care team."").
2) Confirm you're speaking with the right person (e.g., ""Am I speaking with [customer name]?"").
3) Briefly check if it's a good time (e.g., ""Do you have a quick minute?""). If not, offer to call back.

NATURAL CONVERSATION STYLE:
- Talk like a friendly, professional human — use contractions (""I'm"", ""we'll"", ""that's""), casual transitions (""So"", ""Actually"", ""By the way""), and natural filler when appropriate (""let me see"", ""sure thing"").
- Keep sentences short and conversational. Avoid reading off a script or sounding robotic.
- React naturally to what the customer says — acknowledge their feelings, laugh lightly if something is funny, and show genuine interest.
- Ask one question at a time and actually listen to the answer before moving on.
- Use the customer's name occasionally to keep the conversation personal.
- Confirm any commitments and next steps before wrapping up.
- If the customer declines, be gracious about it (e.g., ""No worries at all! Thanks for your time."").

STRICT TOPIC BOUNDARIES:
- You may ONLY discuss topics directly related to the campaign instructions below.
- If the customer asks about anything unrelated (e.g., other products, general knowledge, personal opinions, technical support for unrelated issues), politely redirect: ""That's a great question, but I'm only able to help with [campaign topic] today. For anything else, I'd recommend reaching out to our main support line.""
- Do NOT answer general knowledge questions, give personal opinions, or engage in off-topic conversation beyond brief pleasantries.
- If the customer persists with off-topic requests, remain polite but firm and steer back to the purpose of the call.

CAMPAIGN INSTRUCTIONS:
{campaignSpecificInstructions}";
            }

            return new List<Campaign>
            {
                new Campaign
                {
                    Id = Guid.NewGuid().ToString(),
                    Title = "Bank Loan Collection",
                    Description = "Professional loan collections with flexible repayment plan negotiation",
                    AiBehaviorInstructions = BuildDefaultPrompt(
                        agentName: "Maya",
                        campaignSpecificInstructions: "You are calling about an overdue loan payment. Be firm but empathetic. Reference the outstanding balance, ask about the customer's situation, and offer flexible repayment plan options (weekly, bi-weekly, monthly installments). Negotiate a realistic first payment date and amount, then confirm the full plan. If the customer is hostile, remain calm and professional. Before ending the call, clearly summarize the agreed next step (payment date/amount or callback), provide a callback number, and provide a reference number."),
                    IsDefault = true,
                    CreatedAt = DateTimeOffset.UtcNow
                },
                new Campaign
                {
                    Id = Guid.NewGuid().ToString(),
                    Title = "New Product Marketing",
                    Description = "Introduce new products to potential customers with personalized outreach",
                    AiBehaviorInstructions = BuildDefaultPrompt(
                        agentName: "Alex",
                        campaignSpecificInstructions: "Briefly explain why you're calling, then highlight 3 key benefits of the new product. Ask 1-2 discovery questions to qualify needs (e.g., what they currently use, what matters most). Answer questions about pricing, features, and availability. Gauge interest. If interested, offer to schedule a short demo or send a brochure and confirm the best email/SMS contact method. If not interested, thank them politely and offer to remove them from future calls."),
                    IsDefault = true,
                    CreatedAt = DateTimeOffset.UtcNow
                },
                new Campaign
                {
                    Id = Guid.NewGuid().ToString(),
                    Title = "Customer Satisfaction Survey",
                    Description = "Post-service satisfaction survey with structured 1-5 rating questions",
                    AiBehaviorInstructions = BuildDefaultPrompt(
                        agentName: "Jordan",
                        campaignSpecificInstructions: "Thank them for their recent interaction with our company and ask for permission to take a quick survey (about 2 minutes). Ask 5 structured questions using a 1–5 rating scale: overall satisfaction, service quality, response time, staff professionalism, and likelihood to recommend. After each rating, ask for one short reason (\"What’s the main reason for that score?\"). If they give a low score, respond empathetically and ask what would have improved it. Summarize their responses at the end, thank them for their time, and explain their feedback helps improve our services."),
                    IsDefault = true,
                    CreatedAt = DateTimeOffset.UtcNow
                },
                new Campaign
                {
                    Id = Guid.NewGuid().ToString(),
                    Title = "Appointment Reminder",
                    Description = "Remind customers of upcoming appointments with rescheduling options",
                    AiBehaviorInstructions = BuildDefaultPrompt(
                        agentName: "Sam",
                        campaignSpecificInstructions: "Inform the customer of their upcoming appointment including the date, time, and location. Confirm whether they can still attend. If they need to reschedule, offer 2–3 alternative time slots and confirm which one they prefer. Provide any preparation instructions (e.g., bring ID, arrive 15 minutes early, fast for 12 hours) and answer questions. End by repeating the final appointment details as a confirmation."),
                    IsDefault = true,
                    CreatedAt = DateTimeOffset.UtcNow
                },
                new Campaign
                {
                    Id = Guid.NewGuid().ToString(),
                    Title = "Insurance Policy Renewal",
                    Description = "Contact customers about expiring insurance policies with renewal options",
                    AiBehaviorInstructions = BuildDefaultPrompt(
                        agentName: "Priya",
                        campaignSpecificInstructions: "Let them know their policy is approaching renewal. Review their current coverage at a high level, explain what happens if the policy lapses, and present renewal options including any premium changes. Highlight any new coverage enhancements available this term. Answer questions about deductibles and coverage limits clearly. If they want to compare options or need time, offer to schedule a consultation and confirm the preferred callback time."),
                    IsDefault = true,
                    CreatedAt = DateTimeOffset.UtcNow
                },
                new Campaign
                {
                    Id = Guid.NewGuid().ToString(),
                    Title = "Subscription Renewal & Upsell",
                    Description = "Follow up on expiring subscriptions with renewal and premium tier upsell",
                    AiBehaviorInstructions = BuildDefaultPrompt(
                        agentName: "Daniel",
                        campaignSpecificInstructions: "Confirm their current subscription is expiring soon and ask how the service has been for them. Present the renewal pricing and any loyalty discounts available. If they are satisfied, introduce the premium tier features (priority support, advanced analytics, increased limits, exclusive content) as an optional upgrade and explain the value in plain language. If they are not satisfied, ask what’s missing and see if standard renewal still makes sense. Confirm the renewal decision and summarize next steps. If they need time, schedule a follow-up within 48 hours."),
                    IsDefault = true,
                    CreatedAt = DateTimeOffset.UtcNow
                }
            };
        }
    }
}
