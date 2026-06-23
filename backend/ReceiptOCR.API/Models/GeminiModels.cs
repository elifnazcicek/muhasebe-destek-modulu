using System.Text.Json.Serialization;

namespace ReceiptOCR.API.Models
{
    public class ExtractedReceiptData
    {
        [JsonPropertyName("firma_adi")]
        public string FirmaAdi { get; set; } = string.Empty;

        [JsonPropertyName("tarih")]
        public string Tarih { get; set; } = string.Empty;

        [JsonPropertyName("fis_no")]
        public string FisNo { get; set; } = string.Empty;

        [JsonPropertyName("kdv_orani_yuzde")]
        public int KdvOraniYuzde { get; set; }

        [JsonPropertyName("toplam_tutar")]
        public decimal ToplamTutar { get; set; }
    }
}
