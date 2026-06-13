-- ============================================================================
-- 修复 OrleansStorage 主键冲突问题
-- 问题：WriteToStorageKey 存储过程在 @GrainStateVersion IS NULL 时使用 INSERT ... WHERE NOT EXISTS
--       但 OrleansStorage 表没有 PK 约束，只有非聚集索引 IX_OrleansStorage
--       当数据库中存在旧的 PK_OrleansStorage 约束时，INSERT 会导致主键冲突
-- 解决：更新 WriteToStorageKey 存储过程，使用 MERGE 语句替代 INSERT/UPDATE 逻辑
-- ============================================================================

-- 步骤1：检查并删除旧的 PK_OrleansStorage 主键约束（如果存在）
IF EXISTS (
    SELECT 1 
    FROM sys.key_constraints 
    WHERE name = 'PK_OrleansStorage' 
    AND parent_object_id = OBJECT_ID('OrleansStorage')
)
BEGIN
    PRINT '正在删除 PK_OrleansStorage 主键约束...';
    ALTER TABLE OrleansStorage DROP CONSTRAINT PK_OrleansStorage;
    PRINT '已删除 PK_OrleansStorage 主键约束。';
END
ELSE
BEGIN
    PRINT 'PK_OrleansStorage 主键约束不存在，跳过。';
END

-- 步骤2：确保 IX_OrleansStorage 非聚集索引存在
IF NOT EXISTS (
    SELECT * FROM sys.indexes 
    WHERE name = 'IX_OrleansStorage' 
    AND object_id = OBJECT_ID('OrleansStorage')
)
BEGIN
    PRINT '正在创建 IX_OrleansStorage 非聚集索引...';
    CREATE NONCLUSTERED INDEX IX_OrleansStorage ON OrleansStorage(GrainIdHash, GrainTypeHash);
    PRINT '已创建 IX_OrleansStorage 非聚集索引。';
END

-- 步骤3：更新 WriteToStorageKey 存储过程，使用 MERGE 替代 INSERT/UPDATE
-- MERGE 是原子操作，不存在则插入，存在则更新，避免主键冲突
PRINT '正在更新 WriteToStorageKey 存储过程...';
UPDATE OrleansQuery
SET QueryText = '
-- 使用 MERGE 替代 INSERT/UPDATE 逻辑，避免主键冲突
-- MERGE 是原子操作：不存在则插入，存在则更新
BEGIN TRANSACTION;
SET XACT_ABORT, NOCOUNT ON;
DECLARE @NewGrainStateVersion AS INT;

MERGE INTO OrleansStorage WITH(HOLDLOCK, TABLOCKX) AS target
USING (
    SELECT 
        @GrainIdHash AS GrainIdHash,
        @GrainIdN0 AS GrainIdN0,
        @GrainIdN1 AS GrainIdN1,
        @GrainTypeHash AS GrainTypeHash,
        @GrainTypeString AS GrainTypeString,
        @GrainIdExtensionString AS GrainIdExtensionString,
        @ServiceId AS ServiceId
) AS source
ON (
    target.GrainIdHash = source.GrainIdHash AND source.GrainIdHash IS NOT NULL
    AND target.GrainTypeHash = source.GrainTypeHash AND source.GrainTypeHash IS NOT NULL
    AND (target.GrainIdN0 = source.GrainIdN0 OR source.GrainIdN0 IS NULL)
    AND (target.GrainIdN1 = source.GrainIdN1 OR source.GrainIdN1 IS NULL)
    AND (target.GrainTypeString = source.GrainTypeString OR source.GrainTypeString IS NULL)
    AND ((source.GrainIdExtensionString IS NOT NULL AND target.GrainIdExtensionString IS NOT NULL AND target.GrainIdExtensionString = source.GrainIdExtensionString) OR (source.GrainIdExtensionString IS NULL AND target.GrainIdExtensionString IS NULL))
    AND target.ServiceId = source.ServiceId AND source.ServiceId IS NOT NULL
)
WHEN MATCHED AND (@GrainStateVersion IS NOT NULL AND target.Version = @GrainStateVersion) THEN
    UPDATE SET
        PayloadBinary = @PayloadBinary,
        ModifiedOn = GETUTCDATE(),
        Version = target.Version + 1
WHEN NOT MATCHED BY TARGET AND (@GrainStateVersion IS NULL) THEN
    INSERT (
        GrainIdHash,
        GrainIdN0,
        GrainIdN1,
        GrainTypeHash,
        GrainTypeString,
        GrainIdExtensionString,
        ServiceId,
        PayloadBinary,
        ModifiedOn,
        Version
    )
    VALUES (
        @GrainIdHash,
        @GrainIdN0,
        @GrainIdN1,
        @GrainTypeHash,
        @GrainTypeString,
        @GrainIdExtensionString,
        @ServiceId,
        @PayloadBinary,
        GETUTCDATE(),
        1
    );

-- 返回新版本号
IF @GrainStateVersion IS NULL
BEGIN
    -- 新插入的记录版本为1
    SET @NewGrainStateVersion = ISNULL((SELECT TOP 1 Version FROM OrleansStorage 
        WHERE GrainIdHash = @GrainIdHash AND @GrainIdHash IS NOT NULL
        AND GrainTypeHash = @GrainTypeHash AND @GrainTypeHash IS NOT NULL
        AND (GrainIdN0 = @GrainIdN0 OR @GrainIdN0 IS NULL)
        AND (GrainIdN1 = @GrainIdN1 OR @GrainIdN1 IS NULL)
        AND (GrainTypeString = @GrainTypeString OR @GrainTypeString IS NULL)
        AND ((@GrainIdExtensionString IS NOT NULL AND GrainIdExtensionString IS NOT NULL AND GrainIdExtensionString = @GrainIdExtensionString) OR (@GrainIdExtensionString IS NULL AND GrainIdExtensionString IS NULL))
        AND ServiceId = @ServiceId AND @ServiceId IS NOT NULL), 1);
END
ELSE
BEGIN
    SET @NewGrainStateVersion = @GrainStateVersion + 1;
END

SELECT @NewGrainStateVersion AS NewGrainStateVersion;
COMMIT TRANSACTION;'
WHERE QueryKey = 'WriteToStorageKey';

PRINT '已更新 WriteToStorageKey 存储过程，使用 MERGE 替代 INSERT/UPDATE。';

-- 步骤4：清理可能存在的重复数据（同一 GrainId+GrainType 的重复记录）
-- 仅保留每个 Grain 最新版本
PRINT '';
PRINT '检查是否存在重复数据...';
SELECT GrainIdHash, GrainTypeHash, GrainIdN0, GrainIdN1, GrainTypeString, GrainIdExtensionString, ServiceId, COUNT(*) AS DuplicateCount
FROM OrleansStorage
GROUP BY GrainIdHash, GrainTypeHash, GrainIdN0, GrainIdN1, GrainTypeString, GrainIdExtensionString, ServiceId
HAVING COUNT(*) > 1;

PRINT '';
PRINT '修复脚本执行完成。';
PRINT '如果上方显示了重复数据，请手动清理，仅保留每个 Grain 最新版本的记录。';
