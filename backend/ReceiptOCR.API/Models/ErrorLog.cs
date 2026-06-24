using System;

namespace ReceiptOCR.API.Models
{
    public class ErrorLog
    {
        public int Id { get; set; }
        public DateTime Timestamp { get; set; } = DateTime.Now;
        public string? Username { get; set; }
        public string ActionType { get; set; } = string.Empty;
        public string ErrorMessage { get; set; } = string.Empty;
        public string? StackTrace { get; set; }
    }
}
