import { Component, OnInit, ChangeDetectorRef } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { ApiService } from '../../services/api.service';

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
  // === TABS & PANELS STATE ===
  leftTab: 'camera' | 'upload' = 'camera';
  showPreview: boolean = false;

  // === LEFT PANEL ===
  previewUrl: string | null = null;
  selectedFile: File | null = null;

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

  // === RIGHT PANEL: ARCHIVE ===
  receiptsList: any[] = [];
  filteredReceipts: any[] = [];
  searchQuery: string = '';
  loadingArchive: boolean = false;

  constructor(private apiService: ApiService, private cdr: ChangeDetectorRef) {}

  ngOnInit(): void {
    this.clearForm();
    this.fetchReceiptsList();
  }

  // === LEFT PANEL METHODS ===
  setLeftTab(tab: 'camera' | 'upload'): void {
    this.leftTab = tab;
  }

  // Mock capturing image
  captureImage(): void {
    this.showStatus('Kamera özelliği Dashboard üzerinden değil, Kamera sayfasından kullanılmalıdır.', 'info');
  }

  // File Upload OCR
  onFileSelected(event: any): void {
    const file = event.target.files[0];
    if (file) {
      this.selectedFile = file;
      
      const reader = new FileReader();
      reader.onload = (e: any) => {
        this.previewUrl = e.target.result;
        this.cdr.detectChanges();
      };
      reader.readAsDataURL(file);

      this.showStatus('Dosya yükleniyor ve Gemini OCR tarafından çözümleniyor...', 'info');
      this.cdr.detectChanges();
      
      // BİZİM GERÇEK .NET ENDPOINT'İMİZİ ÇAĞIRIR (/api/receipt/scan)
      this.apiService.scanReceipt(file).subscribe({
        next: (res) => {
          // Gemini'den dönen ExtractedReceiptData modeli
          const data = res.data; 
          this.merchantName = data.firma_adi || '';
          this.vknTckn = data.vkn_tckn || '';
          this.receiptDate = this.formatOcrDate(data.tarih);
          this.fisNo = data.fis_no || '';
          this.totalAmount = data.toplam_tutar || 0;
          const calculatedTax = (data.toplam_tutar * data.kdv_orani_yuzde) / (100 + data.kdv_orani_yuzde) || 0;
          this.taxAmount = Number(calculatedTax.toFixed(2));
          this.imagePath = null; // Opsiyonel, sunucudan dönen yolu atayabiliriz

          this.items = [];

          this.showPreview = true;
          this.showStatus('OCR tamamlandı!', 'success');
          this.cdr.detectChanges();
          setTimeout(() => {
            this.clearStatus();
            this.cdr.detectChanges();
          }, 2500);
        },
        error: (err) => {
          this.showStatus('Görüntü okunamadı: ' + err.message, 'error');
          this.cdr.detectChanges();
        }
      });
    }
  }

  resetInput(): void {
    this.previewUrl = null;
    this.selectedFile = null;
    this.showPreview = false;
    this.clearStatus();
  }

  // === SPREADSHEET FORM METHODS ===
  addItem(): void {
    this.items.push({
      itemName: 'Yeni Ürün',
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

  onItemChange(item: ReceiptItem): void {
    item.totalPrice = Number((item.quantity * item.unitPrice).toFixed(2));
    this.calculateTotals();
  }

  calculateTotals(): void {
    let total = 0;
    let tax = 0;
    this.items.forEach(item => {
      total += item.totalPrice;
      const taxPart = item.totalPrice * (item.taxRate / (100 + item.taxRate));
      tax += taxPart;
    });
    this.totalAmount = Number(total.toFixed(2));
    this.taxAmount = Number(tax.toFixed(2));
  }

  clearForm(): void {
    this.receiptId = null;
    this.merchantName = '';
    this.vknTckn = '';
    this.receiptDate = new Date().toISOString().substring(0, 10);
    this.fisNo = '';
    this.totalAmount = 0;
    this.taxAmount = 0;
    this.imagePath = null;
    this.items = [];
    this.resetInput();
  }

  saveReceipt(): void {
    if (!this.merchantName.trim()) {
      this.showStatus('Lütfen Mağaza Adını girin.', 'error');
      return;
    }

    const taxRate = this.taxAmount > 0 && this.totalAmount > this.taxAmount 
      ? Math.round((this.taxAmount / (this.totalAmount - this.taxAmount)) * 100) 
      : 20;

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
      items: [{
        itemName: 'Genel Gider',
        quantity: 1,
        unitPrice: this.totalAmount,
        totalPrice: this.totalAmount,
        taxRate: taxRate
      }]
    };

    this.showStatus('Kaydediliyor...', 'info');

    const req = this.receiptId 
      ? this.apiService.updateReceipt(this.receiptId, payload)
      : this.apiService.saveReceipt(payload);

    req.subscribe({
      next: (res) => {
        this.showStatus('İşlem tamamlandı!', 'success');
        this.fetchReceiptsList();
        this.cdr.detectChanges();

        setTimeout(() => {
          this.clearForm();
          this.cdr.detectChanges();
        }, 1200);
      },
      error: (err) => {
        this.showStatus('Kayıt başarısız oldu: ' + err.message, 'error');
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
    const currentUsername = localStorage.getItem('username') || '';
    this.apiService.getReceipts(currentUsername).subscribe({
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
    if (!this.searchQuery.trim()) {
      const oneWeekAgo = new Date();
      oneWeekAgo.setDate(oneWeekAgo.getDate() - 7);
      
      this.filteredReceipts = this.receiptsList.filter(r => {
        if (!r.createdAt) return false;
        const createdDate = new Date(r.createdAt.replace(' ', 'T'));
        return createdDate >= oneWeekAgo;
      });
      return;
    }
    const q = this.searchQuery.toLowerCase();
    this.filteredReceipts = this.receiptsList.filter(r => 
      r.merchantName.toLowerCase().includes(q) ||
      r.receiptDate.toLowerCase().includes(q) ||
      r.id.toString().includes(q)
    );
  }

  loadReceiptForEdit(id: number): void {
    this.showStatus('Fatura bilgileri forma yükleniyor...', 'info');
    this.cdr.detectChanges();
    this.apiService.getReceiptDetails(id).subscribe({
      next: (data) => {
        this.receiptId = data.id;
        this.merchantName = data.merchantName;
        this.vknTckn = data.vknTckn || '';
        this.receiptDate = data.receiptDate;
        this.fisNo = data.fisNo || '';
        this.totalAmount = data.totalAmount;
        this.taxAmount = data.taxAmount;
        this.imagePath = data.imagePath;

        this.items = [];

        if (data.imagePath) {
          this.previewUrl = `http://localhost:5000/${data.imagePath}`;
          this.showPreview = true;
        } else {
          this.previewUrl = null;
          this.showPreview = false;
        }
        
        this.showStatus('Fatura düzenleme moduna alındı.', 'success');
        this.cdr.detectChanges();
        setTimeout(() => {
          this.clearStatus();
          this.cdr.detectChanges();
        }, 1500);
      },
      error: (err) => {
        this.showStatus('Veri okuma hatası: ' + err.message, 'error');
        this.cdr.detectChanges();
      }
    });
  }

  showStatus(msg: string, type: 'success' | 'info' | 'error'): void {
    this.statusMessage = msg;
    this.statusType = type;
  }

  clearStatus(): void {
    this.statusMessage = '';
    this.statusType = null;
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
}
