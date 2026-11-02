using System;
using System.Collections.Generic;
using System.Linq;
using FlaxEngine;
using NarrativePro.Core;

namespace NarrativePro.Items
{
    /// <summary>
    /// 装备物品的使用动作（装备到槽位）。
    /// </summary>
    public class UseAction_Equip : NarrativeItemUseAction
    {
        public GameplayTag EquipToSlot { get; set; } = GameplayTag.None;

        public override bool OnUse(NarrativeItem item, NarrativeItem otherItem)
        {
            if (item is EquippableItem equippable)
            {
                return equippable.EquipItem(EquipToSlot);
            }
            return false;
        }

        public override string GetActionDisplayName(NarrativeItem item) => "Equip";
    }

    /// <summary>
    /// 可装备物品基类。玩家可穿戴的物品。适配 UE5 UEquippableItem。
    /// GAS 相关（装备效果/能力）以字符串 ID 引用，待 GAS 阶段填充。
    /// </summary>
    public class EquippableItem : NarrativeItem
    {
        /// <summary>当前装备的槽位</summary>
        public GameplayTag CurrentSlot { get; set; } = GameplayTag.None;

        /// <summary>可装备到的槽位列表</summary>
        public GameplayTagContainer EquippableSlots { get; set; } = new GameplayTagContainer();

        /// <summary>
        /// 槽位声明的 JSON 可序列化写法（对应 UE5 资产上的 <c>EquippableSlots</c> 数组）。
        /// <see cref="GameplayTagContainer"/> 内部是无公开成员的集合，
        /// 直接反序列化会静默丢标签，所以 <see cref="ItemFactory"/> 走这个字符串列表，
        /// 由 <see cref="SyncEquippableSlotsFromNames"/> 落进容器。
        /// </summary>
        public List<string> EquippableSlotTagNames { get; set; } = new List<string>();

        /// <summary>装备时应用的效果 ID（GAS GameplayEffect 等价物）</summary>
        public string EquipmentEffectId { get; set; } = "";

        /// <summary>装备效果按标签的数值（SetByCaller 等价）</summary>
        public Dictionary<string, float> EquipmentEffectValues { get; set; } = new Dictionary<string, float>();

        /// <summary>装备时授予的能力 ID 列表</summary>
        public List<string> EquipmentAbilities { get; set; } = new List<string>();

        // 评分（正在逐步弃用，改用 EquipmentEffectValues）
        public float AttackRating { get; set; } = 0f;
        public float ArmorRating { get; set; } = 0f;
        public float StealthRating { get; set; } = 0f;

        /// <summary>角色形态特定的网格数据 key=形态标签</summary>
        public Dictionary<string, string> FormSpecificMeshData { get; set; } = new Dictionary<string, string>();

        /// <summary>默认网格资源路径</summary>
        public string ClothingMeshPath { get; set; } = "";

        public bool IsEquipped() => CurrentSlot.IsValid();

        public override bool ShouldUseOnAdd() => false;

        public override List<NarrativeItemUseAction> GetItemUseActions()
        {
            var actions = base.GetItemUseActions();
            foreach (var slot in EquippableSlots.GetTags())
            {
                actions.Add(new UseAction_Equip
                {
                    EquipToSlot = new GameplayTag(slot),
                    ActionDisplayName = "Equip",
                    ActionType = EItemUseActionType.Equip
                });
            }
            return actions;
        }

        /// <summary>
        /// 装备到指定槽位。返回是否成功。
        /// 对应 UE5 <c>UEquippableItem::EquipItem</c> → <c>UEquipmentComponent::EquipItem</c>：
        /// 除了置 CurrentSlot/HandleEquip/应用属性，还必须把物品登记进装备组件的槽位表，
        /// 否则 <c>GetEquippedItem(slot)</c>（毒药、外观、UI）永远读不到这件装备。
        /// </summary>
        public virtual bool EquipItem(GameplayTag desiredSlot)
        {
            SyncEquippableSlotsFromNames();

            if (!EquippableSlots.HasTag(desiredSlot))
            {
                NarrativeLog.LogWarning($"Item '{DisplayName}' cannot equip to slot '{desiredSlot}'");
                return false;
            }

            var equipComp = GetEquipmentComponent();
            if (equipComp == null)
            {
                NarrativeLog.LogWarning($"Item '{DisplayName}' has no equipment component");
                return false;
            }

            GameplayTag oldSlot = CurrentSlot;
            CurrentSlot = desiredSlot;

            // 登记进装备组件（同槽位上的旧装备会被它撤下）
            equipComp.EquipItemToSlot(this, desiredSlot);

            if (oldSlot.IsValid() && oldSlot != desiredSlot)
            {
                equipComp.UnequipItemFromSlot(oldSlot);
            }

            HandleEquip();
            ApplyEquipmentAttributes();
            NotifyModified();
            return true;
        }

        /// <summary>卸下物品。</summary>
        public virtual void UnequipItem()
        {
            if (!IsEquipped()) return;
            GameplayTag oldSlot = CurrentSlot;
            CurrentSlot = GameplayTag.None;
            HandleUnequip(oldSlot);
            RemoveEquipmentAttributes();

            // 从装备组件的槽位表摘掉，保持与 EquipItem 的登记对称
            GetEquipmentComponent()?.UnequipItemFromSlot(oldSlot);

            NotifyModified();
        }

        /// <summary>
        /// 声明一个可装备槽位（武器把自己的持握槽位登记进来，
        /// 使"拾取即持握"不依赖 JSON 是否写了 equippableSlots）。返回是否新增。
        /// </summary>
        public bool AddEquippableSlot(GameplayTag slot)
        {
            if (!slot.IsValid()) return false;
            if (EquippableSlots == null) EquippableSlots = new GameplayTagContainer();
            if (EquippableSlots.HasTag(slot)) return false;
            EquippableSlots.AddTag(slot);
            return true;
        }

        /// <summary>把 <see cref="EquippableSlotTagNames"/>（JSON 声明）落进 <see cref="EquippableSlots"/>，幂等。</summary>
        public void SyncEquippableSlotsFromNames()
        {
            if (EquippableSlotTagNames == null || EquippableSlotTagNames.Count == 0) return;
            foreach (var name in EquippableSlotTagNames)
            {
                if (string.IsNullOrEmpty(name)) continue;
                AddEquippableSlot(new GameplayTag(name));
            }
        }

        public override void Use(NarrativeItem otherItem = null)
        {
            if (IsEquipped()) UnequipItem();
            else EquipItem(EquippableSlots.Count > 0
                ? new GameplayTag(new List<string>(EquippableSlots.GetTags())[0])
                : GameplayTag.None);
        }

        public override void AddedToInventory(NarrativeInventoryComponent inventory, bool fromLoad)
        {
            base.AddedToInventory(inventory, fromLoad);
        }

        public override void RemovedFromInventory(NarrativeInventoryComponent inventory)
        {
            if (IsEquipped()) UnequipItem();
            base.RemovedFromInventory(inventory);
        }

        public override bool ShowActiveInUI() => IsEquipped();

        /// <summary>装备时的处理（默认设置角色网格，可覆盖）。</summary>
        public virtual void HandleEquip() { }

        /// <summary>卸下时的处理。</summary>
        /// <param name="oldSlot">卸下前所在的槽位</param>
        public virtual void HandleUnequip(GameplayTag oldSlot) { }

        /// <summary>装备属性效果的激活句柄（对应 UE5 FActiveGameplayEffectHandle EquipmentGEHandle）。</summary>
        public NarrativePro.GAS.ActiveGameplayEffectHandle EquipmentGEHandle
        {
            get => _equipmentGEHandle;
            protected set => _equipmentGEHandle = value;
        }

        private NarrativePro.GAS.ActiveGameplayEffectHandle _equipmentGEHandle;

        /// <summary>装备授予的能力句柄（对应 UE5 TMap&lt;FGameplayTag,FGameplayAbilitySpecHandle&gt; EquipmentAbilityHandles）。</summary>
        public List<NarrativePro.GAS.GameplayAbilitySpecHandle> EquipmentAbilityHandles { get; protected set; }
            = new List<NarrativePro.GAS.GameplayAbilitySpecHandle>();

        /// <summary>取拥有者的 ASC。</summary>
        protected NarrativePro.GAS.NarrativeAbilitySystemComponent GetOwningASC()
        {
            var pawn = GetOwningPawn();
            return pawn != null ? pawn.GetScript<NarrativePro.GAS.NarrativeAbilitySystemComponent>() : null;
        }

        /// <summary>
        /// 应用装备属性。对应 UE5 <c>UEquippableItem::ApplyEquipmentAttributes</c>：
        /// 解析 EquipmentEffectId 得到效果，用 SetByCaller 写入本物品的评分，
        /// 以 Infinite 效果应用到拥有者 ASC 上并记录句柄。
        /// </summary>
        public virtual void ApplyEquipmentAttributes()
        {
            var asc = GetOwningASC();
            if (asc == null) return;

            // 已经应用过则先撤掉，避免重复穿戴叠属性
            if (_equipmentGEHandle.IsValid)
            {
                asc.RemoveActiveEffect(_equipmentGEHandle);
                _equipmentGEHandle = NarrativePro.GAS.ActiveGameplayEffectHandle.Invalid;
            }

            var effect = ResolveEquipmentEffect();
            if (effect == null) return;

            var spec = new NarrativePro.GAS.GameplayEffectSpec(effect, asc);
            SetDefaultSetByCallerMagnitudes(spec);
            ModifyEquipmentEffectSpec(spec);

            // 把物品上配置的按标签数值覆盖进去（允许策划不改代码就调数值）
            if (EquipmentEffectValues != null)
            {
                foreach (var kvp in EquipmentEffectValues)
                {
                    if (string.IsNullOrEmpty(kvp.Key)) continue;
                    spec.SetSetByCallerMagnitude(new GameplayTag(kvp.Key), kvp.Value);
                }
            }

            _equipmentGEHandle = asc.ApplyGameplayEffectSpecToSelf(new NarrativePro.GAS.GameplayEffectSpecHandle(spec));

            // 装备能力（对应 UE5 EquipmentAbilities 逐个 GiveAbility）
            ApplyEquipmentAbilities(asc);
        }

        /// <summary>
        /// 解析装备效果。优先用 <see cref="EquipmentEffectId"/> 走 GAS 注册表；
        /// 未配置效果资产时，用本物品的评分合成一个 Infinite 效果（等价 UE5 用 GE 承载装备属性）。
        /// </summary>
        protected virtual NarrativePro.GAS.GameplayEffect ResolveEquipmentEffect()
        {
            if (!string.IsNullOrEmpty(EquipmentEffectId))
            {
                var loaded = NarrativePro.GAS.NarrativeGASAssetRegistry.ResolveEffect(EquipmentEffectId);
                if (loaded != null)
                {
                    // 注册表实例是共享定义，克隆后本物品持有独立副本
                    var clone = NarrativePro.GAS.NarrativeGASAssetRegistry.CloneEffect(loaded);
                    if (clone.DurationType != NarrativePro.GAS.EGameplayEffectDurationType.Infinite)
                    {
                        clone.DurationType = NarrativePro.GAS.EGameplayEffectDurationType.Infinite;
                    }
                    return clone;
                }
            }

            if (AttackRating == 0f && ArmorRating == 0f && StealthRating == 0f) return null;

            var synthesized = new NarrativePro.GAS.GameplayEffect($"Equipment_{(string.IsNullOrEmpty(DisplayName) ? ItemClassId : DisplayName)}")
            {
                DurationType = NarrativePro.GAS.EGameplayEffectDurationType.Infinite
            };
            if (AttackRating != 0f) synthesized.Modifiers.Add(new NarrativePro.GAS.GameplayModifierInfo { AttributeName = "AttackRating", ModifierOp = NarrativePro.GAS.EGameplayModOp.Add });
            if (ArmorRating != 0f) synthesized.Modifiers.Add(new NarrativePro.GAS.GameplayModifierInfo { AttributeName = "Armor", ModifierOp = NarrativePro.GAS.EGameplayModOp.Add });
            if (StealthRating != 0f) synthesized.Modifiers.Add(new NarrativePro.GAS.GameplayModifierInfo { AttributeName = "StealthRating", ModifierOp = NarrativePro.GAS.EGameplayModOp.Add });
            return synthesized;
        }

        /// <summary>
        /// 把本物品的评分写成 SetByCaller 值。
        /// 对应 UE5 在 ApplyEquipmentAttributes 里先 SetByCallerMagnitude(Armor/AttackRating/StealthRating)。
        /// </summary>
        protected virtual void SetDefaultSetByCallerMagnitudes(NarrativePro.GAS.GameplayEffectSpec spec)
        {
            if (spec == null) return;

            spec.SetSetByCallerMagnitude(NarrativePro.Core.NarrativeGameplayTags.SetByCaller_AttackRating, AttackRating);
            spec.SetSetByCallerMagnitude(NarrativePro.Core.NarrativeGameplayTags.SetByCaller_Armor, ArmorRating);
            spec.SetSetByCallerMagnitude(NarrativePro.Core.NarrativeGameplayTags.SetByCaller_StealthRating, StealthRating);
            spec.SetSetByCallerMagnitude(NarrativePro.Core.NarrativeGameplayTags.SetByCaller_AttackDamage, GetEquipmentAttackDamage());
        }

        /// <summary>装备提供的攻击伤害（武器覆写为自己的 AttackDamage）。</summary>
        protected virtual float GetEquipmentAttackDamage() => 0f;

        /// <summary>授予装备能力（对应 UE5 装备时 GiveAbility × EquipmentAbilities）。</summary>
        protected virtual void ApplyEquipmentAbilities(NarrativePro.GAS.NarrativeAbilitySystemComponent asc)
        {
            if (asc == null || EquipmentAbilities == null || EquipmentAbilities.Count == 0) return;

            EquipmentAbilityHandles = asc.GrantAbilities(EquipmentAbilities, this);
        }

        /// <summary>
        /// 移除装备属性。对应 UE5 <c>RemoveEquipmentAttributes</c>：撤掉 Infinite 效果并收回装备能力。
        /// </summary>
        public virtual void RemoveEquipmentAttributes()
        {
            var asc = GetOwningASC();
            if (asc == null)
            {
                _equipmentGEHandle = NarrativePro.GAS.ActiveGameplayEffectHandle.Invalid;
                return;
            }

            if (_equipmentGEHandle.IsValid)
            {
                asc.RemoveActiveEffect(_equipmentGEHandle);
                _equipmentGEHandle = NarrativePro.GAS.ActiveGameplayEffectHandle.Invalid;
            }

            if (EquipmentAbilityHandles != null && EquipmentAbilityHandles.Count > 0)
            {
                asc.RemoveAbilities(EquipmentAbilityHandles);
                EquipmentAbilityHandles = new List<NarrativePro.GAS.GameplayAbilitySpecHandle>();
            }
            else
            {
                asc.ClearAbilitiesFromSource(this);
            }
        }

        /// <summary>修改装备效果 Spec（子类可覆写以添加武器伤害等 SetByCaller 值）。签名保持 object 以兼容既有调用方。</summary>
        public virtual void ModifyEquipmentEffectSpec(object spec)
        {
            if (spec is NarrativePro.GAS.GameplayEffectSpec effectSpec)
            {
                ModifyEquipmentEffectSpec(effectSpec);
            }
        }

        /// <summary>修改装备效果 Spec 的强类型版本，供子类覆写。</summary>
        public virtual void ModifyEquipmentEffectSpec(NarrativePro.GAS.GameplayEffectSpec spec)
        {
            // 基类无额外字段可写；武器覆写以加入 AttackDamage
        }

        public override string GetStringVariable(string variableName)
        {
            switch (variableName)
            {
                case "AttackRating": return AttackRating.ToString("F0");
                case "ArmorRating": return ArmorRating.ToString("F0");
                case "StealthRating": return StealthRating.ToString("F0");
                default: return base.GetStringVariable(variableName);
            }
        }

        /// <summary>获取拥有者的装备组件。</summary>
        protected EquipmentComponent GetEquipmentComponent()
        {
            var pawn = GetOwningPawn();
            // 通过 Actor 查找 EquipmentComponent
            return pawn != null ? pawn.GetScript<EquipmentComponent>() : null;
        }
    }

    /// <summary>
    /// 服装物品，设置角色网格。
    /// </summary>
    public class EquippableItem_Clothing : EquippableItem
    {
        public override void HandleEquip()
        {
            ApplyClothingMesh();
        }

        public override void HandleUnequip(GameplayTag oldSlot)
        {
            // 卸下时把该部位还原回基础外观
            RestoreClothingMesh(oldSlot);
        }

        /// <summary>取角色的视觉组件（先看角色自身，再向下找一层子 Actor）。</summary>
        private NarrativePro.Character.NarrativeCharacterVisual ResolveVisual()
        {
            var pawn = GetOwningPawn();
            if (pawn == null) return null;

            var visual = pawn.GetScript<NarrativePro.Character.NarrativeCharacterVisual>();
            if (visual != null) return visual;

            // 视觉组件常挂在角色下的子 Actor 上
            foreach (var child in pawn.GetChildren<FlaxEngine.Actor>())
            {
                if (child == null) continue;
                visual = child.GetScript<NarrativePro.Character.NarrativeCharacterVisual>();
                if (visual != null) return visual;
            }
            return null;
        }

        /// <summary>
        /// 应用服装网格到角色：把 <see cref="EquippableItem.ClothingMeshPath"/>
        /// 装载到当前装备槽对应的网格组件上。
        /// 对应 UE5 服装装备写 SCS 网格的做法。
        /// </summary>
        protected virtual void ApplyClothingMesh()
        {
            var visual = ResolveVisual();
            if (visual == null)
            {
                NarrativeLog.LogWarning($"[Item] '{DisplayName}' 找不到角色视觉组件，服装网格未应用");
                return;
            }

            ApplyMeshToSlot(visual, CurrentSlot, ClothingMeshPath);
        }

        /// <summary>
        /// 卸下后按 oldSlot 把该部位恢复成默认（基础外观）网格。
        /// 对应 UE5 装备移除时回写 DefaultMesh。
        /// </summary>
        protected virtual void RestoreClothingMesh(GameplayTag oldSlot)
        {
            var visual = ResolveVisual();
            if (visual == null || !oldSlot.IsValid()) return;

            visual.ResetMeshToBaseAppearance(oldSlot);
        }

        /// <summary>把一个网格路径应用到指定槽位（骨骼/静态两种组件都支持）。</summary>
        protected virtual bool ApplyMeshToSlot(NarrativePro.Character.NarrativeCharacterVisual visual, GameplayTag slot, string meshPath)
        {
            if (visual == null || !slot.IsValid() || string.IsNullOrEmpty(meshPath)) return false;

            var skinned = FlaxEngine.Content.Load<FlaxEngine.SkinnedModel>(meshPath);
            if (skinned != null)
            {
                var animatedModel = visual.GetOrCreateMeshComponent(slot);
                if (animatedModel != null)
                {
                    animatedModel.SkinnedModel = skinned;
                    return true;
                }
            }

            var staticModel = FlaxEngine.Content.Load<FlaxEngine.Model>(meshPath);
            if (staticModel != null)
            {
                var staticComponent = visual.GetOrCreateStaticMeshComponent(slot);
                if (staticComponent != null)
                {
                    staticComponent.Model = staticModel;
                    return true;
                }
            }

            NarrativeLog.LogWarning($"[Item] '{DisplayName}' 的服装网格路径无法加载: {meshPath}");
            return false;
        }
    }
}
