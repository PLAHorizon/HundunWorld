using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Horizon.Entities.Migrations.Games
{
    /// <inheritdoc />
    public partial class AddSkillWuxingColumns : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "skill_attack",
                table: "Game_HunduShijie_SkillTemplate",
                type: "int",
                nullable: false,
                defaultValue: 0,
                comment: "技能附加的该行五行攻击力（求和侧）")
                .Annotation("Relational:ColumnOrder", 34);

            migrationBuilder.AddColumn<int>(
                name: "skill_penetration",
                table: "Game_HunduShijie_SkillTemplate",
                type: "int",
                nullable: false,
                defaultValue: 0,
                comment: "技能附加的该行五行破防（取高侧）")
                .Annotation("Relational:ColumnOrder", 35);

            migrationBuilder.AddColumn<int>(
                name: "wuxing_element",
                table: "Game_HunduShijie_SkillTemplate",
                type: "int",
                nullable: false,
                defaultValue: 0,
                comment: "技能五行（0=无属性，1金2木3水4火5土）")
                .Annotation("Relational:ColumnOrder", 33);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "skill_attack",
                table: "Game_HunduShijie_SkillTemplate");

            migrationBuilder.DropColumn(
                name: "skill_penetration",
                table: "Game_HunduShijie_SkillTemplate");

            migrationBuilder.DropColumn(
                name: "wuxing_element",
                table: "Game_HunduShijie_SkillTemplate");
        }
    }
}
