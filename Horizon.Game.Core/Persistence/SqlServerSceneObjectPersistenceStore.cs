using System;
using System.Collections.Generic;
using System.Data;
using System.Data.SqlClient;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Horizon.Game.Message.Sync.Components;

namespace Horizon.Game.Core.Persistence;

/// <summary>
/// Task C.5.2：基于原始 ADO.NET 的 SqlServer 场景对象状态持久化实现。
/// <para>
/// 使用 <c>System.Data.SqlClient</c>（已在 Horizon.Game.Core 引用）访问 SqlServer，
/// 通过 <c>MERGE</c> 语句实现 upsert 语义。连接字符串由 DI 注入，默认从 <c>DatabaseOptions:Game</c> 读取。
/// </para>
/// <para>
/// 表结构参见 <c>scripts/sql/004_scene_object_state.sql</c>。
/// </para>
/// </summary>
public sealed class SqlServerSceneObjectPersistenceStore : ISceneObjectPersistenceStore
{
    private readonly string _connectionString;
    private readonly ILogger<SqlServerSceneObjectPersistenceStore>? _logger;

    /// <summary>
    /// 构造函数。
    /// </summary>
    /// <param name="connectionString">SqlServer 连接字符串（指向 Game 库）。</param>
    /// <param name="logger">日志记录器（可选）。</param>
    public SqlServerSceneObjectPersistenceStore(string connectionString, ILogger<SqlServerSceneObjectPersistenceStore>? logger = null)
    {
        if (string.IsNullOrWhiteSpace(connectionString))
            throw new ArgumentException("连接字符串不能为空。", nameof(connectionString));
        _connectionString = connectionString;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<Dictionary<ulong, SceneObjectStateData>> LoadWorldStateAsync(long shardKey)
    {
        var result = new Dictionary<ulong, SceneObjectStateData>();

        const string sql = @"
SELECT object_id, shard_key, object_type, state_bits, cooldown_end_tick,
       owner_character_id, transform_x, transform_y, transform_z,
       transform_pitch, transform_yaw, transform_roll, updated_at
FROM dbo.scene_object_state
WHERE shard_key = @ShardKey;";

        try
        {
            await using var conn = new SqlConnection(_connectionString);
            await conn.OpenAsync();

            await using var cmd = new SqlCommand(sql, conn);
            cmd.Parameters.Add(new SqlParameter("@ShardKey", SqlDbType.BigInt) { Value = shardKey });

            await using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                var data = new SceneObjectStateData
                {
                    ObjectId = (ulong)reader.GetInt64(0),
                    ShardKey = reader.GetInt64(1),
                    ObjectType = (SceneObjectType)reader.GetByte(2),
                    StateBits = (uint)reader.GetInt32(3),
                    CooldownEndTick = reader.GetInt64(4),
                    OwnerCharacterId = (ulong)reader.GetInt64(5),
                    TransformX = reader.GetFloat(6),
                    TransformY = reader.GetFloat(7),
                    TransformZ = reader.GetFloat(8),
                    TransformPitch = reader.GetFloat(9),
                    TransformYaw = reader.GetFloat(10),
                    TransformRoll = reader.GetFloat(11),
                    UpdatedAt = reader.GetDateTime(12),
                };
                result[data.ObjectId] = data;
            }
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex,
                "加载场景对象状态失败。ShardKey={ShardKey}", shardKey);
        }

        return result;
    }

    /// <inheritdoc />
    public async Task SaveWorldStateAsync(long shardKey, IEnumerable<SceneObjectStateData> states)
    {
        if (states is null) return;

        try
        {
            await using var conn = new SqlConnection(_connectionString);
            await conn.OpenAsync();

            await using var tx = (SqlTransaction)await conn.BeginTransactionAsync();

            try
            {
                foreach (var state in states)
                {
                    await UpsertAsync(conn, tx, state);
                }

                await tx.CommitAsync();
            }
            catch
            {
                await tx.RollbackAsync();
                throw;
            }
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex,
                "批量保存场景对象状态失败。ShardKey={ShardKey}", shardKey);
        }
    }

    /// <inheritdoc />
    public async Task SaveSingleAsync(long shardKey, SceneObjectStateData state)
    {
        if (state is null) return;

        try
        {
            await using var conn = new SqlConnection(_connectionString);
            await conn.OpenAsync();

            await using var tx = (SqlTransaction)await conn.BeginTransactionAsync();

            try
            {
                await UpsertAsync(conn, tx, state);
                await tx.CommitAsync();
            }
            catch
            {
                await tx.RollbackAsync();
                throw;
            }
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex,
                "保存单个场景对象状态失败。ShardKey={ShardKey}, ObjectId={ObjectId}",
                shardKey, state.ObjectId);
        }
    }

    /// <summary>
    /// 执行 upsert（MERGE）单条记录。
    /// 使用 MERGE 语义：存在则更新，不存在则插入。
    /// </summary>
    private static async Task UpsertAsync(SqlConnection conn, SqlTransaction tx, SceneObjectStateData state)
    {
        const string sql = @"
MERGE dbo.scene_object_state AS target
USING (SELECT @ObjectId AS object_id, @ShardKey AS shard_key) AS source
ON (target.object_id = source.object_id AND target.shard_key = source.shard_key)
WHEN MATCHED THEN
    UPDATE SET object_type = @ObjectType,
               state_bits = @StateBits,
               cooldown_end_tick = @CooldownEndTick,
               owner_character_id = @OwnerCharacterId,
               transform_x = @TransformX,
               transform_y = @TransformY,
               transform_z = @TransformZ,
               transform_pitch = @TransformPitch,
               transform_yaw = @TransformYaw,
               transform_roll = @TransformRoll,
               updated_at = @UpdatedAt
WHEN NOT MATCHED THEN
    INSERT (object_id, shard_key, object_type, state_bits, cooldown_end_tick,
            owner_character_id, transform_x, transform_y, transform_z,
            transform_pitch, transform_yaw, transform_roll, updated_at)
    VALUES (@ObjectId, @ShardKey, @ObjectType, @StateBits, @CooldownEndTick,
            @OwnerCharacterId, @TransformX, @TransformY, @TransformZ,
            @TransformPitch, @TransformYaw, @TransformRoll, @UpdatedAt);";

        await using var cmd = new SqlCommand(sql, conn, tx);
        cmd.Parameters.Add(new SqlParameter("@ObjectId", SqlDbType.BigInt) { Value = (long)state.ObjectId });
        cmd.Parameters.Add(new SqlParameter("@ShardKey", SqlDbType.BigInt) { Value = state.ShardKey });
        cmd.Parameters.Add(new SqlParameter("@ObjectType", SqlDbType.TinyInt) { Value = (byte)state.ObjectType });
        cmd.Parameters.Add(new SqlParameter("@StateBits", SqlDbType.Int) { Value = (int)state.StateBits });
        cmd.Parameters.Add(new SqlParameter("@CooldownEndTick", SqlDbType.BigInt) { Value = state.CooldownEndTick });
        cmd.Parameters.Add(new SqlParameter("@OwnerCharacterId", SqlDbType.BigInt) { Value = (long)state.OwnerCharacterId });
        cmd.Parameters.Add(new SqlParameter("@TransformX", SqlDbType.Real) { Value = state.TransformX });
        cmd.Parameters.Add(new SqlParameter("@TransformY", SqlDbType.Real) { Value = state.TransformY });
        cmd.Parameters.Add(new SqlParameter("@TransformZ", SqlDbType.Real) { Value = state.TransformZ });
        cmd.Parameters.Add(new SqlParameter("@TransformPitch", SqlDbType.Real) { Value = state.TransformPitch });
        cmd.Parameters.Add(new SqlParameter("@TransformYaw", SqlDbType.Real) { Value = state.TransformYaw });
        cmd.Parameters.Add(new SqlParameter("@TransformRoll", SqlDbType.Real) { Value = state.TransformRoll });
        cmd.Parameters.Add(new SqlParameter("@UpdatedAt", SqlDbType.DateTime2) { Value = state.UpdatedAt });

        await cmd.ExecuteNonQueryAsync();
    }
}
