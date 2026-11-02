using System;
using FlaxEngine;
using NarrativePro.Core;
using NarrativePro.Interaction;
using NarrativePro.Items;

namespace NarrativePro.Tales.Tasks
{
    /// <summary>
    /// 「拾起指定物品」的任务条目：可选地把一个拾取物生成到世界里，再等玩家捡走。
    /// 对应 UE5 蓝图任务 <c>BPT_TakePickup</c>
    /// （Content/Pro/Core/Tales/Tasks/BPT_TakePickup.uasset）。
    /// <para>
    /// 二进制证据：变量 <c>PickupToFind</c>（Instanced <c>NarrativeActorProvider</c>，图里跑
    /// <c>AsyncAction_ProvideActor</c> 的 <c>OnProvided</c>/<c>OnFailed</c>）；取不到时
    /// <c>CallFunc_HasAuthority_ReturnValue</c> 过权威门，再用
    /// <c>CallFunc_GetNarrativeProSettings_ReturnValue</c> 拿
    /// <c>DefaultInteractablePickup</c> 类，经 <c>BeginDeferredActorSpawnFromClass</c> +
    /// <c>CallFunc_FinishSpawningActor_ReturnValue</c> 在 <c>PickupSpawnTransform</c> 生成，
    /// <c>K2Node_MakeStruct_PickupConfiguration</c>（<c>InPickupConfig_PickupClass</c> /
    /// <c>QuantityToGive</c>）+ <c>SetPickup</c> 配置内容，<c>PickupSaveGUID</c> 做存档标识，
    /// 完成后绑 <c>OnDestroyed</c> → <c>CompleteTask</c>，并实现
    /// <c>GetNavigationMarkerAttachActor</c> 与 <c>SpawnDefaultNavigationMarker</c>。
    /// </para>
    /// <para>
    /// 端口差异：① UE5 的「Actor 被销毁即完成」在 Flax 没有可绑的蓝图
    /// <c>OnDestroyed</c>（<c>FlaxEngine.CSharp.xml</c> 无 ActorDestroyed 通路），
    /// 改用 <see cref="ItemPickup.bIsTaken"/>（TakePickup 成功时才置位，
    /// InteractableItemPickup.cs:96-120）+ 背包 <c>OnItemAdded</c> 两路信号；
    /// ② UE5 用 <c>BeginDeferredActorSpawnFromClass</c>，端口走
    /// <see cref="Quest.SpawnQuestActor"/>（Prefabric 路径 + 存档登记，UE5 侧同样是
    /// SpawnActor 后 <c>SetActorGUID</c> 的可存档 Actor）；③ <c>Transform</c> 端口按
    /// 位置 + 朝向欧拉角三轴建模，避免依赖未经核实的 Transform JSON 反序列化。
    /// </para>
    /// </summary>
    [Serializable]
    public class TakePickupTask : NarrativeTask
    {
        /// <summary>要捡起来的物品类 ID。对应 UE5 <c>PickupConfiguration.PickupClass</c>。</summary>
        public string PickupClassId { get; set; } = "";

        /// <summary>生成时给拾取物配置的数量。对应 <c>QuantityToGive</c>。</summary>
        public int QuantityToGive { get; set; } = 1;

        /// <summary>场景中已有拾取物的提供者。对应 UE5 <c>PickupToFind</c>。</summary>
        public NarrativePro.Core.NarrativeActorProvider PickupProvider { get; set; }

        /// <summary>找不到现成拾取物时是否自己生成一个。对应图里的 spawn 分支。</summary>
        public bool bSpawnIfMissing { get; set; } = true;

        /// <summary>拾取物 Prefab 路径；留空时用 <c>ArsenalSettings.DefaultInteractablePickup</c>。</summary>
        public string PickupPrefabPath { get; set; } = "";

        /// <summary>生成位置。对应 UE5 <c>PickupSpawnTransform</c> 的平移部分。</summary>
        public Vector3 PickupSpawnLocation { get; set; } = Vector3.Zero;

        /// <summary>生成朝向（欧拉角，度）。对应 UE5 <c>PickupSpawnTransform</c> 的旋转部分。</summary>
        public Vector3 PickupSpawnRotationDegrees { get; set; } = Vector3.Zero;

        private ItemPickup _pickup;
        private Actor _pickupActor;
        private NarrativeInventoryComponent _inventory;
        private ProvideActorRequest _provideRequest;
        private int _baselineCount;

        protected override void OnBeginTask()
        {
            if (string.IsNullOrEmpty(PickupClassId))
            {
                NarrativeLog.LogWarning("TakePickupTask: PickupClassId 为空，不知道要捡什么");
                return;
            }

            _inventory = BuiltinTaskSupport.GetInventory(this);
            if (_inventory != null)
            {
                _baselineCount = _inventory.GetTotalQuantityOfItem(PickupClassId);
                _inventory.OnItemAdded += HandleItemAdded;
            }

            // 端口没有可绑的 Actor.OnDestroyed，靠轮询 bIsTaken 收尾（见类型注释的端口差异）。
            TickInterval = 0.5f;

            var actor = BuiltinTaskSupport.ResolveTargetActor(PickupProvider, null, Guid.Empty);
            if (actor != null)
            {
                AttachPickup(actor);
                return;
            }

            if (PickupProvider != null)
            {
                _provideRequest = new ProvideActorRequest(this, PickupProvider, HandlePickupProvided);
                if (_provideRequest != null && _provideRequest.IsPending && bSpawnIfMissing)
                    TrySpawnPickup();
                return;
            }

            TrySpawnPickup();
        }

        private void HandlePickupProvided(Actor actor)
        {
            _provideRequest = null;
            if (!bIsActive || actor == null) return;
            AttachPickup(actor);
        }

        private void AttachPickup(Actor actor)
        {
            _pickupActor = actor;
            _pickup = actor.GetScript<ItemPickup>();
            if (_pickup == null)
                NarrativeLog.LogWarning($"TakePickupTask: Actor '{actor.Name}' 上没有 ItemPickup 脚本");
        }

        /// <summary>
        /// 生成拾取物。对应 UE5 的 deferred spawn + <c>SetPickup</c>。
        /// 只在权威端执行（<c>CallFunc_HasAuthority_ReturnValue</c> 同口径）。
        /// </summary>
        private void TrySpawnPickup()
        {
            var comp = NarrativeComp;
            if (comp != null && !comp.HasAuthority())
            {
                NarrativeLog.Log("TakePickupTask: 非权威端不生成拾取物");
                return;
            }

            if (!bSpawnIfMissing) return;

            string prefabPath = PickupPrefabPath;
            if (string.IsNullOrEmpty(prefabPath))
                prefabPath = ArsenalStatics.GetNarrativeProSettings()?.DefaultInteractablePickup ?? "";

            if (string.IsNullOrEmpty(prefabPath))
            {
                NarrativeLog.LogWarning("TakePickupTask: 未指定拾取物 Prefab，且设置里没有默认拾取物");
                return;
            }

            if (OwningQuest == null)
            {
                NarrativeLog.LogWarning("TakePickupTask: 没有归属任务，无法登记生成拾取物的存档");
                return;
            }

            var transform = new Transform(PickupSpawnLocation,
                                          Quaternion.Euler(PickupSpawnRotationDegrees));
            if (!OwningQuest.SpawnQuestActor(prefabPath, transform, out var stableActor))
            {
                NarrativeLog.LogWarning($"TakePickupTask: 生成拾取物失败（{prefabPath}）");
                return;
            }

            bool bValid;
            var actor = NarrativePro.SaveSystem.StableActorStatics.GetStableActor(stableActor, out bValid);
            if (!bValid || actor == null) return;

            AttachPickup(actor);
            _pickup?.SetPickup(new PickupConfiguration
            {
                PickupClassId = PickupClassId,
                QuantityToGive = QuantityToGive > 0 ? QuantityToGive : 1,
            });
        }

        protected override void TickTask()
        {
            if (IsComplete()) return;

            // 拾取物被取走时 TakePickup 会先置 bIsTaken 再销毁自身（InteractableItemPickup.cs:107-120）。
            // 读已销毁脚本的属性的行为在 Flax 侧未取证，故整段包护：异常即视为拾取物已消失。
            try
            {
                if (_pickup != null && _pickup.bIsTaken)
                {
                    CompleteTask();
                    return;
                }
            }
            catch (Exception ex)
            {
                NarrativeLog.Log("TakePickupTask: 拾取物已销毁：" + ex.Message);
                _pickup = null;
                _pickupActor = null;
                CompleteTask();
                return;
            }

            if (_inventory != null &&
                _inventory.GetTotalQuantityOfItem(PickupClassId) - _baselineCount >= RequiredQuantity)
            {
                CompleteTask();
            }
        }

        protected override void OnEndTask()
        {
            _provideRequest?.Cancel();
            _provideRequest = null;

            if (_inventory != null)
            {
                _inventory.OnItemAdded -= HandleItemAdded;
                _inventory = null;
            }
            _pickup = null;
            _pickupActor = null;
        }

        private void HandleItemAdded(NarrativeInventoryComponent source, ItemAddResult result)
        {
            if (!bIsActive || source != _inventory || result == null) return;
            if (!string.Equals(result.ItemClassId, PickupClassId, StringComparison.Ordinal)) return;

            int gained = source.GetTotalQuantityOfItem(PickupClassId) - _baselineCount;
            if (gained > 0)
                SetProgress(gained);
        }

        /// <summary>标记挂到拾取物上（对应图内实现的 GetNavigationMarkerAttachActor）。</summary>
        public override Actor GetNavigationMarkerAttachActor()
        {
            return _pickupActor;
        }

        public override Vector3 GetNavigationMarkerLocation()
        {
            if (_pickupActor != null) return Vector3.Zero;
            return PickupSpawnLocation;
        }

        public override string GetTaskDescription()
        {
            if (!string.IsNullOrEmpty(DescriptionOverride)) return DescriptionOverride;
            return QuantityToGive > 1 ? $"拾取 {PickupClassId} x{QuantityToGive}" : $"拾取 {PickupClassId}";
        }
    }
}
