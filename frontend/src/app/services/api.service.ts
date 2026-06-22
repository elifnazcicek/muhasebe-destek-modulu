import { Injectable } from '@angular/core';

/**
 * Backend API İletişim Servisi
 * .NET 9.0 Web API'ye istek gönderir.
 */
@Injectable({ providedIn: 'root' })
export class ApiService {

  private readonly BASE_URL = 'http://localhost:5062';

  /**
   * İşlenmiş görseli backend'e gönderir (pre-processing + kayıt).
   * @param imageBlob - JPEG formatında işlenmiş görsel blob'u
   * @returns Backend API yanıtı
   */
  async uploadProcessedImage(imageBlob: Blob): Promise<PreprocessApiResponse> {
    const formData = new FormData();
    formData.append('file', imageBlob, 'receipt.jpg');

    const response = await fetch(`${this.BASE_URL}/api/receipt/preprocess`, {
      method: 'POST',
      body: formData
    });

    if (!response.ok) {
      const errorData = await response.json().catch(() => null);
      throw new Error(errorData?.error || `Sunucu hatası: ${response.status}`);
    }

    return response.json();
  }

  /**
   * Sunucu sağlık kontrolü.
   */
  async healthCheck(): Promise<{ status: string; message: string }> {
    const response = await fetch(this.BASE_URL);
    return response.json();
  }

  /**
   * İşlenmiş görselin indirme URL'sini oluşturur.
   */
  getDownloadUrl(filename: string): string {
    return `${this.BASE_URL}/api/receipt/download/${filename}`;
  }
}

// ===========================================================================
// API Yanıt Tipleri
// ===========================================================================

export interface PreprocessApiResponse {
  success: boolean;
  message: string;
  data?: {
    original: { filename: string; sizeKb: number };
    processed: { filename: string; sizeKb: number; downloadUrl: string };
    compressionRatio: string;
    appliedSteps: string[];
  };
  error?: string;
}
