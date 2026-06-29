@echo off
:: Türkçe karakter desteği için active code page'i UTF-8 yapalım
chcp 65001 > nul
echo ======================================================
echo   MASRAF FIŞI OKUMA OTOMASYONU BAŞLATILIYOR...
echo ======================================================
echo.

:: Bulunduğumuz klasörü (bat dosyasının olduğu yeri) çalışma dizini yap
cd /d "%~dp0"

:: 1. Backend API Sunucusunu Ayrı Pencerede Başlat
echo [1/3] Backend (API) Sunucusu başlatılıyor...
start "Masraf Fişi API Sunucusu" cmd /c "cd backend\ReceiptOCR.API && dotnet run"

:: 2. Frontend Web Sunucusunu Ayrı Pencerede Başlat
echo [2/3] Kullanıcı Arayüzü (Angular) başlatılıyor...
start "Masraf Fişi Web Arayüzü" cmd /c "cd frontend && npm start"

:: 3. Sunucuların ayağa kalkması için kısa bir süre bekle
echo [3/3] Tarayıcı açılıyor (Sunucuların hazır olması bekleniyor)...
timeout /t 8 /nobreak > nul

:: 4. Tarayıcıda uygulamayı otomatik aç
start http://localhost:4200

echo.
echo ======================================================
echo   Sistem Başlatıldı! 
echo   Lütfen açılan arka plan pencerelerini kapatmayınız.
echo   Uygulamayı kapatmak istediğinizde o pencereleri kapatabilirsiniz.
echo ======================================================
echo.
timeout /t 5 > nul
