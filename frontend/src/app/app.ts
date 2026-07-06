import { Component, signal } from '@angular/core';
import { RouterOutlet, Router, RouterLink, RouterLinkActive } from '@angular/router';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';

@Component({
  selector: 'app-root',
  imports: [RouterOutlet, CommonModule, RouterLink, RouterLinkActive, FormsModule],
  templateUrl: './app.html',
  styleUrl: './app.scss'
})
export class App {
  protected readonly title = signal('frontend');
  isSidebarCollapsed = signal(false); // Açılır-kapanır sol panel durumu

  isEditingCompanyName = false;
  myCompanyName = '';
  myCompanyNameInput = '';

  constructor(private router: Router) {
    this.myCompanyName = localStorage.getItem('myCompanyName') || '';
    this.myCompanyNameInput = this.myCompanyName;
  }

  isLoggedIn(): boolean {
    return localStorage.getItem('isLoggedIn') === 'true';
  }

  getUsername(): string {
    return localStorage.getItem('username') || '';
  }

  isAdmin(): boolean {
    return localStorage.getItem('role') === 'Admin';
  }

  toggleSidebar(): void {
    this.isSidebarCollapsed.set(!this.isSidebarCollapsed());
  }

  logout(): void {
    localStorage.removeItem('isLoggedIn');
    localStorage.removeItem('username');
    localStorage.removeItem('token');
    this.router.navigate(['/login']);
  }

  getHeaderTitle(): string {
    const url = this.router.url;
    if (url.includes('/home')) {
      return 'Muhasebe Destek Modülü';
    } else if (url.includes('/dashboard')) {
      return 'Fiş Okuma Otomasyonu';
    } else if (url.includes('/dekont')) {
      return 'Uyumsoft e-Fatura Aktarımı';
    } else if (url.includes('/users')) {
      return 'Kullanıcı Yetki Yönetimi';
    }
    return 'Muhasebe Destek Modülü';
  }

  getHeaderSubtitle(): string {
    const url = this.router.url;
    if (url.includes('/home')) {
      return 'Sistem Özellikleri ve Hoş Geldiniz Paneli';
    } else if (url.includes('/dashboard')) {
      return 'Yapay Zeka Destekli Fiş Görseli Okuma ve Veritabanı Kayıt Sistemi';
    } else if (url.includes('/dekont')) {
      return 'XML ve PDF Fatura/Dekont Çözümleme ve Cari Eşleştirme Paneli';
    } else if (url.includes('/users')) {
      return 'Sisteme Kayıtlı Personelin Roller ve Erişim Durumlarının Yönetimi';
    }
    return 'Uyumsoft Entegrasyonu ve Otomasyon Sistemi';
  }

  showExcelButton(): boolean {
    const url = this.router.url;
    return url.includes('/dashboard') || url.includes('/dekont');
  }

  isDashboardRoute(): boolean {
    return this.router.url.includes('/dashboard');
  }

  isDekontRoute(): boolean {
    return this.router.url.includes('/dekont');
  }

  isHomeRoute(): boolean {
    return this.router.url.includes('/home');
  }

  downloadExcel(): void {
    const username = localStorage.getItem('username') || 'default';
    window.open(`http://localhost:5000/api/receipts/export?username=${encodeURIComponent(username)}`, '_blank');
  }

  downloadAlisExcel(): void {
    const username = localStorage.getItem('username') || 'default';
    window.open(`http://localhost:5000/api/dekont/export-alis?username=${encodeURIComponent(username)}`, '_blank');
  }

  downloadSatisExcel(): void {
    const username = localStorage.getItem('username') || 'default';
    window.open(`http://localhost:5000/api/dekont/export-satis?username=${encodeURIComponent(username)}`, '_blank');
  }

  enableCompanyNameEdit(): void {
    this.myCompanyNameInput = this.myCompanyName;
    this.isEditingCompanyName = true;
  }

  cancelCompanyNameEdit(): void {
    this.isEditingCompanyName = false;
  }

  saveCompanyName(): void {
    this.myCompanyName = this.myCompanyNameInput;
    localStorage.setItem('myCompanyName', this.myCompanyName);
    this.isEditingCompanyName = false;
  }
}
