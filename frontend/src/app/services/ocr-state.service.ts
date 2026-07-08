import { Injectable } from '@angular/core';

@Injectable({
  providedIn: 'root'
})
export class OcrStateService {
  // DashboardComponent (Fiş Okuma) State
  dashboardState: any = {
    showPreview: false,
    previewUrl: null,
    isPdf: false,
    safePdfUrl: null,
    pdfCurrentPage: 1,
    pdfTotalPages: 1,
    merchantName: '',
    vknTckn: '',
    receiptDate: '',
    fisNo: '',
    items: [],
    taxAmount: 0,
    totalAmount: 0,
    imagePath: null,
    receiptId: null,
    isInspectMode: false
  };

  // DekontComponent (Uyumsoft Aktarımı) State
  dekontState: any = {
    showPreview: false,
    previewUrl: null,
    isPdf: false,
    safePdfUrl: null,
    pdfCurrentPage: 1,
    pdfTotalPages: 1,
    parsedInvoices: [],
    selectedInvoiceIndex: 0
  };

  hasDashboardState(): boolean {
    return this.dashboardState.previewUrl !== null || this.dashboardState.merchantName !== '';
  }

  hasDekontState(): boolean {
    return this.dekontState.previewUrl !== null || (this.dekontState.parsedInvoices && this.dekontState.parsedInvoices.length > 0);
  }
}
