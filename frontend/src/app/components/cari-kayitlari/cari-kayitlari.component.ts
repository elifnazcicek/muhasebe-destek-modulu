import { Component, OnInit, ChangeDetectorRef, HostListener } from '@angular/core';
import { CommonModule } from '@angular/common';
import { HttpClient } from '@angular/common/http';
import { FormsModule } from '@angular/forms';

@Component({
  selector: 'app-cari-kayitlari',
  standalone: true,
  imports: [CommonModule, FormsModule],
  templateUrl: './cari-kayitlari.component.html',
  styleUrl: './cari-kayitlari.component.css'
})
export class CariKayitlariComponent implements OnInit {
  private baseUrl = 'http://localhost:5000/api/dekont';

  loading = false;
  caris: any[] = [];
  filteredCaris: any[] = [];
  searchQuery = '';
  cariLimit = 100;
  totalCarisCount = 0;
  isScrolledDown = false;

  @HostListener('window:scroll', [])
  onWindowScroll() {
    this.isScrolledDown = window.scrollY > 150;
  }
  
  // Manual Cari Add
  newCariName = '';
  newCariVkn = '';

  // Status Alerts
  statusMessage: string | null = null;
  statusType: 'success' | 'info' | 'error' = 'info';

  constructor(
    private http: HttpClient,
    private cdr: ChangeDetectorRef
  ) {}

  ngOnInit(): void {
    this.fetchCaris();
  }

  showStatus(msg: string, type: 'success' | 'info' | 'error', duration: number = 4000): void {
    this.statusMessage = msg;
    this.statusType = type;
    this.cdr.detectChanges();
    if (duration > 0) {
      setTimeout(() => {
        if (this.statusMessage === msg) {
          this.statusMessage = null;
          this.cdr.detectChanges();
        }
      }, duration);
    }
  }

  fetchCaris(): void {
    this.loading = true;
    
    let url = `${this.baseUrl}/list-caris?limit=${this.cariLimit}`;
    if (this.searchQuery.trim()) {
      url += `&search=${encodeURIComponent(this.searchQuery.trim())}`;
    }

    this.http.get<any>(url).subscribe({
      next: (res) => {
        this.loading = false;
        if (res.success && res.data) {
          this.caris = res.data.list;
          this.totalCarisCount = res.data.totalCount;
          this.filteredCaris = [...this.caris];
        } else {
          this.showStatus(res.message || 'Cari kayıtları yüklenemedi.', 'error');
        }
        this.cdr.detectChanges();
      },
      error: (err) => {
        this.loading = false;
        this.showStatus(err.error?.message || 'Cari kayıtları yüklenirken hata oluştu.', 'error');
        this.cdr.detectChanges();
      }
    });
  }

  showMoreCaris(): void {
    this.cariLimit += 100;
    this.fetchCaris();
  }

  onSearch(): void {
    this.fetchCaris();
  }

  // Manuel Cari Ekleme
  addCariManually(): void {
    if (!this.newCariName.trim() || !this.newCariVkn.trim()) {
      this.showStatus('Lütfen tüm alanları doldurun.', 'error');
      return;
    }

    this.loading = true;
    this.showStatus('Cari kartı oluşturuluyor...', 'info', 0);

    const payload = {
      vkn: this.newCariVkn,
      cariAdi: this.newCariName
    };

    this.http.post<any>(`${this.baseUrl}/create-cari`, payload).subscribe({
      next: (res) => {
        this.loading = false;
        if (res.success) {
          this.newCariName = '';
          this.newCariVkn = '';
          this.showStatus('Cari kartı başarıyla oluşturuldu! Sayfa yenileniyor...', 'success', 5000);
          this.fetchCaris();
          setTimeout(() => {
            window.location.reload();
          }, 1500);
        } else {
          this.showStatus(res.message || 'Cari oluşturulamadı.', 'error');
        }
        this.cdr.detectChanges();
      },
      error: (err) => {
        this.loading = false;
        this.showStatus(err.error?.message || 'Cari oluşturulurken hata oluştu.', 'error');
        this.cdr.detectChanges();
      }
    });
  }

  isDragOver = false;

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

  handleFile(file: File): void {
    const ext = file.name.split('.').pop()?.toLowerCase();
    
    if (ext !== 'xlsx' && ext !== 'xls') {
      this.showStatus('Lütfen yalnızca geçerli bir Excel dosyası (.xlsx, .xls) yükleyiniz.', 'error', 5000);
      return;
    }

    this.loading = true;
    this.showStatus('Excel dosyası yükleniyor ve cari listesi çözümleniyor...', 'info', 0);

    const formData = new FormData();
    formData.append('file', file);

    // Excel Çözümleme
    this.http.post<any>(`${this.baseUrl}/parse-excel-cari`, formData).subscribe({
      next: (res) => {
        this.loading = false;
        if (res.success && res.data) {
          const count = res.data.addedCount;
          this.showStatus(`Excel başarıyla okundu! ${count} adet yeni cari veritabanına eklendi. Sayfa yenileniyor...`, 'success', 6000);
          this.fetchCaris();
          setTimeout(() => {
            window.location.reload();
          }, 1800);
        } else {
          this.showStatus(res.message || 'Excel dosyası işlenemedi.', 'error');
        }
        this.cdr.detectChanges();
      },
      error: (err) => {
        this.loading = false;
        this.showStatus(err.error?.message || 'Excel dosyası işlenirken hata oluştu.', 'error');
        this.cdr.detectChanges();
      }
    });
  }
}
