using System;

namespace ReceiptOCR.API.Models
{
    public class PasswordReset
    {
        public int Id { get; set; }
        public string Username { get; set; } = string.Empty;
        public string Code { get; set; } = string.Empty;
        public DateTime ExpiryTime { get; set; }
        public bool IsUsed { get; set; } = false;
        public DateTime CreatedDate { get; set; } = DateTime.Now;
    }
}
