-- AnalyticsServiceDb schema (Database First source of truth). Idempotent: safe to re-run.
IF DB_ID(N'AnalyticsServiceDb') IS NULL
    CREATE DATABASE AnalyticsServiceDb;
GO

USE AnalyticsServiceDb;
GO

IF OBJECT_ID(N'dbo.ClickEvents', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.ClickEvents
    (
        Id            BIGINT IDENTITY(1,1) NOT NULL CONSTRAINT PK_ClickEvents PRIMARY KEY,
        Code          NVARCHAR(16)  COLLATE Latin1_General_100_BIN2 NOT NULL,
        ClickedAtUtc  DATETIME2(3)  NOT NULL,
        Referrer      NVARCHAR(512) NULL,
        UserAgent     NVARCHAR(512) NULL
    );
    CREATE INDEX IX_ClickEvents_Code_ClickedAtUtc ON dbo.ClickEvents (Code, ClickedAtUtc);
END
GO

IF OBJECT_ID(N'dbo.LinkStats', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.LinkStats
    (
        Code              NVARCHAR(16) COLLATE Latin1_General_100_BIN2 NOT NULL CONSTRAINT PK_LinkStats PRIMARY KEY,
        TotalClicks       BIGINT       NOT NULL,
        LastClickedAtUtc  DATETIME2(3) NULL,
        UpdatedAtUtc      DATETIME2(3) NOT NULL
    );
END
GO

-- Per-link summary for the operator: precomputed totals plus a live 24h window.
CREATE OR ALTER VIEW dbo.vw_LinkClickSummary
AS
SELECT
    s.Code,
    s.TotalClicks,
    s.LastClickedAtUtc,
    s.UpdatedAtUtc,
    (SELECT COUNT_BIG(*)
       FROM dbo.ClickEvents c
      WHERE c.Code = s.Code
        AND c.ClickedAtUtc >= DATEADD(HOUR, -24, SYSUTCDATETIME())) AS ClicksLast24h
FROM dbo.LinkStats s;
GO

-- Rebuilds LinkStats from raw ClickEvents. Called on a timer by the analytics BackgroundService.
CREATE OR ALTER PROCEDURE dbo.usp_RecalculateLinkStats
AS
BEGIN
    SET NOCOUNT ON;

    MERGE dbo.LinkStats AS target
    USING (
        SELECT Code, COUNT_BIG(*) AS TotalClicks, MAX(ClickedAtUtc) AS LastClickedAtUtc
          FROM dbo.ClickEvents
         GROUP BY Code
    ) AS source
       ON target.Code = source.Code
    WHEN MATCHED THEN
        UPDATE SET TotalClicks      = source.TotalClicks,
                   LastClickedAtUtc = source.LastClickedAtUtc,
                   UpdatedAtUtc     = SYSUTCDATETIME()
    WHEN NOT MATCHED BY TARGET THEN
        INSERT (Code, TotalClicks, LastClickedAtUtc, UpdatedAtUtc)
        VALUES (source.Code, source.TotalClicks, source.LastClickedAtUtc, SYSUTCDATETIME());
END
GO
