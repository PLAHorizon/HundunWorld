using Horizon.Core.Security;
using Horizon.Game.Core.Security;
using Horizon.Game.Core.Sim.Server;
using Horizon.Game.Core.World;
using Horizon.Game.Message;
using Horizon.Game.Message.Enums;
using Horizon.Game.Message.Network;
using Horizon.Game.Message.Security;
using Horizon.Orleans.Interface.World;
using MemoryPack;
using Microsoft.Extensions.Logging;
using Orleans;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using TouchSocket.Sockets;

namespace Horizon.Game.Core.Handlers
{
    /// <summary>
    /// 脚本化世界生成指令处理器（运行于 Gateway 进程）。
    /// <para>
    /// 链路：<b>身份只取已验签 AuthToken</b> → <see cref="IGmCommandGrain"/> 权威裁决并签发一次性令牌
    /// → 令牌经 <see cref="IDsScriptDispatcher"/> 下发给承载该 shard 的 DS → DS 执行并经快照广播。
    /// </para>
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>为什么不能照抄 <see cref="ChatHandler"/> 的取身份方式</b>：聊天用 <c>req.SenderId</c>
    /// （客户端自报），而本通道要据此授予"能在世界里造东西"的能力，自报身份等于把权限交给伪造包。
    /// 这里 <c>OperatorCharacterId</c> 一律来自令牌解出的 <see cref="UserAuthTokenData.CharacterId"/>。
    /// </para>
    /// <para>
    /// 网关**不做任何权限判断**，只做搬运与回显；一切"能不能生成"的结论来自 Grain。
    /// </para>
    /// </remarks>
    public class WorldScriptCommandHandler : MessageHandlerBase
    {
        private readonly IClusterClient _cluster;
        private readonly UserAuthTokenProvider _tokenProvider;
        private readonly IShardRouter _shardRouter;
        private readonly IDsScriptDispatcher _dispatcher;
        private readonly byte[]? _signingKey;

        public WorldScriptCommandHandler(
            ILogger<MessageHandlerBase> logger,
            IClusterClient clusterClient,
            HorizonMessageAdapter adapter,
            UserAuthTokenProvider tokenProvider,
            IShardRouter shardRouter,
            IDsScriptDispatcher dispatcher,
            ScriptedSpawnOptions? scriptOptions = null) : base(logger, clusterClient, adapter)
        {
            _cluster = clusterClient;
            _tokenProvider = tokenProvider;
            _shardRouter = shardRouter;
            _dispatcher = dispatcher;

            // 密钥与 Grain / DS 同源（配置节 ScriptedSigningKey 的 SigningKeyBase64）。
            // 取不到时不做自检而交由 DS 强制验签兜底——DS 无密钥则一律丢弃，不会误执行。
            var configured = scriptOptions?.SigningKeyBase64;
            if (!string.IsNullOrWhiteSpace(configured))
            {
                try
                {
                    _signingKey = Convert.FromBase64String(configured);
                }
                catch (FormatException)
                {
                    Logger.LogError("ScriptedSpawn:SigningKeyBase64 不是合法 Base64，网关侧令牌自检将被跳过。");
                }
            }
        }

        public override List<MessageType> MessageTypes { get; } = new List<MessageType>
        {
            MessageType.ScriptCommand,
        };

        public override ServiceType ServiceType => ServiceType.System;

        /// <summary>
        /// 旧式路由入口（协议收敛期保留）。真实分发走 <see cref="HandleAsync"/>，
        /// 这里只负责把它的 <see cref="MessageUnion"/> 响应打包成帧。
        /// </summary>
        public override async Task<(bool IsSuccess, HorizonMessagePacket MessagePacket)> RouteHandlerAsync(HorizonMessagePacket message)
        {
            var (isSuccess, response) = await HandleAsync(null!, message);
            var result = response as GmScriptResultMessage
                ?? Result(0, false, ScriptRejectReason.Internal, "脚本指令无响应。", 0, 0);
            return (isSuccess, CreateHorizonMessage(result));
        }

        public override async Task<(bool IsSuccess, MessageUnion? Response)> HandleAsync(
            ITcpSessionClient client, HorizonMessagePacket message)
        {
            if (message.Body is not GmScriptCommandMessage cmd)
                return (false, Result(0, false, ScriptRejectReason.InvalidArgument, "消息类型不匹配。", 0, 0));

            var operatorId = ResolveOperatorId(message);
            if (operatorId <= 0)
            {
                Logger.LogWarning("脚本指令被拒：无法从 AuthToken 解析操作者身份。RequestId={RequestId}", cmd.RequestId);
                return (false, Result(cmd.RequestId, false, ScriptRejectReason.InsufficientPermission,
                    "身份校验失败，请先登录游戏。", 0, 0));
            }

            var shardId = _shardRouter.Resolve(operatorId);

            var ticket = new GmScriptTicket
            {
                OperatorCharacterId = operatorId,
                RequestId = cmd.RequestId,
                Op = cmd.Op,
                Kind = cmd.Kind,
                TemplateKey = cmd.TemplateKey ?? string.Empty,
                Count = cmd.Count,
                HasPosition = cmd.HasPosition,
                X = cmd.X,
                Y = cmd.Y,
                Z = cmd.Z,
                TtlSeconds = cmd.TtlSeconds,
                Radius = cmd.Radius,
                TriggerMode = cmd.TriggerMode,
                CooldownSeconds = cmd.CooldownSeconds,
                TargetEntityId = cmd.TargetEntityId,
                OperatorShardId = shardId,
            };

            // 授权管理类操作（授予/回收/审计）**不产生令牌、不经 DS**：直接交给 Grain 的对应方法。
            // 这样"谁能生成东西"这件事仍然只有一条入口与一个真值源，没有第二条 HTTP 管理门。
            if (cmd.Op == ScriptOp.Grant || cmd.Op == ScriptOp.Revoke || cmd.Op == ScriptOp.Audit)
                return await HandleAuthorityAdminAsync(operatorId, cmd);

            GmScriptVerdict verdict;
            try
            {
                // 全局单例 GM 权威 Grain（Key=0）。一切权限结论出自这里。
                verdict = await _cluster.GetGrain<IGmCommandGrain>(0).TryConsumeScriptAsync(ticket);
            }
            catch (Exception ex)
            {
                // fail-closed：授权层不可用时绝不"当作允许"。
                Logger.LogError(ex, "脚本指令裁决失败（授权层不可达）。Operator={Operator}", operatorId);
                return (false, Result(cmd.RequestId, false, ScriptRejectReason.Internal,
                    "授权服务暂不可用，指令未执行。", 0, 0));
            }

            if (!verdict.Allowed)
            {
                Logger.LogInformation("脚本指令被拒。Operator={Operator} Op={Op} Kind={Kind} Reason={Reason}",
                    operatorId, cmd.Op, cmd.Kind, verdict.Reason);
                return (false, Result(cmd.RequestId, false, verdict.Reason, verdict.Message,
                    verdict.RemainingBudget, verdict.EffectiveFlags));
            }

            // Query 只读授权快照，不需要下发 DS。
            if (cmd.Op == ScriptOp.Query)
            {
                return (true, Result(cmd.RequestId, true, ScriptRejectReason.None,
                    verdict.Message, verdict.RemainingBudget, verdict.EffectiveFlags));
            }

            var token = verdict.Token;
            if (token == null)
                return (false, Result(cmd.RequestId, false, ScriptRejectReason.Internal,
                    "裁决通过但缺少执行凭证。", verdict.RemainingBudget, verdict.EffectiveFlags));

            // 交付前自检：令牌必须由本环境的密钥签发且未过期（防御网关与 Grain 部署错位）。
            var keyBytes = _signingKey;
            if (keyBytes != null && !ScriptGrantTokenSigner.Validate(token, keyBytes,
                    DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(), out var validateReason))
            {
                Logger.LogError("脚本令牌自检失败，拒绝下发。Reason={Reason}", validateReason);
                return (false, Result(cmd.RequestId, false, ScriptRejectReason.Internal,
                    "执行凭证校验失败。", verdict.RemainingBudget, verdict.EffectiveFlags));
            }

            bool delivered;
            try
            {
                delivered = await _dispatcher.DispatchAsync(shardId, token);
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "脚本指令下发异常。Operator={Operator}", operatorId);
                delivered = false;
            }

            if (!delivered)
            {
                // 已经扣过预算但世界没收到指令：如实告知，别让操作者以为"成功但没效果"。
                Logger.LogWarning("脚本指令下发失败（DS 不可用）。Operator={Operator} Shard={Shard}", operatorId, shardId);
                return (false, Result(cmd.RequestId, false, ScriptRejectReason.Internal,
                    "世界服务暂不可用，指令未生效（配额已消耗）。", verdict.RemainingBudget, verdict.EffectiveFlags));
            }

            var entityIds = cmd.Op == ScriptOp.Spawn ? PredictEntityIds(token) : new List<ulong>();
            return (true, Result(cmd.RequestId, true, ScriptRejectReason.None, verdict.Message,
                verdict.RemainingBudget, verdict.EffectiveFlags, entityIds));
        }

        /// <summary>
        /// 授权管理分支：调用者身份同样只来自令牌，权限判定完全在 Grain 内
        /// （须持 GmOps 位，或位列 <c>ScriptedSpawn:BootstrapAdminIds</c> 引导名单）。
        /// </summary>
        private async Task<(bool IsSuccess, MessageUnion? Response)> HandleAuthorityAdminAsync(
            long callerId, GmScriptCommandMessage cmd)
        {
            var grain = _cluster.GetGrain<IGmCommandGrain>(0);
            var target = cmd.TargetOperatorId > 0 ? cmd.TargetOperatorId : callerId;

            try
            {
                switch (cmd.Op)
                {
                    case ScriptOp.Grant:
                    {
                        var result = await grain.GrantScriptAsync(
                            target,
                            (ScriptPermissionFlags)cmd.RequestedFlags,
                            TimeSpan.FromHours(Math.Clamp(cmd.TtlHours, 0, 8760)),
                            callerId,
                            "游戏内 GM 指令");
                        return (result.Success, Result(cmd.RequestId, result.Success,
                            result.Success ? ScriptRejectReason.None : ScriptRejectReason.InsufficientPermission,
                            result.Success ? $"已授予 {target}：{result.OperationId}" : $"授予被拒：{result.ErrorMessage}",
                            0, cmd.RequestedFlags));
                    }

                    case ScriptOp.Revoke:
                    {
                        var result = await grain.RevokeScriptAsync(target, callerId, "游戏内 GM 指令");
                        return (result.Success, Result(cmd.RequestId, result.Success,
                            result.Success ? ScriptRejectReason.None : ScriptRejectReason.InsufficientPermission,
                            result.Success ? $"已回收 {target} 的脚本权限" : $"回收被拒：{result.ErrorMessage}",
                            0, 0));
                    }

                    default:
                    {
                        var entries = await grain.QueryScriptAuditAsync(target, Math.Clamp(cmd.AuditLimit, 1, 200));
                        var text = entries.Length == 0
                            ? $"{target} 暂无脚本操作审计记录。"
                            : string.Join(System.Environment.NewLine, entries.Select(e2 =>
                                $"{e2.Timestamp:MM-dd HH:mm:ss} {e2.OperationType} -> {e2.Details}"));
                        return (true, Result(cmd.RequestId, true, ScriptRejectReason.None, text, 0, 0));
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "脚本授权管理指令失败。Caller={Caller} Op={Op}", callerId, cmd.Op);
                return (false, Result(cmd.RequestId, false, ScriptRejectReason.Internal, "授权服务异常。", 0, 0));
            }
        }

        /// <summary>
        /// 从<b>已验签令牌</b>解析操作者角色 ID。返回 0 表示身份不可用。
        /// </summary>
        private long ResolveOperatorId(HorizonMessagePacket message)
        {
            var raw = message.Header?.AuthToken;
            if (string.IsNullOrWhiteSpace(raw)) return 0;

            var result = _tokenProvider.ValidateToken(raw);
            if (!result.IsValid || result.TokenData == null) return 0;

            return result.TokenData.CharacterId;
        }

        /// <summary>
        /// 预估本次生成的实体 ID，便于操作者随后 <c>/despawn</c> 引用。
        /// <para>
        /// 沿用 DS 的确定性分配式 <c>(spawnPointId &lt;&lt; 32) | (slot+1)</c>，
        /// 但<b>脚本实体用负数域</b>（详见设计方案 §3.3），而负数域的序号只有 DS 自己知道，
        /// 因此受理回包里它始终为空；真实 ID 由 DS 在生效后经
        /// <c>SyncEventKind.ScriptedSpawnResult</c> 定向回执给操作者（客户端 WorldScriptClient
        /// .ApplySpawnResult 落聊天），不做"猜一个 ID 回给客户端"这种会误导的事。
        /// </para>
        /// </summary>
        private static List<ulong> PredictEntityIds(ScriptGrantToken token) => new();

        private static GmScriptResultMessage Result(
            long requestId,
            bool success,
            ScriptRejectReason reason,
            string message,
            int remainingBudget,
            uint permissionFlags,
            List<ulong>? entityIds = null) => new()
            {
                RequestId = requestId,
                Success = success,
                Reason = reason,
                Message = message,
                RemainingBudget = remainingBudget,
                PermissionFlags = permissionFlags,
                EntityIds = entityIds ?? new List<ulong>(),
            };
    }
}
