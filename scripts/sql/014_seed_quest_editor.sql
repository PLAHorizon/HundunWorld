-- ============================================================================
-- 014 任务编辑器落库：给已有任务补发布者与发布状态，并建两条示范任务
--
-- 前置：必须先应用迁移 20261005131122_AddQuestEditorColumns（新列不存在时本文件
-- 整批报错，不会静默写半套）。执行方式与其余种子一致：
--   sqlcmd -S . -d Game -U sa -P <pwd> -C -f 65001 -i scripts/sql/014_seed_quest_editor.sql
--
-- 为什么要有这一段而不是让新列全部默认 0：
-- ① 90001..90005 今天在线上就能接（publish_state 新列默认 0＝下线），
--    不补这一刀，接任务侧按"未发布"一律拒绝，等于上线即把已跑通的功能关掉；
-- ② 示范任务 10001/20001 用来验证编辑器产出的每一条引用链（发布者 NPC、指定怪击杀、
--    域击杀、与 NPC 对话、前置任务、发布/未发布两种状态）——没有可对照的数据，
--    "可视化编辑"就只能证明界面能画出来，证明不了写进库的东西运行时真能用。
--
-- 幂等：每条先 IF EXISTS 再 UPDATE/INSERT，且 UPDATE 分支写全所有列
-- （只改 INSERT 分支的列会在重跑时静默丢数据，这是这个仓库已经踩过一次的坑）。
-- ============================================================================
SET NOCOUNT ON;
SET XACT_ABORT ON;
BEGIN TRANSACTION;

SET IDENTITY_INSERT dbo.Game_HunduShijie_QuestTemplate ON;

-- ── 1. 既有的五行入门任务：补发布者（游方商队 npc=3）并置为已发布 ──────────────
UPDATE dbo.Game_HunduShijie_QuestTemplate
SET giver_kind = 1,
    giver_template_id = 3,
    giver_zone_id = 0,
    giver_spawn_point_id = 0,
    turn_in_kind = 0,
    turn_in_template_id = 0,
    prerequisite_quests = '',
    publish_state = 1,
    content_version = CASE WHEN content_version < 1 THEN 1 ELSE content_version END,
    IsValid = 1
WHERE quest_id BETWEEN 90001 AND 90005;

-- ── 2. 示范任务 10001「初试锋芒」：主线、发布者铸剑师、击杀指定怪物模板 ──────────
IF EXISTS (SELECT 1 FROM dbo.Game_HunduShijie_QuestTemplate WHERE quest_id = 10001)
BEGIN
    UPDATE dbo.Game_HunduShijie_QuestTemplate
    SET quest_name = N'初试锋芒',
        description = N'铁匠要你带回三枚银爪狸的爪痕，作为入门试炼的凭据。',
        quest_type = 0,
        level_require = 1,
        wuxing_domain = 1,
        rewards = N'{"exp":120,"gold":500,"item:810101":1,"favor:1":10}',
        objectives = N'[{"type":"kill_monster:4","desc":"猎杀 3 只银爪狸","count":3,"mandatory":true}]',
        repeat_limit = 0,
        giver_kind = 1,
        giver_template_id = 1,
        giver_zone_id = 0,
        giver_spawn_point_id = 0,
        turn_in_kind = 0,
        turn_in_template_id = 0,
        prerequisite_quests = '',
        publish_state = 1,
        IsValid = 1,
        content_version = CASE WHEN content_version < 1 THEN 1 ELSE content_version END
    WHERE quest_id = 10001;
END
ELSE
BEGIN
    INSERT INTO dbo.Game_HunduShijie_QuestTemplate
        (quest_id, quest_name, description, quest_type, level_require, wuxing_domain,
         rewards, objectives, repeat_limit, giver_kind, giver_template_id, giver_zone_id,
         giver_spawn_point_id, turn_in_kind, turn_in_template_id, prerequisite_quests,
         publish_state, content_version, IsValid, GameId, AreaId, ServerId, GameUserId)
    VALUES
        (10001, N'初试锋芒', N'铁匠要你带回三枚银爪狸的爪痕，作为入门试炼的凭据。',
         0, 1, 1,
         N'{"exp":120,"gold":500,"item:810101":1,"favor:1":10}',
         N'[{"type":"kill_monster:4","desc":"猎杀 3 只银爪狸","count":3,"mandatory":true}]',
         0, 1, 1, 0, 0, 0, 0, '',
         1, 1, 1, 0, 0, 0, '00000000-0000-0000-0000-000000000000');
END

-- ── 3. 示范任务 20001「水落石出」：支线、前置 10001、指定怪击杀 + 域击杀、故意保持未发布 ──
-- 未发布（publish_state=0）是有意留下的：现场要能验证"实时关闭后新接取当场停掉"，
-- 而不只是验证发布这一半。
IF EXISTS (SELECT 1 FROM dbo.Game_HunduShijie_QuestTemplate WHERE quest_id = 20001)
BEGIN
    UPDATE dbo.Game_HunduShijie_QuestTemplate
    SET quest_name = N'水落石出',
        description = N'守门人请你清理水域的异动：先取墨麒麟的鳞甲，再肃清该域其余异兽。',
        quest_type = 1,
        level_require = 10,
        wuxing_domain = 3,
        rewards = N'{"exp":200,"gold":800,"item:830101":1,"favor:3":20}',
        objectives = N'[{"type":"kill_monster:1","desc":"猎杀 2 只墨麒麟，取其鳞甲","count":2,"mandatory":true},{"type":"kill_domain:3","desc":"肃清水域异动：再猎杀 2 只水行异兽","count":2,"mandatory":true}]',
        repeat_limit = 0,
        giver_kind = 1,
        giver_template_id = 2,
        giver_zone_id = 0,
        giver_spawn_point_id = 0,
        turn_in_kind = 0,
        turn_in_template_id = 0,
        prerequisite_quests = '10001',
        publish_state = 0,
        IsValid = 1,
        content_version = CASE WHEN content_version < 1 THEN 1 ELSE content_version END
    WHERE quest_id = 20001;
END
ELSE
BEGIN
    INSERT INTO dbo.Game_HunduShijie_QuestTemplate
        (quest_id, quest_name, description, quest_type, level_require, wuxing_domain,
         rewards, objectives, repeat_limit, giver_kind, giver_template_id, giver_zone_id,
         giver_spawn_point_id, turn_in_kind, turn_in_template_id, prerequisite_quests,
         publish_state, content_version, IsValid, GameId, AreaId, ServerId, GameUserId)
    VALUES
        (20001, N'水落石出', N'守门人请你清理水域的异动：先取墨麒麟的鳞甲，再肃清该域其余异兽。',
         1, 10, 3,
         N'{"exp":200,"gold":800,"item:830101":1,"favor:3":20}',
         N'[{"type":"kill_monster:1","desc":"猎杀 2 只墨麒麟，取其鳞甲","count":2,"mandatory":true},{"type":"kill_domain:3","desc":"肃清水域异动：再猎杀 2 只水行异兽","count":2,"mandatory":true}]',
         0, 1, 2, 0, 0, 0, 0, '10001',
         0, 1, 1, 0, 0, 0, '00000000-0000-0000-0000-000000000000');
END

SET IDENTITY_INSERT dbo.Game_HunduShijie_QuestTemplate OFF;

-- ── 自检 1：奖励引用的物品必须真的在物品模板表里 ─────────────────────────────
-- 现场表现是"交了任务但东西没到手"，而发奖侧只有一条 warning，玩家与策划都对不上账。
IF EXISTS (
    SELECT 1
    FROM dbo.Game_HunduShijie_QuestTemplate q
    CROSS APPLY OPENJSON(q.rewards) AS j
    WHERE LEFT(j.[key], 5) = 'item:'
      AND NOT EXISTS (
          SELECT 1 FROM dbo.Game_HunduShijie_ItemTemplate i
          WHERE i.item_id = TRY_CAST(SUBSTRING(j.[key], 6, 10) AS INT)))
BEGIN
    ROLLBACK TRANSACTION;
    RAISERROR(N'014 种子：任务奖励引用了不存在的物品模板，整批回滚。', 16, 1) WITH NOWAIT;
    RETURN;
END

-- ── 自检 2：发布者必须是 1/3/4（NPC/物品/物件），怪物与地点不能发布 ─────────────
IF EXISTS (SELECT 1 FROM dbo.Game_HunduShijie_QuestTemplate WHERE giver_kind NOT IN (0, 1, 3, 4))
BEGIN
    ROLLBACK TRANSACTION;
    RAISERROR(N'014 种子：出现不能担任发布者的 giver_kind，整批回滚。', 16, 1) WITH NOWAIT;
    RETURN;
END

-- ── 自检 3：目标类型串只能是运行时真有事件源的那几种 ───────────────────────────
-- kill_domain 的域号、kill_monster 的模板号都要成形状，否则任务会永久卡在"进行中"。
IF EXISTS (
    SELECT 1
    FROM dbo.Game_HunduShijie_QuestTemplate q
    CROSS APPLY OPENJSON(q.objectives) AS j
    WHERE JSON_VALUE(j.value, '$.type') NOT LIKE 'kill\_domain:%' ESCAPE '\'
      AND JSON_VALUE(j.value, '$.type') NOT LIKE 'kill\_monster:%' ESCAPE '\'
      AND JSON_VALUE(j.value, '$.type') NOT LIKE 'talk\_npc:%' ESCAPE '\'
      AND JSON_VALUE(j.value, '$.type') NOT LIKE 'kill' ESCAPE '\')
BEGIN
    ROLLBACK TRANSACTION;
    RAISERROR(N'014 种子：目标类型串不在有事件源的集合里，整批回滚。', 16, 1) WITH NOWAIT;
    RETURN;
END

IF EXISTS (
    SELECT 1
    FROM dbo.Game_HunduShijie_QuestTemplate q
    CROSS APPLY OPENJSON(q.objectives) AS j
    WHERE TRY_CAST(JSON_VALUE(j.value, '$.count') AS INT) IS NULL
       OR TRY_CAST(JSON_VALUE(j.value, '$.count') AS INT) <= 0)
BEGIN
    ROLLBACK TRANSACTION;
    RAISERROR(N'014 种子：有目标的 count 不是正整数，整批回滚。', 16, 1) WITH NOWAIT;
    RETURN;
END

-- ── 自检 4：前置任务必须存在，且不是自己 ─────────────────────────────────────
IF EXISTS (
    SELECT 1
    FROM dbo.Game_HunduShijie_QuestTemplate q
    CROSS APPLY STRING_SPLIT(q.prerequisite_quests, ',') AS s
    WHERE LTRIM(RTRIM(s.value)) <> ''
      AND (TRY_CAST(LTRIM(RTRIM(s.value)) AS INT) IS NULL
           OR TRY_CAST(LTRIM(RTRIM(s.value)) AS INT) = q.quest_id
           OR NOT EXISTS (SELECT 1 FROM dbo.Game_HunduShijie_QuestTemplate p
                          WHERE p.quest_id = TRY_CAST(LTRIM(RTRIM(s.value)) AS INT))))
BEGIN
    ROLLBACK TRANSACTION;
    RAISERROR(N'014 种子：前置任务 id 非法或不存在，整批回滚。', 16, 1) WITH NOWAIT;
    RETURN;
END

COMMIT TRANSACTION;

SELECT quest_id, quest_name, quest_type, giver_kind, giver_template_id, publish_state, content_version
FROM dbo.Game_HunduShijie_QuestTemplate
WHERE quest_id IN (10001, 20001) OR quest_id BETWEEN 90001 AND 90005
ORDER BY quest_id;
