using ContactCenterPOC.Models;
using ContactCenterPOC.Services;
using Microsoft.AspNetCore.Mvc;

namespace ContactCenterPOC.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class SettingsController : ControllerBase
    {
        private readonly SettingsService _settingsService;
        private readonly VoiceLiveConfig _voiceLiveConfig;

        public SettingsController(SettingsService settingsService, VoiceLiveConfig voiceLiveConfig)
        {
            _settingsService = settingsService;
            _voiceLiveConfig = voiceLiveConfig;
        }

        [HttpGet]
        public async Task<IActionResult> GetSettings()
        {
            var settings = await _settingsService.GetSettingsAsync();
            var response = new
            {
                settings.MaxCallTimeMinutes,
                settings.VoiceApiMode,
                settings.SelectedVoice,
                settings.TranscriptionMode,
                settings.VoiceLiveModel,
                settings.SelectedVoiceLiveVoice,
                VoiceLiveConfigured = _voiceLiveConfig.IsConfigured,
                AvailableVoiceLiveVoices = VoiceLiveVoices.All,
                AvailableVoiceLiveModels = OperatorSettings.ValidVoiceLiveModels.ToList()
            };
            return Ok(response);
        }

        [HttpPut]
        public async Task<IActionResult> UpdateSettings([FromBody] OperatorSettings settings)
        {
            if (settings == null)
            {
                return BadRequest(new { error = "Invalid settings" });
            }

            var saved = await _settingsService.SaveSettingsAsync(settings);
            var response = new
            {
                saved.MaxCallTimeMinutes,
                saved.VoiceApiMode,
                saved.SelectedVoice,
                saved.TranscriptionMode,
                saved.VoiceLiveModel,
                saved.SelectedVoiceLiveVoice,
                VoiceLiveConfigured = _voiceLiveConfig.IsConfigured,
                AvailableVoiceLiveVoices = VoiceLiveVoices.All,
                AvailableVoiceLiveModels = OperatorSettings.ValidVoiceLiveModels.ToList()
            };
            return Ok(response);
        }
    }
}
