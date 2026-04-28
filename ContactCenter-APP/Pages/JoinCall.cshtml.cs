using Microsoft.AspNetCore.Mvc.RazorPages;

namespace CallCenterPOC_App.Pages
{
    public class JoinCallModel : PageModel
    {
        private readonly IConfiguration _configuration;

        public string ApiBaseUrl => _configuration["ApiBaseUrl"] ?? "";
        public string SessionId { get; set; } = "";

        public JoinCallModel(IConfiguration configuration)
        {
            _configuration = configuration;
        }

        public void OnGet(string sessionId)
        {
            SessionId = sessionId ?? "";
        }
    }
}
