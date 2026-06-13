SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;
SET NOCOUNT ON;
GO

IF OBJECT_ID(N'[dbo].[usp_InsertDocumentChunk]', N'P') IS NOT NULL
    DROP PROCEDURE [dbo].[usp_InsertDocumentChunk];
GO

CREATE PROCEDURE [dbo].[usp_InsertDocumentChunk]
    @DocumentId   BIGINT,
    @ChunkIndex   INT,
    @Content      NVARCHAR(MAX),
    @TokenCount   INT,
    @EmbeddingVec VARBINARY(MAX) = NULL
AS
BEGIN
    SET XACT_ABORT ON;

    INSERT INTO [Flower_DocumentChunk]
        (DocumentId, ChunkIndex, Content, TokenCount, EmbeddingVector, IsIndexed, CreatedAt)
    VALUES
        (@DocumentId, @ChunkIndex, @Content, @TokenCount, @EmbeddingVec, 0, SYSUTCDATETIME());

    UPDATE [Flower_Document]
    SET ChunkCount = ChunkCount + 1,
        ModifyTime = SYSUTCDATETIME()
    WHERE Id = @DocumentId;
END
GO

IF OBJECT_ID(N'[dbo].[usp_GetUnindexedDocuments]', N'P') IS NOT NULL
    DROP PROCEDURE [dbo].[usp_GetUnindexedDocuments];
GO

CREATE PROCEDURE [dbo].[usp_GetUnindexedDocuments]
    @BatchSize INT = 50
AS
BEGIN
    SET NOCOUNT ON;

    SELECT TOP (@BatchSize)
        d.Id,
        d.Title,
        d.Source,
        d.Category,
        d.CreatedAt,
        dc.Id       AS ChunkId,
        dc.ChunkIndex,
        dc.Content,
        dc.TokenCount
    FROM [Flower_Document] d
    INNER JOIN [Flower_DocumentChunk] dc ON d.Id = dc.DocumentId
    WHERE dc.IsIndexed = 0
      AND d.IsDeleted  = 0
    ORDER BY d.Id, dc.ChunkIndex;
END
GO

IF OBJECT_ID(N'[dbo].[usp_MarkChunkIndexed]', N'P') IS NOT NULL
    DROP PROCEDURE [dbo].[usp_MarkChunkIndexed];
GO

CREATE PROCEDURE [dbo].[usp_MarkChunkIndexed]
    @ChunkId BIGINT
AS
BEGIN
    SET NOCOUNT ON;

    UPDATE [Flower_DocumentChunk]
    SET IsIndexed = 1
    WHERE Id = @ChunkId;

    IF NOT EXISTS (
        SELECT 1 FROM [Flower_DocumentChunk]
        WHERE DocumentId = (SELECT DocumentId FROM [Flower_DocumentChunk] WHERE Id = @ChunkId)
          AND IsIndexed = 0
    )
    BEGIN
        UPDATE [Flower_Document]
        SET IsIndexed = 1,
            ModifyTime = SYSUTCDATETIME()
        WHERE Id = (SELECT DocumentId FROM [Flower_DocumentChunk] WHERE Id = @ChunkId);
    END
END
GO

IF OBJECT_ID(N'[dbo].[usp_LogChatMessage]', N'P') IS NOT NULL
    DROP PROCEDURE [dbo].[usp_LogChatMessage];
GO

CREATE PROCEDURE [dbo].[usp_LogChatMessage]
    @UserId       BIGINT,
    @SessionId    UNIQUEIDENTIFIER,
    @Role         NVARCHAR(32),
    @Content      NVARCHAR(MAX),
    @TokenCount   INT          = NULL,
    @ModelVersion NVARCHAR(64) = NULL,
    @LatencyMs    INT          = NULL
AS
BEGIN
    SET XACT_ABORT ON;

    INSERT INTO [Flower_ChatHistory]
        (Passport, SessionId, Role, Content, TokenCount, ModelVersion, LatencyMs, CreateTime)
    VALUES
        (@UserId, @SessionId, @Role, @Content, @TokenCount, @ModelVersion, @LatencyMs, SYSUTCDATETIME());

    SELECT SCOPE_IDENTITY() AS ChatId;
END
GO

IF OBJECT_ID(N'[dbo].[usp_DailyReportStats]', N'P') IS NOT NULL
    DROP PROCEDURE [dbo].[usp_DailyReportStats];
GO

CREATE PROCEDURE [dbo].[usp_DailyReportStats]
    @ReportDate DATE = NULL
AS
BEGIN
    SET NOCOUNT ON;

    IF @ReportDate IS NULL
        SET @ReportDate = CAST(SYSUTCDATETIME() AS DATE);

    DECLARE @PrevDate DATE = DATEADD(DAY, -1, @ReportDate);

    SELECT
        @ReportDate                                          AS ReportDate,
        COUNT(*)                                             AS TotalAlerts,
        SUM(CASE WHEN al.AlertType = 0 THEN 1 ELSE 0 END)   AS PriceAboveAlerts,
        SUM(CASE WHEN al.AlertType = 1 THEN 1 ELSE 0 END)   AS PriceBelowAlerts,
        SUM(CASE WHEN al.AlertType IN (2, 3) THEN 1 ELSE 0 END) AS PriceChangeAlerts
    FROM [Flower_AlertLog] al
    WHERE CAST(al.CreatedAt AS DATE) = @ReportDate;

    SELECT
        ms.SpeciesId,
        AVG(ms.AvgPrice)   AS AvgDailyPrice,
        MIN(ms.MinPrice)   AS DayMinPrice,
        MAX(ms.MaxPrice)   AS DayMaxPrice,
        SUM(ms.TradeCount) AS TotalTrades,
        SUM(ms.Volume)     AS TotalVolume
    FROM [Flower_MarketSnapshot] ms
    WHERE CAST(ms.SnapshotTime AS DATE) = @ReportDate
    GROUP BY ms.SpeciesId
    ORDER BY ms.SpeciesId;

    SELECT
        ch.Role,
        COUNT(*)            AS MessageCount,
        SUM(ISNULL(ch.TokenCount, 0)) AS TotalTokens,
        AVG(ISNULL(ch.LatencyMs, 0))  AS AvgLatencyMs
    FROM [Flower_ChatHistory] ch
    WHERE CAST(ch.CreateTime AS DATE) = @ReportDate
    GROUP BY ch.Role;

    SELECT
        COUNT(*) AS GeneratedReports
    FROM [Flower_GeneratedReport] gr
    WHERE CAST(gr.CreateTime AS DATE) = @ReportDate;

    SELECT
        o.Status,
        COUNT(*)              AS OrderCount,
        SUM(o.TotalAmount)    AS TotalAmount
    FROM [Flower_Order] o
    WHERE CAST(o.CreateTime AS DATE) = @ReportDate
    GROUP BY o.Status
    ORDER BY o.Status;
END
GO

IF OBJECT_ID(N'[dbo].[usp_AggregateDailyPriceStats]', N'P') IS NOT NULL
    DROP PROCEDURE [dbo].[usp_AggregateDailyPriceStats];
GO

CREATE PROCEDURE [dbo].[usp_AggregateDailyPriceStats]
    @TargetDate DATE = NULL
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    IF @TargetDate IS NULL
        SET @TargetDate = CAST(DATEADD(DAY, -1, SYSUTCDATETIME()) AS DATE);

    MERGE [Flower_DailyPriceStats] AS target
    USING (
        SELECT
            ms.SpeciesId,
            ms.MarketId,
            @TargetDate                 AS StatDate,
            AVG(ms.AvgPrice)            AS AvgPrice,
            MIN(ms.MinPrice)            AS MinPrice,
            MAX(ms.MaxPrice)            AS MaxPrice,
            SUM(ms.Volume)              AS TotalVolume,
            SUM(ms.TradeCount)          AS TotalTradeCount,
            STDEV(ms.AvgPrice)          AS PriceStdDev
        FROM [Flower_MarketSnapshot] ms
        WHERE CAST(ms.SnapshotTime AS DATE) = @TargetDate
        GROUP BY ms.SpeciesId, ms.MarketId
    ) AS src
    ON (target.SpeciesId = src.SpeciesId AND target.MarketId = src.MarketId AND target.StatDate = src.StatDate)
    WHEN MATCHED THEN
        UPDATE SET
            AvgPrice         = src.AvgPrice,
            MinPrice         = src.MinPrice,
            MaxPrice         = src.MaxPrice,
            TotalVolume      = src.TotalVolume,
            TotalTradeCount  = src.TotalTradeCount,
            PriceStdDev      = src.PriceStdDev,
            ModifyTime       = SYSUTCDATETIME()
    WHEN NOT MATCHED THEN
        INSERT (SpeciesId, MarketId, StatDate, AvgPrice, MinPrice, MaxPrice, TotalVolume, TotalTradeCount, PriceStdDev, CreateTime, ModifyTime)
        VALUES (src.SpeciesId, src.MarketId, src.StatDate, src.AvgPrice, src.MinPrice, src.MaxPrice, src.TotalVolume, src.TotalTradeCount, src.PriceStdDev, SYSUTCDATETIME(), SYSUTCDATETIME());
END
GO

IF OBJECT_ID(N'[dbo].[usp_EvaluateAlertRules]', N'P') IS NOT NULL
    DROP PROCEDURE [dbo].[usp_EvaluateAlertRules];
GO

CREATE PROCEDURE [dbo].[usp_EvaluateAlertRules]
AS
BEGIN
    SET NOCOUNT ON;

    DECLARE @Now DATETIME2 = SYSUTCDATETIME();

    INSERT INTO [Flower_AlertLog]
        (RuleId, UserId, SpeciesId, MarketId, AlertType, AlertMessage, TriggeredValue, ThresholdValue, IsRead, CreatedAt)
    SELECT
        ar.Id,
        ar.UserId,
        ar.SpeciesId,
        ar.MarketId,
        ar.ConditionType,
        CASE ar.ConditionType
            WHEN 0 THEN N'品种' + CAST(ar.SpeciesId AS NVARCHAR(10)) + N'价格高于阈值'
            WHEN 1 THEN N'品种' + CAST(ar.SpeciesId AS NVARCHAR(10)) + N'价格低于阈值'
            WHEN 2 THEN N'品种' + CAST(ar.SpeciesId AS NVARCHAR(10)) + N'价格涨幅超阈值'
            WHEN 3 THEN N'品种' + CAST(ar.SpeciesId AS NVARCHAR(10)) + N'价格跌幅超阈值'
        END,
        latest.AvgPrice,
        ar.ThresholdValue,
        0,
        @Now
    FROM [Flower_AlertRule] ar
    CROSS APPLY (
        SELECT TOP 1 ms.AvgPrice
        FROM [Flower_MarketSnapshot] ms
        WHERE ms.SpeciesId = ar.SpeciesId
          AND (ar.MarketId = 0 OR ms.MarketId = ar.MarketId)
        ORDER BY ms.SnapshotTime DESC
    ) latest
    CROSS APPLY (
        SELECT TOP 1 ms2.AvgPrice AS PrevPrice
        FROM [Flower_MarketSnapshot] ms2
        WHERE ms2.SpeciesId = ar.SpeciesId
          AND (ar.MarketId = 0 OR ms2.MarketId = ar.MarketId)
          AND ms2.SnapshotTime < latest.SnapshotTime
        ORDER BY ms2.SnapshotTime DESC
    ) prev
    WHERE ar.IsEnabled = 1
      AND ar.IsDeleted = 0
      AND (
          (ar.ConditionType = 0 AND latest.AvgPrice > ar.ThresholdValue)
       OR (ar.ConditionType = 1 AND latest.AvgPrice < ar.ThresholdValue)
       OR (ar.ConditionType = 2 AND prev.PrevPrice > 0 AND (latest.AvgPrice - prev.PrevPrice) / prev.PrevPrice > ar.ThresholdValue)
       OR (ar.ConditionType = 3 AND prev.PrevPrice > 0 AND (prev.PrevPrice - latest.AvgPrice) / prev.PrevPrice > ar.ThresholdValue)
      )
      AND NOT EXISTS (
          SELECT 1 FROM [Flower_AlertLog] al
          WHERE al.RuleId = ar.Id
            AND al.SpeciesId = ar.SpeciesId
            AND DATEDIFF(MINUTE, al.CreatedAt, @Now) < 60
      );
END
GO

IF OBJECT_ID(N'[dbo].[usp_PurgeOldSnapshots]', N'P') IS NOT NULL
    DROP PROCEDURE [dbo].[usp_PurgeOldSnapshots];
GO

CREATE PROCEDURE [dbo].[usp_PurgeOldSnapshots]
    @RetentionDays INT = 365
AS
BEGIN
    SET NOCOUNT ON;

    DECLARE @CutoffDate DATETIME2 = DATEADD(DAY, -@RetentionDays, SYSUTCDATETIME());

    DELETE FROM [Flower_MarketSnapshot]
    WHERE SnapshotTime < @CutoffDate
      AND SnapshotTime < (
          SELECT MIN(StatDate) FROM [Flower_DailyPriceStats]
      );

    DELETE FROM [Flower_SensorReading]
    WHERE ReadingTime < @CutoffDate;
END
GO

IF OBJECT_ID(N'[dbo].[usp_ManagePartitions]', N'P') IS NOT NULL
    DROP PROCEDURE [dbo].[usp_ManagePartitions];
GO

CREATE PROCEDURE [dbo].[usp_ManagePartitions]
AS
BEGIN
    SET NOCOUNT ON;

    DECLARE @Tomorrow DATE = CAST(DATEADD(DAY, 1, SYSUTCDATETIME()) AS DATE);
    DECLARE @PartitionName NVARCHAR(128);

    IF NOT EXISTS (
        SELECT 1 FROM sys.partition_functions pf
        INNER JOIN sys.partition_range_values prv ON pf.function_id = prv.function_id
        WHERE pf.name = N'pf_DailyTicks'
          AND CAST(prv.value AS DATE) = @Tomorrow
    )
    BEGIN
        SET @PartitionName = N'pf_DailyTicks';
        DECLARE @Sql NVARCHAR(MAX) = N'ALTER PARTITION FUNCTION ' + @PartitionName + N'() SPLIT RANGE (''' + CONVERT(NVARCHAR(10), @Tomorrow, 120) + N''');';
        EXEC sp_executesql @Sql;
    END

    IF NOT EXISTS (
        SELECT 1 FROM sys.partition_schemes ps
        WHERE ps.name = N'ps_DailyTicks'
    )
    BEGIN
        CREATE PARTITION SCHEME ps_DailyTicks
            AS PARTITION pf_DailyTicks
            ALL TO ([PRIMARY]);
    END
END
GO
