using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Horizon.Entities.Migrations.Games
{
    /// <inheritdoc />
    public partial class ExpandGameUserPhoneLength : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<string>(
                name: "phone",
                table: "Game_HunduShijie_User",
                type: "varchar(100)",
                nullable: false,
                comment: "手机号",
                oldClrType: typeof(string),
                oldType: "varchar(20)",
                oldComment: "手机号");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<string>(
                name: "phone",
                table: "Game_HunduShijie_User",
                type: "varchar(20)",
                nullable: false,
                comment: "手机号",
                oldClrType: typeof(string),
                oldType: "varchar(100)",
                oldComment: "手机号");
        }
    }
}
