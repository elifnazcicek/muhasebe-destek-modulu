import { Component, OnInit, ChangeDetectorRef } from '@angular/core';
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
    this.http.get<any>(`${this.baseUrl}/list-caris`).subscribe({
      next: (res) => {
        this.loading = false;
        if (res.success && res.data) {
          this.caris = res.data;
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

  onSearch(): void {
    if (!this.searchQuery.trim()) {
      this.filteredCaris = [...this.caris];
    } else {
      const q = this.searchQuery.toLowerCase();
      this.filteredCaris = this.caris.filter(c =>
        c.cariKodu.toLowerCase().includes(q) ||
        c.cariAdi.toLowerCase().includes(q)
      );
    }
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
          this.showStatus('Cari kartı başarıyla oluşturuldu!', 'success', 5000);
          this.fetchCaris();
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

  // Dosya seçilince otomatik çözümleme ve Cari Kartı oluşturma
  onFileSelected(event: any): void {
    const file: File = event.target.files[0];
    if (!file) return;

    const ext = file.name.split('.').pop()?.toLowerCase();
    
    this.loading = true;
    this.showStatus('Dosya yükleniyor ve cari bilgileri çözümleniyor...', 'info', 0);

    const formData = new FormData();
    formData.append('file', file);

    if (ext === 'xlsx' || ext === 'xls') {
      // Excel Çözümleme
      this.http.post<any>(`${this.baseUrl}/parse-excel-cari`, formData).subscribe({
        next: (res) => {
          this.loading = false;
          if (res.success && res.data) {
            const count = res.data.addedCount;
            this.showStatus(`Excel başarıyla okundu! ${count} adet yeni cari veritabanına eklendi.`, 'success', 6000);
            this.fetchCaris();
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
    } else {
      // XML / PDF / Görsel Çözümleme
      this.http.post<any>(`${this.baseUrl}/parse-xml`, formData).subscribe({
        next: (res) => {
          if (res.success && res.data) {
            const extractedVkn = res.data.vkn;
            const extractedCariAdi = res.data.cariAdi;

            if (!extractedVkn || !extractedCariAdi) {
              this.loading = false;
              this.showStatus('Dosyada geçerli VKN veya Cari Ünvanı bulunamadı.', 'error', 5000);
              return;
            }

            // Cariyi kaydet
            this.showStatus(`Cari bilgileri tespit edildi. Kart oluşturuluyor: ${extractedCariAdi} (${extractedVkn})`, 'info', 0);
            const payload = {
              vkn: extractedVkn,
              cariAdi: extractedCariAdi
            };

            this.http.post<any>(`${this.baseUrl}/create-cari`, payload).subscribe({
              next: (createRes) => {
                this.loading = false;
                if (createRes.success) {
                  this.showStatus(`Dosyadaki cari başarıyla veritabanına kaydedildi: ${extractedCariAdi}`, 'success', 6000);
                  this.fetchCaris();
                } else {
                  this.showStatus(createRes.message || 'Cari oluşturulamadı.', 'error');
                }
                this.cdr.detectChanges();
              },
              error: (createErr) => {
                this.loading = false;
                this.showStatus(createErr.error?.message || 'Tespit edilen cari oluşturulurken hata oluştu.', 'error');
                this.cdr.detectChanges();
              }
            });
          } else {
            this.loading = false;
            this.showStatus(res.message || 'Dosya okunamadı.', 'error');
          }
          this.cdr.detectChanges();
        },
        error: (err) => {
          this.loading = false;
          this.showStatus(err.error?.message || 'Dosya okuma/çözümleme hatası.', 'error');
          this.cdr.detectChanges();
        }
      });
    }
  }
}
