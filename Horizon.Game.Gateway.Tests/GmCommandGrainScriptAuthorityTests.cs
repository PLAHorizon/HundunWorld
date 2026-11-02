using Horizon.Core.Abstract;
using Horizon.Entities;
using Horizon.Game.Core.Security;
using Horizon.Game.Core.Sim.Server;
using Horizon.Game.Message.Network;
using Horizon.Game.Message.Security;
using Horizon.Model.GameModel;
using Horizon.Orleans.Grains.World;
using Horizon.Orleans.Interface.World;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Orleans.Runtime;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Threading.Tasks;
using Xunit;

namespace Horizon.Game.Gateway.Tests;

/// <summary>
/// <see cref="GmCommandGrain"/> 脚本授权面的直接测试（这条链路唯一的真值源）。
/// <para>
/// 其余测试都把这个 Grain 换成替身：<see cref="WorldScriptCommandHandlerTests"/> 里它是 mock，
/// <see cref="ScriptedSpawnEndToEndTests"/> 里被换成裁决核本身。于是 Grain 自己的规则
/// ——谁能授予、授予要不要先落权威表、审计活不活得过去去激活——一直没有人管。
/// 本文件补这段。
/// </para>
/// </summary>
public class GmCommandGrainScriptAuthorityTests
{
    private const long AdminId = 9001;
    private const long OperatorId = 7001;
    private const long Shard = 3;

    private static readonly byte[] Key = Enumerable.Repeat((byte)0x5A, 32).ToArray();
    private static readonly string KeyB64 = Convert.ToBase64String(Key);

    private sealed class Harness
    {
        public GmScriptAuthorityState State = new();
        public Mock<IPersistentState<GmScriptAuthorityState>> Authority = null!;
        public Mock<IDataContext<GameEntityContext, GmScriptGrantEntity, long>> GrantStore = null!;
        public List<GmScriptGrantEntity> TableRows = new();
        public GmCommandGrain Grain = null!;

        /// <summary>模拟 Redis 里那条持久记录：换一个 Grain 实例仍读到同一份 State。</summary>
        public GmScriptAuthorityState Persisted => State;
    }

    private static Harness Build(
        ScriptedSpawnOptions? options = null,
        bool withGrantStore = true,
        (float X, float Y, float Z, float Yaw)? position = null)
    {
        var harness = new Harness
        {
            Authority = new Mock<IPersistentState<GmScriptAuthorityState>>(),
            GrantStore = new Mock<IDataContext<GameEntityContext, GmScriptGrantEntity, long>>(),
        };

        harness.Authority.SetupGet(a => a.State).Returns(() => harness.State);
        harness.Authority.SetupGet(a => a.RecordExists).Returns(() => harness.State.Operators.Count > 0 || harness.State.AuditLog.Count > 0);
        harness.Authority.Setup(a => a.ReadStateAsync()).Returns(Task.CompletedTask);
        harness.Authority.Setup(a => a.WriteStateAsync()).Returns(Task.CompletedTask);

        harness.TableRows = new List<GmScriptGrantEntity>();
        harness.GrantStore
            .Setup(g => g.QueryFirstOrDefaultAsync(It.IsAny<Expression<Func<GmScriptGrantEntity, bool>>>(), It.IsAny<bool>()))
            .ReturnsAsync((Expression<Func<GmScriptGrantEntity, bool>> predicate, bool _) =>
                harness.TableRows.AsQueryable().Where(predicate).FirstOrDefault());
        harness.GrantStore
            .Setup(g => g.AddAsync(It.IsAny<GmScriptGrantEntity>()))
            .ReturnsAsync((GmScriptGrantEntity entity) =>
            {
                entity.Id = harness.TableRows.Count + 1;
                harness.TableRows.Add(entity);
                return entity;
            });
        harness.GrantStore
            .Setup(g => g.UpdateAsync(It.IsAny<GmScriptGrantEntity>(), It.IsAny<long>()))
            .ReturnsAsync(true);

        var positionStore = new Mock<ICharacterPositionStore>();
        positionStore
            .Setup(p => p.GetPositionAsync(It.IsAny<long>()))
            .ReturnsAsync(position);

        var grain = new GmCommandGrain(
            NullLogger<GmCommandGrain>.Instance,
            harness.Authority.Object,
            withGrantStore ? harness.GrantStore.Object : null,
            positionStore.Object,
            options ?? new ScriptedSpawnOptions { Enabled = true, SigningKeyBase64 = KeyB64 });

        harness.Grain = grain;
        return harness;
    }

    private static GmScriptTicket Ticket(ScriptOp op = ScriptOp.Spawn, ScriptedKind kind = ScriptedKind.Monster) => new()
    {
        OperatorCharacterId = OperatorId,
        RequestId = 1,
        Op = op,
        Kind = kind,
        TemplateKey = "墨麒麟",
        Count = 1,
        OperatorShardId = Shard,
    };

    /// <summary>直接往热层+权威表里放一个持有指定权限位的调用者（跳过授予路径）。</summary>
    private static void SeedCaller(Harness harness, long callerId, ScriptPermissionFlags flags)
    {
        harness.State.Operators[callerId] = new GmScriptGrantState
        {
            Flags = (uint)flags,
            GrantedAtUtc = DateTime.UtcNow.AddDays(-1),
            ExpiresAtUtc = DateTime.MaxValue,
            GrantedBy = callerId,
        };
    }

    [Fact]
    public async Task 无GmOps的调用者授予被拒_且不留下授权()
    {
        var harness = Build();
        SeedCaller(harness, AdminId, ScriptPermissionFlags.SpawnMonster);

        var result = await harness.Grain.GrantScriptAsync(
            OperatorId, ScriptPermissionFlags.SpawnMonster, TimeSpan.FromHours(1), AdminId, "测试");

        Assert.False(result.Success);
        Assert.DoesNotContain(OperatorId, harness.State.Operators.Keys);
        // 被拒的授予同样要留痕（这才是"谁能造怪"事后可核查的前提）。
        Assert.Contains(harness.State.AuditLog, e => e.OperationType == "GrantScript" && e.Details.Contains("拒绝"));
    }

    [Fact]
    public async Task 引导管理员授予成功_授权与审计都落到持久层()
    {
        var options = new ScriptedSpawnOptions
        {
            Enabled = true,
            SigningKeyBase64 = KeyB64,
            BootstrapAdminIds = new[] { AdminId },
        };
        var harness = Build(options);

        var result = await harness.Grain.GrantScriptAsync(
            OperatorId, ScriptPermissionFlags.WorldDirector, TimeSpan.FromHours(1), AdminId, "首批 GM");

        Assert.True(result.Success);
        Assert.True(harness.State.Operators.ContainsKey(OperatorId));
        // 先表后热层：权威表里必须真有这行，否则 Redis 过期/清档后授权就凭空消失。
        var row = Assert.Single(harness.TableRows);
        Assert.Equal(OperatorId, row.OperatorCharacterId);
        Assert.Equal((long)ScriptPermissionFlags.WorldDirector, row.Flags);
        Assert.Null(row.RevokedAtUtc);
        Assert.Contains(harness.State.AuditLog, e => e.OperationType == "GrantScript");
    }

    [Fact]
    public async Task 不能凭空造出自己没有的权限位()
    {
        var harness = Build();
        // 调用者持 GmOps（可以管理授权），但没有世界编排位。
        SeedCaller(harness, AdminId, ScriptPermissionFlags.SpawnMonster | ScriptPermissionFlags.GmOps);

        var result = await harness.Grain.GrantScriptAsync(
            OperatorId, ScriptPermissionFlags.WorldDirector, TimeSpan.FromHours(1), AdminId, "越权尝试");

        Assert.False(result.Success);
        Assert.Empty(harness.TableRows);
        Assert.DoesNotContain(OperatorId, harness.State.Operators.Keys);
    }

    [Fact]
    public async Task 权威表写失败时不留热层假象()
    {
        var options = new ScriptedSpawnOptions
        {
            Enabled = true,
            SigningKeyBase64 = KeyB64,
            BootstrapAdminIds = new[] { AdminId },
        };
        var harness = Build(options);
        harness.GrantStore
            .Setup(g => g.AddAsync(It.IsAny<GmScriptGrantEntity>()))
            .ThrowsAsync(new InvalidOperationException("SQLServer 不可达"));

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            harness.Grain.GrantScriptAsync(
                OperatorId, ScriptPermissionFlags.WorldDirector, TimeSpan.FromHours(1), AdminId, "表写失败"));

        // "热层已生效但重启后消失"是最坏的一种成功假象，必须没有发生。
        Assert.DoesNotContain(OperatorId, harness.State.Operators.Keys);
    }

    [Fact]
    public async Task 审计活得过去Grain去激活()
    {
        // Orleans 会按需去激活空闲 Grain：进程内字段随之清零，/audit 会返回空表，
        // 看起来像"没人做过"。审计必须与授权同层持久化。
        var options = new ScriptedSpawnOptions
        {
            Enabled = true,
            SigningKeyBase64 = KeyB64,
            BootstrapAdminIds = new[] { AdminId },
        };
        var harness = Build(options);
        await harness.Grain.GrantScriptAsync(
            OperatorId, ScriptPermissionFlags.WorldDirector, TimeSpan.FromHours(1), AdminId, "首批 GM");
        Assert.NotEmpty(harness.State.AuditLog);

        // 换一个 Grain 实例（同一条 Redis 记录）再查。
        var rebuilt = Build(options);
        foreach (var entry in harness.Persisted.AuditLog) rebuilt.State.AuditLog.Add(entry);
        foreach (var pair in harness.Persisted.Operators) rebuilt.State.Operators[pair.Key] = pair.Value;

        var entries = await rebuilt.Grain.QueryScriptAuditAsync(OperatorId, 20);
        Assert.Contains(entries, e => e.OperationType == "GrantScript");
    }

    [Fact]
    public async Task 脚本审计只回脚本面动作()
    {
        var harness = Build();
        SeedCaller(harness, AdminId, ScriptPermissionFlags.GameMaster);

        // 一次与脚本无关的 GM 操作（封禁）不应混进 /audit 的脚本文案里。
        await harness.Grain.BanCharacterAsync(OperatorId, "测试封禁", null, AdminId);

        var entries = await harness.Grain.QueryScriptAuditAsync(OperatorId, 20);
        Assert.DoesNotContain(entries, e => e.OperationType == "BanCharacter");
    }

    [Fact]
    public async Task 裁决消耗预算后拒绝第二次_且签发令牌可用同密钥验签()
    {
        var options = new ScriptedSpawnOptions
        {
            Enabled = true,
            SigningKeyBase64 = KeyB64,
            CooldownSeconds = 3600,   // 冷却拉长，第二次必然被冷却拦下
        };
        var harness = Build(options, position: (10f, 20f, 0f, 0f));
        SeedCaller(harness, OperatorId, ScriptPermissionFlags.WorldDirector);

        var first = await harness.Grain.TryConsumeScriptAsync(Ticket());
        Assert.True(first.Allowed, first.Message);
        var token = first.Token!;
        Assert.Equal(Shard, token.TargetShardId);
        Assert.True(ScriptGrantTokenSigner.Validate(token, Key, DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(), out var reason), reason);

        // 预算是"被裁决消耗的状态"，必须已回写热层（否则冷却与配额永远不生效）。
        Assert.Equal(1, harness.State.Operators[OperatorId].QuotaWindowUsed);

        var second = await harness.Grain.TryConsumeScriptAsync(Ticket());
        Assert.False(second.Allowed);
        Assert.Equal(ScriptRejectReason.Cooldown, second.Reason);
    }

    [Fact]
    public async Task 缺密钥时一律拒绝_不签发无法验证的令牌()
    {
        var options = new ScriptedSpawnOptions { Enabled = true, SigningKeyBase64 = "" };
        var harness = Build(options, position: (10f, 20f, 0f, 0f));
        SeedCaller(harness, OperatorId, ScriptPermissionFlags.WorldDirector);

        var verdict = await harness.Grain.TryConsumeScriptAsync(Ticket());

        Assert.False(verdict.Allowed);
        Assert.Null(verdict.Token);
        // fail-closed 还要求"没有偷偷扣预算"。
        Assert.Equal(0, harness.State.Operators[OperatorId].QuotaWindowUsed);
    }

    [Fact]
    public async Task 无权威位置时生成类被拒_查询面仍可用()
    {
        var harness = Build(); // position = null
        SeedCaller(harness, OperatorId, ScriptPermissionFlags.WorldDirector);

        var spawn = await harness.Grain.TryConsumeScriptAsync(Ticket());
        Assert.False(spawn.Allowed);
        Assert.Contains("权威位置", spawn.Message, StringComparison.Ordinal);

        var query = await harness.Grain.TryConsumeScriptAsync(Ticket(op: ScriptOp.Query, kind: ScriptedKind.None));
        Assert.True(query.Allowed, query.Message);

        var snapshot = await harness.Grain.GetScriptPermissionsAsync(OperatorId);
        Assert.NotNull(snapshot);
        Assert.Equal((uint)ScriptPermissionFlags.WorldDirector, snapshot!.Flags);
    }

    [Fact]
    public async Task 回收授权同时清两层并留下审计()
    {
        var options = new ScriptedSpawnOptions
        {
            Enabled = true,
            SigningKeyBase64 = KeyB64,
            BootstrapAdminIds = new[] { AdminId },
        };
        var harness = Build(options);
        await harness.Grain.GrantScriptAsync(
            OperatorId, ScriptPermissionFlags.WorldDirector, TimeSpan.FromHours(1), AdminId, "首批 GM");

        var revoked = await harness.Grain.RevokeScriptAsync(OperatorId, AdminId, "卸任");

        Assert.True(revoked.Success);
        Assert.DoesNotContain(OperatorId, harness.State.Operators.Keys);
        // 权威表不是删行而是标记撤销：历史授权要能被追回。
        var row = Assert.Single(harness.TableRows);
        Assert.NotNull(row.RevokedAtUtc);
        Assert.Equal(0L, row.Flags);
    }

    [Fact]
    public async Task 没有授权记录时查询返回空而不是伪造快照()
    {
        var harness = Build();
        Assert.Null(await harness.Grain.GetScriptPermissionsAsync(OperatorId));
    }
}
