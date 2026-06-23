using System;

namespace ReceiptOCR.API.Models
{
    public class SystemLog
    {
        public int Id { get; set; }
        public DateTime Timestamp { get; set; } = DateTime.UtcNow;
        public string Username { get; set; } = string.Empty;
        public string ActionType { get; set; } = string.Empty;
        public string Status { get; set; } = string.Empty;
        public string? Details { get; set; }
    }
}
