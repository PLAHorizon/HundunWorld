using FlaxEngine;
using Horizon.Game.Message.Enums;
using Horizon.Game.Message.Network;
using ManagedHundunWorld.Network.Handlers;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace HundunWorld.Game.Chat
{
    /// <summary>
    /// 脚本化世界生成的客户端入口：把聊天框里的 <c>/xxx</c> 指令拆成
    /// <see cref="GmScriptCommandMessage"/> 上行，并把服务端回包写回聊天日志。
    /// <para>
    /// 客户端<b>只做拆分</b>：模板是否存在、数量/半径/TTL 是否越界、有没有权限，全部由
    /// Grain 裁决（见 <c>WorldScriptCommandHandler</c>）。这里不复刻任何校验，避免双端口径漂移。
    /// </para>
    /// </summary>
    /// <remarks>
    /// 与 <see cref="ChatClient"/> 同处 <c>HundunWorld.Game.Chat</c>：指令回显要走聊天日志，
    /// 且该命名空间能屏蔽 <c>HundunWorld.Game.Social.ChatChannel</c> 的同名遮蔽问题。
    /// </remarks>
    public sealed class WorldScriptClient
    {
        public static WorldScriptClient Instance { get; } = new WorldScriptClient();

        private static readonly char[] WhiteSpaces = { ' ', '\t' };

        // 回包可能因断线丢失：待配对请求带时间戳，超龄即清理，字典不会无界增长。
        private static readonly TimeSpan PendingTtl = TimeSpan.FromSeconds(30);

        private readonly object _gate = new object();
        private readonly Dictionary<long, PendingRequest> _pending = new Dictionary<long, PendingRequest>();
        private WorldScriptMessageHandler _handler;
        private long _lastRequestId;

        /// <summary>单调递增的请求序号：服务端原样回填，用于把结果回包配对到本地指令。</summary>
        private static long NextRequestId => Interlocked.Increment(ref Instance._lastRequestId);

        private WorldScriptClient() { }

        private readonly struct PendingRequest
        {
            public readonly string Command;
            public readonly ScriptOp Op;
            public readonly long SentTicks;

            public PendingRequest(string command, ScriptOp op)
            {
                Command = command;
                Op = op;
                SentTicks = DateTime.UtcNow.Ticks;
            }
        }

        /// <summary>惰性挂接网络处理器（首次执行指令或重连后可再次调用；幂等）。</summary>
        public void EnsureAttached()
        {
            var nm = HundunWorldGame.Instance?.NetworkManager;
            var handler = nm?.GetHandler<WorldScriptMessageHandler>();
            if (handler == null || ReferenceEquals(handler, _handler))
                return;

            if (_handler != null)
                _handler.ScriptResultReceived -= OnScriptResult;

            _handler = handler;
            _handler.ScriptResultReceived += OnScriptResult;
        }

        /// <summary>
        /// 尝试把输入当指令执行。返回 true 表示这是斜杠指令（调用方据此不再发送聊天），
        /// 无论指令本身是否合法——语法错误已在聊天日志里回显。
        /// </summary>
        public bool TryHandleCommand(string text)
        {
            var raw = (text ?? string.Empty).Trim();
            if (raw.Length < 2 || raw[0] != '/')
                return false;

            EnsureAttached();

            var parts = raw.Split(WhiteSpaces, StringSplitOptions.RemoveEmptyEntries);
            var verb = parts[0].Substring(1).ToLowerInvariant();

            switch (verb)
            {
                case "spawn": return HandleSpawn(parts, raw);
                case "event": return HandleEvent(parts, raw);
                case "trigger": return HandleTrigger(parts, raw);
                case "despawn": return HandleDespawn(parts, raw);
                case "scriptperm": return Submit(New(ScriptOp.Query, ScriptedKind.None), raw);
                case "grant": return HandleGrant(parts, raw);
                case "revoke": return HandleRevoke(parts, raw);
                case "audit": return HandleAudit(parts, raw);
                default:
                    Echo($"{raw}：未知指令。可用 /spawn monster|npc、/event、/trigger、/despawn <实体ID>|mine、"
                         + "/scriptperm、/grant、/revoke、/audit。");
                    return true;
            }
        }

        #region 解析

        private bool HandleSpawn(string[] parts, string raw)
        {
            if (parts.Length < 3)
                return Reject(raw, "用法：/spawn monster <模板> [数量] [x,y,z] [存活秒] 或 /spawn npc <模板> [x,y,z]。坐标填你在场景里看到的 xyz。");

            var kind = parts[1].ToLowerInvariant();
            if (kind == "monster")
            {
                var cmd = New(ScriptOp.Spawn, ScriptedKind.Monster);
                cmd.TemplateKey = parts[2];

                // 坐标可省，后续裸数字无法按下标定位：按出现顺序归位，先数量后存活秒。
                int seenNumbers = 0;
                for (int i = 3; i < parts.Length; i++)
                {
                    if (parts[i].IndexOf(',') >= 0)
                    {
                        if (!TryApplyPosition(cmd, parts[i]))
                            return Reject(raw, "坐标需为 x,y,z 三段数字（服务端 Z-up 坐标，米）。");
                        continue;
                    }
                    if (!int.TryParse(parts[i], NumberStyles.Integer, CultureInfo.InvariantCulture, out var n))
                        return Reject(raw, $"无法识别的参数：{parts[i]}。");
                    if (seenNumbers++ == 0) cmd.Count = n;
                    else cmd.TtlSeconds = n;
                }
                return Submit(cmd, raw);
            }

            if (kind == "npc")
            {
                var cmd = New(ScriptOp.Spawn, ScriptedKind.Npc);
                cmd.TemplateKey = parts[2];
                for (int i = 3; i < parts.Length; i++)
                {
                    if (!TryApplyPosition(cmd, parts[i]))
                        return Reject(raw, "NPC 只接受可选坐标 x,y,z（数量与 TTL 由模板决定）。");
                }
                return Submit(cmd, raw);
            }

            return Reject(raw, $"未知生成类别：{parts[1]}（可用 monster / npc）。");
        }

        private bool HandleEvent(string[] parts, string raw)
        {
            if (parts.Length < 2)
                return Reject(raw, "用法：/event <模板> [x,y,z] [半径]。");

            var cmd = New(ScriptOp.Spawn, ScriptedKind.Event);
            cmd.TemplateKey = parts[1];
            for (int i = 2; i < parts.Length; i++)
            {
                if (parts[i].IndexOf(',') >= 0)
                {
                    if (!TryApplyPosition(cmd, parts[i]))
                        return Reject(raw, "坐标需为 x,y,z 三段数字（服务端 Z-up 坐标，米）。");
                    continue;
                }
                if (!float.TryParse(parts[i], NumberStyles.Float, CultureInfo.InvariantCulture, out var radius))
                    return Reject(raw, $"无法识别的参数：{parts[i]}。");
                cmd.Radius = radius;
            }
            return Submit(cmd, raw);
        }

        private bool HandleTrigger(string[] parts, string raw)
        {
            if (parts.Length < 4)
                return Reject(raw, "用法：/trigger <模板> <x,y,z> <半径> [once|repeat] [存活秒] [冷却秒]。坐标填你在场景里看到的 xyz。");

            var cmd = New(ScriptOp.Spawn, ScriptedKind.TriggerZone);
            cmd.TemplateKey = parts[1];
            // 触发区域必须显式给定点位（缺省点会落在角色脚下，语义上不是"区域"）。
            if (!TryApplyPosition(cmd, parts[2]))
                return Reject(raw, "坐标需为 x,y,z 三段数字（服务端 Z-up 坐标，米）。");
            if (!float.TryParse(parts[3], NumberStyles.Float, CultureInfo.InvariantCulture, out var triggerRadius))
                return Reject(raw, "半径需为数字（米）。");
            cmd.Radius = triggerRadius;

            int seenNumbers = 0;
            for (int i = 4; i < parts.Length; i++)
            {
                var token = parts[i].ToLowerInvariant();
                if (token == "once") { cmd.TriggerMode = ScriptTriggerMode.Once; continue; }
                if (token == "repeat") { cmd.TriggerMode = ScriptTriggerMode.Repeat; continue; }
                if (!int.TryParse(token, NumberStyles.Integer, CultureInfo.InvariantCulture, out var n))
                    return Reject(raw, $"无法识别的参数：{parts[i]}。");
                if (seenNumbers++ == 0) cmd.TtlSeconds = n;
                else cmd.CooldownSeconds = n;
            }
            return Submit(cmd, raw);
        }

        private bool HandleDespawn(string[] parts, string raw)
        {
            if (parts.Length < 2)
                return Reject(raw, "用法：/despawn <实体ID>（需'清除他人生成物'位）或 /despawn mine（需'清除本人生成物'位）。");

            if (parts[1].ToLowerInvariant() == "mine")
                return Submit(New(ScriptOp.DespawnMine, ScriptedKind.None), raw);

            if (!ulong.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var entityId))
                return Reject(raw, $"实体 ID 需为非负整数：{parts[1]}。");

            var cmd = New(ScriptOp.Despawn, ScriptedKind.None);
            cmd.TargetEntityId = entityId;
            return Submit(cmd, raw);
        }

        /// <summary>
        /// /grant &lt;角色ID&gt; &lt;权限位数值&gt; [有效小时]
        /// <para>
        /// 授权面刻意留在游戏内指令这条链上（客户端 → GameGateway → Grain）：<b>不新增 HTTP 管理端点</b>。
        /// 页面/工具做管理只是表现层的事，权限判定的入口必须只有一个。
        /// </para>
        /// </summary>
        private bool HandleGrant(string[] parts, string raw)
        {
            if (parts.Length < 3)
                return Reject(raw, "用法：/grant <角色ID> <权限位数值> [有效小时，0=不过期]。");

            if (!long.TryParse(parts[1], out var target) || target <= 0)
                return Reject(raw, "角色ID 需为正整数。");

            if (!uint.TryParse(parts[2], out var flags) || flags == 0)
                return Reject(raw, "权限位需为正的无符号整数（按位相加）。");

            var hours = 168;
            if (parts.Length >= 4 && (!int.TryParse(parts[3], out hours) || hours < 0))
                return Reject(raw, "有效小时需为非负整数。");

            var cmd = New(ScriptOp.Grant, ScriptedKind.None);
            cmd.TargetOperatorId = target;
            cmd.RequestedFlags = flags;
            cmd.TtlHours = hours;
            return Submit(cmd, raw);
        }

        /// <summary>/revoke [角色ID]：省略时回收本人授权。</summary>
        private bool HandleRevoke(string[] parts, string raw)
        {
            var cmd = New(ScriptOp.Revoke, ScriptedKind.None);

            if (parts.Length >= 2)
            {
                if (!long.TryParse(parts[1], out var target) || target <= 0)
                    return Reject(raw, "角色ID 需为正整数，或用 /revoke 回收本人授权。");
                cmd.TargetOperatorId = target;
            }

            return Submit(cmd, raw);
        }

        /// <summary>/audit [条数]：查最近若干条脚本授权/操作审计。</summary>
        private bool HandleAudit(string[] parts, string raw)
        {
            var cmd = New(ScriptOp.Audit, ScriptedKind.None);

            if (parts.Length >= 2 && (!int.TryParse(parts[1], out var limit) || limit <= 0 || limit > 200))
                return Reject(raw, "条数需在 1 ~ 200。");
            else if (parts.Length >= 2)
                cmd.AuditLimit = int.Parse(parts[1]);

            return Submit(cmd, raw);
        }

        private static GmScriptCommandMessage New(ScriptOp op, ScriptedKind kind) => new GmScriptCommandMessage
        {
            RequestId = NextRequestId,
            Op = op,
            Kind = kind,
        };

        /// <summary>
        /// 解析并写入 <c>x,y,z</c>。坐标缺省时调用方不写 <see cref="GmScriptCommandMessage.HasPosition"/>
        /// （保持 false）——服务端据此采用该角色的<b>权威坐标</b>，这里绝不用本地演员位置顶替，
        /// 否则等于把"我在哪"交给客户端断言。
        /// <para>
        /// <b>输入用的是玩家在游戏里看到的坐标（Flax Y-up：Y 是上下），发出去前换成服务端口径</b>
        /// （ECS Z-up：Y 是前后、Z 是上下）。DS 的生成点、触发区域球心、距离校验基准全在 Z-up 空间，
        /// 而快照下发给客户端时才换回 Y-up；不换这一刀，"<code>x,y,z</code>"里的高低与前后会互换，
        /// 生成点落到错处、距离校验也会把同一位置算成几十米外。与
        /// <c>HundunWorldGame</c> 收快照时的 <c>(x, z, y)</c> 正好对称。
        /// </para>
        /// </summary>
        private static bool TryApplyPosition(GmScriptCommandMessage cmd, string token)
        {
            var comps = token.Split(',');
            if (comps.Length != 3) return false;
            if (!float.TryParse(comps[0], NumberStyles.Float, CultureInfo.InvariantCulture, out var x)) return false;
            if (!float.TryParse(comps[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var y)) return false;
            if (!float.TryParse(comps[2], NumberStyles.Float, CultureInfo.InvariantCulture, out var z)) return false;

            cmd.X = x;
            cmd.Y = z; // 服务端 Y（前后）← 客户端 Z（前后）
            cmd.Z = y; // 服务端 Z（上下）← 客户端 Y（上下）
            cmd.HasPosition = true;
            return true;
        }

        #endregion

        #region 上行与回显

        private bool Submit(GmScriptCommandMessage cmd, string raw)
        {
            var nm = HundunWorldGame.Instance?.NetworkManager;
            if (nm == null)
                return Reject(raw, "网络未就绪，指令未发送。");

            // 记下原始指令与操作类别：回包只带 RequestId，靠它把结果配对回指令文本并决定回显粒度。
            lock (_gate)
            {
                PruneExpired();
                _pending[cmd.RequestId] = new PendingRequest(raw, cmd.Op);
            }

            // 没有送达回执就不能算"已发出"：DeliverAsync 会撤配对记录并如实回显，
            // 避免操作者以为指令已在执行（真正的成败结论仍只认服务端回包）。
            _ = DeliverAsync(cmd, raw);
            return true;
        }

        private async Task DeliverAsync(GmScriptCommandMessage cmd, string raw)
        {
            bool delivered;
            try
            {
                delivered = await HundunWorldGame.Instance.NetworkManager.SendAsync(cmd);
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[WorldScriptClient] 指令上行异常: {ex.Message}");
                delivered = false;
            }

            if (delivered) return;

            lock (_gate) _pending.Remove(cmd.RequestId);
            Echo($"{raw}：指令未能送达服务器（连接不可用）。");
        }

        private bool Reject(string raw, string reason)
        {
            Echo($"{raw}：{reason}");
            return true;
        }

        private void OnScriptResult(GmScriptResultMessage result)
        {
            if (result == null) return;

            string command = null;
            bool isQuery = false;
            lock (_gate)
            {
                if (_pending.TryGetValue(result.RequestId, out var pending))
                {
                    command = pending.Command;
                    isQuery = pending.Op == ScriptOp.Query;
                    _pending.Remove(result.RequestId);
                }
            }

            var head = string.IsNullOrEmpty(command) ? "脚本指令" : command;
            var text = $"{head} → {ServerText(result)}";

            if (!result.Success)
            {
                // 失败只回原因：服务端未走完裁决流程时 RemainingBudget 无意义，不拿来误导操作者。
                Echo($"{text}（原因码 {result.Reason}：{ReasonText(result.Reason)}）");
                return;
            }

            var extras = new List<string>();
            if (result.EntityIds != null && result.EntityIds.Count > 0)
                extras.Add($"实体ID：{string.Join(", ", result.EntityIds)}");
            extras.Add($"剩余配额：{result.RemainingBudget}");
            // 查询才展开权限位：其余操作回显保持一行，避免刷屏。
            if (isQuery)
                extras.Add($"权限位：{FlagText(result.PermissionFlags)}");
            Echo($"{text}（{string.Join("，", extras)}）");
        }

        /// <summary>
        /// DS 执行完成后的<b>实体 ID 回执</b>（走同步通道的 ScriptedSpawnResult，比网关的受理回包更晚到）。
        /// <para>
        /// 分成两条是必要的：网关那条只代表"裁决通过、指令已投递"，而实体 ID 只有 DS 知道。
        /// 把两者混在一条上会让"已受理"谎报成"世界里已经有了"。
        /// </para>
        /// </summary>
        public void ApplySpawnResult(long requestId, ulong[] entityIds, bool removed)
        {
            if (requestId <= 0) return;

            var verb = removed ? "已清除" : "已生成";
            if (entityIds == null || entityIds.Length == 0)
            {
                Echo($"[脚本回执] {verb}：0 个实体（可能已被清理、超出上限或目标本就不存在）。");
                return;
            }

            // ID 用十进制打印：与 /despawn <id> 的解析口径一致，复制即可用。
            Echo($"[脚本回执] {verb} {entityIds.Length} 个实体：{string.Join(", ", entityIds)}"
                 + (removed ? string.Empty : "（可用 /despawn <id> 清除）"));
        }

        /// <summary>服务端 <c>Message</c> 是权威文案；缺失时按成败给一句兜底。</summary>
        private static string ServerText(GmScriptResultMessage result)
            => string.IsNullOrWhiteSpace(result.Message)
                ? (result.Success ? "已完成。" : "未执行。")
                : result.Message;

        private static void Echo(string text)
        {
            try
            {
                ChatClient.Instance.AddLocalSystem(text);
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[WorldScriptClient] 回显聊天日志失败: {ex.Message}");
            }
        }

        private void PruneExpired()
        {
            var cutoff = DateTime.UtcNow.Subtract(PendingTtl).Ticks;
            if (_pending.Count == 0) return;
            foreach (var id in _pending.Where(kv => kv.Value.SentTicks < cutoff).Select(kv => kv.Key).ToList())
                _pending.Remove(id);
        }

        private static string ReasonText(ScriptRejectReason reason) => reason switch
        {
            ScriptRejectReason.Disabled => "脚本化生成功能未开启",
            ScriptRejectReason.NoGrant => "该角色未被授予脚本权限",
            ScriptRejectReason.InsufficientPermission => "权限位不足",
            ScriptRejectReason.Expired => "授权已过期",
            ScriptRejectReason.UnknownTemplate => "模板不在白名单内",
            ScriptRejectReason.InvalidArgument => "参数非法",
            ScriptRejectReason.TooFar => "目标点超出允许距离",
            ScriptRejectReason.Cooldown => "指令冷却中",
            ScriptRejectReason.QuotaExhausted => "每分钟配额已用尽",
            ScriptRejectReason.EntityBudgetExceeded => "该 shard 脚本实体已达上限",
            ScriptRejectReason.Internal => "服务端内部错误",
            _ => "无",
        };

        private static string FlagText(uint flags)
        {
            if (flags == 0) return "无";
            var named = new List<string>();
            if ((flags & (uint)ScriptPermissionFlags.SpawnMonster) != 0) named.Add("生成怪物");
            if ((flags & (uint)ScriptPermissionFlags.SpawnNpc) != 0) named.Add("生成NPC");
            if ((flags & (uint)ScriptPermissionFlags.FireEvent) != 0) named.Add("表现型事件");
            if ((flags & (uint)ScriptPermissionFlags.SpawnEvent) != 0) named.Add("玩法型事件");
            if ((flags & (uint)ScriptPermissionFlags.PlaceTrigger) != 0) named.Add("触发区域");
            if ((flags & (uint)ScriptPermissionFlags.DespawnOwned) != 0) named.Add("清除本人生成物");
            if ((flags & (uint)ScriptPermissionFlags.DespawnAny) != 0) named.Add("清除他人生成物");
            if ((flags & (uint)ScriptPermissionFlags.MultiEntity) != 0) named.Add("多实体");
            if ((flags & (uint)ScriptPermissionFlags.BypassDist) != 0) named.Add("超距");
            if ((flags & (uint)ScriptPermissionFlags.Query) != 0) named.Add("查询");
            if ((flags & (uint)ScriptPermissionFlags.GmOps) != 0) named.Add("GM运维");
            return named.Count > 0 ? string.Join("、", named) : $"0x{flags:X8}";
        }

        #endregion
    }
}
