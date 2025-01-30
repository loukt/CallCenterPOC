using CallCenterCoreAPI.Services;
using Microsoft.AspNetCore.Mvc;

namespace CallCenterCoreAPI.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class CallController : ControllerBase
    {
        private readonly CallService _callService;

        public CallController(CallService callService)
        {
            _callService = callService;
        }

        [HttpPost("initiate")]
        public async Task<IActionResult> InitiateCall([FromBody] string phoneNumber)
        {
            try
            {
                var callId = await _callService.InitiateCall(phoneNumber, HttpContext);
                return Ok(new { CallId = callId });
            }
            catch (Exception ex)
            {
                return BadRequest(new { Error = ex.Message });
            }
        }
    }


}
