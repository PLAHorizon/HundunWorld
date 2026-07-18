using System;
using System.Collections.Generic;
using FlaxEngine;
using NarrativePro.Core;
using NarrativePro.Items;

namespace NarrativePro.Interaction
{
    /// <summary>
    /// 拾取配置。适配 UE5 FPickupConfiguration。
    /// </summary>
    [Serializable]
    public class PickupConfiguration
    {
        /// <summary>拾取物品的类 ID（对应 UE TSubclassOf&lt;UNarrativeItem&gt;）</summary>
        public string PickupClassId { get; set; } = "";

        /// <summary>拾取物品数量</summary>
        public int QuantityToGive { get; set; } = 1;
    }

    /// <summary>
    /// 物品拾取 Actor。挂载到场景的物品掉落物。
    /// 适配 UE5 AItemPickup，移除复制/RPC，改为本地逻辑 + 事件回调。
    /// 实现 INarrativeSavableActor 通过 PrepareForSave/Load/ShouldRespawn 方法。
    /// </summary>
    public class ItemPickup : Script
    {
        /// <summary>拾取配置（保存到存档）</summary>
        public PickupConfiguration PickupConfig { get; set; } = new PickupConfiguration();

        /// <summary>存档 GUID（用于存档恢复）</summary>
        public Guid PickupSaveGUID { get; set; } = Guid.NewGuid();

        /// <summary>静态网格组件（拾取物视觉）</summary>
        public StaticModel PickupMesh;

        /// <summary>关联的拾取交互组件（自动查找或手动指定）</summary>
        public PickupInteractable PickupInteractable;

        /// <summary>是否已拾取（防止重复拾取）</summary>
        public bool bIsTaken { get; protected set; } = false;

        /// <summary>存档：准备保存数据。</summary>
        public virtual void PrepareForSave()
        {
            // 数据已通过 PickupConfig/PickupSaveGUID 属性保存
        }

        /// <summary>读档：恢复状态。</summary>
        public virtual void Load()
        {
            // 子类可覆盖以恢复拾取状态
            if (bIsTaken)
            {
                // 已被拾取的拾取物根据 ShouldRespawn 决定是否隐藏
                if (!ShouldRespawn())
                {
                    var actor = Actor;
                    if (actor != null) Actor.Destroy(actor);
                }
            }
            else
            {
                RefreshPickup(PickupConfig);
            }
        }

        /// <summary>读档后是否应重生。</summary>
        public virtual bool ShouldRespawn() => false;

        /// <summary>设置存档 GUID。</summary>
        public void SetActorGUID(Guid savedGUID) => PickupSaveGUID = savedGUID;

        /// <summary>获取存档 GUID。</summary>
        public Guid GetActorGUID() => PickupSaveGUID;

        /// <summary>设置拾取配置。</summary>
        public virtual void SetPickup(PickupConfiguration inPickupConfig)
        {
            PickupConfig = inPickupConfig;
            RefreshPickup(inPickupConfig);
        }

        /// <summary>获取拾取配置。</summary>
        public PickupConfiguration GetPickupConfig() => PickupConfig;

        /// <summary>拾取被取走。返回是否成功。</summary>
        public virtual bool TakePickup(Actor taker)
        {
            if (bIsTaken) return false;
            if (string.IsNullOrEmpty(PickupConfig.PickupClassId)) return false;

            // 查找 taker 的背包
            var inventory = taker?.GetScript<NarrativeInventoryComponent>();
            if (inventory == null)
            {
                NarrativeLog.LogWarning($"[ItemPickup] Taker 无背包组件: {taker?.Name}");
                return false;
            }

            var addResult = inventory.TryAddItemFromClass(PickupConfig.PickupClassId, PickupConfig.QuantityToGive);
            if (addResult.AddedSomeItems || addResult.AddedAllItems)
            {
                bIsTaken = true;
                NarrativeLog.Log($"[ItemPickup] {taker.Name} 拾取 {PickupConfig.PickupClassId} x{PickupConfig.QuantityToGive}");
                // 销毁自身
                var actor = Actor;
                if (actor != null) Actor.Destroy(actor);
                return true;
            }
            return false;
        }

        /// <summary>刷新拾取物视觉。子类可覆盖以自定义视觉表现。</summary>
        protected virtual void RefreshPickup(PickupConfiguration inPickupConfig)
        {
            if (PickupMesh == null) return;

            // 根据物品类获取 pickup 网格数据
            var item = ItemFactory.LoadItem(inPickupConfig.PickupClassId);
            if (item == null)
            {
                NarrativeLog.LogWarning($"[ItemPickup] 无法加载物品定义: {inPickupConfig.PickupClassId}");
                return;
            }

            var meshData = item.GetPickupMeshData(inPickupConfig.QuantityToGive);
            if (meshData != null && !string.IsNullOrEmpty(meshData.PickupMeshPath))
            {
                OnPickupDataReady(meshData);
            }
        }

        /// <summary>Pickup 网格数据流加载完成时调用。子类可覆盖以自定义处理。</summary>
        protected virtual void OnPickupDataReady(PickupMeshData data)
        {
            // 子类可覆盖以异步加载网格资源
            // 当前仅记录日志，实际设置 PickupMesh.Model 由子类或外部代码处理
            NarrativeLog.Log($"[ItemPickup] Pickup 网格数据就绪: {data.PickupMeshPath}");
        }

        public override void OnEnable()
        {
            base.OnEnable();
            // 自动查找 PickupInteractable
            if (PickupInteractable == null)
            {
                PickupInteractable = Actor.GetScript<PickupInteractable>();
            }
        }

        public override void OnDisable()
        {
            base.OnDisable();
        }
    }

    /// <summary>
    /// 拾取交互组件。挂到 ItemPickup 上，提供交互接口。
    /// 适配 UE5 UPickupInteractable。
    /// </summary>
    public class PickupInteractable : NarrativeInteractableComponent
    {
        /// <summary>当前拾取物 Actor（自动从 Owner 获取）</summary>
        public ItemPickup OwningPickup { get; protected set; }

        public override void OnEnable()
        {
            base.OnEnable();
            OwningPickup = Actor.GetScript<ItemPickup>();
            InteractableActionText = "拾取";
        }

        public override bool CanInteract(Actor interactor, NarrativeInteractionComponent interactionComp, out string errorText)
        {
            errorText = "";
            if (OwningPickup == null)
            {
                errorText = "拾取物未初始化";
                return false;
            }
            if (OwningPickup.bIsTaken)
            {
                errorText = "已被拾取";
                return false;
            }
            var inventory = interactor?.GetScript<NarrativeInventoryComponent>();
            if (inventory == null)
            {
                errorText = "无背包组件";
                return false;
            }
            return true;
        }

        public override string GetInteractableNameText(Actor interactor, NarrativeInteractionComponent interactionComp)
        {
            var config = OwningPickup?.PickupConfig;
            if (config == null || string.IsNullOrEmpty(config.PickupClassId)) return "未知物品";
            // 加载物品定义获取显示名
            var item = ItemFactory.LoadItem(config.PickupClassId);
            return item?.DisplayName ?? config.PickupClassId;
        }

        /// <summary>交互（拾取）逻辑。由 NarrativeInteractionComponent.Interact 调用。</summary>
        internal bool Pickup(Actor interactor, NarrativeInteractionComponent interactionComp)
        {
            if (OwningPickup == null) return false;
            return OwningPickup.TakePickup(interactor);
        }
    }
}
