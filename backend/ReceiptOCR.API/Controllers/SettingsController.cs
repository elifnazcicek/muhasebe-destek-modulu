using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ReceiptOCR.API.Data;
using ReceiptOCR.API.Models;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace ReceiptOCR.API.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    [Authorize] // Sadece giriş yapan kullanıcılar erişebilir
    public class SettingsController : ControllerBase
    {
        private readonly ReceiptDbContext _context;

        public SettingsController(ReceiptDbContext context)
        {
            _context = context;
        }

        [HttpGet]
        public async Task<IActionResult> GetSettings()
        {
            try
            {
                var geminiKeySetting = await _context.Settings.FindAsync("GeminiApiKey");
                var excelPathSetting = await _context.Settings.FindAsync("ExcelPath");
                var vatRatesSetting = await _context.Settings.FindAsync("DefaultVatRates");
                var logRetentionSetting = await _context.Settings.FindAsync("LogRetentionDays");

                var geminiKey = geminiKeySetting?.Value ?? "";
                var excelPath = excelPathSetting?.Value ?? @"C:\Muhasebe\Masraflar.xlsx";
                var vatRatesStr = vatRatesSetting?.Value ?? "20,10,1";
                var logDaysStr = logRetentionSetting?.Value ?? "365";

                var vatRates = vatRatesStr.Split(',').Select(int.Parse).ToList();
                int logDays = int.TryParse(logDaysStr, out var parsedDays) ? parsedDays : 365;

                var settings = new SystemSettings
                {
                    GeminiApiKey = geminiKey,
                    ExcelExportPath = excelPath,
                    DefaultVatRates = vatRates,
                    LogRetentionDays = logDays
                };

                return Ok(new { success = true, data = settings });
            }
            catch (System.Exception ex)
            {
                return StatusCode(500, new { success = false, message = "Ayarlar veritabanından okunurken hata oluştu: " + ex.Message });
            }
        }

        [HttpPut]
        public async Task<IActionResult> UpdateSettings([FromBody] SystemSettings newSettings)
        {
            try
            {
                var geminiKey = await _context.Settings.FindAsync("GeminiApiKey") ?? new Setting { Key = "GeminiApiKey" };
                geminiKey.Value = newSettings.GeminiApiKey;
                _context.Settings.Update(geminiKey);

                var excelPath = await _context.Settings.FindAsync("ExcelPath") ?? new Setting { Key = "ExcelPath" };
                excelPath.Value = newSettings.ExcelExportPath;
                _context.Settings.Update(excelPath);

                var vatRates = await _context.Settings.FindAsync("DefaultVatRates") ?? new Setting { Key = "DefaultVatRates" };
                vatRates.Value = string.Join(",", newSettings.DefaultVatRates);
                _context.Settings.Update(vatRates);

                var logDays = await _context.Settings.FindAsync("LogRetentionDays") ?? new Setting { Key = "LogRetentionDays" };
                logDays.Value = newSettings.LogRetentionDays.ToString();
                _context.Settings.Update(logDays);

                await _context.SaveChangesAsync();

                return Ok(new { success = true, data = newSettings, message = "Ayarlar veritabanına başarıyla kaydedildi." });
            }
            catch (System.Exception ex)
            {
                return StatusCode(500, new { success = false, message = "Ayarlar kaydedilirken hata oluştu: " + ex.Message });
            }
        }
    }
}
