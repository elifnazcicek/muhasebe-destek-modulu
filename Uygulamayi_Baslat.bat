@echo off
title Fis Okuma Otomasyonu Baslatici

REM Yerel node ve dotnet yollarini PATH degiskenine ekle (Sisteme zarar vermeden sadece bu calisma icin)
SET "PATH=C:\Users\stajyer\.gemini\antigravity\scratch\dotnet;C:\Users\stajyer\.gemini\antigravity\scratch\node24;%PATH%"

echo ======================================================
echo   MASRAF FISI OKUMA OTOMASYONU BASLATILIYOR...
echo ======================================================
echo.

REM Bulundugumuz klasoru calisma dizini yap
cd /d "%~dp0"

REM 1. Backend API Sunucusunu Ayri Pencerede (Minimize) Baslat
echo [1/3] Backend (API) Sunucusu baslatiliyor...
start "Masraf Fisi API Sunucusu" /min cmd /c "cd backend\ReceiptOCR.API && dotnet run"

REM 2. Frontend Web Sunucusunu Ayri Pencerede (Minimize) Baslat
echo [2/3] Kullanici Arayuzu (Angular) baslatiliyor...
start "Masraf Fisi Web Arayuzu" /min cmd /c "cd frontend && npm start"

REM 3. Sunucularin ayaga kalkmasi icin kisa bir sure bekle
echo [3/3] Tarayici aciliyor (Sunucularin hazir olmasi bekleniyor)...
timeout /t 10 /nobreak > nul

REM 4. Tarayicida uygulamayi otomatik ac
start http://localhost:4200

echo.
echo ======================================================
echo   Sistem Basariyla Baslatildi! 
echo   Uygulamayi kapatmak istediginizde gorev cubugundaki
echo   pencereleri kapatabilirsiniz.
echo ======================================================
echo.
timeout /t 5 > nul
