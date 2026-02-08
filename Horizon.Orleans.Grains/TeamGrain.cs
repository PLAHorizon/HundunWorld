using Horizon.Game.Message.Network;
using Horizon.Orleans.Interface;
using Microsoft.Extensions.Logging;
using Orleans;
using Orleans.Runtime;
using MemoryPack;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Horizon.Orleans.Grains
{
    /// <summary>
    /// 队伍状态
    /// </summary>
    [MemoryPackable(SerializeLayout.Explicit)]
    [GenerateSerializer]
    [Serializable]
    public partial class TeamState
    {
        /// <summary>
        /// 队伍名称
        /// </summary>
        [MemoryPackOrder(0)]
        [Id(0)]
        public string TeamName { get; set; } = "";

        /// <summary>
        /// 队长ID
        /// </summary>
        [MemoryPackOrder(1)]
        [Id(1)]
        public Guid LeaderId { get; set; }

        /// <summary>
        /// 队伍是否已创建
        /// </summary>
        [MemoryPackOrder(2)]
        [Id(2)]
        public bool IsCreated { get; set; }

        /// <summary>
        /// 队伍目标描述
        /// </summary>
        [MemoryPackOrder(3)]
        [Id(3)]
        public string TeamGoal { get; set; } = "";

        /// <summary>
        /// 最大成员数
        /// </summary>
        [MemoryPackOrder(4)]
        [Id(4)]
        public int MaxMembers { get; set; } = 5;

        /// <summary>
        /// 成员列表（成员ID -> 成员信息）
        /// </summary>
        [MemoryPackOrder(5)]
        [Id(5)]
        public Dictionary<Guid, TeamMemberState> Members { get; set; } = new();

        /// <summary>
        /// 创建时间
        /// </summary>
        [MemoryPackOrder(6)]
        [Id(6)]
        public long CreateTime { get; set; }
    }

    /// <summary>
    /// 队伍成员状态
    /// </summary>
    [MemoryPackable(SerializeLayout.Explicit)]
    [GenerateSerializer]
    [Serializable]
    public partial class TeamMemberState
    {
        [MemoryPackOrder(0)]
        [Id(0)]
        public Guid MemberId { get; set; }

        [MemoryPackOrder(1)]
        [Id(1)]
        public bool IsLeader { get; set; }

        [MemoryPackOrder(2)]
        [Id(2)]
        public long JoinTime { get; set; }
    }

    /// <summary>
    /// 组队系统Grain实现
    /// </summary>
    public class TeamGrain : Grain, ITeamGrain
    {
        private const int MaxTeamNameLength = 32;
        private const int MaxTeamGoalLength = 100;

        private readonly ILogger<TeamGrain> _logger;
        private readonly IPersistentState<TeamState> _teamState;

        public TeamGrain(
            ILogger<TeamGrain> logger,
            [PersistentState("team", "GameStore")] IPersistentState<TeamState> teamState)
        {
            _logger = logger;
            _teamState = teamState;
        }

        public override async Task OnActivateAsync(CancellationToken cancellationToken)
        {
            _logger.LogInformation("TeamGrain {GrainKey} activating.", this.GetPrimaryKey());

            if (_teamState.State.Members == null)
                _teamState.State.Members = new Dictionary<Guid, TeamMemberState>();

            await base.OnActivateAsync(cancellationToken);
        }

        public async Task<bool> CreateTeamAsync(Guid leaderId, string teamName, string teamGoal)
        {
            try
            {
                var state = _teamState.State;

                if (state.IsCreated)
                {
                    _logger.LogWarning("队伍已存在: TeamName={TeamName}", state.TeamName);
                    return false;
                }

                if (string.IsNullOrWhiteSpace(teamName))
                {
                    _logger.LogWarning("队伍名称无效");
                    return false;
                }

                if (teamName.Length > MaxTeamNameLength)
                {
                    _logger.LogWarning("队伍名称过长: Length={Length}", teamName.Length);
                    return false;
                }

                var goal = teamGoal ?? "";
                if (goal.Length > MaxTeamGoalLength)
                {
                    goal = goal[..MaxTeamGoalLength];
                }

                state.TeamName = teamName.Trim();
                state.TeamGoal = goal;
                state.LeaderId = leaderId;
                state.IsCreated = true;
                state.CreateTime = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

                var leaderMember = new TeamMemberState
                {
                    MemberId = leaderId,
                    IsLeader = true,
                    JoinTime = state.CreateTime
                };
                state.Members[leaderId] = leaderMember;

                await _teamState.WriteStateAsync();

                _logger.LogInformation("创建队伍成功: TeamName={TeamName}, LeaderId={LeaderId}",
                    teamName, leaderId);
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "创建队伍失败: TeamName={TeamName}", teamName);
                throw;
            }
        }

        public async Task<bool> JoinTeamAsync(Guid playerId)
        {
            try
            {
                var state = _teamState.State;

                if (!state.IsCreated)
                {
                    _logger.LogWarning("队伍不存在");
                    return false;
                }

                if (state.Members.ContainsKey(playerId))
                {
                    _logger.LogWarning("玩家已是队伍成员: PlayerId={PlayerId}", playerId);
                    return false;
                }

                if (state.Members.Count >= state.MaxMembers)
                {
                    _logger.LogWarning("队伍成员已满: Count={Count}, Max={Max}",
                        state.Members.Count, state.MaxMembers);
                    return false;
                }

                var newMember = new TeamMemberState
                {
                    MemberId = playerId,
                    IsLeader = false,
                    JoinTime = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
                };
                state.Members[playerId] = newMember;

                await _teamState.WriteStateAsync();

                _logger.LogInformation("加入队伍: PlayerId={PlayerId}", playerId);
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "加入队伍失败: PlayerId={PlayerId}", playerId);
                throw;
            }
        }

        public async Task<bool> LeaveTeamAsync(Guid memberId)
        {
            try
            {
                var state = _teamState.State;

                if (!state.Members.ContainsKey(memberId))
                {
                    _logger.LogWarning("不是队伍成员: MemberId={MemberId}", memberId);
                    return false;
                }

                // 队长不能直接离开，需要先转让或解散
                if (state.LeaderId == memberId)
                {
                    _logger.LogWarning("队长不能直接离开队伍: MemberId={MemberId}", memberId);
                    return false;
                }

                state.Members.Remove(memberId);
                await _teamState.WriteStateAsync();

                _logger.LogInformation("离开队伍: MemberId={MemberId}", memberId);
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "离开队伍失败: MemberId={MemberId}", memberId);
                throw;
            }
        }

        public async Task<bool> KickMemberAsync(Guid operatorId, Guid targetId)
        {
            try
            {
                var state = _teamState.State;

                if (!state.IsCreated)
                {
                    _logger.LogWarning("队伍不存在");
                    return false;
                }

                // 只有队长能踢人
                if (state.LeaderId != operatorId)
                {
                    _logger.LogWarning("无权踢出队员: OperatorId={OperatorId}", operatorId);
                    return false;
                }

                // 不能踢出自己
                if (operatorId == targetId)
                {
                    _logger.LogWarning("不能踢出自己");
                    return false;
                }

                if (!state.Members.ContainsKey(targetId))
                {
                    _logger.LogWarning("目标不是队伍成员: TargetId={TargetId}", targetId);
                    return false;
                }

                state.Members.Remove(targetId);
                await _teamState.WriteStateAsync();

                _logger.LogInformation("踢出队员: TargetId={TargetId}", targetId);
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "踢出队员失败: TargetId={TargetId}", targetId);
                throw;
            }
        }

        public async Task<bool> TransferLeaderAsync(Guid currentLeaderId, Guid newLeaderId)
        {
            try
            {
                var state = _teamState.State;

                if (!state.IsCreated)
                {
                    _logger.LogWarning("队伍不存在");
                    return false;
                }

                if (state.LeaderId != currentLeaderId)
                {
                    _logger.LogWarning("无权转移队长: CurrentLeaderId={CurrentLeaderId}", currentLeaderId);
                    return false;
                }

                if (currentLeaderId == newLeaderId)
                {
                    _logger.LogWarning("不能转移给自己");
                    return false;
                }

                if (!state.Members.TryGetValue(newLeaderId, out var newLeader))
                {
                    _logger.LogWarning("目标不是队伍成员: NewLeaderId={NewLeaderId}", newLeaderId);
                    return false;
                }

                // 更新队长
                if (state.Members.TryGetValue(currentLeaderId, out var oldLeader))
                {
                    oldLeader.IsLeader = false;
                }
                newLeader.IsLeader = true;
                state.LeaderId = newLeaderId;

                await _teamState.WriteStateAsync();

                _logger.LogInformation("转移队长: NewLeaderId={NewLeaderId}", newLeaderId);
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "转移队长失败: NewLeaderId={NewLeaderId}", newLeaderId);
                throw;
            }
        }

        public Task<TeamInfo> GetTeamInfoAsync()
        {
            try
            {
                var state = _teamState.State;

                var info = new TeamInfo
                {
                    TeamId = GuidToUInt64(this.GetPrimaryKey()),
                    LeaderId = GuidToUInt64(state.LeaderId),
                    TeamName = state.TeamName,
                    TeamGoal = state.TeamGoal,
                    Members = state.Members.Values.Select(m => new TeamMemberInfo
                    {
                        CharacterId = GuidToUInt64(m.MemberId),
                        IsLeader = m.IsLeader,
                        IsOnline = true // TODO: 后续通过查询玩家Grain获取真实在线状态
                    }).ToList()
                };

                return Task.FromResult(info);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "获取队伍信息失败");
                throw;
            }
        }

        public Task<List<TeamMemberInfo>> GetMembersAsync()
        {
            try
            {
                var state = _teamState.State;

                var members = state.Members.Values.Select(m => new TeamMemberInfo
                {
                    CharacterId = GuidToUInt64(m.MemberId),
                    IsLeader = m.IsLeader,
                    IsOnline = true // TODO: 后续通过查询玩家Grain获取真实在线状态
                }).ToList();

                return Task.FromResult(members);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "获取队伍成员列表失败");
                throw;
            }
        }

        public async Task<bool> DisbandTeamAsync(Guid leaderId)
        {
            try
            {
                var state = _teamState.State;

                if (!state.IsCreated)
                {
                    _logger.LogWarning("队伍不存在");
                    return false;
                }

                if (state.LeaderId != leaderId)
                {
                    _logger.LogWarning("无权解散队伍: LeaderId={LeaderId}", leaderId);
                    return false;
                }

                state.IsCreated = false;
                state.Members.Clear();
                state.TeamName = "";
                state.TeamGoal = "";
                state.LeaderId = Guid.Empty;

                await _teamState.WriteStateAsync();

                _logger.LogInformation("解散队伍成功");
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "解散队伍失败");
                throw;
            }
        }

        /// <summary>
        /// 将Guid确定性转换为ulong（使用前8个字节）
        /// </summary>
        private static ulong GuidToUInt64(Guid guid)
        {
            return BitConverter.ToUInt64(guid.ToByteArray(), 0);
        }
    }
}
