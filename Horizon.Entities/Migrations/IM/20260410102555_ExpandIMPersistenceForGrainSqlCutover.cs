using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Horizon.Entities.Migrations.IM
{
    /// <inheritdoc />
    public partial class ExpandIMPersistenceForGrainSqlCutover : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<string>(
                name: "RelationshipPassportId",
                table: "IM_Relationship",
                type: "nvarchar(450)",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(max)");

            migrationBuilder.AlterColumn<string>(
                name: "PassportId",
                table: "IM_Relationship",
                type: "nvarchar(450)",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(max)");

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

            migrationBuilder.AlterColumn<string>(
                name: "TargetId",
                table: "IM_M2MChatMessage",
                type: "nvarchar(450)",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(max)");

            migrationBuilder.AlterColumn<string>(
                name: "SourceId",
                table: "IM_M2MChatMessage",
                type: "nvarchar(450)",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(max)");

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

            migrationBuilder.AlterColumn<string>(
                name: "TargetId",
                table: "IM_Log_AddRelationship",
                type: "nvarchar(450)",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(max)");

            migrationBuilder.AlterColumn<string>(
                name: "SourceId",
                table: "IM_Log_AddRelationship",
                type: "nvarchar(450)",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(max)");

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

            migrationBuilder.CreateTable(
                name: "IM_ChatGroupMember",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    IsValid = table.Column<bool>(type: "bit", nullable: false),
                    GroupId = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    PassportId = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    Nickname = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Avatar = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    GroupNickname = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Role = table.Column<int>(type: "int", nullable: false),
                    JoinTime = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_IM_ChatGroupMember", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "IM_Conversation",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    IsValid = table.Column<bool>(type: "bit", nullable: false),
                    OwnerPassportId = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    ConversationId = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    ChatType = table.Column<int>(type: "int", nullable: false),
                    TargetId = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    TargetName = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    TargetAvatar = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    LastMessage = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    LastMessageTime = table.Column<long>(type: "bigint", nullable: false),
                    UnreadCount = table.Column<int>(type: "int", nullable: false),
                    IsPinned = table.Column<bool>(type: "bit", nullable: false),
                    IsMuted = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_IM_Conversation", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_IM_Relationship_PassportId_RelationshipPassportId",
                table: "IM_Relationship",
                columns: new[] { "PassportId", "RelationshipPassportId" });

            migrationBuilder.CreateIndex(
                name: "IX_IM_M2MChatMessage_SourceId_TargetId_Date",
                table: "IM_M2MChatMessage",
                columns: new[] { "SourceId", "TargetId", "Date" });

            migrationBuilder.CreateIndex(
                name: "IX_IM_M2GChatMessage_GroupId_Date",
                table: "IM_M2GChatMessage",
                columns: new[] { "GroupId", "Date" });

            migrationBuilder.CreateIndex(
                name: "IX_IM_Log_AddRelationship_SourceId_TargetId_IsAccpet",
                table: "IM_Log_AddRelationship",
                columns: new[] { "SourceId", "TargetId", "IsAccpet" });

            migrationBuilder.CreateIndex(
                name: "IX_IM_ChatGroup_ExternalGroupId",
                table: "IM_ChatGroup",
                column: "ExternalGroupId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_IM_ChatGroupMember_GroupId_PassportId",
                table: "IM_ChatGroupMember",
                columns: new[] { "GroupId", "PassportId" });

            migrationBuilder.CreateIndex(
                name: "IX_IM_Conversation_OwnerPassportId_ConversationId",
                table: "IM_Conversation",
                columns: new[] { "OwnerPassportId", "ConversationId" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "IM_ChatGroupMember");

            migrationBuilder.DropTable(
                name: "IM_Conversation");

            migrationBuilder.DropIndex(
                name: "IX_IM_Relationship_PassportId_RelationshipPassportId",
                table: "IM_Relationship");

            migrationBuilder.DropIndex(
                name: "IX_IM_M2MChatMessage_SourceId_TargetId_Date",
                table: "IM_M2MChatMessage");

            migrationBuilder.DropIndex(
                name: "IX_IM_M2GChatMessage_GroupId_Date",
                table: "IM_M2GChatMessage");

            migrationBuilder.DropIndex(
                name: "IX_IM_Log_AddRelationship_SourceId_TargetId_IsAccpet",
                table: "IM_Log_AddRelationship");

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

            migrationBuilder.AlterColumn<string>(
                name: "RelationshipPassportId",
                table: "IM_Relationship",
                type: "nvarchar(max)",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(450)");

            migrationBuilder.AlterColumn<string>(
                name: "PassportId",
                table: "IM_Relationship",
                type: "nvarchar(max)",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(450)");

            migrationBuilder.AlterColumn<string>(
                name: "TargetId",
                table: "IM_M2MChatMessage",
                type: "nvarchar(max)",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(450)");

            migrationBuilder.AlterColumn<string>(
                name: "SourceId",
                table: "IM_M2MChatMessage",
                type: "nvarchar(max)",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(450)");

            migrationBuilder.AlterColumn<long>(
                name: "GroupId",
                table: "IM_M2GChatMessage",
                type: "bigint",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(450)");

            migrationBuilder.AlterColumn<string>(
                name: "TargetId",
                table: "IM_Log_AddRelationship",
                type: "nvarchar(max)",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(450)");

            migrationBuilder.AlterColumn<string>(
                name: "SourceId",
                table: "IM_Log_AddRelationship",
                type: "nvarchar(max)",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(450)");
        }
    }
}
