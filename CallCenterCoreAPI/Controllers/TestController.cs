﻿using Azure.AI.OpenAI;
using CallCenterCoreAPI.Models;
using CallCenterCoreAPI.Services;
using Microsoft.AspNetCore.Mvc;
using OpenAI.RealtimeConversation;
using System.Diagnostics.CodeAnalysis;

namespace CallCenterCoreAPI.Controllers
{

    [ApiController]
    [Route("api/[controller]")]
    public class TestController : ControllerBase
    {
        private readonly CallService _callService;
        private readonly ILogger<TestController> _logger;

        public TestController(
            CallService callService,
            ILogger<TestController> logger)
        {
            _callService = callService;
            _logger = logger;
        }

        [HttpPost("test-call")]
        public async Task<IActionResult> TestCall([FromBody] string phoneNumber)
        {
            
            //try
            //{
                // Initiate test call
                var callId = await _callService.InitiateCall(phoneNumber,HttpContext);
            await _callService.StartCallInteraction(callId, HttpContext);

            return Ok(new { CallId=callId });
                // Start realtime conversation
               //await _callService.StartCallInteraction(callId);

             /*   return Ok(new
                {
                    CallId = callId,
                    Status = "Call initiated successfully",
                    Timestamp = DateTime.UtcNow
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during test call");
                return StatusCode(500, new
                {
                    Error = "Failed to initiate test call",
                    Message = ex.Message
                });
            }*/
        }
    }


}
