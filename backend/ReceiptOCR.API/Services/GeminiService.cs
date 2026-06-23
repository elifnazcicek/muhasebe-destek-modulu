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

        public async Task<ExtractedReceiptData?> ScanReceiptAsync(byte[] imageBytes)
        {
            var apiKey = _configuration["Gemini:ApiKey"];
            var modelName = _configuration["Gemini:ModelName"] ?? "gemini-1.5-flash";
            
            if (string.IsNullOrEmpty(apiKey))
            {
                Log.Error("Gemini API Key bulunamadı!");
                throw new Exception("Gemini API Key eksik.");
            }

            var url = $"https://generativelanguage.googleapis.com/v1beta/models/{modelName}:generateContent?key={apiKey}";
            
            var base64Image = Convert.ToBase64String(imageBytes);

            var systemPrompt = @"Sen profesyonel bir muhasebe veri giriş asistanısın. Görevin, sana gönderilen fiş veya fatura görsellerini analiz etmek ve bilgileri sadece belirtilen JSON formatında dönmektir. JSON dışında hiçbir açıklama veya markdown işareti yazma.
Bu fiş görselini analiz et ve aşağıdaki bilgileri Türkçe karakter kurallarına uyarak çıkar:
1. firma_adi: Fişi düzenleyen şirketin adı.
2. tarih: GG.AA.YYYY formatında tarih.
3. fis_no: Fiş veya fatura numarası.
4. kdv_orani_yuzde: Fişte uygulanan en yüksek KDV oranı (Sadece sayı, örn: 20).
5. toplam_tutar: Fişin en altındaki genel toplam tutar (Sadece sayı, örn: 150.50).
JSON Şeması:
{
""firma_adi"": ""Firma Adı"",
""tarih"": ""GG.AA.YYYY"",
""fis_no"": ""Fiş No"",
""kdv_orani_yuzde"": 20,
""toplam_tutar"": 150.50
}";
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
                                    mime_type = "image/jpeg",
                                    data = base64Image
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
