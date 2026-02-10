using FlaxEngine;
using Game.Character.Attributes;

namespace HundunWorld.Game.ECS.Components
{
    /// <summary>
    /// 技能类型枚举（本地定义，避免对外部DLL的依赖）
    /// </summary>
    public enum SkillType
    {
        Active,
        Passive,
        Special,
        Toggle,
        ActiveAttack,
        PassiveEnhancement,
        Control,
        Dash,
        Support,
        Ultimate,
    }

    /// <summary>
    /// 技能组件，存储实体的技能信息
    /// </summary>
    public struct SkillComponent
    {
        /// <summary>
        /// 技能ID
        /// </summary>
        public int SkillId;

        /// <summary>
        /// 技能名称
        /// </summary>
        public string SkillName;

        /// <summary>
        /// 技能类型
        /// </summary>
        public SkillType Type;

        /// <summary>
        /// 五行属性
        /// </summary>
        public WuxingElement Element;

        /// <summary>
        /// 基础伤害倍率
        /// </summary>
        public float DamageMultiplier;

        /// <summary>
        /// 能量消耗
        /// </summary>
        public float EnergyCost;

        /// <summary>
        /// 冷却时间（秒）
        /// </summary>
        public float Cooldown;

        /// <summary>
        /// 当前冷却剩余时间
        /// </summary>
        public float CurrentCooldown;

        /// <summary>
        /// 施法范围（米）
        /// </summary>
        public float Range;

        /// <summary>
        /// 施法时间（秒）
        /// </summary>
        public float CastTime;

        /// <summary>
        /// 技能等级
        /// </summary>
        public int Level;

        public SkillComponent(int skillId, string skillName, SkillType type, WuxingElement element, 
            float damageMultiplier, float energyCost, float cooldown, float range, float castTime, int level = 1)
        {
            SkillId = skillId;
            SkillName = skillName;
            Type = type;
            Element = element;
            DamageMultiplier = damageMultiplier;
            EnergyCost = energyCost;
            Cooldown = cooldown;
            CurrentCooldown = 0f;
            Range = range;
            CastTime = castTime;
            Level = level;
        }

        /// <summary>
        /// 是否就绪
        /// </summary>
        public bool IsReady()
        {
            return CurrentCooldown <= 0;
        }

        /// <summary>
        /// 获取冷却进度（0-1）
        /// </summary>
        public float GetCooldownProgress()
        {
            if (Cooldown <= 0) return 1.0f;
            return 1.0f - (CurrentCooldown / Cooldown);
        }
    }

    /// <summary>
    /// 技能施法状态组件
    /// </summary>
    public struct SkillCastingComponent
    {
        /// <summary>
        /// 正在施放的技能ID
        /// </summary>
        public int SkillId;

        /// <summary>
        /// 施法进度
        /// </summary>
        public float CastProgress;

        /// <summary>
        /// 总施法时间
        /// </summary>
        public float TotalCastTime;

        /// <summary>
        /// 目标实体ID
        /// </summary>
        public ulong TargetEntityId;

        /// <summary>
        /// 目标位置
        /// </summary>
        public Vector3 TargetPosition;

        /// <summary>
        /// 是否可移动施法
        /// </summary>
        public bool CanMoveWhileCasting;

        public SkillCastingComponent(int skillId, float castTime, ulong targetId, Vector3 targetPos, bool canMove = false)
        {
            SkillId = skillId;
            CastProgress = 0f;
            TotalCastTime = castTime;
            TargetEntityId = targetId;
            TargetPosition = targetPos;
            CanMoveWhileCasting = canMove;
        }

        /// <summary>
        /// 获取施法进度（0-1）
        /// </summary>
        public float GetProgress()
        {
            if (TotalCastTime <= 0) return 1.0f;
            return CastProgress / TotalCastTime;
        }
    }

    /// <summary>
    /// 技能槽组件，存储实体装备的技能
    /// </summary>
    public struct SkillSlotComponent
    {
        /// <summary>
        /// 技能槽1
        /// </summary>
        public int Slot1SkillId;

        /// <summary>
        /// 技能槽2
        /// </summary>
        public int Slot2SkillId;

        /// <summary>
        /// 技能槽3
        /// </summary>
        public int Slot3SkillId;

        /// <summary>
        /// 技能槽4
        /// </summary>
        public int Slot4SkillId;

        /// <summary>
        /// 终结技槽
        /// </summary>
        public int UltimateSkillId;

        public SkillSlotComponent(int slot1 = 0, int slot2 = 0, int slot3 = 0, int slot4 = 0, int ultimate = 0)
        {
            Slot1SkillId = slot1;
            Slot2SkillId = slot2;
            Slot3SkillId = slot3;
            Slot4SkillId = slot4;
            UltimateSkillId = ultimate;
        }

        /// <summary>
        /// 获取指定槽位的技能ID
        /// </summary>
        public int GetSkillId(int slotIndex)
        {
            return slotIndex switch
            {
                0 => Slot1SkillId,
                1 => Slot2SkillId,
                2 => Slot3SkillId,
                3 => Slot4SkillId,
                4 => UltimateSkillId,
                _ => 0
            };
        }

        /// <summary>
        /// 设置指定槽位的技能ID
        /// </summary>
        public void SetSkillId(int slotIndex, int skillId)
        {
            switch (slotIndex)
            {
                case 0: Slot1SkillId = skillId; break;
                case 1: Slot2SkillId = skillId; break;
                case 2: Slot3SkillId = skillId; break;
                case 3: Slot4SkillId = skillId; break;
                case 4: UltimateSkillId = skillId; break;
            }
        }
    }

    /// <summary>
    /// 技能范围组件，用于技能判定
    /// </summary>
    public struct SkillRangeComponent
    {
        /// <summary>
        /// 范围类型
        /// </summary>
        public RangeType Type;

        /// <summary>
        /// 范围半径
        /// </summary>
        public float Radius;

        /// <summary>
        /// 范围角度（扇形）
        /// </summary>
        public float Angle;

        /// <summary>
        /// 范围长度（矩形）
        /// </summary>
        public float Length;

        /// <summary>
        /// 范围宽度（矩形）
        /// </summary>
        public float Width;

        public SkillRangeComponent(RangeType type, float radius = 5f, float angle = 90f, float length = 10f, float width = 2f)
        {
            Type = type;
            Radius = radius;
            Angle = angle;
            Length = length;
            Width = width;
        }
    }

    /// <summary>
    /// 范围类型
    /// </summary>
    public enum RangeType
    {
        Single,     // 单体
        Circle,     // 圆形AOE
        Sector,     // 扇形
        Rectangle,  // 矩形
        Line        // 直线
    }
}
