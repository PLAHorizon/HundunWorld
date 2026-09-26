using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Horizon.Entities.Migrations.Games
{
    /// <inheritdoc />
    public partial class AddCharacterAttributePoints : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "earth_point",
                table: "Game_HunduShijie_CharacterAttribute",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "fire_point",
                table: "Game_HunduShijie_CharacterAttribute",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "metal_point",
                table: "Game_HunduShijie_CharacterAttribute",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "water_point",
                table: "Game_HunduShijie_CharacterAttribute",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "wood_point",
                table: "Game_HunduShijie_CharacterAttribute",
                type: "int",
                nullable: false,
                defaultValue: 0);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "earth_point",
                table: "Game_HunduShijie_CharacterAttribute");

            migrationBuilder.DropColumn(
                name: "fire_point",
                table: "Game_HunduShijie_CharacterAttribute");

            migrationBuilder.DropColumn(
                name: "metal_point",
                table: "Game_HunduShijie_CharacterAttribute");

            migrationBuilder.DropColumn(
                name: "water_point",
                table: "Game_HunduShijie_CharacterAttribute");

            migrationBuilder.DropColumn(
                name: "wood_point",
                table: "Game_HunduShijie_CharacterAttribute");
        }
    }
}
