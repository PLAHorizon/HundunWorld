using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Horizon.Entities.Migrations.Games
{
    /// <inheritdoc />
    public partial class GameUserModify : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterTable(
                name: "Game_HunduShijie_Zone",
                comment: "游戏分区信息表",
                oldComment: "游戏分区信息表");

            migrationBuilder.AlterTable(
                name: "Game_HunduShijie_Server",
                comment: "游戏服务器信息表",
                oldComment: "游戏服务器信息表");

            migrationBuilder.AlterColumn<string>(
                name: "zone_name",
                table: "Game_HunduShijie_Zone",
                type: "varchar(255)",
                nullable: false,
                comment: "分区名称",
                oldClrType: typeof(string),
                oldType: "varchar(255)",
                oldComment: "分区名称");

            migrationBuilder.AlterColumn<DateTime>(
                name: "updated_at",
                table: "Game_HunduShijie_Zone",
                type: "datetime2",
                nullable: false,
                comment: "更新时间",
                oldClrType: typeof(DateTime),
                oldType: "datetime2",
                oldComment: "更新时间");

            migrationBuilder.AlterColumn<int>(
                name: "game_id",
                table: "Game_HunduShijie_Zone",
                type: "int",
                nullable: false,
                comment: "游戏Id",
                oldClrType: typeof(int),
                oldType: "int",
                oldComment: "游戏Id");

            migrationBuilder.AlterColumn<string>(
                name: "description",
                table: "Game_HunduShijie_Zone",
                type: "varchar(500)",
                nullable: false,
                comment: "分区描述",
                oldClrType: typeof(string),
                oldType: "varchar(500)",
                oldComment: "分区描述");

            migrationBuilder.AlterColumn<DateTime>(
                name: "created_at",
                table: "Game_HunduShijie_Zone",
                type: "datetime2",
                nullable: false,
                comment: "创建时间",
                oldClrType: typeof(DateTime),
                oldType: "datetime2",
                oldComment: "创建时间");

            migrationBuilder.AlterColumn<int>(
                name: "zone_id",
                table: "Game_HunduShijie_Server",
                type: "int",
                nullable: false,
                comment: "分区ID",
                oldClrType: typeof(int),
                oldType: "int",
                oldComment: "分区ID");

            migrationBuilder.AlterColumn<DateTime>(
                name: "updated_at",
                table: "Game_HunduShijie_Server",
                type: "datetime2",
                nullable: false,
                comment: "更新时间",
                oldClrType: typeof(DateTime),
                oldType: "datetime2",
                oldComment: "更新时间");

            migrationBuilder.AlterColumn<string>(
                name: "status",
                table: "Game_HunduShijie_Server",
                type: "varchar(50)",
                nullable: false,
                comment: "服务器状态",
                oldClrType: typeof(string),
                oldType: "varchar(50)",
                oldComment: "服务器状态");

            migrationBuilder.AlterColumn<string>(
                name: "server_name",
                table: "Game_HunduShijie_Server",
                type: "varchar(255)",
                nullable: false,
                comment: "服务器名称",
                oldClrType: typeof(string),
                oldType: "varchar(255)",
                oldComment: "服务器名称");

            migrationBuilder.AlterColumn<int>(
                name: "port",
                table: "Game_HunduShijie_Server",
                type: "int",
                nullable: false,
                comment: "端口",
                oldClrType: typeof(int),
                oldType: "int",
                oldComment: "端口");

            migrationBuilder.AlterColumn<int>(
                name: "max_players",
                table: "Game_HunduShijie_Server",
                type: "int",
                nullable: false,
                comment: "最大玩家数",
                oldClrType: typeof(int),
                oldType: "int",
                oldComment: "最大玩家数");

            migrationBuilder.AlterColumn<string>(
                name: "ip_address",
                table: "Game_HunduShijie_Server",
                type: "varchar(255)",
                nullable: false,
                comment: "IP地址",
                oldClrType: typeof(string),
                oldType: "varchar(255)",
                oldComment: "IP地址");

            migrationBuilder.AlterColumn<int>(
                name: "current_players",
                table: "Game_HunduShijie_Server",
                type: "int",
                nullable: false,
                comment: "当前玩家数",
                oldClrType: typeof(int),
                oldType: "int",
                oldComment: "当前玩家数");

            migrationBuilder.AlterColumn<DateTime>(
                name: "created_at",
                table: "Game_HunduShijie_Server",
                type: "datetime2",
                nullable: false,
                comment: "创建时间",
                oldClrType: typeof(DateTime),
                oldType: "datetime2",
                oldComment: "创建时间");

            migrationBuilder.CreateTable(
                name: "cfg_arena_season",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    SeasonName = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    StartTime = table.Column<DateTime>(type: "datetime2", nullable: false),
                    EndTime = table.Column<DateTime>(type: "datetime2", nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    Description = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    RequiredLevel = table.Column<int>(type: "int", nullable: false),
                    RewardConfig = table.Column<string>(type: "nvarchar(2048)", maxLength: 2048, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_cfg_arena_season", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "cross_server_match",
                columns: table => new
                {
                    MatchId = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    BattleType = table.Column<int>(type: "int", nullable: false),
                    ParticipatingServerIds = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    StartTime = table.Column<DateTime>(type: "datetime2", nullable: false),
                    EndTime = table.Column<DateTime>(type: "datetime2", nullable: false),
                    WinnerServerId = table.Column<int>(type: "int", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_cross_server_match", x => x.MatchId);
                });

            migrationBuilder.CreateTable(
                name: "cross_server_player",
                columns: table => new
                {
                    CharacterId = table.Column<long>(type: "bigint", nullable: false),
                    SourceServerId = table.Column<int>(type: "int", nullable: false),
                    CurrentIslandId = table.Column<int>(type: "int", nullable: false),
                    CurrentMatchId = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    Kills = table.Column<int>(type: "int", nullable: false),
                    Deaths = table.Column<int>(type: "int", nullable: false),
                    ContributionPoints = table.Column<int>(type: "int", nullable: false),
                    LastTransferTime = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_cross_server_player", x => x.CharacterId);
                });

            migrationBuilder.CreateTable(
                name: "log_arena_match",
                columns: table => new
                {
                    MatchId = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    SeasonId = table.Column<int>(type: "int", nullable: false),
                    RedTeamCharacterId = table.Column<long>(type: "bigint", nullable: false),
                    BlueTeamCharacterId = table.Column<long>(type: "bigint", nullable: false),
                    WinnerTeam = table.Column<int>(type: "int", nullable: false),
                    RedTeamRatingChange = table.Column<int>(type: "int", nullable: false),
                    BlueTeamRatingChange = table.Column<int>(type: "int", nullable: false),
                    DurationSeconds = table.Column<int>(type: "int", nullable: false),
                    StartTime = table.Column<DateTime>(type: "datetime2", nullable: false),
                    EndTime = table.Column<DateTime>(type: "datetime2", nullable: false),
                    MatchReplayData = table.Column<string>(type: "nvarchar(2048)", maxLength: 2048, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_log_arena_match", x => x.MatchId);
                });

            migrationBuilder.CreateTable(
                name: "user_arena_record",
                columns: table => new
                {
                    CharacterId = table.Column<long>(type: "bigint", nullable: false),
                    SeasonId = table.Column<int>(type: "int", nullable: false),
                    CurrentRating = table.Column<int>(type: "int", nullable: false),
                    HighestRating = table.Column<int>(type: "int", nullable: false),
                    TotalMatches = table.Column<int>(type: "int", nullable: false),
                    Wins = table.Column<int>(type: "int", nullable: false),
                    Losses = table.Column<int>(type: "int", nullable: false),
                    Draws = table.Column<int>(type: "int", nullable: false),
                    CurrentWinStreak = table.Column<int>(type: "int", nullable: false),
                    HighestWinStreak = table.Column<int>(type: "int", nullable: false),
                    LastMatchTime = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_user_arena_record", x => x.CharacterId);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "cfg_arena_season");

            migrationBuilder.DropTable(
                name: "cross_server_match");

            migrationBuilder.DropTable(
                name: "cross_server_player");

            migrationBuilder.DropTable(
                name: "log_arena_match");

            migrationBuilder.DropTable(
                name: "user_arena_record");

            migrationBuilder.AlterTable(
                name: "Game_HunduShijie_Zone",
                comment: "游戏分区信息表",
                oldComment: "游戏分区信息表");

            migrationBuilder.AlterTable(
                name: "Game_HunduShijie_Server",
                comment: "游戏服务器信息表",
                oldComment: "游戏服务器信息表");

            migrationBuilder.AlterColumn<string>(
                name: "zone_name",
                table: "Game_HunduShijie_Zone",
                type: "varchar(255)",
                nullable: false,
                comment: "分区名称",
                oldClrType: typeof(string),
                oldType: "varchar(255)",
                oldComment: "分区名称");

            migrationBuilder.AlterColumn<DateTime>(
                name: "updated_at",
                table: "Game_HunduShijie_Zone",
                type: "datetime2",
                nullable: false,
                comment: "更新时间",
                oldClrType: typeof(DateTime),
                oldType: "datetime2",
                oldComment: "更新时间");

            migrationBuilder.AlterColumn<int>(
                name: "game_id",
                table: "Game_HunduShijie_Zone",
                type: "int",
                nullable: false,
                comment: "游戏Id",
                oldClrType: typeof(int),
                oldType: "int",
                oldComment: "游戏Id");

            migrationBuilder.AlterColumn<string>(
                name: "description",
                table: "Game_HunduShijie_Zone",
                type: "varchar(500)",
                nullable: false,
                comment: "分区描述",
                oldClrType: typeof(string),
                oldType: "varchar(500)",
                oldComment: "分区描述");

            migrationBuilder.AlterColumn<DateTime>(
                name: "created_at",
                table: "Game_HunduShijie_Zone",
                type: "datetime2",
                nullable: false,
                comment: "创建时间",
                oldClrType: typeof(DateTime),
                oldType: "datetime2",
                oldComment: "创建时间");

            migrationBuilder.AlterColumn<int>(
                name: "zone_id",
                table: "Game_HunduShijie_Server",
                type: "int",
                nullable: false,
                comment: "分区ID",
                oldClrType: typeof(int),
                oldType: "int",
                oldComment: "分区ID");

            migrationBuilder.AlterColumn<DateTime>(
                name: "updated_at",
                table: "Game_HunduShijie_Server",
                type: "datetime2",
                nullable: false,
                comment: "更新时间",
                oldClrType: typeof(DateTime),
                oldType: "datetime2",
                oldComment: "更新时间");

            migrationBuilder.AlterColumn<string>(
                name: "status",
                table: "Game_HunduShijie_Server",
                type: "varchar(50)",
                nullable: false,
                comment: "服务器状态",
                oldClrType: typeof(string),
                oldType: "varchar(50)",
                oldComment: "服务器状态");

            migrationBuilder.AlterColumn<string>(
                name: "server_name",
                table: "Game_HunduShijie_Server",
                type: "varchar(255)",
                nullable: false,
                comment: "服务器名称",
                oldClrType: typeof(string),
                oldType: "varchar(255)",
                oldComment: "服务器名称");

            migrationBuilder.AlterColumn<int>(
                name: "port",
                table: "Game_HunduShijie_Server",
                type: "int",
                nullable: false,
                comment: "端口",
                oldClrType: typeof(int),
                oldType: "int",
                oldComment: "端口");

            migrationBuilder.AlterColumn<int>(
                name: "max_players",
                table: "Game_HunduShijie_Server",
                type: "int",
                nullable: false,
                comment: "最大玩家数",
                oldClrType: typeof(int),
                oldType: "int",
                oldComment: "最大玩家数");

            migrationBuilder.AlterColumn<string>(
                name: "ip_address",
                table: "Game_HunduShijie_Server",
                type: "varchar(255)",
                nullable: false,
                comment: "IP地址",
                oldClrType: typeof(string),
                oldType: "varchar(255)",
                oldComment: "IP地址");

            migrationBuilder.AlterColumn<int>(
                name: "current_players",
                table: "Game_HunduShijie_Server",
                type: "int",
                nullable: false,
                comment: "当前玩家数",
                oldClrType: typeof(int),
                oldType: "int",
                oldComment: "当前玩家数");

            migrationBuilder.AlterColumn<DateTime>(
                name: "created_at",
                table: "Game_HunduShijie_Server",
                type: "datetime2",
                nullable: false,
                comment: "创建时间",
                oldClrType: typeof(DateTime),
                oldType: "datetime2",
                oldComment: "创建时间");
        }
    }
}
