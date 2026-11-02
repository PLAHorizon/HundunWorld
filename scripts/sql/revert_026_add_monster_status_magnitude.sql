-- revert_026：撤掉"状态强度"那一列（先删缺省约束，再删列；列本就不存在则什么都不做）。
IF COL_LENGTH('dbo.Game_HunduShijie_MonsterSkillMount', 'status_magnitude_bps') IS NOT NULL
BEGIN
    IF OBJECT_ID('dbo.DF_MonsterSkillMount_status_magnitude_bps', 'D') IS NOT NULL
        ALTER TABLE [dbo].[Game_HunduShijie_MonsterSkillMount]
            DROP CONSTRAINT [DF_MonsterSkillMount_status_magnitude_bps];

    ALTER TABLE [dbo].[Game_HunduShijie_MonsterSkillMount]
        DROP COLUMN [status_magnitude_bps];
END
GO
