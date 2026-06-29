import { Component, OnInit, ChangeDetectorRef, HostListener } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { ApiService } from '../../services/api.service';
import { DomSanitizer, SafeResourceUrl } from '@angular/platform-browser';

interface ReceiptItem {
  itemName: string;
  quantity: number;
  unitPrice: number;
  totalPrice: number;
  taxRate: number;
}

@Component({
  selector: 'app-dashboard',
  standalone: true,
  imports: [CommonModule, FormsModule],
  templateUrl: './dashboard.component.html',
  styleUrls: ['./dashboard.component.css']
})
export class DashboardComponent implements OnInit {
  leftTab: 'camera' | 'upload' = 'upload';
  showPreview: boolean = false;
  isDragOver: boolean = false;
  isScanning: boolean = false;
  loading: boolean = false;

  // === LEFT PANEL ===
  previewUrl: string | null = null;
  selectedFile: File | null = null;
  isPdf: boolean = false;
  safePdfUrl: SafeResourceUrl | null = null;

  // === PDF.JS STATE ===
  pdfDocument: any = null;
  pdfCurrentPage: number = 1;
  pdfTotalPages: number = 0;

  // === ZOOM & PAN STATE ===
  zoomLevel: number = 1;
  panX: number = 0;
  panY: number = 0;
  isPanning: boolean = false;
  startX: number = 0;
  startY: number = 0;

  // === MIDDLE PANEL ===
  receiptId: number | null = null;
  merchantName: string = '';
  vknTckn: string = '';
  receiptDate: string = '';
  fisNo: string = '';
  totalAmount: number = 0;
  taxAmount: number = 0;
  imagePath: string | null = null;
  items: ReceiptItem[] = [];
  
  statusMessage: string = '';
  statusType: 'success' | 'info' | 'error' | null = null;
  statusTimeoutId: any = null;

  // === RIGHT PANEL: ARCHIVE ===
  receiptsList: any[] = [];
  filteredReceipts: any[] = [];
  searchQuery: string = '';
  loadingArchive: boolean = false;

  // === ROLE & INSPECT STATE ===
  isInspectMode: boolean = false;
  currentUserRole: string = 'User';
  currentUsername: string = '';

  constructor(private apiService: ApiService, private cdr: ChangeDetectorRef, private sanitizer: DomSanitizer) {}

  ngOnInit(): void {
    this.currentUsername = localStorage.getItem('username') || '';
    this.currentUserRole = localStorage.getItem('role') || 'User';
    this.clearForm();
    this.fetchReceiptsList();
  }

  // === LEFT PANEL METHODS ===

  // File Upload OCR
  onFileSelected(event: any): void {
    const file = event.target.files[0];
    if (file) {
      this.processOcrFile(file);
    }
  }

  processOcrFile(file: File | Blob): void {
    if (!file) return;
    this.resetZoom();

    this.isPdf = false;
    this.safePdfUrl = null;
    this.pdfDocument = null;
    this.pdfCurrentPage = 1;
    this.pdfTotalPages = 0;

    let isPdfFile = false;
    if (file instanceof File) {
      this.selectedFile = file;
      if (file.name.toLowerCase().endsWith('.pdf') || file.type === 'application/pdf') {
        isPdfFile = true;
      }
    } else {
      this.selectedFile = new File([file], 'pasted_receipt.jpg', { type: file.type });
      if (file.type === 'application/pdf') {
        isPdfFile = true;
      }
    }

    if (isPdfFile) {
      this.isPdf = true;
      this.showStatus('PDF belgesi yükleniyor ve sayfalar çıkarılıyor...', 'info');
      this.clearFormInputsOnly();
      this.cdr.detectChanges();

      const reader = new FileReader();
      reader.onload = (e: any) => {
        const arrayBuffer = e.target.result;
        const pdfjsLib = (window as any).pdfjsLib;
        if (pdfjsLib) {
          pdfjsLib.GlobalWorkerOptions.workerSrc = 'https://cdnjs.cloudflare.com/ajax/libs/pdf.js/3.4.120/pdf.worker.min.js';
          pdfjsLib.getDocument({ data: arrayBuffer }).promise.then(
            (pdf: any) => {
              this.pdfDocument = pdf;
              this.pdfTotalPages = pdf.numPages;
              this.pdfCurrentPage = 1;
              this.renderPdfPage();
            },
            (err: any) => {
              this.showStatus('PDF yüklenemedi: ' + err.message, 'error', 5000);
              this.cdr.detectChanges();
            }
          );
        } else {
          this.showStatus('PDF.js kütüphanesi yüklenemedi. index.html dosyasını kontrol edin.', 'error', 5000);
          this.cdr.detectChanges();
        }
      };
      reader.readAsArrayBuffer(file);
    } else {
      // Normal görsel yükleme
      const reader = new FileReader();
      reader.onload = (e: any) => {
        setTimeout(() => {
          this.previewUrl = e.target.result;
          this.showPreview = true;
          this.cdr.detectChanges();
        }, 0);
      };
      reader.readAsDataURL(file);

      // Otomatik tara
      this.triggerOcrScan(file);
    }
  }

  triggerOcrScan(fileToScan: File | Blob): void {
    this.isScanning = true;
    this.showStatus('Dosya çözümleniyor...', 'info');
    this.cdr.detectChanges();
    
    this.apiService.scanReceipt(fileToScan).subscribe({
      next: (res) => {
        this.isScanning = false;
        setTimeout(() => {
          const data = res.data.data; 
          this.merchantName = data.firma_adi || '';
          this.vknTckn = data.vkn_tckn || '';
          this.receiptDate = this.formatOcrDate(data.tarih);
          this.fisNo = data.fis_no || '';
          
          const ocrTotal = data.toplam_tutar || 0;

          if (data.kdv_detaylari && data.kdv_detaylari.length > 0) {
            this.items = data.kdv_detaylari.map((detail: any) => ({
              itemName: 'KDV Satırı',
              quantity: 1,
              unitPrice: detail.matrah || 0,
              totalPrice: detail.toplam_tutar || 0,
              taxRate: detail.kdv_orani || 20
            }));
          } else {
            const ocrTaxRate = data.kdv_orani_yuzde || 20;
            const ocrMatrah = Number((ocrTotal / (1 + ocrTaxRate / 100)).toFixed(2));
            this.items = [{
              itemName: 'KDV Satırı',
              quantity: 1,
              unitPrice: ocrMatrah,
              totalPrice: ocrTotal,
              taxRate: ocrTaxRate
            }];
          }

          this.calculateTotals();
          this.imagePath = res.data.imagePath || null;

          this.showPreview = true;
          this.showStatus('OCR tamamlandı!', 'success', 6000);
          this.cdr.detectChanges();
        }, 0);
      },
      error: (err) => {
        this.isScanning = false;
        this.showStatus('OCR başarısız oldu: ' + (err.error?.error || err.message), 'error', 8000);
        this.cdr.detectChanges();
      }
    });
  }

  renderPdfPage(): void {
    if (!this.pdfDocument) return;
    this.showStatus(`Sayfa ${this.pdfCurrentPage} çiziliyor...`, 'info', 2000);
    this.cdr.detectChanges();

    this.pdfDocument.getPage(this.pdfCurrentPage).then((page: any) => {
      const viewport = page.getViewport({ scale: 2.0 }); // 2.0x scale keeps it crisp for OCR
      const canvas = document.createElement('canvas');
      canvas.width = viewport.width;
      canvas.height = viewport.height;
      const context = canvas.getContext('2d');

      const renderContext = {
        canvasContext: context,
        viewport: viewport
      };

      page.render(renderContext).promise.then(() => {
        this.previewUrl = canvas.toDataURL('image/jpeg', 0.90);
        this.showPreview = true;
        
        canvas.toBlob((blob) => {
          if (blob) {
            this.selectedFile = new File([blob], `pdf_page_${this.pdfCurrentPage}.jpg`, { type: 'image/jpeg' });
            
            // Sayfa her yüklendiğinde/değiştiğinde otomatik tara
            this.scanActivePage();
          }
          this.cdr.detectChanges();
        }, 'image/jpeg', 0.90);
      });
    });
  }

  prevPdfPage(): void {
    if (this.pdfCurrentPage > 1) {
      this.pdfCurrentPage--;
      this.clearFormInputsOnly();
      this.renderPdfPage();
    }
  }

  nextPdfPage(): void {
    if (this.pdfCurrentPage < this.pdfTotalPages) {
      this.pdfCurrentPage++;
      this.clearFormInputsOnly();
      this.renderPdfPage();
    }
  }

  scanActivePage(): void {
    if (this.selectedFile) {
      this.triggerOcrScan(this.selectedFile);
    } else {
      this.showStatus('Taranacak sayfa resmi hazır değil.', 'error', 4000);
    }
  }

  onDragOver(event: DragEvent): void {
    event.preventDefault();
    event.stopPropagation();
    this.isDragOver = true;
    this.cdr.detectChanges();
  }

  onDragLeave(event: DragEvent): void {
    event.preventDefault();
    event.stopPropagation();
    this.isDragOver = false;
    this.cdr.detectChanges();
  }

  onDrop(event: DragEvent): void {
    event.preventDefault();
    event.stopPropagation();
    this.isDragOver = false;
    this.cdr.detectChanges();

    if (event.dataTransfer && event.dataTransfer.files.length > 0) {
      const file = event.dataTransfer.files[0];
      if (file.type.startsWith('image/') || file.type === 'application/pdf') {
        setTimeout(() => {
          this.processOcrFile(file);
        }, 0);
      } else {
        this.showStatus('Lütfen geçerli bir belge veya görsel sürükleyin (PNG, JPG, JPEG, PDF).', 'error', 4000);
      }
    }
  }

  @HostListener('window:dragover', ['$event'])
  onWindowDragOver(event: DragEvent): void {
    event.preventDefault();
  }

  @HostListener('window:drop', ['$event'])
  onWindowDrop(event: DragEvent): void {
    event.preventDefault();
  }

  @HostListener('window:paste', ['$event'])
  onPaste(event: ClipboardEvent): void {
    const items = event.clipboardData?.items;
    if (items) {
      for (let i = 0; i < items.length; i++) {
        if (items[i].type.indexOf('image') !== -1) {
          const file = items[i].getAsFile();
          if (file) {
            event.preventDefault();
            this.showStatus('Panodan görsel algılandı, işleniyor...', 'info', 2000);
            setTimeout(() => {
              this.processOcrFile(file);
            }, 0);
            break;
          }
        }
      }
    }
  }

  resetInput(): void {
    this.previewUrl = null;
    this.selectedFile = null;
    this.isPdf = false;
    this.safePdfUrl = null;
    this.pdfDocument = null;
    this.pdfCurrentPage = 1;
    this.pdfTotalPages = 0;
    this.showPreview = false;
    this.clearStatus();
    this.resetZoom();
  }

  // === ZOOM & PAN METHODS ===
  zoomIn(factor: number = 0.2): void {
    this.zoomLevel = Math.min(this.zoomLevel + factor, 5);
    this.cdr.detectChanges();
  }

  zoomOut(factor: number = 0.2): void {
    this.zoomLevel = Math.max(this.zoomLevel - factor, 1);
    if (this.zoomLevel === 1) {
      this.panX = 0;
      this.panY = 0;
    }
    this.cdr.detectChanges();
  }

  resetZoom(): void {
    this.zoomLevel = 1;
    this.panX = 0;
    this.panY = 0;
    this.isPanning = false;
    this.cdr.detectChanges();
  }

  startPan(event: MouseEvent): void {
    if (this.zoomLevel > 1) {
      event.preventDefault();
      this.isPanning = true;
      this.startX = event.clientX - this.panX;
      this.startY = event.clientY - this.panY;
      this.cdr.detectChanges();
    }
  }

  pan(event: MouseEvent): void {
    if (this.isPanning && this.zoomLevel > 1) {
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

  @HostListener('wheel', ['$event'])
  onWheel(event: WheelEvent): void {
    const target = event.target as HTMLElement;
    if (target && (target.classList.contains('captured-image') || target.closest('.preview-wrapper'))) {
      event.preventDefault();
      const zoomFactor = 0.1;
      if (event.deltaY < 0) {
        this.zoomIn(zoomFactor);
      } else {
        this.zoomOut(zoomFactor);
      }
    }
  }

  // === SPREADSHEET FORM METHODS ===
  addItem(): void {
    this.items.push({
      itemName: 'KDV Satırı',
      quantity: 1,
      unitPrice: 0,
      totalPrice: 0,
      taxRate: 20
    });
    this.calculateTotals();
  }

  removeItem(index: number): void {
    this.items.splice(index, 1);
    this.calculateTotals();
  }

  onItemChange(item: ReceiptItem, field: 'taxRate' | 'unitPrice' | 'totalPrice'): void {
    if (field === 'unitPrice' || field === 'taxRate') {
      const taxAmount = item.unitPrice * (item.taxRate / 100);
      item.totalPrice = Number((item.unitPrice + taxAmount).toFixed(2));
    } else if (field === 'totalPrice') {
      item.unitPrice = Number((item.totalPrice / (1 + item.taxRate / 100)).toFixed(2));
    }
    this.calculateTotals();
  }

  calculateTotals(): void {
    let total = 0;
    let tax = 0;
    this.items.forEach(item => {
      total += item.totalPrice;
      const taxPart = item.totalPrice - item.unitPrice;
      tax += taxPart;
    });
    this.totalAmount = Number(total.toFixed(2));
    this.taxAmount = Number(tax.toFixed(2));
  }

  clearFormInputsOnly(): void {
    this.isInspectMode = false;
    this.receiptId = null;
    this.merchantName = '';
    this.vknTckn = '';
    this.receiptDate = new Date().toISOString().substring(0, 10);
    this.fisNo = '';
    this.totalAmount = 0;
    this.taxAmount = 0;
    this.imagePath = null;
    this.items = [{
      itemName: 'KDV Satırı',
      quantity: 1,
      unitPrice: 0,
      totalPrice: 0,
      taxRate: 20
    }];
    this.cdr.detectChanges();
  }

  clearForm(): void {
    this.isInspectMode = false;
    this.receiptId = null;
    this.merchantName = '';
    this.vknTckn = '';
    this.receiptDate = new Date().toISOString().substring(0, 10);
    this.fisNo = '';
    this.totalAmount = 0;
    this.taxAmount = 0;
    this.imagePath = null;
    this.items = [{
      itemName: 'KDV Satırı',
      quantity: 1,
      unitPrice: 0,
      totalPrice: 0,
      taxRate: 20
    }];
    this.isPdf = false;
    this.safePdfUrl = null;
    this.pdfDocument = null;
    this.pdfCurrentPage = 1;
    this.pdfTotalPages = 0;
    this.resetInput();
    this.cdr.detectChanges(); // Temizlendikten sonra arayüzü zorla yenile
  }

  onReceiptSaved(): void {
    if (this.isPdf && this.pdfDocument) {
      // PDF modundaysak sadece orta paneldeki form verilerini sıfırlayalım, PDF belgesini koruyalım
      this.receiptId = null;
      this.isInspectMode = false;
      this.merchantName = '';
      this.vknTckn = '';
      this.receiptDate = new Date().toISOString().substring(0, 10);
      this.fisNo = '';
      this.totalAmount = 0;
      this.taxAmount = 0;
      this.items = [{
        itemName: 'KDV Satırı',
        quantity: 1,
        unitPrice: 0,
        totalPrice: 0,
        taxRate: 20
      }];
      
      if (this.pdfCurrentPage < this.pdfTotalPages) {
        // Sonraki sayfaya otomatik geçiş yapalım, render işlemi otomatik olarak taramayı başlatacaktır
        this.pdfCurrentPage++;
        this.renderPdfPage();
      } else {
        // Tüm sayfalar bittiğinde PDF'i kapatmıyoruz, sadece kullanıcıya bildiriyoruz
        this.showStatus('Tüm PDF sayfaları başarıyla kaydedildi!', 'success');
      }
      this.cdr.detectChanges();
    } else {
      // Normal görsel ise doğrudan tüm formu temizle
      this.clearForm();
    }
  }

  saveReceipt(): void {
    if (this.loading) return;

    if (!this.merchantName.trim()) {
      this.showStatus('Lütfen Mağaza Adını girin.', 'error');
      return;
    }

    this.loading = true;
    this.showStatus('Kaydediliyor...', 'info');
    this.cdr.detectChanges();

    const payload = {
      id: this.receiptId || 0,
      merchantName: this.merchantName,
      receiptDate: this.receiptDate,
      fisNo: this.fisNo,
      vknTckn: this.vknTckn,
      totalAmount: this.totalAmount,
      taxAmount: this.taxAmount,
      imagePath: this.imagePath,
      createdBy: localStorage.getItem('username') || 'default',
      items: this.items.map(i => ({
        itemName: i.itemName,
        quantity: i.quantity,
        unitPrice: i.unitPrice,
        totalPrice: i.totalPrice,
        taxRate: i.taxRate
      }))
    };

    const req = this.receiptId 
      ? this.apiService.updateReceipt(this.receiptId, payload)
      : this.apiService.saveReceipt(payload);

    req.subscribe({
      next: (res) => {
        this.showStatus('İşlem tamamlandı!', 'success');
        this.fetchReceiptsList();
        this.cdr.detectChanges();

        setTimeout(() => {
          this.loading = false;
          this.onReceiptSaved();
          this.cdr.detectChanges();
        }, 1200);
      },
      error: (err) => {
        this.loading = false;
        const errorMsg = err.error?.error || err.error?.message || err.message;
        this.showStatus('Kayıt başarısız oldu: ' + errorMsg, 'error', 8000);
        this.cdr.detectChanges();
      }
    });
  }

  cancelEdit(): void {
    this.clearForm();
  }

  downloadExcel(): void {
    const username = localStorage.getItem('username') || 'default';
    window.open(`http://localhost:5000/api/receipts/export?username=${encodeURIComponent(username)}`, '_blank');
  }

  // === ARCHIVE METHODS ===
  fetchReceiptsList(): void {
    this.loadingArchive = true;
    this.cdr.detectChanges();
    this.apiService.getReceipts().subscribe({
      next: (data) => {
        this.receiptsList = data;
        this.filterReceipts();
        this.loadingArchive = false;
        this.cdr.detectChanges();
      },
      error: (err) => {
        this.loadingArchive = false;
        this.cdr.detectChanges();
      }
    });
  }

  filterReceipts(): void {
    const groupedList = this.groupReceipts(this.receiptsList);

    if (!this.searchQuery.trim()) {
      const oneWeekAgo = new Date();
      oneWeekAgo.setDate(oneWeekAgo.getDate() - 7);
      
      this.filteredReceipts = groupedList.filter(r => {
        if (!r.createdAt) return false;
        const createdDate = new Date(r.createdAt.replace(' ', 'T'));
        return createdDate >= oneWeekAgo;
      });
      return;
    }
    const q = this.searchQuery.toLowerCase();
    this.filteredReceipts = groupedList.filter(r => 
      r.merchantName.toLowerCase().includes(q) ||
      r.receiptDate.toLowerCase().includes(q) ||
      r.id.toString().includes(q)
    );
  }

  groupReceipts(list: any[]): any[] {
    const groups: { [key: string]: any } = {};
    
    list.forEach(r => {
      const key = `${(r.merchantName || '').toLowerCase()}_${r.receiptDate}_${(r.fisNo || '').toLowerCase()}_${r.createdAt}_${(r.createdBy || '').toLowerCase()}`;
      
      if (!groups[key]) {
        groups[key] = {
          id: r.id,
          merchantName: r.merchantName,
          receiptDate: r.receiptDate,
          createdAt: r.createdAt,
          fisNo: r.fisNo,
          vknTckn: r.vknTckn,
          totalAmount: r.fisinGenelToplami || r.totalAmount,
          taxAmount: r.taxAmount,
          fisinGenelToplami: r.fisinGenelToplami,
          createdBy: r.createdBy,
          ids: [r.id]
        };
      } else {
        groups[key].ids.push(r.id);
        groups[key].taxAmount += r.taxAmount;
        if (!groups[key].fisinGenelToplami) {
          groups[key].totalAmount += r.totalAmount;
        }
      }
    });
    
    return Object.values(groups);
  }

  loadReceiptForEdit(id: number): void {
    this.isInspectMode = false;
    this.showStatus('Fatura bilgileri forma yükleniyor...', 'info');
    this.cdr.detectChanges();
    this.apiService.getReceiptDetails(id).subscribe({
      next: (data) => {
        this.resetZoom();
        this.receiptId = data.id;
        this.merchantName = data.merchantName;
        this.vknTckn = data.vknTckn || '';
        this.receiptDate = data.receiptDate;
        this.fisNo = data.fis_no || '';
        this.totalAmount = data.totalAmount;
        this.taxAmount = data.taxAmount;
        this.imagePath = data.imagePath;

        if (data.items && data.items.length > 0) {
          this.items = data.items.map((i: any) => ({
            itemName: i.itemName || 'KDV Satırı',
            quantity: i.quantity || 1,
            unitPrice: i.unitPrice || 0,
            totalPrice: i.totalPrice || 0,
            taxRate: i.taxRate || i.tax_rate || 20
          }));
        } else {
          const fallbackMatrah = Number((this.totalAmount - this.taxAmount).toFixed(2));
          this.items = [{
            itemName: 'KDV Satırı',
            quantity: 1,
            unitPrice: fallbackMatrah,
            totalPrice: this.totalAmount,
            taxRate: 20
          }];
        }

        if (data.imagePath) {
          this.previewUrl = `http://localhost:5000/processed/${data.imagePath}`;
          this.isPdf = data.imagePath.toLowerCase().endsWith('.pdf');
          this.safePdfUrl = null;
          this.pdfDocument = null;
          this.pdfCurrentPage = 1;
          this.pdfTotalPages = 0;
          this.showPreview = true;

          if (this.isPdf) {
            fetch(this.previewUrl)
              .then(res => res.arrayBuffer())
              .then(arrayBuffer => {
                const pdfjsLib = (window as any).pdfjsLib;
                if (pdfjsLib) {
                  pdfjsLib.GlobalWorkerOptions.workerSrc = 'https://cdnjs.cloudflare.com/ajax/libs/pdf.js/3.4.120/pdf.worker.min.js';
                  pdfjsLib.getDocument({ data: arrayBuffer }).promise.then((pdf: any) => {
                    this.pdfDocument = pdf;
                    this.pdfTotalPages = pdf.numPages;
                    this.pdfCurrentPage = 1;
                    // Render page without auto-scanning
                    this.pdfDocument.getPage(this.pdfCurrentPage).then((page: any) => {
                      const viewport = page.getViewport({ scale: 2.0 });
                      const canvas = document.createElement('canvas');
                      canvas.width = viewport.width;
                      canvas.height = viewport.height;
                      const context = canvas.getContext('2d');
                      page.render({ canvasContext: context, viewport: viewport }).promise.then(() => {
                        this.previewUrl = canvas.toDataURL('image/jpeg', 0.90);
                        canvas.toBlob((blob) => {
                          if (blob) {
                            this.selectedFile = new File([blob], `pdf_page_${this.pdfCurrentPage}.jpg`, { type: 'image/jpeg' });
                          }
                          this.cdr.detectChanges();
                        }, 'image/jpeg', 0.90);
                      });
                    });
                  });
                } else {
                  this.safePdfUrl = this.sanitizer.bypassSecurityTrustResourceUrl(this.previewUrl!);
                }
              })
              .catch(() => {
                this.safePdfUrl = this.sanitizer.bypassSecurityTrustResourceUrl(this.previewUrl!);
              });
          }
        } else {
          this.previewUrl = null;
          this.isPdf = false;
          this.safePdfUrl = null;
          this.pdfDocument = null;
          this.pdfCurrentPage = 1;
          this.pdfTotalPages = 0;
          this.showPreview = false;
        }
        
        this.showStatus('Fatura düzenleme moduna alındı.', 'success', 1500);
        this.cdr.detectChanges();
      },
      error: (err) => {
        const errorMsg = err.error?.error || err.error?.message || err.message;
        this.showStatus('Veri okuma hatası: ' + errorMsg, 'error', 5000);
        this.cdr.detectChanges();
      }
    });
  }



  showStatus(msg: string, type: 'success' | 'info' | 'error', durationMs: number = 0): void {
    if (this.statusTimeoutId) {
      clearTimeout(this.statusTimeoutId);
      this.statusTimeoutId = null;
    }
    this.statusMessage = msg;
    this.statusType = type;
    this.cdr.detectChanges(); // Mesaj değiştiğinde arayüzü anında yenile

    if (durationMs > 0) {
      this.statusTimeoutId = setTimeout(() => {
        this.clearStatus();
      }, durationMs);
    }
  }

  clearStatus(): void {
    this.statusMessage = '';
    this.statusType = null;
    this.cdr.detectChanges(); // Durum temizlendiğinde arayüzü yenile
  }

  private formatOcrDate(dateStr: string): string {
    if (!dateStr) return new Date().toISOString().substring(0, 10);
    
    dateStr = dateStr.trim();
    
    // YYYY-MM-DD kontrolü
    if (/^\d{4}-\d{2}-\d{2}$/.test(dateStr)) {
      return dateStr;
    }

    // GG.AA.YYYY veya GG/AA/YYYY veya GG-AA-YYYY parçalama
    const parts = dateStr.split(/[./-]/);
    if (parts.length === 3) {
      let day = parts[0].trim();
      let month = parts[1].trim();
      let year = parts[2].trim();

      // Eğer yıl 2 haneli geldiyse (örn: 26 -> 2026)
      if (year.length === 2) {
        year = '20' + year;
      }

      // Eğer ilk kısım yıl ise (YYYY.AA.GG)
      if (day.length === 4) {
        year = parts[0].trim();
        month = parts[1].trim();
        day = parts[2].trim();
      }

      // Hane tamamlama (örn: 5 -> 05)
      if (day.length === 1) day = '0' + day;
      if (month.length === 1) month = '0' + month;

      const formatted = `${year}-${month}-${day}`;
      // Geçerli bir tarih mi kontrol et
      if (!isNaN(Date.parse(formatted))) {
        return formatted;
      }
    }

    // Parse edilemezse bugünün tarihini yyyy-MM-dd formatında dön
    return new Date().toISOString().substring(0, 10);
  }

  loadReceiptForInspect(id: number): void {
    this.loadReceiptForEdit(id);
    this.isInspectMode = true;
    this.cdr.detectChanges();
  }

  cancelInspect(): void {
    this.isInspectMode = false;
    this.clearForm();
  }

  deleteReceipt(id: number): void {
    const confirmDelete = confirm('Bu faturayı silmek istediğinize emin misiniz?');
    if (!confirmDelete) return;

    this.showStatus('Fatura siliniyor...', 'info');
    const username = this.currentUsername || 'admin';
    this.apiService.deleteReceipt(id, username).subscribe({
      next: (res) => {
        this.showStatus('Fatura başarıyla silindi.', 'success', 4000);
        if (this.receiptId === id) {
          this.clearForm();
        }
        this.fetchReceiptsList();
        this.cdr.detectChanges();
      },
      error: (err) => {
        const errorMsg = err.error?.error || err.error?.message || err.message;
        this.showStatus('Silme işlemi başarısız oldu: ' + errorMsg, 'error', 5000);
        this.cdr.detectChanges();
      }
    });
  }
}
