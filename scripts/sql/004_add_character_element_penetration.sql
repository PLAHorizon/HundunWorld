BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260929045814_AddCharacterElementPenetration'
)
BEGIN
    ALTER TABLE [Game_HunduShijie_CharacterAttribute] ADD [earth_penetration] int NOT NULL DEFAULT 0;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260929045814_AddCharacterElementPenetration'
)
BEGIN
    ALTER TABLE [Game_HunduShijie_CharacterAttribute] ADD [fire_penetration] int NOT NULL DEFAULT 0;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260929045814_AddCharacterElementPenetration'
)
BEGIN
    ALTER TABLE [Game_HunduShijie_CharacterAttribute] ADD [metal_penetration] int NOT NULL DEFAULT 0;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260929045814_AddCharacterElementPenetration'
)
BEGIN
    ALTER TABLE [Game_HunduShijie_CharacterAttribute] ADD [water_penetration] int NOT NULL DEFAULT 0;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260929045814_AddCharacterElementPenetration'
)
BEGIN
    ALTER TABLE [Game_HunduShijie_CharacterAttribute] ADD [wood_penetration] int NOT NULL DEFAULT 0;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260929045814_AddCharacterElementPenetration'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260929045814_AddCharacterElementPenetration', N'10.0.2');
END;

COMMIT;
GO

