using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ReceiptOCR.API.Data;
using ReceiptOCR.API.Models;
using ReceiptOCR.API.Services;
using ClosedXML.Excel;

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
    private readonly IExcelQueueService _excelQueueService;
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
        IExcelQueueService excelQueueService,
        ILogger<ReceiptController> logger)
    {
        _preprocessingService = preprocessingService;
        _geminiService = geminiService;
        _context = context;
        _excelQueueService = excelQueueService;
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
            await LogErrorToDbAsync("Preprocess", ex);
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
            await LogErrorToDbAsync("Scan", ex);
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

            var expense = new Expense
            {
                FirmaAdi = request.MerchantName,
                FisNo = request.FisNo,
                VknTckn = request.VknTckn,
                ToplamTutar = request.TotalAmount,
                KdvTutari = request.TaxAmount,
                KaydedenKullanici = request.CreatedBy,
                CreatedDate = DateTime.Now
            };

            // Tarih ayrıştırma
            if (DateTime.TryParse(request.ReceiptDate, out var parsedDate))
            {
                expense.Tarih = parsedDate;
            }
            else
            {
                expense.Tarih = DateTime.Today;
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

            // Excel (.xlsx) dosyasına yazma işlemini kuyruğa ekle
            _excelQueueService.QueueWrite(expense, "ADD");

            return Ok(ApiResponse<object>.Ok(null, "Fiş başarıyla veritabanına kaydedildi ve Excel yazma kuyruğuna eklendi."));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[API] Confirm hatası.");
            await LogErrorToDbAsync("Confirm", ex, request?.CreatedBy);
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
                createdAt = e.CreatedDate.ToString("yyyy-MM-dd HH:mm:ss"),
                fisNo = e.FisNo,
                vknTckn = e.VknTckn,
                totalAmount = e.ToplamTutar,
                taxAmount = e.KdvTutari,
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
                        tax_rate = 20
                    }
                }
            }).ToList();

            return Ok(response);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[API] History listeleme hatası.");
            await LogErrorToDbAsync("History", ex, username);
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
                createdAt = expense.CreatedDate.ToString("yyyy-MM-dd HH:mm:ss"),
                fisNo = expense.FisNo,
                vknTckn = expense.VknTckn,
                totalAmount = expense.ToplamTutar,
                taxAmount = expense.KdvTutari,
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
                        tax_rate = 20
                    }
                }
            };

            return Ok(response);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[API] GetDetails hatası.");
            await LogErrorToDbAsync($"GetDetails_{id}", ex);
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
            expense.FisNo = request.FisNo;
            expense.VknTckn = request.VknTckn;
            expense.ToplamTutar = request.TotalAmount;
            expense.KdvTutari = request.TaxAmount;
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

            // Excel (.xlsx) dosyasına güncelleme işlemini kuyruğa ekle
            _excelQueueService.QueueWrite(expense, "UPDATE");

            return Ok(ApiResponse<object>.Ok(null, "Fiş başarıyla güncellendi ve Excel yazma kuyruğuna eklendi."));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[API] Update hatası.");
            await LogErrorToDbAsync($"Update_{id}", ex, request?.CreatedBy);
            return StatusCode(500, ApiResponse<object>.Fail($"Güncelleme Hatası: {ex.Message}"));
        }
    }

    /// <summary>
    /// Excel (XLSX) dosyasını indirtir.
    /// </summary>
    [HttpGet("/api/receipts/export")]
    public async Task<IActionResult> Export([FromQuery] string? username)
    {
        try
        {
            var excelPathSetting = await _context.Settings.FirstOrDefaultAsync(s => s.Key == "ExcelPath");
            string excelPath = excelPathSetting?.Value ?? @"C:\Muhasebe\Masraflar.xlsx";

            var directory = Path.GetDirectoryName(excelPath);
            if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }

            using var workbook = new XLWorkbook();
            var worksheet = workbook.Worksheets.Add("Masraflar");

            // Başlıklar
            worksheet.Cell(1, 1).Value = "Tarih";
            worksheet.Cell(1, 2).Value = "Firma Adi";
            worksheet.Cell(1, 3).Value = "Fis No";
            worksheet.Cell(1, 4).Value = "Vkn Tckn";
            worksheet.Cell(1, 5).Value = "Kdv Tutari";
            worksheet.Cell(1, 6).Value = "Toplam Tutar";
            worksheet.Cell(1, 7).Value = "Kaydeden Kullanici";

            var headerRow = worksheet.Row(1);
            headerRow.Style.Font.Bold = true;
            headerRow.Style.Fill.BackgroundColor = XLColor.LightGray;

            // Filtreleme (eğer username gönderilmişse sadece onun verilerini getir)
            List<Expense> dbExpenses;
            if (string.IsNullOrEmpty(username))
            {
                dbExpenses = await _context.Expenses.OrderByDescending(e => e.Tarih).ToListAsync();
            }
            else
            {
                dbExpenses = await _context.Expenses
                    .Where(e => e.KaydedenKullanici == username)
                    .OrderByDescending(e => e.Tarih)
                    .ToListAsync();
            }

            int row = 2;
            foreach (var exp in dbExpenses)
            {
                worksheet.Cell(row, 1).Value = exp.Tarih.ToString("yyyy-MM-dd");
                worksheet.Cell(row, 2).Value = exp.FirmaAdi;
                worksheet.Cell(row, 3).Value = exp.FisNo ?? "";
                worksheet.Cell(row, 4).Value = exp.VknTckn ?? "";
                worksheet.Cell(row, 5).Value = exp.KdvTutari;
                worksheet.Cell(row, 6).Value = exp.ToplamTutar;
                worksheet.Cell(row, 7).Value = exp.KaydedenKullanici;

                worksheet.Cell(row, 5).Style.NumberFormat.Format = "0.00";
                worksheet.Cell(row, 6).Style.NumberFormat.Format = "0.00";
                row++;
            }

            worksheet.Columns().AdjustToContents();
            workbook.SaveAs(excelPath);

            var bytes = await System.IO.File.ReadAllBytesAsync(excelPath);
            return File(bytes, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", "masraflar.xlsx");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[API] Export hatası.");
            await LogErrorToDbAsync("Export", ex, username);
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

    private async Task LogErrorToDbAsync(string actionType, Exception ex, string? username = null)
    {
        try
        {
            _context.ChangeTracker.Clear();
            var errorLog = new ErrorLog
            {
                Timestamp = DateTime.Now,
                Username = username ?? User?.Identity?.Name,
                ActionType = actionType,
                ErrorMessage = ex.Message,
                StackTrace = ex.StackTrace
            };
            _context.ErrorLogs.Add(errorLog);
            await _context.SaveChangesAsync();
        }
        catch (Exception dbEx)
        {
            _logger.LogError(dbEx, "Hata veritabanına kaydedilemedi.");
        }
    }
}

public class ConfirmRequest
{
    public int Id { get; set; }
    public string MerchantName { get; set; } = string.Empty;
    public string ReceiptDate { get; set; } = string.Empty;
    public string? FisNo { get; set; }
    public string? VknTckn { get; set; }
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
