using System;
using System.Collections.Generic;
using FlaxEngine;
using NarrativePro.Core;

namespace NarrativePro.Items
{
    /// <summary>武器配件槽配置。</summary>
    public class WeaponAttachmentSlotConfig
    {
        public string SocketName { get; set; } = "";
    }

    /// <summary>武器在槽位中的附加配置（骨骼/偏移）。</summary>
    public class WeaponAttachmentConfig
    {
        public string SocketName { get; set; } = "";
        public Transform Offset { get; set; } = Transform.Identity;
    }

    /// <summary>武器弹夹状态。</summary>
    public class WeaponClipState
    {
        /// <summary>弹夹中弹药数</summary>
        public int AmmoInClip { get; set; } = 0;

        /// <summary>非装备式弹药时，作为弹药源的物品实例</summary>
        public NarrativeItem AmmoItemSource { get; set; }

        /// <summary>弹药物品的 GUID（用于存档恢复）</summary>
        public Guid AmmoItemGUID { get; set; } = Guid.Empty;
    }

    /// <summary>
    /// 可装备武器基类。武器本身不含攻击逻辑，而是授予能力（由 GAS 阶段填充）。
    /// 适配 UE5 UWeaponItem。
    /// </summary>
    public class WeaponItem : EquippableItem
    {
        /// <summary>武器视觉 Actor 资源路径</summary>
        public string WeaponVisualPath { get; set; } = "";

        /// <summary>不同收起槽位的附加配置</summary>
        public Dictionary<string, WeaponAttachmentConfig> HolsterAttachmentConfigs { get; set; } = new Dictionary<string, WeaponAttachmentConfig>();

        /// <summary>不同持握槽位的附加配置</summary>
        public Dictionary<string, WeaponAttachmentConfig> WieldAttachmentConfigs { get; set; } = new Dictionary<string, WeaponAttachmentConfig>();

        /// <summary>十字准星 UI 资源路径</summary>
        public string CrosshairWidgetPath { get; set; } = "";

        /// <summary>持握手规则</summary>
        public EWeaponHandRule WeaponHand { get; set; } = EWeaponHandRule.Both;

        /// <summary>单独持握时授予的能力 ID 列表</summary>
        public List<string> WeaponAbilities { get; set; } = new List<string>();

        /// <summary>主手持握时授予的能力 ID 列表</summary>
        public List<string> MainhandWeaponAbilities { get; set; } = new List<string>();

        /// <summary>副手持握时授予的能力 ID 列表</summary>
        public List<string> OffhandWeaponAbilities { get; set; } = new List<string>();

        /// <summary>已授予能力的句柄（GAS 阶段填充）</summary>
        public List<string> WeaponAbilityHandles { get; set; } = new List<string>();

        /// <summary>Pawn 是否跟随控制器旋转</summary>
        public bool bPawnFollowsControlRotation { get; set; } = false;

        /// <summary>Pawn 是否朝向移动方向</summary>
        public bool bPawnOrientsRotationToMovement { get; set; } = true;

        /// <summary>基础攻击伤害</summary>
        public float AttackDamage { get; set; } = 0f;

        /// <summary>重击伤害倍率</summary>
        public float HeavyAttackDamageMultiplier { get; set; } = 1.5f;

        /// <summary>是否允许手动装弹</summary>
        public bool bAllowManualReload { get; set; } = true;

        /// <summary>武器配件配置（槽位标签 → 配置）</summary>
        public Dictionary<string, WeaponAttachmentSlotConfig> WeaponAttachmentConfiguration { get; set; } = new Dictionary<string, WeaponAttachmentSlotConfig>();

        /// <summary>所需弹药物品类 ID（空表示无需弹药）</summary>
        public string RequiredAmmo { get; set; } = "";

        /// <summary>机器人是否消耗弹药</summary>
        public bool bBotsConsumeAmmo { get; set; } = true;

        /// <summary>机器人攻击距离</summary>
        public float BotAttackRange { get; set; } = 1000f;

        /// <summary>弹夹容量</summary>
        public int ClipSize { get; set; } = 0;

        /// <summary>弹夹状态</summary>
        public WeaponClipState WeaponClipState { get; set; } = new WeaponClipState();

        /// <summary>当前持握的槽位</summary>
        public GameplayTag WieldedSlot { get; set; } = GameplayTag.None;

        /// <summary>当前配件（槽位标签 → 配件物品）</summary>
        public Dictionary<string, WeaponAttachmentItem> WeaponAttachments { get; set; } = new Dictionary<string, WeaponAttachmentItem>();

        /// <summary>上次攻击时间</summary>
        public float LastAttackTime { get; set; } = -9999f;

        /// <summary>消耗弹药。返回是否成功。</summary>
        public virtual bool ConsumeAmmo(int amount = 1)
        {
            if (ClipSize <= 0) return true; // 无弹夹武器不消耗
            if (WeaponClipState.AmmoInClip < amount) return false;
            WeaponClipState.AmmoInClip -= amount;
            NotifyModified();
            return true;
        }

        /// <summary>武器散布。</summary>
        public virtual float GetWeaponSpread() => 0f;

        /// <summary>获取收起槽位的附加配置。</summary>
        public WeaponAttachmentConfig GetWeaponHolsterAttachConfig(GameplayTag desiredSlot)
        {
            if (desiredSlot.IsValid() && HolsterAttachmentConfigs.TryGetValue(desiredSlot.TagName, out var cfg))
                return cfg;
            return new WeaponAttachmentConfig();
        }

        /// <summary>获取持握槽位的附加配置。</summary>
        public WeaponAttachmentConfig GetWeaponWieldAttachConfig(GameplayTag desiredSlot)
        {
            if (desiredSlot.IsValid() && WieldAttachmentConfigs.TryGetValue(desiredSlot.TagName, out var cfg))
                return cfg;
            return new WeaponAttachmentConfig();
        }

        /// <summary>武器显示名（可选显示配件/弹药）。</summary>
        public virtual string GetWeaponDisplayName(bool bShowAttachments, bool bShowAmmo)
        {
            string name = DisplayName;
            if (bShowAmmo && ClipSize > 0)
                name += $" ({GetAmmoInClip()}/{GetSpareAmmo()})";
            return name;
        }

        /// <summary>装弹（不播放特效，需特效用 GA_Reload）。</summary>
        public virtual bool Reload()
        {
            if (ClipSize <= 0) return false;
            int needed = ClipSize - WeaponClipState.AmmoInClip;
            if (needed <= 0) return false;

            int spare = GetSpareAmmo();
            if (spare <= 0) return false;

            int toLoad = Math.Min(needed, spare);
            WeaponClipState.AmmoInClip += toLoad;

            // 消耗备弹
            if (OwningInventory != null && !string.IsNullOrEmpty(RequiredAmmo))
            {
                OwningInventory.ConsumeItemsOfClass(RequiredAmmo, toLoad);
            }
            NotifyModified();
            return true;
        }

        /// <summary>弹夹中弹药数。</summary>
        public virtual int GetAmmoInClip() => WeaponClipState.AmmoInClip;

        /// <summary>备弹数量。</summary>
        public virtual int GetSpareAmmo()
        {
            if (string.IsNullOrEmpty(RequiredAmmo) || OwningInventory == null) return 0;
            return OwningInventory.GetTotalQuantityOfItem(RequiredAmmo);
        }

        /// <summary>弹夹容量。</summary>
        public virtual int GetClipSize() => ClipSize;

        /// <summary>当前使用的弹药源。</summary>
        public virtual NarrativeItem GetAmmoSource()
        {
            if (WeaponClipState.AmmoItemSource != null) return WeaponClipState.AmmoItemSource;
            if (!string.IsNullOrEmpty(RequiredAmmo) && OwningInventory != null)
                return OwningInventory.FindItemOfClass(RequiredAmmo);
            return null;
        }

        /// <summary>初始化弹药源。</summary>
        public virtual void InitAmmoSource()
        {
            if (WeaponClipState.AmmoItemSource == null && !string.IsNullOrEmpty(RequiredAmmo))
            {
                WeaponClipState.AmmoItemSource = OwningInventory?.FindItemOfClass(RequiredAmmo);
                if (WeaponClipState.AmmoItemSource != null)
                    WeaponClipState.AmmoItemGUID = WeaponClipState.AmmoItemSource.ItemGUID;
            }
        }

        /// <summary>获取指定槽位的配件。</summary>
        public virtual WeaponAttachmentItem GetAttachment(GameplayTag attachmentSlot)
        {
            if (!attachmentSlot.IsValid()) return null;
            return WeaponAttachments.TryGetValue(attachmentSlot.TagName, out var a) ? a : null;
        }

        /// <summary>返回连击动画（默认无，需连击的武器覆盖）。</summary>
        public virtual List<string> GetComboAnims(bool bHeavyAttack) => new List<string>();

        public bool IsHolstered() => !WieldedSlot.IsValid();
        public bool IsWielded() => WieldedSlot.IsValid();
        public bool WantsOrientRotationToMovement() => bPawnOrientsRotationToMovement;
        public bool WantsUseControllerRotationYaw() => bPawnFollowsControlRotation;

        /// <summary>是否需要自动装弹（通常弹夹空时）。</summary>
        public virtual bool RequiresAutoReload() => ClipSize > 0 && WeaponClipState.AmmoInClip <= 0;

        /// <summary>是否有弹药可发射。</summary>
        public virtual bool HasAmmo()
        {
            if (string.IsNullOrEmpty(RequiredAmmo)) return true;
            if (ClipSize > 0) return WeaponClipState.AmmoInClip > 0;
            return OwningInventory != null && OwningInventory.HasItem(RequiredAmmo, 1);
        }

        /// <summary>攻击时调用（由攻击能力调用）。</summary>
        public virtual void OnAttack()
        {
            LastAttackTime = Time.GameTime;
        }

        /// <summary>是否允许攻击。</summary>
        public virtual bool CanAttack() => HasAmmo();

        /// <summary>攻击范围。</summary>
        public virtual float GetAttackRange() => BotAttackRange;

        /// <summary>尝试添加配件。</summary>
        public virtual bool TryAddAttachment(WeaponAttachmentItem attachment)
        {
            if (attachment == null || !WeaponAllowsAttachment(attachment)) return false;
            AddAttachment(attachment);
            return true;
        }

        /// <summary>尝试移除配件。</summary>
        public virtual void TryRemoveAttachment(WeaponAttachmentItem attachment)
        {
            if (attachment == null) return;
            RemoveAttachment(attachment);
        }

        protected virtual void AddAttachment(WeaponAttachmentItem attachment)
        {
            if (attachment.WeaponAttachmentSlot.IsValid())
                WeaponAttachments[attachment.WeaponAttachmentSlot.TagName] = attachment;
            AddAttachmentVisual(attachment);
        }

        protected virtual void RemoveAttachment(WeaponAttachmentItem attachment)
        {
            if (attachment.WeaponAttachmentSlot.IsValid())
                WeaponAttachments.Remove(attachment.WeaponAttachmentSlot.TagName);
            RemoveAttachmentVisual(attachment);
        }

        protected virtual void AddAttachmentVisual(WeaponAttachmentItem attachment) { }
        protected virtual void RemoveAttachmentVisual(WeaponAttachmentItem attachment) { }

        /// <summary>测试是否允许添加配件。</summary>
        public virtual bool WeaponAllowsAttachment(WeaponAttachmentItem attachment) => attachment != null;

        /// <summary>是否能与另一武器双持。</summary>
        public virtual bool CanDualWieldWith(WeaponItem other) => WeaponHand == EWeaponHandRule.Either && other != null && other.WeaponHand == EWeaponHandRule.Either;

        /// <summary>持握时调用。</summary>
        public virtual void HandleWield() { }

        /// <summary>收起时调用。</summary>
        public virtual void HandleUnWield() { }

        /// <summary>授予武器能力（GAS 阶段填充）。</summary>
        public virtual void GiveWeaponAbilities() { }

        /// <summary>移除武器能力。</summary>
        public virtual void RemoveWeaponAbilities() { }

        /// <summary>持握到指定槽位。</summary>
        public virtual void WieldInSlot(GameplayTag desiredSlot)
        {
            GameplayTag old = WieldedSlot;
            WieldedSlot = desiredSlot;
            if (desiredSlot.IsValid()) HandleWield();
            else HandleUnWield();
            GiveWeaponAbilities();
        }

        public override void HandleEquip()
        {
            base.HandleEquip();
            InitAmmoSource();
        }

        public override void HandleUnequip(GameplayTag oldSlot)
        {
            RemoveWeaponAbilities();
            base.HandleUnequip(oldSlot);
        }

        public override string GetStringVariable(string variableName)
        {
            switch (variableName)
            {
                case "AttackDamage": return AttackDamage.ToString("F0");
                case "AmmoInClip": return GetAmmoInClip().ToString();
                case "SpareAmmo": return GetSpareAmmo().ToString();
                case "ClipSize": return ClipSize.ToString();
                default: return base.GetStringVariable(variableName);
            }
        }
    }
}
