using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Horizon.Entities.Migrations.Flowers
{
    /// <inheritdoc />
    public partial class Flower524 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "UserId",
                table: "Flower_PlantingBatch",
                type: "uniqueidentifier",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"),
                comment: "用户ID");

            migrationBuilder.AddColumn<string>(
                name: "NotifyId",
                table: "Flower_PaymentStatusChangeLog",
                type: "varchar(128)",
                maxLength: 128,
                nullable: false,
                defaultValue: "",
                comment: "通知ID");

            migrationBuilder.CreateIndex(
                name: "IX_FlowerPaymentStatusChangeLog_NotifyId",
                table: "Flower_PaymentStatusChangeLog",
                column: "NotifyId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_FlowerPaymentStatusChangeLog_NotifyId",
                table: "Flower_PaymentStatusChangeLog");

            migrationBuilder.DropColumn(
                name: "UserId",
                table: "Flower_PlantingBatch");

            migrationBuilder.DropColumn(
                name: "NotifyId",
                table: "Flower_PaymentStatusChangeLog");
        }
    }
}
