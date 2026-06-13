using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Horizon.Entities.Migrations.Flowers
{
    /// <inheritdoc />
    public partial class Flower525 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Passport",
                table: "Flower_SensorReading",
                type: "varchar(64)",
                maxLength: 64,
                nullable: false,
                defaultValue: "",
                comment: "通行证ID");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Passport",
                table: "Flower_SensorReading");
        }
    }
}
