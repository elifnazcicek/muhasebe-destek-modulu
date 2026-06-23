using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ReceiptOCR.API.Data;
using ReceiptOCR.API.Models;
using ReceiptOCR.API.Services;

namespace ReceiptOCR.API.Controllers;

/// <summary>
/// Fiş/Fatura İşleme Controller'ı
/// </summary>
[ApiController]
[Route("api/[controller]")]
public class ReceiptController : ControllerBase
{
    private readonly ImagePreprocessingService _preprocessingService;
    private readonly GeminiService _geminiService;
    private readonly ReceiptDbContext _context;
    private readonly ILogger<ReceiptController> _logger;

    // Desteklenen dosya formatları
    private static readonly HashSet<string> AllowedContentTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "image/jpeg", "image/png", "image/webp", "image/bmp"
    };

    public ReceiptController(
        ImagePreprocessingService preprocessingService,
        GeminiService geminiService,
        ReceiptDbContext context,
        ILogger<ReceiptController> logger)
    {
        _preprocessingService = preprocessingService;
        _geminiService = geminiService;
        _context = context;
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
            using var ms = new MemoryStream();
            await file.CopyToAsync(ms);
            var imageBytes = ms.ToArray();

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
    /// [SQL SERVER EKİBİ] Onaylanan fiş verisini veritabanına ve Excel'e kaydeder.
    /// </summary>
    [HttpPost("confirm")]
    public async Task<IActionResult> Confirm([FromBody] ConfirmRequest request)
    {
        _logger.LogInformation("[API] Confirm isteği alındı. Mağaza: {Merchant}", request.MerchantName);

        try
        {
            if (string.IsNullOrEmpty(request.MerchantName))
            {
                return BadRequest(ApiResponse<object>.Fail("Mağaza adı boş olamaz."));
            }

            // KDV oranını ürünlerden al, yoksa varsayılan 20 yap
            int kdvOrani = request.Items.FirstOrDefault()?.TaxRate ?? 20;

            var expense = new Expense
            {
                FirmaAdi = request.MerchantName,
                ToplamTutar = request.TotalAmount,
                KdvOrani = kdvOrani,
                KaydedenKullanici = request.CreatedBy,
                CreatedDate = DateTime.UtcNow
            };

            // Tarih ayrıştırma
            if (DateTime.TryParse(request.ReceiptDate, out var parsedDate))
            {
                expense.Tarih = parsedDate;
            }
            else
            {
                expense.Tarih = DateTime.UtcNow.Date;
            }

            // Veritabanına kaydet
            _context.Expenses.Add(expense);

            // Log ekle
            _context.SystemLogs.Add(new SystemLog
            {
                Username = request.CreatedBy,
                ActionType = "Confirm_Receipt",
                Status = "SUCCESS",
                Details = $"Fiş kaydedildi: {request.MerchantName} - Tutar: {request.TotalAmount}"
            });

            await _context.SaveChangesAsync();

            // Excel (CSV) dosyasına ekle
            try
            {
                var excelPathSetting = await _context.Settings.FirstOrDefaultAsync(s => s.Key == "ExcelPath");
                string excelPath = excelPathSetting?.Value ?? @"C:\Muhasebe\Masraflar.xlsx";

                var directory = Path.GetDirectoryName(excelPath);
                if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                // Dosya uzantısı xlsx olarak görünse de, sistemde Excel kütüphanesi olmadığı için
                // en güvenli yol olan CSV formatında yazıp Excel'in açmasını sağlıyoruz.
                bool exists = System.IO.File.Exists(excelPath);
                using (var writer = new StreamWriter(excelPath, true, System.Text.Encoding.UTF8))
                {
                    if (!exists)
                    {
                        writer.WriteLine("Tarih,Firma Adi,Fis No,Kdv Orani,Toplam Tutar,Kaydeden Kullanici");
                    }
                    writer.WriteLine($"{expense.Tarih:yyyy-MM-dd},{EscapeCsv(expense.FirmaAdi)},{EscapeCsv(expense.FisNo)},{expense.KdvOrani},{expense.ToplamTutar},{EscapeCsv(expense.KaydedenKullanici)}");
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Excel dosyasına yazılırken hata oluştu, ancak veritabanı kaydı başarılı.");
            }

            return Ok(ApiResponse<object>.Ok(null, "Fiş başarıyla veritabanına ve Excel dosyasına kaydedildi."));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[API] Confirm hatası.");
            return StatusCode(500, ApiResponse<object>.Fail($"Kayıt Hatası: {ex.Message}"));
        }
    }

    /// <summary>
    /// [SQL SERVER EKİBİ] Geçmiş fiş kayıtlarını listeler.
    /// </summary>
    [HttpGet("history")]
    public async Task<IActionResult> History([FromQuery] string? username)
    {
        _logger.LogInformation("[API] History sorgusu alındı. Kullanıcı: {Username}", username);

        try
        {
            List<Expense> expenses;
            if (string.IsNullOrEmpty(username))
            {
                expenses = await _context.Expenses.OrderByDescending(e => e.Tarih).ToListAsync();
            }
            else
            {
                expenses = await _context.Expenses
                    .Where(e => e.KaydedenKullanici == username)
                    .OrderByDescending(e => e.Tarih)
                    .ToListAsync();
            }

            // Angular'ın beklediği formatta çıktı üretiyoruz
            var response = expenses.Select(e => new
            {
                id = e.Id,
                merchantName = e.FirmaAdi,
                receiptDate = e.Tarih.ToString("yyyy-MM-dd"),
                totalAmount = e.ToplamTutar,
                taxAmount = e.ToplamTutar * e.KdvOrani / (100 + e.KdvOrani),
                imagePath = (string?)null,
                createdBy = e.KaydedenKullanici,
                items = new[]
                {
                    new
                    {
                        itemName = "Masraf Kalemi",
                        quantity = 1,
                        unitPrice = e.ToplamTutar,
                        totalPrice = e.ToplamTutar,
                        tax_rate = e.KdvOrani
                    }
                }
            }).ToList();

            return Ok(response);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[API] History listeleme hatası.");
            return StatusCode(500, ApiResponse<object>.Fail($"Geçmiş okunamadı: {ex.Message}"));
        }
    }

    /// <summary>
    /// Fiş detaylarını getirir.
    /// </summary>
    [HttpGet("{id}")]
    public async Task<IActionResult> GetDetails(int id)
    {
        try
        {
            var expense = await _context.Expenses.FindAsync(id);
            if (expense == null)
            {
                return NotFound(ApiResponse<object>.Fail("Masraf kaydı bulunamadı."));
            }

            var response = new
            {
                id = expense.Id,
                merchantName = expense.FirmaAdi,
                receiptDate = expense.Tarih.ToString("yyyy-MM-dd"),
                totalAmount = expense.ToplamTutar,
                taxAmount = expense.ToplamTutar * expense.KdvOrani / (100 + expense.KdvOrani),
                imagePath = (string?)null,
                createdBy = expense.KaydedenKullanici,
                items = new[]
                {
                    new
                    {
                        itemName = "Masraf Kalemi",
                        quantity = 1,
                        unitPrice = expense.ToplamTutar,
                        totalPrice = expense.ToplamTutar,
                        tax_rate = expense.KdvOrani
                    }
                }
            };

            return Ok(response);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[API] GetDetails hatası.");
            return StatusCode(500, ApiResponse<object>.Fail($"Veri okuma hatası: {ex.Message}"));
        }
    }

    /// <summary>
    /// Kayıtlı fiş verilerini günceller.
    /// </summary>
    [HttpPut("{id}")]
    public async Task<IActionResult> Update(int id, [FromBody] ConfirmRequest request)
    {
        try
        {
            var expense = await _context.Expenses.FindAsync(id);
            if (expense == null)
            {
                return NotFound(ApiResponse<object>.Fail("Masraf kaydı bulunamadı."));
            }

            expense.FirmaAdi = request.MerchantName;
            expense.ToplamTutar = request.TotalAmount;
            expense.KdvOrani = request.Items.FirstOrDefault()?.TaxRate ?? 20;
            expense.KaydedenKullanici = request.CreatedBy;

            if (DateTime.TryParse(request.ReceiptDate, out var date))
            {
                expense.Tarih = date;
            }

            _context.Expenses.Update(expense);

            // Log ekle
            _context.SystemLogs.Add(new SystemLog
            {
                Username = request.CreatedBy,
                ActionType = "Update_Receipt",
                Status = "SUCCESS",
                Details = $"Fiş güncellendi: ID {id} - {request.MerchantName} - Tutar: {request.TotalAmount}"
            });

            await _context.SaveChangesAsync();

            return Ok(ApiResponse<object>.Ok(null, "Fiş başarıyla güncellendi."));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[API] Update hatası.");
            return StatusCode(500, ApiResponse<object>.Fail($"Güncelleme Hatası: {ex.Message}"));
        }
    }

    /// <summary>
    /// Excel (CSV) dosyasını indirtir.
    /// </summary>
    [HttpGet("/api/receipts/export")]
    public async Task<IActionResult> Export([FromQuery] string? username)
    {
        try
        {
            var excelPathSetting = await _context.Settings.FirstOrDefaultAsync(s => s.Key == "ExcelPath");
            string excelPath = excelPathSetting?.Value ?? @"C:\Muhasebe\Masraflar.xlsx";

            // Eğer dosya yoksa veritabanındakileri baştan yazalım
            if (!System.IO.File.Exists(excelPath))
            {
                var directory = Path.GetDirectoryName(excelPath);
                if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                using var writer = new StreamWriter(excelPath, false, System.Text.Encoding.UTF8);
                writer.WriteLine("Tarih,Firma Adi,Fis No,Kdv Orani,Toplam Tutar,Kaydeden Kullanici");
                
                var dbExpenses = await _context.Expenses.ToListAsync();
                foreach (var exp in dbExpenses)
                {
                    writer.WriteLine($"{exp.Tarih:yyyy-MM-dd},{EscapeCsv(exp.FirmaAdi)},{EscapeCsv(exp.FisNo)},{exp.KdvOrani},{exp.ToplamTutar},{EscapeCsv(exp.KaydedenKullanici)}");
                }
            }

            var bytes = await System.IO.File.ReadAllBytesAsync(excelPath);
            return File(bytes, "text/csv", "masraflar.csv");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[API] Export hatası.");
            return StatusCode(500, "Dosya indirme hatası: " + ex.Message);
        }
    }

    private static string EscapeCsv(string? value)
    {
        if (string.IsNullOrEmpty(value)) return "";
        if (value.Contains(",") || value.Contains("\"") || value.Contains("\n") || value.Contains("\r"))
        {
            return $"\"{value.Replace("\"", "\"\"")}\"";
        }
        return value;
    }
}

public class ConfirmRequest
{
    public int Id { get; set; }
    public string MerchantName { get; set; } = string.Empty;
    public string ReceiptDate { get; set; } = string.Empty;
    public decimal TotalAmount { get; set; }
    public decimal TaxAmount { get; set; }
    public string? ImagePath { get; set; }
    public string CreatedBy { get; set; } = string.Empty;
    public List<ConfirmRequestItem> Items { get; set; } = new();
}

public class ConfirmRequestItem
{
    public string ItemName { get; set; } = string.Empty;
    public int Quantity { get; set; }
    public decimal UnitPrice { get; set; }
    public decimal TotalPrice { get; set; }
    public int TaxRate { get; set; }
}
