import { Component, OnInit, ChangeDetectorRef } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { Router } from '@angular/router';
import { ApiService } from '../../services/api.service';

@Component({
  selector: 'app-user-management',
  standalone: true,
  imports: [CommonModule, FormsModule],
  templateUrl: './user-management.component.html',
  styleUrls: ['./user-management.component.css']
})
export class UserManagementComponent implements OnInit {
  users: any[] = [];
  loading: boolean = false;
  adminUsername: string = '';
  currentUserRole: string = 'User';

  statusMessage: string = '';
  statusType: 'success' | 'info' | 'error' | null = null;
  statusTimeoutId: any = null;

  constructor(
    private apiService: ApiService,
    private cdr: ChangeDetectorRef,
    private router: Router
  ) {}

  ngOnInit(): void {
    this.adminUsername = localStorage.getItem('username') || '';
    this.currentUserRole = localStorage.getItem('role') || 'User';

    // Yetki kontrolü (Admin değilse ana sayfaya yönlendir)
    if (this.currentUserRole !== 'Admin') {
      this.router.navigate(['/dashboard']);
      return;
    }

    this.fetchUsers();
  }

  fetchUsers(): void {
    this.loading = true;
    this.showStatus('Kullanıcı listesi yükleniyor...', 'info');
    this.cdr.detectChanges();

    this.apiService.getUsers(this.adminUsername).subscribe({
      next: (data) => {
        this.users = data;
        this.loading = false;
        this.clearStatus();
        this.cdr.detectChanges();
      },
      error: (err) => {
        this.loading = false;
        const errorMsg = err.error?.error || err.error?.message || err.message;
        this.showStatus('Kullanıcılar yüklenemedi: ' + errorMsg, 'error');
        this.cdr.detectChanges();
      }
    });
  }

  toggleRole(user: any): void {
    const newRole = user.role === 'Admin' ? 'User' : 'Admin';
    const confirmChange = confirm(`${user.username} adlı kullanıcının rolünü ${newRole} yapmak istediğinize emin misiniz?`);
    if (!confirmChange) {
      // Değişikliği iptal et
      this.fetchUsers();
      return;
    }

    this.showStatus('Rol güncelleniyor...', 'info');
    this.cdr.detectChanges();

    this.apiService.updateUserRole(user.id, this.adminUsername, newRole).subscribe({
      next: () => {
        this.showStatus(`${user.username} rolü başarıyla ${newRole} olarak güncellendi.`, 'success', 3000);
        this.fetchUsers();
      },
      error: (err) => {
        const errorMsg = err.error?.error || err.error?.message || err.message;
        this.showStatus('Rol güncelleme hatası: ' + errorMsg, 'error', 4000);
        this.fetchUsers();
      }
    });
  }

  toggleStatus(user: any): void {
    const newStatus = !user.isActive;
    const confirmChange = confirm(`${user.username} adlı kullanıcının hesabını ${newStatus ? 'etkinleştirmek' : 'dondurmak'} istediğinize emin misiniz?`);
    if (!confirmChange) {
      // Değişikliği iptal et
      this.fetchUsers();
      return;
    }

    this.showStatus('Kullanıcı durumu güncelleniyor...', 'info');
    this.cdr.detectChanges();

    this.apiService.updateUserStatus(user.id, this.adminUsername, newStatus).subscribe({
      next: () => {
        const statusText = newStatus ? 'etkinleştirildi' : 'donduruldu';
        this.showStatus(`${user.username} hesabı başarıyla ${statusText}.`, 'success', 3000);
        this.fetchUsers();
      },
      error: (err) => {
        const errorMsg = err.error?.error || err.error?.message || err.message;
        this.showStatus('Durum güncelleme hatası: ' + errorMsg, 'error', 4000);
        this.fetchUsers();
      }
    });
  }

  goBack(): void {
    this.router.navigate(['/dashboard']);
  }

  showStatus(msg: string, type: 'success' | 'info' | 'error', durationMs: number = 0): void {
    if (this.statusTimeoutId) {
      clearTimeout(this.statusTimeoutId);
      this.statusTimeoutId = null;
    }
    this.statusMessage = msg;
    this.statusType = type;
    this.cdr.detectChanges();

    if (durationMs > 0) {
      this.statusTimeoutId = setTimeout(() => {
        this.clearStatus();
      }, durationMs);
    }
  }

  clearStatus(): void {
    this.statusMessage = '';
    this.statusType = null;
    this.cdr.detectChanges();
  }
}
