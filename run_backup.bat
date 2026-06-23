@echo off
:: SQL Server Veritabanı Yedekleme Çalıştırıcısı (Ayarlar Tablosundaki Klasör Yolunu Kullanır)
sqlcmd -S localhost -d ReceiptOcrDb -Q "EXEC sp_BackupDatabase;"
echo Yedekleme islemi tetiklendi.
pause
