using System;
using Arch.Core;
using Horizon.Game.ECS.Arch.Components;
using Horizon.Game.ECS.Arch.Core;
using Horizon.Game.ECS.Arch.Network;
using Horizon.Game.Message.Sync;

namespace Horizon.Game.ECS.Arch.Systems;

/// <summary>
/// P1.4 战斗输入系统：采集玩家攻击/技能/道具输入，打包为 <see cref="CombatActionPacket"/> 放入发送队列。
/// <para>
/// 在 <see cref="SystemGroup.Update"/> 阶段执行，每帧检测本地玩家的战斗输入状态，
/// 若触发攻击/技能/道具使用，则构造 CombatActionPacket 并通过 <see cref="InputSendQueue"/> 发送。
/// </para>
/// </summary>
[ArchSystem(SystemGroup.Update, order: 10)]
public sealed class CombatInputSystem : ArchSystemBase
{
    /// <summary>当前实例（供外部静态访问）。</summary>
    public static CombatInputSystem? Instance { get; private set; }

    /// <summary>本地玩家实体引用。</summary>
    private Entity _localPlayerEntity;
    private bool _hasLocalPlayer;

    /// <summary>攻击输入缓冲（由 Flax 侧 Input 系统写入）。</summary>
    private CombatInputState _pendingInput;

    /// <summary>攻击冷却计时（秒）。</summary>
    private float _attackCooldownTimer;

    /// <summary>基础攻击冷却时间（秒）。</summary>
    private const float BaseAttackCooldown = 0.8f;

    /// <summary>网络发送队列引用。</summary>
    private InputSendQueue? _sendQueue;

    public override void Initialize(World world)
    {
        Instance = this;
        _sendQueue = InputSendQueue.Instance;
    }

    public override void Update(World world, TimeSpan deltaTime)
    {
        if (!_hasLocalPlayer)
        {
            TryFindLocalPlayer(world);
            return;
        }

        // 冷却递减
        if (_attackCooldownTimer > 0)
        {
            _attackCooldownTimer -= (float)deltaTime.TotalSeconds;
        }

        // 无输入或冷却中则跳过
        if (!_pendingInput.HasInput || _attackCooldownTimer > 0)
        {
            _pendingInput.Reset();
            return;
        }

        // 读取本地玩家组件
        if (!world.IsAlive(_localPlayerEntity))
        {
            _hasLocalPlayer = false;
            return;
        }

        ref var identity = ref world.Get<NetworkIdentityComponent>(_localPlayerEntity);
        ref var transform = ref world.Get<PredictedTransformComponent>(_localPlayerEntity);

        // 构造战斗动作包
        var packet = new CombatActionPacket
        {
            AttackerId = identity.EntityId,
            TargetId = _pendingInput.TargetId,
            ActionKind = (CombatActionKind)_pendingInput.ActionKind,
            SkillId = _pendingInput.SkillId,
            ClientTick = 0, // 由 InputSendSystem 统一赋 tick
            AttackerYaw = transform.Yaw,
        };

        // 放入发送队列（复用 InputSendQueue 的通道）
        _sendQueue?.EnqueueCombatAction(packet);

        // 重置输入 + 启动冷却
        _pendingInput.Reset();
        _attackCooldownTimer = BaseAttackCooldown;
    }

    /// <summary>
    /// 由 Flax 侧 Input Action 调用：设置攻击输入。
    /// </summary>
    /// <param name="targetId">目标实体 NetworkId（0=无目标/自身）。</param>
    /// <param name="actionKind">动作类型。</param>
    /// <param name="skillId">技能 ID（0=普攻）。</param>
    public void SetCombatInput(ulong targetId, CombatActionKind actionKind, int skillId = 0)
    {
        _pendingInput = new CombatInputState
        {
            HasInput = true,
            TargetId = targetId,
            ActionKind = (byte)actionKind,
            SkillId = skillId,
        };
    }

    /// <summary>设置本地玩家实体（由 ArchWorldHost 在角色创建后调用）。</summary>
    public void SetLocalPlayer(Entity entity)
    {
        _localPlayerEntity = entity;
        _hasLocalPlayer = true;
    }

    private void TryFindLocalPlayer(World world)
    {
        var query = new QueryDescription().WithAll<PlayerInputComponent, NetworkIdentityComponent>();
        world.Query(query, (Entity entity, ref NetworkIdentityComponent identity) =>
        {
            if (identity.IsLocalPlayer)
            {
                _localPlayerEntity = entity;
                _hasLocalPlayer = true;
            }
        });
    }

    public override void Dispose(World world)
    {
        if (Instance == this) Instance = null;
    }

    /// <summary>内部输入状态结构。</summary>
    private struct CombatInputState
    {
        public bool HasInput;
        public ulong TargetId;
        public byte ActionKind;
        public int SkillId;

        public void Reset()
        {
            HasInput = false;
            TargetId = 0;
            ActionKind = 0;
            SkillId = 0;
        }
    }
}
