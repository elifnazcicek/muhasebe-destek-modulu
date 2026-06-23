import {
  Component,
  ElementRef,
  OnDestroy,
  OnInit,
  ViewChild,
  signal,
} from '@angular/core';
import { CommonModule } from '@angular/common';
import {
  ImageProcessorService,
  ProcessingOutput,
} from '../../services/image-processor.service';
import { ApiService } from '../../services/api.service';
import { CropperComponent, CropperOutput } from '../cropper/cropper.component';
import { firstValueFrom } from 'rxjs';

/**
 * Kamera Component'i (Güncellenmiş)
 *
 * Yeni özellikler:
 * - Drag-and-drop dosya yükleme
 * - Otomatik belge köşe tespiti + 4-köşe sürükleme (Cropper entegrasyonu)
 * - Çoklu fiş çekme (toplu tarama modu)
 * - Kamera çözünürlük ve ön/arka kamera seçici
 */
@Component({
  selector: 'app-camera',
  standalone: true,
  imports: [CommonModule, CropperComponent],
  templateUrl: './camera.component.html',
  styleUrl: './camera.component.scss',
})
export class CameraComponent implements OnInit, OnDestroy {
  @ViewChild('videoElement') videoRef!: ElementRef<HTMLVideoElement>;
  @ViewChild('fileInput') fileInputRef!: ElementRef<HTMLInputElement>;

  // === Durum Sinyalleri ===
  cameraActive = signal(false);
  isProcessing = signal(false);
  processingLogs = signal<string[]>([]);
  errorMessage = signal<string | null>(null);
  uploadResult = signal<any>(null);
  isDragOver = signal(false);

  // === Kamera Seçici ===
  availableCameras = signal<MediaDeviceInfo[]>([]);
  selectedCameraId = signal<string>('');
  selectedResolution = signal<ResolutionOption>(RESOLUTIONS[0]);
  resolutions = RESOLUTIONS;

  // === Cropper ===
  showCropper = signal(false);
  cropperImageUrl = signal<string | null>(null);
  private pendingBlob: Blob | null = null;

  // === Çoklu Tarama ===
  batchMode = signal(false);
  batchResults = signal<BatchItem[]>([]);

  // === Önizleme ===
  previewUrl = signal<string | null>(null);

  private mediaStream: MediaStream | null = null;

  constructor(
    private imageProcessor: ImageProcessorService,
    private apiService: ApiService
  ) {}

  async ngOnInit(): Promise<void> {
    await this.enumerateCameras();
  }

  // =========================================================================
  // Kamera Listeleme ve Seçim
  // =========================================================================

  /** Kullanılabilir kameraları listeler. */
  async enumerateCameras(): Promise<void> {
    try {
      // İlk erişim izni iste (cihaz listesi için gerekli)
      const tempStream = await navigator.mediaDevices.getUserMedia({ video: true });
      tempStream.getTracks().forEach(t => t.stop());

      const devices = await navigator.mediaDevices.enumerateDevices();
      const cameras = devices.filter(d => d.kind === 'videoinput');
      this.availableCameras.set(cameras);

      if (cameras.length > 0 && !this.selectedCameraId()) {
        this.selectedCameraId.set(cameras[0].deviceId);
      }

      this.addLog(`${cameras.length} kamera bulundu.`);
    } catch (err: any) {
      this.addLog(`Kamera listesi alınamadı: ${err.message}`);
    }
  }

  /** Kamera değiştiğinde. */
  onCameraChange(event: Event): void {
    const select = event.target as HTMLSelectElement;
    this.selectedCameraId.set(select.value);
    if (this.cameraActive()) {
      this.stopCamera();
      this.startCamera();
    }
  }

  /** Çözünürlük değiştiğinde. */
  onResolutionChange(event: Event): void {
    const select = event.target as HTMLSelectElement;
    const idx = parseInt(select.value, 10);
    this.selectedResolution.set(RESOLUTIONS[idx]);
    if (this.cameraActive()) {
      this.stopCamera();
      this.startCamera();
    }
  }

  // =========================================================================
  // Kamera Aç / Kapat
  // =========================================================================

  async startCamera(): Promise<void> {
    this.errorMessage.set(null);
    const res = this.selectedResolution();

    try {
      const constraints: MediaStreamConstraints = {
        video: {
          deviceId: this.selectedCameraId() ? { exact: this.selectedCameraId() } : undefined,
          width: { ideal: res.width },
          height: { ideal: res.height },
        },
      };

      this.mediaStream = await navigator.mediaDevices.getUserMedia(constraints);
      const video = this.videoRef.nativeElement;
      video.srcObject = this.mediaStream;
      await video.play();

      // Gerçek çözünürlüğü logla
      const track = this.mediaStream.getVideoTracks()[0];
      const settings = track.getSettings();
      this.addLog(`Kamera açıldı: ${settings.width}x${settings.height}`);
      this.cameraActive.set(true);
    } catch (err: any) {
      this.errorMessage.set(`Kamera erişimi reddedildi: ${err.message}`);
      this.addLog(`HATA: Kamera açılamadı — ${err.message}`);
    }
  }

  stopCamera(): void {
    if (this.mediaStream) {
      this.mediaStream.getTracks().forEach(t => t.stop());
      this.mediaStream = null;
    }
    this.cameraActive.set(false);
    this.addLog('Kamera kapatıldı.');
  }

  // =========================================================================
  // Çekim → Cropper → İşleme
  // =========================================================================

  /** Frame yakalar ve Cropper'a gönderir. */
  async captureForCrop(): Promise<void> {
    if (!this.cameraActive()) return;

    const video = this.videoRef.nativeElement;
    const canvas = document.createElement('canvas');
    canvas.width = video.videoWidth;
    canvas.height = video.videoHeight;
    const ctx = canvas.getContext('2d')!;

    // Webcam → flip uygula
    ctx.translate(canvas.width, 0);
    ctx.scale(-1, 1);
    ctx.drawImage(video, 0, 0);

    // Blob oluştur ve Cropper'a gönder
    canvas.toBlob((blob) => {
      if (blob) {
        const url = URL.createObjectURL(blob);
        this.cropperImageUrl.set(url);
        this.showCropper.set(true);
        this.addLog('Frame yakalandı. Belge köşelerini ayarlayın.');
      }
    }, 'image/png');
  }

  /** Cropper'dan kırpılmış sonuç geldiğinde. */
  async onCropped(output: CropperOutput): Promise<void> {
    this.showCropper.set(false);
    this.cleanupCropperUrl();

    this.isProcessing.set(true);
    this.previewUrl.set(output.previewUrl);
    this.addLog(`Belge kırpıldı: ${output.width}x${output.height}`);

    try {
      // Kırpılmış görseli dosya olarak işle (resize + JPEG)
      const file = new File([output.blob], 'cropped_receipt.jpg', { type: 'image/jpeg' });
      const result = await this.imageProcessor.processFile(file, false);

      this.processingLogs.update(logs => [...logs, ...result.logs]);
      this.previewUrl.set(result.previewUrl);

      // Backend'e gönder
      await this.uploadToBackend(result);

      // Çoklu tarama modunda listeye ekle
      if (this.batchMode()) {
        this.batchResults.update(items => [...items, {
          previewUrl: result.previewUrl,
          sizeKb: result.fileSizeKb,
          timestamp: new Date().toLocaleTimeString('tr-TR'),
          uploaded: true,
        }]);
      }
    } catch (err: any) {
      this.errorMessage.set(`İşleme hatası: ${err.message}`);
    } finally {
      this.isProcessing.set(false);
    }
  }

  /** Cropper iptal edildiğinde. */
  onCropCancelled(): void {
    this.showCropper.set(false);
    this.cleanupCropperUrl();
    this.addLog('Kırpma iptal edildi.');
  }

  /** Doğrudan çekim (cropper'sız) — hızlı mod. */
  async captureAndProcess(): Promise<void> {
    if (!this.cameraActive()) return;
    this.isProcessing.set(true);
    this.errorMessage.set(null);
    this.previewUrl.set(null);
    this.uploadResult.set(null);

    try {
      const video = this.videoRef.nativeElement;
      const result = await this.imageProcessor.processFrame(video, true, null);
      this.processingLogs.set(result.logs);
      this.previewUrl.set(result.previewUrl);
      await this.uploadToBackend(result);

      if (this.batchMode()) {
        this.batchResults.update(items => [...items, {
          previewUrl: result.previewUrl,
          sizeKb: result.fileSizeKb,
          timestamp: new Date().toLocaleTimeString('tr-TR'),
          uploaded: true,
        }]);
      }
    } catch (err: any) {
      this.errorMessage.set(`İşleme hatası: ${err.message}`);
    } finally {
      this.isProcessing.set(false);
    }
  }

  // =========================================================================
  // Drag & Drop Dosya Yükleme
  // =========================================================================

  onDragOver(event: DragEvent): void {
    event.preventDefault();
    event.stopPropagation();
    this.isDragOver.set(true);
  }

  onDragLeave(event: DragEvent): void {
    event.preventDefault();
    event.stopPropagation();
    this.isDragOver.set(false);
  }

  async onDrop(event: DragEvent): Promise<void> {
    event.preventDefault();
    event.stopPropagation();
    this.isDragOver.set(false);

    const files = event.dataTransfer?.files;
    if (!files || files.length === 0) return;

    // Çoklu dosya desteği
    for (let i = 0; i < files.length; i++) {
      await this.processDroppedFile(files[i]);
    }
  }

  private async processDroppedFile(file: File): Promise<void> {
    const allowedTypes = ['image/jpeg', 'image/png', 'image/webp', 'image/bmp'];
    if (!allowedTypes.includes(file.type)) {
      this.errorMessage.set(`Desteklenmeyen format: ${file.type}`);
      return;
    }

    this.addLog(`Dosya sürüklendi: ${file.name} (${(file.size / 1024).toFixed(1)} KB)`);

    // Cropper'a gönder
    const url = URL.createObjectURL(file);
    this.cropperImageUrl.set(url);
    this.showCropper.set(true);
  }

  /** File input ile dosya seçme. */
  openFileSelector(): void {
    this.fileInputRef.nativeElement.click();
  }

  async onFileSelected(event: Event): Promise<void> {
    const input = event.target as HTMLInputElement;
    if (!input.files || input.files.length === 0) return;

    for (let i = 0; i < input.files.length; i++) {
      await this.processDroppedFile(input.files[i]);
    }
    input.value = '';
  }

  // =========================================================================
  // Çoklu Tarama Modu
  // =========================================================================

  toggleBatchMode(): void {
    this.batchMode.update(v => !v);
    if (this.batchMode()) {
      this.batchResults.set([]);
      this.addLog('Toplu tarama modu açıldı.');
    } else {
      this.addLog('Toplu tarama modu kapatıldı.');
    }
  }

  clearBatch(): void {
    // Object URL'leri temizle
    this.batchResults().forEach(item => {
      if (item.previewUrl) URL.revokeObjectURL(item.previewUrl);
    });
    this.batchResults.set([]);
    this.addLog('Toplu tarama listesi temizlendi.');
  }

  // =========================================================================
  // Backend İletişimi
  // =========================================================================

  private async uploadToBackend(result: ProcessingOutput): Promise<void> {
    this.addLog("Backend'e gönderiliyor...");
    try {
      const apiResponse = await firstValueFrom(this.apiService.scanReceipt(result.blob));
      this.uploadResult.set(apiResponse);
      if (apiResponse && apiResponse.success !== false) {
        this.addLog(`Backend işlem başarılı.`);
      } else {
        this.addLog(`Backend hatası: ${apiResponse?.message}`);
      }
    } catch (err: any) {
      this.addLog(`Backend bağlantı hatası: ${err.message}`);
    }
  }

  // =========================================================================
  // Yardımcı
  // =========================================================================

  private addLog(message: string): void {
    const timestamp = new Date().toLocaleTimeString('tr-TR');
    this.processingLogs.update(logs => [...logs, `[${timestamp}] ${message}`]);
  }

  private cleanupCropperUrl(): void {
    const url = this.cropperImageUrl();
    if (url) URL.revokeObjectURL(url);
    this.cropperImageUrl.set(null);
  }

  ngOnDestroy(): void {
    this.stopCamera();
    this.cleanupCropperUrl();
    const url = this.previewUrl();
    if (url) URL.revokeObjectURL(url);
    this.batchResults().forEach(item => {
      if (item.previewUrl) URL.revokeObjectURL(item.previewUrl);
    });
  }
}

// ===========================================================================
// Yardımcı Tipler
// ===========================================================================

export interface ResolutionOption {
  label: string;
  width: number;
  height: number;
}

const RESOLUTIONS: ResolutionOption[] = [
  { label: 'Full HD (1920×1080)', width: 1920, height: 1080 },
  { label: 'HD (1280×720)', width: 1280, height: 720 },
  { label: '4K (3840×2160)', width: 3840, height: 2160 },
  { label: 'VGA (640×480)', width: 640, height: 480 },
];

export interface BatchItem {
  previewUrl: string;
  sizeKb: number;
  timestamp: string;
  uploaded: boolean;
}
