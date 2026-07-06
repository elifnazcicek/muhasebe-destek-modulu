using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ClosedXML.Excel;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ReceiptOCR.API.Data;
using ReceiptOCR.API.Models;

namespace ReceiptOCR.API.Services
{
    public class ExcelBackgroundWorker : BackgroundService
    {
        private readonly IExcelQueueService _queueService;
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly ILogger<ExcelBackgroundWorker> _logger;

        public ExcelBackgroundWorker(
            IExcelQueueService queueService,
            IServiceScopeFactory scopeFactory,
            ILogger<ExcelBackgroundWorker> logger)
        {
            _queueService = queueService;
            _scopeFactory = scopeFactory;
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("Excel Arka Plan Yazici Servisi baslatildi.");

            await foreach (var item in _queueService.Reader.ReadAllAsync(stoppingToken))
            {
                _logger.LogInformation("Kuyruktan eleman alindi, Excel'e yaziliyor: ExpenseId {ExpenseId}", item.ExpenseId);
                
                bool written = false;
                int retryCount = 0;
                const int maxRetries = 10;
                
                while (!written && retryCount < maxRetries && !stoppingToken.IsCancellationRequested)
                {
                    try
                    {
                        await WriteToExcelAsync(item, stoppingToken);
                        written = true;
                        _logger.LogInformation("Excel'e basariyla yazildi: ExpenseId {ExpenseId}", item.ExpenseId);
                    }
                    catch (IOException ioEx)
                    {
                        retryCount++;
                        _logger.LogWarning(ioEx, "Excel dosyasi kilitli veya erisilemiyor. Yeniden denenecek ({RetryCount}/{MaxRetries})...", retryCount, maxRetries);
                        
                        await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Excel'e yazilirken beklenmeyen hata olustu. Kuyruktan atlanıyor: ExpenseId {ExpenseId}", item.ExpenseId);
                        
                        await LogErrorToDbAsync(item, ex);
                        break;
                    }
                }

                if (!stoppingToken.IsCancellationRequested && !written && retryCount >= maxRetries)
                {
                    _logger.LogError("Excel'e yazma islemi {MaxRetries} deneme sonrasinda basarisiz oldu. Islemi atliyoruz: ExpenseId {ExpenseId}", maxRetries, item.ExpenseId);
                    await LogErrorToDbAsync(item, new Exception($"Excel dosyası uzun süre kilitli kaldı. {maxRetries} deneme başarısız oldu."));
                }
            }
        }

        private async Task WriteToExcelAsync(ExcelQueueItem item, CancellationToken cancellationToken)
        {
            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ReceiptDbContext>();

            if (item.ItemType == "DEKONT")
            {
                await WriteDekontToExcelAsync(item, db, cancellationToken);
                return;
            }

            var excelPathSetting = await db.Settings.FirstOrDefaultAsync(s => s.Key == "ExcelPath", cancellationToken);
            string excelPath = excelPathSetting?.Value ?? @"C:\Muhasebe\Masraflar.xlsx";

            var directory = Path.GetDirectoryName(excelPath);
            if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }

            bool existsAndValid = File.Exists(excelPath);
            if (existsAndValid)
            {
                try
                {
                    using var testWb = new XLWorkbook(excelPath);
                }
                catch
                {
                    File.Delete(excelPath);
                    existsAndValid = false;
                }
            }

            // Excel dosyasını güvenli yazma ve yedekleme mekanizması (Safe-Write & Auto-Backup)
            string backupPath = excelPath + ".bak";
            if (existsAndValid)
            {
                try
                {
                    File.Copy(excelPath, backupPath, true);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Expense Excel yedek dosyasi olusturulamadi.");
                }
            }

            try
            {
                using var workbook = existsAndValid ? new XLWorkbook(excelPath) : new XLWorkbook();
                var worksheet = workbook.Worksheets.FirstOrDefault(w => w.Name == "Masraflar") ?? workbook.Worksheets.Add("Masraflar");

                if (!existsAndValid || worksheet.Cell(1, 1).Value.ToString() != "Tarih" || worksheet.Cell(1, 9).Value.ToString() != "Fişin Genel Toplamı")
                {
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
                }

                if (item.Action == "DELETE")
                {
                    int lastRowNumber = worksheet.LastRowUsed()?.RowNumber() ?? 1;
                    for (int r = lastRowNumber; r >= 2; r--)
                    {
                        var cellFirma = worksheet.Cell(r, 2).Value.ToString();
                        var cellFisNo = worksheet.Cell(r, 3).Value.ToString();
                        var cellKdvOrani = worksheet.Cell(r, 5).Value.ToString().Replace("%", "").Trim();
                        
                        if ((!string.IsNullOrEmpty(item.FisNo) && cellFisNo == item.FisNo && cellKdvOrani == item.KdvOrani.ToString()) ||
                            (cellFirma == item.FirmaAdi && worksheet.Cell(r, 1).Value.ToString() == item.Tarih.ToString("yyyy-MM-dd") && cellKdvOrani == item.KdvOrani.ToString()))
                        {
                            worksheet.Row(r).Delete();
                            _logger.LogInformation("Excel satiri silindi: Satir {Row}", r);
                        }
                    }
                }
                else if (item.Action == "UPDATE")
                {
                    bool rowFound = false;
                    int lastRowNumber = worksheet.LastRowUsed()?.RowNumber() ?? 1;
                    for (int r = 2; r <= lastRowNumber; r++)
                    {
                        var cellFirma = worksheet.Cell(r, 2).Value.ToString();
                        var cellFisNo = worksheet.Cell(r, 3).Value.ToString();
                        var cellKdvOrani = worksheet.Cell(r, 5).Value.ToString().Replace("%", "").Trim();
                        
                        if ((!string.IsNullOrEmpty(item.FisNo) && cellFisNo == item.FisNo && cellKdvOrani == item.KdvOrani.ToString()) ||
                            (cellFirma == item.FirmaAdi && worksheet.Cell(r, 1).Value.ToString() == item.Tarih.ToString("yyyy-MM-dd") && cellKdvOrani == item.KdvOrani.ToString()))
                        {
                            worksheet.Cell(r, 1).Value = item.Tarih.ToString("yyyy-MM-dd");
                            worksheet.Cell(r, 2).Value = item.FirmaAdi;
                            worksheet.Cell(r, 3).Value = item.FisNo ?? "";
                            worksheet.Cell(r, 4).Value = item.VknTckn ?? "";
                            worksheet.Cell(r, 5).Value = item.KdvOrani + "%";
                            worksheet.Cell(r, 6).Value = item.KdvTutari;
                            worksheet.Cell(r, 7).Value = item.ToplamTutar;
                            worksheet.Cell(r, 8).Value = item.Matrah;
                            worksheet.Cell(r, 9).Value = item.FisinGenelToplami;
                            worksheet.Cell(r, 10).Value = item.KaydedenKullanici;

                            worksheet.Cell(r, 6).Style.NumberFormat.Format = "0.00";
                            worksheet.Cell(r, 7).Style.NumberFormat.Format = "0.00";
                            worksheet.Cell(r, 8).Style.NumberFormat.Format = "0.00";
                            worksheet.Cell(r, 9).Style.NumberFormat.Format = "0.00";
                            rowFound = true;
                            _logger.LogInformation("Excel satiri guncellendi: Satir {Row}", r);
                            break;
                        }
                    }

                    if (!rowFound)
                    {
                        AppendRow(worksheet, item);
                    }
                }
                else
                {
                    AppendRow(worksheet, item);
                }

                worksheet.Columns().AdjustToContents();
                workbook.SaveAs(excelPath);

                // Başarılı kayıtta yedeği sil
                if (File.Exists(backupPath))
                {
                    File.Delete(backupPath);
                }
            }
            catch (Exception)
            {
                // Hata durumunda yedeği geri yükle
                if (File.Exists(backupPath))
                {
                    try
                    {
                        File.Copy(backupPath, excelPath, true);
                        File.Delete(backupPath);
                    }
                    catch {}
                }
                throw;
            }
        }

        private async Task WriteDekontToExcelAsync(ExcelQueueItem item, ReceiptDbContext db, CancellationToken cancellationToken)
        {
            var excelPathSetting = await db.Settings.FirstOrDefaultAsync(s => s.Key == "DekontExcelPath", cancellationToken);
            string excelPath = excelPathSetting?.Value ?? @"C:\Muhasebe\Dekontlar.xlsx";

            var directory = Path.GetDirectoryName(excelPath);
            if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }

            bool existsAndValid = File.Exists(excelPath);
            if (existsAndValid)
            {
                try
                {
                    using var testWb = new XLWorkbook(excelPath);
                }
                catch
                {
                    File.Delete(excelPath);
                    existsAndValid = false;
                }
            }

            // Excel dosyasını güvenli yazma ve yedekleme mekanizması (Safe-Write & Auto-Backup)
            string backupPath = excelPath + ".bak";
            if (existsAndValid)
            {
                try
                {
                    File.Copy(excelPath, backupPath, true);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Dekont Excel yedek dosyasi olusturulamadi.");
                }
            }

            try
            {
                using var workbook = existsAndValid ? new XLWorkbook(excelPath) : new XLWorkbook();
                var worksheet = workbook.Worksheets.FirstOrDefault(w => w.Name == "Dekontlar") ?? workbook.Worksheets.Add("Dekontlar");

                if (!existsAndValid || worksheet.Cell(1, 1).Value.ToString() != "Fatura Tarihi" || worksheet.Cell(1, 2).Value.ToString() != "Cari Kodu")
                {
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
                }

                if (item.Action == "DELETE")
                {
                    int lastRowNumber = worksheet.LastRowUsed()?.RowNumber() ?? 1;
                    for (int r = lastRowNumber; r >= 2; r--)
                    {
                        var cellDekontNo = worksheet.Cell(r, 5).Value.ToString();
                        var cellHesapNo = worksheet.Cell(r, 2).Value.ToString();
                        var cellTutar = worksheet.Cell(r, 12).Value.ToString();
                        
                        if ((!string.IsNullOrEmpty(item.DekontNo) && cellDekontNo == item.DekontNo) ||
                            (cellHesapNo == item.HesapNo && worksheet.Cell(r, 1).Value.ToString() == item.Tarih.ToString("yyyy-MM-dd") && cellTutar == item.Tutar.ToString()))
                        {
                            worksheet.Row(r).Delete();
                            _logger.LogInformation("Excel dekont satiri silindi: Satir {Row}", r);
                        }
                    }
                }
                else if (item.Action == "UPDATE")
                {
                    bool rowFound = false;
                    int lastRowNumber = worksheet.LastRowUsed()?.RowNumber() ?? 1;
                    for (int r = 2; r <= lastRowNumber; r++)
                    {
                        var cellDekontNo = worksheet.Cell(r, 5).Value.ToString();
                        var cellHesapNo = worksheet.Cell(r, 2).Value.ToString();
                        
                        if ((!string.IsNullOrEmpty(item.DekontNo) && cellDekontNo == item.DekontNo) ||
                            (cellHesapNo == item.HesapNo && worksheet.Cell(r, 1).Value.ToString() == item.Tarih.ToString("yyyy-MM-dd") && cellDekontNo == item.DekontNo))
                        {
                            worksheet.Cell(r, 1).Value = item.Tarih.ToString("yyyy-MM-dd");
                            worksheet.Cell(r, 2).Value = item.HesapNo ?? "";
                            worksheet.Cell(r, 3).Value = item.KarsiTaraf ?? "";
                            worksheet.Cell(r, 4).Value = item.CariVkn ?? "";
                            worksheet.Cell(r, 5).Value = item.DekontNo ?? "";
                            worksheet.Cell(r, 6).Value = item.MalzemeHizmetKodu ?? "";
                            worksheet.Cell(r, 7).Value = item.Aciklama ?? "";
                            worksheet.Cell(r, 8).Value = item.Miktar;
                            worksheet.Cell(r, 9).Value = item.BirimFiyat;
                            worksheet.Cell(r, 10).Value = item.KdvOraniDouble + "%";
                            worksheet.Cell(r, 11).Value = item.Masraf;
                            worksheet.Cell(r, 12).Value = item.Tutar;
                            worksheet.Cell(r, 13).Value = item.OdenecekTutar;
                            worksheet.Cell(r, 14).Value = item.FaturaTipi == "Satis" ? "Çıktı" : "Girdi";

                            worksheet.Cell(r, 8).Style.NumberFormat.Format = "0.00";
                            worksheet.Cell(r, 9).Style.NumberFormat.Format = "0.00";
                            worksheet.Cell(r, 11).Style.NumberFormat.Format = "0.00";
                            worksheet.Cell(r, 12).Style.NumberFormat.Format = "0.00";
                            worksheet.Cell(r, 13).Style.NumberFormat.Format = "0.00";

                            rowFound = true;
                            _logger.LogInformation("Excel dekont satiri guncellendi: Satir {Row}", r);
                            break;
                        }
                    }

                    if (!rowFound)
                    {
                        AppendDekontRow(worksheet, item);
                    }
                }
                else
                {
                    AppendDekontRow(worksheet, item);
                }

                worksheet.Columns().AdjustToContents();
                workbook.SaveAs(excelPath);

                // Başarılı kayıtta yedeği sil
                if (File.Exists(backupPath))
                {
                    File.Delete(backupPath);
                }
            }
            catch (Exception)
            {
                // Hata durumunda yedeği geri yükle
                if (File.Exists(backupPath))
                {
                    try
                    {
                        File.Copy(backupPath, excelPath, true);
                        File.Delete(backupPath);
                    }
                    catch {}
                }
                throw;
            }
        }

        private void AppendRow(IXLWorksheet worksheet, ExcelQueueItem item)
        {
            int lastRow = worksheet.LastRowUsed()?.RowNumber() ?? 1;
            int newRow = lastRow + 1;

            worksheet.Cell(newRow, 1).Value = item.Tarih.ToString("yyyy-MM-dd");
            worksheet.Cell(newRow, 2).Value = item.FirmaAdi;
            worksheet.Cell(newRow, 3).Value = item.FisNo ?? "";
            worksheet.Cell(newRow, 4).Value = item.VknTckn ?? "";
            worksheet.Cell(newRow, 5).Value = item.KdvOrani + "%";
            worksheet.Cell(newRow, 6).Value = item.KdvTutari;
            worksheet.Cell(newRow, 7).Value = item.ToplamTutar;
            worksheet.Cell(newRow, 8).Value = item.Matrah;
            worksheet.Cell(newRow, 9).Value = item.FisinGenelToplami;
            worksheet.Cell(newRow, 10).Value = item.KaydedenKullanici;

            worksheet.Cell(newRow, 6).Style.NumberFormat.Format = "0.00";
            worksheet.Cell(newRow, 7).Style.NumberFormat.Format = "0.00";
            worksheet.Cell(newRow, 8).Style.NumberFormat.Format = "0.00";
            worksheet.Cell(newRow, 9).Style.NumberFormat.Format = "0.00";
            _logger.LogInformation("Excel'e yeni satir eklendi: Satir {Row}", newRow);
        }

        private void AppendDekontRow(IXLWorksheet worksheet, ExcelQueueItem item)
        {
            int lastRow = worksheet.LastRowUsed()?.RowNumber() ?? 1;
            int newRow = lastRow + 1;

            worksheet.Cell(newRow, 1).Value = item.Tarih.ToString("yyyy-MM-dd");
            worksheet.Cell(newRow, 2).Value = item.HesapNo ?? "";
            worksheet.Cell(newRow, 3).Value = item.KarsiTaraf ?? "";
            worksheet.Cell(newRow, 4).Value = item.CariVkn ?? "";
            worksheet.Cell(newRow, 5).Value = item.DekontNo ?? "";
            worksheet.Cell(newRow, 6).Value = item.MalzemeHizmetKodu ?? "";
            worksheet.Cell(newRow, 7).Value = item.Aciklama ?? "";
            worksheet.Cell(newRow, 8).Value = item.Miktar;
            worksheet.Cell(newRow, 9).Value = item.BirimFiyat;
            worksheet.Cell(newRow, 10).Value = item.KdvOraniDouble + "%";
            worksheet.Cell(newRow, 11).Value = item.Masraf;
            worksheet.Cell(newRow, 12).Value = item.Tutar;
            worksheet.Cell(newRow, 13).Value = item.OdenecekTutar;
            worksheet.Cell(newRow, 14).Value = item.FaturaTipi == "Satis" ? "Çıktı" : "Girdi";

            worksheet.Cell(newRow, 8).Style.NumberFormat.Format = "0.00";
            worksheet.Cell(newRow, 9).Style.NumberFormat.Format = "0.00";
            worksheet.Cell(newRow, 11).Style.NumberFormat.Format = "0.00";
            worksheet.Cell(newRow, 12).Style.NumberFormat.Format = "0.00";
            worksheet.Cell(newRow, 13).Style.NumberFormat.Format = "0.00";
            _logger.LogInformation("Excel'e yeni dekont satiri eklendi: Satir {Row}", newRow);
        }

        private async Task LogErrorToDbAsync(ExcelQueueItem item, Exception ex)
        {
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<ReceiptDbContext>();
                
                db.ErrorLogs.Add(new ErrorLog
                {
                    Timestamp = DateTime.Now,
                    Username = item.KaydedenKullanici,
                    ActionType = $"ExcelBackgroundWorker_{item.Action}",
                    ErrorMessage = $"Excel dosyasına yazılamadı (ExpenseId: {item.ExpenseId}): {ex.Message}",
                    StackTrace = ex.StackTrace
                });
                await db.SaveChangesAsync();
            }
            catch (Exception dbEx)
            {
                _logger.LogError(dbEx, "Excel hatasi veritabanina kaydedilemedi!");
            }
        }
    }
}
