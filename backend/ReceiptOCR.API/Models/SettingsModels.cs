using System.Collections.Generic;

namespace ReceiptOCR.API.Models
{
    public class SystemSettings
    {
        public string GeminiApiKey { get; set; } = string.Empty;
        public string ExcelExportPath { get; set; } = "C:\\Muhasebe\\Masraflar.xlsx";
        public List<int> DefaultVatRates { get; set; } = new List<int> { 1, 10, 20 };
        public int LogRetentionDays { get; set; } = 30;
    }
}
