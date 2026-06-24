@echo off
:: SQL Server Log Temizleme Çalıştırıcısı (localhost varsayılan sunucusu ile)
sqlcmd -S localhost -d ReceiptOcrDb -Q "EXEC sp_CleanOldLogs;"
echo Log temizleme islemi tetiklendi.

:: Diskteki 6 aydan (180 gün) eski yedek dosyalarini (.bak) temizle
echo Eski veritabani yedekleri (.bak) temizleniyor...
powershell -NoProfile -Command "$conn = New-Object System.Data.SqlClient.SqlConnection('Server=localhost;Database=ReceiptOcrDb;Trusted_Connection=True;TrustServerCertificate=True;'); $conn.Open(); $cmd = $conn.CreateCommand(); $cmd.CommandText = 'SELECT Value FROM Settings WHERE [Key] = ''BackupFolder'''; $folder = $cmd.ExecuteScalar(); $conn.Close(); if ([string]::IsNullOrEmpty($folder)) { $folder = 'C:\Backup' }; if (Test-Path $folder) { Get-ChildItem -Path $folder -Filter '*.bak' | Where-Object { $_.LastWriteTime -lt (Get-Date).AddDays(-180) } | Remove-Item -Force; echo 'Yedek dosyasi temizleme islemi tamamlandi.' } else { echo 'Yedek klasoru bulunamadi.' }"

pause
