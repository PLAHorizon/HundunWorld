-- =============================================================================
-- HundunWorld 世界状态持久化 DDL（P4-a）
-- Target: Microsoft SQL Server 2019+
-- 用途：
--   1. chunk_state  — WorldChunkCellGrain 的权威状态快照（Orleans 已有的 OrleansStorage 表存 grain JSON；
--                     本表额外维护"按 Morton 键分区的最终状态副本"，便于 Zone Shard 批量加载。）
--   2. diff_log     — WorldDiffLogGrain 使用的 append-only 日志，利用 IDENTITY 作为全局单调 seq。
-- 约束：
--   * 数据库编译对齐规范：datetime2 + nvarchar(max) JSON + VARBINARY(MAX)。
--   * 所有表按 morton_key % N 哈希分区；N 由 sysadmin 按硬件决定（示例给出 8 分区）。
-- =============================================================================

SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;
SET NOCOUNT ON;
GO

-- ---------------------------------------------------------------------------
-- 1. 分区函数 / 分区方案（8 分区示例；量级大的集群请调大，但需 DBA 审核）
-- ---------------------------------------------------------------------------
IF NOT EXISTS (SELECT 1 FROM sys.partition_functions WHERE name = N'pf_world_morton_8')
BEGIN
    -- 按 morton_key & 7 划分到 0..7 共 8 桶
    CREATE PARTITION FUNCTION pf_world_morton_8 (BIGINT)
        AS RANGE LEFT FOR VALUES (0, 1, 2, 3, 4, 5, 6);
END
GO

IF NOT EXISTS (SELECT 1 FROM sys.partition_schemes WHERE name = N'ps_world_morton_8')
BEGIN
    CREATE PARTITION SCHEME ps_world_morton_8
        AS PARTITION pf_world_morton_8
        ALL TO ([PRIMARY]);
END
GO

-- ---------------------------------------------------------------------------
-- 2. chunk_state: 单 chunk 的最新权威状态快照
-- ---------------------------------------------------------------------------
IF OBJECT_ID(N'dbo.chunk_state', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.chunk_state
    (
        morton_key        BIGINT          NOT NULL,
        -- morton_key 的哈希桶（persisted computed column，供分区与索引使用）
        morton_bucket     TINYINT         NOT NULL,
        -- grain state 版本号；每次应用 op 递增，用于乐观并发
        version           BIGINT          NOT NULL,
        -- 最近一次写入时的服务器 UTC 时间
        updated_at        DATETIME2(3)    NOT NULL CONSTRAINT DF_chunk_state_updated_at DEFAULT SYSUTCDATETIME(),
        -- JSON 序列化的 ChunkCellState.OpLog + 其它元数据（紧凑后的 op 集合）
        state_json        NVARCHAR(MAX)   NOT NULL,
        -- 可选：相同数据的 MemoryPack 二进制副本，用于快速批量加载（首选此列；state_json 用于诊断）
        state_bin         VARBINARY(MAX)  NULL,
        CONSTRAINT PK_chunk_state PRIMARY KEY CLUSTERED (morton_bucket, morton_key)
    ) ON ps_world_morton_8 (morton_bucket);
END
GO

-- ---------------------------------------------------------------------------
-- 3. diff_log: append-only 日志，IDENTITY 提供跨 silo 单调 seq
-- ---------------------------------------------------------------------------
IF OBJECT_ID(N'dbo.diff_log', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.diff_log
    (
        seq               BIGINT          IDENTITY(1,1) NOT NULL,
        morton_key        BIGINT          NOT NULL,
        morton_bucket     TINYINT         NOT NULL,
        op_kind           TINYINT         NOT NULL,
        -- 原始 MemoryPack 字节；客户端直接透传到 WorldChunkDiffPacket.Payload
        payload           VARBINARY(MAX)  NOT NULL,
        created_at        DATETIME2(3)    NOT NULL CONSTRAINT DF_diff_log_created_at DEFAULT SYSUTCDATETIME(),
        CONSTRAINT PK_diff_log PRIMARY KEY CLUSTERED (morton_bucket, seq)
    ) ON ps_world_morton_8 (morton_bucket);

    CREATE NONCLUSTERED INDEX IX_diff_log_morton_key ON dbo.diff_log (morton_key, seq)
        WITH (DATA_COMPRESSION = PAGE);

    CREATE NONCLUSTERED INDEX IX_diff_log_seq ON dbo.diff_log (seq)
        WITH (DATA_COMPRESSION = PAGE);
END
GO

-- ---------------------------------------------------------------------------
-- 4. 清理过期 diff 的 job 模板（默认保留 7 天；后续可按需调整到"保留 100M 条"之类阈值）
--    运维需自行注册到 SQL Agent；这里仅给 SQL。
-- ---------------------------------------------------------------------------
-- DELETE TOP (10000) FROM dbo.diff_log WHERE created_at < DATEADD(day, -7, SYSUTCDATETIME());
GO
