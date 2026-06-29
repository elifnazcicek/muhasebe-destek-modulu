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
    }
}
