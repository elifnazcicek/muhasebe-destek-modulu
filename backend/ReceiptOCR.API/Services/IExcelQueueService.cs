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
        public string Action { get; set; } = "ADD"; // "ADD" or "UPDATE"
    }

    public interface IExcelQueueService
    {
        bool QueueWrite(Expense expense, string action = "ADD");
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
    }
}
