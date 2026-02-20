using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Horizon.Entities.Migrations.Games
{
    /// <inheritdoc />
    public partial class midfCharacter : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<decimal>(
                name: "character_id",
                table: "Game_HunduShijie_MaterialSynthesisLog",
                type: "decimal(20,0)",
                nullable: false,
                oldClrType: typeof(long),
                oldType: "bigint");

            migrationBuilder.AlterColumn<decimal>(
                name: "character_id",
                table: "Game_HunduShijie_Currency",
                type: "decimal(20,0)",
                nullable: false,
                oldClrType: typeof(long),
                oldType: "bigint");

            migrationBuilder.AlterColumn<decimal>(
                name: "character_id",
                table: "Game_HunduShijie_CharacterAttribute",
                type: "decimal(20,0)",
                nullable: false,
                oldClrType: typeof(long),
                oldType: "bigint");

            migrationBuilder.AlterColumn<decimal>(
                name: "character_id",
                table: "Game_HunduShijie_CharacterActivity",
                type: "decimal(20,0)",
                nullable: false,
                oldClrType: typeof(long),
                oldType: "bigint");

            migrationBuilder.AlterColumn<int>(
                name: "skin_color",
                table: "Game_HunduShijie_Character",
                type: "int",
                nullable: false,
                comment: "皮肤颜色",
                oldClrType: typeof(int),
                oldType: "int",
                oldComment: "皮肤颜色")
                .Annotation("Relational:ColumnOrder", 33)
                .OldAnnotation("Relational:ColumnOrder", 28);

            migrationBuilder.AlterColumn<double>(
                name: "rotation",
                table: "Game_HunduShijie_Character",
                type: "float",
                nullable: false,
                comment: "朝向",
                oldClrType: typeof(double),
                oldType: "float",
                oldComment: "朝向")
                .Annotation("Relational:ColumnOrder", 21)
                .OldAnnotation("Relational:ColumnOrder", 16);

            migrationBuilder.AlterColumn<int>(
                name: "reputation",
                table: "Game_HunduShijie_Character",
                type: "int",
                nullable: false,
                comment: "声望值",
                oldClrType: typeof(int),
                oldType: "int",
                oldComment: "声望值")
                .Annotation("Relational:ColumnOrder", 25)
                .OldAnnotation("Relational:ColumnOrder", 20);

            migrationBuilder.AlterColumn<double>(
                name: "position_z",
                table: "Game_HunduShijie_Character",
                type: "float",
                nullable: false,
                comment: "Z坐标",
                oldClrType: typeof(double),
                oldType: "float",
                oldComment: "Z坐标")
                .Annotation("Relational:ColumnOrder", 20)
                .OldAnnotation("Relational:ColumnOrder", 15);

            migrationBuilder.AlterColumn<double>(
                name: "position_y",
                table: "Game_HunduShijie_Character",
                type: "float",
                nullable: false,
                comment: "Y坐标",
                oldClrType: typeof(double),
                oldType: "float",
                oldComment: "Y坐标")
                .Annotation("Relational:ColumnOrder", 19)
                .OldAnnotation("Relational:ColumnOrder", 14);

            migrationBuilder.AlterColumn<double>(
                name: "position_x",
                table: "Game_HunduShijie_Character",
                type: "float",
                nullable: false,
                comment: "X坐标",
                oldClrType: typeof(double),
                oldType: "float",
                oldComment: "X坐标")
                .Annotation("Relational:ColumnOrder", 18)
                .OldAnnotation("Relational:ColumnOrder", 13);

            migrationBuilder.AlterColumn<int>(
                name: "map_id",
                table: "Game_HunduShijie_Character",
                type: "int",
                nullable: false,
                comment: "当前地图ID",
                oldClrType: typeof(int),
                oldType: "int",
                oldComment: "当前地图ID")
                .Annotation("Relational:ColumnOrder", 17)
                .OldAnnotation("Relational:ColumnOrder", 12);

            migrationBuilder.AlterColumn<DateTime>(
                name: "last_login_time",
                table: "Game_HunduShijie_Character",
                type: "datetime",
                nullable: true,
                comment: "最后登录时间",
                oldClrType: typeof(DateTime),
                oldType: "datetime",
                oldNullable: true,
                oldComment: "最后登录时间")
                .Annotation("Relational:ColumnOrder", 27)
                .OldAnnotation("Relational:ColumnOrder", 22);

            migrationBuilder.AlterColumn<bool>(
                name: "is_deleted",
                table: "Game_HunduShijie_Character",
                type: "bit",
                nullable: false,
                comment: "是否删除",
                oldClrType: typeof(bool),
                oldType: "bit",
                oldComment: "是否删除")
                .Annotation("Relational:ColumnOrder", 28)
                .OldAnnotation("Relational:ColumnOrder", 23);

            migrationBuilder.AlterColumn<int>(
                name: "hair_model",
                table: "Game_HunduShijie_Character",
                type: "int",
                nullable: false,
                comment: "头发模型",
                oldClrType: typeof(int),
                oldType: "int",
                oldComment: "头发模型")
                .Annotation("Relational:ColumnOrder", 30)
                .OldAnnotation("Relational:ColumnOrder", 25);

            migrationBuilder.AlterColumn<int>(
                name: "hair_color",
                table: "Game_HunduShijie_Character",
                type: "int",
                nullable: false,
                comment: "头发颜色",
                oldClrType: typeof(int),
                oldType: "int",
                oldComment: "头发颜色")
                .Annotation("Relational:ColumnOrder", 31)
                .OldAnnotation("Relational:ColumnOrder", 26);

            migrationBuilder.AlterColumn<long>(
                name: "game_user_id",
                table: "Game_HunduShijie_Character",
                type: "bigint",
                nullable: false,
                comment: "游戏用户ID",
                oldClrType: typeof(long),
                oldType: "bigint",
                oldComment: "游戏用户ID")
                .Annotation("Relational:ColumnOrder", 35)
                .OldAnnotation("Relational:ColumnOrder", 30);

            migrationBuilder.AlterColumn<int>(
                name: "face_model",
                table: "Game_HunduShijie_Character",
                type: "int",
                nullable: false,
                comment: "脸型模型",
                oldClrType: typeof(int),
                oldType: "int",
                oldComment: "脸型模型")
                .Annotation("Relational:ColumnOrder", 32)
                .OldAnnotation("Relational:ColumnOrder", 27);

            migrationBuilder.AlterColumn<int>(
                name: "eye_color",
                table: "Game_HunduShijie_Character",
                type: "int",
                nullable: false,
                comment: "眼睛颜色",
                oldClrType: typeof(int),
                oldType: "int",
                oldComment: "眼睛颜色")
                .Annotation("Relational:ColumnOrder", 34)
                .OldAnnotation("Relational:ColumnOrder", 29);

            migrationBuilder.AlterColumn<int>(
                name: "evil_points",
                table: "Game_HunduShijie_Character",
                type: "int",
                nullable: false,
                comment: "恶名值",
                oldClrType: typeof(int),
                oldType: "int",
                oldComment: "恶名值")
                .Annotation("Relational:ColumnOrder", 24)
                .OldAnnotation("Relational:ColumnOrder", 19);

            migrationBuilder.AlterColumn<DateTime>(
                name: "delete_time",
                table: "Game_HunduShijie_Character",
                type: "datetime",
                nullable: true,
                comment: "删除时间",
                oldClrType: typeof(DateTime),
                oldType: "datetime",
                oldNullable: true,
                oldComment: "删除时间")
                .Annotation("Relational:ColumnOrder", 29)
                .OldAnnotation("Relational:ColumnOrder", 24);

            migrationBuilder.AlterColumn<int>(
                name: "current_title_id",
                table: "Game_HunduShijie_Character",
                type: "int",
                nullable: true,
                comment: "当前称号ID",
                oldClrType: typeof(int),
                oldType: "int",
                oldNullable: true,
                oldComment: "当前称号ID")
                .Annotation("Relational:ColumnOrder", 22)
                .OldAnnotation("Relational:ColumnOrder", 17);

            migrationBuilder.AlterColumn<DateTime>(
                name: "create_time",
                table: "Game_HunduShijie_Character",
                type: "datetime",
                nullable: false,
                comment: "创建时间",
                oldClrType: typeof(DateTime),
                oldType: "datetime",
                oldComment: "创建时间")
                .Annotation("Relational:ColumnOrder", 26)
                .OldAnnotation("Relational:ColumnOrder", 21);

            migrationBuilder.AlterColumn<int>(
                name: "chivalry_points",
                table: "Game_HunduShijie_Character",
                type: "int",
                nullable: false,
                comment: "侠义值",
                oldClrType: typeof(int),
                oldType: "int",
                oldComment: "侠义值")
                .Annotation("Relational:ColumnOrder", 23)
                .OldAnnotation("Relational:ColumnOrder", 18);

            migrationBuilder.AddColumn<double>(
                name: "attack_power",
                table: "Game_HunduShijie_Character",
                type: "float",
                nullable: false,
                defaultValue: 0.0,
                comment: "攻击力")
                .Annotation("Relational:ColumnOrder", 14);

            migrationBuilder.AddColumn<double>(
                name: "defense",
                table: "Game_HunduShijie_Character",
                type: "float",
                nullable: false,
                defaultValue: 0.0,
                comment: "防御力")
                .Annotation("Relational:ColumnOrder", 15);

            migrationBuilder.AddColumn<double>(
                name: "health",
                table: "Game_HunduShijie_Character",
                type: "float",
                nullable: false,
                defaultValue: 0.0,
                comment: "当前血量")
                .Annotation("Relational:ColumnOrder", 12);

            migrationBuilder.AddColumn<double>(
                name: "max_health",
                table: "Game_HunduShijie_Character",
                type: "float",
                nullable: false,
                defaultValue: 0.0,
                comment: "最大血量")
                .Annotation("Relational:ColumnOrder", 13);

            migrationBuilder.AddColumn<int>(
                name: "wuxing_element",
                table: "Game_HunduShijie_Character",
                type: "int",
                nullable: false,
                defaultValue: 0,
                comment: "五行元素类型 0=无 1=金 2=木 3=水 4=火 5=土")
                .Annotation("Relational:ColumnOrder", 16);

            migrationBuilder.CreateIndex(
                name: "IX_User_AccountName",
                table: "Game_HunduShijie_User",
                column: "account_name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_User_LastLoginTime",
                table: "Game_HunduShijie_User",
                column: "last_login_time");

            migrationBuilder.CreateIndex(
                name: "IX_TradeLog_BuyerId",
                table: "Game_HunduShijie_TradeLog",
                column: "buyer_id");

            migrationBuilder.CreateIndex(
                name: "IX_TradeLog_SellerId",
                table: "Game_HunduShijie_TradeLog",
                column: "seller_id");

            migrationBuilder.CreateIndex(
                name: "IX_TradeLog_TradeTime",
                table: "Game_HunduShijie_TradeLog",
                column: "trade_time");

            migrationBuilder.CreateIndex(
                name: "IX_Guild_GuildName",
                table: "Game_HunduShijie_Guild",
                column: "guild_name");

            migrationBuilder.CreateIndex(
                name: "IX_Guild_LeaderId",
                table: "Game_HunduShijie_Guild",
                column: "leader_id");

            migrationBuilder.CreateIndex(
                name: "IX_ChatMessage_Channel_SendTime",
                table: "Game_HunduShijie_ChatMessage",
                columns: new[] { "channel", "send_time" });

            migrationBuilder.CreateIndex(
                name: "IX_ChatMessage_SenderId",
                table: "Game_HunduShijie_ChatMessage",
                column: "sender_id");

            migrationBuilder.CreateIndex(
                name: "IX_ChatMessage_SendTime",
                table: "Game_HunduShijie_ChatMessage",
                column: "send_time");

            migrationBuilder.CreateIndex(
                name: "IX_Character_CharacterName",
                table: "Game_HunduShijie_Character",
                column: "character_name");

            migrationBuilder.CreateIndex(
                name: "IX_Character_LastLoginTime",
                table: "Game_HunduShijie_Character",
                column: "last_login_time");

            migrationBuilder.CreateIndex(
                name: "IX_Character_UserId",
                table: "Game_HunduShijie_Character",
                column: "user_id");

            migrationBuilder.CreateIndex(
                name: "IX_Character_UserId_GameId",
                table: "Game_HunduShijie_Character",
                columns: new[] { "user_id", "GameId" });

            migrationBuilder.CreateIndex(
                name: "IX_Bag_CharacterId",
                table: "Game_HunduShijie_Bag",
                column: "character_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_User_AccountName",
                table: "Game_HunduShijie_User");

            migrationBuilder.DropIndex(
                name: "IX_User_LastLoginTime",
                table: "Game_HunduShijie_User");

            migrationBuilder.DropIndex(
                name: "IX_TradeLog_BuyerId",
                table: "Game_HunduShijie_TradeLog");

            migrationBuilder.DropIndex(
                name: "IX_TradeLog_SellerId",
                table: "Game_HunduShijie_TradeLog");

            migrationBuilder.DropIndex(
                name: "IX_TradeLog_TradeTime",
                table: "Game_HunduShijie_TradeLog");

            migrationBuilder.DropIndex(
                name: "IX_Guild_GuildName",
                table: "Game_HunduShijie_Guild");

            migrationBuilder.DropIndex(
                name: "IX_Guild_LeaderId",
                table: "Game_HunduShijie_Guild");

            migrationBuilder.DropIndex(
                name: "IX_ChatMessage_Channel_SendTime",
                table: "Game_HunduShijie_ChatMessage");

            migrationBuilder.DropIndex(
                name: "IX_ChatMessage_SenderId",
                table: "Game_HunduShijie_ChatMessage");

            migrationBuilder.DropIndex(
                name: "IX_ChatMessage_SendTime",
                table: "Game_HunduShijie_ChatMessage");

            migrationBuilder.DropIndex(
                name: "IX_Character_CharacterName",
                table: "Game_HunduShijie_Character");

            migrationBuilder.DropIndex(
                name: "IX_Character_LastLoginTime",
                table: "Game_HunduShijie_Character");

            migrationBuilder.DropIndex(
                name: "IX_Character_UserId",
                table: "Game_HunduShijie_Character");

            migrationBuilder.DropIndex(
                name: "IX_Character_UserId_GameId",
                table: "Game_HunduShijie_Character");

            migrationBuilder.DropIndex(
                name: "IX_Bag_CharacterId",
                table: "Game_HunduShijie_Bag");

            migrationBuilder.DropColumn(
                name: "attack_power",
                table: "Game_HunduShijie_Character");

            migrationBuilder.DropColumn(
                name: "defense",
                table: "Game_HunduShijie_Character");

            migrationBuilder.DropColumn(
                name: "health",
                table: "Game_HunduShijie_Character");

            migrationBuilder.DropColumn(
                name: "max_health",
                table: "Game_HunduShijie_Character");

            migrationBuilder.DropColumn(
                name: "wuxing_element",
                table: "Game_HunduShijie_Character");

            migrationBuilder.AlterColumn<long>(
                name: "character_id",
                table: "Game_HunduShijie_MaterialSynthesisLog",
                type: "bigint",
                nullable: false,
                oldClrType: typeof(decimal),
                oldType: "decimal(20,0)");

            migrationBuilder.AlterColumn<long>(
                name: "character_id",
                table: "Game_HunduShijie_Currency",
                type: "bigint",
                nullable: false,
                oldClrType: typeof(decimal),
                oldType: "decimal(20,0)");

            migrationBuilder.AlterColumn<long>(
                name: "character_id",
                table: "Game_HunduShijie_CharacterAttribute",
                type: "bigint",
                nullable: false,
                oldClrType: typeof(decimal),
                oldType: "decimal(20,0)");

            migrationBuilder.AlterColumn<long>(
                name: "character_id",
                table: "Game_HunduShijie_CharacterActivity",
                type: "bigint",
                nullable: false,
                oldClrType: typeof(decimal),
                oldType: "decimal(20,0)");

            migrationBuilder.AlterColumn<int>(
                name: "skin_color",
                table: "Game_HunduShijie_Character",
                type: "int",
                nullable: false,
                comment: "皮肤颜色",
                oldClrType: typeof(int),
                oldType: "int",
                oldComment: "皮肤颜色")
                .Annotation("Relational:ColumnOrder", 28)
                .OldAnnotation("Relational:ColumnOrder", 33);

            migrationBuilder.AlterColumn<double>(
                name: "rotation",
                table: "Game_HunduShijie_Character",
                type: "float",
                nullable: false,
                comment: "朝向",
                oldClrType: typeof(double),
                oldType: "float",
                oldComment: "朝向")
                .Annotation("Relational:ColumnOrder", 16)
                .OldAnnotation("Relational:ColumnOrder", 21);

            migrationBuilder.AlterColumn<int>(
                name: "reputation",
                table: "Game_HunduShijie_Character",
                type: "int",
                nullable: false,
                comment: "声望值",
                oldClrType: typeof(int),
                oldType: "int",
                oldComment: "声望值")
                .Annotation("Relational:ColumnOrder", 20)
                .OldAnnotation("Relational:ColumnOrder", 25);

            migrationBuilder.AlterColumn<double>(
                name: "position_z",
                table: "Game_HunduShijie_Character",
                type: "float",
                nullable: false,
                comment: "Z坐标",
                oldClrType: typeof(double),
                oldType: "float",
                oldComment: "Z坐标")
                .Annotation("Relational:ColumnOrder", 15)
                .OldAnnotation("Relational:ColumnOrder", 20);

            migrationBuilder.AlterColumn<double>(
                name: "position_y",
                table: "Game_HunduShijie_Character",
                type: "float",
                nullable: false,
                comment: "Y坐标",
                oldClrType: typeof(double),
                oldType: "float",
                oldComment: "Y坐标")
                .Annotation("Relational:ColumnOrder", 14)
                .OldAnnotation("Relational:ColumnOrder", 19);

            migrationBuilder.AlterColumn<double>(
                name: "position_x",
                table: "Game_HunduShijie_Character",
                type: "float",
                nullable: false,
                comment: "X坐标",
                oldClrType: typeof(double),
                oldType: "float",
                oldComment: "X坐标")
                .Annotation("Relational:ColumnOrder", 13)
                .OldAnnotation("Relational:ColumnOrder", 18);

            migrationBuilder.AlterColumn<int>(
                name: "map_id",
                table: "Game_HunduShijie_Character",
                type: "int",
                nullable: false,
                comment: "当前地图ID",
                oldClrType: typeof(int),
                oldType: "int",
                oldComment: "当前地图ID")
                .Annotation("Relational:ColumnOrder", 12)
                .OldAnnotation("Relational:ColumnOrder", 17);

            migrationBuilder.AlterColumn<DateTime>(
                name: "last_login_time",
                table: "Game_HunduShijie_Character",
                type: "datetime",
                nullable: true,
                comment: "最后登录时间",
                oldClrType: typeof(DateTime),
                oldType: "datetime",
                oldNullable: true,
                oldComment: "最后登录时间")
                .Annotation("Relational:ColumnOrder", 22)
                .OldAnnotation("Relational:ColumnOrder", 27);

            migrationBuilder.AlterColumn<bool>(
                name: "is_deleted",
                table: "Game_HunduShijie_Character",
                type: "bit",
                nullable: false,
                comment: "是否删除",
                oldClrType: typeof(bool),
                oldType: "bit",
                oldComment: "是否删除")
                .Annotation("Relational:ColumnOrder", 23)
                .OldAnnotation("Relational:ColumnOrder", 28);

            migrationBuilder.AlterColumn<int>(
                name: "hair_model",
                table: "Game_HunduShijie_Character",
                type: "int",
                nullable: false,
                comment: "头发模型",
                oldClrType: typeof(int),
                oldType: "int",
                oldComment: "头发模型")
                .Annotation("Relational:ColumnOrder", 25)
                .OldAnnotation("Relational:ColumnOrder", 30);

            migrationBuilder.AlterColumn<int>(
                name: "hair_color",
                table: "Game_HunduShijie_Character",
                type: "int",
                nullable: false,
                comment: "头发颜色",
                oldClrType: typeof(int),
                oldType: "int",
                oldComment: "头发颜色")
                .Annotation("Relational:ColumnOrder", 26)
                .OldAnnotation("Relational:ColumnOrder", 31);

            migrationBuilder.AlterColumn<long>(
                name: "game_user_id",
                table: "Game_HunduShijie_Character",
                type: "bigint",
                nullable: false,
                comment: "游戏用户ID",
                oldClrType: typeof(long),
                oldType: "bigint",
                oldComment: "游戏用户ID")
                .Annotation("Relational:ColumnOrder", 30)
                .OldAnnotation("Relational:ColumnOrder", 35);

            migrationBuilder.AlterColumn<int>(
                name: "face_model",
                table: "Game_HunduShijie_Character",
                type: "int",
                nullable: false,
                comment: "脸型模型",
                oldClrType: typeof(int),
                oldType: "int",
                oldComment: "脸型模型")
                .Annotation("Relational:ColumnOrder", 27)
                .OldAnnotation("Relational:ColumnOrder", 32);

            migrationBuilder.AlterColumn<int>(
                name: "eye_color",
                table: "Game_HunduShijie_Character",
                type: "int",
                nullable: false,
                comment: "眼睛颜色",
                oldClrType: typeof(int),
                oldType: "int",
                oldComment: "眼睛颜色")
                .Annotation("Relational:ColumnOrder", 29)
                .OldAnnotation("Relational:ColumnOrder", 34);

            migrationBuilder.AlterColumn<int>(
                name: "evil_points",
                table: "Game_HunduShijie_Character",
                type: "int",
                nullable: false,
                comment: "恶名值",
                oldClrType: typeof(int),
                oldType: "int",
                oldComment: "恶名值")
                .Annotation("Relational:ColumnOrder", 19)
                .OldAnnotation("Relational:ColumnOrder", 24);

            migrationBuilder.AlterColumn<DateTime>(
                name: "delete_time",
                table: "Game_HunduShijie_Character",
                type: "datetime",
                nullable: true,
                comment: "删除时间",
                oldClrType: typeof(DateTime),
                oldType: "datetime",
                oldNullable: true,
                oldComment: "删除时间")
                .Annotation("Relational:ColumnOrder", 24)
                .OldAnnotation("Relational:ColumnOrder", 29);

            migrationBuilder.AlterColumn<int>(
                name: "current_title_id",
                table: "Game_HunduShijie_Character",
                type: "int",
                nullable: true,
                comment: "当前称号ID",
                oldClrType: typeof(int),
                oldType: "int",
                oldNullable: true,
                oldComment: "当前称号ID")
                .Annotation("Relational:ColumnOrder", 17)
                .OldAnnotation("Relational:ColumnOrder", 22);

            migrationBuilder.AlterColumn<DateTime>(
                name: "create_time",
                table: "Game_HunduShijie_Character",
                type: "datetime",
                nullable: false,
                comment: "创建时间",
                oldClrType: typeof(DateTime),
                oldType: "datetime",
                oldComment: "创建时间")
                .Annotation("Relational:ColumnOrder", 21)
                .OldAnnotation("Relational:ColumnOrder", 26);

            migrationBuilder.AlterColumn<int>(
                name: "chivalry_points",
                table: "Game_HunduShijie_Character",
                type: "int",
                nullable: false,
                comment: "侠义值",
                oldClrType: typeof(int),
                oldType: "int",
                oldComment: "侠义值")
                .Annotation("Relational:ColumnOrder", 18)
                .OldAnnotation("Relational:ColumnOrder", 23);
        }
    }
}
