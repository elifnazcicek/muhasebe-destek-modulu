import { Component, OnInit, OnDestroy } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { ApiService } from '../../services/api.service';

@Component({
  selector: 'app-logs',
  standalone: true,
  imports: [CommonModule, FormsModule],
  templateUrl: './logs.component.html',
  styleUrls: ['./logs.component.css']
})
export class LogsComponent implements OnInit, OnDestroy {
  logContent: string = 'Yükleniyor...';
  backups: any[] = [];
  selectedBackup: string | null = null;
  autoRefreshInterval: any;
  loadingBackups: boolean = false;
  statusMessage: string = '';

  constructor(private apiService: ApiService) {}

  ngOnInit(): void {
    this.fetchLiveLogs();
    this.fetchBackups();
    
    // Auto-refresh live logs every 5 seconds
    this.autoRefreshInterval = setInterval(() => {
      if (!this.selectedBackup) {
        this.fetchLiveLogs();
      }
    }, 5000);
  }

  ngOnDestroy(): void {
    if (this.autoRefreshInterval) {
      clearInterval(this.autoRefreshInterval);
    }
  }

  fetchLiveLogs(): void {
    this.apiService.getLogs().subscribe({
      next: (data) => {
        this.logContent = data || 'Henüz log kaydı bulunmuyor.';
      },
      error: (err) => {
        this.logContent = 'Loglar alınırken hata oluştu: ' + err.message;
      }
    });
  }

  fetchBackups(): void {
    this.loadingBackups = true;
    this.apiService.getBackups().subscribe({
      next: (data) => {
        this.backups = data;
        this.loadingBackups = false;
      },
      error: (err) => {
        this.statusMessage = 'Yedekler yüklenemedi.';
        this.loadingBackups = false;
      }
    });
  }

  viewBackup(filename: string): void {
    this.selectedBackup = filename;
    this.logContent = 'Yedek log yükleniyor...';
    this.apiService.getBackupContent(filename).subscribe({
      next: (data) => {
        this.logContent = data;
      },
      error: (err) => {
        this.logContent = 'Yedek okuma hatası: ' + err.message;
      }
    });
  }

  showLiveLogs(): void {
    this.selectedBackup = null;
    this.logContent = 'Canlı loglar yükleniyor...';
    this.fetchLiveLogs();
  }

  triggerBackup(): void {
    this.statusMessage = 'Loglar yedekleniyor...';
    this.apiService.triggerBackup().subscribe({
      next: (res) => {
        this.statusMessage = 'Yedekleme başarılı!';
        this.fetchBackups();
        setTimeout(() => this.statusMessage = '', 3000);
      },
      error: (err) => {
        this.statusMessage = 'Yedekleme hatası: ' + err.message;
        setTimeout(() => this.statusMessage = '', 4000);
      }
    });
  }
}
