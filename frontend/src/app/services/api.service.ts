import { Injectable } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';

@Injectable({
  providedIn: 'root'
})
export class ApiService {
  private baseUrl = 'http://localhost:5000/api';

  constructor(private http: HttpClient) {}

  // ==========================================
  // RECEIPT ENDPOINTS (Görüntü İşleme & OCR)
  // ==========================================

  // Görseli Gemini'ye gönderip OCR verisi almak
  scanReceipt(file: File | Blob): Observable<any> {
    const formData = new FormData();
    formData.append('file', file, 'receipt.jpg');
    return this.http.post<any>(`${this.baseUrl}/receipt/scan`, formData);
  }

  getReceipts(username?: string): Observable<any[]> {
    const url = username ? `${this.baseUrl}/receipt/history?username=${encodeURIComponent(username)}` : `${this.baseUrl}/receipt/history`;
    return this.http.get<any[]>(url);
  }

  getReceiptDetails(id: number): Observable<any> {
    return this.http.get<any>(`${this.baseUrl}/receipt/${id}`);
  }

  saveReceipt(receipt: any): Observable<any> {
    return this.http.post<any>(`${this.baseUrl}/receipt/confirm`, receipt);
  }

  updateReceipt(id: number, receipt: any): Observable<any> {
    return this.http.put<any>(`${this.baseUrl}/receipt/${id}`, receipt);
  }

  deleteReceipt(id: number, username: string): Observable<any> {
    return this.http.delete<any>(`${this.baseUrl}/receipt/${id}?username=${encodeURIComponent(username)}`);
  }



  // ==========================================================
  // AUTH ENDPOINTS (Kullanıcı Girişi ve Kayıt)
  // ==========================================================
  login(credentials: any): Observable<any> {
    return this.http.post<any>(`${this.baseUrl}/auth/login`, credentials);
  }

  register(credentials: any): Observable<any> {
    return this.http.post<any>(`${this.baseUrl}/auth/register`, credentials);
  }

  forgotPassword(username: string): Observable<any> {
    return this.http.post<any>(`${this.baseUrl}/auth/forgot-password`, { username });
  }

  resetPassword(payload: any): Observable<any> {
    return this.http.post<any>(`${this.baseUrl}/auth/reset-password`, payload);
  }

  // ==========================================
  // LOG ENDPOINTS (Sistem Kayıtları)
  // ==========================================
  getLogs(): Observable<string> {
    return this.http.get(`${this.baseUrl}/logs`, { responseType: 'text' });
  }

  triggerBackup(): Observable<any> {
    return this.http.post<any>(`${this.baseUrl}/logs/backup`, {});
  }

  getBackups(): Observable<any[]> {
    return this.http.get<any[]>(`${this.baseUrl}/logs/backups`);
  }

  getBackupContent(filename: string): Observable<string> {
    return this.http.get(`${this.baseUrl}/logs/backups/${filename}`, { responseType: 'text' });
  }

  // ==========================================
  // USER MANAGEMENT ENDPOINTS (Yönetici Yetkileri)
  // ==========================================
  getUsers(adminUsername: string): Observable<any[]> {
    return this.http.get<any[]>(`${this.baseUrl}/auth/users?adminUsername=${encodeURIComponent(adminUsername)}`);
  }

  updateUserRole(userId: number, adminUsername: string, role: string): Observable<any> {
    return this.http.put<any>(`${this.baseUrl}/auth/users/${userId}/role`, { adminUsername, role });
  }

  updateUserStatus(userId: number, adminUsername: string, isActive: boolean): Observable<any> {
    return this.http.put<any>(`${this.baseUrl}/auth/users/${userId}/status`, { adminUsername, isActive });
  }
}
