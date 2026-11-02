using System;
using System.Collections.Generic;
using FlaxEngine;
using NarrativePro.Core;

namespace NarrativePro.Items
{
    /// <summary>
    /// 武器配件物品，可附加到 WeaponItem 上以修改其功能或外观。
    /// 适配 UE5 UWeaponAttachmentItem。
    /// 移除 UE5 中的复制/RPC，改为本地逻辑 + 事件回调。
    /// </summary>
    public class WeaponAttachmentItem : NarrativeItem
    {
        /// <summary>此配件当前所附加到的武器；未附加时为 null。</summary>
        public WeaponItem WeaponOwner { get; protected set; }

        /// <summary>所附加武器的 GUID，存档恢复时用于查找对应武器。</summary>
        public Guid WeaponOwnerGUID { get; set; } = Guid.Empty;

        /// <summary>此配件应装备到的武器配件槽位标签（属于 Narrative.Equipment.Weapon.AttachSlot 类别）</summary>
        public GameplayTag WeaponAttachmentSlot { get; set; } = GameplayTag.None;

        /// <summary>应创建并附加到武器的网格资源路径（对应 UE5 TObjectPtr&lt;UStaticMesh&gt;）</summary>
        public string AttachmentMeshPath { get; set; } = "";

        /// <summary>
        /// 配件的静态网格资产（对应 UE5 UStaticMesh* AttachmentMesh）。
        /// Flax 中静态网格资产是 FlaxEngine.Model；未显式赋值时按 AttachmentMeshPath 惰性加载。
        /// </summary>
        public Model AttachmentMesh
        {
            get
            {
                if (_attachmentMesh == null && !string.IsNullOrEmpty(AttachmentMeshPath))
                {
                    try
                    {
                        _attachmentMesh = Content.Load<Model>(AttachmentMeshPath);
                    }
                    catch (Exception ex)
                    {
                        NarrativeLog.LogWarning($"[WeaponAttachmentItem] 加载配件网格失败 {AttachmentMeshPath}: {ex.Message}");
                        AttachmentMeshPath = "";
                    }
                }
                return _attachmentMesh;
            }
            set => _attachmentMesh = value;
        }

        [NonSerialized]
        private Model _attachmentMesh;

        /// <summary>配件挂到武器骨骼/socket 上的本地偏移。对应 UE5 FWeaponAttachmentSlotConfig 之外的相对变换。</summary>
        public Transform AttachmentLocalTransform { get; set; } = Transform.Identity;

        /// <summary>配件附加的伤害加成</summary>
        public float DamageBonus { get; set; } = 0f;

        /// <summary>配件授予的能力 ID 列表</summary>
        public List<string> AttachmentAbilities { get; set; } = new List<string>();

        /// <summary>瞄准 FOV 覆盖值（非负值才生效）</summary>
        public float FOVOverride { get; set; } = -1f;

        /// <summary>瞄准时武器渲染 FOV 覆盖值（非负值才生效）</summary>
        public float WeaponRenderFOVOverride { get; set; } = -1f;

        /// <summary>瞄准时武器 FStop 覆盖值（非负值才生效）</summary>
        public float WeaponAimFStopOverride { get; set; } = -1f;

        public override bool bUsedWithOtherItem { get => true; set => base.bUsedWithOtherItem = value; }

        public WeaponAttachmentItem()
        {
            // 默认构造
        }

        public override void PostInventoryLoaded()
        {
            base.PostInventoryLoaded();
            // 尝试根据 WeaponOwnerGUID 恢复与所属武器的关联
            if (WeaponOwner == null && WeaponOwnerGUID != Guid.Empty && OwningInventory != null)
            {
                foreach (var item in OwningInventory.GetItems())
                {
                    if (item is WeaponItem weapon && weapon.ItemGUID == WeaponOwnerGUID)
                    {
                        WeaponOwner = weapon;
                        break;
                    }
                }
            }
        }

        public override void Use(NarrativeItem otherItem = null)
        {
            if (otherItem is WeaponItem weapon)
            {
                weapon.TryAddAttachment(this);
            }
        }

        public override bool CanUseItemWith(NarrativeItem testItem)
        {
            return testItem is WeaponItem weapon && weapon.WeaponAllowsAttachment(this);
        }

        public override bool ShouldUseOnAdd() => false;

        /// <summary>配件附加到武器时调用，子类可覆盖以处理附加逻辑。</summary>
        public virtual void HandleAttach(WeaponItem attachingTo)
        {
            if (attachingTo == null) return;
            WeaponOwner = attachingTo;
            WeaponOwnerGUID = attachingTo.ItemGUID;
        }

        /// <summary>配件从武器上卸下时调用，子类可覆盖以处理卸下逻辑。</summary>
        public virtual void HandleDetach(WeaponItem detachingFrom)
        {
            if (detachingFrom != null && WeaponOwner == detachingFrom)
            {
                WeaponOwner = null;
                // 注意：WeaponOwnerGUID 保留以便存档恢复
            }
        }

        /// <summary>
        /// 当所属武器被持握，或配件被附加到已持握的武器时调用。
        /// UE5 的 <c>HandleWield_Implementation</c> 原生为空（WeaponAttachmentItem.cpp:196-199），
        /// 是蓝图配件覆写"应用属性/显示网格"的钩子；Flax 数据驱动端口把等价行为落在基类：
        /// stat 修饰（<see cref="DamageBonus"/>）由 WeaponItem.GetBaseAttackDamage →
        /// GetAttachmentDamageBonus 汇入伤害结算链，这里负责配件视觉的即时挂载
        /// （转发 WeaponVisual.HandleAddAttachment，网格可见性随持握槽位由 OnRep_AttachState 驱动）。
        /// </summary>
        public virtual void HandleWield()
        {
            var weapon = WeaponOwner;
            if (weapon == null) return;
            if (!string.IsNullOrEmpty(AttachmentMeshPath) || _attachmentMesh != null)
            {
                weapon.AddAttachmentVisual(this);
            }
        }

        /// <summary>
        /// 当所属武器被收起，或配件从已持握的武器上卸下时调用。
        /// 与 <see cref="HandleWield"/> 对称：还原本槽位默认网格
        /// （对应 UE5 配件在 un-wield 时撤掉自己施加的状态；伤害加成随 WeaponAttachments 移除自然失效）。
        /// </summary>
        public virtual void HandleUnWield()
        {
            WeaponOwner?.RemoveAttachmentVisual(this);
        }

        /// <summary>设置此配件的所属武器（由 WeaponItem 内部调用）。</summary>
        public virtual void SetWeaponOwner(WeaponItem weaponOwner)
        {
            WeaponItem previous = WeaponOwner;
            WeaponOwner = weaponOwner;
            WeaponOwnerGUID = weaponOwner != null ? weaponOwner.ItemGUID : Guid.Empty;
            OnRep_WeaponOwner(previous);
        }

        /// <summary>所属武器变更时的回调（对应 UE5 OnRep_WeaponOwner）。</summary>
        public virtual void OnRep_WeaponOwner(WeaponItem previousOwner)
        {
            // 默认无操作，子类可覆盖
        }

        /// <summary>允许配件覆盖瞄准相机 FOV。返回 -1 表示不覆盖。</summary>
        public virtual float OverrideWeaponCameraFOV() => FOVOverride;

        /// <summary>允许配件覆盖武器渲染 FOV。返回 -1 表示不覆盖。</summary>
        public virtual float OverrideWeaponRenderFOV() => WeaponRenderFOVOverride;

        /// <summary>允许配件覆盖瞄准时的相机 FStop。返回 -1 表示不覆盖。</summary>
        public virtual float OverrideWeaponAimFStop() => WeaponAimFStopOverride;
    }
}
