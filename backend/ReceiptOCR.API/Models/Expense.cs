using System;

namespace ReceiptOCR.API.Models
{
    public class Expense
    {
        public int Id { get; set; }
        public DateTime Tarih { get; set; }
        public string FirmaAdi { get; set; } = string.Empty;
        public string? FisNo { get; set; }
        public string? VknTckn { get; set; }
        public decimal KdvTutari { get; set; }
        public decimal ToplamTutar { get; set; }
        public string KaydedenKullanici { get; set; } = string.Empty;
        public DateTime CreatedDate { get; set; } = DateTime.Now;
    }
}
