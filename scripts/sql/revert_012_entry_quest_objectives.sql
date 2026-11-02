-- 回退脚本（本次 012 应用前的实测状态）：入门任务 90001..90005 的 objectives 原本是空数组。
-- 用法：sqlcmd -f 65001 -i scripts/sql/revert_012_entry_quest_objectives.sql
-- 只做这一件事；技能书掉落/领取记录等玩家侧数据不受影响（已发出的书不会因此消失）。
SET NOCOUNT ON;
UPDATE dbo.Game_HunduShijie_QuestTemplate
SET objectives = N'[]'
WHERE quest_id BETWEEN 90001 AND 90005;

PRINT N'已把 90001..90005 的 objectives 恢复为空数组（012 应用前的状态）';
