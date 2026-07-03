using System;

namespace ReceiptOCR.API.Models
{
    public class Dekont
    {
        public int Id { get; set; }
        public string? HesapNo { get; set; }
        public DateTime Tarih { get; set; }
        public string? DekontNo { get; set; }
        public string KarsiTaraf { get; set; } = string.Empty;
        public decimal Tutar { get; set; }
        public decimal Masraf { get; set; }
        public string? Aciklama { get; set; }
        public string KaydedenKullanici { get; set; } = string.Empty;
        public string? ImagePath { get; set; }
        public DateTime CreatedDate { get; set; } = DateTime.Now;
        public string? FaturaTipi { get; set; } // "Alis" veya "Satis"
    }
}
