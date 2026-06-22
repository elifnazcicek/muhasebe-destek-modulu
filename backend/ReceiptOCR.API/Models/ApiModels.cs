namespace ReceiptOCR.API.Models;

/// <summary>
/// API'den dönen standart yanıt modeli.
/// Tüm endpoint'ler bu formatta yanıt döner.
/// </summary>
public class ApiResponse<T>
{
    /// <summary>İşlem başarılı mı?</summary>
    public bool Success { get; set; }

    /// <summary>Kullanıcıya gösterilecek mesaj.</summary>
    public string Message { get; set; } = string.Empty;

    /// <summary>Yanıt verisi (generic).</summary>
    public T? Data { get; set; }

    /// <summary>Hata varsa detay bilgisi.</summary>
    public string? Error { get; set; }

    public static ApiResponse<T> Ok(T data, string message = "İşlem başarılı.")
        => new() { Success = true, Data = data, Message = message };

    public static ApiResponse<T> Fail(string error)
        => new() { Success = false, Error = error, Message = "İşlem başarısız." };
}

/// <summary>
/// Görüntü ön işleme sonucu.
/// </summary>
public class PreprocessingResult
{
    /// <summary>Orijinal dosya bilgileri.</summary>
    public FileInfo Original { get; set; } = new();

    /// <summary>İşlenmiş dosya bilgileri.</summary>
    public FileInfo Processed { get; set; } = new();

    /// <summary>Sıkıştırma oranı (örn: "%73 küçülme").</summary>
    public string CompressionRatio { get; set; } = string.Empty;

    /// <summary>Uygulanan ön işleme adımları.</summary>
    public List<string> AppliedSteps { get; set; } = new();
}

/// <summary>
/// Dosya bilgisi modeli.
/// </summary>
public class FileInfo
{
    public string Filename { get; set; } = string.Empty;
    public double SizeKb { get; set; }
    public string? DownloadUrl { get; set; }
}

/// <summary>
/// Fiş/Fatura veri modeli — Gemini API'den parse edilen yapılandırılmış veri.
/// NOT: Bu modelin doldurulması Gemini API ekibinin sorumluluğundadır.
/// </summary>
public class ReceiptData
{
    public int Id { get; set; }
    public string DocumentType { get; set; } = "receipt"; // receipt, invoice, e-invoice

    // Tedarikçi bilgileri
    public string SupplierName { get; set; } = string.Empty;
    public string? TaxNumber { get; set; }

    // Fatura detayları
    public DateTime? InvoiceDate { get; set; }
    public string? InvoiceTime { get; set; }
    public string? ReceiptNo { get; set; }

    // Mali bilgiler
    public string Currency { get; set; } = "TRY";
    public decimal SubTotal { get; set; }
    public decimal TotalVat { get; set; }
    public decimal GrandTotal { get; set; }

    // KDV detayları
    public List<VatDetail> VatDetails { get; set; } = new();

    // Meta bilgiler
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public string? OriginalImagePath { get; set; }
    public string? ProcessedImagePath { get; set; }
}

/// <summary>
/// KDV oran detayı.
/// </summary>
public class VatDetail
{
    public int Rate { get; set; }      // %1, %10, %20
    public decimal Amount { get; set; } // KDV tutarı
}
