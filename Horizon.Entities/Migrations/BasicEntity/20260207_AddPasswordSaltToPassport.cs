using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Horizon.Entities.Migrations.BasicEntity
{
    /// <summary>
    /// 添加密码盐值字段以支持安全密码存储
    /// Migration to add PasswordSalt column for secure password storage
    /// </summary>
    public partial class AddPasswordSaltToPassport : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // 添加 PasswordSalt 列
            migrationBuilder.AddColumn<string>(
                name: "PasswordSalt",
                table: "Basic_Sys_Passport",
                type: "nvarchar(max)",
                nullable: true,
                comment: "密码盐值");

            // 注意：现有的明文密码需要手动迁移
            // 可以使用以下 SQL 更新现有记录（需要管理员手动执行）：
            // UPDATE Basic_Sys_Passport SET PasswordSalt = '' WHERE PasswordSalt IS NULL;
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // 删除 PasswordSalt 列
            migrationBuilder.DropColumn(
                name: "PasswordSalt",
                table: "Basic_Sys_Passport");
        }
    }
}
