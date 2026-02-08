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
    /// 公会状态
    /// </summary>
    [MemoryPackable(SerializeLayout.Explicit)]
    [GenerateSerializer]
    [Serializable]
    public partial class GuildState
    {
        /// <summary>
        /// 公会名称
        /// </summary>
        [MemoryPackOrder(0)]
        [Id(0)]
        public string GuildName { get; set; } = "";

        /// <summary>
        /// 创建者/帮主ID
        /// </summary>
        [MemoryPackOrder(1)]
        [Id(1)]
        public Guid LeaderId { get; set; }

        /// <summary>
        /// 公会是否已创建
        /// </summary>
        [MemoryPackOrder(2)]
        [Id(2)]
        public bool IsCreated { get; set; }

        /// <summary>
        /// 公会等级
        /// </summary>
        [MemoryPackOrder(3)]
        [Id(3)]
        public int Level { get; set; } = 1;

        /// <summary>
        /// 最大成员数
        /// </summary>
        [MemoryPackOrder(4)]
        [Id(4)]
        public int MaxMembers { get; set; } = 50;

        /// <summary>
        /// 公会宣言
        /// </summary>
        [MemoryPackOrder(5)]
        [Id(5)]
        public string Declaration { get; set; } = "";

        /// <summary>
        /// 成员列表（成员ID -> 成员信息）
        /// </summary>
        [MemoryPackOrder(6)]
        [Id(6)]
        public Dictionary<Guid, GuildMemberState> Members { get; set; } = new();

        /// <summary>
        /// 入会申请列表（申请ID -> 申请信息）
        /// </summary>
        [MemoryPackOrder(7)]
        [Id(7)]
        public Dictionary<Guid, GuildApplication> Applications { get; set; } = new();

        /// <summary>
        /// 公会资源
        /// </summary>
        [MemoryPackOrder(8)]
        [Id(8)]
        public Dictionary<string, int> Resources { get; set; } = new();

        /// <summary>
        /// 创建时间
        /// </summary>
        [MemoryPackOrder(9)]
        [Id(9)]
        public long CreateTime { get; set; }
    }

    /// <summary>
    /// 公会成员状态
    /// </summary>
    [MemoryPackable(SerializeLayout.Explicit)]
    [GenerateSerializer]
    [Serializable]
    public partial class GuildMemberState
    {
        [MemoryPackOrder(0)]
        [Id(0)]
        public Guid MemberId { get; set; }

        /// <summary>
        /// 职位: 0=帮主, 1=副帮主, 2=长老, 3=精英, 4=普通成员
        /// </summary>
        [MemoryPackOrder(1)]
        [Id(1)]
        public int Position { get; set; } = 4;

        [MemoryPackOrder(2)]
        [Id(2)]
        public int Contribution { get; set; }

        [MemoryPackOrder(3)]
        [Id(3)]
        public long JoinTime { get; set; }
    }

    /// <summary>
    /// 公会入会申请
    /// </summary>
    [MemoryPackable(SerializeLayout.Explicit)]
    [GenerateSerializer]
    [Serializable]
    public partial class GuildApplication
    {
        [MemoryPackOrder(0)]
        [Id(0)]
        public Guid ApplicationId { get; set; }

        [MemoryPackOrder(1)]
        [Id(1)]
        public Guid PlayerId { get; set; }

        [MemoryPackOrder(2)]
        [Id(2)]
        public string Message { get; set; } = "";

        [MemoryPackOrder(3)]
        [Id(3)]
        public long Timestamp { get; set; }
    }

    /// <summary>
    /// 公会系统Grain实现
    /// </summary>
    public class GuildGrain : Grain, IGuildGrain
    {
        private readonly ILogger<GuildGrain> _logger;
        private readonly IPersistentState<GuildState> _guildState;

        public GuildGrain(
            ILogger<GuildGrain> logger,
            [PersistentState("guild", "GameStore")] IPersistentState<GuildState> guildState)
        {
            _logger = logger;
            _guildState = guildState;
        }

        public override async Task OnActivateAsync(CancellationToken cancellationToken)
        {
            _logger.LogInformation("GuildGrain {GrainKey} activating.", this.GetPrimaryKey());

            if (_guildState.State.Members == null)
                _guildState.State.Members = new Dictionary<Guid, GuildMemberState>();

            if (_guildState.State.Applications == null)
                _guildState.State.Applications = new Dictionary<Guid, GuildApplication>();

            if (_guildState.State.Resources == null)
                _guildState.State.Resources = new Dictionary<string, int>();

            await base.OnActivateAsync(cancellationToken);
        }

        public async Task<bool> CreateGuildAsync(string guildName, Guid creatorId)
        {
            try
            {
                var state = _guildState.State;

                if (state.IsCreated)
                {
                    _logger.LogWarning("公会已存在: GuildName={GuildName}", state.GuildName);
                    return false;
                }

                if (string.IsNullOrWhiteSpace(guildName))
                {
                    _logger.LogWarning("公会名称无效");
                    return false;
                }

                state.GuildName = guildName;
                state.LeaderId = creatorId;
                state.IsCreated = true;
                state.Level = 1;
                state.CreateTime = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

                // 添加创建者为帮主
                var leaderMember = new GuildMemberState
                {
                    MemberId = creatorId,
                    Position = 0, // 帮主
                    Contribution = 0,
                    JoinTime = state.CreateTime
                };
                state.Members[creatorId] = leaderMember;

                await _guildState.WriteStateAsync();

                _logger.LogInformation("创建公会成功: GuildName={GuildName}, CreatorId={CreatorId}",
                    guildName, creatorId);
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "创建公会失败: GuildName={GuildName}", guildName);
                throw;
            }
        }

        public async Task<bool> ApplyToJoinAsync(Guid playerId, string message)
        {
            try
            {
                var state = _guildState.State;

                if (!state.IsCreated)
                {
                    _logger.LogWarning("公会不存在");
                    return false;
                }

                if (state.Members.ContainsKey(playerId))
                {
                    _logger.LogWarning("玩家已是公会成员: PlayerId={PlayerId}", playerId);
                    return false;
                }

                if (state.Members.Count >= state.MaxMembers)
                {
                    _logger.LogWarning("公会成员已满: Count={Count}, Max={Max}",
                        state.Members.Count, state.MaxMembers);
                    return false;
                }

                // 检查是否已有待处理申请
                if (state.Applications.Values.Any(a => a.PlayerId == playerId))
                {
                    _logger.LogWarning("已有待处理的入会申请: PlayerId={PlayerId}", playerId);
                    return false;
                }

                var application = new GuildApplication
                {
                    ApplicationId = Guid.NewGuid(),
                    PlayerId = playerId,
                    Message = message ?? "",
                    Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
                };

                state.Applications[application.ApplicationId] = application;
                await _guildState.WriteStateAsync();

                _logger.LogInformation("提交入会申请: PlayerId={PlayerId}", playerId);
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "提交入会申请失败: PlayerId={PlayerId}", playerId);
                throw;
            }
        }

        public async Task<bool> ProcessApplicationAsync(Guid applicationId, Guid approverId, bool approve)
        {
            try
            {
                var state = _guildState.State;

                if (!state.IsCreated)
                {
                    _logger.LogWarning("公会不存在");
                    return false;
                }

                // 检查审批者权限（帮主或副帮主）
                if (!state.Members.TryGetValue(approverId, out var approver) || approver.Position > 1)
                {
                    _logger.LogWarning("无权审批入会申请: ApproverId={ApproverId}", approverId);
                    return false;
                }

                if (!state.Applications.TryGetValue(applicationId, out var application))
                {
                    _logger.LogWarning("入会申请不存在: ApplicationId={ApplicationId}", applicationId);
                    return false;
                }

                state.Applications.Remove(applicationId);

                if (approve)
                {
                    if (state.Members.Count >= state.MaxMembers)
                    {
                        _logger.LogWarning("公会成员已满，无法批准申请");
                        await _guildState.WriteStateAsync();
                        return false;
                    }

                    var newMember = new GuildMemberState
                    {
                        MemberId = application.PlayerId,
                        Position = 4, // 普通成员
                        Contribution = 0,
                        JoinTime = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
                    };
                    state.Members[application.PlayerId] = newMember;

                    _logger.LogInformation("批准入会申请: PlayerId={PlayerId}", application.PlayerId);
                }
                else
                {
                    _logger.LogInformation("拒绝入会申请: ApplicationId={ApplicationId}", applicationId);
                }

                await _guildState.WriteStateAsync();
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "处理入会申请失败: ApplicationId={ApplicationId}", applicationId);
                throw;
            }
        }

        public async Task<bool> KickMemberAsync(Guid operatorId, Guid targetId)
        {
            try
            {
                var state = _guildState.State;

                if (!state.IsCreated)
                {
                    _logger.LogWarning("公会不存在");
                    return false;
                }

                // 不能踢出自己
                if (operatorId == targetId)
                {
                    _logger.LogWarning("不能踢出自己");
                    return false;
                }

                // 检查操作者权限
                if (!state.Members.TryGetValue(operatorId, out var operatorMember))
                {
                    _logger.LogWarning("操作者不是公会成员: OperatorId={OperatorId}", operatorId);
                    return false;
                }

                if (!state.Members.TryGetValue(targetId, out var targetMember))
                {
                    _logger.LogWarning("目标不是公会成员: TargetId={TargetId}", targetId);
                    return false;
                }

                // 操作者职位必须高于目标（数值越小职位越高）
                if (operatorMember.Position >= targetMember.Position)
                {
                    _logger.LogWarning("权限不足，无法踢出目标: OperatorPosition={OpPos}, TargetPosition={TgtPos}",
                        operatorMember.Position, targetMember.Position);
                    return false;
                }

                state.Members.Remove(targetId);
                await _guildState.WriteStateAsync();

                _logger.LogInformation("踢出公会成员: TargetId={TargetId}", targetId);
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "踢出公会成员失败: TargetId={TargetId}", targetId);
                throw;
            }
        }

        public async Task<bool> LeaveGuildAsync(Guid memberId)
        {
            try
            {
                var state = _guildState.State;

                if (!state.Members.TryGetValue(memberId, out var member))
                {
                    _logger.LogWarning("不是公会成员: MemberId={MemberId}", memberId);
                    return false;
                }

                // 帮主不能直接离开，需要先转让
                if (member.Position == 0)
                {
                    _logger.LogWarning("帮主不能直接离开公会: MemberId={MemberId}", memberId);
                    return false;
                }

                state.Members.Remove(memberId);
                await _guildState.WriteStateAsync();

                _logger.LogInformation("离开公会: MemberId={MemberId}", memberId);
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "离开公会失败: MemberId={MemberId}", memberId);
                throw;
            }
        }

        public Task<GuildInfo> GetGuildInfoAsync()
        {
            try
            {
                var state = _guildState.State;

                var info = new GuildInfo
                {
                    GuildName = state.GuildName,
                    LeaderId = GuidToUInt64(state.LeaderId),
                    Level = state.Level,
                    MemberCount = state.Members.Count,
                    MaxMembers = state.MaxMembers,
                    Declaration = state.Declaration,
                    Resources = new Dictionary<string, int>(state.Resources)
                };

                return Task.FromResult(info);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "获取公会信息失败");
                throw;
            }
        }

        public Task<List<GuildMember>> GetMembersAsync()
        {
            try
            {
                var state = _guildState.State;

                var members = state.Members.Values.Select(m => new GuildMember
                {
                    CharacterId = GuidToUInt64(m.MemberId),
                    GuildPosition = GetPositionName(m.Position),
                    Contribution = m.Contribution
                }).ToList();

                return Task.FromResult(members);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "获取公会成员列表失败");
                throw;
            }
        }

        public async Task<bool> AppointPositionAsync(Guid operatorId, Guid targetId, int position)
        {
            try
            {
                var state = _guildState.State;

                if (!state.IsCreated)
                {
                    _logger.LogWarning("公会不存在");
                    return false;
                }

                // 只有帮主才能任命职位
                if (!state.Members.TryGetValue(operatorId, out var operatorMember) || operatorMember.Position != 0)
                {
                    _logger.LogWarning("无权任命职位: OperatorId={OperatorId}", operatorId);
                    return false;
                }

                if (!state.Members.TryGetValue(targetId, out var targetMember))
                {
                    _logger.LogWarning("目标不是公会成员: TargetId={TargetId}", targetId);
                    return false;
                }

                if (position < 0 || position > 4)
                {
                    _logger.LogWarning("无效职位: Position={Position}", position);
                    return false;
                }

                // 不能将他人任命为帮主（需要通过转让）
                if (position == 0)
                {
                    _logger.LogWarning("帮主职位需要通过转让操作");
                    return false;
                }

                targetMember.Position = position;
                await _guildState.WriteStateAsync();

                _logger.LogInformation("任命职位: TargetId={TargetId}, Position={Position}",
                    targetId, GetPositionName(position));
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "任命职位失败: TargetId={TargetId}", targetId);
                throw;
            }
        }

        private static string GetPositionName(int position) => position switch
        {
            0 => "帮主",
            1 => "副帮主",
            2 => "长老",
            3 => "精英",
            4 => "成员",
            _ => "成员"
        };

        /// <summary>
        /// 将Guid确定性转换为ulong（使用前8个字节）
        /// </summary>
        private static ulong GuidToUInt64(Guid guid)
        {
            return BitConverter.ToUInt64(guid.ToByteArray(), 0);
        }
    }
}
