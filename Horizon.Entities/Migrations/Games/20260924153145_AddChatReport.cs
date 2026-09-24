using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Horizon.Entities.Migrations.Games
{
    /// <inheritdoc />
    public partial class AddChatReport : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_User_Id",
                table: "Game_HunduShijie_User");

            migrationBuilder.AlterTable(
                name: "Game_HunduShijie_Zone",
                comment: "游戏分区信息表",
                oldComment: "��Ϸ������Ϣ��");

            migrationBuilder.AlterTable(
                name: "Game_HunduShijie_Server",
                comment: "游戏服务器信息表",
                oldComment: "��Ϸ��������Ϣ��");

            migrationBuilder.AlterColumn<string>(
                name: "zone_name",
                table: "Game_HunduShijie_Zone",
                type: "varchar(255)",
                nullable: false,
                comment: "分区名称",
                oldClrType: typeof(string),
                oldType: "varchar(255)",
                oldComment: "��������");

            migrationBuilder.AlterColumn<DateTime>(
                name: "updated_at",
                table: "Game_HunduShijie_Zone",
                type: "datetime2",
                nullable: false,
                comment: "更新时间",
                oldClrType: typeof(DateTime),
                oldType: "datetime2",
                oldComment: "����ʱ��");

            migrationBuilder.AlterColumn<int>(
                name: "game_id",
                table: "Game_HunduShijie_Zone",
                type: "int",
                nullable: false,
                comment: "游戏Id",
                oldClrType: typeof(int),
                oldType: "int",
                oldComment: "��ϷId");

            migrationBuilder.AlterColumn<string>(
                name: "description",
                table: "Game_HunduShijie_Zone",
                type: "varchar(500)",
                nullable: false,
                comment: "分区描述",
                oldClrType: typeof(string),
                oldType: "varchar(500)",
                oldComment: "��������");

            migrationBuilder.AlterColumn<DateTime>(
                name: "created_at",
                table: "Game_HunduShijie_Zone",
                type: "datetime2",
                nullable: false,
                comment: "创建时间",
                oldClrType: typeof(DateTime),
                oldType: "datetime2",
                oldComment: "����ʱ��");

            migrationBuilder.AlterColumn<int>(
                name: "zone_id",
                table: "Game_HunduShijie_Server",
                type: "int",
                nullable: false,
                comment: "分区ID",
                oldClrType: typeof(int),
                oldType: "int",
                oldComment: "����ID");

            migrationBuilder.AlterColumn<DateTime>(
                name: "updated_at",
                table: "Game_HunduShijie_Server",
                type: "datetime2",
                nullable: false,
                comment: "更新时间",
                oldClrType: typeof(DateTime),
                oldType: "datetime2",
                oldComment: "����ʱ��");

            migrationBuilder.AlterColumn<string>(
                name: "status",
                table: "Game_HunduShijie_Server",
                type: "varchar(50)",
                nullable: false,
                comment: "服务器状态",
                oldClrType: typeof(string),
                oldType: "varchar(50)",
                oldComment: "������״̬");

            migrationBuilder.AlterColumn<string>(
                name: "server_name",
                table: "Game_HunduShijie_Server",
                type: "varchar(255)",
                nullable: false,
                comment: "服务器名称",
                oldClrType: typeof(string),
                oldType: "varchar(255)",
                oldComment: "����������");

            migrationBuilder.AlterColumn<int>(
                name: "port",
                table: "Game_HunduShijie_Server",
                type: "int",
                nullable: false,
                comment: "端口",
                oldClrType: typeof(int),
                oldType: "int",
                oldComment: "�˿�");

            migrationBuilder.AlterColumn<int>(
                name: "max_players",
                table: "Game_HunduShijie_Server",
                type: "int",
                nullable: false,
                comment: "最大玩家数",
                oldClrType: typeof(int),
                oldType: "int",
                oldComment: "��������");

            migrationBuilder.AlterColumn<string>(
                name: "ip_address",
                table: "Game_HunduShijie_Server",
                type: "varchar(255)",
                nullable: false,
                comment: "IP地址",
                oldClrType: typeof(string),
                oldType: "varchar(255)",
                oldComment: "IP��ַ");

            migrationBuilder.AlterColumn<int>(
                name: "current_players",
                table: "Game_HunduShijie_Server",
                type: "int",
                nullable: false,
                comment: "当前玩家数",
                oldClrType: typeof(int),
                oldType: "int",
                oldComment: "��ǰ�����");

            migrationBuilder.AlterColumn<DateTime>(
                name: "created_at",
                table: "Game_HunduShijie_Server",
                type: "datetime2",
                nullable: false,
                comment: "创建时间",
                oldClrType: typeof(DateTime),
                oldType: "datetime2",
                oldComment: "����ʱ��");

            migrationBuilder.CreateTable(
                name: "chunk_state",
                columns: table => new
                {
                    morton_key = table.Column<long>(type: "bigint", nullable: false, comment: "Morton 编码键（chunk 空间坐标）"),
                    morton_bucket = table.Column<byte>(type: "tinyint", nullable: false, comment: "Morton 哈希桶（morton_key % N，分区列）"),
                    version = table.Column<long>(type: "bigint", nullable: false, comment: "状态版本号，乐观并发"),
                    updated_at = table.Column<DateTime>(type: "datetime2(3)", nullable: false, defaultValueSql: "SYSUTCDATETIME()", comment: "最近写入 UTC 时间"),
                    state_json = table.Column<string>(type: "nvarchar(max)", nullable: false, comment: "JSON 状态副本（诊断用）"),
                    state_bin = table.Column<byte[]>(type: "varbinary(max)", nullable: false, comment: "MemoryPack 二进制状态副本（快速批量加载）")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_chunk_state", x => new { x.morton_bucket, x.morton_key });
                },
                comment: "世界 Chunk 权威状态快照表（按 Morton 键哈希分区）");

            migrationBuilder.CreateTable(
                name: "diff_log",
                columns: table => new
                {
                    seq = table.Column<long>(type: "bigint", nullable: false, comment: "全局单调序列号（IDENTITY）")
                        .Annotation("SqlServer:Identity", "1, 1"),
                    morton_key = table.Column<long>(type: "bigint", nullable: false, comment: "Morton 编码键（chunk 空间坐标）"),
                    morton_bucket = table.Column<byte>(type: "tinyint", nullable: false, comment: "Morton 哈希桶（分区列）"),
                    op_kind = table.Column<byte>(type: "tinyint", nullable: false, comment: "VoxelOp 操作类型（VoxelOpKind）"),
                    payload = table.Column<byte[]>(type: "varbinary(max)", nullable: false, comment: "MemoryPack 序列化的 VoxelOp payload"),
                    created_at = table.Column<DateTime>(type: "datetime2(3)", nullable: false, defaultValueSql: "SYSUTCDATETIME()", comment: "写入 UTC 时间（供过期清理使用）")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_diff_log", x => new { x.morton_bucket, x.seq });
                },
                comment: "世界 Diff 追加日志表（IDENTITY seq，按 Morton 键哈希分区）");

            migrationBuilder.CreateTable(
                name: "Game_HunduShijie_ChatReport",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false, comment: "记录ID")
                        .Annotation("SqlServer:Identity", "1, 1"),
                    message_id = table.Column<long>(type: "bigint", nullable: false, comment: "被举报的消息ID"),
                    channel = table.Column<int>(type: "int", nullable: false, comment: "消息所在频道"),
                    reporter_id = table.Column<long>(type: "bigint", nullable: false, comment: "举报者角色ID"),
                    target_id = table.Column<long>(type: "bigint", nullable: false, comment: "被举报者角色ID"),
                    reason = table.Column<string>(type: "nvarchar(200)", nullable: false, comment: "举报原因"),
                    status = table.Column<int>(type: "int", nullable: false, comment: "处理状态 0-待处理 1-已处理 2-已驳回"),
                    create_time = table.Column<DateTime>(type: "datetime", nullable: false, comment: "举报时间"),
                    IsValid = table.Column<bool>(type: "bit", nullable: false),
                    GameId = table.Column<int>(type: "int", nullable: false),
                    AreaId = table.Column<int>(type: "int", nullable: false),
                    ServerId = table.Column<int>(type: "int", nullable: false),
                    GameUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Game_HunduShijie_ChatReport", x => x.id);
                },
                comment: "聊天举报记录表");

            migrationBuilder.CreateIndex(
                name: "IX_User_AccountName",
                table: "Game_HunduShijie_User",
                column: "account_name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_chunk_state_morton_key",
                table: "chunk_state",
                column: "morton_key");

            migrationBuilder.CreateIndex(
                name: "IX_diff_log_created_at",
                table: "diff_log",
                column: "created_at");

            migrationBuilder.CreateIndex(
                name: "IX_diff_log_morton_key",
                table: "diff_log",
                columns: new[] { "morton_key", "seq" });

            migrationBuilder.CreateIndex(
                name: "IX_diff_log_seq",
                table: "diff_log",
                column: "seq");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "chunk_state");

            migrationBuilder.DropTable(
                name: "diff_log");

            migrationBuilder.DropTable(
                name: "Game_HunduShijie_ChatReport");

            migrationBuilder.DropIndex(
                name: "IX_User_AccountName",
                table: "Game_HunduShijie_User");

            migrationBuilder.AlterTable(
                name: "Game_HunduShijie_Zone",
                comment: "��Ϸ������Ϣ��",
                oldComment: "游戏分区信息表");

            migrationBuilder.AlterTable(
                name: "Game_HunduShijie_Server",
                comment: "��Ϸ��������Ϣ��",
                oldComment: "游戏服务器信息表");

            migrationBuilder.AlterColumn<string>(
                name: "zone_name",
                table: "Game_HunduShijie_Zone",
                type: "varchar(255)",
                nullable: false,
                comment: "��������",
                oldClrType: typeof(string),
                oldType: "varchar(255)",
                oldComment: "分区名称");

            migrationBuilder.AlterColumn<DateTime>(
                name: "updated_at",
                table: "Game_HunduShijie_Zone",
                type: "datetime2",
                nullable: false,
                comment: "����ʱ��",
                oldClrType: typeof(DateTime),
                oldType: "datetime2",
                oldComment: "更新时间");

            migrationBuilder.AlterColumn<int>(
                name: "game_id",
                table: "Game_HunduShijie_Zone",
                type: "int",
                nullable: false,
                comment: "��ϷId",
                oldClrType: typeof(int),
                oldType: "int",
                oldComment: "游戏Id");

            migrationBuilder.AlterColumn<string>(
                name: "description",
                table: "Game_HunduShijie_Zone",
                type: "varchar(500)",
                nullable: false,
                comment: "��������",
                oldClrType: typeof(string),
                oldType: "varchar(500)",
                oldComment: "分区描述");

            migrationBuilder.AlterColumn<DateTime>(
                name: "created_at",
                table: "Game_HunduShijie_Zone",
                type: "datetime2",
                nullable: false,
                comment: "����ʱ��",
                oldClrType: typeof(DateTime),
                oldType: "datetime2",
                oldComment: "创建时间");

            migrationBuilder.AlterColumn<int>(
                name: "zone_id",
                table: "Game_HunduShijie_Server",
                type: "int",
                nullable: false,
                comment: "����ID",
                oldClrType: typeof(int),
                oldType: "int",
                oldComment: "分区ID");

            migrationBuilder.AlterColumn<DateTime>(
                name: "updated_at",
                table: "Game_HunduShijie_Server",
                type: "datetime2",
                nullable: false,
                comment: "����ʱ��",
                oldClrType: typeof(DateTime),
                oldType: "datetime2",
                oldComment: "更新时间");

            migrationBuilder.AlterColumn<string>(
                name: "status",
                table: "Game_HunduShijie_Server",
                type: "varchar(50)",
                nullable: false,
                comment: "������״̬",
                oldClrType: typeof(string),
                oldType: "varchar(50)",
                oldComment: "服务器状态");

            migrationBuilder.AlterColumn<string>(
                name: "server_name",
                table: "Game_HunduShijie_Server",
                type: "varchar(255)",
                nullable: false,
                comment: "����������",
                oldClrType: typeof(string),
                oldType: "varchar(255)",
                oldComment: "服务器名称");

            migrationBuilder.AlterColumn<int>(
                name: "port",
                table: "Game_HunduShijie_Server",
                type: "int",
                nullable: false,
                comment: "�˿�",
                oldClrType: typeof(int),
                oldType: "int",
                oldComment: "端口");

            migrationBuilder.AlterColumn<int>(
                name: "max_players",
                table: "Game_HunduShijie_Server",
                type: "int",
                nullable: false,
                comment: "��������",
                oldClrType: typeof(int),
                oldType: "int",
                oldComment: "最大玩家数");

            migrationBuilder.AlterColumn<string>(
                name: "ip_address",
                table: "Game_HunduShijie_Server",
                type: "varchar(255)",
                nullable: false,
                comment: "IP��ַ",
                oldClrType: typeof(string),
                oldType: "varchar(255)",
                oldComment: "IP地址");

            migrationBuilder.AlterColumn<int>(
                name: "current_players",
                table: "Game_HunduShijie_Server",
                type: "int",
                nullable: false,
                comment: "��ǰ�����",
                oldClrType: typeof(int),
                oldType: "int",
                oldComment: "当前玩家数");

            migrationBuilder.AlterColumn<DateTime>(
                name: "created_at",
                table: "Game_HunduShijie_Server",
                type: "datetime2",
                nullable: false,
                comment: "����ʱ��",
                oldClrType: typeof(DateTime),
                oldType: "datetime2",
                oldComment: "创建时间");

            migrationBuilder.CreateIndex(
                name: "IX_User_Id",
                table: "Game_HunduShijie_User",
                column: "user_id",
                unique: true);
        }
    }
}
