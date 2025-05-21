using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json;

public class IndexModel : PageModel
{
    


    [IgnoreAntiforgeryToken]
    public async Task<JsonResult> OnPostInitiateCallAsync()
    {
        using var reader = new StreamReader(Request.Body);
        var body = await reader.ReadToEndAsync();
        var input = JsonConvert.DeserializeObject<CallRequest>(body);
      //  public List<CallEntry> Entries { get; set; }
        // = new() { new CallEntry() { PhoneNumber = "+6597507515", Name = "Yassine", NumericValue = 500, Date = DateTime.Now.AddDays(53) }, new CallEntry() { PhoneNumber = "+6568887174", Name = "Calvin", NumericValue = 1500, Date = DateTime.Now.AddDays(47) }, new CallEntry() };

        var apiUrl = "https://XXXXXX.azurewebsites.net/api/Call/initiate";
        using var http = new HttpClient();
        var content = new StringContent(JsonConvert.SerializeObject(input), Encoding.UTF8, "application/json");
        try
        {
            var response = await http.PostAsync(apiUrl, content);
            return new JsonResult(new { success = response.IsSuccessStatusCode });
        }
        catch
        {
            return new JsonResult(new { success = false });
        }
    }

    public class CallRequest
    {
        public string phoneNumber { get; set; }
        public string prompt { get; set; }
    }
}
