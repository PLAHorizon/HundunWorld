using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Horizon.Entities.Migrations.Games
{
    /// <inheritdoc />
    public partial class AddWuxingQuestTemplate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Game_HunduShijie_QuestTemplate",
                columns: table => new
                {
                    quest_id = table.Column<int>(type: "int", nullable: false, comment: "任务模板ID")
                        .Annotation("SqlServer:Identity", "1, 1"),
                    quest_name = table.Column<string>(type: "nvarchar(50)", nullable: false, comment: "任务名称"),
                    description = table.Column<string>(type: "nvarchar(500)", nullable: false, comment: "任务描述"),
                    quest_type = table.Column<int>(type: "int", nullable: false, comment: "任务类型 0主线 1支线 2日常 3周常"),
                    level_require = table.Column<int>(type: "int", nullable: false, comment: "接取所需等级"),
                    wuxing_domain = table.Column<int>(type: "int", nullable: false, comment: "五行域 1金2木3水4火5土 0=通用"),
                    rewards = table.Column<string>(type: "nvarchar(max)", nullable: false, comment: "奖励 JSON：{\"item:810101\":1,\"favor:1\":20,\"exp\":100}"),
                    objectives = table.Column<string>(type: "nvarchar(max)", nullable: false, comment: "完成目标 JSON"),
                    repeat_limit = table.Column<int>(type: "int", nullable: false, comment: "可重复完成次数 0=一次性"),
                    IsValid = table.Column<bool>(type: "bit", nullable: false),
                    GameId = table.Column<int>(type: "int", nullable: false),
                    AreaId = table.Column<int>(type: "int", nullable: false),
                    ServerId = table.Column<int>(type: "int", nullable: false),
                    GameUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Game_HunduShijie_QuestTemplate", x => x.quest_id);
                },
                comment: "任务模板表");

            migrationBuilder.CreateTable(
                name: "gm_script_grant",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    reason = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false, comment: "授予理由"),
                    IsValid = table.Column<bool>(type: "bit", nullable: false),
                    operator_character_id = table.Column<long>(type: "bigint", nullable: false, comment: "被授权角色ID"),
                    flags = table.Column<long>(type: "bigint", nullable: false, comment: "权限位"),
                    granted_by = table.Column<long>(type: "bigint", nullable: false, comment: "授予者角色ID，0表示系统"),
                    granted_at_utc = table.Column<DateTime>(type: "datetime2", nullable: false, comment: "授予时刻UTC"),
                    expires_at_utc = table.Column<DateTime>(type: "datetime2", nullable: false, comment: "到期时刻UTC"),
                    revoked_at_utc = table.Column<DateTime>(type: "datetime2", nullable: true, comment: "回收时刻UTC，null表示未回收")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_gm_script_grant", x => x.Id);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Game_HunduShijie_QuestTemplate");

            migrationBuilder.DropTable(
                name: "gm_script_grant");
        }
    }
}
