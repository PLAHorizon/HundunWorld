-- 024：NPC 技能挂载（`Casts` 的数据前置）
-- 状态：**已生成、未执行**。执行前请确认：库可用；Silo/Gateway/DsHost 可按批次重启。
-- 依赖：021（NPC 表已存在）。与本脚本同批落地的还有：实体/迁移/快照、NpcStore 读写、导出、DsWorldSession v2 分流。
--
-- 为什么**不能**复用 Game_HunduShijie_MonsterSkillMount：
--   怪物侧 Upsert 与删除路径都按 monster_id **整段删光再插**（Horizon.MonsterCli/MonsterStore.cs:446/:675），
--   而 NPC 与怪物在实体注册表里共用同一整数 id 空间 ⇒ 两个编辑器保存时会互抹对方的挂载。
--   另 MonsterStore 导出会把**全表**按 monster_id 分组进怪物内容，npc_id 的行会混进去。
--
-- 回退：
--   DROP TABLE [Game_HunduShijie_NpcSkillMount];
--   （扩展属性随表删除；无外键、无种子数据需要单独清）

SET NOCOUNT ON;
SET XACT_ABORT ON;

IF OBJECT_ID(N'Game_HunduShijie_NpcSkillMount', N'U') IS NULL
BEGIN
    CREATE TABLE [Game_HunduShijie_NpcSkillMount] (
        [IsValid] bit NOT NULL,
        [mount_id] int NOT NULL IDENTITY,
        [npc_id] int NOT NULL,
        [skill_id] int NOT NULL,
        [slot_seq] int NOT NULL,
        [slot_type] tinyint NOT NULL,
        [chance_bps] int NOT NULL,
        [cast_ms] int NOT NULL,
        [cooldown_ms] int NOT NULL,
        [min_range] float NOT NULL,
        [max_range] float NOT NULL,
        [target_select] tinyint NOT NULL,
        [target_count] int NOT NULL,
        [aoe_radius] float NOT NULL,
        [school_mask] int NOT NULL,
        [interruptible] tinyint NOT NULL,
        [allow_movement] tinyint NOT NULL,
        [damage_scale_bps] int NOT NULL,
        [status_id] int NOT NULL,
        [status_duration_seconds] float NOT NULL,
        [start_emote] int NOT NULL,
        [emergency_hp_percent] int NOT NULL,
        [GameId] int NOT NULL,
        [AreaId] int NOT NULL,
        [ServerId] int NOT NULL,
        [GameUserId] uniqueidentifier NOT NULL,
        CONSTRAINT [PK_Game_HunduShijie_NpcSkillMount] PRIMARY KEY ([mount_id])
    );

    DECLARE @defaultSchema AS sysname;
    SET @defaultSchema = SCHEMA_NAME();
    DECLARE @description AS sql_variant;

    SET @description = N'NPC 技能挂载表（施法数据前置；与怪物侧同形，owner 改 npc_id）';
    EXEC sp_addextendedproperty 'MS_Description', @description, 'SCHEMA', @defaultSchema,
        'TABLE', N'Game_HunduShijie_NpcSkillMount';

    SET @description = N'挂载 id（自增）';
    EXEC sp_addextendedproperty 'MS_Description', @description, 'SCHEMA', @defaultSchema,
        'TABLE', N'Game_HunduShijie_NpcSkillMount', 'COLUMN', N'mount_id';

    SET @description = N'NPC 编号';
    EXEC sp_addextendedproperty 'MS_Description', @description, 'SCHEMA', @defaultSchema,
        'TABLE', N'Game_HunduShijie_NpcSkillMount', 'COLUMN', N'npc_id';

    SET @description = N'技能 id（对齐技能模板表）';
    EXEC sp_addextendedproperty 'MS_Description', @description, 'SCHEMA', @defaultSchema,
        'TABLE', N'Game_HunduShijie_NpcSkillMount', 'COLUMN', N'skill_id';

    SET @description = N'遍历顺序（顺序影响平局结果，故是数据而不是插入历史）';
    EXEC sp_addextendedproperty 'MS_Description', @description, 'SCHEMA', @defaultSchema,
        'TABLE', N'Game_HunduShijie_NpcSkillMount', 'COLUMN', N'slot_seq';

    SET @description = N'释放时机：零战斗 一非战斗 二残血 三受控';
    EXEC sp_addextendedproperty 'MS_Description', @description, 'SCHEMA', @defaultSchema,
        'TABLE', N'Game_HunduShijie_NpcSkillMount', 'COLUMN', N'slot_type';

    SET @description = N'独立释放概率（基点零到一万）';
    EXEC sp_addextendedproperty 'MS_Description', @description, 'SCHEMA', @defaultSchema,
        'TABLE', N'Game_HunduShijie_NpcSkillMount', 'COLUMN', N'chance_bps';

    SET @description = N'吟唱毫秒（零等于瞬发）';
    EXEC sp_addextendedproperty 'MS_Description', @description, 'SCHEMA', @defaultSchema,
        'TABLE', N'Game_HunduShijie_NpcSkillMount', 'COLUMN', N'cast_ms';

    SET @description = N'冷却毫秒';
    EXEC sp_addextendedproperty 'MS_Description', @description, 'SCHEMA', @defaultSchema,
        'TABLE', N'Game_HunduShijie_NpcSkillMount', 'COLUMN', N'cooldown_ms';

    SET @description = N'最小射程（米）';
    EXEC sp_addextendedproperty 'MS_Description', @description, 'SCHEMA', @defaultSchema,
        'TABLE', N'Game_HunduShijie_NpcSkillMount', 'COLUMN', N'min_range';

    SET @description = N'最大射程（米）';
    EXEC sp_addextendedproperty 'MS_Description', @description, 'SCHEMA', @defaultSchema,
        'TABLE', N'Game_HunduShijie_NpcSkillMount', 'COLUMN', N'max_range';

    SET @description = N'选敌方式：零当前威胁 一仇恨内随机 二最近 三血最低 四以目标为圆心';
    EXEC sp_addextendedproperty 'MS_Description', @description, 'SCHEMA', @defaultSchema,
        'TABLE', N'Game_HunduShijie_NpcSkillMount', 'COLUMN', N'target_select';

    SET @description = N'最大目标数';
    EXEC sp_addextendedproperty 'MS_Description', @description, 'SCHEMA', @defaultSchema,
        'TABLE', N'Game_HunduShijie_NpcSkillMount', 'COLUMN', N'target_count';

    SET @description = N'范围伤害半径（米）';
    EXEC sp_addextendedproperty 'MS_Description', @description, 'SCHEMA', @defaultSchema,
        'TABLE', N'Game_HunduShijie_NpcSkillMount', 'COLUMN', N'aoe_radius';

    SET @description = N'系别位掩码（金木水火土与物理）';
    EXEC sp_addextendedproperty 'MS_Description', @description, 'SCHEMA', @defaultSchema,
        'TABLE', N'Game_HunduShijie_NpcSkillMount', 'COLUMN', N'school_mask';

    SET @description = N'是否可打断（一等于可）';
    EXEC sp_addextendedproperty 'MS_Description', @description, 'SCHEMA', @defaultSchema,
        'TABLE', N'Game_HunduShijie_NpcSkillMount', 'COLUMN', N'interruptible';

    SET @description = N'是否允许移动施法';
    EXEC sp_addextendedproperty 'MS_Description', @description, 'SCHEMA', @defaultSchema,
        'TABLE', N'Game_HunduShijie_NpcSkillMount', 'COLUMN', N'allow_movement';

    SET @description = N'伤害倍率基点（一万等于乘一）';
    EXEC sp_addextendedproperty 'MS_Description', @description, 'SCHEMA', @defaultSchema,
        'TABLE', N'Game_HunduShijie_NpcSkillMount', 'COLUMN', N'damage_scale_bps';

    SET @description = N'附加状态 id';
    EXEC sp_addextendedproperty 'MS_Description', @description, 'SCHEMA', @defaultSchema,
        'TABLE', N'Game_HunduShijie_NpcSkillMount', 'COLUMN', N'status_id';

    SET @description = N'状态持续秒';
    EXEC sp_addextendedproperty 'MS_Description', @description, 'SCHEMA', @defaultSchema,
        'TABLE', N'Game_HunduShijie_NpcSkillMount', 'COLUMN', N'status_duration_seconds';

    SET @description = N'起手喊话或表情 id';
    EXEC sp_addextendedproperty 'MS_Description', @description, 'SCHEMA', @defaultSchema,
        'TABLE', N'Game_HunduShijie_NpcSkillMount', 'COLUMN', N'start_emote';

    SET @description = N'残血阈值百分比（仅残血时机使用）';
    EXEC sp_addextendedproperty 'MS_Description', @description, 'SCHEMA', @defaultSchema,
        'TABLE', N'Game_HunduShijie_NpcSkillMount', 'COLUMN', N'emergency_hp_percent';
END
GO

-- 自检一：表必须存在
IF OBJECT_ID(N'Game_HunduShijie_NpcSkillMount', N'U') IS NULL
    THROW 52401, N'024 失败：NpcSkillMount 表没有建出来', 1;

-- 自检二：概率必须落在基点位域（空表也过，给将来的种子/编辑器写入兜底）
IF EXISTS (SELECT 1 FROM [Game_HunduShijie_NpcSkillMount] WHERE [chance_bps] < 0 OR [chance_bps] > 10000)
    THROW 52402, N'024 失败：chance_bps 超出零到一万', 1;

-- 自检三：吟唱与射程不允许负数（负射程会让"能打到的范围"变成空集而无人察觉）
IF EXISTS (SELECT 1 FROM [Game_HunduShijie_NpcSkillMount] WHERE [cast_ms] < 0 OR [max_range] < 0)
    THROW 52403, N'024 失败：cast_ms 或 max_range 为负', 1;

-- 自检四：残血阈值必须在百分比位域
IF EXISTS (SELECT 1 FROM [Game_HunduShijie_NpcSkillMount] WHERE [emergency_hp_percent] < 0 OR [emergency_hp_percent] > 100)
    THROW 52404, N'024 失败：emergency_hp_percent 超出百分比位域', 1;

PRINT N'024 OK：NpcSkillMount 已就绪（空表）。';
