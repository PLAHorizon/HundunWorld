using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Horizon.Entities.Migrations.Flowers
{
    /// <inheritdoc />
    public partial class Flower523 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<double>(
                name: "BatteryLevel",
                table: "Flower_IoTDevice",
                type: "float",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "InstallDate",
                table: "Flower_IoTDevice",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Location",
                table: "Flower_IoTDevice",
                type: "nvarchar(max)",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "Manufacturer",
                table: "Flower_IoTDevice",
                type: "nvarchar(max)",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "Model",
                table: "Flower_IoTDevice",
                type: "nvarchar(max)",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "Remark",
                table: "Flower_IoTDevice",
                type: "nvarchar(max)",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "SensorCapabilities",
                table: "Flower_IoTDevice",
                type: "nvarchar(max)",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "SerialNumber",
                table: "Flower_IoTDevice",
                type: "nvarchar(max)",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<double>(
                name: "SignalStrength",
                table: "Flower_IoTDevice",
                type: "float",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "BatteryLevel",
                table: "Flower_IoTDevice");

            migrationBuilder.DropColumn(
                name: "InstallDate",
                table: "Flower_IoTDevice");

            migrationBuilder.DropColumn(
                name: "Location",
                table: "Flower_IoTDevice");

            migrationBuilder.DropColumn(
                name: "Manufacturer",
                table: "Flower_IoTDevice");

            migrationBuilder.DropColumn(
                name: "Model",
                table: "Flower_IoTDevice");

            migrationBuilder.DropColumn(
                name: "Remark",
                table: "Flower_IoTDevice");

            migrationBuilder.DropColumn(
                name: "SensorCapabilities",
                table: "Flower_IoTDevice");

            migrationBuilder.DropColumn(
                name: "SerialNumber",
                table: "Flower_IoTDevice");

            migrationBuilder.DropColumn(
                name: "SignalStrength",
                table: "Flower_IoTDevice");
        }
    }
}
