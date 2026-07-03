using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace ReceiptOCR.API.Models
{
    public class ExtractedVatDetail
    {
        [JsonPropertyName("kdv_orani")]
        public int KdvOrani { get; set; }

        [JsonPropertyName("matrah")]
        public decimal Matrah { get; set; }

        [JsonPropertyName("kdv_tutari")]
        public decimal KdvTutari { get; set; }

        [JsonPropertyName("toplam_tutar")]
        public decimal ToplamTutar { get; set; }
    }

    public class ExtractedReceiptData
    {
        [JsonPropertyName("firma_adi")]
        public string? FirmaAdi { get; set; }

        [JsonPropertyName("vkn_tckn")]
        public string? VknTckn { get; set; }

        [JsonPropertyName("tarih")]
        public string? Tarih { get; set; }

        [JsonPropertyName("fis_no")]
        public string? FisNo { get; set; }

        [JsonPropertyName("kdv_orani_yuzde")]
        public int? KdvOraniYuzde { get; set; }

        [JsonPropertyName("toplam_tutar")]
        public decimal? ToplamTutar { get; set; }

        [JsonPropertyName("kdv_detaylari")]
        public List<ExtractedVatDetail>? KdvDetaylari { get; set; }
    }

    public class ExtractedDekontData
    {
        [JsonPropertyName("hesap_no")]
        public string? HesapNo { get; set; }

        [JsonPropertyName("tarih")]
        public string? Tarih { get; set; }

        [JsonPropertyName("dekont_no")]
        public string? DekontNo { get; set; }

        [JsonPropertyName("karsi_taraf")]
        public string? KarsiTaraf { get; set; }

        [JsonPropertyName("tutar")]
        public decimal? Tutar { get; set; }

        [JsonPropertyName("masraf")]
        public decimal? Masraf { get; set; }

        [JsonPropertyName("aciklama")]
        public string? Aciklama { get; set; }
    }
}
