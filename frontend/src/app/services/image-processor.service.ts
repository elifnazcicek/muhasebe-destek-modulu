import { Injectable } from '@angular/core';

/**
 * Görüntü Ön İşleme Servisi (Tarayıcı Tarafı / Client-Side)
 *
 * KRİTİK: Gemini'nin renkli doku algısını bozmamak için
 * Grayscale, Binarization veya Thresholding KULLANILMAZ.
 * Görüntü her zaman renkli (RGB) kalır.
 *
 * Adımlar:
 * 1. Horizontal Flip (Webcam aynalama düzeltme)
 * 2. Document Crop (Belge kırpma — kullanıcı seçimi veya otomatik)
 * 3. Resize & JPEG Compression (Boyut optimizasyonu)
 */
@Injectable({ providedIn: 'root' })
export class ImageProcessorService {

  private readonly MAX_LONG_EDGE = 1500;
  private readonly JPEG_QUALITY = 0.80;

  /**
   * Video frame'ini yakalar ve ön işleme pipeline'ından geçirir.
   * @param video - Webcam <video> elementi
   * @param flipHorizontal - Webcam görüntüsü ise true (aynalama düzeltme)
   * @param cropRect - Kırpma alanı (null ise tüm frame kullanılır)
   * @returns İşlenmiş görüntünün Blob'u ve işlem logları
   */
  async processFrame(
    video: HTMLVideoElement,
    flipHorizontal: boolean = true,
    cropRect: CropRect | null = null
  ): Promise<ProcessingOutput> {
    const logs: string[] = [];
    const startTime = performance.now();

    // --- 1. Video frame'ini Canvas'a yakala ---
    const captureCanvas = document.createElement('canvas');
    captureCanvas.width = video.videoWidth;
    captureCanvas.height = video.videoHeight;
    const captureCtx = captureCanvas.getContext('2d')!;

    captureCtx.drawImage(video, 0, 0);
    logs.push(`Frame yakalandı: ${video.videoWidth}x${video.videoHeight}`);

    // --- 2. Horizontal Flip (Aynalama Düzeltme) ---
    if (flipHorizontal) {
      const flipCanvas = document.createElement('canvas');
      flipCanvas.width = captureCanvas.width;
      flipCanvas.height = captureCanvas.height;
      const flipCtx = flipCanvas.getContext('2d')!;

      flipCtx.translate(flipCanvas.width, 0);
      flipCtx.scale(-1, 1);
      flipCtx.drawImage(captureCanvas, 0, 0);

      // Flip sonucunu captureCanvas'a geri kopyala
      captureCtx.clearRect(0, 0, captureCanvas.width, captureCanvas.height);
      captureCtx.drawImage(flipCanvas, 0, 0);

      logs.push('Yatay aynalama (horizontal flip) uygulandı.');
    }

    // --- 3. Belge Kırpma (Document Crop) ---
    let workingCanvas = captureCanvas;
    if (cropRect) {
      const cropCanvas = document.createElement('canvas');
      cropCanvas.width = cropRect.width;
      cropCanvas.height = cropRect.height;
      const cropCtx = cropCanvas.getContext('2d')!;

      cropCtx.drawImage(
        captureCanvas,
        cropRect.x, cropRect.y, cropRect.width, cropRect.height,
        0, 0, cropRect.width, cropRect.height
      );

      workingCanvas = cropCanvas;
      logs.push(`Belge kırpıldı: (${cropRect.x},${cropRect.y}) ${cropRect.width}x${cropRect.height}`);
    } else {
      logs.push('Kırpma atlandı (cropRect belirtilmedi).');
    }

    // --- 4. Resize (Uzun Kenar Maks 1500px) ---
    const resizedCanvas = this.resizeIfNeeded(workingCanvas, logs);

    // --- 5. JPEG Sıkıştırma (%80 kalite) ---
    const blob = await this.canvasToJpegBlob(resizedCanvas);
    const sizeKb = blob.size / 1024;

    const elapsed = (performance.now() - startTime).toFixed(0);
    logs.push(`JPEG sıkıştırma: %${(this.JPEG_QUALITY * 100).toFixed(0)} kalite, ${sizeKb.toFixed(1)} KB`);
    logs.push(`Pipeline tamamlandı (${elapsed}ms).`);

    // Önizleme URL'si oluştur
    const previewUrl = URL.createObjectURL(blob);

    return {
      blob,
      previewUrl,
      logs,
      originalSize: { width: video.videoWidth, height: video.videoHeight },
      processedSize: { width: resizedCanvas.width, height: resizedCanvas.height },
      fileSizeKb: Math.round(sizeKb * 10) / 10
    };
  }

  /**
   * Dosya yükleme (drag-drop / file input) için ön işleme.
   * @param file - Kullanıcının seçtiği dosya
   * @param flipHorizontal - Dosya yükleme için genellikle false
   */
  async processFile(file: File, flipHorizontal: boolean = false): Promise<ProcessingOutput> {
    const logs: string[] = [];
    const startTime = performance.now();

    // Dosyayı Image olarak yükle
    const img = await this.loadImageFromFile(file);
    logs.push(`Dosya yüklendi: ${file.name} (${(file.size / 1024).toFixed(1)} KB, ${img.width}x${img.height})`);

    // Canvas'a çiz
    const canvas = document.createElement('canvas');
    canvas.width = img.width;
    canvas.height = img.height;
    const ctx = canvas.getContext('2d')!;

    if (flipHorizontal) {
      ctx.translate(canvas.width, 0);
      ctx.scale(-1, 1);
      logs.push('Yatay aynalama uygulandı.');
    }
    ctx.drawImage(img, 0, 0);

    // Resize
    const resizedCanvas = this.resizeIfNeeded(canvas, logs);

    // JPEG
    const blob = await this.canvasToJpegBlob(resizedCanvas);
    const sizeKb = blob.size / 1024;

    const elapsed = (performance.now() - startTime).toFixed(0);
    logs.push(`JPEG sıkıştırma: %${(this.JPEG_QUALITY * 100).toFixed(0)} kalite, ${sizeKb.toFixed(1)} KB`);
    logs.push(`Pipeline tamamlandı (${elapsed}ms).`);

    const previewUrl = URL.createObjectURL(blob);

    return {
      blob,
      previewUrl,
      logs,
      originalSize: { width: img.width, height: img.height },
      processedSize: { width: resizedCanvas.width, height: resizedCanvas.height },
      fileSizeKb: Math.round(sizeKb * 10) / 10
    };
  }

  // ===========================================================================
  // Yardımcı Metotlar
  // ===========================================================================

  /**
   * Uzun kenar MAX_LONG_EDGE'i aşarsa oranı koruyarak küçültür.
   */
  private resizeIfNeeded(source: HTMLCanvasElement, logs: string[]): HTMLCanvasElement {
    const longEdge = Math.max(source.width, source.height);

    if (longEdge <= this.MAX_LONG_EDGE) {
      logs.push(`Boyut uygun (${source.width}x${source.height}), resize atlandı.`);
      return source;
    }

    const scale = this.MAX_LONG_EDGE / longEdge;
    const newWidth = Math.round(source.width * scale);
    const newHeight = Math.round(source.height * scale);

    const resizedCanvas = document.createElement('canvas');
    resizedCanvas.width = newWidth;
    resizedCanvas.height = newHeight;
    const ctx = resizedCanvas.getContext('2d')!;
    ctx.drawImage(source, 0, 0, newWidth, newHeight);

    logs.push(`Yeniden boyutlandırıldı: ${source.width}x${source.height} → ${newWidth}x${newHeight}`);
    return resizedCanvas;
  }

  /**
   * Canvas'ı JPEG Blob'a dönüştürür.
   */
  private canvasToJpegBlob(canvas: HTMLCanvasElement): Promise<Blob> {
    return new Promise((resolve, reject) => {
      canvas.toBlob(
        (blob) => {
          if (blob) resolve(blob);
          else reject(new Error('Canvas to Blob dönüşümü başarısız.'));
        },
        'image/jpeg',
        this.JPEG_QUALITY
      );
    });
  }

  /**
   * File nesnesini HTMLImageElement'e yükler.
   */
  private loadImageFromFile(file: File): Promise<HTMLImageElement> {
    return new Promise((resolve, reject) => {
      const img = new Image();
      img.onload = () => {
        URL.revokeObjectURL(img.src);
        resolve(img);
      };
      img.onerror = () => reject(new Error('Görsel yüklenemedi.'));
      img.src = URL.createObjectURL(file);
    });
  }
}

// ===========================================================================
// Arayüz Tanımları (Interfaces)
// ===========================================================================

/** Kırpma alanı koordinatları */
export interface CropRect {
  x: number;
  y: number;
  width: number;
  height: number;
}

/** Boyut bilgisi */
export interface ImageSize {
  width: number;
  height: number;
}

/** Pipeline çıktısı */
export interface ProcessingOutput {
  /** İşlenmiş JPEG blob'u (backend'e gönderilecek) */
  blob: Blob;
  /** Önizleme için object URL */
  previewUrl: string;
  /** İşlem log kayıtları */
  logs: string[];
  /** Orijinal boyut */
  originalSize: ImageSize;
  /** İşlenmiş boyut */
  processedSize: ImageSize;
  /** Dosya boyutu (KB) */
  fileSizeKb: number;
}
