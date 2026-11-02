-- 027 的回退：删掉两列（含各自的默认约束）。
-- 只在确认这两列没被内容依赖时执行；回退后 `MonsterStore` 的读写会因缺列而失败，
-- 所以回退必须连同客户端/服务端一起回到 027 之前的装配。

IF OBJECT_ID('dbo.DF_MonsterTemplate_loot_window_seconds', 'D') IS NOT NULL
    ALTER TABLE [dbo].[Game_HunduShijie_MonsterTemplate] DROP CONSTRAINT [DF_MonsterTemplate_loot_window_seconds];
GO
IF COL_LENGTH('dbo.Game_HunduShijie_MonsterTemplate', 'loot_window_seconds') IS NOT NULL
    ALTER TABLE [dbo].[Game_HunduShijie_MonsterTemplate] DROP COLUMN [loot_window_seconds];
GO

IF OBJECT_ID('dbo.DF_MonsterTemplate_pickup_distance_meters', 'D') IS NOT NULL
    ALTER TABLE [dbo].[Game_HunduShijie_MonsterTemplate] DROP CONSTRAINT [DF_MonsterTemplate_pickup_distance_meters];
GO
IF COL_LENGTH('dbo.Game_HunduShijie_MonsterTemplate', 'pickup_distance_meters') IS NOT NULL
    ALTER TABLE [dbo].[Game_HunduShijie_MonsterTemplate] DROP COLUMN [pickup_distance_meters];
GO
