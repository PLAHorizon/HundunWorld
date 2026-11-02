using FlaxEngine;
using System;

namespace Game.Character.Attributes
{
    /// <summary>
    /// 角色成长阶段
    /// </summary>
    public enum CharacterStage
    {
        /// <summary>武侠阶段（1-50级）</summary>
        Wuxia = 1,
        
        /// <summary>仙侠阶段（51-150级）</summary>
        Xianxia = 2,
        
        /// <summary>玄幻阶段（151-300级）</summary>
        Xuanhuan = 3
    }

    /// <summary>
    /// 角色属性组件
    /// 管理角色的基础属性、五行亲和度、能量系统等
    /// </summary>
    public class CharacterAttributesComponent : Script
    {
        #region 基础属性

        [Header("基础属性")]
        [Tooltip("角色等级")]
        public int Level = 1;

        [Tooltip("当前修为经验（服务端 EntityStateAuthComponent.Exp 同步）")]
        public long Exp = 0;

        [Tooltip("当前等级升级所需修为经验（由等级推导；服务端暂未下发上限）")]
        public long MaxExp => 1000L + (long)Level * 500L;

        [Tooltip("角色昵称")]
        public string Nickname = "无名侠";

        [Tooltip("角色称号（服务端 CharacterEntity.CurrentTitleId → TitleEntity.TitleName 同步；空 = 未佩戴，头顶名牌回退按等级推导）")]
        public string Title = "";

        [Tooltip("当前生命值")]
        public float CurrentHealth = 1000f;

        /// <summary>
        /// 服务端宣告的"已死亡"（网络状态位 Dead 的镜像；远端怪物的尸体窗口用它判）。
        /// <para>刻意不是 <c>CurrentHealth &lt;= 0</c> 的推导：血量归零是数值，死亡是状态。
        /// 由宿主写入，本地不做结算，所以任何"看起来像 0 血"的中间态都不会误判成尸体。</para>
        /// </summary>
        public bool IsDead = false;

        /// <summary>
        /// 服务端宣告的"此刻打不到它"（网络状态位 Evading 的镜像：怪物正在回位）。
        /// <para>与 <see cref="IsDead"/> 分开写：尸体不能选也不该显示血条，而回位中的怪看得见、选得中，
        /// 只是这一手打不动。这个字段同时充当"上一次同步读到的值"，让搬运只在真的变化时去取标记组件。</para>
        /// </summary>
        public bool Unattackable = false;

        // 可变属性一律"基值 + 装备加成"合成分：setter 写基值（服务端同步/默认初始化透明落基值），
        // getter 返回含装备加成的最终值——所有每帧读取方（血条/内力条/名牌/属性面板）零改动即反映穿戴。
        // _equip* 加成字段集中声明于"装备加成"region。
        private float _maxHealthBase = 1000f;
        /// <summary>生命上限 = 服务端基值 + 装备 + 力量×20 + 体质×100 + 木×300。</summary>
        public float MaxHealth
        {
            get => _maxHealthBase + _equipMaxHealth
                   + _alloc.Strength * StrengthHealthPerPoint
                   + _alloc.Health * HealthPerPoint
                   + _alloc.Wood * WoodHealthPerPoint;
            set => _maxHealthBase = value;
        }

        private float _physicalAttackBase = 100f;
        /// <summary>物攻 = 基值 + 装备 + 力量×10 + 金×50。</summary>
        public float PhysicalAttack
        {
            get => _physicalAttackBase + _equipPhysicalAttack
                   + _alloc.Strength * StrengthAttackPerPoint
                   + _alloc.Metal * MetalAttackPerPoint;
            set => _physicalAttackBase = value;
        }

        private float _magicAttackBase = 100f;
        public float MagicAttack
        {
            get => _magicAttackBase + _equipMagicAttack;
            set => _magicAttackBase = value;
        }

        private float _physicalDefenseBase = 50f;
        /// <summary>物防 = 基值 + 装备 + 敏捷×20 + 土×50。</summary>
        public float PhysicalDefense
        {
            get => _physicalDefenseBase + _equipPhysicalDefense
                   + _alloc.Agility * AgilityDefensePerPoint
                   + _alloc.Earth * EarthDefensePerPoint;
            set => _physicalDefenseBase = value;
        }

        private float _magicDefenseBase = 50f;
        public float MagicDefense
        {
            get => _magicDefenseBase + _equipMagicDefense;
            set => _magicDefenseBase = value;
        }

        #endregion

        #region 能量系统

        [Header("能量系统")]
        [Tooltip("当前能量值（内力/灵力/元力）")]
        public float CurrentEnergy = 1000f;

        private float _maxEnergyBase = 1000f;
        [Tooltip("最大能量值 = 基值 + 装备 + 智力×100 + 水×200")]
        public float MaxEnergy
        {
            get => _maxEnergyBase + _equipMaxEnergy
                   + _alloc.Intelligence * IntelligenceEnergyPerPoint
                   + _alloc.Water * WaterEnergyPerPoint;
            set => _maxEnergyBase = value;
        }

        [Tooltip("能量恢复速率（每秒）")]
        public float EnergyRecoveryRate = 10f;

        [Tooltip("体力值")]
        public float CurrentStamina = 100f;

        [Tooltip("最大体力值")]
        public float MaxStamina = 100f;

        [Tooltip("体力恢复速率（每秒）")]
        public float StaminaRecoveryRate = 5f;

        #endregion

        #region 五行属性

        [Header("五行亲和度")]
        private int _metalAffinityBase;
        [Tooltip("金属性亲和度（0-10000，含装备与加点）")]
        public int MetalAffinity
        {
            get => ClampAffinity(_metalAffinityBase + _equipMetal + _alloc.Metal * WuxingPerPoint);
            set => _metalAffinityBase = value;
        }

        private int _woodAffinityBase;
        [Tooltip("木属性亲和度（0-10000，含装备与加点）")]
        public int WoodAffinity
        {
            get => ClampAffinity(_woodAffinityBase + _equipWood + _alloc.Wood * WuxingPerPoint);
            set => _woodAffinityBase = value;
        }

        private int _waterAffinityBase;
        [Tooltip("水属性亲和度（0-10000，含装备与加点）")]
        public int WaterAffinity
        {
            get => ClampAffinity(_waterAffinityBase + _equipWater + _alloc.Water * WuxingPerPoint);
            set => _waterAffinityBase = value;
        }

        private int _fireAffinityBase;
        [Tooltip("火属性亲和度（0-10000，含装备与加点）")]
        public int FireAffinity
        {
            get => ClampAffinity(_fireAffinityBase + _equipFire + _alloc.Fire * WuxingPerPoint);
            set => _fireAffinityBase = value;
        }

        private int _earthAffinityBase;
        [Tooltip("土属性亲和度（0-10000，含装备与加点）")]
        public int EarthAffinity
        {
            get => ClampAffinity(_earthAffinityBase + _equipEarth + _alloc.Earth * WuxingPerPoint);
            set => _earthAffinityBase = value;
        }

        private static int ClampAffinity(int v) => v < 0 ? 0 : (v > 10000 ? 10000 : v);

        #endregion

        #region 成长阶段

        [Header("成长阶段")]
        [Tooltip("当前成长阶段")]
        public CharacterStage CurrentStage = CharacterStage.Wuxia;

        #endregion

        #region 装备加成

        // 装备加成独立于基值存储；基值由服务端同步/默认初始化直接写入（对应属性 setter）。
        private float _equipMaxHealth;
        private float _equipPhysicalAttack;
        private float _equipMagicAttack;
        private float _equipPhysicalDefense;
        private float _equipMagicDefense;
        private float _equipMaxEnergy;
        private int _equipMetal;
        private int _equipWood;
        private int _equipWater;
        private int _equipFire;
        private int _equipEarth;

        // 装备的五行三行（攻击/防御/破防）。亲和（_equipMetal…）只喂雷达图，不参与伤害公式；
        // 这三行才是服务端权威结算里真正进CombatProfile的那一层，两端必须同口径。
        private WuxingRows _equipElementAttack;
        private WuxingRows _equipElementDefense;
        private WuxingRows _equipElementPenetration;

        /// <summary>
        /// 按元素分行的一组整数（装备的五行三行）。
        /// 行号语义与 <c>GetElementDamage/GetElementDefense/GetPenetration</c> 的元素入参一致。
        /// </summary>
        public struct WuxingRows
        {
            public int Metal;
            public int Wood;
            public int Water;
            public int Fire;
            public int Earth;

            /// <summary>取某一行的值；无属性或越界返回 0。</summary>
            public int Of(WuxingElement element) => element switch
            {
                WuxingElement.Metal => Metal,
                WuxingElement.Wood => Wood,
                WuxingElement.Water => Water,
                WuxingElement.Fire => Fire,
                WuxingElement.Earth => Earth,
                _ => 0,
            };

            /// <summary>把 (元素, 值) 累加进对应行。</summary>
            public void Add(WuxingElement element, int value)
            {
                switch (element)
                {
                    case WuxingElement.Metal: Metal += value; break;
                    case WuxingElement.Wood: Wood += value; break;
                    case WuxingElement.Water: Water += value; break;
                    case WuxingElement.Fire: Fire += value; break;
                    case WuxingElement.Earth: Earth += value; break;
                }
            }
        }

        /// <summary>
        /// 装备聚合加成（由 <c>EquipmentAttributeBridge</c> 按服务端权威穿戴快照求和）。
        /// </summary>
        public struct EquipmentStatBonus
        {
            public float MaxHealth;
            public float PhysicalAttack;
            public float MagicAttack;
            public float PhysicalDefense;
            public float MagicDefense;
            public float MaxEnergy;
            public int MetalAffinity;
            public int WoodAffinity;
            public int WaterAffinity;
            public int FireAffinity;
            public int EarthAffinity;

            /// <summary>装备提供的五行攻击力（按行）。</summary>
            public WuxingRows ElementAttack;
            /// <summary>装备提供的五行防御力（按行）。</summary>
            public WuxingRows ElementDefense;
            /// <summary>装备提供的五行破防值（按行，存攻方本行）。</summary>
            public WuxingRows ElementPenetration;
        }

        /// <summary>属性变化事件（穿戴/卸下重算后触发；UI 可用于即时刷新）。</summary>
        public event Action AttributesChanged;

        /// <summary>
        /// 应用装备加成并触发 <see cref="AttributesChanged"/>。传默认结构 = 清空全部装备加成。
        /// 当前值（生命/内力）超出新的上限时按新上限夹取。
        /// </summary>
        public void SetEquipmentBonus(in EquipmentStatBonus bonus)
        {
            _equipMaxHealth = bonus.MaxHealth;
            _equipPhysicalAttack = bonus.PhysicalAttack;
            _equipMagicAttack = bonus.MagicAttack;
            _equipPhysicalDefense = bonus.PhysicalDefense;
            _equipMagicDefense = bonus.MagicDefense;
            _equipMaxEnergy = bonus.MaxEnergy;
            _equipMetal = bonus.MetalAffinity;
            _equipWood = bonus.WoodAffinity;
            _equipWater = bonus.WaterAffinity;
            _equipFire = bonus.FireAffinity;
            _equipEarth = bonus.EarthAffinity;
            _equipElementAttack = bonus.ElementAttack;
            _equipElementDefense = bonus.ElementDefense;
            _equipElementPenetration = bonus.ElementPenetration;

            CurrentHealth = Mathf.Min(CurrentHealth, MaxHealth);
            CurrentEnergy = Mathf.Min(CurrentEnergy, MaxEnergy);

            AttributesChanged?.Invoke();
        }

        #endregion

        #region 点数分配

        /// <summary>
        /// 自由点分配（第三层属性来源：基值 + 装备 + 加点）。
        /// 点池 = 等级 × <see cref="AllocationPointsPerLevel"/>，随服务端等级同步自动扩容；
        /// 分配存客户端本地（SQLite），装备加成走服务端权威快照，两者互不覆盖。
        /// 字段序=UI 行序：力量/敏捷/体质/智力 + 金木水火土。
        /// 第 4 项历史上叫"内力"（每点 +10 内力上限），2026-09-28 起改为"智力"
        /// （每点 +100 内力上限 +10 精神 +1 智慧 +3 神识 +0.2 幸运）；
        /// 线上协议字段与数据库列沿用旧名以免迁移，语义以这里为准。
        /// </summary>
        public struct AttributeAllocation
        {
            public int Strength;
            public int Agility;
            public int Health;
            public int Intelligence;
            public int Metal;
            public int Wood;
            public int Water;
            public int Fire;
            public int Earth;

            public readonly int Spent => Strength + Agility + Health + Intelligence + Metal + Wood + Water + Fire + Earth;
        }

        public enum AllocationKind { Strength, Agility, Health, Intelligence, Metal, Wood, Water, Fire, Earth }

        // ── 每点换算（UI 描述与属性公式的单一来源） ──────────────────────
        public const int AllocationPointsPerLevel = 5;

        // 力量：+20 生命上限、+10 攻击力
        public const int StrengthHealthPerPoint = 20;
        public const int StrengthAttackPerPoint = 10;
        // 敏捷：+20 防御、+3 移动速度
        public const int AgilityDefensePerPoint = 20;
        public const int AgilityMoveSpeedPerPoint = 3;
        // 体质：+100 生命上限
        public const int HealthPerPoint = 100;
        // 智力：+100 内力上限、+10 精神、+1 智慧、+3 神识、+0.2 幸运
        //（2026-09-28 由"内力 +10 内力上限/点"改换；精神/智慧/神识/幸运此前没有任何数据支撑，
        //  现在统一由智力点派生，属纯派生量，不落库）
        public const int IntelligenceEnergyPerPoint = 100;
        public const int IntelligenceSpiritPerPoint = 10;
        public const int IntelligenceWisdomPerPoint = 1;
        public const int IntelligencePerceptionPerPoint = 3;
        public const float IntelligenceLuckPerPoint = 0.2f;
        // 五行共同：+10 对应亲和（雷达图口径）
        public const int WuxingPerPoint = 10;
        // 金：+50 攻击、+30 金伤、+5 金防、+30 破木防、+0.1% 暴击率、+3% 暴击伤害
        public const int MetalAttackPerPoint = 50;
        public const int MetalElementDamagePerPoint = 30;
        public const int MetalElementDefensePerPoint = 5;
        public const int MetalPenetrationPerPoint = 30;
        public const float MetalCritRatePerPoint = 0.1f;
        public const float MetalCritDamagePerPoint = 3f;
        // 木：+300 生命上限、+20 木伤、+5 木防、+30 破土防、+15 每秒回血、+0.1% 毒素伤害几率、+0.2% 恢复技能回复率
        public const int WoodHealthPerPoint = 300;
        public const int WoodElementDamagePerPoint = 20;
        public const int WoodElementDefensePerPoint = 5;
        public const int WoodPenetrationPerPoint = 30;
        public const int WoodRegenPerPoint = 15;
        public const float WoodPoisonPerPoint = 0.1f;
        public const float WoodHealBonusPerPoint = 0.2f;
        // 水：+200 内力上限、+30 水伤、+5 水防、+30 破火防、+0.1% 冰冻几率、+0.2% 移动减速（上限 70%）
        public const int WaterEnergyPerPoint = 200;
        public const int WaterElementDamagePerPoint = 30;
        public const int WaterElementDefensePerPoint = 5;
        public const int WaterPenetrationPerPoint = 30;
        public const float WaterFreezePerPoint = 0.1f;
        public const float WaterSlowPerPoint = 0.2f;
        public const float SlowCapPercent = 70f;
        // 火：+2 攻击速度、+30 火伤、+0.1% 极速攻击几率（触发时 3 倍攻速）、+30 破金防、+5 移动速度
        public const int FireAttackSpeedPerPoint = 2;
        public const int FireElementDamagePerPoint = 30;
        public const float FireRapidPerPoint = 0.1f;
        public const float RapidAttackSpeedMultiplier = 3f;
        public const int FirePenetrationPerPoint = 30;
        public const int FireMoveSpeedPerPoint = 5;
        // 土：+50 防御、+30 土伤、+5 土防、+30 破水防、+0.1% 反伤几率（反弹目标实际伤害的 10%）
        public const int EarthDefensePerPoint = 50;
        public const int EarthElementDamagePerPoint = 30;
        public const int EarthElementDefensePerPoint = 5;
        public const int EarthPenetrationPerPoint = 30;
        public const float EarthReflectPerPoint = 0.1f;
        public const float ReflectDamageFactor = 0.10f;

        private AttributeAllocation _alloc;

        // ── 服务端权威画像（CharacterAttributeRefresh 下行）──────────────────
        //
        // 三行的权威出处是服务端：属性表 *_attack / *_resistance / *_penetration 三组列、
        // 物品模板表里的装备数值、以及五行属性值兜底，客户端都读不到。
        // 本地那套"加点 + 装备硬编码表"只是**镜像**，画像没到时用它兜底，
        // 一旦画像到达就以它为准——运营改了配表也不会出现面板一套、结算一套。
        private bool _hasAuthoritativeProfile;
        private WuxingRows _authElementAttack;
        private WuxingRows _authElementDefense;
        private WuxingRows _authElementPenetration;
        private float _authAttack;
        private float _authDefense;

        /// <summary>是否已收到服务端权威画像（false 时三行与攻防按本地镜像算）。</summary>
        public bool HasAuthoritativeProfile => _hasAuthoritativeProfile;

        /// <summary>
        /// 宠物反哺的归因量（服务端 <c>PetAssistCore.ApplyToOwnerStats</c> 的 FromPets* 四项）。
        /// <para>按字段名赋值而不是走一长串位置参数：八个整数里换错两位不报错，
        /// 只会让"宠物给的生命"显示在攻击那一栏。</para>
        /// </summary>
        public struct PetBonusInfo
        {
            /// <summary>宠物贡献的攻击。</summary>
            public int Attack;

            /// <summary>宠物贡献的生命上限。</summary>
            public int Health;

            /// <summary>宠物贡献的防御。</summary>
            public int Defense;

            /// <summary>宠物贡献的命中。</summary>
            public int Hit;

            /// <summary>宠物替主人承受会心的概率（bps）。</summary>
            public int CritTankChanceBps;

            /// <summary>参与反哺的宠物只数。</summary>
            public int Contributors;

            /// <summary>名册总只数。</summary>
            public int RosterCount;

            /// <summary>问题描述；空 = 正常。</summary>
            public string Issues;
        }

        // 宠物栏与权威画像是**分开**的一份账：服务端的 Attack/Defense 没有含宠物（宠物还没进
        // CombatProfile 那条权威结算），所以这里绝不做 PetAttack + _authAttack 的合并显示——
        // 合并了就是"面板说宠物加了 1060 攻，打怪一点没变"。面板要把它作为独立一行念出来。
        private bool _hasPetBonus;
        private PetBonusInfo _petBonus;

        /// <summary>是否已收到宠物反哺数据（false 时宠物栏显示占位而不是 0，0 会被读成"没有出战宠"）。</summary>
        public bool HasPetBonus => _hasPetBonus;

        /// <summary>宠物反哺归因量（未到达时各项为默认值，用 <see cref="HasPetBonus"/> 区分）。</summary>
        public PetBonusInfo PetBonus => _petBonus;

        /// <summary>写入宠物反哺数据（由权威画像那条下行一起带过来）。</summary>
        public void ApplyPetBonus(in PetBonusInfo info)
        {
            _petBonus = info;
            _hasPetBonus = true;
            AttributesChanged?.Invoke();
        }

        /// <summary>
        /// 写入服务端权威画像。浮点三行按四舍五入落成整数行（面板与本地口径都是整数）。
        /// </summary>
        public void ApplyAuthoritativeProfile(
            in WuxingRows elementAttack, in WuxingRows elementDefense, in WuxingRows elementPenetration,
            float attackPower, float defense)
        {
            _authElementAttack = elementAttack;
            _authElementDefense = elementDefense;
            _authElementPenetration = elementPenetration;
            _authAttack = attackPower;
            _authDefense = defense;
            _hasAuthoritativeProfile = true;

            AttributesChanged?.Invoke();
        }

        /// <summary>
        /// 攻击力显示口径：有权威画像时用服务端那份通用攻（已含装备与加点），
        /// 否则退回本地"物攻 + 法攻"。两者不能相加——服务端的 AttackPower 已经涵盖了加点攻击。
        /// </summary>
        public float AttackForDisplay => _hasAuthoritativeProfile ? _authAttack : PhysicalAttack + MagicAttack;

        /// <summary>防御力显示口径，同 <see cref="AttackForDisplay"/>。</summary>
        public float DefenseForDisplay => _hasAuthoritativeProfile ? _authDefense : PhysicalDefense + MagicDefense;

        public AttributeAllocation Allocation => _alloc;
        public int AllocationPointsTotal => Level * AllocationPointsPerLevel;
        public int AllocationPointsAvailable => AllocationPointsTotal - _alloc.Spent;

        // ── 派生战斗属性（当前仅由加点产生；服务端接入伤害公式前的单一读取口径） ──

        /// <summary>暴击率 %（金）。</summary>
        public float CritRatePercent => _alloc.Metal * MetalCritRatePerPoint;
        /// <summary>暴击伤害加成 %（金）。</summary>
        public float CritDamageBonusPercent => _alloc.Metal * MetalCritDamagePerPoint;
        /// <summary>每秒回血（木）。</summary>
        public float HpRegenPerSecond => _alloc.Wood * WoodRegenPerPoint;
        /// <summary>毒素伤害几率 %（木）。</summary>
        public float PoisonChancePercent => _alloc.Wood * WoodPoisonPerPoint;
        /// <summary>恢复技能回复率加成 %（木）。</summary>
        public float HealEffectBonusPercent => _alloc.Wood * WoodHealBonusPerPoint;
        /// <summary>冰冻几率 %（水）。</summary>
        public float FreezeChancePercent => _alloc.Water * WaterFreezePerPoint;
        /// <summary>移动速度减缓 %（水，命中后生效，上限 <see cref="SlowCapPercent"/>%）。</summary>
        public float MoveSlowPercent => System.Math.Min(SlowCapPercent, _alloc.Water * WaterSlowPerPoint);
        /// <summary>攻击速度加成（火，点值）。</summary>
        public float AttackSpeedBonus => _alloc.Fire * FireAttackSpeedPerPoint;
        /// <summary>极速攻击几率 %（火；触发时攻速 ×<see cref="RapidAttackSpeedMultiplier"/>）。</summary>
        public float RapidAttackChancePercent => _alloc.Fire * FireRapidPerPoint;
        /// <summary>反伤几率 %（土；触发时反弹目标实际伤害的 <see cref="ReflectDamageFactor"/>）。</summary>
        public float ReflectChancePercent => _alloc.Earth * EarthReflectPerPoint;
        /// <summary>移动速度加成 = 敏捷×3 + 火×5。</summary>
        public float MoveSpeedBonus => _alloc.Agility * AgilityMoveSpeedPerPoint + _alloc.Fire * FireMoveSpeedPerPoint;

        /// <summary>精神（智力×10）。</summary>
        public int Spirit => _alloc.Intelligence * IntelligenceSpiritPerPoint;
        /// <summary>智慧（智力×1）。</summary>
        public int Wisdom => _alloc.Intelligence * IntelligenceWisdomPerPoint;
        /// <summary>神识（智力×3）。</summary>
        public int Perception => _alloc.Intelligence * IntelligencePerceptionPerPoint;
        /// <summary>幸运值（智力×0.2）。</summary>
        public float Luck => _alloc.Intelligence * IntelligenceLuckPerPoint;

        // 三行的合并口径与服务端 ElementalDamageResolver / CombatProfile 逐字对齐：
        // 攻击力=各来源求和；防御力与破防值="已高者计"，不是相加。
        // 写成相加会让"同时有加点土防和土防具"的角色面板显示 70、服务端只认 50，
        // 这类面板与结算不一致是最难查的缺陷，改任何一行都要同时看两端。

        // 五行属性值（亲和）→ 三行兜底。与服务端
        // Horizon.Game.Core.Combat.AttributeDerivation.DeriveAffinityFallback 是同一规则的两份实现：
        // 每 10 点亲和算 1 个等效加点（即 WuxingPerPoint），按各自每点换算折成三行后取半额，
        // 最后与"加点+装备"聚出的那一行<b>取较高者</b>。
        // 取较高而不是相加是关键：加点已经全额进过三行，再加一遍半额亲和等于凭空多付五成伤害。
        public const float AffinityPerEquivalentPoint = 10f;
        public const float AffinityFallbackRatio = 0.5f;

        /// <summary>某行的兜底值：该五行属性值 ÷10 个等效点 × 每点换算 × 50%（向下取整）。</summary>
        private int AffinityFallbackRow(WuxingElement element, float perPoint)
            => (int)(GetWuxingAffinity(element) * (AffinityFallbackRatio / AffinityPerEquivalentPoint) * perPoint);

        /// <summary>该行的每点攻击换算（与上面 GetElementDamage 用的同一组常数）。</summary>
        private static int ElementAttackPerPointOf(WuxingElement element) => element switch
        {
            WuxingElement.Metal => MetalElementDamagePerPoint,
            WuxingElement.Wood => WoodElementDamagePerPoint,
            WuxingElement.Water => WaterElementDamagePerPoint,
            WuxingElement.Fire => FireElementDamagePerPoint,
            WuxingElement.Earth => EarthElementDamagePerPoint,
            _ => 0,
        };

        /// <summary>该行的每点防御换算（火系规格未给，兜底同样不给）。</summary>
        private static int ElementDefensePerPointOf(WuxingElement element) => element switch
        {
            WuxingElement.Metal => MetalElementDefensePerPoint,
            WuxingElement.Wood => WoodElementDefensePerPoint,
            WuxingElement.Water => WaterElementDefensePerPoint,
            WuxingElement.Fire => 0,
            WuxingElement.Earth => EarthElementDefensePerPoint,
            _ => 0,
        };

        /// <summary>该行的每点破防换算（五行各 30）。</summary>
        private static int ElementPenetrationPerPointOf(WuxingElement element) => element switch
        {
            WuxingElement.Metal => MetalPenetrationPerPoint,
            WuxingElement.Wood => WoodPenetrationPerPoint,
            WuxingElement.Water => WaterPenetrationPerPoint,
            WuxingElement.Fire => FirePenetrationPerPoint,
            WuxingElement.Earth => EarthPenetrationPerPoint,
            _ => 0,
        };

        /// <summary>
        /// 属性伤害（加点本行 + 装备该行五行攻击，攻击侧求和；再与五行属性值兜底行取较高）。
        /// </summary>
        public int GetElementDamage(WuxingElement element)
        {
            if (_hasAuthoritativeProfile) return _authElementAttack.Of(element);

            int fromSources = element switch
            {
                WuxingElement.Metal => _alloc.Metal * MetalElementDamagePerPoint,
                WuxingElement.Wood => _alloc.Wood * WoodElementDamagePerPoint,
                WuxingElement.Water => _alloc.Water * WaterElementDamagePerPoint,
                WuxingElement.Fire => _alloc.Fire * FireElementDamagePerPoint,
                WuxingElement.Earth => _alloc.Earth * EarthElementDamagePerPoint,
                _ => 0,
            } + _equipElementAttack.Of(element);

            return System.Math.Max(fromSources, AffinityFallbackRow(element, ElementAttackPerPointOf(element)));
        }

        /// <summary>
        /// 五行防御（加点金/木/水/土各 5/点，火系规格未给；与装备该行防御、五行属性值兜底行取最高者）。
        /// </summary>
        public int GetElementDefense(WuxingElement element)
        {
            if (_hasAuthoritativeProfile) return _authElementDefense.Of(element);

            int fromAllocation = element switch
            {
                WuxingElement.Metal => _alloc.Metal * MetalElementDefensePerPoint,
                WuxingElement.Wood => _alloc.Wood * WoodElementDefensePerPoint,
                WuxingElement.Water => _alloc.Water * WaterElementDefensePerPoint,
                WuxingElement.Fire => 0,
                WuxingElement.Earth => _alloc.Earth * EarthElementDefensePerPoint,
                _ => 0,
            };
            return System.Math.Max(
                System.Math.Max(fromAllocation, _equipElementDefense.Of(element)),
                AffinityFallbackRow(element, ElementDefensePerPointOf(element)));
        }

        /// <summary>
        /// 破防（按攻方五行：金破木、木破土、水破火、火破金、土破水，各 30/点；
        /// 与装备该行破防、五行属性值兜底行取最高者）。
        /// </summary>
        public int GetPenetration(WuxingElement attacker)
        {
            if (_hasAuthoritativeProfile) return _authElementPenetration.Of(attacker);

            int fromAllocation = attacker switch
            {
                WuxingElement.Metal => _alloc.Metal * MetalPenetrationPerPoint,
                WuxingElement.Wood => _alloc.Wood * WoodPenetrationPerPoint,
                WuxingElement.Water => _alloc.Water * WaterPenetrationPerPoint,
                WuxingElement.Fire => _alloc.Fire * FirePenetrationPerPoint,
                WuxingElement.Earth => _alloc.Earth * EarthPenetrationPerPoint,
                _ => 0,
            };
            return System.Math.Max(
                System.Math.Max(fromAllocation, _equipElementPenetration.Of(attacker)),
                AffinityFallbackRow(attacker, ElementPenetrationPerPointOf(attacker)));
        }

        /// <summary>
        /// 回填服务端权威分配；非法（负点/超池）按空分配处理。
        /// 这是本组件唯一的加点写入口——单点增减由服务端校验与提交，客户端不自行算点。
        /// </summary>
        public void ApplyAllocation(in AttributeAllocation alloc)
        {
            var safe = alloc;
            safe.Strength = Mathf.Max(0, safe.Strength);
            safe.Agility = Mathf.Max(0, safe.Agility);
            safe.Health = Mathf.Max(0, safe.Health);
            safe.Intelligence = Mathf.Max(0, safe.Intelligence);
            safe.Metal = Mathf.Max(0, safe.Metal);
            safe.Wood = Mathf.Max(0, safe.Wood);
            safe.Water = Mathf.Max(0, safe.Water);
            safe.Fire = Mathf.Max(0, safe.Fire);
            safe.Earth = Mathf.Max(0, safe.Earth);
            if (safe.Spent > AllocationPointsTotal)
                safe = default;

            _alloc = safe;
            ClampVitalsToMax();
            AttributesChanged?.Invoke();
        }

        private void ClampVitalsToMax()
        {
            CurrentHealth = Mathf.Min(CurrentHealth, MaxHealth);
            CurrentEnergy = Mathf.Min(CurrentEnergy, MaxEnergy);
        }

        #endregion

        /// <summary>
        /// 获取指定五行属性的亲和度
        /// </summary>
        public int GetWuxingAffinity(WuxingElement element)
        {
            return element switch
            {
                WuxingElement.Metal => MetalAffinity,
                WuxingElement.Wood => WoodAffinity,
                WuxingElement.Water => WaterAffinity,
                WuxingElement.Fire => FireAffinity,
                WuxingElement.Earth => EarthAffinity,
                _ => 0
            };
        }

        /// <summary>
        /// 增加五行亲和度
        /// </summary>
        public void AddWuxingAffinity(WuxingElement element, int amount)
        {
            switch (element)
            {
                case WuxingElement.Metal:
                    MetalAffinity = Mathf.Clamp(MetalAffinity + amount, 0, 10000);
                    break;
                case WuxingElement.Wood:
                    WoodAffinity = Mathf.Clamp(WoodAffinity + amount, 0, 10000);
                    break;
                case WuxingElement.Water:
                    WaterAffinity = Mathf.Clamp(WaterAffinity + amount, 0, 10000);
                    break;
                case WuxingElement.Fire:
                    FireAffinity = Mathf.Clamp(FireAffinity + amount, 0, 10000);
                    break;
                case WuxingElement.Earth:
                    EarthAffinity = Mathf.Clamp(EarthAffinity + amount, 0, 10000);
                    break;
            }
        }

        /// <summary>
        /// 消耗能量
        /// </summary>
        public bool ConsumeEnergy(float amount)
        {
            if (CurrentEnergy >= amount)
            {
                CurrentEnergy -= amount;
                return true;
            }
            return false;
        }

        /// <summary>
        /// 消耗体力
        /// </summary>
        public bool ConsumeStamina(float amount)
        {
            if (CurrentStamina >= amount)
            {
                CurrentStamina -= amount;
                return true;
            }
            return false;
        }

        /// <summary>
        /// 造成伤害
        /// </summary>
        public void TakeDamage(float damage)
        {
            CurrentHealth = Mathf.Max(0, CurrentHealth - damage);
            
            if (CurrentHealth <= 0)
            {
                OnDeath();
            }
        }

        /// <summary>
        /// 治疗
        /// </summary>
        public void Heal(float amount)
        {
            CurrentHealth = Mathf.Min(MaxHealth, CurrentHealth + amount);
        }

        private void OnDeath()
        {
            Debug.Log("角色死亡");
        }

        public override void OnUpdate()
        {
            float deltaTime = Time.DeltaTime;

            // 能量恢复
            if (CurrentEnergy < MaxEnergy)
            {
                CurrentEnergy = Mathf.Min(MaxEnergy, CurrentEnergy + EnergyRecoveryRate * deltaTime);
            }

            // 体力恢复
            if (CurrentStamina < MaxStamina)
            {
                CurrentStamina = Mathf.Min(MaxStamina, CurrentStamina + StaminaRecoveryRate * deltaTime);
            }
        }
    }
}
