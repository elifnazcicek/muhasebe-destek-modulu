import {
  Component,
  ElementRef,
  EventEmitter,
  Input,
  OnChanges,
  OnDestroy,
  Output,
  SimpleChanges,
  ViewChild,
  signal,
} from '@angular/core';
import { CommonModule } from '@angular/common';

/**
 * 4-Köşe Belge Kırpma (Cropper) Component'i
 *
 * Görselin üzerine 4 sürüklenebilir köşe noktası ve çokgen overlay koyar.
 * Kullanıcı köşeleri sürükleyerek belge sınırlarını belirler.
 * "Kırp" butonuna basıldığında perspektif dönüşüm uygulanır.
 *
 * Özellikler:
 * - Otomatik köşe tespiti (basit kenar algılama ile başlangıç noktaları)
 * - Kullanıcının 4 köşeyi elle sürüklemesi
 * - Perspektif düzeltme (perspective warp) — Canvas tabanlı
 */
@Component({
  selector: 'app-cropper',
  standalone: true,
  imports: [CommonModule],
  templateUrl: './cropper.component.html',
  styleUrl: './cropper.component.scss',
})
export class CropperComponent implements OnChanges, OnDestroy {
  /** Kırpılacak görselin URL'si (object URL veya data URL) */
  @Input() imageUrl: string | null = null;

  /** Kırpma tamamlandığında çıktı olarak blob ve log verir */
  @Output() cropped = new EventEmitter<CropperOutput>();

  /** İptal edildiğinde */
  @Output() cancelled = new EventEmitter<void>();

  @ViewChild('cropperCanvas') canvasRef!: ElementRef<HTMLCanvasElement>;
  @ViewChild('overlayCanvas') overlayRef!: ElementRef<HTMLCanvasElement>;

  // Köşe noktaları (sol-üst, sağ-üst, sağ-alt, sol-alt)
  corners = signal<Point[]>([]);
  activeCorner = signal<number>(-1);
  imageLoaded = signal(false);

  private image: HTMLImageElement | null = null;
  private displayScale = 1;
  private offsetX = 0;
  private offsetY = 0;

  ngOnChanges(changes: SimpleChanges): void {
    if (changes['imageUrl'] && this.imageUrl) {
      this.loadImage(this.imageUrl);
    }
  }

  // =========================================================================
  // Görsel Yükleme ve Otomatik Köşe Tespiti
  // =========================================================================

  private async loadImage(url: string): Promise<void> {
    const img = new Image();
    img.onload = () => {
      this.image = img;
      this.imageLoaded.set(true);

      // İlk render'dan sonra canvas'a çiz
      requestAnimationFrame(() => {
        this.drawImageToCanvas();
        const autoCorners = this.detectDocumentCorners(img);
        this.corners.set(autoCorners);
        this.drawOverlay();
      });
    };
    img.src = url;
  }

  private drawImageToCanvas(): void {
    const canvas = this.canvasRef.nativeElement;
    const ctx = canvas.getContext('2d')!;
    const img = this.image!;

    // Canvas'ı container boyutuna ayarla
    const container = canvas.parentElement!;
    const maxW = container.clientWidth;
    const maxH = Math.min(window.innerHeight * 0.6, 500);

    // Oranı koru
    const scale = Math.min(maxW / img.width, maxH / img.height);
    canvas.width = Math.round(img.width * scale);
    canvas.height = Math.round(img.height * scale);

    this.displayScale = scale;

    ctx.drawImage(img, 0, 0, canvas.width, canvas.height);
  }

  /**
   * Basit kenar algılama ile belge köşelerini tahmin eder.
   * Gerçek görüntü piksellerini analiz ederek beyaz/açık renk belge alanını bulur.
   */
  private detectDocumentCorners(img: HTMLImageElement): Point[] {
    const tempCanvas = document.createElement('canvas');
    const w = Math.min(img.width, 600); // Performans için küçült
    const h = Math.round((img.height / img.width) * w);
    tempCanvas.width = w;
    tempCanvas.height = h;
    const ctx = tempCanvas.getContext('2d')!;
    ctx.drawImage(img, 0, 0, w, h);

    const imageData = ctx.getImageData(0, 0, w, h);
    const data = imageData.data;

    // Parlaklık haritası oluştur
    const brightness = new Uint8Array(w * h);
    for (let i = 0; i < w * h; i++) {
      const r = data[i * 4];
      const g = data[i * 4 + 1];
      const b = data[i * 4 + 2];
      brightness[i] = Math.round(0.299 * r + 0.587 * g + 0.114 * b);
    }

    // Otsu eşik değeri hesapla
    const threshold = this.otsuThreshold(brightness);

    // Beyaz bölgelerin sınır kutusunu bul (belge genellikle açık renktir)
    let minX = w, minY = h, maxX = 0, maxY = 0;
    let foundBright = false;

    for (let y = 0; y < h; y++) {
      for (let x = 0; x < w; x++) {
        if (brightness[y * w + x] > threshold) {
          if (x < minX) minX = x;
          if (x > maxX) maxX = x;
          if (y < minY) minY = y;
          if (y > maxY) maxY = y;
          foundBright = true;
        }
      }
    }

    // Orijinal görüntü koordinatlarına dönüştür
    const scaleBack = img.width / w;

    if (foundBright && (maxX - minX) > w * 0.2 && (maxY - minY) > h * 0.2) {
      // %5 marj ekle
      const marginX = (maxX - minX) * 0.02;
      const marginY = (maxY - minY) * 0.02;
      return [
        { x: (minX + marginX) * scaleBack, y: (minY + marginY) * scaleBack },
        { x: (maxX - marginX) * scaleBack, y: (minY + marginY) * scaleBack },
        { x: (maxX - marginX) * scaleBack, y: (maxY - marginY) * scaleBack },
        { x: (minX + marginX) * scaleBack, y: (maxY - marginY) * scaleBack },
      ];
    }

    // Tespit başarısız → %10 marjla tüm görüntü
    const mx = img.width * 0.1;
    const my = img.height * 0.1;
    return [
      { x: mx, y: my },
      { x: img.width - mx, y: my },
      { x: img.width - mx, y: img.height - my },
      { x: mx, y: img.height - my },
    ];
  }

  /** Otsu eşik değeri hesaplama */
  private otsuThreshold(data: Uint8Array): number {
    const histogram = new Array(256).fill(0);
    for (const val of data) histogram[val]++;

    const total = data.length;
    let sum = 0;
    for (let i = 0; i < 256; i++) sum += i * histogram[i];

    let sumB = 0, wB = 0, wF = 0;
    let maxVariance = 0, threshold = 0;

    for (let t = 0; t < 256; t++) {
      wB += histogram[t];
      if (wB === 0) continue;
      wF = total - wB;
      if (wF === 0) break;

      sumB += t * histogram[t];
      const mB = sumB / wB;
      const mF = (sum - sumB) / wF;
      const variance = wB * wF * (mB - mF) * (mB - mF);

      if (variance > maxVariance) {
        maxVariance = variance;
        threshold = t;
      }
    }
    return threshold;
  }

  // =========================================================================
  // Overlay Çizimi (Köşe noktaları ve alan)
  // =========================================================================

  private drawOverlay(): void {
    const canvas = this.overlayRef?.nativeElement;
    if (!canvas) return;

    const bgCanvas = this.canvasRef.nativeElement;
    canvas.width = bgCanvas.width;
    canvas.height = bgCanvas.height;
    const ctx = canvas.getContext('2d')!;
    ctx.clearRect(0, 0, canvas.width, canvas.height);

    const pts = this.corners();
    if (pts.length !== 4) return;

    const scale = this.displayScale;

    // Karartma maskesi (belge dışı alan)
    ctx.fillStyle = 'rgba(0, 0, 0, 0.45)';
    ctx.fillRect(0, 0, canvas.width, canvas.height);

    // Belge alanını temizle (şeffaf yap)
    ctx.globalCompositeOperation = 'destination-out';
    ctx.beginPath();
    ctx.moveTo(pts[0].x * scale, pts[0].y * scale);
    for (let i = 1; i < 4; i++) {
      ctx.lineTo(pts[i].x * scale, pts[i].y * scale);
    }
    ctx.closePath();
    ctx.fill();
    ctx.globalCompositeOperation = 'source-over';

    // Kenar çizgileri
    ctx.strokeStyle = '#2ec4b6';
    ctx.lineWidth = 2;
    ctx.setLineDash([6, 3]);
    ctx.beginPath();
    ctx.moveTo(pts[0].x * scale, pts[0].y * scale);
    for (let i = 1; i < 4; i++) {
      ctx.lineTo(pts[i].x * scale, pts[i].y * scale);
    }
    ctx.closePath();
    ctx.stroke();
    ctx.setLineDash([]);

    // Köşe noktaları (sürüklenebilir daireler)
    const active = this.activeCorner();
    pts.forEach((pt, i) => {
      const px = pt.x * scale;
      const py = pt.y * scale;
      const radius = i === active ? 14 : 10;

      // Dış halka
      ctx.beginPath();
      ctx.arc(px, py, radius, 0, Math.PI * 2);
      ctx.fillStyle = i === active ? '#ff6b6b' : '#2ec4b6';
      ctx.fill();

      // İç nokta
      ctx.beginPath();
      ctx.arc(px, py, 4, 0, Math.PI * 2);
      ctx.fillStyle = '#fff';
      ctx.fill();
    });
  }

  // =========================================================================
  // Sürükleme (Drag) Olayları
  // =========================================================================

  onPointerDown(event: PointerEvent): void {
    const canvas = this.overlayRef.nativeElement;
    const rect = canvas.getBoundingClientRect();
    const mx = event.clientX - rect.left;
    const my = event.clientY - rect.top;

    const pts = this.corners();
    const scale = this.displayScale;
    const hitRadius = 20;

    // En yakın köşeyi bul
    for (let i = 0; i < pts.length; i++) {
      const dx = pts[i].x * scale - mx;
      const dy = pts[i].y * scale - my;
      if (Math.sqrt(dx * dx + dy * dy) < hitRadius) {
        this.activeCorner.set(i);
        canvas.setPointerCapture(event.pointerId);
        this.drawOverlay();
        return;
      }
    }
  }

  onPointerMove(event: PointerEvent): void {
    const idx = this.activeCorner();
    if (idx < 0) return;

    const canvas = this.overlayRef.nativeElement;
    const rect = canvas.getBoundingClientRect();
    const mx = event.clientX - rect.left;
    const my = event.clientY - rect.top;
    const scale = this.displayScale;

    // Orijinal görüntü koordinatlarına dönüştür ve sınırla
    const imgW = this.image!.width;
    const imgH = this.image!.height;
    const newX = Math.max(0, Math.min(imgW, mx / scale));
    const newY = Math.max(0, Math.min(imgH, my / scale));

    const pts = [...this.corners()];
    pts[idx] = { x: newX, y: newY };
    this.corners.set(pts);
    this.drawOverlay();
  }

  onPointerUp(): void {
    this.activeCorner.set(-1);
    this.drawOverlay();
  }

  // =========================================================================
  // Perspektif Dönüşüm ve Kırpma
  // =========================================================================

  applyCrop(): void {
    if (!this.image) return;

    const pts = this.corners();
    const img = this.image;

    // Hedef boyutları hesapla
    const widthTop = this.distance(pts[0], pts[1]);
    const widthBottom = this.distance(pts[3], pts[2]);
    const heightLeft = this.distance(pts[0], pts[3]);
    const heightRight = this.distance(pts[1], pts[2]);

    const dstW = Math.round(Math.max(widthTop, widthBottom));
    const dstH = Math.round(Math.max(heightLeft, heightRight));

    // Perspektif dönüşüm (bilineer interpolasyon ile)
    const srcCanvas = document.createElement('canvas');
    srcCanvas.width = img.width;
    srcCanvas.height = img.height;
    const srcCtx = srcCanvas.getContext('2d')!;
    srcCtx.drawImage(img, 0, 0);
    const srcData = srcCtx.getImageData(0, 0, img.width, img.height);

    const dstCanvas = document.createElement('canvas');
    dstCanvas.width = dstW;
    dstCanvas.height = dstH;
    const dstCtx = dstCanvas.getContext('2d')!;
    const dstData = dstCtx.createImageData(dstW, dstH);

    // Her hedef pikseli için kaynak koordinatını hesapla (bilineer mapping)
    for (let dy = 0; dy < dstH; dy++) {
      for (let dx = 0; dx < dstW; dx++) {
        const u = dx / dstW;
        const v = dy / dstH;

        // Bilineer interpolasyon: 4 köşe arasında karışım
        const srcX =
          (1 - u) * (1 - v) * pts[0].x +
          u * (1 - v) * pts[1].x +
          u * v * pts[2].x +
          (1 - u) * v * pts[3].x;
        const srcY =
          (1 - u) * (1 - v) * pts[0].y +
          u * (1 - v) * pts[1].y +
          u * v * pts[2].y +
          (1 - u) * v * pts[3].y;

        // En yakın piksel (nearest neighbor, hız için)
        const sx = Math.round(Math.max(0, Math.min(img.width - 1, srcX)));
        const sy = Math.round(Math.max(0, Math.min(img.height - 1, srcY)));

        const srcIdx = (sy * img.width + sx) * 4;
        const dstIdx = (dy * dstW + dx) * 4;

        dstData.data[dstIdx] = srcData.data[srcIdx];
        dstData.data[dstIdx + 1] = srcData.data[srcIdx + 1];
        dstData.data[dstIdx + 2] = srcData.data[srcIdx + 2];
        dstData.data[dstIdx + 3] = 255;
      }
    }

    dstCtx.putImageData(dstData, 0, 0);

    // JPEG Blob oluştur
    dstCanvas.toBlob(
      (blob) => {
        if (blob) {
          this.cropped.emit({
            blob,
            previewUrl: URL.createObjectURL(blob),
            width: dstW,
            height: dstH,
            corners: pts,
          });
        }
      },
      'image/jpeg',
      0.92
    );
  }

  cancel(): void {
    this.cancelled.emit();
  }

  private distance(a: Point, b: Point): number {
    return Math.sqrt((a.x - b.x) ** 2 + (a.y - b.y) ** 2);
  }

  ngOnDestroy(): void {
    this.image = null;
  }
}

// ===========================================================================
// Arayüz Tanımları
// ===========================================================================

export interface Point {
  x: number;
  y: number;
}

export interface CropperOutput {
  blob: Blob;
  previewUrl: string;
  width: number;
  height: number;
  corners: Point[];
}
