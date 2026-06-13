using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Horizon.Entities.Migrations.IM
{
    /// <inheritdoc />
    public partial class InitIM : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "IM_ChatComplaint",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    IsValid = table.Column<bool>(type: "bit", nullable: false),
                    PassportId = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    TargetId = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Content = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    ChatComplaintType = table.Column<int>(type: "int", nullable: false),
                    Date = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_IM_ChatComplaint", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "IM_ChatGroup",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false),
                    IsValid = table.Column<bool>(type: "bit", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    LimitMember = table.Column<int>(type: "int", nullable: false),
                    IsNormal = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_IM_ChatGroup", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "IM_ContactData",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    IsValid = table.Column<bool>(type: "bit", nullable: false),
                    Type = table.Column<int>(type: "int", nullable: false),
                    DataName = table.Column<string>(type: "nvarchar(max)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_IM_ContactData", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "IM_CustomContactData",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    IsValid = table.Column<bool>(type: "bit", nullable: false),
                    PassportId = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Type = table.Column<int>(type: "int", nullable: false),
                    DataName = table.Column<string>(type: "nvarchar(max)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_IM_CustomContactData", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "IM_CustomPreference",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    IsValid = table.Column<bool>(type: "bit", nullable: false),
                    PassportId = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Type = table.Column<int>(type: "int", nullable: false),
                    Preference = table.Column<string>(type: "nvarchar(max)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_IM_CustomPreference", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "IM_IMGift",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    IsValid = table.Column<bool>(type: "bit", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    GiftPath = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Price = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    ExchangeRatio = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    TimeLenght = table.Column<long>(type: "bigint", nullable: false),
                    IsExchange = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_IM_IMGift", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "IM_Invitation",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    IsValid = table.Column<bool>(type: "bit", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Description = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    CreateDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    StartDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    EndDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    TimeLenght = table.Column<long>(type: "bigint", nullable: false),
                    InvitationType = table.Column<int>(type: "int", nullable: false),
                    RewardId = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_IM_Invitation", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "IM_Log_AddRelationship",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    IsValid = table.Column<bool>(type: "bit", nullable: false),
                    SourceId = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    TargetId = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    SourceType = table.Column<int>(type: "int", nullable: false),
                    Message = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    RedMoney = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    IMGiftId = table.Column<long>(type: "bigint", nullable: false),
                    GiftCount = table.Column<long>(type: "bigint", nullable: false),
                    IsAccpet = table.Column<bool>(type: "bit", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_IM_Log_AddRelationship", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "IM_Log_Invitation",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    IsValid = table.Column<bool>(type: "bit", nullable: false),
                    InvitationId = table.Column<long>(type: "bigint", nullable: false),
                    PassportId = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    BeInvitedId = table.Column<long>(type: "bigint", nullable: false),
                    BeInvitedDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Token = table.Column<string>(type: "nvarchar(max)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_IM_Log_Invitation", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "IM_M2BChatMessage",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    IsValid = table.Column<bool>(type: "bit", nullable: false),
                    PassportId = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    ChatBotId = table.Column<long>(type: "bigint", nullable: false),
                    ApplictionId = table.Column<long>(type: "bigint", nullable: false),
                    BurnAfterReading = table.Column<int>(type: "int", nullable: false),
                    OtherTime = table.Column<long>(type: "bigint", nullable: false),
                    Text = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    ImagePath = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    SoundPath = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    VideoPath = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    VoicePath = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Date = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_IM_M2BChatMessage", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "IM_M2MChatMessage",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    IsValid = table.Column<bool>(type: "bit", nullable: false),
                    SourceId = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    TargetId = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    IsRead = table.Column<bool>(type: "bit", nullable: true),
                    ApplictionId = table.Column<long>(type: "bigint", nullable: false),
                    BurnAfterReading = table.Column<int>(type: "int", nullable: false),
                    OtherTime = table.Column<long>(type: "bigint", nullable: false),
                    Text = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    ImagePath = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    SoundPath = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    VideoPath = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    VoicePath = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Date = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_IM_M2MChatMessage", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "IM_MemberContactData",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    IsValid = table.Column<bool>(type: "bit", nullable: false),
                    PassportId = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    ContactDataId = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_IM_MemberContactData", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "IM_MemberGift",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    IsValid = table.Column<bool>(type: "bit", nullable: false),
                    PassportId = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    IMGiftId = table.Column<long>(type: "bigint", nullable: false),
                    Count = table.Column<long>(type: "bigint", nullable: false),
                    GetDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    LastApplyDate = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_IM_MemberGift", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "IM_MemberPreference",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    IsValid = table.Column<bool>(type: "bit", nullable: false),
                    PassportId = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    PreferenceId = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_IM_MemberPreference", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "IM_MoneyPackage",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    IsValid = table.Column<bool>(type: "bit", nullable: false),
                    PassportId = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Balance = table.Column<decimal>(type: "decimal(18,2)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_IM_MoneyPackage", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "IM_Preference",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    IsValid = table.Column<bool>(type: "bit", nullable: false),
                    Type = table.Column<int>(type: "int", nullable: false),
                    Preference = table.Column<string>(type: "nvarchar(max)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_IM_Preference", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "IM_Relationship",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    IsValid = table.Column<bool>(type: "bit", nullable: false),
                    PassportId = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    RelationshipPassportId = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    RemarkName = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    RelationshipStatus = table.Column<int>(type: "int", nullable: false),
                    Date = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_IM_Relationship", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "IM_Reward",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    IsValid = table.Column<bool>(type: "bit", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    RewardType = table.Column<int>(type: "int", nullable: false),
                    Description = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Money = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    EntityPath = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    ThirdPartyDescription = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    ThirdPartyPath = table.Column<string>(type: "nvarchar(max)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_IM_Reward", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "IM_SysMoneyPackage",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    IsValid = table.Column<bool>(type: "bit", nullable: false),
                    OutPassportId = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    IntPassportId = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    IMGiftId = table.Column<long>(type: "bigint", nullable: false),
                    GiftCount = table.Column<long>(type: "bigint", nullable: false),
                    Amount = table.Column<decimal>(type: "decimal(18,2)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_IM_SysMoneyPackage", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "IM_M2GChatMessage",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    IsValid = table.Column<bool>(type: "bit", nullable: false),
                    PassportId = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    GroupId = table.Column<long>(type: "bigint", nullable: false),
                    ChatGroupId = table.Column<long>(type: "bigint", nullable: true),
                    ApplictionId = table.Column<long>(type: "bigint", nullable: false),
                    BurnAfterReading = table.Column<int>(type: "int", nullable: false),
                    OtherTime = table.Column<long>(type: "bigint", nullable: false),
                    Text = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    ImagePath = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    SoundPath = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    VideoPath = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    VoicePath = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Date = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_IM_M2GChatMessage", x => x.Id);
                    table.ForeignKey(
                        name: "FK_IM_M2GChatMessage_IM_ChatGroup_ChatGroupId",
                        column: x => x.ChatGroupId,
                        principalTable: "IM_ChatGroup",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateIndex(
                name: "IX_IM_M2GChatMessage_ChatGroupId",
                table: "IM_M2GChatMessage",
                column: "ChatGroupId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "IM_ChatComplaint");

            migrationBuilder.DropTable(
                name: "IM_ContactData");

            migrationBuilder.DropTable(
                name: "IM_CustomContactData");

            migrationBuilder.DropTable(
                name: "IM_CustomPreference");

            migrationBuilder.DropTable(
                name: "IM_IMGift");

            migrationBuilder.DropTable(
                name: "IM_Invitation");

            migrationBuilder.DropTable(
                name: "IM_Log_AddRelationship");

            migrationBuilder.DropTable(
                name: "IM_Log_Invitation");

            migrationBuilder.DropTable(
                name: "IM_M2BChatMessage");

            migrationBuilder.DropTable(
                name: "IM_M2GChatMessage");

            migrationBuilder.DropTable(
                name: "IM_M2MChatMessage");

            migrationBuilder.DropTable(
                name: "IM_MemberContactData");

            migrationBuilder.DropTable(
                name: "IM_MemberGift");

            migrationBuilder.DropTable(
                name: "IM_MemberPreference");

            migrationBuilder.DropTable(
                name: "IM_MoneyPackage");

            migrationBuilder.DropTable(
                name: "IM_Preference");

            migrationBuilder.DropTable(
                name: "IM_Relationship");

            migrationBuilder.DropTable(
                name: "IM_Reward");

            migrationBuilder.DropTable(
                name: "IM_SysMoneyPackage");

            migrationBuilder.DropTable(
                name: "IM_ChatGroup");
        }
    }
}
