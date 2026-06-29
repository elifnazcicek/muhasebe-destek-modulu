-- =============================================================================
-- RECEIPT OCR SİSTEMİ - VERİTABANI GÜNCELLEME (MİGRASYON) BETİĞİ
-- AÇIKLAMA: Dün kurulan veritabanını verileri kaybetmeden en son sürüme günceller.
-- KULLANIM: SSMS üzerinde "ReceiptOcrDb" veritabanı seçiliyken çalıştırın.
-- =============================================================================

USE ReceiptOcrDb;
GO

-- 1. Expenses Tablosuna Eksik Sütunları Ekle
PRINT '1. Expenses tablosu kontrol ediliyor...';

-- KdvOrani sütunu yoksa ekle
IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID('Expenses') AND name = 'KdvOrani')
BEGIN
    ALTER TABLE Expenses ADD KdvOrani INT NOT NULL DEFAULT 20;
    PRINT '- KdvOrani sütunu eklendi.';
END

-- Matrah sütunu yoksa ekle
IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID('Expenses') AND name = 'Matrah')
BEGIN
    ALTER TABLE Expenses ADD Matrah DECIMAL(10,2) NOT NULL DEFAULT 0.00;
    PRINT '- Matrah sütunu eklendi.';
END

-- FisinGenelToplami sütunu yoksa ekle
IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID('Expenses') AND name = 'FisinGenelToplami')
BEGIN
    ALTER TABLE Expenses ADD FisinGenelToplami DECIMAL(10,2) NOT NULL DEFAULT 0.00;
    PRINT '- FisinGenelToplami sütunu eklendi.';
END
GO

-- 2. Yeni ErrorLogs Tablosunu Oluştur (Eğer yoksa)
PRINT '2. ErrorLogs tablosu kontrol ediliyor...';
IF NOT EXISTS (SELECT * FROM sys.objects WHERE object_id = OBJECT_ID(N'[dbo].[ErrorLogs]') AND type in (N'U'))
BEGIN
    CREATE TABLE [dbo].[ErrorLogs] (
        [Id]           INT IDENTITY(1,1) NOT NULL,
        [Timestamp]    DATETIME2(7)      NOT NULL DEFAULT GETDATE(),
        [Username]     NVARCHAR(50)      NULL,
        [ActionType]   NVARCHAR(100)     NOT NULL,
        [ErrorMessage] NVARCHAR(MAX)     NOT NULL,
        [StackTrace]   NVARCHAR(MAX)     NULL,
        
        CONSTRAINT [PK_ErrorLogs] PRIMARY KEY CLUSTERED ([Id] ASC)
    );
    
    CREATE NONCLUSTERED INDEX [IX_ErrorLogs_Timestamp] ON [dbo].[ErrorLogs] ([Timestamp] DESC);
    PRINT '- ErrorLogs tablosu oluşturuldu.';
END
ELSE
BEGIN
    PRINT '- ErrorLogs tablosu zaten mevcut.';
END
GO

-- 3. Stored Procedure'leri Güncelle (Yeniden Oluştur)
PRINT '3. Stored Procedureler güncelleniyor...';
GO

CREATE OR ALTER PROCEDURE sp_CleanOldLogs
AS
BEGIN
    SET NOCOUNT ON;
    
    DECLARE @DeletedSystemRows INT = 0;
    DECLARE @DeletedErrorRows INT = 0;
    DECLARE @RetentionDays INT = 365;
    
    SELECT @RetentionDays = TRY_CAST([Value] AS INT) 
    FROM dbo.Settings 
    WHERE [Key] = 'LogRetentionDays';
    
    IF @RetentionDays IS NULL OR @RetentionDays <= 0
        SET @RetentionDays = 365;
    
    DELETE FROM dbo.SystemLogs
    WHERE [Timestamp] < DATEADD(day, -@RetentionDays, GETDATE());
    SET @DeletedSystemRows = @@ROWCOUNT;

    DELETE FROM dbo.ErrorLogs
    WHERE [Timestamp] < DATEADD(day, -30, GETDATE());
    SET @DeletedErrorRows = @@ROWCOUNT;
    
    INSERT INTO dbo.SystemLogs (Username, ActionType, [Status], Details)
    VALUES ('System_Job', 'Log_Cleanup', 'SUCCESS', CONCAT(@DeletedSystemRows, ' adet sistem logu (Limit: ', @RetentionDays, ' gün) ve ', @DeletedErrorRows, ' adet hata logu (Limit: 30 gün) temizlendi.'));
    
    PRINT CONCAT(@DeletedSystemRows, ' adet sistem logu ve ', @DeletedErrorRows, ' adet hata logu başarıyla silindi.');
END;
GO

PRINT '- sp_CleanOldLogs saklı yordamı güncellendi.';
PRINT 'VERİTABANI GÜNCELLEME İŞLEMİ BAŞARIYLA TAMAMLANDI!';
GO
