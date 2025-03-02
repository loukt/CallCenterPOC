using System.ComponentModel.DataAnnotations;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Reflection.Metadata;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using static System.Net.WebRequestMethods;

namespace CallCenterPOC_App.Pages
{
    public class IndexModel : PageModel
    {
        private readonly ILogger<IndexModel> _logger;
        private readonly IConfiguration _configuration;

        [BindProperty]
        [RegularExpression(@"^\+[1-9]\d{1,14}$", ErrorMessage = "Please enter a valid phone number with country code (e.g., +6591234567)")]
        [Required(ErrorMessage = "Phone number is required")]
        public string PhoneNumber { get; set; }

        [BindProperty]
        public string Prompt { get; set; }
        public List<PromptOption> PromptOptions { get; private set; }
        [BindProperty]
        public bool IsCalling { get; set; }
        //[BindProperty]
        public string StatusMessage { get; set; }



        public IndexModel(ILogger<IndexModel> logger, IConfiguration configuration)
        {
            _logger = logger;
            _configuration = configuration;
            Prompt = "You are a very rude impatient receptionist for a doctor, you are waiting for a patient to speak, to acknowledge this, say a greeting first";
            //StatusMessage = "";
            PromptOptions = new List<PromptOption>
            {
                new PromptOption { Title = "Rude Receptionist", Content = "You are a very rude impatient receptionist for a doctor, you are waiting for a patient to speak, to acknowledge this, say a greeting first" },
                new PromptOption { Title = "Customer Service", Content = "You are a friendly and helpful customer service representative, ready to assist the caller with any inquiries." },
                new PromptOption { Title = "Technical Support Agent", Content = "You are a technical support agent, prepared to help the caller troubleshoot their issues." },
                new PromptOption { Title = "Loan Collector", Content = "You are a loan collections agent, you are calling people to tell them about their credit card debt and ask them when they will be able to pay it." }
            };
        }


        public async Task<IActionResult> OnPostAsync()
        {
            if (!ModelState.IsValid)
            {
                return Page();
            }

            IsCalling = true;

            try
            {
                var client = new HttpClient();
                client.BaseAddress = new Uri(_configuration["ApiBaseUrl"]);
                client.DefaultRequestHeaders.Accept.Clear();
                client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
                client.Timeout = TimeSpan.FromSeconds(30);

                string urltotest = _configuration["ApiBaseUrl"] + "/api/Call/initiate";
                _logger.LogInformation($"Calling API at: {urltotest}");

                var response = await client.PostAsJsonAsync("/api/Call/initiate", new { PhoneNumber, Prompt });

                if (response.IsSuccessStatusCode)
                {
                    TempData["SuccessMessage"] = "Call initiated successfully!";
                    StatusMessage = "Call successful!";
                }
                else
                {
                    TempData["ErrorMessage"] = "Failed to initiate call.";
                    StatusMessage = "Call failed.";
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error initiating call");
                TempData["ErrorMessage"] = "An error occurred while initiating the call.";
                StatusMessage = "An error occurred while initiating the call.";

            }
            finally
            {
                IsCalling = false;
            }

            return Page();
        }
    }
    public class PromptOption
    {
        public string Title { get; set; }
        public string Content { get; set; }
    }
}
