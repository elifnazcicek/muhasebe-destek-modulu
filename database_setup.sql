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
-- TABLO 3: Settings (Sistem Ayarları)
-- =======================================================
IF NOT EXISTS (SELECT * FROM sys.objects WHERE object_id = OBJECT_ID(N'[dbo].[Settings]') AND type in (N'U'))
BEGIN
    CREATE TABLE [dbo].[Settings] (
        [Key]         NVARCHAR(100) NOT NULL,
        [Value]       NVARCHAR(500) NOT NULL,
        [Description] NVARCHAR(255) NULL,
        
        CONSTRAINT [PK_Settings] PRIMARY KEY CLUSTERED ([Key] ASC)
    );
    
    -- Varsayılan ayarları yükle (Seed Data)
    INSERT INTO [dbo].[Settings] ([Key], [Value], [Description]) VALUES
    (N'GeminiApiKey', N'YOUR_API_KEY_HERE', N'Google Gemini API erişim anahtarı'),
    (N'ExcelPath', N'C:\Muhasebe\Masraflar.xlsx', N'Muhasebe masraf kayıtlarının yazılacağı Excel dosyasının yolu'),
    (N'DefaultVatRates', N'20,10,1', N'Sistemde tanımlı olan varsayılan KDV oranları (virgülle ayrılmış)'),
    (N'LogRetentionDays', N'365', N'Sistem loglarının veritabanında saklanacağı gün sayısı'),
    (N'BackupFolder', N'C:\Backup', N'Otomatik veritabanı yedeklerinin alınacağı klasör yolu');

    PRINT 'Settings tablosu oluşturuldu ve varsayılan ayarlar eklendi.';
END
ELSE
BEGIN
    PRINT 'Settings tablosu zaten mevcut.';
END
GO

-- =======================================================
-- TABLO 4: Users (Kullanıcılar ve Giriş Bilgileri)
-- =======================================================
IF NOT EXISTS (SELECT * FROM sys.objects WHERE object_id = OBJECT_ID(N'[dbo].[Users]') AND type in (N'U'))
BEGIN
    CREATE TABLE [dbo].[Users] (
        [Id]           INT IDENTITY(1,1) NOT NULL,
        [Username]     NVARCHAR(50)      NOT NULL,
        [PasswordHash] NVARCHAR(255)     NOT NULL, -- Güvenlik için hash'lenmiş şifre
        [FullName]     NVARCHAR(100)     NOT NULL,
        [Role]         NVARCHAR(20)      NOT NULL DEFAULT 'User', -- Admin, User, Auditor
        [IsActive]     BIT               NOT NULL DEFAULT 1,
        [CreatedDate]  DATETIME2(7)      NOT NULL DEFAULT GETDATE(),
        
        CONSTRAINT [PK_Users] PRIMARY KEY CLUSTERED ([Id] ASC),
        CONSTRAINT [UQ_Users_Username] UNIQUE ([Username])
    );
    
    -- Varsayılan kullanıcıları yükle (Varsayılan şifre: 123456)
    -- Şifrenin SHA-256 hash karşılığı eklenmiştir.
    INSERT INTO [dbo].[Users] ([Username], [PasswordHash], [FullName], [Role], [IsActive]) VALUES
    (N'stajyer', N'8d969ee567061054a737b4d0cd200774033b700b76e9867610e120b29d4e5c7e', N'Stajyer Kullanıcı', N'Admin', 1),
    (N'muhasebe1', N'8d969ee567061054a737b4d0cd200774033b700b76e9867610e120b29d4e5c7e', N'Ahmet Yılmaz', N'User', 1);

    PRINT 'Users tablosu oluşturuldu ve varsayılan kullanıcılar eklendi.';
END
ELSE
BEGIN
    PRINT 'Users tablosu zaten mevcut.';
END
GO

-- =======================================================
-- SAKLI YORDAMLAR (STORED PROCEDURES)
-- =======================================================

USE ReceiptOcrDb;
GO

-- 1. Ayarlardan Okuma Yapan Eski Log Temizleme Prosedürü
CREATE OR ALTER PROCEDURE sp_CleanOldLogs
AS
BEGIN
    SET NOCOUNT ON;
    
    DECLARE @DeletedRows INT = 0;
    DECLARE @RetentionDays INT = 365; -- Varsayılan gün sayısı
    
    -- Ayarlar tablosundan LogRetentionDays değerini oku
    SELECT @RetentionDays = TRY_CAST([Value] AS INT) 
    FROM dbo.Settings 
    WHERE [Key] = 'LogRetentionDays';
    
    -- Geçersiz değer kontrolü
    IF @RetentionDays IS NULL OR @RetentionDays <= 0
        SET @RetentionDays = 365;
    
    -- Belirlenen günden eski logları sil
    DELETE FROM dbo.SystemLogs
    WHERE [Timestamp] < DATEADD(day, -@RetentionDays, GETDATE());
    
    SET @DeletedRows = @@ROWCOUNT;
    
    -- Temizleme işlemini log tablosuna kaydet
    INSERT INTO dbo.SystemLogs (Username, ActionType, [Status], Details)
    VALUES ('System_Job', 'Log_Cleanup', 'SUCCESS', CONCAT(@DeletedRows, ' adet ', @RetentionDays, ' günden eski log kaydı temizlendi.'));
    
    PRINT CONCAT(@DeletedRows, ' adet eski log kaydı başarıyla silindi. (Limit: ', @RetentionDays, ' gün)');
END;
GO

-- 2. Ayarlardan Klasör Yolu Okuyan Yedekleme Prosedürü
CREATE OR ALTER PROCEDURE sp_BackupDatabase
    @BackupFolder NVARCHAR(500) = NULL
AS
BEGIN
    SET NOCOUNT ON;
    
    DECLARE @FileName NVARCHAR(1000);
    DECLARE @DateStr NVARCHAR(20);
    DECLARE @ActualBackupFolder NVARCHAR(500) = @BackupFolder;
    
    -- Eğer parametre olarak klasör verilmemişse, Ayarlar tablosundan oku
    IF @ActualBackupFolder IS NULL
    BEGIN
        SELECT @ActualBackupFolder = [Value] 
        FROM dbo.Settings 
        WHERE [Key] = 'BackupFolder';
    END
    
    -- Eğer ayarlarda da yoksa varsayılan klasörü kullan
    IF @ActualBackupFolder IS NULL OR @ActualBackupFolder = ''
        SET @ActualBackupFolder = 'C:\Backup';
    
    -- Klasör yoksa oluştur (Gerekli yetkiler olmalıdır)
    EXEC master.dbo.xp_create_subdir @ActualBackupFolder;
    
    -- Tarih formatı: YYYY_MM_DD
    SET @DateStr = REPLACE(CONVERT(NVARCHAR(10), GETDATE(), 111), '/', '_');
    SET @FileName = CONCAT(@ActualBackupFolder, '\yedek_', @DateStr, '.bak');
    
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

-- Job Adımı Ekle (Parametresiz çağırarak Ayarlar tablosundan okutuyoruz)
EXEC msdb.dbo.sp_add_jobstep 
    @job_name = N'ReceiptOcrDb_DailyBackup', 
    @step_name = N'Execute Backup Stored Procedure', 
    @subsystem = N'TSQL', 
    @command = N'EXEC sp_BackupDatabase;', 
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

