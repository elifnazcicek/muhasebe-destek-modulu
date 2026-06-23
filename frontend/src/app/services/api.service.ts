import { Injectable } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';

@Injectable({
  providedIn: 'root'
})
export class ApiService {
  private baseUrl = 'http://localhost:5000/api/receipts';

  constructor(private http: HttpClient) {}

  getReceipts(username?: string): Observable<any[]> {
    const url = username ? `${this.baseUrl}?username=${encodeURIComponent(username)}` : this.baseUrl;
    return this.http.get<any[]>(url);
  }

  // Get receipt detail by id
  getReceiptDetails(id: number): Observable<any> {
    return this.http.get<any>(`${this.baseUrl}/${id}`);
  }

  // Upload scanned receipt image
  uploadImage(file: File): Observable<any> {
    const formData = new FormData();
    formData.append('file', file);
    return this.http.post<any>(`${this.baseUrl}/upload`, formData);
  }

  // Parse raw OCR text
  ocrParse(text: string): Observable<any> {
    return this.http.post<any>(`${this.baseUrl}/ocr-parse`, { text });
  }

  // Save new receipt (db, excel, logs backup)
  saveReceipt(receipt: any): Observable<any> {
    return this.http.post<any>(this.baseUrl, receipt);
  }

  // Update existing receipt (db, excel, logs backup)
  updateReceipt(id: number, receipt: any): Observable<any> {
    return this.http.put<any>(`${this.baseUrl}/${id}`, receipt);
  }

  // Fetch backend logs
  getLogs(): Observable<string> {
    return this.http.get(`${this.baseUrl}/logs`, { responseType: 'text' });
  }

  // Trigger manual log backup
  triggerBackup(): Observable<any> {
    return this.http.post<any>(`${this.baseUrl}/logs/backup`, {});
  }

  // List log backup files
  getBackups(): Observable<any[]> {
    return this.http.get<any[]>(`${this.baseUrl}/logs/backups`);
  }

  // Fetch specific backup log content
  getBackupContent(filename: string): Observable<string> {
    return this.http.get(`${this.baseUrl}/logs/backups/${filename}`, { responseType: 'text' });
  }
}
