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
        "image/jpeg", "image/png", "image/webp", "image/bmp", "application/pdf"
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

        var ext = Path.GetExtension(filename).ToLowerInvariant();
        var contentType = ext == ".pdf" ? "application/pdf" : "image/jpeg";
        return PhysicalFile(filePath, contentType, filename);
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
            // 1. Önce görüntüyü/belgeyi işle
            using var stream = file.OpenReadStream();
            var preprocessResult = await _preprocessingService.ProcessAsync(stream, file.FileName);

            // 2. İşlenmiş dosyayı diskten oku
            var processedFilePath = Path.Combine(_preprocessingService.GetProcessedDir(), preprocessResult.ProcessedFileName);
            var fileBytes = await System.IO.File.ReadAllBytesAsync(processedFilePath);

            var ext = Path.GetExtension(preprocessResult.ProcessedFileName).ToLowerInvariant();
            var mimeType = ext == ".pdf" ? "application/pdf" : "image/jpeg";

            var result = await _geminiService.ScanReceiptAsync(fileBytes, mimeType);

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

            if (request.VknTckn?.Trim().Length > 11)
            {
                var errorMsg = "VKN/TCKN alanı 11 karakter sınırını aştı. Lütfen kontrol edin.";
                await LogErrorToDbAsync("Confirm_Validation", new ArgumentException(errorMsg), request.CreatedBy);
                return BadRequest(ApiResponse<object>.Fail(errorMsg));
            }

            var itemsToProcess = request.Items;
            if (itemsToProcess == null || itemsToProcess.Count == 0)
            {
                itemsToProcess = new List<ConfirmRequestItem>
                {
                    new ConfirmRequestItem
                    {
                        ItemName = "Genel Gider",
                        Quantity = 1,
                        UnitPrice = request.TotalAmount - request.TaxAmount,
                        TotalPrice = request.TotalAmount,
                        TaxRate = 20
                    }
                };
            }

            var expensesSaved = new List<Expense>();
            foreach (var item in itemsToProcess)
            {
                var expense = new Expense
                {
                    FirmaAdi = request.MerchantName,
                    FisNo = request.FisNo,
                    VknTckn = request.VknTckn?.Trim(),
                    KdvOrani = item.TaxRate,
                    Matrah = item.UnitPrice,
                    KdvTutari = item.TotalPrice - item.UnitPrice,
                    ToplamTutar = item.TotalPrice,
                    FisinGenelToplami = request.TotalAmount,
                    KaydedenKullanici = request.CreatedBy,
                    CreatedDate = DateTime.Now
                };

                if (DateTime.TryParse(request.ReceiptDate, out var parsedDate))
                {
                    expense.Tarih = parsedDate;
                }
                else
                {
                    expense.Tarih = DateTime.Today;
                }

                _context.Expenses.Add(expense);
                expensesSaved.Add(expense);
            }

            // Log ekle
            _context.SystemLogs.Add(new SystemLog
            {
                Username = request.CreatedBy,
                ActionType = "Confirm_Receipt",
                Status = "SUCCESS",
                Details = $"Fiş kaydedildi ({expensesSaved.Count} KDV satırı): {request.MerchantName} - Toplam: {request.TotalAmount}"
            });

            await _context.SaveChangesAsync();

            // Excel (.xlsx) dosyasına yazma işlemini kuyruğa ekle
            foreach (var exp in expensesSaved)
            {
                _excelQueueService.QueueWrite(exp, "ADD");
            }

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
                expenses = await _context.Expenses.OrderByDescending(e => e.CreatedDate).ToListAsync();
            }
            else
            {
                expenses = await _context.Expenses
                    .Where(e => e.KaydedenKullanici == username)
                    .OrderByDescending(e => e.CreatedDate)
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
                fisinGenelToplami = e.FisinGenelToplami,
                imagePath = (string?)null,
                createdBy = e.KaydedenKullanici,
                items = new[]
                {
                    new
                    {
                        itemName = "KDV Detayı",
                        quantity = 1,
                        unitPrice = e.Matrah,
                        totalPrice = e.ToplamTutar,
                        taxRate = e.KdvOrani
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

            // Fiş ile ilişkili olan (aynı anda kaydedilen) tüm KDV satırlarını bulalım
            var relatedExpenses = await _context.Expenses
                .Where(e => e.FirmaAdi == expense.FirmaAdi 
                         && e.FisNo == expense.FisNo 
                         && e.Tarih == expense.Tarih 
                         && e.KaydedenKullanici == expense.KaydedenKullanici
                         && e.FisinGenelToplami == expense.FisinGenelToplami)
                .ToListAsync();

            // Aynı saniyeler içinde kaydedilmiş olanları filtreleyelim (5 saniye tolerans)
            relatedExpenses = relatedExpenses
                .Where(e => Math.Abs((e.CreatedDate - expense.CreatedDate).TotalSeconds) <= 5)
                .OrderBy(e => e.Id)
                .ToList();

            var response = new
            {
                id = expense.Id,
                merchantName = expense.FirmaAdi,
                receiptDate = expense.Tarih.ToString("yyyy-MM-dd"),
                createdAt = expense.CreatedDate.ToString("yyyy-MM-dd HH:mm:ss"),
                fisNo = expense.FisNo,
                vknTckn = expense.VknTckn,
                totalAmount = expense.FisinGenelToplami > 0 ? expense.FisinGenelToplami : relatedExpenses.Sum(e => e.ToplamTutar),
                taxAmount = relatedExpenses.Sum(e => e.KdvTutari),
                fisinGenelToplami = expense.FisinGenelToplami,
                imagePath = (string?)null,
                createdBy = expense.KaydedenKullanici,
                items = relatedExpenses.Select(e => new
                {
                    itemName = "KDV Detayı",
                    quantity = 1,
                    unitPrice = e.Matrah,
                    totalPrice = e.ToplamTutar,
                    taxRate = e.KdvOrani
                }).ToArray()
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

            var user = await _context.Users.FirstOrDefaultAsync(u => u.Username.ToLower() == request.CreatedBy.ToLower());
            string userRole = user?.Role ?? "User";

            if (expense.KaydedenKullanici != request.CreatedBy && userRole != "Admin")
            {
                return StatusCode(403, ApiResponse<object>.Fail("Bu kaydı sadece oluşturan kişi veya bir yönetici güncelleyebilir."));
            }

            if (string.IsNullOrEmpty(request.MerchantName))
            {
                return BadRequest(ApiResponse<object>.Fail("Mağaza adı boş olamaz."));
            }

            if (request.VknTckn?.Trim().Length > 11)
            {
                var errorMsg = "VKN/TCKN alanı 11 karakter sınırını aştı. Lütfen kontrol edin.";
                await LogErrorToDbAsync("Update_Validation", new ArgumentException(errorMsg), request.CreatedBy);
                return BadRequest(ApiResponse<object>.Fail(errorMsg));
            }

            var itemsToProcess = request.Items;
            if (itemsToProcess == null || itemsToProcess.Count == 0)
            {
                itemsToProcess = new List<ConfirmRequestItem>
                {
                    new ConfirmRequestItem
                    {
                        ItemName = "Genel Gider",
                        Quantity = 1,
                        UnitPrice = request.TotalAmount - request.TaxAmount,
                        TotalPrice = request.TotalAmount,
                        TaxRate = 20
                    }
                };
            }

            // İlk kalemi bu satırda güncelle
            var mainItem = itemsToProcess[0];
            expense.FirmaAdi = request.MerchantName;
            expense.FisNo = request.FisNo;
            expense.VknTckn = request.VknTckn?.Trim();
            expense.KdvOrani = mainItem.TaxRate;
            expense.Matrah = mainItem.UnitPrice;
            expense.KdvTutari = mainItem.TotalPrice - mainItem.UnitPrice;
            expense.ToplamTutar = mainItem.TotalPrice;
            expense.FisinGenelToplami = request.TotalAmount;
            // expense.KaydedenKullanici = request.CreatedBy; // İlk kaydeden kullanıcıyı korumak için güncellenmiyor

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

            // Önceki ilişkili diğer KDV satırlarını bulup silelim (mükerrerliği önlemek için)
            var oldRelated = await _context.Expenses
                .Where(e => e.FirmaAdi == expense.FirmaAdi 
                         && e.FisNo == expense.FisNo 
                         && e.Tarih == expense.Tarih 
                         && e.KaydedenKullanici == expense.KaydedenKullanici
                         && e.FisinGenelToplami == expense.FisinGenelToplami
                         && e.Id != expense.Id)
                .ToListAsync();

            oldRelated = oldRelated
                .Where(e => Math.Abs((e.CreatedDate - expense.CreatedDate).TotalSeconds) <= 5)
                .ToList();

            foreach (var rel in oldRelated)
            {
                _context.Expenses.Remove(rel);
                _excelQueueService.QueueWrite(rel, "DELETE");
            }
            await _context.SaveChangesAsync();

            // Eğer birden fazla kalem varsa, diğerlerini yeni satır olarak ekle
            if (itemsToProcess.Count > 1)
            {
                for (int i = 1; i < itemsToProcess.Count; i++)
                {
                    var item = itemsToProcess[i];
                    var newExpense = new Expense
                    {
                        FirmaAdi = request.MerchantName,
                        FisNo = request.FisNo,
                        VknTckn = request.VknTckn?.Trim(),
                        KdvOrani = item.TaxRate,
                        Matrah = item.UnitPrice,
                        KdvTutari = item.TotalPrice - item.UnitPrice,
                        ToplamTutar = item.TotalPrice,
                        FisinGenelToplami = request.TotalAmount,
                        KaydedenKullanici = expense.KaydedenKullanici, // İlk kaydeden kullanıcıyı koruyoruz
                        CreatedDate = DateTime.Now
                    };

                    if (DateTime.TryParse(request.ReceiptDate, out var d))
                    {
                        newExpense.Tarih = d;
                    }
                    else
                    {
                        newExpense.Tarih = DateTime.Today;
                    }

                    _context.Expenses.Add(newExpense);
                    await _context.SaveChangesAsync();
                    _excelQueueService.QueueWrite(newExpense, "ADD");
                }
            }

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
    /// Fiş/Fatura kaydını siler (Yalnızca Admin).
    /// </summary>
    [HttpDelete("{id}")]
    public async Task<IActionResult> Delete(int id, [FromQuery] string username)
    {
        try
        {
            var user = await _context.Users.FirstOrDefaultAsync(u => u.Username.ToLower() == username.ToLower());
            if (user == null || user.Role != "Admin")
            {
                return StatusCode(403, ApiResponse<object>.Fail("Bu işlemi gerçekleştirmek için yönetici (Admin) yetkiniz olmalıdır."));
            }

            var expense = await _context.Expenses.FindAsync(id);
            if (expense == null)
            {
                return NotFound(ApiResponse<object>.Fail("Masraf kaydı bulunamadı."));
            }

            // Fiş ile ilişkili tüm KDV satırlarını bulup silelim
            var relatedExpenses = await _context.Expenses
                .Where(e => e.FirmaAdi == expense.FirmaAdi 
                         && e.FisNo == expense.FisNo 
                         && e.Tarih == expense.Tarih 
                         && e.KaydedenKullanici == expense.KaydedenKullanici
                         && e.FisinGenelToplami == expense.FisinGenelToplami)
                .ToListAsync();

            relatedExpenses = relatedExpenses
                .Where(e => Math.Abs((e.CreatedDate - expense.CreatedDate).TotalSeconds) <= 5)
                .ToList();

            foreach (var exp in relatedExpenses)
            {
                _context.Expenses.Remove(exp);
                _excelQueueService.QueueWrite(exp, "DELETE");
            }

            // Log ekle
            _context.SystemLogs.Add(new SystemLog
            {
                Username = username,
                ActionType = "Delete_Receipt",
                Status = "SUCCESS",
                Details = $"Fiş ve ilişkili {relatedExpenses.Count} KDV satırı silindi: ID {id} - {expense.FirmaAdi} - Genel Toplam: {expense.FisinGenelToplami}"
            });

            await _context.SaveChangesAsync();

            return Ok(ApiResponse<object>.Ok(null, "Fatura başarıyla silindi."));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[API] Delete hatası.");
            await LogErrorToDbAsync($"Delete_{id}", ex, username);
            return StatusCode(500, ApiResponse<object>.Fail($"Silme Hatası: {ex.Message}"));
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
            worksheet.Cell(1, 5).Value = "KDV Oranı";
            worksheet.Cell(1, 6).Value = "Kdv Tutari";
            worksheet.Cell(1, 7).Value = "Toplam Tutar";
            worksheet.Cell(1, 8).Value = "Matrah";
            worksheet.Cell(1, 9).Value = "Fişin Genel Toplamı";
            worksheet.Cell(1, 10).Value = "Kaydeden Kullanici";

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
                worksheet.Cell(row, 5).Value = exp.KdvOrani + "%";
                worksheet.Cell(row, 6).Value = exp.KdvTutari;
                worksheet.Cell(row, 7).Value = exp.ToplamTutar;
                worksheet.Cell(row, 8).Value = exp.Matrah;
                worksheet.Cell(row, 9).Value = exp.FisinGenelToplami;
                worksheet.Cell(row, 10).Value = exp.KaydedenKullanici;

                worksheet.Cell(row, 6).Style.NumberFormat.Format = "0.00";
                worksheet.Cell(row, 7).Style.NumberFormat.Format = "0.00";
                worksheet.Cell(row, 8).Style.NumberFormat.Format = "0.00";
                worksheet.Cell(row, 9).Style.NumberFormat.Format = "0.00";
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
