-- 025：经济逐笔流水 + 周期快照（P1）
-- 状态：可执行。apply 前建议先确认 023/024 已执行（本脚本与前两者无依赖）。
-- 回退：
--   DROP TABLE [Game_HunduShijie_EconomyLedger];
--   DROP TABLE [Game_HunduShijie_EconomySnapshot];
--
-- 口径：
--   * Ledger 只追加、不改行；delta 正=产出（mint），负=消耗（burn）；balance_after 是本次之后的余额。
--   * Snapshot 只追加；supply = 所有账户余额之和，rows_off_book = 余额≠累计获得−累计消耗 的行数。

SET NOCOUNT ON;
SET XACT_ABORT ON;

IF OBJECT_ID(N'Game_HunduShijie_EconomyLedger', N'U') IS NULL
BEGIN
    CREATE TABLE [Game_HunduShijie_EconomyLedger] (
        [id] bigint NOT NULL IDENTITY,
        [IsValid] bit NOT NULL,
        [character_id] decimal(20,0) NOT NULL,
        [currency_type] int NOT NULL,
        [delta] bigint NOT NULL,
        [balance_after] bigint NOT NULL,
        [reason] nvarchar(96) NOT NULL,
        [created_at] datetime2 NOT NULL,
        [GameId] int NOT NULL,
        [AreaId] int NOT NULL,
        [ServerId] int NOT NULL,
        [GameUserId] uniqueidentifier NOT NULL,
        CONSTRAINT [PK_Game_HunduShijie_EconomyLedger] PRIMARY KEY ([id])
    );

    CREATE INDEX [IX_EconomyLedger_Character] ON [Game_HunduShijie_EconomyLedger] ([character_id], [created_at]);
    CREATE INDEX [IX_EconomyLedger_Currency] ON [Game_HunduShijie_EconomyLedger] ([currency_type], [created_at]);
END
GO

IF OBJECT_ID(N'Game_HunduShijie_EconomySnapshot', N'U') IS NULL
BEGIN
    CREATE TABLE [Game_HunduShijie_EconomySnapshot] (
        [id] bigint NOT NULL IDENTITY,
        [IsValid] bit NOT NULL,
        [currency_type] int NOT NULL,
        [supply] bigint NOT NULL,
        [earned] bigint NOT NULL,
        [spent] bigint NOT NULL,
        [accounts] int NOT NULL,
        [rows_off_book] int NOT NULL,
        [taken_at] datetime2 NOT NULL,
        [GameId] int NOT NULL,
        [AreaId] int NOT NULL,
        [ServerId] int NOT NULL,
        [GameUserId] uniqueidentifier NOT NULL,
        CONSTRAINT [PK_Game_HunduShijie_EconomySnapshot] PRIMARY KEY ([id])
    );

    CREATE INDEX [IX_EconomySnapshot_Currency] ON [Game_HunduShijie_EconomySnapshot] ([currency_type], [taken_at]);
END
GO

-- 自检：两张表都在，且列数分别 >= 12。
IF OBJECT_ID(N'Game_HunduShijie_EconomyLedger', N'U') IS NULL
    THROW 52501, N'025：Game_HunduShijie_EconomyLedger 未创建', 1;
IF (SELECT COUNT(*) FROM sys.columns WHERE object_id = OBJECT_ID(N'Game_HunduShijie_EconomyLedger')) < 12
    THROW 52502, N'025：EconomyLedger 列数不足', 1;
IF OBJECT_ID(N'Game_HunduShijie_EconomySnapshot', N'U') IS NULL
    THROW 52503, N'025：Game_HunduShijie_EconomySnapshot 未创建', 1;
IF (SELECT COUNT(*) FROM sys.columns WHERE object_id = OBJECT_ID(N'Game_HunduShijie_EconomySnapshot')) < 12
    THROW 52504, N'025：EconomySnapshot 列数不足', 1;

PRINT N'025 OK：EconomyLedger / EconomySnapshot 已就绪';
