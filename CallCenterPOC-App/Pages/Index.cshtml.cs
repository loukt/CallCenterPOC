using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using static System.Net.WebRequestMethods;

namespace CallCenterPOC_App.Pages
{
    public class IndexModel : PageModel
    {
        private readonly ILogger<IndexModel> _logger;

        private string phoneNumber;
        private string promptBox;
        private bool isCalling;

        public IndexModel(ILogger<IndexModel> logger)
        {
            _logger = logger;
        }

        public void OnGet()
        { }


        private async Task MakeCall()
        {
            isCalling = true;
            try
            {
                var httpClient = new HttpClient();
                var response = await httpClient.PostAsJsonAsync("api/your-api-endpoint", new { phoneNumber, promptBox });
                if (response.IsSuccessStatusCode)
                {
                    // Handle success
                }
                else
                {
                    // Handle failure
                }
            }
            catch (Exception ex)
            {
                // Handle exception
            }
            finally
            {
                isCalling = false;
            }
        }








    }
    
}
