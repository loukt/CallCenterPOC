using Microsoft.AspNetCore.Mvc.RazorPages;

namespace CallCenterPOC_App.Pages
{
    public class IndexModel : PageModel
    {
        private readonly ILogger<IndexModel> _logger;
        private readonly IConfiguration _configuration;

        public string ApiBaseUrl => _configuration["ApiBaseUrl"] ?? "";

        public IndexModel(ILogger<IndexModel> logger, IConfiguration configuration)
        {
            _logger = logger;
            _configuration = configuration;
        }

        public void OnGet()
        {
        }
    }
}
