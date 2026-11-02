-- 027：给怪物模板表加"尸体拾取窗口"两列（2026-10-28 拍板 D21/D22 的数据面）。
--
-- **只生成、不执行**：按纪律，DDL 等用户点头后再跑（与 019/020/021..026 同一条规矩）。
-- 归属：怪物编辑器线（019/020 那一族）。号段已经撞过四次，本号之前请先 `ls scripts/sql` 复核。
--
-- 为什么是两列而不是复用 corpse_seconds：
--   `corpse_seconds` 回答"尸体在地上躺多久"，`loot_window_seconds` 回答"多久之内还能取东西"，
--   两者可以不同（尸体留 5 分钟、只有前 60 秒能拾取）。混成一格就再也表达不出"尸体还在但取不了"。
--   `pickup_distance_meters` 是交互距离（AC `INTERACTION_DISTANCE`），与尸体时长无关。
--
-- 缺省值取 AzerothCore 的 60 秒 / 5.5 米：**不是**拍脑袋，是为了"没配 = 与 AC 同观感"，
-- 而且 0 在拾取语义里是"取不了"，拿 0 当缺省会让所有既有怪突然不能拾取。
-- 0 的合法含义只有一个：`loot_window_seconds = 0` = 明确关掉拾取；`pickup_distance_meters = 0` 是非法值
--（校验器报 Error，与索敌半径 0 同一口径）。
--
-- 幂等：列已存在就跳过，重复执行不报错。

IF COL_LENGTH('dbo.Game_HunduShijie_MonsterTemplate', 'loot_window_seconds') IS NULL
BEGIN
    ALTER TABLE [dbo].[Game_HunduShijie_MonsterTemplate]
        ADD [loot_window_seconds] FLOAT NOT NULL
            CONSTRAINT [DF_MonsterTemplate_loot_window_seconds] DEFAULT (60);
END
GO

IF COL_LENGTH('dbo.Game_HunduShijie_MonsterTemplate', 'pickup_distance_meters') IS NULL
BEGIN
    ALTER TABLE [dbo].[Game_HunduShijie_MonsterTemplate]
        ADD [pickup_distance_meters] FLOAT NOT NULL
            CONSTRAINT [DF_MonsterTemplate_pickup_distance_meters] DEFAULT (5.5);
END
GO

-- 回执：两列都必须在，且既有行的缺省值就是 AC 的数（拿不到行数就是没跑成，别把"0 行"读成成功）。
SELECT
    COL_LENGTH('dbo.Game_HunduShijie_MonsterTemplate', 'loot_window_seconds')      AS loot_window_col,
    COL_LENGTH('dbo.Game_HunduShijie_MonsterTemplate', 'pickup_distance_meters')  AS pickup_distance_col,
    (SELECT COUNT(*) FROM [dbo].[Game_HunduShijie_MonsterTemplate] WHERE [loot_window_seconds] = 60)     AS rows_at_ac_window,
    (SELECT COUNT(*) FROM [dbo].[Game_HunduShijie_MonsterTemplate] WHERE [pickup_distance_meters] = 5.5) AS rows_at_ac_distance,
    (SELECT COUNT(*) FROM [dbo].[Game_HunduShijie_MonsterTemplate])                AS rows_total;
GO
