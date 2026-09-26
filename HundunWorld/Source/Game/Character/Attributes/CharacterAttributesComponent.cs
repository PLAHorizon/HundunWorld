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
        [Tooltip("最大能量值 = 基值 + 装备 + 内力×10 + 水×200")]
        public float MaxEnergy
        {
            get => _maxEnergyBase + _equipMaxEnergy
                   + _alloc.Energy * EnergyPerPoint
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
        /// 字段序=UI 行序：力量/敏捷/体质/内力 + 金木水火土。
        /// </summary>
        public struct AttributeAllocation
        {
            public int Strength;
            public int Agility;
            public int Health;
            public int Energy;
            public int Metal;
            public int Wood;
            public int Water;
            public int Fire;
            public int Earth;

            public readonly int Spent => Strength + Agility + Health + Energy + Metal + Wood + Water + Fire + Earth;
        }

        public enum AllocationKind { Strength, Agility, Health, Energy, Metal, Wood, Water, Fire, Earth }

        // ── 每点换算（UI 描述与属性公式的单一来源） ──────────────────────
        public const int AllocationPointsPerLevel = 5;

        // 力量：+20 生命上限、+10 攻击力
        public const int StrengthHealthPerPoint = 20;
        public const int StrengthAttackPerPoint = 10;
        // 敏捷：+20 防御、+3 移动速度
        public const int AgilityDefensePerPoint = 20;
        public const int AgilityMoveSpeedPerPoint = 3;
        // 体质：+100 生命上限；内力：+10 内力上限
        public const int HealthPerPoint = 100;
        public const int EnergyPerPoint = 10;
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

        /// <summary>属性伤害（按来源五行：金木水火土）。</summary>
        public int GetElementDamage(WuxingElement element) => element switch
        {
            WuxingElement.Metal => _alloc.Metal * MetalElementDamagePerPoint,
            WuxingElement.Wood => _alloc.Wood * WoodElementDamagePerPoint,
            WuxingElement.Water => _alloc.Water * WaterElementDamagePerPoint,
            WuxingElement.Fire => _alloc.Fire * FireElementDamagePerPoint,
            WuxingElement.Earth => _alloc.Earth * EarthElementDamagePerPoint,
            _ => 0,
        };

        /// <summary>五行防御（金/木/水/土各 5/点；规格未给火系防御，保持 0）。</summary>
        public int GetElementDefense(WuxingElement element) => element switch
        {
            WuxingElement.Metal => _alloc.Metal * MetalElementDefensePerPoint,
            WuxingElement.Wood => _alloc.Wood * WoodElementDefensePerPoint,
            WuxingElement.Water => _alloc.Water * WaterElementDefensePerPoint,
            WuxingElement.Fire => 0,
            WuxingElement.Earth => _alloc.Earth * EarthElementDefensePerPoint,
            _ => 0,
        };

        /// <summary>破防（按攻方五行：金破木、木破土、水破火、火破金、土破水，各 30/点）。</summary>
        public int GetPenetration(WuxingElement attacker) => attacker switch
        {
            WuxingElement.Metal => _alloc.Metal * MetalPenetrationPerPoint,
            WuxingElement.Wood => _alloc.Wood * WoodPenetrationPerPoint,
            WuxingElement.Water => _alloc.Water * WaterPenetrationPerPoint,
            WuxingElement.Fire => _alloc.Fire * FirePenetrationPerPoint,
            WuxingElement.Earth => _alloc.Earth * EarthPenetrationPerPoint,
            _ => 0,
        };

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
            safe.Energy = Mathf.Max(0, safe.Energy);
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
