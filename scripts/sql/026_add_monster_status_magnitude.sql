-- 026：给怪物技能挂载表加"状态强度"一列（今天只有减速用它：5000 = 走速打五折）。
--
-- **只生成、不执行**：按纪律，DDL 等用户点头后再跑（与 019/020/021..025 同一条规矩）。
-- 归属：怪物编辑器线（019/020 那一族）。号段已经撞过四次，本号之前请先 `ls scripts/sql` 复核。
--
-- 为什么要有这一列：`status_id` + `status_duration_seconds` 只能表达"上什么状态、多久"，
-- 而减速还必须回答"减多少"（AC 那边是每一式技能自带的 effect 值）。没有这一格，
-- 减速只能"登记不生效"——那正是 audit 一直在报的那类"配了等于没配"。
--
-- 缺省 0 = **没填**（不是 50%）：与"填了 0"同义、都不生效。随手给一个 50% 就是把猜测当配置。
-- 内核上限 9000（减到 10%）：10000 等于定身，两档会分不清（见 MonsterStatusCore.MaxSlowMagnitudeBps）。
--
-- 幂等：列已存在就跳过，重复执行不报错。
IF COL_LENGTH('dbo.Game_HunduShijie_MonsterSkillMount', 'status_magnitude_bps') IS NULL
BEGIN
    ALTER TABLE [dbo].[Game_HunduShijie_MonsterSkillMount]
        ADD [status_magnitude_bps] INT NOT NULL
            CONSTRAINT [DF_MonsterSkillMount_status_magnitude_bps] DEFAULT (0);
END
GO
