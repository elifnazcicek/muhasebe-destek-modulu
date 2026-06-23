using Microsoft.AspNetCore.Mvc;
using ReceiptOCR.API.Models;
using ReceiptOCR.API.Services;

namespace ReceiptOCR.API.Controllers;

/// <summary>
/// Fiş/Fatura İşleme Controller'ı
/// 
/// Sorumluluk Dağılımı:
/// - POST /api/receipt/preprocess → BİZİM (görüntü ön işleme)
/// - POST /api/receipt/scan       → GEMİNİ API EKİBİ (OCR + parse)
/// - POST /api/receipt/confirm    → SQL SERVER EKİBİ (DB kayıt)
/// - GET  /api/receipt/history    → SQL SERVER EKİBİ (geçmiş sorgulama)
/// </summary>
[ApiController]
[Route("api/[controller]")]
public class ReceiptController : ControllerBase
{
    private readonly ImagePreprocessingService _preprocessingService;
    private readonly GeminiService _geminiService;
    private readonly ILogger<ReceiptController> _logger;

    // Desteklenen dosya formatları
    private static readonly HashSet<string> AllowedContentTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "image/jpeg", "image/png", "image/webp", "image/bmp"
    };

    public ReceiptController(
        ImagePreprocessingService preprocessingService,
        GeminiService geminiService,
        ILogger<ReceiptController> logger)
    {
        _preprocessingService = preprocessingService;
        _geminiService = geminiService;
        _logger = logger;
    }

    // =========================================================================
    // BİZİM SORUMLULUK ALANLARIMIZ
    // =========================================================================

    /// <summary>
    /// Görsel yükle ve ön işlemeden geçir (boyut optimizasyonu + JPEG sıkıştırma).
    /// Angular tarafında kırpma/flip yapıldıktan sonra bu endpoint'e gönderilir.
    /// </summary>
    [HttpPost("preprocess")]
    public async Task<IActionResult> Preprocess(IFormFile file)
    {
        _logger.LogInformation("[API] Preprocess isteği alındı. Dosya: {FileName}, Boyut: {Size} bytes",
            file?.FileName, file?.Length);

        // Dosya kontrolü
        if (file == null || file.Length == 0)
        {
            return BadRequest(ApiResponse<object>.Fail("Dosya yüklenmedi veya boş."));
        }

        // Format kontrolü
        if (!AllowedContentTypes.Contains(file.ContentType))
        {
            return BadRequest(ApiResponse<object>.Fail(
                $"Desteklenmeyen format: {file.ContentType}. Desteklenen: JPEG, PNG, WebP, BMP"));
        }

        // Boyut kontrolü (maks 20MB)
        if (file.Length > 20 * 1024 * 1024)
        {
            return BadRequest(ApiResponse<object>.Fail("Dosya boyutu 20MB'ı aşamaz."));
        }

        try
        {
            using var stream = file.OpenReadStream();
            var result = await _preprocessingService.ProcessAsync(stream, file.FileName);

            var response = new PreprocessingResult
            {
                Original = new Models.FileInfo
                {
                    Filename = result.OriginalFileName,
                    SizeKb = result.OriginalSizeKb
                },
                Processed = new Models.FileInfo
                {
                    Filename = result.ProcessedFileName,
                    SizeKb = result.ProcessedSizeKb,
                    DownloadUrl = $"/api/receipt/download/{result.ProcessedFileName}"
                },
                CompressionRatio = result.CompressionRatio,
                AppliedSteps = result.AppliedSteps
            };

            return Ok(ApiResponse<PreprocessingResult>.Ok(response, "Görüntü ön işleme tamamlandı."));
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(ApiResponse<object>.Fail(ex.Message));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[API] Preprocess hatası.");
            return StatusCode(500, ApiResponse<object>.Fail($"Sunucu hatası: {ex.Message}"));
        }
    }

    /// <summary>
    /// İşlenmiş görseli indirme endpoint'i.
    /// </summary>
    [HttpGet("download/{filename}")]
    public IActionResult Download(string filename)
    {
        var filePath = Path.Combine(_preprocessingService.GetProcessedDir(), filename);

        if (!System.IO.File.Exists(filePath))
        {
            return NotFound(ApiResponse<object>.Fail("Dosya bulunamadı."));
        }

        return PhysicalFile(filePath, "image/jpeg", filename);
    }

    // =========================================================================
    // DİĞER EKİPLERİN SORUMLULUK ALANLARI (PLACEHOLDER)
    // =========================================================================

    /// <summary>
    /// Görseli Gemini'ye gönderip fiş verilerini parse eder.
    /// Görüntü direkt base64 veya dosya olarak gönderilebilir.
    /// </summary>
    [HttpPost("scan")]
    public async Task<IActionResult> Scan(IFormFile file)
    {
        _logger.LogInformation("[API] Scan isteği alındı. Dosya: {FileName}", file?.FileName);

        if (file == null || file.Length == 0)
            return BadRequest(ApiResponse<object>.Fail("Dosya yüklenmedi."));

        if (!AllowedContentTypes.Contains(file.ContentType))
            return BadRequest(ApiResponse<object>.Fail("Desteklenmeyen format."));

        try
        {
            // 1. Önce görüntüyü işle (döndürme, kırpma, netleştirme)
            using var stream = file.OpenReadStream();
            var preprocessResult = await _preprocessingService.ProcessAsync(stream, file.FileName);

            // 2. İşlenmiş dosyayı diskten oku
            var processedFilePath = Path.Combine(_preprocessingService.GetProcessedDir(), preprocessResult.ProcessedFileName);
            var imageBytes = await System.IO.File.ReadAllBytesAsync(processedFilePath);

            var result = await _geminiService.ScanReceiptAsync(imageBytes);

            if (result == null)
            {
                return BadRequest(ApiResponse<object>.Fail("Gemini API'den sonuç alınamadı."));
            }

            return Ok(ApiResponse<ExtractedReceiptData>.Ok(result, "Fiş başarıyla okundu."));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[API] Scan hatası.");
            return StatusCode(500, ApiResponse<object>.Fail($"OCR Hatası: {ex.Message}"));
        }
    }

    /// <summary>
    /// [SQL SERVER EKİBİ] Onaylanan fiş verisini veritabanına kaydeder.
    /// TODO: SQL Server ekibi tarafından implement edilecek.
    /// </summary>
    [HttpPost("confirm")]
    public IActionResult Confirm()
    {
        return StatusCode(501, ApiResponse<object>.Fail(
            "Bu endpoint henüz implement edilmedi. SQL Server ekibi tarafından geliştirilecek."));
    }

    /// <summary>
    /// [SQL SERVER EKİBİ] Geçmiş fiş kayıtlarını listeler.
    /// TODO: SQL Server ekibi tarafından implement edilecek.
    /// </summary>
    [HttpGet("history")]
    public IActionResult History()
    {
        return StatusCode(501, ApiResponse<object>.Fail(
            "Bu endpoint henüz implement edilmedi. SQL Server ekibi tarafından geliştirilecek."));
    }
}
