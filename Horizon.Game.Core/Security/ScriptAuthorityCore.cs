using System;
using System.Collections.Generic;
using Horizon.Game.Message.Network;
using Horizon.Game.Message.Security;
using Horizon.Game.Sim;

namespace Horizon.Game.Core.Security
{
    /// <summary>
    /// 脚本化世界生成的服务端限额（设计方案 §4.1 判定口径、§7 防护清单）。
    /// <para>
    /// 全部走配置而非常量：不同环境（本地开发 / 压测 / 正式服）的容许强度差别极大，
    /// 但<b>判定逻辑本身不变</b>——逻辑在 <see cref="ScriptAuthorityCore"/>，数值在这里。
    /// </para>
    /// </summary>
    public sealed class ScriptedSpawnOptions
    {
        /// <summary>配置节名。</summary>
        public const string SectionName = "ScriptedSpawn";

        /// <summary>总开关。默认关闭，灰度开启。</summary>
        public bool Enabled { get; set; }

        /// <summary>网关与 DS 共享的 HMAC 密钥（Base64）。空 = 无法签发/验签，功能实际不可用（fail-closed）。</summary>
        public string SigningKeyBase64 { get; set; } = string.Empty;

        /// <summary>令牌有效期秒数。必须短——它是"已裁决"的凭证，不是长期权限。</summary>
        public int TokenTtlSeconds { get; set; } = 5;

        /// <summary>单操作者两次指令之间的最小间隔秒数。</summary>
        public int CooldownSeconds { get; set; } = 5;

        /// <summary>单操作者每分钟允许的指令数。</summary>
        public int QuotaPerMinute { get; set; } = 60;

        /// <summary>单次生成数量硬顶（即便持 <c>MultiEntity</c> 位也不能超）。</summary>
        public int MaxCountPerRequest { get; set; } = 10;

        /// <summary>目标点相对角色权威坐标的最大距离（米）。持 <c>BypassDist</c> 位可越。</summary>
        public float MaxDistanceMetres { get; set; } = 50f;

        /// <summary>单个 shard 同时存活的脚本实体上限。</summary>
        public int MaxScriptedEntitiesPerShard { get; set; } = 200;

        /// <summary>单个 shard 同时存活的触发区域上限。</summary>
        public int MaxTriggerZonesPerShard { get; set; } = 32;

        /// <summary>触发区域半径上限（米）。</summary>
        public float MaxTriggerRadiusMetres { get; set; } = 60f;

        /// <summary>触发区域最大存活秒数（强制有限，防止长期驻留的隐形刷怪机器）。</summary>
        public int MaxTriggerTtlSeconds { get; set; } = 600;

        /// <summary>非触发类生成物的最大存活秒数；0 表示允许永久。</summary>
        public int MaxSpawnTtlSeconds { get; set; } = 0;

        /// <summary>
        /// <b>引导管理员</b>角色 ID 列表：解决"第一个 GM 由谁授予"的鸡生蛋问题。
        /// <para>
        /// 列在其中的 ID 被视为你已人工确认过的最高权限持有者，可调用
        /// <c>GrantScriptAsync</c> 授予 <see cref="Message.Network.ScriptPermissionFlags.GameMaster"/>
        /// 。每次引导授予都会以 Warning 落审计，便于事后核查。
        /// </para>
        /// <para>正式运营应尽快把权限迁到 DB 里，并清空本列表。</para>
        /// </summary>
        public long[] BootstrapAdminIds { get; set; } = Array.Empty<long>();

        /// <summary>默认值（关闭态）。</summary>
        public static ScriptedSpawnOptions Default { get; } = new();
    }

    /// <summary>
    /// 操作者的授权与预算状态（由 Grain 持有并持久化；本结构为纯数据，便于单测构造）。
    /// </summary>
    public sealed class ScriptOperatorState
    {
        /// <summary>生效权限位（<see cref="ScriptPermissionFlags"/> 的整数值）。</summary>
        public uint Flags { get; set; }

        /// <summary>授权到期 UTC 时刻。<see cref="DateTime.MaxValue"/> 表示不过期。</summary>
        public DateTime ExpiresAtUtc { get; set; } = DateTime.MaxValue;

        /// <summary>授予时刻。</summary>
        public DateTime GrantedAtUtc { get; set; }

        /// <summary>授予者（0 = 系统引导）。</summary>
        public long GrantedBy { get; set; }

        /// <summary>授予理由（进审计）。</summary>
        public string Reason { get; set; } = string.Empty;

        /// <summary>最近一次被接受的 Unix 毫秒，用于冷却判定。</summary>
        public long LastAcceptedUnixMs { get; set; }

        /// <summary>当前配额窗口起始 Unix 毫秒。</summary>
        public long QuotaWindowStartUnixMs { get; set; }

        /// <summary>当前配额窗口内已消耗次数。</summary>
        public int QuotaWindowUsed { get; set; }

        /// <summary>已签发令牌计数（单调，参与 Nonce 生成，保证同毫秒也不重复）。</summary>
        public long IssuedTokenCounter { get; set; }

        /// <summary>是否存在有效授权（不判权限位，只看有没有记录且未过期）。</summary>
        public bool HasActiveGrant(DateTime nowUtc) =>
            Flags != 0 && nowUtc <= ExpiresAtUtc;
    }

    /// <summary>裁决所需的运行时上下文。</summary>
    public sealed class ScriptEvalContext
    {
        /// <summary>待裁决请求。</summary>
        public GmScriptTicket Ticket { get; set; } = null!;

        /// <summary>操作者当前<b>权威</b>坐标（服务端 Z-up）。由 Grain 从 DS 位置回写/ZoneShard 取，绝不用客户端上报。</summary>
        public float OperatorX { get; set; }

        public float OperatorY { get; set; }

        public float OperatorZ { get; set; }

        /// <summary>
        /// 服务端是否<b>确实持有</b>操作者的权威坐标。
        /// <para>
        /// 必须有这个标志位，而不能靠"坐标取不到就当 0"：<c>(0,0,0)</c> 在世界里是一个真实位置，
        /// 一旦位置存储为空时仍继续裁决，客户端上报的目标点就会被拿去和原点比距离——
        /// 于是"在自己脚下生成"被误拒，而"在 origin 附近生成"被误放，距离校验形同虚设。
        /// </para>
        /// </summary>
        public bool OperatorPositionKnown { get; set; } = true;

        /// <summary>操作者当前所在 shard。</summary>
        public long OperatorShardId { get; set; }

        /// <summary>目标 shard 当前脚本实体存活数（触发区域另计）。</summary>
        public int ShardScriptedEntityCount { get; set; }

        /// <summary>目标 shard 当前触发区域数。</summary>
        public int ShardTriggerZoneCount { get; set; }

        /// <summary>当前 UTC 时刻。</summary>
        public DateTime NowUtc { get; set; }

        /// <summary>当前 Unix 毫秒。</summary>
        public long NowUnixMs { get; set; }
    }

    /// <summary>
    /// 脚本授权裁决核（设计方案 §4.1 的七步判定）。
    /// <para>
    /// <b>为什么是纯逻辑类</b>：这是唯一真值源，必须可脱离 Orleans/网络单测（本项目要求黄金值
    /// 与权限矩阵具备可执行路径，不接受"仅编译通过"）。Grain 只做状态装载、调用本核、持久化回写。
    /// </para>
    /// </summary>
    public sealed class ScriptAuthorityCore
    {
        private readonly ScriptedSpawnOptions _options;
        private readonly IScriptTemplateCatalog _catalog;

        public ScriptAuthorityCore(ScriptedSpawnOptions options, IScriptTemplateCatalog catalog)
        {
            _options = options ?? ScriptedSpawnOptions.Default;
            _catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
        }

        /// <summary>
        /// 裁决一次请求。<b>通过时会就地扣减 <paramref name="state"/> 的冷却/配额/计数并签发令牌</b>。
        /// </summary>
        /// <param name="state">操作者状态（会被修改）。</param>
        /// <param name="ctx">运行时上下文。</param>
        /// <param name="signingKey">HMAC 密钥；为空则一律拒绝（fail-closed）。</param>
        public ScriptDecision Evaluate(ScriptOperatorState state, ScriptEvalContext ctx, byte[]? signingKey)
        {
            var ticket = ctx.Ticket;
            var nowUtc = ctx.NowUtc;

            // 0) 总开关与密钥：缺一不可，且缺密钥绝不"跳过验签"。
            if (!_options.Enabled)
                return ScriptDecision.Deny(ScriptRejectReason.Disabled, "脚本化生成功能未开启。");

            if (signingKey == null || signingKey.Length == 0)
                return ScriptDecision.Deny(ScriptRejectReason.Internal, "授权服务密钥缺失（fail-closed）。");

            // 1) 授权存在性与到期。
            if (state.Flags == 0)
                return ScriptDecision.Deny(ScriptRejectReason.NoGrant, "该角色未被授予脚本权限。");

            if (nowUtc > state.ExpiresAtUtc)
                return ScriptDecision.Deny(ScriptRejectReason.Expired, "脚本权限授权已过期。");

            // 2) 权限位覆盖本操作。
            var required = RequiredFlagFor(ticket.Op, ticket.Kind);

            // 事件分两档，且**形态由服务端目录决定**（不是指令参数）：
            // 只放表现（文字/特效）的派发用 FireEvent，会往世界里放怪群的用 SpawnEvent。
            // 模板解析不出来时保持 SpawnEvent 一档，让第 5 步去报"模板不在白名单"——
            // 那样操作者看到的是他真正该修的东西。
            if (ticket.Op == ScriptOp.Spawn && ticket.Kind == ScriptedKind.Event
                && _catalog.TryResolve(ticket.Kind, ticket.TemplateKey, out var eventId)
                && _catalog.IsPresentationOnly(ticket.Kind, eventId))
            {
                required = (uint)ScriptPermissionFlags.FireEvent;
            }

            if ((state.Flags & required) != required)
                return ScriptDecision.Deny(ScriptRejectReason.InsufficientPermission,
                    $"缺少权限位 {DescribeFlag(required)}。");

            // "Debug 能跑、Release 报错误导"的排查噪声。

            // 2.5) 生成类操作必须有服务端权威位置作参照。不看 ticket.HasPosition：
            //      客户端**可以**自报目标点，但"自报的目标点"与"自报的立场"是两回事，
            //      后者一律不采信（否则距离校验的比较基准就归客户端管了）。
            //      Despawn/Query/授权管理不依赖位置，故不在此拦。
            if (ticket.Op == ScriptOp.Spawn && !ctx.OperatorPositionKnown)
                return ScriptDecision.Deny(ScriptRejectReason.InvalidArgument,
                    "尚未取得该角色的服务端权威位置，无法校验生成点。");

            // 3) 冷却。
            if (state.LastAcceptedUnixMs > 0 &&
                ctx.NowUnixMs - state.LastAcceptedUnixMs < (long)_options.CooldownSeconds * 1000L)
                return ScriptDecision.Deny(ScriptRejectReason.Cooldown,
                    $"指令冷却中（{_options.CooldownSeconds}s）。");

            // 4) 分钟配额（窗口滚动：起始时刻落后超过 60s 则重开窗口）。
            if (ctx.NowUnixMs - state.QuotaWindowStartUnixMs >= 60_000L)
            {
                state.QuotaWindowStartUnixMs = ctx.NowUnixMs;
                state.QuotaWindowUsed = 0;
            }

            if (state.QuotaWindowUsed >= _options.QuotaPerMinute)
                return ScriptDecision.Deny(ScriptRejectReason.QuotaExhausted,
                    $"每分钟配额已用尽（{_options.QuotaPerMinute}）。");

            // 5) 参数合法性 + 模板白名单（数值一律服务端解析，客户端只给标识）。
            //    顺序刻意是"参数先、目录后"：半径/TTL 写错时，操作者需要看到"半径非法"而不是
            //    "模板不存在"——后者会让人去查错的东西（目录还没登记的类别尤其如此）。
            var templateId = 0;
            if (ticket.Op == ScriptOp.Spawn)
            {
                if (ticket.Count < 1)
                    return ScriptDecision.Deny(ScriptRejectReason.InvalidArgument, "数量必须 ≥ 1。");

                if (ticket.Count > 1 && (state.Flags & (uint)ScriptPermissionFlags.MultiEntity) == 0)
                    return ScriptDecision.Deny(ScriptRejectReason.InsufficientPermission,
                        "单次生成多个实体需要 MultiEntity 权限位。");

                if (ticket.TtlSeconds < 0)
                    return ScriptDecision.Deny(ScriptRejectReason.InvalidArgument, "TTL 不得为负。");

                if (_options.MaxSpawnTtlSeconds > 0 && ticket.TtlSeconds > _options.MaxSpawnTtlSeconds)
                    return ScriptDecision.Deny(ScriptRejectReason.InvalidArgument,
                        $"TTL 超过上限 {_options.MaxSpawnTtlSeconds}s。");

                if (ticket.Kind == ScriptedKind.TriggerZone)
                {
                    if (ticket.Radius <= 0f || ticket.Radius > _options.MaxTriggerRadiusMetres)
                        return ScriptDecision.Deny(ScriptRejectReason.InvalidArgument,
                            $"触发区域半径需在 (0, {_options.MaxTriggerRadiusMetres}] 米。");

                    // 区域必须有限寿命：否则"隐形刷怪机器"可永久驻留。
                    if (ticket.TtlSeconds <= 0 || ticket.TtlSeconds > _options.MaxTriggerTtlSeconds)
                        return ScriptDecision.Deny(ScriptRejectReason.InvalidArgument,
                            $"触发区域 TTL 需在 (0, {_options.MaxTriggerTtlSeconds}] 秒。");

                    if (ticket.TriggerMode == ScriptTriggerMode.Repeat && ticket.CooldownSeconds <= 0)
                        return ScriptDecision.Deny(ScriptRejectReason.InvalidArgument,
                            "Repeat 模式的冷却秒数不得为 0（边界横跳防护）。");

                    if (ctx.ShardTriggerZoneCount >= _options.MaxTriggerZonesPerShard)
                        return ScriptDecision.Deny(ScriptRejectReason.EntityBudgetExceeded,
                            $"该 shard 触发区域已达上限 {_options.MaxTriggerZonesPerShard}。");
                }

                if (!_catalog.TryResolve(ticket.Kind, ticket.TemplateKey, out templateId))
                    return ScriptDecision.Deny(ScriptRejectReason.UnknownTemplate,
                        $"模板不在白名单目录内：{ticket.TemplateKey}");
            }

            var count = Math.Clamp(ticket.Op == ScriptOp.Spawn ? ticket.Count : 1,
                1, _options.MaxCountPerRequest);

            // 6) 目标点：缺省取操作者权威坐标；否则校验距离与 shard 归属。
            float x = ctx.OperatorX, y = ctx.OperatorY, z = ctx.OperatorZ;
            if (ticket.Op == ScriptOp.Spawn && ticket.HasPosition)
            {
                x = ticket.X;
                y = ticket.Y;
                z = ticket.Z;

                var canBypass = (state.Flags & (uint)ScriptPermissionFlags.BypassDist) != 0;
                if (!canBypass)
                {
                    var maxDist = _options.MaxDistanceMetres;
                    var dx = x - ctx.OperatorX;
                    var dy = y - ctx.OperatorY;
                    var dz = z - ctx.OperatorZ;
                    // 平方距离比较：热路径避免开方，且与既有 AI 半径判定口径一致。
                    if ((dx * dx + dy * dy + dz * dz) > maxDist * maxDist)
                        return ScriptDecision.Deny(ScriptRejectReason.TooFar,
                            $"目标点超出允许距离 {maxDist} 米。");
                }
            }

            var targetShard = ctx.OperatorShardId;
            // 分片实体上限**不受 BypassDist 影响**：那一位的语义是"允许超距"，不是"允许打爆带宽/AOI"。
            // 触发区域上限同样是硬顶（见上面的 TriggerZone 分支），两处口径必须一致，
            // 否则一个取名只管距离的位悄悄关掉了防洪闸。
            if (ticket.Op == ScriptOp.Spawn &&
                ctx.ShardScriptedEntityCount >= _options.MaxScriptedEntitiesPerShard)
                return ScriptDecision.Deny(ScriptRejectReason.EntityBudgetExceeded,
                    $"该 shard 脚本实体已达上限 {_options.MaxScriptedEntitiesPerShard}。");

            if (ticket.Op == ScriptOp.Despawn &&
                ticket.TargetEntityId == 0)
                return ScriptDecision.Deny(ScriptRejectReason.InvalidArgument, "缺少目标实体 ID。");

            // 7) 全部通过：扣预算、签发令牌。
            state.LastAcceptedUnixMs = ctx.NowUnixMs;
            state.QuotaWindowUsed++;
            state.IssuedTokenCounter++;

            var token = new ScriptGrantToken
            {
                // Nonce = 时间戳 + 单调计数：同毫秒内也不重复，且不依赖外部随机源（可单测）。
                Nonce = ctx.NowUnixMs * 1000L + (state.IssuedTokenCounter % 1000L),
                OperatorCharacterId = ticket.OperatorCharacterId,
                RequestId = ticket.RequestId,
                Op = ticket.Op,
                Kind = ticket.Kind,
                TemplateId = templateId,
                TargetShardId = targetShard,
                X = x,
                Y = y,
                Z = z,
                Count = count,
                TtlSeconds = ticket.TtlSeconds,
                Radius = ticket.Radius,
                TriggerMode = ticket.TriggerMode,
                CooldownSeconds = ticket.CooldownSeconds,
                TargetEntityId = ticket.TargetEntityId,
            };

            ScriptGrantTokenSigner.Sign(token, signingKey, ctx.NowUnixMs, _options.TokenTtlSeconds);

            return ScriptDecision.Allow(token, _options.QuotaPerMinute - state.QuotaWindowUsed, state.Flags);
        }

        /// <summary>
        /// 把所需权限位翻成操作者看得懂的话。<b>拒绝原因要能指向下一步动作</b>：
        /// 只给一个 64 或 0x40，运营只能回来问；给名字，他就能直接去 /grant。
        /// </summary>
        public static string DescribeFlag(uint required) => required switch
        {
            (uint)ScriptPermissionFlags.SpawnMonster => "SpawnMonster(0x1, 生成怪物)",
            (uint)ScriptPermissionFlags.SpawnNpc => "SpawnNpc(0x2, 生成NPC)",
            (uint)ScriptPermissionFlags.FireEvent => "FireEvent(0x4, 表现型事件)",
            (uint)ScriptPermissionFlags.SpawnEvent => "SpawnEvent(0x8, 玩法型事件)",
            (uint)ScriptPermissionFlags.PlaceTrigger => "PlaceTrigger(0x10, 触发区域)",
            (uint)ScriptPermissionFlags.DespawnOwned => "DespawnOwned(0x20, 清除本人生成物)",
            (uint)ScriptPermissionFlags.DespawnAny => "DespawnAny(0x40, 清除他人生成物)",
            (uint)ScriptPermissionFlags.MultiEntity => "MultiEntity(0x80, 多实体)",
            (uint)ScriptPermissionFlags.BypassDist => "BypassDist(0x100, 超距)",
            (uint)ScriptPermissionFlags.Query => "Query(0x400, 查询)",
            (uint)ScriptPermissionFlags.GmOps => "GmOps(0x800, GM运维)",
            _ => $"0x{required:X8}",
        };

        /// <summary>
        /// 操作 + 类别 → 所需权限位。<b>映射集中在此处</b>，避免网关/DS/Grain 各写一份判定而口径漂移。
        /// </summary>
        public static uint RequiredFlagFor(ScriptOp op, ScriptedKind kind) => op switch
        {
            // 按实体 ID 清除**可能是别人的生成物**，所以要求 DespawnAny；
            // "清我自己的"走 DespawnMine，只要 DespawnOwned。
            // 两档分开是必要的：否则任何拿到 DespawnOwned 的运营都能清空别人摆的内容。
            ScriptOp.Despawn => (uint)ScriptPermissionFlags.DespawnAny,
            ScriptOp.DespawnMine => (uint)ScriptPermissionFlags.DespawnOwned,
            ScriptOp.Query => (uint)ScriptPermissionFlags.Query,
            ScriptOp.Spawn => kind switch
            {
                ScriptedKind.Monster => (uint)ScriptPermissionFlags.SpawnMonster,
                ScriptedKind.Npc => (uint)ScriptPermissionFlags.SpawnNpc,
                // 事件档默认按"会建实体"要求 SpawnEvent；纯表现型事件由 Evaluate 就地换成 FireEvent
                // （形态由服务端目录决定，见那里）。
                ScriptedKind.Event => (uint)ScriptPermissionFlags.SpawnEvent,
                ScriptedKind.TriggerZone => (uint)ScriptPermissionFlags.PlaceTrigger,
                _ => uint.MaxValue, // 未知类别：要求"全位"，实际不可能满足 → 拒绝
            },
            _ => uint.MaxValue,
        };
    }

    /// <summary>模板白名单解析：客户端给标识，服务端给数值。</summary>
    public interface IScriptTemplateCatalog
    {
        /// <summary>把 <paramref name="key"/>（模板名或数字 ID 字符串）解析为已登记模板 ID。</summary>
        bool TryResolve(ScriptedKind kind, string? key, out int templateId);

        /// <summary>
        /// 该模板是否**只放表现、不往世界里生实体**（决定它要 <c>FireEvent</c> 还是 <c>SpawnEvent</c> 位）。
        /// <para>默认 <c>false</c>：不认识的目录一律按"会建实体"的较高档要求，权限不会因为目录缺失而变松。</para>
        /// </summary>
        bool IsPresentationOnly(ScriptedKind kind, int templateId) => false;
    }

    /// <summary>
    /// 默认白名单目录：怪物与玩法型事件共用 <see cref="MonsterTemplateCatalog"/>，
    /// NPC 走 <see cref="NpcTemplateCatalog"/>；触发区域在 P4 登记目录前一律拒绝（fail-closed）。
    /// </summary>
    public sealed class DefaultScriptTemplateCatalog : IScriptTemplateCatalog
    {
        /// <summary>共享实例。</summary>
        public static readonly DefaultScriptTemplateCatalog Instance = new();

        /// <inheritdoc />
        public bool IsPresentationOnly(ScriptedKind kind, int templateId)
            => kind == ScriptedKind.Event
               && WorldEventCatalog.TryGetRegistered(templateId, out var info)
               && info.Mode == WorldEventMode.Presentation;

        /// <inheritdoc />
        public bool TryResolve(ScriptedKind kind, string? key, out int templateId)
        {
            templateId = 0;
            if (string.IsNullOrWhiteSpace(key)) return false;

            // 玩法型事件 = "带 TTL 的怪群"，与怪物共用同一张数值/显示目录。
            // 特殊事件有独立目录：形态（刷怪群 / 只放表现）由服务端配置决定。
            if (kind == ScriptedKind.Event)
            {
                if (int.TryParse(key, out var byIdEvent))
                    return WorldEventCatalog.TryGetRegistered(byIdEvent, out var ev1)
                           && Assign(out templateId, ev1.TemplateId);

                return WorldEventCatalog.TryFindByName(key!, out var ev2)
                       && Assign(out templateId, ev2.TemplateId);
            }

            // 触发区域的绑定对象就是"踏入时召唤什么"，与怪物共用同一张数值/显示目录。
            if (kind == ScriptedKind.Monster || kind == ScriptedKind.TriggerZone)
            {
                if (int.TryParse(key, out var byIdMonster))
                    return MonsterTemplateCatalog.TryGetRegistered(byIdMonster, out var m1)
                           && Assign(out templateId, m1.TemplateId);

                return MonsterTemplateCatalog.TryFindByName(key!, out var m2)
                       && Assign(out templateId, m2.TemplateId);
            }

            if (kind == ScriptedKind.Npc)
            {
                if (int.TryParse(key, out var byIdNpc))
                    return NpcTemplateCatalog.TryGetRegistered(byIdNpc, out var n1)
                           && Assign(out templateId, n1.TemplateId);

                return NpcTemplateCatalog.TryFindByName(key!, out var n2)
                       && Assign(out templateId, n2.TemplateId);
            }

            // 其余类别尚未登记目录：不给未登记内容开门。
            return false;
        }

        private static bool Assign(out int target, int value)
        {
            target = value;
            return true;
        }
    }

    /// <summary>裁决结果（内部结构，由 Grain 转成 <c>GmScriptVerdict</c>）。</summary>
    public sealed class ScriptDecision
    {
        /// <summary>是否允许。</summary>
        public bool Allowed { get; private init; }

        /// <summary>拒绝原因。</summary>
        public ScriptRejectReason Reason { get; private init; }

        /// <summary>说明文本。</summary>
        public string Message { get; private init; } = string.Empty;

        /// <summary>通过时已签名的令牌。</summary>
        public ScriptGrantToken? Token { get; private init; }

        /// <summary>剩余配额。</summary>
        public int RemainingBudget { get; private init; }

        /// <summary>生效权限位。</summary>
        public uint EffectiveFlags { get; private init; }

        /// <summary>构造拒绝结果。</summary>
        public static ScriptDecision Deny(ScriptRejectReason reason, string message) => new()
        {
            Allowed = false,
            Reason = reason,
            Message = message,
        };

        /// <summary>构造允许结果。</summary>
        public static ScriptDecision Allow(ScriptGrantToken token, int remainingBudget, uint flags) => new()
        {
            Allowed = true,
            Reason = ScriptRejectReason.None,
            Message = "已受理。",
            Token = token,
            RemainingBudget = remainingBudget,
            EffectiveFlags = flags,
        };
    }
}
