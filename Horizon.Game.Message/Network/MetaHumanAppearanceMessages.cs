using MemoryPack;
using Orleans;
using Horizon.Game.Message.Enums;
using System;

namespace Horizon.Game.Message.Network
{
    /// <summary>
    /// 数字人外观差量存档的传输载体。
    ///
    /// 设计要点：外观对服务端是**不透明 blob + 自描述版本**。服务端不参与求解、不认识具体参数名，
    /// 因此加一根新滑杆不需要同步改服务端类型；同时也不会有「服务端看不懂新字段就把它丢掉」的静默失效。
    /// blob 的产物与解释规则在客户端的 MetaHuman 插件里（MetaHumanLook.ToBytes）。
    /// </summary>
    [MemoryPackable]
    [GenerateSerializer]
    public partial class MetaHumanLookPayload
    {
        /// <summary>合成模板 Id（例如 "metahuman.face_archetype+metahuman.body_base"）。换模板要整体重建，不做字段平移。</summary>
        [MemoryPackOrder(0)]
        [Id(0)]
        public string TemplateId { get; set; } = "";

        /// <summary>导出时的模板版本，回读时用来判定兼容性。</summary>
        [MemoryPackOrder(1)]
        [Id(1)]
        public string TemplateVersion { get; set; } = "";

        /// <summary>导出时的参数槽位总数：回读时用来发现槽位漂移（旧存档按错位槽位解释最阴）。
        [MemoryPackOrder(2)]
        [Id(2)]
        public int TemplateParamCount { get; set; }

        /// <summary>差量条目数。</summary>
        [MemoryPackOrder(3)]
        [Id(3)]
        public int EntryCount { get; set; }

        /// <summary>MetaHumanLook.ToBytes() 的产物（自带内容校验和）。</summary>
        [MemoryPackOrder(4)]
        [Id(4)]
        public byte[] Blob { get; set; } = new byte[0];

        /// <summary>传输层校验：对 Blob 字节的 FNV-1a。服务端用同一算法**另算一遍**再比，不信客户端自述。</summary>
        [MemoryPackOrder(5)]
        [Id(5)]
        public uint BlobChecksum { get; set; }

        /// <summary>发布标记。为 true 之后服务端拒绝再改（对应「保存→发布后不可回溯」）。</summary>
        [MemoryPackOrder(6)]
        [Id(6)]
        public bool Published { get; set; }

        /// <summary>客户端导出时刻（UTC ticks），只用于展示与对账，不参与判定。</summary>
        [MemoryPackOrder(7)]
        [Id(7)]
        public long SavedAtUtcTicks { get; set; }
    }

    /// <summary>
    /// 外观 blob 的传输层校验和。算法与客户端 <c>MetaHumanLookBlobHash</c> 必须逐字一致：
    /// 两边各算一遍再比，才能把「传一半就断」与「客户端自己算错」区分开。
    /// </summary>
    public static class MetaHumanLookBlobHash
    {
        public static uint Compute(byte[] blob)
        {
            // FNV-1a 32 位：offset basis 2166136261，prime 16777619
            const uint offset = 2166136261;
            const uint prime = 16777619;
            uint hash = offset;
            if (blob == null)
            {
                return hash;
            }

            for (int i = 0; i < blob.Length; i++)
            {
                hash ^= blob[i];
                hash *= prime;
            }

            return hash;
        }
    }

    /// <summary>玩家提交（或发布）外观。</summary>
    [MemoryPackable]
    [GenerateSerializer]
    [NetworkMessage(246, Enums.ServiceType.Game)]
    public partial class UpdateCharacterAppearanceRequest : MessageUnion, INetworkMessage
    {
        public UpdateCharacterAppearanceRequest()
        {
            // Type/ServiceType 由 MessageUnion 基类负责序列化（order 254/255），
            // 这里只赋值不再声明一遍：两边各存一份会让回执路由看的是基类那份、判的是子类那份。
            Type = MessageType.UpdateCharacterAppearance;
            ServiceType = ServiceType.Game;
        }

        [MemoryPackOrder(0)]
        [Id(0)]
        public ulong CharacterId { get; set; }

        [MemoryPackOrder(1)]
        [Id(1)]
        public MetaHumanLookPayload Look { get; set; } = new();
    }

    [MemoryPackable]
    [GenerateSerializer]
    [NetworkMessage(247, Enums.ServiceType.Game)]
    public partial class UpdateCharacterAppearanceResponse : MessageUnion, INetworkMessage
    {
        public UpdateCharacterAppearanceResponse()
        {
            Type = MessageType.UpdateCharacterAppearanceResult;
            ServiceType = ServiceType.Game;
        }

        [MemoryPackOrder(0)]
        [Id(0)]
        public bool Success { get; set; }

        /// <summary>失败原因（成功时为空）。服务端判定：已发布、模板不一致、校验和不符、尺寸超限。</summary>
        [MemoryPackOrder(1)]
        [Id(1)]
        public string Error { get; set; } = "";

        /// <summary>服务端实际存下的校验和（0 表示没有存档）。客户端与本机那份比对即可确认写没写进去。</summary>
        [MemoryPackOrder(2)]
        [Id(2)]
        public uint StoredChecksum { get; set; }

        /// <summary>服务端记录的外观版本（自增），用来判断「我收到的这条比本机新还是旧」。</summary>
        [MemoryPackOrder(3)]
        [Id(3)]
        public int StoredRevision { get; set; }

        /// <summary>
        /// 服务端告知：这份存档已经封存（已发布），任何后续提交都会被拒。
        /// 客户端要据此把本机会话也封起来，而不是让玩家继续编辑、一路撞到服务端拒绝。
        /// 用独立状态位而不是判 <see cref="Error"/> 的文案：文案是要给人看的，
        /// 换一次措辞就把这条判定改反（展示文本不能当身份用）。
        /// </summary>
        [MemoryPackOrder(4)]
        [Id(4)]
        public bool AppearanceSealed { get; set; }

        /// <summary>
        /// 写入成功、但**没落进权威副本**（SQL 里找不到这个角色的行）。
        /// 只记日志不够：状态确实存住了，但那依赖 Redis 不被淘汰 ——
        /// 而"发布后永久生效"承诺的恰恰是淘汰之后还得在。
        /// 所以这件事要有机器可读的一格，客户端与工装都能据此判"这条存档其实不稳"。
        /// （真集群那轮就是靠 `character_id=40` 这行不存在才发现：对不存在的角色也能全绿。）
        /// </summary>
        [MemoryPackOrder(5)]
        [Id(5)]
        public bool AuthorityCopyMissing { get; set; }
    }

    /// <summary>
    /// 拉取某个角色已落库的外观：远端玩家进场后要靠它把别人捏的脸贴到自己屏幕上。
    /// 玩家实体 Id 就是角色 Id（DsWorldSession 建档时 EntityId=CharacterId），所以远端 Actor 不需要额外映射。
    /// </summary>
    [MemoryPackable]
    [GenerateSerializer]
    [NetworkMessage(248, Enums.ServiceType.Game)]
    public partial class GetCharacterAppearanceRequest : MessageUnion, INetworkMessage
    {
        public GetCharacterAppearanceRequest()
        {
            Type = MessageType.GetCharacterAppearance;
            ServiceType = ServiceType.Game;
        }

        [MemoryPackOrder(0)]
        [Id(0)]
        public ulong CharacterId { get; set; }
    }

    /// <summary>
    /// 外观读取回执。<see cref="Look"/> 为空且 <see cref="Success"/> 为真表示该角色从未自定义过，
    /// 客户端应回落到模板默认脸，而不是当成失败重试。
    /// </summary>
    [MemoryPackable]
    [GenerateSerializer]
    [NetworkMessage(249, Enums.ServiceType.Game)]
    public partial class GetCharacterAppearanceResponse : MessageUnion, INetworkMessage
    {
        public GetCharacterAppearanceResponse()
        {
            Type = MessageType.GetCharacterAppearanceResult;
            ServiceType = ServiceType.Game;
        }

        [MemoryPackOrder(0)]
        [Id(0)]
        public ulong CharacterId { get; set; }

        [MemoryPackOrder(1)]
        [Id(1)]
        public bool Success { get; set; }

        [MemoryPackOrder(2)]
        [Id(2)]
        public string Error { get; set; } = "";

        [MemoryPackOrder(3)]
        [Id(3)]
        public MetaHumanLookPayload Look { get; set; }

        /// <summary>创角离散发色下标（AppearanceInfo.HairColor）；远端头发用它还原与本人一致的颜色。</summary>
        [MemoryPackOrder(4)]
        [Id(4)]
        public int HairColor { get; set; }
    }
}
