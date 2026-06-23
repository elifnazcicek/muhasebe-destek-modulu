using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ReceiptOCR.API.Models;

namespace ReceiptOCR.API.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    [Authorize] // Sadece giriş yapan kullanıcılar erişebilir
    public class SettingsController : ControllerBase
    {
        // In-memory mock veri (DB ekibi bunu Entity Framework'e bağlayacak)
        private static SystemSettings _settings = new SystemSettings
        {
            GeminiApiKey = "AIzaSy_mock_key_for_now",
            ExcelExportPath = "C:\\Muhasebe\\Masraflar.xlsx",
            DefaultVatRates = new List<int> { 1, 10, 20 },
            LogRetentionDays = 30
        };

        [HttpGet]
        public IActionResult GetSettings()
        {
            return Ok(new { success = true, data = _settings });
        }

        [HttpPut]
        public IActionResult UpdateSettings([FromBody] SystemSettings newSettings)
        {
            _settings.GeminiApiKey = newSettings.GeminiApiKey;
            _settings.ExcelExportPath = newSettings.ExcelExportPath;
            _settings.DefaultVatRates = newSettings.DefaultVatRates;
            _settings.LogRetentionDays = newSettings.LogRetentionDays;

            return Ok(new { success = true, data = _settings, message = "Ayarlar başarıyla güncellendi." });
        }
    }
}
