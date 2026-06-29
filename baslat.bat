@echo off
title Fis Okuma Sistemi Baslatici

echo ===================================================
echo     FIS OKUMA VE ANALIZ SISTEMI BASLATILIYOR
echo ===================================================
echo.

echo [1/3] Eski calisan servisler sonlandiriliyor...
taskkill /f /im ReceiptOCR.API.exe 2>nul
taskkill /f /im node.exe 2>nul

echo [2/3] Backend (API) baslatiliyor...
cd /d "C:\Projeler\receipt_reg_auto\receipt_reg_auto\backend\ReceiptOCR.API\bin\Debug\net9.0-windows"
start /min "Fis Okuma Backend" "ReceiptOCR.API.exe"

echo [3/3] Frontend (Angular) baslatiliyor...
cd /d "C:\Projeler\receipt_reg_auto\receipt_reg_auto\frontend"
start /min "Fis Okuma Frontend" cmd /c "npm.cmd start"

echo.
echo Uygulamanin yuklenmesi icin 6 saniye bekleniyor...
timeout /t 6 /nobreak > nul

echo Tarayici aciliyor...
start http://localhost:4200

echo.
echo ===================================================
echo     SISTEM BASARIYLA BASLATILDI. BU PENCERE KAPANABILIR.
echo ===================================================
timeout /t 3 > nul
exit
