using System.ComponentModel.DataAnnotations;

namespace Horizon.Game.Gateway.Configuration
{
    /// <summary>
    /// 网关配置选项
    /// </summary>
    public class GatewayOptions
    {
        /// <summary>
        /// 网关名称
        /// </summary>
        [Required]
        public string Name { get; set; } = "混沌世界游戏网关";

        /// <summary>
        /// 网关ID
        /// </summary>
        [Required]
        public string GatewayId { get; set; } = "TYMYD-Gateway-001";

        /// <summary>
        /// 集群ID
        /// </summary>
        public string ClusterId { get; set; } = "TYMYD-Cluster-001";

        /// <summary>
        /// Redis连接字符串
        /// </summary>
        public string RedisConnectionString { get; set; } = "localhost:6379";

        /// <summary>
        /// 服务器区域
        /// </summary>
        public string Region { get; set; } = "华东";

        /// <summary>
        /// 阶段 6.3 P1⑥+ — 空 region DS 严格模式（防旧注册盲路由）。默认 <c>false</c> = 宽松（向后兼容）。
        /// <para>
        /// false（宽松）：未声明 region 的 DS 视为不限区，可服务任意区域网关——灰度期兼容旧注册；
        /// true（严格）：未声明 region 的 DS 仅服务空 region 网关（<c>Region</c> 为空的网关不设区、
        /// 不设过滤），有 region 的网关绝不明路由到空区 DS（跨区防护同样按精确匹配拒绝）。
        /// 部署收敛（全部 DS 上架已声明 region）后开启，防止灰度期遗留的旧注册被跨区盲路由。
        /// 由数据面逐调用读取（IOptionsMonitor），支持不重启网关热切换。
        /// </para>
        /// </summary>
        public bool StrictEmptyRegionMode { get; set; } = false;

        /// <summary>
        /// 最大并发连接数
        /// </summary>
        [Range(1, 100000)]
        public int MaxConnections { get; set; } = 10000;

        /// <summary>
        /// 连接超时时间（秒）
        /// </summary>
        [Range(10, 3600)]
        public int ConnectionTimeout { get; set; } = 300;

        /// <summary>
        /// 心跳间隔（秒）
        /// </summary>
        [Range(10, 300)]
        public int HeartbeatInterval { get; set; } = 30;

        /// <summary>
        /// 是否启用消息压缩
        /// </summary>
        public bool EnableCompression { get; set; } = true;

        /// <summary>
        /// 是否启用消息加密
        /// </summary>
        public bool EnableEncryption { get; set; } = true;

        /// <summary>
        /// 消息缓冲区大小
        /// </summary>
        [Range(1024, 1048576)]
        public int BufferSize { get; set; } = 8192;

        /// <summary>
        /// 统计信息更新间隔（秒）
        /// </summary>
        [Range(1, 300)]
        public int StatisticsInterval { get; set; } = 60;

        /// <summary>
        /// 是否启用详细日志
        /// </summary>
        public bool EnableVerboseLogging { get; set; } = false;

        /// <summary>
        /// 是否在验证鉴权令牌时检查客户端机器ID与令牌中记录的机器ID是否一致。
        /// 通行证级共享令牌（WebApi PassportImTokenStore 签发，多端复用）不绑定机器ID，
        /// 默认关闭；仅在使用"按机器签发令牌"的特殊部署场景下才应设为 true。
        /// </summary>
        public bool ValidateTokenMachineId { get; set; } = false;

        /// <summary>
        /// 对外公布的网关 IP 或主机名（写入 Redis 供客户端通过 WebApi 发现）。
        /// 为空时回退到 Network:IpAddress；若仍为 0.0.0.0 / 空，则使用本机主机名。
        /// </summary>
        public string PublicIpAddress { get; set; } = string.Empty;

        /// <summary>
        /// 对外公布的网关端口（写入 Redis）。为 0 时回退到 Network:TcpPort。
        /// </summary>
        public int PublicPort { get; set; } = 0;

        /// <summary>
        /// 网关注册心跳间隔（秒）。
        /// </summary>
        [Range(5, 300)]
        public int RegistryHeartbeatIntervalSeconds { get; set; } = 30;

        /// <summary>
        /// 灰度开关（P6-a）：启用后，网关将把出站 <c>SyncPacket</c> 路由到新版
        /// <c>SyncDispatcher</c>（走 ZoneShard→Session fanout 路径），而不是直接广播到 AOI 旧流。
        /// 默认 <c>true</c>；新版 fanout 链路已稳定，作为默认同步路径。
        /// </summary>
        public bool UseSyncPacketDispatch { get; set; } = true;

        /// <summary>
        /// Task 18：SyncDispatcher 工作线程数，默认 1（串行）。调高可并行处理 fanout 事件。
        /// 多 worker 模式下 Channel 多读保证每个事件只被一个 worker 取走，无丢失无重复。
        /// </summary>
        [Range(1, 64)]
        public int MaxDispatcherWorkers { get; set; } = 1;

        /// <summary>
        /// 灰度开关（Phase 2 安全补全）：启用后，网关将对上行的交互意图/场景对象交互/订阅变更包
        /// 执行会话绑定校验（验证 characterId 是否与当前 TCP 连接绑定一致），
        /// 防止身份伪造。默认 <c>false</c>（关闭），验证通过后开启。
        /// </summary>
        public bool EnableSessionBindingValidation { get; set; } = false;

        /// <summary>
        /// ZoneShard 分片数量（Phase 3.3 统一 Shard 路由）。默认 1（单 Shard 模式，向后兼容）。
        /// 调高后需同步部署对应数量的 ZoneShardGrain 实例。
        /// </summary>
        [Range(1, 64)]
        public int ShardCount { get; set; } = 1;

        /// <summary>
        /// 阶段 4.3 — 跨 Shard 边界双重订阅（2D X/Z 网格划分）：每个 Shard 沿 X 轴覆盖的 chunk 数（16m/chunk）。<br/>
        /// 默认 <c>0</c> = 禁用空间分片（玩家仅订阅主 Shard，行为与阶段 4.2 之前完全一致）。<br/>
        /// 调大即启用：玩家视口（AOI 半径 28 chunk ≈ 448m）跨过 Shard 边界时，网关向相邻 Shard
        /// 也建立 AOI 订阅，实现跨 Shard 双向可见（无需实体副本迁移）；角色移出边界自动退订。<br/>
        /// 2D 网格启用需 <c>ChunksPerShardX</c>/<c>ChunksPerShardZ</c>/<c>ShardsPerRowX</c> 同时 &gt; 0
        /// （任一为 0 即禁用）；网格按行主序映射 Shard ID：id(gx, gz) = gz*ShardsPerRowX + gx。
        /// </summary>
        [Range(0, 1_000_000)]
        public int ChunksPerShardX { get; set; } = 0;

        /// <summary>
        /// 阶段 4.3 — 2D 网格划分：每个 Shard 沿 Z 轴覆盖的 chunk 数（16m/chunk）。默认 <c>0</c> = 禁用空间分片。
        /// </summary>
        [Range(0, 1_000_000)]
        public int ChunksPerShardZ { get; set; } = 0;

        /// <summary>
        /// 阶段 4.3 — 2D 网格划分：网格每行（沿 X 轴）的 Shard 数；行主序 id(gx, gz) = gz*ShardsPerRowX + gx。<br/>
        /// 默认 <c>0</c> = 禁用空间分片。<c>ShardsPerRowX &gt;= ShardCount</c> 时退化为单行（1D 带状划分）。
        /// </summary>
        [Range(0, 64)]
        public int ShardsPerRowX { get; set; } = 0;

        /// <summary>
        /// 阶段 2 灰度开关：是否启用 UE5 DS 模拟通道（UseDS）。默认 <c>false</c>。
        /// <para>
        /// false → 网关完全走现有 Orleans 路径（零行为变化）；
        /// true → 网关把 DS 模拟范围内的上行包（Handshake/Input/ReconnectResume）按 shard
        /// 转发到 <see cref="Services.DSRegistry"/> 中已注册的 UE5 DS；无可用 DS / 转发失败
        /// 自动回退 Orleans 路径（灰度期双轨，任一时点可回切）。
        /// </para>
        /// </summary>
        public bool UseDS { get; set; } = false;

        /// <summary>
        /// MMORPG 开发计划 M2.1 —— DS 上行控制面监听端口。默认 <c>0</c> = **不监听**（零行为变化）。
        /// <para>
        /// 该端口承接 DS → 网关方向的**单条复用 TCP 连接**，承载两类消息：
        /// 控制信令（注册 / 心跳 / 摘除）与权威快照回传（<c>DsSnapshotBatch</c>）。
        /// 在此之前 <see cref="Services.DSRegistry"/> 没有任何外部调用方（缺口 G3），
        /// 且 DS 快照没有回到客户端分发管线的入口（缺口 G4）。
        /// </para>
        /// <para>
        /// 仅在 <see cref="UseDS"/> 为 true 且端口 &gt; 0 时启动监听；关闭开关即停用，
        /// 既有 Orleans 路径零影响（可回切纪律）。
        /// </para>
        /// </summary>
        [Range(0, 65535)]
        public int DsUplinkPort { get; set; } = 0;

        /// <summary>
        /// DS 控制面共享令牌（写入请求体 <c>Token</c> 字段）。空串 = 不校验（仅适用于
        /// DS 与网关同机/可信内网的部署）。生产部署应配置为随机长串。
        /// </summary>
        public string DsControlToken { get; set; } = string.Empty;

        /// <summary>
        /// DS 控制面监听地址。默认 <c>0.0.0.0</c>；仅本机调试时建议 <c>127.0.0.1</c>。
        /// </summary>
        public string DsUplinkListenAddress { get; set; } = "0.0.0.0";

        /// <summary>
        /// 阶段 0.6 — 协议严格版本匹配模式。默认 <c>false</c> = 宽松（开发/过渡期，向后兼容）。
        /// <para>
        /// false（宽松）：客户端 <see cref="SyncProtocolVersion"/> 低于 <c>MinimumSupported</c> 拒绝（强制更新），
        /// 否则允许连接并记录警告（过渡期软校验，旧客户端不中断）；
        /// true（严格，生产）：任何与 <c>Current</c> 不一致的版本（含过渡期版本）都拒绝连接——
        /// 协议字段布局/语义变更时旧客户端强行连接会导致字段错位/行为异常，生产应强制版本一致。
        /// 由 <see cref="Horizon.Game.Core.Handlers.StrictProtocolVersionPolicy"/> 实现，
        /// 握手与逐包路径（<see cref="Horizon.Game.Core.Handlers.SyncPacketHandler"/>）均生效。
        /// </para>
        /// </summary>
        public bool RequireStrictVersionMatch { get; set; } = false;

        /// <summary>
        /// 阶段 6.3 P2 — 自动回滚门配置（DsRollback 节）。
        /// 网关进程内采样 <c>horizon.sync.ds.route.*</c> 指标，按窗口判定 DS 通道健康度：
        /// 回退率或延迟连续超阈 → 自动回切 UseDS（内存 override，冷却后连续健康窗口自愈恢复）。
        /// </summary>
        public class DsRollbackOptions
        {
            /// <summary>采样窗口时长（秒）。默认 30s（与 DSRegistry 心跳 30s 同周期，便于对齐观察）。</summary>
            [Range(5, 300)]
            public int WindowSeconds { get; set; } = 30;

            /// <summary>窗口最小路由尝试数：低于该值视为「流量不足」，窗口不参与判定（防零流量/低流量误报）。默认 10。</summary>
            [Range(1, 1_000_000)]
            public int MinAttemptsPerWindow { get; set; } = 10;

            /// <summary>回退率阈值（0–1）：窗口内 fallback/attempts 超过即视为坏窗口。默认 0.5。</summary>
            [Range(0.0, 1.0)]
            public double FallbackRateThreshold { get; set; } = 0.5;

            /// <summary>DS 转发平均延迟阈值（毫秒）：窗口内均值超过即视为坏窗口（长时间退化信号）。默认 1000ms。</summary>
            [Range(1, 60_000)]
            public double LatencyMsThreshold { get; set; } = 1000;

            /// <summary>
            /// 修正率信号阈值（每窗口修正数）：窗口内跨进程汇聚的修正计数 ≥ 该值即视为坏窗口
            /// （服务端权威质量信号，独立于网关路由量——Silo 权威模拟对所有会话产生修正）。
            /// <para>默认 0 = 禁用（保持既有「回退率 + 延迟」双信号；修正率跨进程汇聚见 OPS_RUNBOOK §5.1）。
            /// 启用须设高于正常基线的值（修正率高企 = 权威模拟持续与客户端预测对抗，典型为滞后/去同步）。</para>
            /// </summary>
            [Range(0, long.MaxValue)]
            public long CorrectionsPerWindowThreshold { get; set; } = 0;

            /// <summary>触发回滚所需连续坏窗口数（防单窗抖动误触发）。默认 2。</summary>
            [Range(1, 100)]
            public int ConsecutiveBadWindowsToTrigger { get; set; } = 2;

            /// <summary>自愈恢复所需连续健康窗口数（冷却到期后，防恢复瞬间再触发振荡）。默认 3。</summary>
            [Range(1, 100)]
            public int ConsecutiveHealthyWindowsToRecover { get; set; } = 3;

            /// <summary>回滚 override 生效时长（秒）：触发后该时长内强制保持回切，到期后才允许评估恢复。默认 60s。</summary>
            [Range(1, 3600)]
            public int OverrideCooldownSeconds { get; set; } = 60;
        }

        /// <summary>阶段 6.3 P2 — 自动回滚门配置（DsRollback 节；未配置时用类内默认值）。</summary>
        public DsRollbackOptions? DsRollback { get; set; }

        /// <summary>
        /// 阶段 6.3 — 按比例灰度放量（会话一致性哈希）。取值范围 0–100（百分比）。默认 <c>100</c>（全量）。
        /// <para>
        /// 100 → 所有会话都进入 DS 通道（与 <c>UseDS=true</c> 的既有全量语义完全一致，向后兼容）；
        /// 0 → 所有会话排除（DS 通道形同关闭，回退 Orleans）；0–99 → 按 <c>characterId</c> 的
        /// 一致性哈希（Knuth 乘法散列，均匀分散连续 ID）决定会话是否在灰度群组内。
        /// 同一 characterId 的判定恒定（会话级粘滞，不会逐包在 DS/Orleans 间摇摆）；
        /// 比例上调只会把群组外的会话加入（单调），群组内会话不因调档切换宿主。
        /// 灰度放量路径：<c>UseDS=true</c> + 从 <c>10%</c> 逐步上调至 <c>100%</c>，每档观察
        /// <c>horizon.sync.ds.route.*</c> 与 <c>horizon.sync.corrections</c> 指标（OPS_RUNBOOK §5.3）。
        /// </para>
        /// </summary>
        [Range(0, 100)]
        public int DsGrayRatio { get; set; } = 100;

        /// <summary>
        /// Sprint 4 — DS 下行 fanout Channel 容量。默认 <c>65536</c>，足以支撑百万级会话的峰值并发。
        /// <para>
        /// 原固定 8192 在高并发下易触发反压丢弃（DropOldest），调大后可容纳更多瞬态突发。
        /// 生产应根据实际连接数与 tick 产出率调整：推荐 <c>会话数 × 8</c> 以上。
        /// </para>
        /// </summary>
        [Range(1024, 1048576)]
        public int DsDownlinkChannelCapacity { get; set; } = 65536;

        /// <summary>
        /// Sprint 2 — DS 上行路由是否使用持久连接池（替代每包新建 TCP）。默认 <c>false</c> = 旧路径（每包新建连接，行为与本开关引入前完全一致）。
        /// <para>
        /// 通过 <see cref="Microsoft.Extensions.Options.IOptionsMonitor{TOptions}"/> 热切换：关闭后立即回退到每包新建模式，
        /// 开启后 <see cref="Services.DsSyncDataPlane"/> 改为向 <see cref="Services.DsTcpConnectionPool"/> 租借复用长连接，
        /// 消除 60Hz × N 玩家场景下每包三次握手 + TIME_WAIT 的延迟瓶颈（DS 侧 <c>DsDataPlaneHost</c> 已支持长连接循环读帧，无需任何改动）。
        /// 池满载/新建失败时仍自动回退一次性直连（不丢包），熔断触发时联动清空该 DS 的池连接。
        /// </para>
        /// </summary>
        public bool DsRouteUsePersistentConnection { get; set; } = false;
    }
}