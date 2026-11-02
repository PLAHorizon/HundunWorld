using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Horizon.Entities.Migrations.Games
{
    /// <inheritdoc />
    public partial class AddQuestEditorColumns : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "content_version",
                table: "Game_HunduShijie_QuestTemplate",
                type: "int",
                nullable: false,
                defaultValue: 0,
                comment: "内容版本号 每次入库自增")
                .Annotation("Relational:ColumnOrder", 18);

            migrationBuilder.AddColumn<int>(
                name: "giver_kind",
                table: "Game_HunduShijie_QuestTemplate",
                type: "int",
                nullable: false,
                defaultValue: 0,
                comment: "发布者类别 1NPC 2怪物 3物品 4物件 5区域")
                .Annotation("Relational:ColumnOrder", 10);

            migrationBuilder.AddColumn<int>(
                name: "giver_spawn_point_id",
                table: "Game_HunduShijie_QuestTemplate",
                type: "int",
                nullable: false,
                defaultValue: 0,
                comment: "发布者限定生成点 0=不限")
                .Annotation("Relational:ColumnOrder", 13);

            migrationBuilder.AddColumn<int>(
                name: "giver_template_id",
                table: "Game_HunduShijie_QuestTemplate",
                type: "int",
                nullable: false,
                defaultValue: 0,
                comment: "发布者模板ID 0=未配置")
                .Annotation("Relational:ColumnOrder", 11);

            migrationBuilder.AddColumn<int>(
                name: "giver_zone_id",
                table: "Game_HunduShijie_QuestTemplate",
                type: "int",
                nullable: false,
                defaultValue: 0,
                comment: "发布者限定区域 0=不限")
                .Annotation("Relational:ColumnOrder", 12);

            migrationBuilder.AddColumn<string>(
                name: "prerequisite_quests",
                table: "Game_HunduShijie_QuestTemplate",
                type: "varchar(200)",
                nullable: false,
                defaultValue: "",
                comment: "前置任务ID 逗号分隔")
                .Annotation("Relational:ColumnOrder", 16);

            migrationBuilder.AddColumn<int>(
                name: "publish_state",
                table: "Game_HunduShijie_QuestTemplate",
                type: "int",
                nullable: false,
                defaultValue: 0,
                comment: "发布状态 0下线 1已发布（实时开关，不影响已接实例）")
                .Annotation("Relational:ColumnOrder", 17);

            migrationBuilder.AddColumn<int>(
                name: "turn_in_kind",
                table: "Game_HunduShijie_QuestTemplate",
                type: "int",
                nullable: false,
                defaultValue: 0,
                comment: "交付者类别 0=沿用发布者")
                .Annotation("Relational:ColumnOrder", 14);

            migrationBuilder.AddColumn<int>(
                name: "turn_in_template_id",
                table: "Game_HunduShijie_QuestTemplate",
                type: "int",
                nullable: false,
                defaultValue: 0,
                comment: "交付者模板ID 0=沿用发布者")
                .Annotation("Relational:ColumnOrder", 15);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "content_version",
                table: "Game_HunduShijie_QuestTemplate");

            migrationBuilder.DropColumn(
                name: "giver_kind",
                table: "Game_HunduShijie_QuestTemplate");

            migrationBuilder.DropColumn(
                name: "giver_spawn_point_id",
                table: "Game_HunduShijie_QuestTemplate");

            migrationBuilder.DropColumn(
                name: "giver_template_id",
                table: "Game_HunduShijie_QuestTemplate");

            migrationBuilder.DropColumn(
                name: "giver_zone_id",
                table: "Game_HunduShijie_QuestTemplate");

            migrationBuilder.DropColumn(
                name: "prerequisite_quests",
                table: "Game_HunduShijie_QuestTemplate");

            migrationBuilder.DropColumn(
                name: "publish_state",
                table: "Game_HunduShijie_QuestTemplate");

            migrationBuilder.DropColumn(
                name: "turn_in_kind",
                table: "Game_HunduShijie_QuestTemplate");

            migrationBuilder.DropColumn(
                name: "turn_in_template_id",
                table: "Game_HunduShijie_QuestTemplate");
        }
    }
}
