using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Horizon.Entities.Migrations.Games
{
    /// <inheritdoc />
    public partial class AddEconomyLedgerAndSnapshot : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Game_HunduShijie_EconomyLedger",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    IsValid = table.Column<bool>(type: "bit", nullable: false),
                    character_id = table.Column<decimal>(type: "decimal(20,0)", nullable: false),
                    currency_type = table.Column<int>(type: "int", nullable: false),
                    delta = table.Column<long>(type: "bigint", nullable: false),
                    balance_after = table.Column<long>(type: "bigint", nullable: false),
                    reason = table.Column<string>(type: "nvarchar(96)", maxLength: 96, nullable: false),
                    created_at = table.Column<DateTime>(type: "datetime2", nullable: false),
                    GameId = table.Column<int>(type: "int", nullable: false),
                    AreaId = table.Column<int>(type: "int", nullable: false),
                    ServerId = table.Column<int>(type: "int", nullable: false),
                    GameUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Game_HunduShijie_EconomyLedger", x => x.id);
                },
                comment: "经济逐笔流水表");

            migrationBuilder.CreateTable(
                name: "Game_HunduShijie_EconomySnapshot",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    IsValid = table.Column<bool>(type: "bit", nullable: false),
                    currency_type = table.Column<int>(type: "int", nullable: false),
                    supply = table.Column<long>(type: "bigint", nullable: false),
                    earned = table.Column<long>(type: "bigint", nullable: false),
                    spent = table.Column<long>(type: "bigint", nullable: false),
                    accounts = table.Column<int>(type: "int", nullable: false),
                    rows_off_book = table.Column<int>(type: "int", nullable: false),
                    taken_at = table.Column<DateTime>(type: "datetime2", nullable: false),
                    GameId = table.Column<int>(type: "int", nullable: false),
                    AreaId = table.Column<int>(type: "int", nullable: false),
                    ServerId = table.Column<int>(type: "int", nullable: false),
                    GameUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Game_HunduShijie_EconomySnapshot", x => x.id);
                },
                comment: "经济周期快照表");

            migrationBuilder.CreateIndex(
                name: "IX_Game_HunduShijie_EconomyLedger_CharacterId_CreatedAt",
                table: "Game_HunduShijie_EconomyLedger",
                columns: new[] { "character_id", "created_at" });

            migrationBuilder.CreateIndex(
                name: "IX_Game_HunduShijie_EconomyLedger_CurrencyType_CreatedAt",
                table: "Game_HunduShijie_EconomyLedger",
                columns: new[] { "currency_type", "created_at" });

            migrationBuilder.CreateIndex(
                name: "IX_Game_HunduShijie_EconomySnapshot_CurrencyType_TakenAt",
                table: "Game_HunduShijie_EconomySnapshot",
                columns: new[] { "currency_type", "taken_at" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Game_HunduShijie_EconomyLedger");

            migrationBuilder.DropTable(
                name: "Game_HunduShijie_EconomySnapshot");
        }
    }
}
