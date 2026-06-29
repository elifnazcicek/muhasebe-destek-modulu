using System.Collections.Generic;

namespace ReceiptOCR.API.Models
{
    public class SystemSettings
    {
        public string GeminiApiKey { get; set; } = string.Empty;
        public string ExcelExportPath { get; set; } = "C:\\Muhasebe\\Masraflar.xlsx";
        public int LogRetentionDays { get; set; } = 30;
    }
}
