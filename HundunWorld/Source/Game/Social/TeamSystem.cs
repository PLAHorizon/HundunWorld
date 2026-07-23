using FlaxEngine;
using System;
using System.Collections.Generic;
using System.Linq;

namespace HundunWorld.Game.Social
{
    /// <summary>
    /// 队伍成员信息
    /// </summary>
    [Serializable]
    public class TeamMemberInfo
    {
        /// <summary>玩家ID</summary>
        public ulong PlayerId { get; set; }

        /// <summary>玩家名称</summary>
        public string PlayerName { get; set; } = "";

        /// <summary>等级</summary>
        public int Level { get; set; }

        /// <summary>职业</summary>
        public string ClassName { get; set; } = "";

        /// <summary>当前生命值</summary>
        public float Health { get; set; }

        /// <summary>最大生命值</summary>
        public float MaxHealth { get; set; }

        /// <summary>当前内力</summary>
        public float Energy { get; set; }

        /// <summary>最大内力</summary>
        public float MaxEnergy { get; set; }

        /// <summary>位置</summary>
        public Vector3 Position { get; set; }

        /// <summary>是否在线</summary>
        public bool IsOnline { get; set; } = true;

        /// <summary>是否死亡</summary>
        public bool IsDead { get; set; }

        /// <summary>生命值比例</summary>
        public float HealthPercent => MaxHealth > 0 ? Health / MaxHealth : 0f;

        /// <summary>内力比例</summary>
        public float EnergyPercent => MaxEnergy > 0 ? Energy / MaxEnergy : 0f;
    }

    /// <summary>
    /// 队伍邀请
    /// </summary>
    public class TeamInvite
    {
        public ulong InviterId { get; set; }
        public string InviterName { get; set; } = "";
        public ulong TeamId { get; set; }
        public float ExpireTime { get; set; }
    }

    /// <summary>
    /// 队伍系统 - 管理组队、队伍同步、队伍UI数据。
    /// 产品级特性：
    /// - 创建/解散/加入/离开队伍
    /// - 队长转让/踢人
    /// - 队伍成员状态实时同步（血量/位置/状态）
    /// - 队伍邀请/申请机制
    /// - 队伍标记/集合点
    /// - 经验/掉落分配规则
    /// </summary>
    public class TeamSystem
    {
        private static TeamSystem _instance;
        public static TeamSystem Instance => _instance ??= new TeamSystem();

        // ===== 队伍数据 =====
        private ulong _teamId = 0;
        private ulong _leaderId = 0;
        private List<TeamMemberInfo> _members = new List<TeamMemberInfo>();
        private List<TeamInvite> _pendingInvites = new List<TeamInvite>();

        // ===== 配置 =====
        public const int MaxTeamSize = 5;
        public const float InviteExpireDuration = 30f;

        // ===== 分配规则 =====
        public enum LootRule { FreeForAll, RoundRobin, Leader, NeedBeforeGreed }
        public LootRule CurrentLootRule { get; set; } = LootRule.NeedBeforeGreed;

        // ===== 事件 =====
        /// <summary>队伍创建</summary>
        public event Action<ulong> OnTeamCreated;

        /// <summary>队伍解散</summary>
        public event Action OnTeamDisbanded;

        /// <summary>成员加入</summary>
        public event Action<TeamMemberInfo> OnMemberJoined;

        /// <summary>成员离开</summary>
        public event Action<ulong, string> OnMemberLeft;

        /// <summary>成员状态更新</summary>
        public event Action<TeamMemberInfo> OnMemberUpdated;

        /// <summary>队长变更</summary>
        public event Action<ulong> OnLeaderChanged;

        /// <summary>收到邀请</summary>
        public event Action<TeamInvite> OnInviteReceived;

        /// <summary>队伍标记设置</summary>
        public event Action<Vector3, string> OnRallyPointSet;

        // ===== 属性 =====
        public bool IsInTeam => _teamId > 0;
        public bool IsLeader => _leaderId == GetLocalPlayerId();
        public int MemberCount => _members.Count;
        public bool IsFull => _members.Count >= MaxTeamSize;
        public ulong TeamId => _teamId;
        public ulong LeaderId => _leaderId;

        // ===== 队伍操作 =====

        /// <summary>创建队伍</summary>
        public void CreateTeam()
        {
            if (IsInTeam)
            {
                Debug.LogWarning("[TeamSystem] 已在队伍中");
                return;
            }

            _teamId = GenerateTeamId();
            _leaderId = GetLocalPlayerId();
            _members.Clear();

            // 添加自己
            var self = CreateLocalMemberInfo();
            _members.Add(self);

            OnTeamCreated?.Invoke(_teamId);
            Debug.Log($"[TeamSystem] 队伍创建成功, ID: {_teamId}");

            // TODO: 通知服务器
        }

        /// <summary>解散队伍（仅队长）</summary>
        public void DisbandTeam()
        {
            if (!IsInTeam || !IsLeader) return;

            _teamId = 0;
            _leaderId = 0;
            _members.Clear();

            OnTeamDisbanded?.Invoke();
            Debug.Log("[TeamSystem] 队伍已解散");

            // TODO: 通知服务器
        }

        /// <summary>邀请玩家</summary>
        public void InvitePlayer(ulong playerId, string playerName)
        {
            if (!IsInTeam) CreateTeam();
            if (!IsLeader && !IsInTeam) return;
            if (IsFull)
            {
                Debug.LogWarning("[TeamSystem] 队伍已满");
                return;
            }

            // TODO: 发送邀请到服务器 -> 目标玩家
            Debug.Log($"[TeamSystem] 邀请玩家: {playerName} ({playerId})");
        }

        /// <summary>接受邀请</summary>
        public void AcceptInvite(ulong teamId)
        {
            _pendingInvites.RemoveAll(i => i.TeamId == teamId);

            // TODO: 通知服务器加入队伍
            Debug.Log($"[TeamSystem] 接受邀请, 加入队伍: {teamId}");
        }

        /// <summary>拒绝邀请</summary>
        public void DeclineInvite(ulong teamId)
        {
            _pendingInvites.RemoveAll(i => i.TeamId == teamId);
        }

        /// <summary>离开队伍</summary>
        public void LeaveTeam()
        {
            if (!IsInTeam) return;

            var localId = GetLocalPlayerId();
            _members.RemoveAll(m => m.PlayerId == localId);

            // 如果是队长离开，转让队长
            if (_leaderId == localId && _members.Count > 0)
            {
                _leaderId = _members[0].PlayerId;
                OnLeaderChanged?.Invoke(_leaderId);
            }

            if (_members.Count <= 1)
            {
                _teamId = 0;
                _leaderId = 0;
                _members.Clear();
                OnTeamDisbanded?.Invoke();
            }

            OnMemberLeft?.Invoke(localId, GetLocalPlayerName());
            // TODO: 通知服务器
        }

        /// <summary>踢出成员（仅队长）</summary>
        public void KickMember(ulong playerId)
        {
            if (!IsLeader) return;

            var member = _members.FirstOrDefault(m => m.PlayerId == playerId);
            if (member == null) return;

            _members.Remove(member);
            OnMemberLeft?.Invoke(playerId, member.PlayerName);
            // TODO: 通知服务器
        }

        /// <summary>转让队长</summary>
        public void TransferLeader(ulong newLeaderId)
        {
            if (!IsLeader) return;
            if (!_members.Any(m => m.PlayerId == newLeaderId)) return;

            _leaderId = newLeaderId;
            OnLeaderChanged?.Invoke(newLeaderId);
            // TODO: 通知服务器
        }

        // ===== 状态同步 =====

        /// <summary>更新本地成员状态（每帧/定时调用）</summary>
        public void UpdateLocalMemberState(float health, float maxHealth, float energy, float maxEnergy, Vector3 position, bool isDead)
        {
            if (!IsInTeam) return;

            var localId = GetLocalPlayerId();
            var member = _members.FirstOrDefault(m => m.PlayerId == localId);
            if (member == null) return;

            member.Health = health;
            member.MaxHealth = maxHealth;
            member.Energy = energy;
            member.MaxEnergy = maxEnergy;
            member.Position = position;
            member.IsDead = isDead;

            // TODO: 定时同步到服务器（0.5秒间隔）
        }

        /// <summary>接收远程成员状态更新</summary>
        public void ReceiveMemberUpdate(TeamMemberInfo info)
        {
            if (info == null) return;

            var existing = _members.FirstOrDefault(m => m.PlayerId == info.PlayerId);
            if (existing != null)
            {
                existing.Health = info.Health;
                existing.MaxHealth = info.MaxHealth;
                existing.Energy = info.Energy;
                existing.MaxEnergy = info.MaxEnergy;
                existing.Position = info.Position;
                existing.IsDead = info.IsDead;
                existing.IsOnline = info.IsOnline;
                OnMemberUpdated?.Invoke(existing);
            }
        }

        /// <summary>接收成员加入通知</summary>
        public void ReceiveMemberJoined(TeamMemberInfo member)
        {
            if (member == null) return;
            if (_members.Any(m => m.PlayerId == member.PlayerId)) return;

            _members.Add(member);
            OnMemberJoined?.Invoke(member);
        }

        // ===== 队伍功能 =====

        /// <summary>设置集合点</summary>
        public void SetRallyPoint(Vector3 position, string description = "")
        {
            if (!IsLeader) return;
            OnRallyPointSet?.Invoke(position, description);
            // TODO: 同步到服务器
        }

        /// <summary>获取成员列表</summary>
        public List<TeamMemberInfo> GetMembers() => new List<TeamMemberInfo>(_members);

        /// <summary>获取指定成员</summary>
        public TeamMemberInfo GetMember(ulong playerId) => _members.FirstOrDefault(m => m.PlayerId == playerId);

        /// <summary>获取队长信息</summary>
        public TeamMemberInfo GetLeader() => _members.FirstOrDefault(m => m.PlayerId == _leaderId);

        /// <summary>是否全部存活</summary>
        public bool AllAlive() => _members.All(m => !m.IsDead);

        /// <summary>获取队伍平均等级</summary>
        public float GetAverageLevel()
        {
            if (_members.Count == 0) return 0;
            return (float)_members.Average(m => m.Level);
        }

        // ===== 内部方法 =====

        private ulong GenerateTeamId() => (ulong)DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

        private ulong GetLocalPlayerId() => 1; // TODO: 从本地玩家获取

        private string GetLocalPlayerName() => "玩家"; // TODO: 从本地玩家获取

        private TeamMemberInfo CreateLocalMemberInfo() => new TeamMemberInfo
        {
            PlayerId = GetLocalPlayerId(),
            PlayerName = GetLocalPlayerName(),
            Level = 1,
            ClassName = "剑客",
            Health = 100,
            MaxHealth = 100,
            Energy = 100,
            MaxEnergy = 100,
            IsOnline = true
        };
    }
}
