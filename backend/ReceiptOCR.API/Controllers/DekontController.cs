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
using ReceiptOCR.API.Services;

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

    private readonly GeminiService _geminiService;
    private readonly IExcelQueueService _excelQueueService;

    public DekontController(IConfiguration configuration, ILogger<DekontController> logger, ReceiptDbContext context, GeminiService geminiService, IExcelQueueService excelQueueService)
    {
        _configuration = configuration;
        _logger = logger;
        _context = context;
        _geminiService = geminiService;
        _excelQueueService = excelQueueService;

        // Otomatik tablo güncelleme - Kolon kontrolü ve ekleme
        try
        {
            using var conn = new SqlConnection(ConnectionString);
            conn.Open();
            using var cmd = new SqlCommand(@"
                IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID('Dekonts') AND name = 'FaturaTipi')
                BEGIN
                    ALTER TABLE Dekonts ADD FaturaTipi NVARCHAR(50) NULL;
                END
                IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID('Dekonts') AND name = 'CariVkn')
                BEGIN
                    ALTER TABLE Dekonts ADD CariVkn NVARCHAR(50) NULL;
                END
                IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID('Dekonts') AND name = 'MalzemeHizmetKodu')
                BEGIN
                    ALTER TABLE Dekonts ADD MalzemeHizmetKodu NVARCHAR(100) NULL;
                END
                IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID('Dekonts') AND name = 'Miktar')
                BEGIN
                    ALTER TABLE Dekonts ADD Miktar FLOAT NOT NULL DEFAULT 1.0;
                END
                IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID('Dekonts') AND name = 'BirimFiyat')
                BEGIN
                    ALTER TABLE Dekonts ADD BirimFiyat DECIMAL(18,2) NOT NULL DEFAULT 0.0;
                END
                IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID('Dekonts') AND name = 'KdvOrani')
                BEGIN
                    ALTER TABLE Dekonts ADD KdvOrani FLOAT NOT NULL DEFAULT 0.0;
                END
                IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID('Dekonts') AND name = 'OdenecekTutar')
                BEGIN
                    ALTER TABLE Dekonts ADD OdenecekTutar DECIMAL(18,2) NOT NULL DEFAULT 0.0;
                END
                IF NOT EXISTS (SELECT * FROM sys.objects WHERE object_id = OBJECT_ID(N'[dbo].[CARI_HESAPLAR]') AND type in (N'U'))
                BEGIN
                    CREATE TABLE CARI_HESAPLAR (
                        cari_kod NVARCHAR(50) PRIMARY KEY,
                        cari_unvan1 NVARCHAR(250) NOT NULL,
                        cari_vkn NVARCHAR(50) NULL,
                        cari_tckn NVARCHAR(50) NULL,
                        cari_Doviz_Cinsi INT NULL,
                        cari_created_date DATETIME NULL
                    );
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
    public async Task<IActionResult> ParseXml(IFormFile file, [FromForm] string? myCompanyName)
    {
        _logger.LogInformation("[API] Uyumsoft XML Çözümleme isteği alındı. Dosya: {FileName}", file?.FileName);

        if (file == null || file.Length == 0)
            return BadRequest(ApiResponse<object>.Fail("Lütfen geçerli bir dosya yükleyiniz."));

        try
        {
            var ext = Path.GetExtension(file.FileName).ToLowerInvariant();
            if (ext == ".pdf" || ext == ".jpg" || ext == ".jpeg" || ext == ".png")
            {
                // PDF veya Görsel Çözümleme - Gemini API kullanarak
                using var ms = new MemoryStream();
                await file.CopyToAsync(ms);
                var fileBytes = ms.ToArray();

                string mimeType = "application/pdf";
                if (ext == ".png") mimeType = "image/png";
                else if (ext == ".jpg" || ext == ".jpeg") mimeType = "image/jpeg";

                _logger.LogInformation("Gemini ile {Ext} belgesi çözümleniyor...", ext);
                var scanResult = await _geminiService.ScanDekontAsync(fileBytes, mimeType);

                if (scanResult == null)
                {
                    return BadRequest(ApiResponse<object>.Fail("Gemini API dekont/fatura verilerini okuyamadı."));
                }

                // Tarihi yyyy-MM-dd formatına çevirelim
                string formattedDate = DateTime.Today.ToString("yyyy-MM-dd");
                if (!string.IsNullOrEmpty(scanResult.Tarih) && DateTime.TryParse(scanResult.Tarih, out var parsedDate))
                {
                    formattedDate = parsedDate.ToString("yyyy-MM-dd");
                }

                // Alış/Satış Tespiti
                string detectedType = "Alis";
                string resolvedCariAdi = "";
                string resolvedVkn = "";
                string saticiUnvan = scanResult.SaticiUnvan ?? "";
                string aliciUnvan = scanResult.AliciUnvan ?? "";
                string saticiVkn = scanResult.SaticiVkn ?? "";
                string aliciVkn = scanResult.AliciVkn ?? "";

                if (!string.IsNullOrEmpty(myCompanyName))
                {
                    var myCompLower = myCompanyName.Trim().ToLowerInvariant();
                    if (saticiUnvan.ToLowerInvariant().Contains(myCompLower))
                    {
                        detectedType = "Satis";
                        resolvedCariAdi = aliciUnvan;
                        resolvedVkn = aliciVkn;
                    }
                    else
                    {
                        detectedType = "Alis";
                        resolvedCariAdi = saticiUnvan;
                        resolvedVkn = saticiVkn;
                    }
                }
                else
                {
                    resolvedCariAdi = scanResult.KarsiTaraf ?? "Bilinmeyen Cari";
                    resolvedVkn = saticiVkn;
                }

                if (string.IsNullOrEmpty(resolvedCariAdi))
                {
                    resolvedCariAdi = scanResult.KarsiTaraf ?? "Bilinmeyen Cari";
                }

                // Cariyi bulma
                string pdfCariKodu = "";
                bool pdfIsCariValid = false;

                if (!string.IsNullOrEmpty(resolvedVkn))
                {
                    var lookupVkn = await CheckCariInMikroDbAsync(resolvedVkn);
                    if (lookupVkn.isValid)
                    {
                        pdfCariKodu = lookupVkn.cariKodu;
                        resolvedCariAdi = lookupVkn.cariAdi;
                        pdfIsCariValid = true;
                    }
                }

                if (!pdfIsCariValid && !string.IsNullOrEmpty(resolvedCariAdi))
                {
                    var lookupName = await CheckCariByNameInMikroDbAsync(resolvedCariAdi);
                    if (lookupName.isValid)
                    {
                        pdfCariKodu = lookupName.cariKodu;
                        resolvedCariAdi = lookupName.cariAdi;
                        pdfIsCariValid = true;
                    }
                }

                var pdfLines = new List<ParsedInvoiceLine>();
                if (scanResult.FaturaSatirlari != null && scanResult.FaturaSatirlari.Count > 0)
                {
                    foreach (var line in scanResult.FaturaSatirlari)
                    {
                        pdfLines.Add(new ParsedInvoiceLine
                        {
                            Cinsi = "Hizmet",
                            Kodu = line.MalzemeHizmetKodu ?? "",
                            Ismi = line.MalzemeHizmetAdi ?? "Hizmet Bedeli",
                            Tutar = (double)line.NetTutar,
                            KdvOrani = line.KdvOrani,
                            Miktar = line.Miktar,
                            BirimFiyat = (double)line.BirimFiyat,
                            GrossTotal = (double)line.GrossTotal,
                            Iskonto = (double)line.Iskonto,
                            KdvTutari = (double)line.KdvTutari,
                            NetTutar = (double)line.NetTutar,
                            VergilerDahilToplam = (double)line.VergilerDahilToplam
                        });
                    }
                }
                else
                {
                    pdfLines.Add(new ParsedInvoiceLine
                    { 
                        Cinsi = "Hizmet", 
                        Kodu = "", 
                        Ismi = !string.IsNullOrEmpty(scanResult.Aciklama) ? scanResult.Aciklama : "Banka Transfer Bedeli", 
                        Tutar = (double)(scanResult.Tutar ?? 0.00m), 
                        KdvOrani = 0,
                        Miktar = 1,
                        BirimFiyat = (double)(scanResult.Tutar ?? 0.00m),
                        GrossTotal = (double)(scanResult.Tutar ?? 0.00m),
                        Iskonto = 0,
                        KdvTutari = 0,
                        NetTutar = (double)(scanResult.Tutar ?? 0.00m),
                        VergilerDahilToplam = (double)(scanResult.Tutar ?? 0.00m)
                    });
                }

                double pdfAraToplam = 0;
                double pdfKdvToplam = 0;
                foreach (var line in pdfLines)
                {
                    pdfAraToplam += line.NetTutar;
                    pdfKdvToplam += line.KdvTutari;
                }
                double CalculatedTotal = pdfAraToplam + pdfKdvToplam;

                // Basit bir HTML şablonu oluşturalım
                string tableRowsHtml = "";
                int itemIndex = 1;
                foreach (var line in pdfLines)
                {
                    tableRowsHtml += $@"
                            <tr>
                                <td style='padding: 10px; border-bottom: 1px solid #eee;'>{itemIndex++}</td>
                                <td style='padding: 10px; border-bottom: 1px solid #eee;'>{line.Ismi}</td>
                                <td style='padding: 10px; border-bottom: 1px solid #eee; text-align: right;'>% {line.KdvOrani:F0}</td>
                                <td style='padding: 10px; border-bottom: 1px solid #eee; text-align: right;'>₺ {line.NetTutar:N2}</td>
                            </tr>";
                }

                string pdfHtmlTemplate = $@"
                <div style='font-family: Arial, sans-serif; max-width: 600px; margin: 0 auto; color: #333; line-height: 1.5;'>
                    <div style='display: flex; justify-content: space-between; border-bottom: 2px solid #8b5cf6; padding-bottom: 15px;'>
                        <div>
                            <h2 style='color: #8b5cf6; margin: 0;'>Belge Önizleme Detayı</h2>
                            <p style='margin: 5px 0 0 0; font-size: 0.9rem;'>Belge No: <strong>{scanResult.DekontNo ?? "Belirtilmemiş"}</strong></p>
                        </div>
                        <div style='text-align: right;'>
                            <p style='margin: 0; font-size: 0.9rem;'>Belge Tarihi: <strong>{formattedDate}</strong></p>
                        </div>
                    </div>
                    
                    <div style='margin: 20px 0; background-color: #f8fafc; padding: 15px; border-radius: 8px;'>
                        <h4 style='margin: 0 0 8px 0; color: #475569;'>Banka Hesap / IBAN / Cari Detay:</h4>
                        <p style='margin: 0; font-size: 1rem; font-weight: bold;'>{scanResult.HesapNo ?? "Belirtilmemiş"}</p>
                    </div>

                    <div style='margin: 20px 0; background-color: #f8fafc; padding: 15px; border-radius: 8px;'>
                        <h4 style='margin: 0 0 8px 0; color: #475569;'>Müşteri / Tedarikçi (Karşı Taraf):</h4>
                        <p style='margin: 0; font-size: 1rem; font-weight: bold;'>{resolvedCariAdi}</p>
                        <p style='margin: 4px 0 0 0; font-size: 0.85rem; color: #64748b;'>Mikro Cari Kodu: {(!string.IsNullOrEmpty(pdfCariKodu) ? pdfCariKodu : "Kaydı Yok")}</p>
                    </div>

                    <table style='width: 100%; border-collapse: collapse; margin-top: 10px; font-size: 0.9rem;'>
                        <thead>
                            <tr style='background-color: #f1f5f9; font-weight: bold;'>
                                <th style='padding: 10px; text-align: left;'>No</th>
                                <th style='padding: 10px; text-align: left;'>Açıklama</th>
                                <th style='padding: 10px; text-align: right;'>KDV</th>
                                <th style='padding: 10px; text-align: right;'>Tutar</th>
                            </tr>
                        </thead>
                        <tbody>
                            {tableRowsHtml}
                        </tbody>
                    </table>

                    <div style='margin-top: 20px; border-top: 2px solid #eee; padding-top: 10px; display: flex; justify-content: flex-end;'>
                        <div style='width: 250px; font-size: 0.9rem;'>
                            <div style='display: flex; justify-content: space-between; padding: 4px 0;'>
                                <span>Ara Toplam (KDV Hariç):</span>
                                <span>₺ {pdfAraToplam:N2}</span>
                            </div>
                            <div style='display: flex; justify-content: space-between; padding: 4px 0;'>
                                <span>Hesaplanan KDV:</span>
                                <span>₺ {pdfKdvToplam:N2}</span>
                            </div>
                            <div style='display: flex; justify-content: space-between; padding: 6px 0; border-top: 1px solid #eee; font-weight: bold; font-size: 1.05rem; color: #8b5cf6;'>
                                <span>Genel Toplam:</span>
                                <span>₺ {CalculatedTotal:N2}</span>
                            </div>
                        </div>
                    </div>
                </div>";

                return Ok(ApiResponse<object>.Ok(new
                {
                    belgeNo = scanResult.DekontNo ?? "PDF-" + new Random().Next(100000, 999999),
                    tarih = formattedDate,
                    vkn = resolvedVkn,
                    cariAdi = resolvedCariAdi,
                    cariKodu = pdfCariKodu,
                    isCariValid = pdfIsCariValid,
                    invoiceLines = pdfLines,
                    araToplam = pdfAraToplam,
                    kdvToplam = pdfKdvToplam,
                    genelToplam = CalculatedTotal,
                    htmlContent = pdfHtmlTemplate,
                    detectedType = detectedType
                }, "PDF/Görsel başarıyla okundu."));
            }

            // XML Çözümleme (UBL-TR Standartı)
            using var stream = file.OpenReadStream();
            var doc = XDocument.Load(stream);
            XNamespace cbc = "urn:oasis:names:specification:ubl:schema:xsd:CommonBasicComponents-2";
            XNamespace cac = "urn:oasis:names:specification:ubl:schema:xsd:CommonAggregateComponents-2";

            // XML içinde gömülü PDF var mı kontrol edelim
            string? base64Pdf = null;
            var additionalDocs = doc.Root?.Elements(cac + "AdditionalDocumentReference");
            if (additionalDocs != null)
            {
                foreach (var ad in additionalDocs)
                {
                    var docType = ad.Element(cbc + "DocumentType")?.Value;
                    var binaryObject = ad.Element(cac + "Attachment")?.Element(cbc + "EmbeddedDocumentBinaryObject");
                    
                    if (binaryObject != null && (docType?.ToUpperInvariant() == "PDF" || binaryObject.Attribute("mimeCode")?.Value == "application/pdf"))
                    {
                        base64Pdf = binaryObject.Value?.Trim();
                        break;
                    }
                }
            }

            // Fatura ve Belge Bilgileri
            var belgeNo = doc.Root?.Element(cbc + "ID")?.Value ?? "";
            var tarihStr = doc.Root?.Element(cbc + "IssueDate")?.Value ?? "";
            
            // Satıcı (Supplier / Fatura Kesen) Bilgileri
            var supplierParty = doc.Root?.Element(cac + "AccountingSupplierParty")?.Element(cac + "Party");
            var supplierVkn = supplierParty?.Elements(cac + "PartyIdentification")
                .FirstOrDefault(x => x.Element(cbc + "ID")?.Attribute("schemeID")?.Value == "VKN" 
                                  || x.Element(cbc + "ID")?.Attribute("schemeID")?.Value == "TCKN")
                ?.Element(cbc + "ID")?.Value ?? "";
            var supplierName = supplierParty?.Element(cac + "PartyName")?.Element(cbc + "Name")?.Value 
                ?? supplierParty?.Element(cac + "PartyLegalEntity")?.Element(cbc + "RegistrationName")?.Value 
                ?? "";

            // Alıcı (Customer / Fatura Kesilen) Bilgileri
            var customerParty = doc.Root?.Element(cac + "AccountingCustomerParty")?.Element(cac + "Party");
            var customerVkn = customerParty?.Elements(cac + "PartyIdentification")
                .FirstOrDefault(x => x.Element(cbc + "ID")?.Attribute("schemeID")?.Value == "VKN" 
                                  || x.Element(cbc + "ID")?.Attribute("schemeID")?.Value == "TCKN")
                ?.Element(cbc + "ID")?.Value ?? "";
            var customerName = customerParty?.Element(cac + "PartyName")?.Element(cbc + "Name")?.Value 
                ?? customerParty?.Element(cac + "PartyLegalEntity")?.Element(cbc + "RegistrationName")?.Value 
                ?? "";

            // Alış/Satış Tespiti
            string detectedTypeXml = "Alis";
            string resolvedCariAdiXml = "";
            string resolvedVknXml = "";

            if (!string.IsNullOrEmpty(myCompanyName))
            {
                var myCompLower = myCompanyName.Trim().ToLowerInvariant();
                if (supplierName.ToLowerInvariant().Contains(myCompLower))
                {
                    detectedTypeXml = "Satis";
                    resolvedCariAdiXml = customerName;
                    resolvedVknXml = customerVkn;
                }
                else
                {
                    detectedTypeXml = "Alis";
                    resolvedCariAdiXml = supplierName;
                    resolvedVknXml = supplierVkn;
                }
            }
            else
            {
                resolvedCariAdiXml = supplierName;
                resolvedVknXml = supplierVkn;
            }

            // Satırlar Çözümleme
            var lines = new List<ParsedInvoiceLine>();
            var lineElements = doc.Root?.Elements(cac + "InvoiceLine") ?? Enumerable.Empty<XElement>();
            
            double calculatedAraToplam = 0;
            double calculatedKdvToplam = 0;

            foreach (var el in lineElements)
            {
                var itemCode = "";
                var itemName = el.Element(cac + "Item")?.Element(cac + "Name")?.Value ?? "Hizmet Satırı";
                var qtyStr = el.Element(cbc + "InvoicedQuantity")?.Value ?? "1";
                var priceStr = el.Element(cac + "Price")?.Element(cbc + "PriceAmount")?.Value ?? "0";
                var lineAmountStr = el.Element(cbc + "LineExtensionAmount")?.Value ?? "0"; 
                var percentStr = el.Element(cac + "TaxTotal")?.Element(cac + "TaxSubtotal")?.Element(cbc + "Percent")?.Value ?? "20";

                double.TryParse(qtyStr, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out double qty);
                double.TryParse(priceStr, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out double price);
                double.TryParse(lineAmountStr, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out double netTutar);
                double.TryParse(percentStr, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out double taxPercent);

                // Iskonto bulma
                double iskonto = 0;
                var allowance = el.Element(cac + "AllowanceCharge");
                if (allowance != null)
                {
                    var chargeIndicator = allowance.Element(cbc + "ChargeIndicator")?.Value;
                    if (chargeIndicator == "false" || chargeIndicator == "0")
                    {
                        var allowanceAmountStr = allowance.Element(cbc + "Amount")?.Value;
                        double.TryParse(allowanceAmountStr, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out iskonto);
                    }
                }

                double grossTotal = qty * price;
                if (grossTotal == 0) grossTotal = netTutar + iskonto;

                // KDV Tutarını oku veya hesapla
                var taxAmountStr = el.Element(cac + "TaxTotal")?.Element(cbc + "TaxAmount")?.Value;
                double.TryParse(taxAmountStr, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out double taxAmount);
                if (taxAmount == 0) taxAmount = netTutar * (taxPercent / 100);

                double grandTotal = netTutar + taxAmount;

                lines.Add(new ParsedInvoiceLine
                {
                    Cinsi = "Hizmet",
                    Kodu = itemCode,
                    Ismi = itemName,
                    Miktar = qty,
                    BirimFiyat = price,
                    KdvOrani = taxPercent,
                    GrossTotal = grossTotal,
                    Iskonto = iskonto,
                    KdvTutari = taxAmount,
                    NetTutar = netTutar,
                    VergilerDahilToplam = grandTotal,
                    Tutar = netTutar
                });

                calculatedAraToplam += netTutar;
                calculatedKdvToplam += taxAmount;
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
                    Kodu = "",
                    Ismi = "Uyumsoft Genel Hizmet Bedeli",
                    Miktar = 1,
                    BirimFiyat = calculatedAraToplam,
                    KdvOrani = 20,
                    GrossTotal = calculatedAraToplam,
                    Iskonto = 0,
                    KdvTutari = calculatedKdvToplam,
                    NetTutar = calculatedAraToplam,
                    VergilerDahilToplam = calculatedAraToplam + calculatedKdvToplam,
                    Tutar = calculatedAraToplam
                });
            }

            // Mikro SQL Veritabanında VKN Sorgulama
            var (cariKodu, matchedCariAdi, isCariValid) = await CheckCariInMikroDbAsync(resolvedVknXml);

            // Eğer veritabanından eşleşen ünvan geldiyse arayüzde onu göster
            if (!string.IsNullOrEmpty(matchedCariAdi))
            {
                resolvedCariAdiXml = matchedCariAdi;
            }

            // XML'i görsel olarak arayüzde render etmek için HTML formatına çevirme şablonu (Simüle edilmiş basit şablon)
            var htmlTemplate = GenerateSimpleHtmlInvoice(belgeNo, tarihStr, resolvedVknXml, resolvedCariAdiXml, lines, calculatedAraToplam, calculatedKdvToplam);

            var result = new
            {
                belgeNo,
                tarih = tarihStr,
                vkn = resolvedVknXml,
                cariAdi = resolvedCariAdiXml,
                cariKodu,
                isCariValid,
                invoiceLines = lines,
                araToplam = Math.Round(calculatedAraToplam, 2),
                kdvToplam = Math.Round(calculatedKdvToplam, 2),
                genelToplam = Math.Round(calculatedAraToplam + calculatedKdvToplam, 2),
                htmlContent = htmlTemplate,
                pdfContent = base64Pdf,
                detectedType = detectedTypeXml
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
    /// Excel dosyasından (Cari Adı, VKN/TCKN) cari kartı listesi çözümler ve Mikro CARI_HESAPLAR tablosuna kaydeder.
    /// </summary>
    [HttpPost("parse-excel-cari")]
    public async Task<IActionResult> ParseExcelCari(IFormFile file)
    {
        _logger.LogInformation("[API] Excel Cari Yükleme isteği alındı. Dosya: {FileName}", file?.FileName);

        if (file == null || file.Length == 0)
            return BadRequest(ApiResponse<object>.Fail("Lütfen geçerli bir dosya yükleyiniz."));

        var ext = Path.GetExtension(file.FileName).ToLowerInvariant();
        if (ext != ".xlsx" && ext != ".xls")
        {
            return BadRequest(ApiResponse<object>.Fail("Yalnızca Excel (.xlsx, .xls) dosyaları desteklenmektedir."));
        }

        try
        {
            using var stream = file.OpenReadStream();
            using var workbook = new XLWorkbook(stream);
            var worksheet = workbook.Worksheets.FirstOrDefault();
            if (worksheet == null)
            {
                return BadRequest(ApiResponse<object>.Fail("Excel dosyasında çalışma sayfası bulunamadı."));
            }

            var rows = worksheet.RowsUsed().ToList();
            if (rows.Count <= 1)
            {
                return BadRequest(ApiResponse<object>.Fail("Excel dosyasında veri satırı bulunamadı."));
            }

            // Kolon başlıklarını tespit etmeye çalış
            var firstRow = rows.First();
            int kodCol = -1;
            int unvanCol = -1;

            // İlk satırı tarayarak başlıkları eşleştir
            foreach (var cell in firstRow.Cells())
            {
                var val = cell.Value.ToString().Trim().ToLowerInvariant();
                if (val.Contains("kod"))
                {
                    kodCol = cell.Address.ColumnNumber;
                }
                else if (val.Contains("unvan") || val.Contains("ünvan") || val.Contains("ad") || val.Contains("cari"))
                {
                    unvanCol = cell.Address.ColumnNumber;
                }
            }

            // Başlık bulunamadıysa varsayılan sütun atamaları (1: Kod, 2: Unvan)
            if (kodCol == -1) kodCol = 1;
            if (unvanCol == -1) unvanCol = 2;

            int addedCount = 0;
            var addedCaris = new List<object>();

            using var conn = new SqlConnection(ConnectionString);
            await conn.OpenAsync();

            // İkinci satırdan itibaren oku
            for (int r = 2; r <= rows.Count; r++)
            {
                var row = worksheet.Row(r);
                var rawCariKod = row.Cell(kodCol).Value.ToString().Trim();
                var rawCariAdi = row.Cell(unvanCol).Value.ToString().Trim();

                if (string.IsNullOrEmpty(rawCariKod) || string.IsNullOrEmpty(rawCariAdi))
                    continue;

                // Başlıkların yanlışlıkla okunmasını engelle (e.g. "cari kodu" metnini satır olarak ekleme)
                if (rawCariKod.ToLowerInvariant().Contains("kod") || rawCariAdi.ToLowerInvariant().Contains("ünvan") || rawCariAdi.ToLowerInvariant().Contains("unvan"))
                    continue;

                // 2. Mikro'da bu cari_kod veya cari_unvan1 var mı kontrol et (Mükerrer kaydı önle)
                var checkCmdText = "SELECT COUNT(*) FROM CARI_HESAPLAR WHERE cari_kod = @kod OR LTRIM(RTRIM(UPPER(cari_unvan1))) = LTRIM(RTRIM(UPPER(@unvan)))";
                using var checkCmd = new SqlCommand(checkCmdText, conn);
                checkCmd.Parameters.AddWithValue("@kod", rawCariKod);
                checkCmd.Parameters.AddWithValue("@unvan", rawCariAdi.Trim());
                var exists = (int)(await checkCmd.ExecuteScalarAsync() ?? 0) > 0;

                if (!exists)
                {
                    // Ekle
                    var insertCmdText = @"
                        INSERT INTO CARI_HESAPLAR (cari_kod, cari_unvan1, cari_vkn, cari_tckn, cari_Doviz_Cinsi, cari_created_date) 
                        VALUES (@kod, @unvan, NULL, NULL, 0, GETDATE())";
                    
                    using (var insertCmd = new SqlCommand(insertCmdText, conn))
                    {
                        insertCmd.Parameters.AddWithValue("@kod", rawCariKod);
                        insertCmd.Parameters.AddWithValue("@unvan", rawCariAdi);

                        await insertCmd.ExecuteNonQueryAsync();
                    }

                    addedCount++;
                    addedCaris.Add(new { cariKodu = rawCariKod, cariAdi = rawCariAdi });
                }
            }

            return Ok(ApiResponse<object>.Ok(new { addedCount = addedCount, addedCaris = addedCaris }, $"{addedCount} adet yeni cari kartı başarıyla yüklendi ve oluşturuldu."));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Excel cari listesi yüklenirken hata oluştu.");
            return StatusCode(500, ApiResponse<object>.Fail("Excel dosyası okunurken bir hata oluştu: " + ex.Message));
        }
    }

    /// <summary>
    /// Mikro SQL veritabanında yeni cari hesabı açar.
    /// </summary>
    [HttpPost("create-cari")]
    public async Task<IActionResult> CreateCari([FromBody] CreateCariRequest request)
    {
        if (string.IsNullOrEmpty(request.CariAdi))
        {
            return BadRequest(ApiResponse<object>.Fail("Cari Unvanı zorunludur."));
        }

        try
        {
            using var conn = new SqlConnection(ConnectionString);
            await conn.OpenAsync();

            // 1. Ünvan mükerrer kontrolü (Case-insensitive, Trimmed)
            var checkNameCmdText = "SELECT COUNT(*) FROM CARI_HESAPLAR WHERE LTRIM(RTRIM(UPPER(cari_unvan1))) = LTRIM(RTRIM(UPPER(@unvan)))";
            using (var checkNameCmd = new SqlCommand(checkNameCmdText, conn))
            {
                checkNameCmd.Parameters.AddWithValue("@unvan", request.CariAdi.Trim());
                var nameExists = (int)(await checkNameCmd.ExecuteScalarAsync() ?? 0) > 0;
                if (nameExists)
                {
                    return BadRequest(ApiResponse<object>.Fail("Bu Cari Ünvanı ile kayıtlı bir cari zaten var."));
                }
            }

            // 2. VKN/TCKN mükerrer kontrolü (eğer girilmişse)
            if (!string.IsNullOrEmpty(request.Vkn))
            {
                var cleanVkn = new string(request.Vkn.Where(char.IsDigit).ToArray());
                if (!string.IsNullOrEmpty(cleanVkn))
                {
                    var checkVknCmdText = "SELECT COUNT(*) FROM CARI_HESAPLAR WHERE cari_vkn = @vkn OR cari_tckn = @vkn";
                    using (var checkVknCmd = new SqlCommand(checkVknCmdText, conn))
                    {
                        checkVknCmd.Parameters.AddWithValue("@vkn", cleanVkn);
                        var vknExists = (int)(await checkVknCmd.ExecuteScalarAsync() ?? 0) > 0;
                        if (vknExists)
                        {
                            return BadRequest(ApiResponse<object>.Fail("Bu VKN/TCKN ile kayıtlı bir cari zaten var."));
                        }
                    }
                }
            }

            // 3. Son kullanılan Cari kodunu alarak yeni bir kod üret (Örn: 120.01.XXX)
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

            // 4. Mikro CARI_HESAPLAR tablosuna yeni cariyi ekle
            var insertCmdText = @"
                INSERT INTO CARI_HESAPLAR (cari_kod, cari_unvan1, cari_vkn, cari_tckn, cari_Doviz_Cinsi, cari_created_date) 
                VALUES (@kod, @unvan, @vkn, @tckn, 0, GETDATE())";

            using (var insertCmd = new SqlCommand(insertCmdText, conn))
            {
                insertCmd.Parameters.AddWithValue("@kod", nextCariKod);
                insertCmd.Parameters.AddWithValue("@unvan", request.CariAdi.Trim());
                if (string.IsNullOrEmpty(request.Vkn))
                {
                    insertCmd.Parameters.AddWithValue("@vkn", DBNull.Value);
                    insertCmd.Parameters.AddWithValue("@tckn", DBNull.Value);
                }
                else
                {
                    var cleanVkn = new string(request.Vkn.Where(char.IsDigit).ToArray());
                    if (cleanVkn.Length == 11)
                    {
                        insertCmd.Parameters.AddWithValue("@vkn", DBNull.Value);
                        insertCmd.Parameters.AddWithValue("@tckn", cleanVkn);
                    }
                    else
                    {
                        insertCmd.Parameters.AddWithValue("@vkn", cleanVkn);
                        insertCmd.Parameters.AddWithValue("@tckn", DBNull.Value);
                    }
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
    /// Mikro veritabanındaki tüm cari kayıtlarını listeler.
    /// </summary>
    [HttpGet("list-caris")]
    public async Task<IActionResult> ListCaris([FromQuery] int limit = 100)
    {
        if (limit <= 0) limit = 100;
        if (limit > 5000) limit = 5000;

        try
        {
            var results = new List<object>();
            using var conn = new SqlConnection(ConnectionString);
            await conn.OpenAsync();

            var cmdText = $"SELECT TOP {limit} cari_kod, cari_unvan1, ISNULL(cari_vkn, ISNULL(cari_tckn, '')) AS vkn FROM CARI_HESAPLAR ORDER BY cari_created_date DESC";
            using var cmd = new SqlCommand(cmdText, conn);

            using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                results.Add(new
                {
                    cariKodu = reader.IsDBNull(0) ? "" : reader.GetString(0),
                    cariAdi = reader.IsDBNull(1) ? "" : reader.GetString(1),
                    vkn = reader.IsDBNull(2) ? "" : reader.GetString(2)
                });
            }

            return Ok(ApiResponse<List<object>>.Ok(results, "Cari kayıtları listelendi."));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[API] Cari kayıtlarını listeleme hatası.");
            return Ok(ApiResponse<List<object>>.Ok(new List<object>(), "Cari tablosu bulunamadı."));
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

            worksheet.Cell(1, 1).Value = "Fatura Tarihi";
            worksheet.Cell(1, 2).Value = "Cari Kodu";
            worksheet.Cell(1, 3).Value = "Cari Ünvanı";
            worksheet.Cell(1, 4).Value = "Cari VKN";
            worksheet.Cell(1, 5).Value = "Belge No";
            worksheet.Cell(1, 6).Value = "Malzeme/Hizmet Kod";
            worksheet.Cell(1, 7).Value = "Hizmet Açıklaması";
            worksheet.Cell(1, 8).Value = "Miktar";
            worksheet.Cell(1, 9).Value = "Birim Fiyat";
            worksheet.Cell(1, 10).Value = "KDV Oranı";
            worksheet.Cell(1, 11).Value = "KDV Tutarı";
            worksheet.Cell(1, 12).Value = "Net Tutar";
            worksheet.Cell(1, 13).Value = "Ödenecek Tutar";
            worksheet.Cell(1, 14).Value = "Evrak Tipi";

            var headerRow = worksheet.Row(1);
            headerRow.Style.Font.Bold = true;
            headerRow.Style.Fill.BackgroundColor = XLColor.FromHtml("#0ea5e9");
            headerRow.Style.Font.FontColor = XLColor.White;

            int rowIdx = 2;
            foreach (var line in request.Lines)
            {
                worksheet.Cell(rowIdx, 1).Value = request.Tarih;
                worksheet.Cell(rowIdx, 2).Value = request.CariKodu;
                worksheet.Cell(rowIdx, 3).Value = request.CariAdi;
                worksheet.Cell(rowIdx, 4).Value = request.Vkn;
                worksheet.Cell(rowIdx, 5).Value = request.BelgeNo;
                worksheet.Cell(rowIdx, 6).Value = line.Kodu;
                worksheet.Cell(rowIdx, 7).Value = line.Ismi;
                worksheet.Cell(rowIdx, 8).Value = line.Miktar;
                worksheet.Cell(rowIdx, 9).Value = line.BirimFiyat;
                worksheet.Cell(rowIdx, 10).Value = line.KdvOrani + "%";
                worksheet.Cell(rowIdx, 11).Value = line.KdvTutari >= 0 ? line.KdvTutari : (line.Tutar * (line.KdvOrani / 100.0));
                worksheet.Cell(rowIdx, 12).Value = line.NetTutar > 0 ? line.NetTutar : line.Tutar;
                worksheet.Cell(rowIdx, 13).Value = line.VergilerDahilToplam > 0 ? line.VergilerDahilToplam : (line.NetTutar + line.KdvTutari);
                worksheet.Cell(rowIdx, 14).Value = request.FaturaTipi == "Satis" ? "Çıktı" : "Girdi";

                worksheet.Cell(rowIdx, 8).Style.NumberFormat.Format = "#,##0.00";
                worksheet.Cell(rowIdx, 9).Style.NumberFormat.Format = "#,##0.00";
                worksheet.Cell(rowIdx, 11).Style.NumberFormat.Format = "#,##0.00";
                worksheet.Cell(rowIdx, 12).Style.NumberFormat.Format = "#,##0.00";
                worksheet.Cell(rowIdx, 13).Style.NumberFormat.Format = "#,##0.00";
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
            var savedDekonts = new List<Models.Dekont>();

            foreach (var line in request.Lines)
            {
                var dekont = new Models.Dekont
                {
                    HesapNo = request.CariKodu,
                    Tarih = parsedDate,
                    DekontNo = request.BelgeNo,
                    KarsiTaraf = request.CariAdi,
                    Tutar = (decimal)(line.NetTutar > 0 ? line.NetTutar : line.Tutar),
                    Masraf = (decimal)(line.KdvTutari >= 0 ? line.KdvTutari : (line.Tutar * (line.KdvOrani / 100.0))),
                    Aciklama = line.Ismi,
                    FaturaTipi = request.FaturaTipi, // "Alis" veya "Satis"
                    KaydedenKullanici = request.CreatedBy,
                    CreatedDate = DateTime.Now,
                    CariVkn = request.Vkn,
                    MalzemeHizmetKodu = line.Kodu,
                    Miktar = line.Miktar,
                    BirimFiyat = (decimal)line.BirimFiyat,
                    KdvOrani = line.KdvOrani,
                    OdenecekTutar = (decimal)(line.VergilerDahilToplam > 0 ? line.VergilerDahilToplam : (line.NetTutar + line.KdvTutari))
                };
                _context.Dekonts.Add(dekont);
                savedDekonts.Add(dekont);
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

            // Queue Excel writing after successful DB save
            foreach (var sDekont in savedDekonts)
            {
                _excelQueueService.QueueWriteDekont(sDekont, "ADD");
            }
            return Ok(ApiResponse<object>.Ok(null, $"{(request.FaturaTipi == "Alis" ? "Alış" : "Satış")} faturası veritabanına başarıyla kaydedildi."));
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

        worksheet.Cell(1, 1).Value = "Fatura Tarihi";
        worksheet.Cell(1, 2).Value = "Cari Kodu";
        worksheet.Cell(1, 3).Value = "Cari Ünvanı";
        worksheet.Cell(1, 4).Value = "Cari VKN";
        worksheet.Cell(1, 5).Value = "Belge No";
        worksheet.Cell(1, 6).Value = "Malzeme/Hizmet Kod";
        worksheet.Cell(1, 7).Value = "Hizmet Açıklaması";
        worksheet.Cell(1, 8).Value = "Miktar";
        worksheet.Cell(1, 9).Value = "Birim Fiyat";
        worksheet.Cell(1, 10).Value = "KDV Oranı";
        worksheet.Cell(1, 11).Value = "KDV Tutarı";
        worksheet.Cell(1, 12).Value = "Net Tutar";
        worksheet.Cell(1, 13).Value = "Ödenecek Tutar";
        worksheet.Cell(1, 14).Value = "Evrak Tipi";

        var headerRow = worksheet.Row(1);
        headerRow.Style.Font.Bold = true;
        headerRow.Style.Fill.BackgroundColor = XLColor.FromHtml("#0ea5e9");
        headerRow.Style.Font.FontColor = XLColor.White;

        int rowIdx = 2;
        foreach (var d in dekonts)
        {
            worksheet.Cell(rowIdx, 1).Value = d.Tarih.ToString("yyyy-MM-dd");
            worksheet.Cell(rowIdx, 2).Value = d.HesapNo ?? "";
            worksheet.Cell(rowIdx, 3).Value = d.KarsiTaraf ?? "";
            worksheet.Cell(rowIdx, 4).Value = d.CariVkn ?? "";
            worksheet.Cell(rowIdx, 5).Value = d.DekontNo ?? "";
            worksheet.Cell(rowIdx, 6).Value = d.MalzemeHizmetKodu ?? "";
            worksheet.Cell(rowIdx, 7).Value = d.Aciklama ?? "";
            worksheet.Cell(rowIdx, 8).Value = d.Miktar;
            worksheet.Cell(rowIdx, 9).Value = d.BirimFiyat;
            worksheet.Cell(rowIdx, 10).Value = d.KdvOrani + "%";
            worksheet.Cell(rowIdx, 11).Value = d.Masraf; // Kdv Tutari
            worksheet.Cell(rowIdx, 12).Value = d.Tutar; // Net Tutar
            worksheet.Cell(rowIdx, 13).Value = d.OdenecekTutar;
            worksheet.Cell(rowIdx, 14).Value = d.FaturaTipi == "Satis" ? "Çıktı" : "Girdi";

            worksheet.Cell(rowIdx, 8).Style.NumberFormat.Format = "#,##0.00";
            worksheet.Cell(rowIdx, 9).Style.NumberFormat.Format = "#,##0.00";
            worksheet.Cell(rowIdx, 11).Style.NumberFormat.Format = "#,##0.00";
            worksheet.Cell(rowIdx, 12).Style.NumberFormat.Format = "#,##0.00";
            worksheet.Cell(rowIdx, 13).Style.NumberFormat.Format = "#,##0.00";
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

    private async Task<(string cariKodu, string cariAdi, bool isValid)> CheckCariByNameInMikroDbAsync(string cariName)
    {
        if (string.IsNullOrEmpty(cariName)) return ("", "", false);

        try
        {
            using var conn = new SqlConnection(ConnectionString);
            await conn.OpenAsync();

            var cmdText = "SELECT TOP 1 cari_kod, cari_unvan1 FROM CARI_HESAPLAR WHERE cari_unvan1 LIKE @name";
            using var cmd = new SqlCommand(cmdText, conn);
            cmd.Parameters.AddWithValue("@name", $"%{cariName}%");

            using var reader = await cmd.ExecuteReaderAsync();
            if (await reader.ReadAsync())
            {
                return (reader.GetString(0), reader.GetString(1), true);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning("[API] Veritabanından Cari isme göre sorgulanırken hata (Güvenli atlama yapıldı): {Msg}", ex.Message);
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

public class CreateCariRequest
{
    public string Vkn { get; set; } = string.Empty;
    public string CariAdi { get; set; } = string.Empty;
}

public class ExportExcelRequest
{
    public string EvrakNo { get; set; } = string.Empty;
    public string BelgeNo { get; set; } = string.Empty;
    public string Tarih { get; set; } = string.Empty;
    public string OdemeTipi { get; set; } = string.Empty;
    public string Vkn { get; set; } = string.Empty;
    public string CariKodu { get; set; } = string.Empty;
    public string CariAdi { get; set; } = string.Empty;
    public List<ParsedInvoiceLine> Lines { get; set; } = new();
    public double AraToplam { get; set; }
    public double KdvToplam { get; set; }
    public double GenelToplam { get; set; }
    public string Kullanici { get; set; } = string.Empty;
    public string FaturaTipi { get; set; } = string.Empty;
}

public class ParsedInvoiceLine
{
    public string Cinsi { get; set; } = "Hizmet";
    public string Kodu { get; set; } = string.Empty;
    public string Ismi { get; set; } = string.Empty;
    public double Tutar { get; set; }
    public double KdvOrani { get; set; }
    public double Miktar { get; set; } = 1;
    public double BirimFiyat { get; set; }
    public double GrossTotal { get; set; }
    public double Iskonto { get; set; }
    public double KdvTutari { get; set; }
    public double NetTutar { get; set; }
    public double VergilerDahilToplam { get; set; }
    public string? Aciklama { get; set; }
}
