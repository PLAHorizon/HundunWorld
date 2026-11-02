using System;
using FlaxEngine;
using Horizon.Game.Message.Network;
using ManagedHundunWorld.Network.Handlers;

namespace HundunWorld.Game.Network
{
    /// <summary>
    /// 组队客户端服务：Create/Join/Leave/Kick/Transfer/Disband 统一走
    /// CreateTeamRequest/Response + TeamOperation。
    /// 网络回调只写缓存与 Revision，页面在 Update 里发现变化才重绘；没有回执不编造队伍。
    /// </summary>
    public sealed class TeamClientService
    {
        public static TeamClientService Instance { get; } = new TeamClientService();

        private readonly object _gate = new object();
        private TeamInfo _team;
        private bool _receivedOnce;
        private int _revision;
        private string _lastNotice = string.Empty;
        private TeamMessageHandler _handler;

        private TeamClientService() { }

        public int Revision { get { lock (_gate) return _revision; } }
        public string LastNotice { get { lock (_gate) return _lastNotice; } }
        public bool HasServerData { get { lock (_gate) return _receivedOnce; } }

        public TeamInfo CurrentTeam
        {
            get { lock (_gate) return _team; }
        }

        public bool HasTeam
        {
            get { lock (_gate) return _team != null && _team.TeamId != 0UL; }
        }

        public void EnsureAttached()
        {
            var nm = HundunWorldGame.Instance?.NetworkManager;
            var handler = nm?.GetHandler<TeamMessageHandler>();
            if (handler == null || ReferenceEquals(handler, _handler)) return;

            if (_handler != null)
            {
                _handler.TeamOperationResolved -= OnResolved;
                _handler.TeamUpdated -= OnUpdated;
            }

            _handler = handler;
            _handler.TeamOperationResolved += OnResolved;
            _handler.TeamUpdated += OnUpdated;
        }

        public bool CreateTeam(string teamName, string teamGoal)
        {
            var game = HundunWorldGame.Instance;
            if (game?.NetworkManager == null || game.PlayerId == 0UL) return Notify("尚未登录，无法创建队伍");
            if (HasTeam) return Notify("已在队伍中；请先离开或解散");
            if (string.IsNullOrWhiteSpace(teamName)) return Notify("队伍名称不能为空");

            return Send(new CreateTeamRequest
            {
                Operation = TeamOperation.Create,
                LeaderId = game.PlayerId,
                TeamName = teamName.Trim(),
                TeamGoal = teamGoal ?? string.Empty,
            });
        }

        public bool JoinTeam(ulong teamId)
        {
            var game = HundunWorldGame.Instance;
            if (game?.NetworkManager == null || game.PlayerId == 0UL) return Notify("尚未登录，无法加入队伍");
            if (HasTeam) return Notify("已在队伍中；请先离开或解散");
            if (teamId == 0UL) return Notify("队伍ID无效");

            return Send(new CreateTeamRequest
            {
                Operation = TeamOperation.Join,
                LeaderId = game.PlayerId,
                TeamId = teamId,
            });
        }

        public bool LeaveTeam()
        {
            var team = CurrentTeam;
            var game = HundunWorldGame.Instance;
            if (team == null || team.TeamId == 0UL || game?.NetworkManager == null) return Notify("当前没有可离开的队伍");
            return Send(new CreateTeamRequest
            {
                Operation = TeamOperation.Leave,
                LeaderId = game.PlayerId,
                TeamId = team.TeamId,
            });
        }

        public bool KickMember(ulong targetId)
        {
            var team = CurrentTeam;
            var game = HundunWorldGame.Instance;
            if (team == null || team.TeamId == 0UL || game?.NetworkManager == null) return Notify("当前没有队伍");
            if (targetId == 0UL) return Notify("目标角色无效");
            return Send(new CreateTeamRequest
            {
                Operation = TeamOperation.Kick,
                LeaderId = game.PlayerId,
                TeamId = team.TeamId,
                TargetId = targetId,
            });
        }

        public bool TransferLeader(ulong newLeaderId)
        {
            var team = CurrentTeam;
            var game = HundunWorldGame.Instance;
            if (team == null || team.TeamId == 0UL || game?.NetworkManager == null) return Notify("当前没有队伍");
            if (newLeaderId == 0UL) return Notify("目标角色无效");
            return Send(new CreateTeamRequest
            {
                Operation = TeamOperation.Transfer,
                LeaderId = game.PlayerId,
                TeamId = team.TeamId,
                TargetId = newLeaderId,
            });
        }

        public bool DisbandTeam()
        {
            var team = CurrentTeam;
            var game = HundunWorldGame.Instance;
            if (team == null || team.TeamId == 0UL || game?.NetworkManager == null) return Notify("当前没有可解散的队伍");
            return Send(new CreateTeamRequest
            {
                Operation = TeamOperation.Disband,
                LeaderId = game.PlayerId,
                TeamId = team.TeamId,
            });
        }

        private bool Send(CreateTeamRequest request)
        {
            var nm = HundunWorldGame.Instance?.NetworkManager;
            if (nm == null) return Notify("网络未连接");
            EnsureAttached();
            _ = nm.SendAsync(request);
            return true;
        }

        private void OnResolved(CreateTeamResponse response)
        {
            if (response == null) return;

            lock (_gate)
            {
                if (response.Success)
                {
                    switch (response.Operation)
                    {
                        case TeamOperation.Leave:
                        case TeamOperation.Disband:
                            _team = null;
                            break;
                        default:
                            if (response.TeamInfo != null && response.TeamInfo.TeamId != 0UL)
                                _team = response.TeamInfo;
                            break;
                    }
                    _receivedOnce = true;
                }

                _lastNotice = response.Message ?? (response.Success ? "操作成功" : "操作失败");
                _revision++;
            }
            Debug.Log("[TeamClientService] " + _lastNotice);
        }

        private void OnUpdated(TeamInfo info)
        {
            if (info == null || info.TeamId == 0UL) return;
            lock (_gate)
            {
                _team = info;
                _receivedOnce = true;
                _revision++;
            }
        }

        private bool Notify(string message)
        {
            lock (_gate) _lastNotice = message ?? string.Empty;
            Debug.Log("[TeamClientService] " + message);
            return false;
        }
    }
}
