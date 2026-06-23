@echo off
:: SQL Server Log Temizleme Çalıştırıcısı (localhost varsayılan sunucusu ile)
sqlcmd -S localhost -d ReceiptOcrDb -Q "EXEC sp_CleanOldLogs;"
echo Log temizleme islemi tetiklendi.
pause
