-- =======================================================
-- PROJE: Muhasebe Fiş Okuma Sistemi Veritabanı Kurulumu
-- HEDEF: Microsoft SQL Server 2019+
-- AÇIKLAMA: SystemLogs ve Expenses tablolarının oluşturulması
-- =======================================================

-- 1. Veritabanını oluştur (Eğer yoksa)
IF NOT EXISTS (SELECT * FROM sys.databases WHERE name = 'ReceiptOcrDb')
BEGIN
    CREATE DATABASE ReceiptOcrDb;
    PRINT 'ReceiptOcrDb veritabanı başarıyla oluşturuldu.';
END
GO

-- Veritabanını aktif et
USE ReceiptOcrDb;
GO

-- =======================================================
-- TABLO 1: SystemLogs (İşlem ve Hata Logları)
-- =======================================================
IF NOT EXISTS (SELECT * FROM sys.objects WHERE object_id = OBJECT_ID(N'[dbo].[SystemLogs]') AND type in (N'U'))
BEGIN
    CREATE TABLE [dbo].[SystemLogs] (
        [Id]         INT IDENTITY(1,1) NOT NULL,
        [Timestamp]  DATETIME2(7)      NOT NULL DEFAULT GETDATE(),
        [Username]   NVARCHAR(50)      NOT NULL,
        [ActionType] NVARCHAR(50)      NOT NULL, -- Giriş, OCR_Okuma, Excel_Kayıt vb.
        [Status]     NVARCHAR(10)      NOT NULL, -- SUCCESS / ERROR
        [Details]    NVARCHAR(MAX)     NULL,     -- Hata detayı veya işlem açıklaması
        
        CONSTRAINT [PK_SystemLogs] PRIMARY KEY CLUSTERED ([Id] ASC)
    );
    
    -- Hızlı arama için indeksler
    CREATE NONCLUSTERED INDEX [IX_SystemLogs_Timestamp] ON [dbo].[SystemLogs] ([Timestamp] DESC);
    CREATE NONCLUSTERED INDEX [IX_SystemLogs_Username] ON [dbo].[SystemLogs] ([Username] ASC);
    
    PRINT 'SystemLogs tablosu ve indeksleri oluşturuldu.';
END
ELSE
BEGIN
    PRINT 'SystemLogs tablosu zaten mevcut.';
END
GO

-- =======================================================
-- TABLO 2: Expenses (Excel'e Yazılan Masraf Kayıtları)
-- =======================================================
IF NOT EXISTS (SELECT * FROM sys.objects WHERE object_id = OBJECT_ID(N'[dbo].[Expenses]') AND type in (N'U'))
BEGIN
    CREATE TABLE [dbo].[Expenses] (
        [Id]                INT IDENTITY(1,1) NOT NULL,
        [Tarih]             DATE              NOT NULL,
        [FirmaAdi]          NVARCHAR(255)     NOT NULL,
        [FisNo]             NVARCHAR(50)      NULL,
        [KdvOrani]          INT               NOT NULL DEFAULT 20,
        [ToplamTutar]       DECIMAL(10,2)     NOT NULL,
        [KaydedenKullanici] NVARCHAR(50)      NOT NULL,
        [CreatedDate]       DATETIME2(7)      NOT NULL DEFAULT GETDATE(),
        
        CONSTRAINT [PK_Expenses] PRIMARY KEY CLUSTERED ([Id] ASC)
    );
    
    -- Tarih ve firmaya göre sorguları hızlandırmak için indeksler
    CREATE NONCLUSTERED INDEX [IX_Expenses_Tarih] ON [dbo].[Expenses] ([Tarih] DESC);
    CREATE NONCLUSTERED INDEX [IX_Expenses_FirmaAdi] ON [dbo].[Expenses] ([FirmaAdi] ASC);

    PRINT 'Expenses tablosu ve indeksleri oluşturuldu.';
END
ELSE
BEGIN
    PRINT 'Expenses tablosu zaten mevcut.';
END
GO

-- =======================================================
-- SAKLI YORDAMLAR (STORED PROCEDURES)
-- =======================================================

USE ReceiptOcrDb;
GO

-- 1. 365 Günü Aşan Eski Logları Otomatik Temizleyen Prosedür
CREATE OR ALTER PROCEDURE sp_CleanOldLogs
AS
BEGIN
    SET NOCOUNT ON;
    
    DECLARE @DeletedRows INT = 0;
    
    -- 365 günden eski logları sil
    DELETE FROM dbo.SystemLogs
    WHERE [Timestamp] < DATEADD(day, -365, GETDATE());
    
    SET @DeletedRows = @@ROWCOUNT;
    
    -- Temizleme işlemini log tablosuna kaydet
    INSERT INTO dbo.SystemLogs (Username, ActionType, [Status], Details)
    VALUES ('System_Job', 'Log_Cleanup', 'SUCCESS', CONCAT(@DeletedRows, ' adet 365 günden eski log kaydı temizlendi.'));
    
    PRINT CONCAT(@DeletedRows, ' adet eski log kaydı başarıyla silindi.');
END;
GO

-- 2. Dinamik Tarihli Veritabanı Yedeği Alan Prosedür
CREATE OR ALTER PROCEDURE sp_BackupDatabase
    @BackupFolder NVARCHAR(500) = 'C:\Backup'
AS
BEGIN
    SET NOCOUNT ON;
    
    DECLARE @FileName NVARCHAR(1000);
    DECLARE @DateStr NVARCHAR(20);
    
    -- Klasör yoksa oluştur (Gerekli yetkiler olmalıdır)
    EXEC master.dbo.xp_create_subdir @BackupFolder;
    
    -- Tarih formatı: YYYY_MM_DD
    SET @DateStr = REPLACE(CONVERT(NVARCHAR(10), GETDATE(), 111), '/', '_');
    SET @FileName = CONCAT(@BackupFolder, '\yedek_', @DateStr, '.bak');
    
    -- Yedekleme komutunu çalıştır
    BACKUP DATABASE ReceiptOcrDb
    TO DISK = @FileName
    WITH FORMAT, INIT, NAME = N'ReceiptOcrDb-Daily Full Backup', SKIP, NOREWIND, NOUNLOAD, STATS = 10;
    
    -- Yedekleme işlemini log tablosuna kaydet
    INSERT INTO dbo.SystemLogs (Username, ActionType, [Status], Details)
    VALUES ('System_Job', 'Db_Backup', 'SUCCESS', CONCAT('Veritabanı yedeği başarıyla alındı: ', @FileName));
    
    PRINT CONCAT('Yedekleme tamamlandı: ', @FileName);
END;
GO

-- =======================================================
-- SQL SERVER AGENT JOBS (İŞ PLANLAYICI) TANIMLAMALARI
-- =======================================================
-- NOT: SQL Server Agent servisi yalnızca Express dışındaki 
-- (Developer, Standard, Enterprise) sürümlerde aktiftir.
-- =======================================================

USE msdb;
GO

-- 1. GÜNLÜK YEDEKLEME JOB'I (Her gün gece 00:00'da çalışır)
IF EXISTS (SELECT job_id FROM msdb.dbo.sysjobs WHERE name = N'ReceiptOcrDb_DailyBackup')
    EXEC msdb.dbo.sp_delete_job @job_name = N'ReceiptOcrDb_DailyBackup', @delete_unused_schedule = 1;
GO

-- Job Oluştur
EXEC msdb.dbo.sp_add_job 
    @job_name = N'ReceiptOcrDb_DailyBackup', 
    @enabled = 1, 
    @description = N'ReceiptOcrDb veritabanının günlük yedeklemesini yapar.', 
    @category_name = N'Database Maintenance';
GO

-- Job Adımı Ekle
EXEC msdb.dbo.sp_add_jobstep 
    @job_name = N'ReceiptOcrDb_DailyBackup', 
    @step_name = N'Execute Backup Stored Procedure', 
    @subsystem = N'TSQL', 
    @command = N'EXEC sp_BackupDatabase ''C:\Backup'';', 
    @database_name = N'ReceiptOcrDb';
GO

-- Zamanlama (Schedule) Tanımla: Her gün saat 00:00:00
EXEC msdb.dbo.sp_add_schedule 
    @schedule_name = N'Daily_Midnight_Backup', 
    @freq_type = 4, -- Günlük
    @freq_interval = 1, 
    @active_start_time = 000000;
GO

-- Zamanlamayı Job'a Bağla
EXEC msdb.dbo.sp_attach_schedule 
    @job_name = N'ReceiptOcrDb_DailyBackup', 
    @schedule_name = N'Daily_Midnight_Backup';
GO

-- İşi Hedef Sunucuya (Local) Ekle
EXEC msdb.dbo.sp_add_jobserver 
    @job_name = N'ReceiptOcrDb_DailyBackup', 
    @server_name = N'(local)';
GO

-- 2. GÜNLÜK ESKİ LOG TEMİZLEME JOB'I (Her gün gece 01:00'de çalışır)
IF EXISTS (SELECT job_id FROM msdb.dbo.sysjobs WHERE name = N'ReceiptOcrDb_DailyCleanup')
    EXEC msdb.dbo.sp_delete_job @job_name = N'ReceiptOcrDb_DailyCleanup', @delete_unused_schedule = 1;
GO

-- Job Oluştur
EXEC msdb.dbo.sp_add_job 
    @job_name = N'ReceiptOcrDb_DailyCleanup', 
    @enabled = 1, 
    @description = N'365 günü aşan eski log kayıtlarını temizler.', 
    @category_name = N'Database Maintenance';
GO

-- Job Adımı Ekle
EXEC msdb.dbo.sp_add_jobstep 
    @job_name = N'ReceiptOcrDb_DailyCleanup', 
    @step_name = N'Execute CleanOldLogs Stored Procedure', 
    @subsystem = N'TSQL', 
    @command = N'EXEC sp_CleanOldLogs;', 
    @database_name = N'ReceiptOcrDb';
GO

-- Zamanlama Tanımla: Her gün saat 01:00:00
EXEC msdb.dbo.sp_add_schedule 
    @schedule_name = N'Daily_01AM_Cleanup', 
    @freq_type = 4, -- Günlük
    @freq_interval = 1, 
    @active_start_time = 010000;
GO

-- Zamanlamayı Job'a Bağla
EXEC msdb.dbo.sp_attach_schedule 
    @job_name = N'ReceiptOcrDb_DailyCleanup', 
    @schedule_name = N'Daily_01AM_Cleanup';
GO

-- İşi Hedef Sunucuya (Local) Ekle
EXEC msdb.dbo.sp_add_jobserver 
    @job_name = N'ReceiptOcrDb_DailyCleanup', 
    @server_name = N'(local)';
GO

