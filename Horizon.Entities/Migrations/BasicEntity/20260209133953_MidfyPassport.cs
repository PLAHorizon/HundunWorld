using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Horizon.Entities.Migrations.BasicEntity
{
    /// <inheritdoc />
    public partial class MidfyPassport : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Basic_Sys_Applications",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    IsValid = table.Column<bool>(type: "bit", nullable: false),
                    AppType = table.Column<int>(type: "int", nullable: false, comment: "应用类型"),
                    Name = table.Column<string>(type: "nvarchar(max)", nullable: false, comment: "应用名称"),
                    Description = table.Column<string>(type: "nvarchar(max)", nullable: false, comment: "应用简述"),
                    Contacts = table.Column<string>(type: "nvarchar(max)", nullable: false, comment: "应用负责人"),
                    Team = table.Column<string>(type: "nvarchar(max)", nullable: false, comment: "团队"),
                    Home = table.Column<string>(type: "nvarchar(max)", nullable: false, comment: "应用网站首页地址"),
                    Logo = table.Column<string>(type: "nvarchar(max)", nullable: false, comment: "应用Logo"),
                    Date = table.Column<DateTime>(type: "datetime2", nullable: true, comment: "应用上线时间"),
                    OverDate = table.Column<DateTime>(type: "datetime2", nullable: true, comment: "应用下线时间")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Basic_Sys_Applications", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Basic_Sys_DDBs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    DDbType = table.Column<int>(type: "int", nullable: false, comment: "数据库类型"),
                    IP = table.Column<string>(type: "varchar(39)", maxLength: 39, nullable: false, comment: "数据库IP"),
                    Port = table.Column<int>(type: "int", nullable: false, comment: "数据库端口"),
                    Account = table.Column<string>(type: "varchar(32)", maxLength: 32, nullable: false, comment: "数据账号"),
                    Password = table.Column<string>(type: "varchar(256)", maxLength: 256, nullable: false, comment: "数据密码"),
                    COMMENTS = table.Column<string>(type: "varchar(256)", maxLength: 256, nullable: false, comment: "备注"),
                    MODITIME = table.Column<DateTime>(type: "datetime", nullable: false, comment: "时间戳,分库建立时间"),
                    IsValid = table.Column<bool>(type: "bit", nullable: false),
                    AppType = table.Column<int>(type: "int", nullable: false, comment: "数据应用类型"),
                    APPId = table.Column<long>(type: "bigint", nullable: false, comment: "应用Id"),
                    AreaId = table.Column<long>(type: "bigint", nullable: false, comment: "区域Id"),
                    ServerId = table.Column<long>(type: "bigint", nullable: false, comment: "服务Id")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Basic_Sys_DDBs", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Basic_Sys_Labe",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    IsValid = table.Column<bool>(type: "bit", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(max)", nullable: false, comment: "标签")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Basic_Sys_Labe", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Basic_Sys_OrganizationCategory",
                columns: table => new
                {
                    IsValid = table.Column<bool>(type: "bit", nullable: false),
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ParentId = table.Column<long>(type: "bigint", nullable: false, comment: "父级"),
                    Name = table.Column<string>(type: "nvarchar(max)", nullable: false, comment: "名称"),
                    Description = table.Column<string>(type: "nvarchar(max)", nullable: false, comment: "简介")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Basic_Sys_OrganizationCategory", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Basic_Sys_OrganizationCategory_Basic_Sys_OrganizationCategory_ParentId",
                        column: x => x.ParentId,
                        principalTable: "Basic_Sys_OrganizationCategory",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.NoAction);
                });

            migrationBuilder.CreateTable(
                name: "Basic_Sys_Passport",
                columns: table => new
                {
                    Id = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    IsValid = table.Column<bool>(type: "bit", nullable: false),
                    Password = table.Column<string>(type: "nvarchar(max)", nullable: false, comment: "登录密码哈希值"),
                    PasswordSalt = table.Column<string>(type: "nvarchar(max)", nullable: false, comment: "密码盐值"),
                    CreateTime = table.Column<DateTime>(type: "datetime2", nullable: false, comment: "创建时间"),
                    UpdateTime = table.Column<DateTime>(type: "datetime2", nullable: false, comment: "更新时间")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Basic_Sys_Passport", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Basic_Sys_PassportFlag",
                columns: table => new
                {
                    IsValid = table.Column<bool>(type: "bit", nullable: false),
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    IsCreating = table.Column<bool>(type: "bit", nullable: false, comment: "是否正在生成中"),
                    LastTime = table.Column<DateTime>(type: "datetime2", nullable: false, comment: "最后一次生成时间"),
                    Total = table.Column<long>(type: "bigint", nullable: false, comment: "生成总数")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Basic_Sys_PassportFlag", x => x.Id);
                },
                comment: "生成通行证开关表");

            migrationBuilder.CreateTable(
                name: "Basic_Sys_PassportIds",
                columns: table => new
                {
                    Id = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    IsValid = table.Column<bool>(type: "bit", nullable: false),
                    CreatingTime = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ApplyTime = table.Column<DateTime>(type: "datetime2", nullable: true, comment: "应用时间")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Basic_Sys_PassportIds", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Basic_Sys_Region",
                columns: table => new
                {
                    IsValid = table.Column<bool>(type: "bit", nullable: false),
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Name = table.Column<string>(type: "nvarchar(max)", nullable: false, comment: "区域名称"),
                    ShortName = table.Column<string>(type: "nvarchar(max)", nullable: false, comment: "区域简称"),
                    Status = table.Column<int>(type: "int", nullable: false, comment: "状态"),
                    ParentId = table.Column<int>(type: "int", nullable: false, comment: "上级区域编号"),
                    Level = table.Column<int>(type: "int", nullable: false, comment: "区域行政等级")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Basic_Sys_Region", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Basic_Sys_Region_Basic_Sys_Region_ParentId",
                        column: x => x.ParentId,
                        principalTable: "Basic_Sys_Region",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.NoAction);
                });

            migrationBuilder.CreateTable(
                name: "Basic_Sys_User",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    IsValid = table.Column<bool>(type: "bit", nullable: false),
                    PassportType = table.Column<int>(type: "int", nullable: false, comment: "当前用户通行证类型"),
                    Avatar = table.Column<string>(type: "nvarchar(max)", nullable: true, comment: "用户头像"),
                    Description = table.Column<string>(type: "nvarchar(max)", nullable: true, comment: "用户简介"),
                    Gender = table.Column<int>(type: "int", nullable: true),
                    PassportId = table.Column<string>(type: "nvarchar(60)", maxLength: 60, nullable: false),
                    AppId = table.Column<long>(type: "bigint", nullable: false),
                    AppType = table.Column<int>(type: "int", nullable: false),
                    OrganizationId = table.Column<long>(type: "bigint", nullable: true),
                    Name = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true, comment: "真实姓名"),
                    NickName = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true, comment: "昵称"),
                    IdCard = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true, comment: "身份Id号"),
                    Birthday = table.Column<DateTime>(type: "datetime2", nullable: true),
                    Constellation = table.Column<int>(type: "int", nullable: true),
                    Occupation = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Phone = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true, comment: "电话"),
                    Email = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true, comment: "邮箱"),
                    RegionPath = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Address = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true, comment: "地址"),
                    CreateDate = table.Column<DateTime>(type: "datetime2", nullable: true, comment: "创建时间"),
                    LastLoginDate = table.Column<DateTime>(type: "datetime2", nullable: true, comment: "最后登录时间"),
                    FrozenDate = table.Column<DateTime>(type: "datetime2", nullable: true, comment: "冻结时间"),
                    Status = table.Column<int>(type: "int", nullable: false),
                    LoginNumber = table.Column<long>(type: "bigint", nullable: false, comment: "登录次数"),
                    IP = table.Column<string>(type: "nvarchar(max)", nullable: true, comment: "使用地址")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Basic_Sys_User", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Basics_Sys_Role",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    IsValid = table.Column<bool>(type: "bit", nullable: false),
                    AdminId = table.Column<long>(type: "bigint", nullable: false, comment: "系统用户Id"),
                    RoleName = table.Column<string>(type: "nvarchar(max)", nullable: false, comment: "角色名称"),
                    Description = table.Column<string>(type: "nvarchar(max)", nullable: false, comment: "简述")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Basics_Sys_Role", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Basics_Sys_SysManager",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    IsValid = table.Column<bool>(type: "bit", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(max)", nullable: false, comment: "用户名"),
                    Password = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false, comment: "密码"),
                    RoleId = table.Column<long>(type: "bigint", nullable: false, comment: "角色")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Basics_Sys_SysManager", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Basic_Sys_Organization",
                columns: table => new
                {
                    IsValid = table.Column<bool>(type: "bit", nullable: false),
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ParentId = table.Column<long>(type: "bigint", nullable: false, comment: "父级"),
                    CategoryId = table.Column<long>(type: "bigint", nullable: false, comment: "分类Id"),
                    Name = table.Column<string>(type: "nvarchar(max)", nullable: false, comment: "名称"),
                    Description = table.Column<string>(type: "nvarchar(max)", nullable: false, comment: "简介"),
                    OrganizationType = table.Column<int>(type: "int", nullable: false, comment: "组织机构类型")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Basic_Sys_Organization", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Basic_Sys_Organization_Basic_Sys_OrganizationCategory_CategoryId",
                        column: x => x.CategoryId,
                        principalTable: "Basic_Sys_OrganizationCategory",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_Basic_Sys_Organization_Basic_Sys_Organization_ParentId",
                        column: x => x.ParentId,
                        principalTable: "Basic_Sys_Organization",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.NoAction);
                });

            migrationBuilder.CreateTable(
                name: "Basic_Sys_MemberLabe",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    IsValid = table.Column<bool>(type: "bit", nullable: false),
                    PassportId = table.Column<string>(type: "nvarchar(450)", nullable: false, comment: "通行证"),
                    LabeId = table.Column<long>(type: "bigint", nullable: false, comment: "标签Id")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Basic_Sys_MemberLabe", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Basic_Sys_MemberLabe_Basic_Sys_Labe_LabeId",
                        column: x => x.LabeId,
                        principalTable: "Basic_Sys_Labe",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_Basic_Sys_MemberLabe_Basic_Sys_Passport_PassportId",
                        column: x => x.PassportId,
                        principalTable: "Basic_Sys_Passport",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Basics_Sys_RolePrivilege",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    IsValid = table.Column<bool>(type: "bit", nullable: false),
                    Privilege = table.Column<long>(type: "bigint", nullable: false, comment: "权限值"),
                    RoleId = table.Column<long>(type: "bigint", nullable: false, comment: "角色Id")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Basics_Sys_RolePrivilege", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Basics_Sys_RolePrivilege_Basics_Sys_Role_RoleId",
                        column: x => x.RoleId,
                        principalTable: "Basics_Sys_Role",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Basic_Sys_MemberLabe_LabeId",
                table: "Basic_Sys_MemberLabe",
                column: "LabeId");

            migrationBuilder.CreateIndex(
                name: "IX_Basic_Sys_MemberLabe_PassportId",
                table: "Basic_Sys_MemberLabe",
                column: "PassportId");

            migrationBuilder.CreateIndex(
                name: "IX_Basic_Sys_Organization_CategoryId",
                table: "Basic_Sys_Organization",
                column: "CategoryId");

            migrationBuilder.CreateIndex(
                name: "IX_Basic_Sys_Organization_ParentId",
                table: "Basic_Sys_Organization",
                column: "ParentId");

            migrationBuilder.CreateIndex(
                name: "IX_Basic_Sys_OrganizationCategory_ParentId",
                table: "Basic_Sys_OrganizationCategory",
                column: "ParentId");

            migrationBuilder.CreateIndex(
                name: "IX_Basic_Sys_Region_ParentId",
                table: "Basic_Sys_Region",
                column: "ParentId");

            migrationBuilder.CreateIndex(
                name: "IX_Basics_Sys_RolePrivilege_RoleId",
                table: "Basics_Sys_RolePrivilege",
                column: "RoleId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Basic_Sys_Applications");

            migrationBuilder.DropTable(
                name: "Basic_Sys_DDBs");

            migrationBuilder.DropTable(
                name: "Basic_Sys_MemberLabe");

            migrationBuilder.DropTable(
                name: "Basic_Sys_Organization");

            migrationBuilder.DropTable(
                name: "Basic_Sys_PassportFlag");

            migrationBuilder.DropTable(
                name: "Basic_Sys_PassportIds");

            migrationBuilder.DropTable(
                name: "Basic_Sys_Region");

            migrationBuilder.DropTable(
                name: "Basic_Sys_User");

            migrationBuilder.DropTable(
                name: "Basics_Sys_RolePrivilege");

            migrationBuilder.DropTable(
                name: "Basics_Sys_SysManager");

            migrationBuilder.DropTable(
                name: "Basic_Sys_Labe");

            migrationBuilder.DropTable(
                name: "Basic_Sys_Passport");

            migrationBuilder.DropTable(
                name: "Basic_Sys_OrganizationCategory");

            migrationBuilder.DropTable(
                name: "Basics_Sys_Role");
        }
    }
}
