namespace Horizon.Game.ECS.Arch.Components;

/// <summary>
/// 网络实体标识组件：区分本地玩家与远程实体。
/// </summary>
public struct NetworkIdentityComponent
{
    /// <summary>服务器分配的实体唯一 ID。</summary>
    public ulong EntityId;

    /// <summary>是否为本地玩家所控制的实体。</summary>
    public bool IsLocalPlayer;

    /// <summary>角色昵称（随 Identity 于 Spawn 下发；NPC 为空）。供头顶名牌显示远程玩家名称。</summary>
    public string Nickname;

    /// <summary>角色称号名（随 Identity 于 Spawn 下发；空 = 未佩戴）。供头顶名牌显示称号。</summary>
    public string Title;

    /// <summary>
    /// 实体网络类型，取值与共享枚举 <c>Horizon.Game.Message.Network.NetworkEntityType</c> 一致
    /// （0=Unknown, 1=LocalPlayer, 2=RemotePlayer, 3=Npc, 4=Monster, 5=Projectile, 6=Item）。
    /// <para>
    /// 用 <c>int</c> 而非枚举存储：本组件被 Flax 客户端与服务器共用，而客户端另有一份同号但不同命名空间
    /// 的枚举副本（<c>HundunWorld.Game.ECS.Components.NetworkEntityType</c>），存 int 可免去跨程序集类型依赖。
    /// 服务端在把玩家写成本地/远程类型时都填 <c>RemotePlayer</c>——本地与否由 <see cref="IsLocalPlayer"/> 判定，
    /// 不要从本字段推断。
    /// </para>
    /// </summary>
    public int EntityType;

    /// <summary>
    /// 等级（随 State 于 Spawn/Update 下发）。玩家用于头顶名牌，怪物用于选中信息卡。
    /// <c>0</c> = 服务端未下发，客户端应隐藏等级而非显示 "Lv.0"。
    /// </summary>
    public int Level;

    /// <summary>怪物模板编号（玩家为 0）。外观目录的查表键——刻意不用显示名当键。</summary>
    public int TemplateId;

    /// <summary>模型键（编辑器配的 model_key）；空 = 服务端未下发，客户端按占位外观处理。</summary>
    public string? ModelKey;

    /// <summary>体型缩放；&lt;=0 = 未下发，客户端按 1.0（拿 0 当默认会把怪压成一个点）。</summary>
    public float ModelScale;

    /// <summary>
    /// 待机姿态（0站1坐2卧3潜水）；<c>-1 = 服务端未下发</c>，客户端保留自己的默认。
    /// <para>这里不给字段初始值（带初始值设定项的结构必须显式写无参构造，编译器直接拦），
    /// 值一律由 <c>SnapshotApplySystem</c> 从 wire Identity 赋过来；0 本身是合法姿态，不能当哨兵。</para>
    /// </summary>
    public int StandState;

    /// <summary>怪物档位 rank（0普通…4世界BOSS）：精英边框与骷髅 "??" 表现的依据。</summary>
    public int Rank;

    /// <summary>
    /// Spawn 那一刻这只怪正在吟唱的技能 id（0 = 内容包里查不出这条挂载，条子照样画，只是没有技能身份）。
    /// <para>这一组的用途是**晚进场者**：施法起手是瞬时事件，只发给当时看得见的人，
    /// 而补发通路只有 Spawn。缺了它，新玩家眼里正在读条的怪就是一根空条。</para>
    /// </summary>
    public int CastSkillId;

    /// <summary>吟唱总秒数；&lt;=0 = 这只怪没在读条（瞬发本来就没有条）。这是"有没有条"的唯一判据。</summary>
    public float CastTotalSeconds;

    /// <summary>服务端认为已经过去多少秒（客户端据此把条子画在半程，而不是从 0 重放）。</summary>
    public float CastElapsedSeconds;

    /// <summary>系别掩码（位号==域号，与 <c>MonsterSchoolBits</c> 同一套位）。</summary>
    public int CastSchoolMask;

    /// <summary>可否打断（1=可）：晚进场的人看到的描边必须与起手事件一致。</summary>
    public int CastInterruptible;
}
