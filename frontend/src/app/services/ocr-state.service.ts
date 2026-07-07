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
    evrakNo: 'F2026-AUTO',
    belgeNo: '',
    tarih: '',
    odemeTipi: 'Açık Hesap',
    VKN: '',
    cariKodu: '',
    cariAdi: '',
    isCariValid: false,
    detectedType: 'Alis',
    invoiceLines: [],
    araToplam: 0,
    kdvToplam: 0,
    genelToplam: 0,
    imageUrl: null,
    htmlPreviewContent: null
  };

  hasDashboardState(): boolean {
    return this.dashboardState.previewUrl !== null || this.dashboardState.merchantName !== '';
  }

  hasDekontState(): boolean {
    return this.dekontState.previewUrl !== null || this.dekontState.belgeNo !== '';
  }
}
