import { Component, OnInit } from '@angular/core';
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
  receiptDate: string = '';
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

  constructor(private apiService: ApiService) {}

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
    this.showStatus('Fotoğraf çekiliyor ve okuma başlatılıyor...', 'info');
    
    this.apiService.ocrParse("STARBUCKS COFFEE\nTARİH: 22.06.2026\nCAPPACCINO GRANDE 95.00\nHAVUÇLU KEK 75.00\nKDV 10\nTOPLAM 170.00").subscribe({
      next: (res) => {
        this.merchantName = res.merchant_name;
        this.receiptDate = res.receipt_date;
        this.totalAmount = res.total_amount;
        this.taxAmount = res.tax_amount;
        this.imagePath = "data/receipt_sample.jpg";
        this.previewUrl = "https://images.unsplash.com/photo-1554415707-6e8cfc93fe23?w=500&auto=format&fit=crop";
        
        this.items = res.items.map((i: any) => ({
          itemName: i.item_name,
          quantity: i.quantity,
          unitPrice: i.unit_price,
          totalPrice: i.total_price,
          taxRate: i.tax_rate
        }));

        this.showPreview = true;
        this.showStatus('Fiş başarıyla okundu.', 'success');
        setTimeout(() => this.clearStatus(), 2000);
      },
      error: (err) => {
        this.showStatus('Kamera tarama hatası: ' + err.message, 'error');
      }
    });
  }

  // File Upload OCR
  onFileSelected(event: any): void {
    const file = event.target.files[0];
    if (file) {
      this.selectedFile = file;
      
      const reader = new FileReader();
      reader.onload = (e: any) => {
        this.previewUrl = e.target.result;
      };
      reader.readAsDataURL(file);

      this.showStatus('Dosya yükleniyor ve OCR çözümleniyor...', 'info');
      this.apiService.uploadImage(file).subscribe({
        next: (res) => {
          this.merchantName = res.merchant_name;
          this.receiptDate = res.receipt_date;
          this.totalAmount = res.total_amount;
          this.taxAmount = res.tax_amount;
          this.imagePath = res.image_path;

          this.items = res.items.map((i: any) => ({
            itemName: i.item_name,
            quantity: i.quantity,
            unitPrice: i.unit_price,
            totalPrice: i.total_price,
            taxRate: i.tax_rate
          }));

          this.showPreview = true;
          this.showStatus('OCR tamamlandı!', 'success');
          setTimeout(() => this.clearStatus(), 2500);
        },
        error: (err) => {
          this.showStatus('Görüntü okunamadı: ' + err.message, 'error');
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
    this.receiptDate = new Date().toISOString().substring(0, 10);
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
    if (this.items.length === 0) {
      this.showStatus('Lütfen ürün kalemlerini ekleyin.', 'error');
      return;
    }

    const payload = {
      id: this.receiptId || 0,
      merchantName: this.merchantName,
      receiptDate: this.receiptDate,
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

    this.showStatus('Kaydediliyor...', 'info');

    const req = this.receiptId 
      ? this.apiService.updateReceipt(this.receiptId, payload)
      : this.apiService.saveReceipt(payload);

    req.subscribe({
      next: (res) => {
        this.showStatus('İşlem tamamlandı!', 'success');
        this.fetchReceiptsList();

        setTimeout(() => {
          this.clearForm();
        }, 1200);
      },
      error: (err) => {
        this.showStatus('Kayıt başarısız oldu: ' + err.message, 'error');
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
    const currentUsername = localStorage.getItem('username') || '';
    this.apiService.getReceipts(currentUsername).subscribe({
      next: (data) => {
        this.receiptsList = data;
        this.filterReceipts();
        this.loadingArchive = false;
      },
      error: (err) => {
        this.loadingArchive = false;
      }
    });
  }

  filterReceipts(): void {
    if (!this.searchQuery.trim()) {
      this.filteredReceipts = this.receiptsList;
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
    this.apiService.getReceiptDetails(id).subscribe({
      next: (data) => {
        this.receiptId = data.id;
        this.merchantName = data.merchantName;
        this.receiptDate = data.receiptDate;
        this.totalAmount = data.totalAmount;
        this.taxAmount = data.taxAmount;
        this.imagePath = data.imagePath;

        this.items = data.items.map((i: any) => ({
          itemName: i.itemName,
          quantity: i.quantity,
          unitPrice: i.unitPrice,
          totalPrice: i.totalPrice,
          taxRate: i.tax_rate
        }));

        if (data.imagePath) {
          this.previewUrl = `http://localhost:5000/${data.imagePath}`;
          this.showPreview = true;
        } else {
          this.previewUrl = null;
          this.showPreview = false;
        }
        
        this.showStatus('Fatura düzenleme moduna alındı.', 'success');
        setTimeout(() => this.clearStatus(), 1500);
      },
      error: (err) => {
        this.showStatus('Veri okuma hatası: ' + err.message, 'error');
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
}
