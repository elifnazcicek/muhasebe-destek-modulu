import { Component, OnInit, OnDestroy, ChangeDetectorRef, ViewChild, ElementRef } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { HttpClient } from '@angular/common/http';
import { DomSanitizer, SafeResourceUrl } from '@angular/platform-browser';
import { OcrStateService } from '../../services/ocr-state.service';

interface InvoiceLine {
  cinsi: 'Hizmet' | 'Stok';
  kodu: string;
  ismi: string;
  miktar: number;
  birimFiyat: number;
  kdvOrani: number;
  grossTotal: number; // Mal/Hizmet Toplam Tutarı (Miktar * BirimFiyat)
  iskonto: number; // Toplam İskonto
  kdvTutari: number; // Hesaplanan KDV
  netTutar: number; // Net Tutar (GrossTotal - Iskonto)
  vergilerDahilToplam: number; // Vergiler Dahil Toplam Tutar (NetTutar + KdvTutari)
  aciklama?: string;
  tutar?: number; // for backward compatibility
}

@Component({
  selector: 'app-dekont',
  standalone: true,
  imports: [CommonModule, FormsModule],
  templateUrl: './dekont.component.html',
  styleUrl: './dekont.component.css'
})
export class DekontComponent implements OnInit, OnDestroy {
  @ViewChild('fileInput') fileInput!: ElementRef;

  private baseUrl = 'http://localhost:5000/api/dekont';

  // Fiş/Fatura Başlık Bilgileri
  evrakNo: string = 'F2026-AUTO'; 
  belgeNo: string = '';
  tarih: string = '';
  odemeTipi: string = 'Açık Hesap';
  
  // Cari Bilgileri
  VKN: string = '';
  cariKodu: string = '';
  cariAdi: string = '';
  isCariValid: boolean = false;

  // Otomatik Alış/Satış Tespiti ve Şirket Adı
  myCompanyName: string = '';
  detectedType: 'Alis' | 'Satis' = 'Alis';

  // Grid Satırları
  invoiceLines: InvoiceLine[] = [];

  // Toplamlar
  araToplam: number = 0;
  kdvToplam: number = 0;
  genelToplam: number = 0;

  // Önizleme ve Dosya Kontrolleri
  showPreview: boolean = false;
  isPdf: boolean = false;
  isImage: boolean = false;
  safePdfUrl: SafeResourceUrl | null = null;
  imageUrl: string | null = null;
  htmlPreviewContent: string | null = null;
  isDragOver: boolean = false;
  loading: boolean = false;
  imageZoomLevel: number = 1.0;

  // Sayfalama (Çok sayfalı PDF'ler için)
  pdfCurrentPage: number = 1;
  pdfTotalPages: number = 1;

  // Bildirim Mesajları
  statusMessage: string | null = null;
  statusType: 'success' | 'info' | 'error' = 'info';

  // Giriş Yapan Kullanıcı
  currentUsername: string = '';

  constructor(
    private http: HttpClient,
    private cdr: ChangeDetectorRef,
    private sanitizer: DomSanitizer,
    private ocrState: OcrStateService
  ) {}

  ngOnInit(): void {
    this.currentUsername = localStorage.getItem('username') || 'Sistem Kullanıcısı';
    this.myCompanyName = localStorage.getItem('myCompanyName') || '';
    this.restoreState();
  }

  ngOnDestroy(): void {
    this.saveState();
  }

  saveState(): void {
    const s = this.ocrState.dekontState;
    s.showPreview = this.showPreview;
    s.previewUrl = this.previewUrl;
    s.isPdf = this.isPdf;
    s.safePdfUrl = this.safePdfUrl;
    s.pdfCurrentPage = this.pdfCurrentPage;
    s.pdfTotalPages = this.pdfTotalPages;
    s.evrakNo = this.evrakNo;
    s.belgeNo = this.belgeNo;
    s.tarih = this.tarih;
    s.odemeTipi = this.odemeTipi;
    s.VKN = this.VKN;
    s.cariKodu = this.cariKodu;
    s.cariAdi = this.cariAdi;
    s.isCariValid = this.isCariValid;
    s.detectedType = this.detectedType;
    s.invoiceLines = this.invoiceLines;
    s.araToplam = this.araToplam;
    s.kdvToplam = this.kdvToplam;
    s.genelToplam = this.genelToplam;
    s.imageUrl = this.imageUrl;
    s.htmlPreviewContent = this.htmlPreviewContent;
  }

  restoreState(): void {
    const s = this.ocrState.dekontState;
    this.showPreview = s.showPreview;
    this.previewUrl = s.previewUrl;
    this.isPdf = s.isPdf;
    this.safePdfUrl = s.safePdfUrl;
    this.pdfCurrentPage = s.pdfCurrentPage;
    this.pdfTotalPages = s.pdfTotalPages;
    this.evrakNo = s.evrakNo;
    this.belgeNo = s.belgeNo;
    this.tarih = s.tarih;
    this.odemeTipi = s.odemeTipi;
    this.VKN = s.VKN;
    this.cariKodu = s.cariKodu;
    this.cariAdi = s.cariAdi;
    this.isCariValid = s.isCariValid;
    this.detectedType = s.detectedType;
    this.invoiceLines = s.invoiceLines;
    this.araToplam = s.araToplam;
    this.kdvToplam = s.kdvToplam;
    this.genelToplam = s.genelToplam;
    this.imageUrl = s.imageUrl;
    this.htmlPreviewContent = s.htmlPreviewContent;
  }

  saveMyCompanyName(): void {
    localStorage.setItem('myCompanyName', this.myCompanyName);
  }

  onCariKoduChange(): void {
    this.isCariValid = !!(this.cariKodu && this.cariKodu.trim() !== '');
  }

  // Sürükle Bırak Eventleri
  onDragOver(event: DragEvent): void {
    event.preventDefault();
    this.isDragOver = true;
  }

  onDragLeave(event: DragEvent): void {
    event.preventDefault();
    this.isDragOver = false;
  }

  onDrop(event: DragEvent): void {
    event.preventDefault();
    this.isDragOver = false;
    const file = event.dataTransfer?.files?.[0];
    if (file) {
      this.handleFile(file);
    }
  }

  onFileSelected(event: any): void {
    const file = event.target.files?.[0];
    if (file) {
      this.handleFile(file);
    }
  }

  // Dosya Yükleme ve Çözümleme
  handleFile(file: File): void {
    const ext = file.name.split('.').pop()?.toLowerCase();
    
    if (ext !== 'xml' && ext !== 'pdf' && ext !== 'jpg' && ext !== 'jpeg' && ext !== 'png') {
      this.showStatus('Lütfen yalnızca Uyumsoft XML, PDF veya Fiş/Dekont Görseli yükleyiniz.', 'error', 5000);
      return;
    }

    this.resetInput();
    this.loading = true;
    this.showStatus('Dosya yükleniyor ve çözümleniyor...', 'info');

    if (ext === 'pdf') {
      this.isPdf = true;
      this.safePdfUrl = this.sanitizer.bypassSecurityTrustResourceUrl(URL.createObjectURL(file));
      this.showPreview = true;
    } else if (ext === 'jpg' || ext === 'jpeg' || ext === 'png') {
      this.isImage = true;
      this.imageUrl = URL.createObjectURL(file);
      this.showPreview = true;
    }

    const formData = new FormData();
    formData.append('file', file);
    formData.append('myCompanyName', this.myCompanyName);

    this.http.post<any>(`${this.baseUrl}/parse-xml`, formData).subscribe({
      next: (res) => {
        this.loading = false;
        if (res.success && res.data) {
          const data = res.data;
          
          this.belgeNo = data.belgeNo || '';
          this.tarih = data.tarih ? data.tarih.substring(0, 10) : '';
          this.VKN = data.vkn || '';
          this.cariAdi = data.cariAdi || '';
          this.cariKodu = data.cariKodu || '';
          this.isCariValid = data.isCariValid || false;

          // Satır Detaylarını Yükle
          if (data.invoiceLines && data.invoiceLines.length > 0) {
            this.invoiceLines = data.invoiceLines.map((line: any) => {
              const miktar = line.miktar || 1;
              const birimFiyat = line.birimFiyat || line.tutar || 0;
              const kdvOrani = line.kdvOrani !== undefined ? line.kdvOrani : 20;
              const grossTotal = line.grossTotal || (miktar * birimFiyat);
              const iskonto = line.iskonto || 0;
              const netTutar = line.netTutar || (grossTotal - iskonto);
              const kdvTutari = line.kdvTutari || (netTutar * (kdvOrani / 100));
              const vergilerDahilToplam = line.vergilerDahilToplam || (netTutar + kdvTutari);

              return {
                cinsi: line.cinsi || 'Hizmet',
                kodu: line.kodu !== undefined && line.kodu !== null ? line.kodu : '',
                ismi: line.ismi !== undefined && line.ismi !== null ? line.ismi : '',
                miktar: miktar,
                birimFiyat: birimFiyat,
                kdvOrani: kdvOrani,
                grossTotal: grossTotal,
                iskonto: iskonto,
                kdvTutari: kdvTutari,
                netTutar: netTutar,
                vergilerDahilToplam: vergilerDahilToplam,
                tutar: netTutar
              };
            });
          } else {
            const total = data.genelToplam || 0;
            this.invoiceLines = [{
              cinsi: 'Hizmet',
              kodu: '',
              ismi: '',
              miktar: 1,
              birimFiyat: total,
              kdvOrani: 20,
              grossTotal: total,
              iskonto: 0,
              kdvTutari: total * 0.20,
              netTutar: total,
              vergilerDahilToplam: total * 1.20,
              tutar: total
            }];
          }

          if (!this.isPdf && data.pdfContent) {
            try {
              const base64Pdf = data.pdfContent;
              const byteCharacters = atob(base64Pdf);
              const byteNumbers = new Array(byteCharacters.length);
              for (let i = 0; i < byteCharacters.length; i++) {
                byteNumbers[i] = byteCharacters.charCodeAt(i);
              }
              const byteArray = new Uint8Array(byteNumbers);
              const blob = new Blob([byteArray], { type: 'application/pdf' });
              
              this.safePdfUrl = this.sanitizer.bypassSecurityTrustResourceUrl(URL.createObjectURL(blob));
              this.isPdf = true;
              this.showPreview = true;
            } catch (pdfErr) {
              console.error('Embedded PDF parse hatası:', pdfErr);
            }
          }

          if (!this.isPdf && !data.pdfContent && data.htmlContent) {
            this.htmlPreviewContent = data.htmlContent;
            this.showPreview = true;
          }

          if (data.detectedType) {
            this.detectedType = data.detectedType as 'Alis' | 'Satis';
          }

          this.calculateTotals();
          
          if (this.isCariValid) {
            this.showStatus('Dosya başarıyla çözümlendi ve cari eşleştirildi.', 'success', 5000);
          } else {
            this.showStatus('Belge çözümlendi ancak Cari Kartı Mikro\'da bulunamadı! Lütfen kart oluşturun.', 'error');
          }
        } else {
          this.showStatus(res.message || 'Çözümleme başarısız oldu.', 'error');
        }
        this.cdr.detectChanges();
      },
      error: (err) => {
        this.loading = false;
        this.showStatus(err.error?.message || 'Sunucuyla bağlantı kurulamadı. Dosya okuma hatası.', 'error');
        this.cdr.detectChanges();
      }
    });
  }

  // Toplam Hesaplamaları
  calculateTotals(): void {
    this.araToplam = 0;
    this.kdvToplam = 0;
    
    this.invoiceLines.forEach(line => {
      line.grossTotal = line.miktar * line.birimFiyat;
      line.iskonto = line.iskonto || 0;
      line.netTutar = line.grossTotal - line.iskonto;
      line.kdvTutari = Math.round((line.netTutar * (line.kdvOrani / 100)) * 100) / 100;
      line.vergilerDahilToplam = Math.round((line.netTutar + line.kdvTutari) * 100) / 100;
      
      line.tutar = line.netTutar; // compatibility
      this.araToplam += line.netTutar;
      this.kdvToplam += line.kdvTutari;
    });

    this.genelToplam = this.araToplam + this.kdvToplam;
  }

  // Genel toplamı el ile girilen değerlere göre yeniden hesaplar
  calculateOverallTotals(): void {
    this.araToplam = 0;
    this.kdvToplam = 0;
    this.genelToplam = 0;
    this.invoiceLines.forEach(line => {
      const gross = line.miktar * line.birimFiyat;
      line.iskonto = line.iskonto || 0;
      line.netTutar = gross - line.iskonto;
      line.tutar = line.netTutar;

      this.araToplam += line.netTutar;
      this.kdvToplam += line.kdvTutari || 0;
      this.genelToplam += line.vergilerDahilToplam || 0;
    });
  }

  // Satır Ekleme
  addNewLine(): void {
    this.invoiceLines.push({
      cinsi: 'Hizmet',
      kodu: '',
      ismi: '',
      miktar: 1,
      birimFiyat: 0,
      kdvOrani: 20,
      grossTotal: 0,
      iskonto: 0,
      kdvTutari: 0,
      netTutar: 0,
      vergilerDahilToplam: 0,
      tutar: 0
    });
    this.calculateTotals();
  }

  // Mikro Veritabanında Yeni Cari Kartı Açma
  createCariCard(): void {
    if (!this.VKN || !this.cariAdi) return;

    this.loading = true;
    this.showStatus('Mikro veritabanına yeni Cari Kartı ekleniyor...', 'info');

    const payload = {
      vkn: this.VKN,
      cariAdi: this.cariAdi
    };

    this.http.post<any>(`${this.baseUrl}/create-cari`, payload).subscribe({
      next: (res) => {
        this.loading = false;
        if (res.success && res.data) {
          this.cariKodu = res.data.cariKodu;
          this.isCariValid = true;
          this.showStatus(`Yeni cari başarıyla oluşturuldu! Cari Kodu: ${this.cariKodu}`, 'success', 6000);
        } else {
          this.showStatus(res.message || 'Cari kart oluşturma başarısız.', 'error');
        }
        this.cdr.detectChanges();
      },
      error: (err) => {
        this.loading = false;
        this.showStatus(err.error?.message || 'Cari kart eklenirken SQL sunucu hatası oluştu.', 'error');
        this.cdr.detectChanges();
      }
    });
  }

  // Elle Cari Eşleştirme (Arama)
  searchAndMatchCari(): void {
    const searchString = prompt('Mikro veritabanında aramak istediğiniz Cari Adı veya Kodu giriniz:');
    if (!searchString) return;

    this.loading = true;
    this.showStatus('Cari hesaplar listeleniyor...', 'info');

    this.http.get<any>(`${this.baseUrl}/search-cari?query=${encodeURIComponent(searchString)}`).subscribe({
      next: (res) => {
        this.loading = false;
        if (res.success && res.data && res.data.length > 0) {
          const match = res.data[0];
          this.cariKodu = match.cariKodu;
          this.cariAdi = match.cariAdi;
          this.isCariValid = true;
          this.showStatus(`Cari elle eşleştirildi: ${this.cariAdi} (${this.cariKodu})`, 'success', 5000);
        } else {
          this.showStatus('Arama kriterine uygun cari hesap bulunamadı.', 'error', 4000);
        }
        this.cdr.detectChanges();
      },
      error: () => {
        this.loading = false;
        this.showStatus('Sorgulama sırasında bir hata oluştu.', 'error', 4000);
        this.cdr.detectChanges();
      }
    });
  }

  // Excel Çıktısı Üretme ve İndirme (010401 Modülü Uyumlu)
  downloadSingleExcel(faturaTipi?: string): void {
    const typeToSend = faturaTipi || this.detectedType || 'Alis';
    const payload = {
      evrakNo: this.evrakNo,
      belgeNo: this.belgeNo,
      tarih: this.tarih,
      odemeTipi: this.odemeTipi,
      vkn: this.VKN,
      cariKodu: this.cariKodu,
      cariAdi: this.cariAdi,
      lines: this.invoiceLines,
      araToplam: this.araToplam,
      kdvToplam: this.kdvToplam,
      genelToplam: this.genelToplam,
      kullanici: this.currentUsername,
      faturaTipi: typeToSend
    };

    this.http.post(`${this.baseUrl}/export-excel`, payload, { responseType: 'blob' }).subscribe({
      next: (blob: Blob) => {
        const link = document.createElement('a');
        link.href = window.URL.createObjectURL(blob);
        const prefix = typeToSend === 'Satis' ? 'Satis_Aktarim' : 'Alis_Aktarim';
        link.download = `${prefix}_${this.belgeNo || 'Fatura'}_${new Date().toISOString().substring(0,10)}.xlsx`;
        link.click();
      },
      error: (err) => {
        console.error('Bireysel Excel indirme hatası.', err);
      }
    });
  }

  saveAs(faturaTipi: 'Alis' | 'Satis'): void {
    if (!this.cariKodu || !this.cariKodu.trim()) {
      this.showStatus('Lütfen kaydetmeden önce cari kodunu giriniz veya eşleştiriniz.', 'error', 5000);
      return;
    }
    this.isCariValid = true;

    this.loading = true;
    this.showStatus('Fatura veritabanına kaydediliyor...', 'info');

    const payload = {
      evrakNo: this.evrakNo,
      belgeNo: this.belgeNo,
      tarih: this.tarih,
      odemeTipi: this.odemeTipi,
      vkn: this.VKN,
      cariKodu: this.cariKodu,
      cariAdi: this.cariAdi,
      faturaTipi: faturaTipi,
      createdBy: this.currentUsername,
      lines: this.invoiceLines
    };

    this.http.post<any>(`${this.baseUrl}/confirm`, payload).subscribe({
      next: (res) => {
        this.loading = false;
        if (res.success) {
          this.showStatus(`${faturaTipi === 'Alis' ? 'Alış' : 'Satış'} faturası başarıyla kaydedildi!`, 'success', 6000);
          this.downloadSingleExcel(faturaTipi);
        } else {
          this.showStatus(res.message || 'Fatura kaydedilemedi.', 'error');
        }
        this.cdr.detectChanges();
      },
      error: (err) => {
        this.loading = false;
        this.showStatus(err.error?.message || 'SQL veritabanına kaydetme sırasında hata oluştu.', 'error');
        this.cdr.detectChanges();
      }
    });
  }

  // Önizleme Sayfa Navigasyonu (PDF'ler için)
  prevPdfPage(): void {
    if (this.pdfCurrentPage > 1) this.pdfCurrentPage--;
  }

  nextPdfPage(): void {
    if (this.pdfCurrentPage < this.pdfTotalPages) this.pdfCurrentPage++;
  }

  // Ekranı Sıfırla
  resetInput(): void {
    this.showPreview = false;
    this.isPdf = false;
    this.isImage = false;
    this.safePdfUrl = null;
    this.imageUrl = null;
    this.htmlPreviewContent = null;
    this.imageZoomLevel = 1.0;
    this.panX = 0;
    this.panY = 0;
    this.isPanning = false;
    this.belgeNo = '';
    this.tarih = '';
    this.VKN = '';
    this.cariKodu = '';
    this.cariAdi = '';
    this.isCariValid = false;
    this.invoiceLines = [];
    this.araToplam = 0;
    this.kdvToplam = 0;
    this.genelToplam = 0;
    this.clearStatus();

    if (this.ocrState) {
      this.ocrState.dekontState = {
        showPreview: false,
        previewUrl: null,
        isPdf: false,
        safePdfUrl: null,
        pdfCurrentPage: 1,
        pdfTotalPages: 1,
        evrakNo: 'F2026-AUTO',
        belgeNo: '',
        tarih: '',
        odemeTipi: 'Açık Hesap',
        VKN: '',
        cariKodu: '',
        cariAdi: '',
        isCariValid: false,
        detectedType: 'Alis',
        invoiceLines: [],
        araToplam: 0,
        kdvToplam: 0,
        genelToplam: 0,
        imageUrl: null,
        htmlPreviewContent: null
      };
    }

    if (this.fileInput) {
      this.fileInput.nativeElement.value = '';
    }
  }

  showStatus(msg: string, type: 'success' | 'info' | 'error', durationMs: number = 0): void {
    this.statusMessage = msg;
    this.statusType = type;
    if (durationMs > 0) {
      setTimeout(() => {
        if (this.statusMessage === msg) {
          this.clearStatus();
        }
      }, durationMs);
    }
  }

  clearStatus(): void {
    this.statusMessage = null;
  }

  panX: number = 0;
  panY: number = 0;
  startX: number = 0;
  startY: number = 0;
  isPanning: boolean = false;

  zoomInImage(factor: number = 0.2): void {
    this.imageZoomLevel = Math.min(this.imageZoomLevel + factor, 5);
    this.cdr.detectChanges();
  }

  zoomOutImage(factor: number = 0.2): void {
    this.imageZoomLevel = Math.max(this.imageZoomLevel - factor, 0.4);
    if (this.imageZoomLevel === 1.0) {
      this.panX = 0;
      this.panY = 0;
    }
    this.cdr.detectChanges();
  }

  resetImageZoom(): void {
    this.imageZoomLevel = 1.0;
    this.panX = 0;
    this.panY = 0;
    this.isPanning = false;
    this.cdr.detectChanges();
  }

  startPan(event: MouseEvent): void {
    if (this.imageZoomLevel > 1) {
      event.preventDefault();
      this.isPanning = true;
      this.startX = event.clientX - this.panX;
      this.startY = event.clientY - this.panY;
      this.cdr.detectChanges();
    }
  }

  pan(event: MouseEvent): void {
    if (this.isPanning && this.imageZoomLevel > 1) {
      event.preventDefault();
      this.panX = event.clientX - this.startX;
      this.panY = event.clientY - this.startY;
      this.cdr.detectChanges();
    }
  }

  endPan(): void {
    if (this.isPanning) {
      this.isPanning = false;
      this.cdr.detectChanges();
    }
  }

  onImageWheel(event: WheelEvent): void {
    event.preventDefault();
    const zoomFactor = 0.1;
    if (event.deltaY < 0) {
      this.zoomInImage(zoomFactor);
    } else {
      this.zoomOutImage(zoomFactor);
    }
  }
}
