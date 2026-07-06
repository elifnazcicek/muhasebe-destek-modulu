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

        [JsonPropertyName("satici_unvan")]
        public string? SaticiUnvan { get; set; }

        [JsonPropertyName("satici_vkn")]
        public string? SaticiVkn { get; set; }

        [JsonPropertyName("alici_unvan")]
        public string? AliciUnvan { get; set; }

        [JsonPropertyName("alici_vkn")]
        public string? AliciVkn { get; set; }

        [JsonPropertyName("fatura_satirlari")]
        public List<ExtractedInvoiceLine>? FaturaSatirlari { get; set; }
    }

    public class ExtractedInvoiceLine
    {
        [JsonPropertyName("malzeme_hizmet_kodu")]
        public string? MalzemeHizmetKodu { get; set; }

        [JsonPropertyName("malzeme_hizmet_adi")]
        public string? MalzemeHizmetAdi { get; set; }

        [JsonPropertyName("miktar")]
        public double Miktar { get; set; } = 1;

        [JsonPropertyName("birim_fiyat")]
        public decimal BirimFiyat { get; set; }

        [JsonPropertyName("kdv_orani")]
        public double KdvOrani { get; set; }

        [JsonPropertyName("iskonto")]
        public decimal Iskonto { get; set; } = 0;

        [JsonPropertyName("kdv_tutari")]
        public decimal KdvTutari { get; set; }

        [JsonPropertyName("gross_total")]
        public decimal GrossTotal { get; set; }

        [JsonPropertyName("net_tutar")]
        public decimal NetTutar { get; set; }

        [JsonPropertyName("vergiler_dahil_toplam")]
        public decimal VergilerDahilToplam { get; set; }
    }
}
