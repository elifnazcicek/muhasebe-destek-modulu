using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Xml.Linq;
using ClosedXML.Excel;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.EntityFrameworkCore;
using ReceiptOCR.API.Data;
using ReceiptOCR.API.Models;

namespace ReceiptOCR.API.Controllers;

[ApiController]
[Route("api/dekont")]
public class DekontController : ControllerBase
{
    private readonly IConfiguration _configuration;
    private readonly ILogger<DekontController> _logger;
    private readonly ReceiptDbContext _context;

    // Mikro SQL Server Connection String appsettings.json'dan çekilecek
    private string ConnectionString => _configuration.GetConnectionString("DefaultConnection") 
        ?? "Server=localhost;Database=ReceiptOcrDb_New;Trusted_Connection=True;TrustServerCertificate=True;";

    public DekontController(IConfiguration configuration, ILogger<DekontController> logger, ReceiptDbContext context)
    {
        _configuration = configuration;
        _logger = logger;
        _context = context;

        // Otomatik tablo güncelleme - FaturaTipi kolonu kontrolü ve ekleme
        try
        {
            using var conn = new SqlConnection(ConnectionString);
            conn.Open();
            using var cmd = new SqlCommand(@"
                IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID('Dekonts') AND name = 'FaturaTipi')
                BEGIN
                    ALTER TABLE Dekonts ADD FaturaTipi NVARCHAR(50) NULL;
                END", conn);
            cmd.ExecuteNonQuery();
        }
        catch (Exception ex)
        {
            _logger.LogWarning("Dekonts tablosu guncelleme sirasinda hata: " + ex.Message);
        }
    }

    /// <summary>
    /// Uyumsoft XML faturasını okuyup çözümleyen ve Mikro SQL veritabanında carisini denetleyen endpoint
    /// </summary>
    [HttpPost("parse-xml")]
    public async Task<IActionResult> ParseXml(IFormFile file)
    {
        _logger.LogInformation("[API] Uyumsoft XML Çözümleme isteği alındı. Dosya: {FileName}", file?.FileName);

        if (file == null || file.Length == 0)
            return BadRequest(ApiResponse<object>.Fail("Lütfen geçerli bir dosya yükleyiniz."));

        try
        {
            var ext = Path.GetExtension(file.FileName).ToLowerInvariant();
            if (ext == ".pdf")
            {
                // PDF Simülasyon Modu (Gerekirse text okuma yapılabilir, şimdilik test verisi dönüyoruz)
                return Ok(ApiResponse<object>.Ok(new
                {
                    belgeNo = "PDF-" + new Random().Next(100000, 999999),
                    tarih = DateTime.Now.ToString("yyyy-MM-dd"),
                    vkn = "1234567890",
                    cariAdi = "Test PDF Firması A.Ş.",
                    cariKodu = "",
                    isCariValid = false,
                    invoiceLines = new List<object>
                    {
                        new { cinsi = "Hizmet", kodu = "760.01.001", ismi = "PDF İşlem Hizmet Bedeli", tutar = 1000.00, kdvOrani = 20 }
                    },
                    araToplam = 1000.00,
                    kdvToplam = 200.00,
                    genelToplam = 1200.00
                }, "PDF faturası test modunda okundu."));
            }

            // XML Çözümleme (UBL-TR Standartı)
            using var stream = file.OpenReadStream();
            var doc = XDocument.Load(stream);
            XNamespace cbc = "urn:oasis:names:specification:ubl:schema:xsd:CommonBasicComponents-2";
            XNamespace cac = "urn:oasis:names:specification:ubl:schema:xsd:CommonAggregateComponents-2";

            // Fatura ve Belge Bilgileri
            var belgeNo = doc.Root?.Element(cbc + "ID")?.Value ?? "";
            var tarihStr = doc.Root?.Element(cbc + "IssueDate")?.Value ?? "";
            
            // Satıcı (Supplier / Cari) Bilgileri
            var supplierParty = doc.Root?.Element(cac + "AccountingSupplierParty")?.Element(cac + "Party");
            var vkn = supplierParty?.Elements(cac + "PartyIdentification")
                .FirstOrDefault(x => x.Element(cbc + "ID")?.Attribute("schemeID")?.Value == "VKN" 
                                  || x.Element(cbc + "ID")?.Attribute("schemeID")?.Value == "TCKN")
                ?.Element(cbc + "ID")?.Value ?? "";

            var cariAdi = supplierParty?.Element(cac + "PartyName")?.Element(cbc + "Name")?.Value 
                ?? supplierParty?.Element(cac + "PartyLegalEntity")?.Element(cbc + "RegistrationName")?.Value 
                ?? "";

            // Satırları Çözümleme
            var lines = new List<ParsedInvoiceLine>();
            var lineElements = doc.Root?.Elements(cac + "InvoiceLine") ?? Enumerable.Empty<XElement>();
            
            double calculatedAraToplam = 0;
            double calculatedKdvToplam = 0;

            foreach (var el in lineElements)
            {
                var itemName = el.Element(cac + "Item")?.Element(cbc + "Name")?.Value ?? "Hizmet Satırı";
                var lineAmountStr = el.Element(cbc + "LineExtensionAmount")?.Value ?? "0";
                var percentStr = el.Element(cac + "TaxTotal")?.Element(cac + "TaxSubtotal")?.Element(cbc + "Percent")?.Value ?? "20";

                double.TryParse(lineAmountStr, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out double lineAmount);
                double.TryParse(percentStr, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out double taxPercent);

                lines.Add(new ParsedInvoiceLine
                {
                    Cinsi = "Hizmet", // Varsayılan Hizmet kartı
                    Kodu = "760.01.001", // Varsayılan Muhasebe banka masraf kodu
                    Ismi = itemName,
                    Tutar = lineAmount,
                    KdvOrani = taxPercent
                });

                calculatedAraToplam += lineAmount;
                calculatedKdvToplam += (lineAmount * (taxPercent / 100));
            }

            // Eğer satır detayları okunamadıysa, fatura toplam alanından oku
            if (lines.Count == 0)
            {
                var taxExclusiveAmountStr = doc.Root?.Element(cac + "LegalMonetaryTotal")?.Element(cbc + "TaxExclusiveAmount")?.Value ?? "0";
                var taxAmountStr = doc.Root?.Element(cac + "TaxTotal")?.Element(cac + "TaxSubtotal")?.Element(cbc + "TaxAmount")?.Value ?? "0";
                
                double.TryParse(taxExclusiveAmountStr, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out calculatedAraToplam);
                double.TryParse(taxAmountStr, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out calculatedKdvToplam);

                lines.Add(new ParsedInvoiceLine
                {
                    Cinsi = "Hizmet",
                    Kodu = "760.01.001",
                    Ismi = "Uyumsoft Genel Hizmet Bedeli",
                    Tutar = calculatedAraToplam,
                    KdvOrani = 20
                });
            }

            // Mikro SQL Veritabanında VKN Sorgulama
            var (cariKodu, matchedCariAdi, isCariValid) = await CheckCariInMikroDbAsync(vkn);

            // Eğer veritabanından eşleşen ünvan geldiyse arayüzde onu göster
            if (!string.IsNullOrEmpty(matchedCariAdi))
            {
                cariAdi = matchedCariAdi;
            }

            // XML'i görsel olarak arayüzde render etmek için HTML formatına çevirme şablonu (Simüle edilmiş basit şablon)
            var htmlTemplate = GenerateSimpleHtmlInvoice(belgeNo, tarihStr, vkn, cariAdi, lines, calculatedAraToplam, calculatedKdvToplam);

            var result = new
            {
                belgeNo,
                tarih = tarihStr,
                vkn,
                cariAdi,
                cariKodu,
                isCariValid,
                invoiceLines = lines,
                araToplam = Math.Round(calculatedAraToplam, 2),
                kdvToplam = Math.Round(calculatedKdvToplam, 2),
                genelToplam = Math.Round(calculatedAraToplam + calculatedKdvToplam, 2),
                htmlContent = htmlTemplate
            };

            return Ok(ApiResponse<object>.Ok(result, "XML başarıyla çözümlendi."));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[API] XML Parse Hatası.");
            return StatusCode(500, ApiResponse<object>.Fail($"Dosya ayrıştırma hatası: {ex.Message}"));
        }
    }

    /// <summary>
    /// Mikro SQL veritabanında yeni cari hesabı açar.
    /// </summary>
    [HttpPost("create-cari")]
    public async Task<IActionResult> CreateCari([FromBody] CreateCariRequest request)
    {
        if (string.IsNullOrEmpty(request.Vkn) || string.IsNullOrEmpty(request.CariAdi))
        {
            return BadRequest(ApiResponse<object>.Fail("VKN ve Cari Unvanı zorunludur."));
        }

        try
        {
            using var conn = new SqlConnection(ConnectionString);
            await conn.OpenAsync();

            // 1. Son kullanılan Cari kodunu alarak yeni bir kod üret (Örn: 120.01.XXX)
            string nextCariKod = "120.01.001";
            var selectCmdText = "SELECT TOP 1 cari_kod FROM CARI_HESAPLAR WHERE cari_kod LIKE '120.01.%' ORDER BY cari_kod DESC";
            
            using (var selectCmd = new SqlCommand(selectCmdText, conn))
            {
                var lastCode = await selectCmd.ExecuteScalarAsync() as string;
                if (!string.IsNullOrEmpty(lastCode))
                {
                    var lastNumStr = lastCode.Split('.').Last();
                    if (int.TryParse(lastNumStr, out int num))
                    {
                        nextCariKod = $"120.01.{(num + 1):D3}";
                    }
                }
            }

            // 2. Mikro CARI_HESAPLAR tablosuna yeni cariyi ekle
            var insertCmdText = @"
                INSERT INTO CARI_HESAPLAR (cari_kod, cari_unvan1, cari_vkn, cari_tckn, cari_Doviz_Cinsi, cari_created_date) 
                VALUES (@kod, @unvan, @vkn, @tckn, 0, GETDATE())";

            using (var insertCmd = new SqlCommand(insertCmdText, conn))
            {
                insertCmd.Parameters.AddWithValue("@kod", nextCariKod);
                insertCmd.Parameters.AddWithValue("@unvan", request.CariAdi);
                if (request.Vkn.Length == 11)
                {
                    insertCmd.Parameters.AddWithValue("@vkn", DBNull.Value);
                    insertCmd.Parameters.AddWithValue("@tckn", request.Vkn);
                }
                else
                {
                    insertCmd.Parameters.AddWithValue("@vkn", request.Vkn);
                    insertCmd.Parameters.AddWithValue("@tckn", DBNull.Value);
                }

                await insertCmd.ExecuteNonQueryAsync();
            }

            return Ok(ApiResponse<object>.Ok(new { cariKodu = nextCariKod }, "Cari Kartı Mikro'da başarıyla oluşturuldu."));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[API] Cari kart oluşturma hatası.");
            return StatusCode(500, ApiResponse<object>.Fail($"SQL Veritabanı hatası: {ex.Message}"));
        }
    }

    /// <summary>
    /// Mikro veritabanında cari arama (Elle Eşleştirme için)
    /// </summary>
    [HttpGet("search-cari")]
    public async Task<IActionResult> SearchCari(string query)
    {
        if (string.IsNullOrEmpty(query)) return BadRequest(ApiResponse<object>.Fail("Arama parametresi boş olamaz."));

        try
        {
            var results = new List<object>();
            using var conn = new SqlConnection(ConnectionString);
            await conn.OpenAsync();

            var cmdText = "SELECT TOP 10 cari_kod, cari_unvan1 FROM CARI_HESAPLAR WHERE cari_kod LIKE @q OR cari_unvan1 LIKE @q";
            using var cmd = new SqlCommand(cmdText, conn);
            cmd.Parameters.AddWithValue("@q", $"%{query}%");

            using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                results.Add(new
                {
                    cariKodu = reader.GetString(0),
                    cariAdi = reader.GetString(1)
                });
            }

            return Ok(ApiResponse<List<object>>.Ok(results, "Cari arama tamamlandı."));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[API] Cari arama hatası.");
            return StatusCode(500, ApiResponse<object>.Fail($"SQL Veritabanı hatası: {ex.Message}"));
        }
    }

    /// <summary>
    /// İnceleme sonrası onaylanan dekont verilerinden Mikro 010401 uyumlu Excel dosyası üretir.
    /// </summary>
    [HttpPost("export-excel")]
    public IActionResult ExportExcel([FromBody] ExportExcelRequest request)
    {
        try
        {
            using var workbook = new XLWorkbook();
            var worksheet = workbook.Worksheets.Add("Mikro_010401_Aktarim");

            worksheet.Cell(1, 1).Value = "Fatura_Tarihi";
            worksheet.Cell(1, 2).Value = "Belge_No";
            worksheet.Cell(1, 3).Value = "Fatura_No";
            worksheet.Cell(1, 4).Value = "Cari_Kodu";
            worksheet.Cell(1, 5).Value = "Cari_VKN";
            worksheet.Cell(1, 6).Value = "Cari_Adi";
            worksheet.Cell(1, 7).Value = "Tutar";
            worksheet.Cell(1, 8).Value = "Vergi_Tutar";
            worksheet.Cell(1, 9).Value = "Açıklama";
            worksheet.Cell(1, 10).Value = "Hareket_Kodu";
            worksheet.Cell(1, 11).Value = "Hareket_Adi";
            worksheet.Cell(1, 12).Value = "Evrak_Tipi";
            worksheet.Cell(1, 13).Value = "Acik_Kapali";

            var headerRow = worksheet.Row(1);
            headerRow.Style.Font.Bold = true;
            headerRow.Style.Fill.BackgroundColor = XLColor.FromHtml("#0ea5e9");
            headerRow.Style.Font.FontColor = XLColor.White;

            int rowIdx = 2;
            foreach (var line in request.Lines)
            {
                worksheet.Cell(rowIdx, 1).Value = request.Tarih;
                worksheet.Cell(rowIdx, 2).Value = request.EvrakNo;
                worksheet.Cell(rowIdx, 3).Value = request.BelgeNo;
                worksheet.Cell(rowIdx, 4).Value = request.CariKodu;
                worksheet.Cell(rowIdx, 5).Value = request.Vkn;
                worksheet.Cell(rowIdx, 6).Value = request.CariAdi;
                worksheet.Cell(rowIdx, 7).Value = line.Tutar;
                worksheet.Cell(rowIdx, 8).Value = line.Tutar * (line.KdvOrani / 100.0);
                worksheet.Cell(rowIdx, 9).Value = line.Aciklama ?? request.CariAdi + " Hizmet/Stok Bedeli";
                worksheet.Cell(rowIdx, 10).Value = line.Kodu;
                worksheet.Cell(rowIdx, 11).Value = line.Ismi;
                worksheet.Cell(rowIdx, 12).Value = "e-Fatura";
                worksheet.Cell(rowIdx, 13).Value = request.OdemeTipi == "Peşin" ? "Kapalı" : "Açık";

                worksheet.Cell(rowIdx, 7).Style.NumberFormat.Format = "#,##0.00";
                worksheet.Cell(rowIdx, 8).Style.NumberFormat.Format = "#,##0.00";
                rowIdx++;
            }

            worksheet.Columns().AdjustToContents();

            using var stream = new MemoryStream();
            workbook.SaveAs(stream);
            var content = stream.ToArray();

            return File(
                content,
                "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
                $"Mikro_010401_Aktarim_{request.BelgeNo}.xlsx"
            );
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[API] Excel Aktarım Üretim hatası.");
            return StatusCode(500, ApiResponse<object>.Fail($"Excel oluşturma hatası: {ex.Message}"));
        }
    }

    /// <summary>
    /// Onaylanan dekontu veritabanındaki Dekonts tablosuna kaydeder (Alış veya Satış olarak işaretleyerek).
    /// </summary>
    [HttpPost("confirm")]
    public async Task<IActionResult> Confirm([FromBody] ConfirmDekontRequest request)
    {
        _logger.LogInformation("[API] Dekont kaydetme isteği alındı. Cari: {Cari}, Belge: {BelgeNo}, Tip: {Tip}", 
            request.CariAdi, request.BelgeNo, request.FaturaTipi);

        try
        {
            if (string.IsNullOrEmpty(request.BelgeNo))
            {
                return BadRequest(ApiResponse<object>.Fail("Belge numarası boş olamaz."));
            }

            DateTime parsedDate = DateTime.TryParse(request.Tarih, out var d) ? d : DateTime.Today;

            foreach (var line in request.Lines)
            {
                var dekont = new Models.Dekont
                {
                    HesapNo = request.CariKodu,
                    Tarih = parsedDate,
                    DekontNo = request.BelgeNo,
                    KarsiTaraf = request.CariAdi,
                    Tutar = (decimal)line.Tutar,
                    Masraf = (decimal)(line.Tutar * (line.KdvOrani / 100.0)),
                    Aciklama = line.Ismi,
                    FaturaTipi = request.FaturaTipi, // "Alis" veya "Satis"
                    KaydedenKullanici = request.CreatedBy,
                    CreatedDate = DateTime.Now
                };
                _context.Dekonts.Add(dekont);
            }

            // Sistem Logu kaydet
            _context.SystemLogs.Add(new SystemLog
            {
                Username = request.CreatedBy,
                ActionType = $"Confirm_Dekont_{request.FaturaTipi}",
                Status = "SUCCESS",
                Details = $"Dekont kaydedildi ({request.FaturaTipi}): {request.CariAdi} - Toplam Tutar: {request.Lines.Sum(l => l.Tutar)}"
            });

            await _context.SaveChangesAsync();
            return Ok(ApiResponse<object>.Ok(null, $"{request.FaturaTipi == "Alis" ? "Alış" : "Satış"} faturası veritabanına başarıyla kaydedildi."));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[API] Dekont kaydetme hatası.");
            return StatusCode(500, ApiResponse<object>.Fail($"Veri kaydetme hatası: {ex.Message}"));
        }
    }

    /// <summary>
    /// Alış faturaları tablosunu Excel olarak indirir.
    /// </summary>
    [HttpGet("export-alis")]
    public async Task<IActionResult> ExportAlis([FromQuery] string? username)
    {
        try
        {
            List<Models.Dekont> query;
            if (string.IsNullOrEmpty(username))
            {
                query = await _context.Dekonts.Where(d => d.FaturaTipi == "Alis").OrderByDescending(d => d.Tarih).ToListAsync();
            }
            else
            {
                query = await _context.Dekonts.Where(d => d.FaturaTipi == "Alis" && d.KaydedenKullanici == username).OrderByDescending(d => d.Tarih).ToListAsync();
            }

            var fileBytes = GenerateExportExcel(query, "Alis_Fatura_Aktarim");
            return File(fileBytes, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", "Alis_Fatura_Aktarim.xlsx");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[API] Alis export hatası.");
            return StatusCode(500, "Alış faturaları indirme hatası: " + ex.Message);
        }
    }

    /// <summary>
    /// Satış faturaları tablosunu Excel olarak indirir.
    /// </summary>
    [HttpGet("export-satis")]
    public async Task<IActionResult> ExportSatis([FromQuery] string? username)
    {
        try
        {
            List<Models.Dekont> query;
            if (string.IsNullOrEmpty(username))
            {
                query = await _context.Dekonts.Where(d => d.FaturaTipi == "Satis").OrderByDescending(d => d.Tarih).ToListAsync();
            }
            else
            {
                query = await _context.Dekonts.Where(d => d.FaturaTipi == "Satis" && d.KaydedenKullanici == username).OrderByDescending(d => d.Tarih).ToListAsync();
            }

            var fileBytes = GenerateExportExcel(query, "Satis_Fatura_Aktarim");
            return File(fileBytes, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", "Satis_Fatura_Aktarim.xlsx");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[API] Satis export hatası.");
            return StatusCode(500, "Satış faturaları indirme hatası: " + ex.Message);
        }
    }

    private byte[] GenerateExportExcel(List<Models.Dekont> dekonts, string sheetName)
    {
        using var workbook = new XLWorkbook();
        var worksheet = workbook.Worksheets.Add(sheetName);

        worksheet.Cell(1, 1).Value = "Fatura_Tarihi";
        worksheet.Cell(1, 2).Value = "Belge_No";
        worksheet.Cell(1, 3).Value = "Cari_Kodu";
        worksheet.Cell(1, 4).Value = "Cari_Adi";
        worksheet.Cell(1, 5).Value = "Tutar (KDV Haric)";
        worksheet.Cell(1, 6).Value = "KDV_Tutar";
        worksheet.Cell(1, 7).Value = "Hizmet_Stok_Ismi";
        worksheet.Cell(1, 8).Value = "Kaydeden_Kullanici";

        var headerRow = worksheet.Row(1);
        headerRow.Style.Font.Bold = true;
        headerRow.Style.Fill.BackgroundColor = XLColor.FromHtml("#0ea5e9");
        headerRow.Style.Font.FontColor = XLColor.White;

        int rowIdx = 2;
        foreach (var d in dekonts)
        {
            worksheet.Cell(rowIdx, 1).Value = d.Tarih.ToString("yyyy-MM-dd");
            worksheet.Cell(rowIdx, 2).Value = d.DekontNo ?? "";
            worksheet.Cell(rowIdx, 3).Value = d.HesapNo ?? "";
            worksheet.Cell(rowIdx, 4).Value = d.KarsiTaraf;
            worksheet.Cell(rowIdx, 5).Value = d.Tutar;
            worksheet.Cell(rowIdx, 6).Value = d.Masraf;
            worksheet.Cell(rowIdx, 7).Value = d.Aciklama ?? "";
            worksheet.Cell(rowIdx, 8).Value = d.KaydedenKullanici;

            worksheet.Cell(rowIdx, 5).Style.NumberFormat.Format = "#,##0.00";
            worksheet.Cell(rowIdx, 6).Style.NumberFormat.Format = "#,##0.00";
            rowIdx++;
        }

        worksheet.Columns().AdjustToContents();

        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        return stream.ToArray();
    }

    private async Task<(string cariKodu, string cariAdi, bool isValid)> CheckCariInMikroDbAsync(string vkn)
    {
        if (string.IsNullOrEmpty(vkn)) return ("", "", false);

        try
        {
            using var conn = new SqlConnection(ConnectionString);
            await conn.OpenAsync();

            var cmdText = "SELECT TOP 1 cari_kod, cari_unvan1 FROM CARI_HESAPLAR WHERE cari_vkn = @vkn OR cari_tckn = @vkn";
            using var cmd = new SqlCommand(cmdText, conn);
            cmd.Parameters.AddWithValue("@vkn", vkn);

            using var reader = await cmd.ExecuteReaderAsync();
            if (await reader.ReadAsync())
            {
                return (reader.GetString(0), reader.GetString(1), true);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning("[API] Veritabanından Cari sorgulanırken hata (Güvenli atlama yapıldı): {Msg}", ex.Message);
        }

        return ("", "", false);
    }

    private string GenerateSimpleHtmlInvoice(string belgeNo, string tarih, string vkn, string cariAdi, List<ParsedInvoiceLine> lines, double subtotal, double tax)
    {
        var rows = string.Join("\n", lines.Select((x, i) => $@"
            <tr>
                <td style='padding: 10px; border-bottom: 1px solid #eee;'>{i+1}</td>
                <td style='padding: 10px; border-bottom: 1px solid #eee;'>{x.Ismi}</td>
                <td style='padding: 10px; border-bottom: 1px solid #eee; text-align: right;'>% {x.KdvOrani}</td>
                <td style='padding: 10px; border-bottom: 1px solid #eee; text-align: right;'>₺ {x.Tutar:N2}</td>
            </tr>
        "));

        return $@"
            <div style='font-family: Arial, sans-serif; max-width: 600px; margin: 0 auto; color: #333; line-height: 1.5;'>
                <div style='display: flex; justify-content: space-between; border-bottom: 2px solid #0ea5e9; padding-bottom: 15px;'>
                    <div>
                        <h2 style='color: #0ea5e9; margin: 0;'>Uyumsoft e-Fatura</h2>
                        <p style='margin: 5px 0 0 0; font-size: 0.9rem;'>Belge No: <strong>{belgeNo}</strong></p>
                    </div>
                    <div style='text-align: right;'>
                        <p style='margin: 0; font-size: 0.9rem;'>Tarih: <strong>{tarih}</strong></p>
                    </div>
                </div>
                
                <div style='margin: 20px 0; background-color: #f8fafc; padding: 15px; border-radius: 8px;'>
                    <h4 style='margin: 0 0 8px 0; color: #475569;'>Fatura Düzenleyen (Cari):</h4>
                    <p style='margin: 0; font-size: 1rem; font-weight: bold;'>{cariAdi}</p>
                    <p style='margin: 4px 0 0 0; font-size: 0.85rem; color: #64748b;'>VKN / TCKN: {vkn}</p>
                </div>

                <table style='width: 100%; border-collapse: collapse; margin-top: 10px; font-size: 0.9rem;'>
                    <thead>
                        <tr style='background-color: #f1f5f9; font-weight: bold;'>
                            <th style='padding: 10px; text-align: left;'>No</th>
                            <th style='padding: 10px; text-align: left;'>Hizmet/Stok Açıklaması</th>
                            <th style='padding: 10px; text-align: right;'>KDV</th>
                            <th style='padding: 10px; text-align: right;'>Tutar (KDV Hariç)</th>
                        </tr>
                    </thead>
                    <tbody>
                        {rows}
                    </tbody>
                </table>

                <div style='margin-top: 20px; border-top: 2px solid #eee; padding-top: 10px; display: flex; justify-content: flex-end;'>
                    <div style='width: 250px; font-size: 0.9rem;'>
                        <div style='display: flex; justify-content: space-between; padding: 4px 0;'>
                            <span>Ara Toplam:</span>
                            <span>₺ {subtotal:N2}</span>
                        </div>
                        <div style='display: flex; justify-content: space-between; padding: 4px 0;'>
                            <span>Toplam KDV:</span>
                            <span>₺ {tax:N2}</span>
                        </div>
                        <div style='display: flex; justify-content: space-between; padding: 6px 0; border-top: 1px solid #eee; font-weight: bold; font-size: 1.05rem; color: #0ea5e9;'>
                            <span>Genel Toplam:</span>
                            <span>₺ {(subtotal + tax):N2}</span>
                        </div>
                    </div>
                </div>
            </div>
        ";
    }
}

public class ConfirmDekontRequest
{
    public string EvrakNo { get; set; } = string.Empty;
    public string BelgeNo { get; set; } = string.Empty;
    public string Tarih { get; set; } = string.Empty;
    public string OdemeTipi { get; set; } = string.Empty;
    public string Vkn { get; set; } = string.Empty;
    public string CariKodu { get; set; } = string.Empty;
    public string CariAdi { get; set; } = string.Empty;
    public string FaturaTipi { get; set; } = string.Empty; // "Alis" veya "Satis"
    public string CreatedBy { get; set; } = string.Empty;
    public List<ParsedInvoiceLine> Lines { get; set; } = new();
}
