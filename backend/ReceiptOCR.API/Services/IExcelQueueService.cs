using System;
using System.Threading.Channels;
using Microsoft.Extensions.Logging;
using ReceiptOCR.API.Models;

namespace ReceiptOCR.API.Services
{
    public class ExcelQueueItem
    {
        public int ExpenseId { get; set; }
        public string FirmaAdi { get; set; } = string.Empty;
        public string? FisNo { get; set; }
        public string? VknTckn { get; set; }
        public int KdvOrani { get; set; }
        public decimal Matrah { get; set; }
        public decimal KdvTutari { get; set; }
        public decimal ToplamTutar { get; set; }
        public decimal FisinGenelToplami { get; set; }
        public string KaydedenKullanici { get; set; } = string.Empty;
        public DateTime Tarih { get; set; }
        public string Action { get; set; } = "ADD"; // "ADD" or "UPDATE" or "DELETE"
        
        // Dekont alanları
        public string? ItemType { get; set; } = "EXPENSE"; // "EXPENSE" or "DEKONT"
        public string? HesapNo { get; set; }
        public string? DekontNo { get; set; }
        public string? KarsiTaraf { get; set; }
        public decimal Tutar { get; set; }
        public decimal Masraf { get; set; }
        public string? Aciklama { get; set; }
    }

    public interface IExcelQueueService
    {
        bool QueueWrite(Expense expense, string action = "ADD");
        bool QueueWriteDekont(Dekont dekont, string action = "ADD");
        ChannelReader<ExcelQueueItem> Reader { get; }
    }

    public class ExcelQueueService : IExcelQueueService
    {
        private readonly Channel<ExcelQueueItem> _channel;
        private readonly ILogger<ExcelQueueService> _logger;

        public ExcelQueueService(ILogger<ExcelQueueService> logger)
        {
            _logger = logger;
            var options = new BoundedChannelOptions(1000)
            {
                FullMode = BoundedChannelFullMode.Wait,
                SingleWriter = false,
                SingleReader = true
            };
            _channel = Channel.CreateBounded<ExcelQueueItem>(options);
        }

        public ChannelReader<ExcelQueueItem> Reader => _channel.Reader;

        public bool QueueWrite(Expense expense, string action = "ADD")
        {
            var item = new ExcelQueueItem
            {
                ItemType = "EXPENSE",
                ExpenseId = expense.Id,
                FirmaAdi = expense.FirmaAdi,
                FisNo = expense.FisNo,
                VknTckn = expense.VknTckn,
                KdvOrani = expense.KdvOrani,
                Matrah = expense.Matrah,
                KdvTutari = expense.KdvTutari,
                ToplamTutar = expense.ToplamTutar,
                FisinGenelToplami = expense.FisinGenelToplami,
                KaydedenKullanici = expense.KaydedenKullanici,
                Tarih = expense.Tarih,
                Action = action
            };

            var success = _channel.Writer.TryWrite(item);
            if (success)
            {
                _logger.LogInformation("Excel yazma islemi kuyruga eklendi: ExpenseId {ExpenseId}, Firma: {FirmaAdi}, Islem: {Action}", expense.Id, expense.FirmaAdi, action);
            }
            else
            {
                _logger.LogWarning("Excel kuyrugu dolu! Isleme alinamadi: ExpenseId {ExpenseId}", expense.Id);
            }
            return success;
        }

        public bool QueueWriteDekont(Dekont dekont, string action = "ADD")
        {
            var item = new ExcelQueueItem
            {
                ItemType = "DEKONT",
                ExpenseId = dekont.Id,
                HesapNo = dekont.HesapNo,
                Tarih = dekont.Tarih,
                DekontNo = dekont.DekontNo,
                KarsiTaraf = dekont.KarsiTaraf,
                Tutar = dekont.Tutar,
                Masraf = dekont.Masraf,
                Aciklama = dekont.Aciklama,
                KaydedenKullanici = dekont.KaydedenKullanici,
                Action = action
            };

            var success = _channel.Writer.TryWrite(item);
            if (success)
            {
                _logger.LogInformation("Excel dekont yazma islemi kuyruga eklendi: DekontId {DekontId}, Karsi Taraf: {KarsiTaraf}, Islem: {Action}", dekont.Id, dekont.KarsiTaraf, action);
            }
            else
            {
                _logger.LogWarning("Excel kuyrugu dolu! Dekont isleme alinamadi: DekontId {DekontId}", dekont.Id);
            }
            return success;
        }
    }
}
