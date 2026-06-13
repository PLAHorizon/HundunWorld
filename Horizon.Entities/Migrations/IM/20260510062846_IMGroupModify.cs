using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Horizon.Entities.Migrations.IM
{
    /// <inheritdoc />
    public partial class IMGroupModify : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_IM_ChatGroup_ExternalGroupId",
                table: "IM_ChatGroup");

            migrationBuilder.DropColumn(
                name: "Avatar",
                table: "IM_Relationship");

            migrationBuilder.DropColumn(
                name: "NickName",
                table: "IM_Relationship");

            migrationBuilder.DropColumn(
                name: "AttachmentsJson",
                table: "IM_M2MChatMessage");

            migrationBuilder.DropColumn(
                name: "ClientMessageId",
                table: "IM_M2MChatMessage");

            migrationBuilder.DropColumn(
                name: "ContentType",
                table: "IM_M2MChatMessage");

            migrationBuilder.DropColumn(
                name: "ExtDataJson",
                table: "IM_M2MChatMessage");

            migrationBuilder.DropColumn(
                name: "SourceAvatar",
                table: "IM_M2MChatMessage");

            migrationBuilder.DropColumn(
                name: "SourceName",
                table: "IM_M2MChatMessage");

            migrationBuilder.DropColumn(
                name: "Status",
                table: "IM_M2MChatMessage");

            migrationBuilder.DropColumn(
                name: "AttachmentsJson",
                table: "IM_M2GChatMessage");

            migrationBuilder.DropColumn(
                name: "ClientMessageId",
                table: "IM_M2GChatMessage");

            migrationBuilder.DropColumn(
                name: "ContentType",
                table: "IM_M2GChatMessage");

            migrationBuilder.DropColumn(
                name: "MentionAll",
                table: "IM_M2GChatMessage");

            migrationBuilder.DropColumn(
                name: "MentionedUserIdsJson",
                table: "IM_M2GChatMessage");

            migrationBuilder.DropColumn(
                name: "PassportAvatar",
                table: "IM_M2GChatMessage");

            migrationBuilder.DropColumn(
                name: "PassportName",
                table: "IM_M2GChatMessage");

            migrationBuilder.DropColumn(
                name: "Status",
                table: "IM_M2GChatMessage");

            migrationBuilder.DropColumn(
                name: "Announcement",
                table: "IM_ChatGroup");

            migrationBuilder.DropColumn(
                name: "Avatar",
                table: "IM_ChatGroup");

            migrationBuilder.DropColumn(
                name: "CreateTime",
                table: "IM_ChatGroup");

            migrationBuilder.DropColumn(
                name: "ExternalGroupId",
                table: "IM_ChatGroup");

            migrationBuilder.DropColumn(
                name: "IsDisbanded",
                table: "IM_ChatGroup");

            migrationBuilder.DropColumn(
                name: "MaxMembers",
                table: "IM_ChatGroup");

            migrationBuilder.DropColumn(
                name: "OwnerPassportId",
                table: "IM_ChatGroup");

            migrationBuilder.AlterColumn<long>(
                name: "GroupId",
                table: "IM_M2GChatMessage",
                type: "bigint",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(450)");

            migrationBuilder.CreateIndex(
                name: "IX_IM_ChatGroup_Id",
                table: "IM_ChatGroup",
                column: "Id",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_IM_ChatGroup_Id",
                table: "IM_ChatGroup");

            migrationBuilder.AddColumn<string>(
                name: "Avatar",
                table: "IM_Relationship",
                type: "nvarchar(max)",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "NickName",
                table: "IM_Relationship",
                type: "nvarchar(max)",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "AttachmentsJson",
                table: "IM_M2MChatMessage",
                type: "nvarchar(max)",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "ClientMessageId",
                table: "IM_M2MChatMessage",
                type: "nvarchar(max)",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<int>(
                name: "ContentType",
                table: "IM_M2MChatMessage",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "ExtDataJson",
                table: "IM_M2MChatMessage",
                type: "nvarchar(max)",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "SourceAvatar",
                table: "IM_M2MChatMessage",
                type: "nvarchar(max)",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "SourceName",
                table: "IM_M2MChatMessage",
                type: "nvarchar(max)",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<int>(
                name: "Status",
                table: "IM_M2MChatMessage",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AlterColumn<string>(
                name: "GroupId",
                table: "IM_M2GChatMessage",
                type: "nvarchar(450)",
                nullable: false,
                oldClrType: typeof(long),
                oldType: "bigint");

            migrationBuilder.AddColumn<string>(
                name: "AttachmentsJson",
                table: "IM_M2GChatMessage",
                type: "nvarchar(max)",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "ClientMessageId",
                table: "IM_M2GChatMessage",
                type: "nvarchar(max)",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<int>(
                name: "ContentType",
                table: "IM_M2GChatMessage",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<bool>(
                name: "MentionAll",
                table: "IM_M2GChatMessage",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "MentionedUserIdsJson",
                table: "IM_M2GChatMessage",
                type: "nvarchar(max)",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "PassportAvatar",
                table: "IM_M2GChatMessage",
                type: "nvarchar(max)",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "PassportName",
                table: "IM_M2GChatMessage",
                type: "nvarchar(max)",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<int>(
                name: "Status",
                table: "IM_M2GChatMessage",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "Announcement",
                table: "IM_ChatGroup",
                type: "nvarchar(max)",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "Avatar",
                table: "IM_ChatGroup",
                type: "nvarchar(max)",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<long>(
                name: "CreateTime",
                table: "IM_ChatGroup",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.AddColumn<string>(
                name: "ExternalGroupId",
                table: "IM_ChatGroup",
                type: "nvarchar(450)",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<bool>(
                name: "IsDisbanded",
                table: "IM_ChatGroup",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<int>(
                name: "MaxMembers",
                table: "IM_ChatGroup",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "OwnerPassportId",
                table: "IM_ChatGroup",
                type: "nvarchar(max)",
                nullable: false,
                defaultValue: "");

            migrationBuilder.CreateIndex(
                name: "IX_IM_ChatGroup_ExternalGroupId",
                table: "IM_ChatGroup",
                column: "ExternalGroupId",
                unique: true);
        }
    }
}
