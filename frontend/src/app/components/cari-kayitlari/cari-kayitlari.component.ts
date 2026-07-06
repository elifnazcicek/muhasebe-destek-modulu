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
  errorMessage = '';

  constructor(
    private http: HttpClient,
    private cdr: ChangeDetectorRef
  ) {}

  ngOnInit(): void {
    this.fetchCaris();
  }

  fetchCaris(): void {
    this.loading = true;
    this.errorMessage = '';
    this.http.get<any>(`${this.baseUrl}/list-caris`).subscribe({
      next: (res) => {
        this.loading = false;
        if (res.success && res.data) {
          this.caris = res.data;
          this.filteredCaris = [...this.caris];
        } else {
          this.errorMessage = res.message || 'Cari kayıtları yüklenemedi.';
        }
        this.cdr.detectChanges();
      },
      error: (err) => {
        this.loading = false;
        this.errorMessage = err.error?.message || 'Cari kayıtları yüklenirken hata oluştu.';
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
        c.cariAdi.toLowerCase().includes(q) ||
        c.vkn.toLowerCase().includes(q)
      );
    }
  }
}
