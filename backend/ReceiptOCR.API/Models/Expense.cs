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
        public int KdvOrani { get; set; }
        public decimal Matrah { get; set; }
        public decimal KdvTutari { get; set; }
        public decimal ToplamTutar { get; set; }
        public decimal FisinGenelToplami { get; set; }
        public string KaydedenKullanici { get; set; } = string.Empty;
        public string? ImagePath { get; set; }
        public DateTime CreatedDate { get; set; } = DateTime.Now;
    }
}
