using System.Drawing;
using System.Drawing.Imaging;

namespace ReceiptOCR.API.Services;

/// <summary>
/// Görüntü Ön İşleme Servisi (Backend Tarafı)
/// 
/// KRİTİK NOT:
/// Ana ön işleme (flip, kırpma, perspektif düzeltme) Angular tarafında
/// HTML5 Canvas ile yapılır. Bu servis, Angular'dan gelen "zaten kırpılmış"
/// görseli alıp aşağıdaki son optimizasyonları uygular:
/// 
/// 1. Boyut Kontrolü — Uzun kenar maks 1500px
/// 2. JPEG Sıkıştırma — %80 kalite ile dosya boyutunu küçültme
/// 3. Format Doğrulama — Geçerli görsel formatı kontrolü
/// 
/// Görüntü HER ZAMAN renkli (RGB) kalır. Grayscale/Binarization KULLANILMAZ.
/// </summary>
public class ImagePreprocessingService
{
    private readonly ILogger<ImagePreprocessingService> _logger;
    private readonly string _uploadDir;
    private readonly string _processedDir;

    private const int MAX_LONG_EDGE = 1500;
    private const long JPEG_QUALITY = 80L;

    public ImagePreprocessingService(ILogger<ImagePreprocessingService> logger)
    {
        _logger = logger;

        // Yükleme ve çıktı klasörlerini oluştur
        var baseDir = AppDomain.CurrentDomain.BaseDirectory;
        _uploadDir = Path.Combine(baseDir, "uploads");
        _processedDir = Path.Combine(baseDir, "processed");
        Directory.CreateDirectory(_uploadDir);
        Directory.CreateDirectory(_processedDir);

        _logger.LogInformation("[Pre-processing] Servis başlatıldı. Upload: {UploadDir}, Processed: {ProcessedDir}", 
            _uploadDir, _processedDir);
    }

    /// <summary>
    /// Yüklenen görseli kaydeder, boyut optimizasyonu ve JPEG sıkıştırması uygular.
    /// </summary>
    /// <param name="fileStream">Yüklenen dosyanın stream'i.</param>
    /// <param name="originalFileName">Orijinal dosya adı.</param>
    /// <returns>İşlem sonucu: orijinal ve işlenmiş dosya bilgileri.</returns>
    public async Task<PreprocessingResultInternal> ProcessAsync(Stream fileStream, string originalFileName)
    {
        var steps = new List<string>();
        var timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
        var uniqueId = Guid.NewGuid().ToString("N")[..8];

        // --- 1. Dosyayı Kaydet ---
        var ext = Path.GetExtension(originalFileName).ToLowerInvariant();
        var savedFileName = $"receipt_{timestamp}_{uniqueId}{ext}";
        var savedPath = Path.Combine(_uploadDir, savedFileName);

        using (var fs = new FileStream(savedPath, FileMode.Create))
        {
            await fileStream.CopyToAsync(fs);
        }

        var originalSizeKb = new System.IO.FileInfo(savedPath).Length / 1024.0;
        _logger.LogInformation("[Pre-processing] Dosya kaydedildi: {FileName} ({Size:F1} KB)", 
            savedFileName, originalSizeKb);

        // --- 2. Format Doğrulama ---
        Image originalImage;
        try
        {
            originalImage = Image.FromFile(savedPath);
            steps.Add("Format doğrulama: Geçerli görsel.");
            _logger.LogInformation("[Pre-processing] Format doğrulandı. Boyut: {W}x{H}", 
                originalImage.Width, originalImage.Height);
        }
        catch (Exception ex)
        {
            _logger.LogError("[Pre-processing] Geçersiz görsel formatı: {Error}", ex.Message);
            throw new InvalidOperationException($"Geçersiz görsel formatı: {ex.Message}");
        }

        // --- 3. Boyut Kontrolü ve Yeniden Boyutlandırma ---
        var resizedImage = ResizeIfNeeded(originalImage, steps);

        // --- 4. JPEG Sıkıştırma ---
        var processedFileName = $"receipt_{timestamp}_{uniqueId}_processed.jpg";
        var processedPath = Path.Combine(_processedDir, processedFileName);
        SaveAsJpeg(resizedImage, processedPath);
        steps.Add($"JPEG sıkıştırma: %{JPEG_QUALITY} kalite.");

        var processedSizeKb = new System.IO.FileInfo(processedPath).Length / 1024.0;
        var compressionRatio = (1 - processedSizeKb / originalSizeKb) * 100;

        _logger.LogInformation(
            "[Pre-processing] Pipeline tamamlandı. {OrigSize:F1} KB -> {ProcSize:F1} KB (%{Ratio:F0} küçülme)", 
            originalSizeKb, processedSizeKb, compressionRatio);

        // Kaynakları serbest bırak
        originalImage.Dispose();
        if (resizedImage != originalImage) resizedImage.Dispose();

        return new PreprocessingResultInternal
        {
            OriginalFileName = savedFileName,
            OriginalSizeKb = Math.Round(originalSizeKb, 1),
            ProcessedFileName = processedFileName,
            ProcessedSizeKb = Math.Round(processedSizeKb, 1),
            CompressionRatio = $"%{compressionRatio:F0} küçülme",
            AppliedSteps = steps
        };
    }

    /// <summary>
    /// Uzun kenar MAX_LONG_EDGE'i aşıyorsa oranı koruyarak küçültür.
    /// Görüntü RENKLİ (RGB) kalır.
    /// </summary>
    private Image ResizeIfNeeded(Image img, List<string> steps)
    {
        var longEdge = Math.Max(img.Width, img.Height);

        if (longEdge <= MAX_LONG_EDGE)
        {
            steps.Add($"Boyut uygun ({img.Width}x{img.Height}), resize atlandı.");
            _logger.LogInformation("[Pre-processing] Boyut zaten uygun ({W}x{H}), resize atlandı.", 
                img.Width, img.Height);
            return img;
        }

        var scale = (double)MAX_LONG_EDGE / longEdge;
        var newWidth = (int)(img.Width * scale);
        var newHeight = (int)(img.Height * scale);

        var resized = new Bitmap(newWidth, newHeight);
        using (var g = Graphics.FromImage(resized))
        {
            g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.HighQuality;
            g.DrawImage(img, 0, 0, newWidth, newHeight);
        }

        steps.Add($"Yeniden boyutlandırıldı: {img.Width}x{img.Height} -> {newWidth}x{newHeight}");
        _logger.LogInformation("[Pre-processing] Resize: {OldW}x{OldH} -> {NewW}x{NewH}", 
            img.Width, img.Height, newWidth, newHeight);

        return resized;
    }

    /// <summary>
    /// Görseli belirtilen kalitede JPEG olarak kaydeder.
    /// </summary>
    private void SaveAsJpeg(Image img, string outputPath)
    {
        var jpegCodec = ImageCodecInfo.GetImageEncoders()
            .First(c => c.FormatID == ImageFormat.Jpeg.Guid);

        var encoderParams = new EncoderParameters(1);
        encoderParams.Param[0] = new EncoderParameter(
            System.Drawing.Imaging.Encoder.Quality, JPEG_QUALITY);

        img.Save(outputPath, jpegCodec, encoderParams);
        _logger.LogInformation("[Pre-processing] JPEG kaydedildi: {Path}", outputPath);
    }

    /// <summary>Upload klasörünün yolunu döndürür.</summary>
    public string GetUploadDir() => _uploadDir;

    /// <summary>Processed klasörünün yolunu döndürür.</summary>
    public string GetProcessedDir() => _processedDir;
}

/// <summary>
/// Dahili işlem sonucu modeli (servis katmanı).
/// </summary>
public class PreprocessingResultInternal
{
    public string OriginalFileName { get; set; } = string.Empty;
    public double OriginalSizeKb { get; set; }
    public string ProcessedFileName { get; set; } = string.Empty;
    public double ProcessedSizeKb { get; set; }
    public string CompressionRatio { get; set; } = string.Empty;
    public List<string> AppliedSteps { get; set; } = new();
}
