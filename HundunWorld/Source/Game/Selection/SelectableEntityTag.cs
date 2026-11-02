using System.Collections.Generic;
using FlaxEngine;

namespace HundunWorld.Game.Selection
{
    /// <summary>可选中实体的种类。与网络层的 <c>NetworkEntityType</c> 分离：本枚举面向"选中/信息卡"表现。</summary>
    public enum SelectableKind
    {
        None = 0,
        Player = 1,
        Monster = 2,
        Item = 3,
        GatherNode = 4,
        Npc = 5,
    }

    /// <summary>
    /// 可选中实体标记：挂在怪物 / 掉落物 / 采集点等由网络或场景生成的 Actor 上，
    /// 作为「Flax Actor ↔ ECS 实体 ↔ 选中与信息卡」的唯一桥。
    /// <para>
    /// <b>为什么要显式挂标记而不是靠 Tag/Layer 判定</b>：旧的
    /// <c>Combat.TargetSelectionSystem</c> 用 <c>Actor.Tag == "Monster"</c> 与 Layer 8 判敌对，
    /// 而全仓没有任何 Actor 带这些 Tag，选中能力因此恒空。标记由创建 Actor 的同步系统当场写入，
    /// 类型来源是服务端下发的 <c>NetworkIdentityComponent.EntityType</c>，不存在漂移。
    /// </para>
    /// <para>
    /// 注册表 <see cref="Active"/> 是选中系统枚举"附近有哪些候选"的入口，避免每 0.5 秒
    /// <c>Level.GetActors</c> 全场景扫描。
    /// </para>
    /// </summary>
    public class SelectableEntityTag : Script
    {
        /// <summary>当前存活的可选中标记。由 <see cref="OnEnable"/> / <see cref="OnDisable"/> 自维护。</summary>
        public static readonly List<SelectableEntityTag> Active = new List<SelectableEntityTag>();

        /// <summary>服务器权威实体 ID；0 = 纯场景对象（尚未接入网络同步的采集点）。</summary>
        public ulong EntityId { get; set; }

        /// <summary>
        /// 这具尸体现在能不能取（快照的 <c>EntityStateBits.CorpseInteractable</c>）。 CORPSE-LOOT-PATCH
        /// <para>与 <see cref="Selectable"/> 是两件事：尸体不可选中（选中框架的既有语义），
        /// 但选中框挂着的那个目标如果已经死了，G 键要能对它发拾取请求。</para>
        /// </summary>
        public bool CorpseInteractable { get; set; }

        /// <summary>实体种类，决定信息卡字段与选中后的可用动作。</summary>
        public SelectableKind Kind { get; set; } = SelectableKind.None;

        /// <summary>
        /// 显示名兜底：玩家/怪物的名字优先走 <c>CharacterAttributesComponent.Nickname</c>
        /// （由 FlaxActorSyncSystem 从网络搬运），无该组件的纯场景对象用本字段。
        /// </summary>
        public string DisplayName { get; set; } = string.Empty;

        /// <summary>等级；0 = 服务端未下发，信息卡应隐藏等级而非显示 "Lv.0"。</summary>
        public int Level { get; set; }

        /// <summary>物品品质（对齐 <c>ItemCatalog</c> 的品质索引）；-1 = 该实体无品质语义。</summary>
        public int Quality { get; set; } = -1;

        /// <summary>怪物模板编号（玩家/NPC 为 0）。身份键，绝不用显示名当键。</summary>
        public int TemplateId { get; set; }

        /// <summary>模型键（服务端 spawn-only 下发）；空 = 未下发。</summary>
        public string ModelKey { get; set; } = string.Empty;

        /// <summary>怪物档位 rank（0普通 1精英 2稀有精英 3首领 4世界BOSS）；玩家为 0。</summary>
        public int Rank { get; set; }

        /// <summary>待机姿态（0站1坐2卧3潜水）；-1 = 服务端未下发。</summary>
        public int StandState { get; set; } = -1;

        /// <summary>
        /// 服务位图（Gossip=1 QuestGiver=2 QuestTurnIn=4 Vendor=8 Trainer=16 CraftTrainer=32 CraftStation=64），
        /// 由 <c>EntityStateAuthComponent.NpcServiceFlags</c> 每快照搬下来。
        /// <para><b>-1 = 服务端未下发</b>（老内容 / 非内容 NPC），这时才退到本地目录 <c>NpcStanceCatalog</c> 去猜；
        /// <b>0 = 确知没有任何服务</b>，不许再退回目录——山贼头目的服务位就是 0，
        /// 若把它也当"未下发"，客户端会替它长出一个点不开的对话按钮。</para>
        /// </summary>
        public int NpcServiceFlags { get; set; } = -1;

        /// <summary>是否仍可被选中（死亡/被采集/超时后由持有系统置 false，Actor 销毁前不留悬空目标）。</summary>
        public bool Selectable { get; set; } = true;

        /// <summary>
        /// 服务端已宣告"此刻打不到它"（怪物正在回位：v1 的 Leash / v2 的 Evade）。
        /// <para>与 <see cref="Selectable"/> 是两件事：尸体不能选也打不到，而回位中的怪<b>看得见、选得中、
        /// 就是打不动</b>——所以不能拿 Selectable 兼这个意思，否则选它都选不了，玩家连"它在往家跑"都看不出来。</para>
        /// <para>这一位由 <c>FlaxActorSyncSystem</c> 从 <c>EntityStateAuthComponent.IsEvading</c> 搬下来，
        /// 是<b>服务端算好的事实</b>；客户端据此给人话原因，但免伤本身仍然由服务端权威判定。</para>
        /// </summary>
        public bool Unattackable { get; set; }

        // ---- 施法状态（服务端 MonsterCastStart/Stop 事件写，NameplateUI 每帧读）----
        //
        // 为什么挂在标记上而不是一个按实体 ID 索引的全局字典：实体 ID 会随重生复用同一个刷怪点槽位。
        // 字典里那条"上一世"的残留会让刚复活的怪开局挂着一根别人的条子，而没人会去清它；
        // 挂在 Actor 的标记上，Despawn 销毁实体时状态跟着一起没了——不需要任何额外清理纪律。

        /// <summary>正在吟唱（起手事件置真，结束事件置假）。</summary>
        public bool IsCasting { get; set; }

        /// <summary>吟唱中的技能 id（诊断与后续接技能名用）。</summary>
        public int CastSkillId { get; set; }

        /// <summary>伤害系别位（<c>MonsterSchoolBits</c> 口径）——客户端据此给条子分色。</summary>
        public int CastSchoolMask { get; set; }

        /// <summary>可打断：条子描金边（WoW 里"能打断"是玩家一眼要分辨的事）。</summary>
        public bool CastInterruptible { get; set; } = true;

        /// <summary>吟唱总秒数（起手事件带的时长，本地插值的分母）。</summary>
        public float CastDurationSeconds { get; set; }

        /// <summary>本地累计的吟唱秒数（名牌每帧推进；不靠引擎绝对时钟，免得暂停/切后台时错位）。</summary>
        public float CastElapsedSeconds { get; set; }

        /// <summary>
        /// 进战喊话的文案（空 = 没有话）。由 <c>HundunWorldGame</c> 收到
        /// <c>SyncEventKind.MonsterSay</c> 时按 <c>(ModelKey, sayId)</c> 查客户端文本表填进来——
        /// <b>线上只发号，文案永远在客户端表里</b>（把文案当身份用，翻译一次就作废全部旧引用）。
        /// </summary>
        public string ShoutText { get; set; } = string.Empty;

        /// <summary>本地累计的喊话秒数（名牌每帧推进，与吟唱条同一套时钟口径）。</summary>
        public float ShoutElapsedSeconds { get; set; }

        /// <summary>喊话显示秒数：一句话读完就收，不靠服务端再发一条"收"事件。</summary>
        public const float ShoutSeconds = 4.0f;

        /// <summary>挂一句喊话（空文案直接收干净，别留一个空气泡）。</summary>
        public void ShowShout(string text)
        {
            ShoutText = text ?? string.Empty;
            ShoutElapsedSeconds = 0f;
            if (ShoutText.Length == 0) return;
            // 标记上没有显示名（名字走快照那条路），所以这条自证只带实体号与模型键——
            // 够指认"哪只怪念了哪句"，不为了写日志去编一个新字段。
            Debug.Log($"[MonsterSay] 实体 {EntityId}（模型键 {ModelKey}）喊话：{ShoutText}");
        }

        /// <summary>每帧推进；到点自动收（收的动作在名牌那边判 <see cref="HasShout"/>）。</summary>
        public void AdvanceShout(float deltaTime)
        {
            if (ShoutText.Length == 0) return;
            ShoutElapsedSeconds += deltaTime;
            if (ShoutElapsedSeconds >= ShoutSeconds) ShoutText = string.Empty;
        }

        /// <summary>现在有话要说吗。</summary>
        public bool HasShout => ShoutText.Length > 0;

        /// <summary>起手：把条子摆出来。时长非正一律不摆——0 秒的条子只是一次闪烁噪声。</summary>
        public void BeginCast(int skillId, int schoolMask, bool interruptible, float durationSeconds, float startElapsedSeconds = 0f)
        {
            if (durationSeconds <= 0f)
            {
                // 瞬发没有条子（服务端本来也不该发这种起手）；真收到了就当场收干净，别留半根。
                EndCast();
                return;
            }

            // startElapsedSeconds>0 只出现在"晚进场者补发 Spawn"这条路：服务端给的是"已经读到哪了"，
            // 从 0 重放会让新人看到一根倒退回去重新读的条子（这只怪明明下一秒就该放出来了）。
            var elapsed = startElapsedSeconds <= 0f ? 0f : (startElapsedSeconds >= durationSeconds ? durationSeconds : startElapsedSeconds);

            CastSkillId = skillId;
            CastSchoolMask = schoolMask;
            CastInterruptible = interruptible;
            CastDurationSeconds = durationSeconds;
            CastElapsedSeconds = elapsed;
            IsCasting = true;
        }

        /// <summary>收条：无论原因是完成、打断还是取消，客户端的状态位都是同一套清零。</summary>
        public void EndCast()
        {
            IsCasting = false;
            CastSkillId = 0;
            CastSchoolMask = 0;
            CastDurationSeconds = 0f;
            CastElapsedSeconds = 0f;
        }

        /// <summary>推进本地吟唱秒数；返回是否仍在读条（超时后自行收条，见调用处理由）。</summary>
        public bool AdvanceCast(float deltaSeconds)
        {
            if (!IsCasting) return false;
            CastElapsedSeconds += deltaSeconds;
            // 结束事件丢了也要收条：一根停在满格不动的条子会谎报"这怪还在读条"，
            // 比根本没有施法条更误导玩家（他会一直等一个不会来的打断窗口）。
            if (CastDurationSeconds > 0f && CastElapsedSeconds > CastDurationSeconds + GraceSecondsAfterEnd)
                EndCast();
            return IsCasting;
        }

        /// <summary>结束事件允许晚到的宽限秒数（一帧的抖动 + 一次重传的短延迟）。</summary>
        private const float GraceSecondsAfterEnd = 0.35f;

        /// <summary>吟唱进度 0..1（供血条下方那根施法条使用）。</summary>
        public float CastProgress
        {
            get
            {
                if (!IsCasting || CastDurationSeconds <= 0f) return 0f;
                var ratio = CastElapsedSeconds / CastDurationSeconds;
                return ratio < 0f ? 0f : (ratio > 1f ? 1f : ratio);
            }
        }

        public override void OnEnable()
        {
            if (!Active.Contains(this))
                Active.Add(this);
        }

        public override void OnDisable()
        {
            Active.Remove(this);
        }
    }
}
