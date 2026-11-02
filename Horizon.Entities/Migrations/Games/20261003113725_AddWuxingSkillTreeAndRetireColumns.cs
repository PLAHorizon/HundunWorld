using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Horizon.Entities.Migrations.Games
{
    /// <inheritdoc />
    public partial class AddWuxingSkillTreeAndRetireColumns : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<double>(
                name: "damage_multiplier",
                table: "Game_HunduShijie_SkillTemplate",
                type: "float",
                nullable: false,
                defaultValue: 0.0,
                comment: "技能伤害倍率（权威结算乘算项）")
                .Annotation("Relational:ColumnOrder", 39);

            migrationBuilder.AddColumn<int>(
                name: "grade_stage",
                table: "Game_HunduShijie_SkillTemplate",
                type: "int",
                nullable: false,
                defaultValue: 0,
                comment: "阶内境界序号 1初 2中 3极")
                .Annotation("Relational:ColumnOrder", 37);

            migrationBuilder.AddColumn<string>(
                name: "series_id",
                table: "Game_HunduShijie_SkillTemplate",
                type: "nvarchar(50)",
                nullable: false,
                defaultValue: "",
                comment: "技系标识：五行_阶，同阶三式同属一个技系")
                .Annotation("Relational:ColumnOrder", 38);

            migrationBuilder.AddColumn<int>(
                name: "skill_grade",
                table: "Game_HunduShijie_SkillTemplate",
                type: "int",
                nullable: false,
                defaultValue: 0,
                comment: "技能阶位 1白 2黄 3玄 4地 5天 6道 7混沌")
                .Annotation("Relational:ColumnOrder", 36);

            migrationBuilder.AddColumn<bool>(
                name: "is_retired",
                table: "Game_HunduShijie_CharacterSkill",
                type: "bit",
                nullable: false,
                defaultValue: false,
                comment: "已被相生演化封存：不可施放、不显示，散功可解封")
                .Annotation("Relational:ColumnOrder", 20);

            migrationBuilder.AddColumn<int>(
                name: "retired_into_skill_id",
                table: "Game_HunduShijie_CharacterSkill",
                type: "int",
                nullable: true,
                comment: "封存去向技能ID（演化产物）")
                .Annotation("Relational:ColumnOrder", 21);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "damage_multiplier",
                table: "Game_HunduShijie_SkillTemplate");

            migrationBuilder.DropColumn(
                name: "grade_stage",
                table: "Game_HunduShijie_SkillTemplate");

            migrationBuilder.DropColumn(
                name: "series_id",
                table: "Game_HunduShijie_SkillTemplate");

            migrationBuilder.DropColumn(
                name: "skill_grade",
                table: "Game_HunduShijie_SkillTemplate");

            migrationBuilder.DropColumn(
                name: "is_retired",
                table: "Game_HunduShijie_CharacterSkill");

            migrationBuilder.DropColumn(
                name: "retired_into_skill_id",
                table: "Game_HunduShijie_CharacterSkill");
        }
    }
}
