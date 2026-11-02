-- 023：NPC 对话台词文本（SpeaksGossip 的数据前置）
-- 状态：**已生成、未执行**。执行前请确认：库可用、Silo/Gateway/DsHost 可重启（EF 模型会在同一批里加这一列）。
-- 依赖：021（已执行，NpcGossipOption 表存在）。
-- 回退：ALTER TABLE ... DROP CONSTRAINT DF_NpcGossipText; ALTER TABLE ... DROP COLUMN [text];

SET NOCOUNT ON;
SET XACT_ABORT ON;

IF COL_LENGTH(N'Game_HunduShijie_NpcGossipOption', N'text') IS NULL
BEGIN
    ALTER TABLE [Game_HunduShijie_NpcGossipOption]
        ADD [text] nvarchar(200) NOT NULL
            CONSTRAINT DF_NpcGossipText DEFAULT (N'');
END
GO

-- 与其它列同一个扩展属性风格；中文标签会让紧随其后的数字被吞，描述里不放裸数字。
IF NOT EXISTS (
    SELECT 1 FROM sys.extended_properties
    WHERE major_id = OBJECT_ID(N'Game_HunduShijie_NpcGossipOption')
      AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'Game_HunduShijie_NpcGossipOption'), N'text', 'ColumnId')
      AND name = N'MS_Description')
BEGIN
    EXEC sp_addextendedproperty
        @name = N'MS_Description',
        @value = N'对话台词正文（Kind=仅文本 时显示；空串=没配，客户端按种类合成标签）',
        @level0type = N'SCHEMA', @level0name = N'dbo',
        @level1type = N'TABLE',  @level1name = N'Game_HunduShijie_NpcGossipOption',
        @level2type = N'COLUMN', @level2name = N'text';
END
GO

-- 自检：列必须存在，表里现有行必须都是空串（不许把 NULL 带进客户端）。
IF COL_LENGTH(N'Game_HunduShijie_NpcGossipOption', N'text') IS NULL
    THROW 52301, N'023 失败：text 列没有加上', 1;

IF EXISTS (SELECT 1 FROM [Game_HunduShijie_NpcGossipOption] WHERE [text] IS NULL)
    THROW 52302, N'023 失败：text 列存在 NULL（NOT NULL 约束没生效）', 1;

PRINT N'023 OK：NpcGossipOption.text 已就绪（现有行一律空串）。';
