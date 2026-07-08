using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using ReceiptOCR.API.Models;
using Serilog;

namespace ReceiptOCR.API.Services
{
    public class GeminiService
    {
        private readonly HttpClient _httpClient;
        private readonly IConfiguration _configuration;

        public GeminiService(HttpClient httpClient, IConfiguration configuration)
        {
            _httpClient = httpClient;
            _configuration = configuration;
        }

        public async Task<ExtractedReceiptData?> ScanReceiptAsync(byte[] fileBytes, string mimeType = "image/jpeg")
        {
            var apiKey = _configuration["Gemini:ApiKey"];
            var modelName = _configuration["Gemini:ModelName"] ?? "gemini-1.5-flash";
            
            if (string.IsNullOrEmpty(apiKey))
            {
                Log.Error("Gemini API Key bulunamadı!");
                throw new Exception("Gemini API Key eksik.");
            }

            var url = $"https://generativelanguage.googleapis.com/v1beta/models/{modelName}:generateContent?key={apiKey}";
            
            var base64File = Convert.ToBase64String(fileBytes);

            var systemPrompt = @"Sen profesyonel bir muhasebe veri giriş asistanısın. Görevin, sana gönderilen fiş veya fatura görsellerini/belgelerini analiz etmek ve bilgileri sadece belirtilen JSON formatında dönmektir. JSON dışında hiçbir açıklama veya markdown işareti yazma.
Bu fiş veya fatura belgesini analiz et ve aşağıdaki bilgileri Türkçe karakter kurallarına uyarak çıkar:
1. firma_adi: Fişi veya faturayı düzenleyen işletmenin adı. ÖNEMLİ: İşletme adını sadece A.Ş., Anonim Şirketi, Ltd. Şti., Limited Şirketi, Şti gibi şirket türünü belirten ibareye kadar temiz şekilde al. Sonrasındaki adres, şube, telefon veya vergi dairesi gibi ekleri dahil etme. (Örn: 'MİGROS TİCARET A.Ş. ANKARA ŞUBESİ' yerine 'MİGROS TİCARET A.Ş.').
2. vkn_tckn: Fişi/faturayı düzenleyen firmanın 10 haneli Vergi Kimlik Numarası (VKN) veya 11 haneli T.C. Kimlik Numarası (TCKN). Bulamazsan boş bırak.
3. tarih: GG.AA.YYYY formatında tarih.
4. fis_no: Fiş veya fatura numarası. (Fis No veya Fatura No ibaresinin yanındaki numara).
5. kdv_orani_yuzde: Fişte uygulanan en yüksek KDV oranı (Sadece sayı, örn: 20).
6. toplam_tutar: Fişin en altındaki genel toplam tutar (Sadece sayı, örn: 150.50).
7. kdv_detaylari: Fişin en altında (genellikle TOPKDV veya TOPLAM satırlarının altında) KDV oranlarına göre KDV tutarları, matrahlar ve toplamların ayrı ayrı döküldüğü satırlar varsa (Örn: '%1 *3.044,52 *30,45 *3.074,97' veya '%20 *519,04 *103,81 *622,85' gibi satırlar), bu satırlardaki değerleri tam olarak oku ve listele. Her satır için:
   - kdv_orani: KDV yüzdesi (Sadece sayı, örn: 1, 10, 20)
   - matrah: KDV hariç matrah tutarı (Sadece sayı, örn: 3044.52)
   - kdv_tutari: O KDV oranının tutarı (Sadece sayı, örn: 30.45)
   - toplam_tutar: O KDV oranının dahil olduğu toplam tutar (Sadece sayı, örn: 3074.97)
   Eğer fişte bu detaylı KDV döküm satırları yoksa, 'kdv_detaylari' alanını null veya boş liste olarak dön.

JSON Şeması:
{
""firma_adi"": ""Temiz Firma Adı"",
""vkn_tckn"": ""1234567890"",
""tarih"": ""GG.AA.YYYY"",
""fis_no"": ""Fiş No"",
""kdv_orani_yuzde"": 20,
""toplam_tutar"": 150.50,
""kdv_detaylari"": [
  {
    ""kdv_orani"": 1,
    ""matrah"": 3044.52,
    ""kdv_tutari"": 30.45,
    ""toplam_tutar"": 3074.97
  }
]
}";

            var payload = new
            {
                contents = new[]
                {
                    new
                    {
                        parts = new object[]
                        {
                            new { text = systemPrompt },
                            new 
                            { 
                                inline_data = new 
                                {
                                    mime_type = mimeType,
                                    data = base64File
                                }
                            }
                        }
                    }
                },
                generationConfig = new
                {
                    responseMimeType = "application/json"
                }
            };

            var jsonPayload = JsonSerializer.Serialize(payload);
            var content = new StringContent(jsonPayload, Encoding.UTF8, "application/json");

            Log.Information("Gemini API'sine istek gönderiliyor...");
            var response = await _httpClient.PostAsync(url, content);
            
            var responseString = await response.Content.ReadAsStringAsync();

            if (!response.IsSuccessStatusCode)
            {
                Log.Error("Gemini API Hatası: {StatusCode} - {Response}", response.StatusCode, responseString);
                throw new Exception("Gemini API OCR işlemi başarısız oldu.");
            }

            // Gemini yanıtını parse et
            try
            {
                var doc = JsonDocument.Parse(responseString);
                var textResponse = doc.RootElement
                    .GetProperty("candidates")[0]
                    .GetProperty("content")
                    .GetProperty("parts")[0]
                    .GetProperty("text").GetString();

                // Markdown kod bloklarını temizle (eğer Gemini inatla markdown dönerse)
                textResponse = textResponse?.Replace("```json", "").Replace("```", "").Trim();

                if (string.IsNullOrEmpty(textResponse)) return null;

                var result = JsonSerializer.Deserialize<ExtractedReceiptData>(textResponse);
                return result;
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Gemini yanıtı parse edilemedi. Gelen veri: {Response}", responseString);
                throw new Exception("Fiş verisi okunamadı veya parse edilemedi.");
            }
        }

        public async Task<List<ExtractedDekontData>?> ScanDekontAsync(byte[] fileBytes, string mimeType = "image/jpeg")
        {
            var apiKey = _configuration["Gemini:ApiKey"];
            var modelName = _configuration["Gemini:ModelName"] ?? "gemini-1.5-flash";
            
            if (string.IsNullOrEmpty(apiKey))
            {
                Log.Error("Gemini API Key bulunamadı!");
                throw new Exception("Gemini API Key eksik.");
            }

            var url = $"https://generativelanguage.googleapis.com/v1beta/models/{modelName}:generateContent?key={apiKey}";
            
            var base64File = Convert.ToBase64String(fileBytes);

            var systemPrompt = @"Sen profesyonel bir muhasebe ve bankacılık veri giriş asistanısın. Görevin, sana gönderilen banka dekontu, transfer makbuzu veya fatura (e-Fatura, e-Arşiv vb.) görsellerini/belgelerini analiz etmek ve bilgileri sadece belirtilen JSON formatında bir JSON dizisi (Array) olarak dönmektir. Belgede sadece tek bir fatura veya dekont bulunsa dahi, bunu mutlaka tek elemanlı bir JSON dizisi [ { ... } ] şeklinde sararak döndür. JSON dışında hiçbir açıklama veya markdown işareti yazma.
Bu belgeyi analiz et ve bulduğun tüm fatura/dekont belgeleri için aşağıdaki bilgileri Türkçe karakter kurallarına uyarak çıkar:
1. hesap_no: Banka dekontu ise, işlem gören hesap numarası veya IBAN numarası. Fatura ise boş bırak.
2. tarih: GG.AA.YYYY formatında işlem tarihi veya fatura tarihi.
3. dekont_no: Banka dekontu ise dekont/işlem numarası. Fatura ise fatura numarası (Örn: GIB2026000000123).
4. satici_unvan: Faturayı düzenleyen (fatura kesen / Supplier) firmanın adı/unvanı. Şirket türüne (A.Ş., Ltd. Şti.) kadar temiz şekilde al. Banka dekontu ise banka adını veya gönderen adı yaz.
5. satici_vkn: Faturayı düzenleyen (kesen) firmanın 10 haneli VKN veya 11 haneli TCKN'si.
6. alici_unvan: Fatura kesilen (fatura alıcısı / Customer) firmanın adı/unvanı. Şirket türüne kadar temiz şekilde al. Banka dekontu ise alıcı adı yaz.
7. alici_vkn: Fatura kesilen (alıcı) firmanın 10 haneli VKN veya 11 haneli TCKN'si.
8. karsi_taraf: Genel karşı taraf adı/unvanı (Bizim şirket dışındaki tarafın adı).
9. tutar: Faturanın genel toplam tutarı veya transfer tutarı.
10. masraf: Banka komisyonu veya masrafı (Faturada KDV dahil toplam masrafı veya 0.00 yaz).
11. aciklama: Dekont açıklaması veya fatura açıklaması.
12. fatura_satirlari: Faturadaki tüm ürün veya hizmet kalemlerini ayrı ayrı liste halinde çıkar. Banka dekontu ise tek bir hizmet kalemi olarak transfer bedelini ekle. Her bir kalemde şu bilgiler bulunmalıdır:
   - malzeme_hizmet_kodu: Kalemin kodu (varsa stok kodu veya hizmet kodu, yoksa HER ZAMAN bos dize olarak "" yaz).
   - malzeme_hizmet_adi: Kalemin adı veya açıklaması.
   - miktar: Kalemin miktarı (varsa miktar, yoksa 1).
   - birim_fiyat: Kalemin KDV hariç birim fiyatı.
   - kdv_orani: Kalemin KDV oranı (yüzde cinsinden örn: 20, 10, 1, 0).
   - iskonto: Kaleme uygulanan iskonto tutarı (yoksa 0).
   - kdv_tutari: Kalemin KDV tutarı.
   - net_tutar: Kalemin KDV hariç net tutarı (Miktar * Birim Fiyat - İskonto).
   - vergiler_dahil_toplam: Kalemin KDV dahil toplam tutarı (Net Tutar + KDV Tutarı).

JSON Dizi Şeması:
[
  {
    ""hesap_no"": ""TR000000000000000000000000"",
    ""tarih"": ""GG.AA.YYYY"",
    ""dekont_no"": ""Fatura No veya Dekont No"",
    ""satici_unvan"": ""Satıcı Firma Adı A.Ş."",
    ""satici_vkn"": ""1234567890"",
    ""alici_unvan"": ""Alıcı Firma Adı Ltd. Şti."",
    ""alici_vkn"": ""0987654321"",
    ""karsi_taraf"": ""Karşı Tarafın Adı"",
    ""tutar"": 1500.00,
    ""masraf"": 0.00,
    ""aciklama"": ""İşlem Açıklaması"",
    ""fatura_satirlari"": [
      {
        ""malzeme_hizmet_kodu"": """",
        ""malzeme_hizmet_adi"": ""Ürün/Hizmet Adı"",
        ""miktar"": 1.0,
        ""birim_fiyat"": 1250.00,
        ""kdv_orani"": 20.0,
        ""iskonto"": 0.0,
        ""kdv_tutari"": 250.00,
        ""net_tutar"": 1250.00,
        ""vergiler_dahil_toplam"": 1500.00
      }
    ]
  }
]";

            var payload = new
            {
                contents = new[]
                {
                    new
                    {
                        parts = new object[]
                        {
                            new { text = systemPrompt },
                            new 
                            { 
                                inline_data = new 
                                
                                {
                                    mime_type = mimeType,
                                    data = base64File
                                }
                            }
                        }
                    }
                },
                generationConfig = new
                {
                    responseMimeType = "application/json"
                }
            };

            var jsonPayload = JsonSerializer.Serialize(payload);
            var content = new StringContent(jsonPayload, Encoding.UTF8, "application/json");

            Log.Information("Gemini API'sine dekont tarama isteği gönderiliyor...");
            var response = await _httpClient.PostAsync(url, content);
            
            var responseString = await response.Content.ReadAsStringAsync();

            if (!response.IsSuccessStatusCode)
            {
                Log.Error("Gemini API Hatası (Dekont): {StatusCode} - {Response}", response.StatusCode, responseString);
                throw new Exception("Gemini API dekont tarama işlemi başarısız oldu.");
            }

            try
            {
                var doc = JsonDocument.Parse(responseString);
                var textResponse = doc.RootElement
                    .GetProperty("candidates")[0]
                    .GetProperty("content")
                    .GetProperty("parts")[0]
                    .GetProperty("text").GetString();

                textResponse = textResponse?.Replace("```json", "").Replace("```", "").Trim();

                if (string.IsNullOrEmpty(textResponse)) return null;

                List<ExtractedDekontData>? result = null;
                try
                {
                    result = JsonSerializer.Deserialize<List<ExtractedDekontData>>(textResponse);
                }
                catch
                {
                    // Fallback: dizisiz tekli nesne döndüyse onu listeye çevirip kurtaralım
                    var singleObj = JsonSerializer.Deserialize<ExtractedDekontData>(textResponse);
                    if (singleObj != null)
                    {
                        result = new List<ExtractedDekontData> { singleObj };
                    }
                }
                return result;
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Gemini dekont yanıtı parse edilemedi. Gelen veri: {Response}", responseString);
                throw new Exception("Dekont verisi okunamadı veya parse edilemedi.");
            }
        }
    }
}
