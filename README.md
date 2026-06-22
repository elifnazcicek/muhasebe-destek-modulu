# Fiş/Fatura OCR Sistemi

Kamera veya dosya yükleme ile fiş/fatura görsellerini tarayarak muhasebe sistemlerine otomatik veri girişi sağlayan web tabanlı yazılım.

## Proje Yapısı

```
receipt_ocr_system/
├── backend/
│   ├── app/
│   │   ├── __init__.py
│   │   ├── main.py              # FastAPI sunucusu (API endpoint'leri)
│   │   ├── preprocessing.py     # Görüntü ön işleme pipeline'ı (OpenCV)
│   │   └── utils/
│   │       ├── __init__.py
│   │       └── logger.py        # Merkezi loglama sistemi
│   ├── requirements.txt
│   └── test_preprocessing.py    # Pre-processing test scripti
├── frontend/                    # Web arayüzü (HTML/CSS/JS)
│   └── index.html               # (Ekip arkadaşı tarafından geliştirilecek)
├── .gitignore
└── README.md
```

## Kurulum

### 1. Sanal ortam oluştur ve aktifleştir
```bash
python -m venv venv

# Windows:
venv\Scripts\activate

# Linux/Mac:
source venv/bin/activate
```

### 2. Bağımlılıkları yükle
```bash
pip install -r backend/requirements.txt
```

### 3. Backend sunucusunu başlat
```bash
cd backend
uvicorn app.main:app --reload --port 8000
```

Sunucu `http://localhost:8000` adresinde çalışacaktır.

## API Endpoint'leri

| Method | Endpoint | Açıklama |
| :--- | :--- | :--- |
| `GET` | `/` | Sunucu durumu kontrolü |
| `POST` | `/api/preprocess` | Görsel yükle ve ön işlemden geçir |
| `GET` | `/api/download/{filename}` | İşlenmiş görseli indir |
| `GET` | `/api/logs?lines=20` | Son N satır logu oku |

### Örnek Kullanım (Frontend → Backend)

```javascript
const formData = new FormData();
formData.append('file', selectedFile);
formData.append('flip_horizontal', 'true');  // Webcam ise true

const response = await fetch('http://localhost:8000/api/preprocess', {
    method: 'POST',
    body: formData
});
const result = await response.json();
console.log(result);
```

## Pre-processing Pipeline (v2)

> **Kritik:** Gemini'nin renkli doku algısını bozmamak için Grayscale/Binarization **kullanılmaz**. Görüntü renkli (RGB) kalır.

1. **Horizontal Flip** — Webcam aynalama düzeltmesi
2. **Document Detection & Perspective Warp** — Belge tespiti, arka plan kırpma, kuşbakışı düzeltme
3. **Resize & JPEG Compression** — Maks 1500px, %80 kalite JPEG

## Ekip Çalışması

- `backend/` → Backend geliştirme
- `frontend/` → Frontend geliştirme (HTML/CSS/JS)
- Her iki taraf da `main` branch üzerinden birleştirme (merge) yapılır
