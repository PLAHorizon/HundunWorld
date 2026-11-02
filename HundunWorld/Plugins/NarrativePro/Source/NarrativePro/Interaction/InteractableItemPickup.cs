using System;
using System.Collections.Generic;
using FlaxEngine;
using NarrativePro.Core;
using NarrativePro.Data;
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

        /// <summary>
        /// 是否已完整取走（防止重复拾取）。
        /// 之所以是公共字段而不是 <c>{ get; protected set; }</c>：状态归档器只收公共字段与
        /// 「公共读 + 公共写」的属性（NarrativeSaveStateArchive.cs:773-781 用
        /// <c>GetGetMethod(false)</c>/<c>GetSetMethod(false)</c> 取访问器，非公共 setter 会让整条成员被丢弃），
        /// 而 UE5 侧进入 SaveGame 归档的是 <c>PickupConfig</c>
        /// （Public/Interaction/InteractableItemPickup.h:82-83 的 <c>UPROPERTY(SaveGame)</c>）——
        /// 取走状态必须能落盘，否则读档时一件已被拿走的拾取物会重新站在原地。
        /// </summary>
        public bool bIsTaken = false;

        /// <summary>
        /// 当前拾取物对应的物品定义。对应 UE5 侧从 <c>PickupConfig.PickupClass</c> 取的 CDO
        /// （InteractableItemPickup.cpp:172、:208）。
        /// </summary>
        [NonSerialized]
        protected NarrativeItem CurrentPickupItem;

        /// <summary>缺 pickup 网格数据只警告一次（RefreshPickup 会在读档/设置配置时多次进入）。</summary>
        private bool _bLoggedMissingPickupMesh;

        /// <summary>存档：准备保存数据。</summary>
        public virtual void PrepareForSave()
        {
            // 数据已通过 PickupConfig/PickupSaveGUID 属性保存
        }

        /// <summary>
        /// 读档：恢复状态。UE5 依据（Private/Interaction/InteractableItemPickup.cpp:156-160）——
        /// <c>Load_Implementation</c> 只有一句无条件的 <c>RefreshPickup(PickupConfig)</c>，
        /// 没有「已取走就早退」的门：UE5 的 AItemPickup 里根本没有取走标记
        /// （Public/Interaction/InteractableItemPickup.h:36-99 全文无此字段），取走的拾取物在
        /// <c>TakePickup</c> 数量扣完后 <c>Destroy()</c>（cpp:111-114），<c>EndPlay</c> 的 Destroyed 分支
        /// 再 <c>RemoveSingleActor</c>（cpp:43-47），于是读档时它既不存在也没有记录。
        /// 端口把等价语义放进 <see cref="RefreshPickup"/> 的 UE5 数量闸门（cpp:202-204、244-250）：
        /// 取完的拾取物 QuantityToGive 已扣到 0，走的是「清空网格」那条 else 分支，而不是自造早退。
        /// </summary>
        public virtual void Load()
        {
            RefreshPickup(PickupConfig);
        }

        /// <summary>
        /// 读档后是否应重生。对应 UE5 <c>ShouldRespawn_Implementation</c>
        /// （InteractableItemPickup.cpp:151-154 返回 true，基类默认亦为 true，NarrativeSavableActor.cpp:16-19）。
        /// 拾取物是放置在世界里的动态 Actor：存档记录必须带 bNeedsDynamicSpawn，读档时缺失的拾取物才会被重建。
        /// </summary>
        public virtual bool ShouldRespawn() => true;

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

        /// <summary>
        /// 拾取被取走。返回是否成功。
        /// UE5 依据（Private/Interaction/InteractableItemPickup.cpp:94-125）：配置有效且
        /// <c>QuantityToGive > 0</c> 才继续（:96），从 Taker 上取背包组件（:103）后
        /// <c>TryAddItemFromClass</c>（:105）；只要 <c>AmountGiven > 0</c> 就从
        /// <c>QuantityToGive</c> 里扣掉实际给到的数量（:107-109），扣到 0 才 <c>Destroy()</c>
        /// （:111-114）—— 背包一时装不下的拾取物留在原地，下次交互接着取剩下的数量。
        /// UE5 的 <c>HasAuthority()</c>（:99）是网络复制门，端口按单机本地逻辑去掉。
        /// </summary>
        public virtual bool TakePickup(Actor taker)
        {
            if (bIsTaken) return false;
            if (PickupConfig == null
                || string.IsNullOrEmpty(PickupConfig.PickupClassId)
                || PickupConfig.QuantityToGive <= 0) return false;

            // 查找 taker 的背包
            var inventory = taker?.GetScript<NarrativeInventoryComponent>();
            if (inventory == null)
            {
                NarrativeLog.LogWarning($"[ItemPickup] Taker 无背包组件: {taker?.Name}");
                return false;
            }

            var addResult = inventory.TryAddItemFromClass(PickupConfig.PickupClassId, PickupConfig.QuantityToGive);
            if (addResult.AmountGiven <= 0) return false;

            NarrativeLog.Log($"[ItemPickup] {taker.Name} 拾取 {PickupConfig.PickupClassId} x{addResult.AmountGiven}");

            PickupConfig.QuantityToGive -= addResult.AmountGiven;
            if (PickupConfig.QuantityToGive > 0) return true;

            bIsTaken = true;
            RemoveTakenPickupFromScene();
            return true;
        }

        /// <summary>
        /// 把取完的拾取物连同其存档记录一起摘掉。
        /// UE5 依据（cpp:111-114 的 <c>Destroy()</c> → cpp:39-52 <c>EndPlay</c> 的
        /// <c>EEndPlayReason::Destroyed</c> 分支 <c>RemoveSingleActor</c>）：摘记录必须发生在销毁之前，
        /// 否则 <c>ShouldRespawn</c> 为真（cpp:151-154）会让读档把已取走的拾取物重新生成。
        /// </summary>
        private void RemoveTakenPickupFromScene()
        {
            var actor = Actor;
            if (actor == null) return;

            NarrativePro.SaveSystem.SaveSystemStatics.RemoveSingleActor(actor);
            Actor.Destroy(actor);
        }

        /// <summary>刷新拾取物视觉。子类可覆盖以自定义视觉表现。</summary>
        protected virtual void RefreshPickup(PickupConfiguration inPickupConfig)
        {
            if (inPickupConfig == null) return;

            // UE5 依据（cpp:202-204 的「配置有效且 QuantityToGive > 0」闸门 + cpp:244-250 的 else 分支）：
            // 配置无效或数量已经扣完时清空网格后返回，不留上一件物品的视觉。
            // 端口原先没有这道数量闸门，取完的拾取物（QuantityToGive 归零）读档后仍会把网格重新画出来。
            if (string.IsNullOrEmpty(inPickupConfig.PickupClassId) || inPickupConfig.QuantityToGive <= 0)
            {
                CurrentPickupItem = null;
                ClearPickupMesh();
                return;
            }

            // 根据物品类获取 pickup 网格数据（对应 UE5 RefreshPickup_Implementation :202-251）
            var item = ResolveItemDefinition(inPickupConfig.PickupClassId);
            CurrentPickupItem = item;
            if (item == null)
            {
                NarrativeLog.LogWarning($"[ItemPickup] 无法加载物品定义: {inPickupConfig.PickupClassId}");
                ClearPickupMesh();
                return;
            }

            // 名称先落位：UE5 只在流送回调里写名称（:170-176），而端口没有异步流送，
            // 物品没配网格时也必须让玩家看得见它叫什么。
            ApplyPickupNameText();

            var meshData = item.GetPickupMeshData(inPickupConfig.QuantityToGive);
            if (meshData != null && !string.IsNullOrEmpty(meshData.PickupMeshPath))
            {
                OnPickupDataReady(meshData);
            }
            else if (!_bLoggedMissingPickupMesh)
            {
                _bLoggedMissingPickupMesh = true;
                NarrativeLog.LogWarning(
                    $"[ItemPickup] 物品 '{inPickupConfig.PickupClassId}' 没有 pickup 网格数据" +
                    $"（JSON 缺 pickupMeshData.PickupMeshPath，目录 {NarrativeItemDatabase.ResolvedItemsDirectory}），拾取物不显示网格。");
            }
        }

        /// <summary>
        /// 解析拾取物对应的物品定义。
        /// UE5 直接取 <c>PickupConfig.PickupClass</c> 的 CDO（:172）；端口以 JSON 定义库为数据源，
        /// 按 ItemClassId 命中 Content/NarrativePro/Items/*.json，兼容旧调用：给的是文件路径时走 ItemFactory。
        /// </summary>
        protected virtual NarrativeItem ResolveItemDefinition(string itemClassId)
        {
            if (string.IsNullOrEmpty(itemClassId)) return null;

            NarrativeItemDatabase.Initialize();
            var template = NarrativeItemDatabase.GetTemplate(itemClassId);
            if (template != null) return template;

            return ItemFactory.LoadItem(itemClassId);
        }

        /// <summary>把物品显示名写进交互组件。对应 UE5 <c>SetInteractableNameText(ItemCDO-&gt;DisplayName)</c>（:172-174）。</summary>
        protected virtual void ApplyPickupNameText()
        {
            var item = CurrentPickupItem;
            if (item == null || PickupInteractable == null) return;
            if (string.IsNullOrEmpty(item.DisplayName)) return;

            PickupInteractable.SetInteractableNameText(item.DisplayName);
        }

        /// <summary>Pickup 网格数据就绪时应用到拾取物上。子类可覆盖以自定义处理。</summary>
        protected virtual void OnPickupDataReady(PickupMeshData data)
        {
            if (data == null) return;

            ApplyPickupNameText();

            if (string.IsNullOrEmpty(data.PickupMeshPath)) return;

            if (PickupMesh == null) PickupMesh = EnsurePickupMesh();
            if (PickupMesh == null) return;

            // UE5 :178-191：LoadSynchronous 取静态网格 → SetStaticMesh → 逐槽 SetMaterial。
            // Flax 的 Content.Load 本就同步，直接套用。
            var model = Content.Load<Model>(data.PickupMeshPath);
            if (model == null)
            {
                NarrativeLog.LogWarning($"[ItemPickup] pickup 网格资产加载失败: {data.PickupMeshPath}");
                return;
            }

            PickupMesh.Model = model;

            var materialPaths = data.PickupMeshMaterialPaths;
            if (materialPaths != null)
            {
                for (int i = 0; i < materialPaths.Count; i++)
                {
                    string materialPath = materialPaths[i];
                    if (string.IsNullOrEmpty(materialPath)) continue;

                    var material = Content.Load<Material>(materialPath);
                    if (material == null)
                    {
                        NarrativeLog.LogWarning($"[ItemPickup] pickup 材质资产加载失败: {materialPath}（槽位 {i}）");
                        continue;
                    }

                    // SetMaterial 不在 StaticModel 自己的成员表里（FlaxEngine.CSharp.xml 只列本类声明的成员），
                    // 它是继承来的：Flax 1.12 的 StaticModel 派生自 ModelInstanceActor
                    // （其 SetMaterial(Int32, MaterialBase) 见同一份 XML），
                    // 与 Character/NarrativeCharacterVisual.cs:414、:429 用的是同一条通路。
                    PickupMesh.SetMaterial(i, material);
                }
            }

            // 掉落偏移（NarrativeItem.h:144 的初衷：物品默认原点不好看）——
            // UE5 把它施加在 Actor 上（ActorFactoryNarrativeItem.cpp:31-32），端口只挪网格，
            // 免得覆盖存档/预制体里已经摆好的 Actor 变换。
            // Scale 门保留是防作者把 Transform 写成零缩放（那等于把拾取物缩到看不见）：
            // PickupMeshData 里的 Transform 能真绑上值，靠的是 Items/ItemFactory.cs 的 IncludeFields
            // （FlaxEngine.Transform 只有公共字段，不开这一项整份偏移都会静默归零）。
            if (data.PickupOffset != Transform.Identity && data.PickupOffset.Scale != Float3.Zero)
            {
                PickupMesh.LocalTransform = data.PickupOffset;
            }
        }

        /// <summary>
        /// 清空拾取物网格。对应 UE5 <c>RefreshPickup_Implementation</c> 的 else 分支
        /// <c>PickupMesh-&gt;SetStaticMesh(nullptr)</c>（Private/Interaction/InteractableItemPickup.cpp:244-250）。
        /// </summary>
        protected virtual void ClearPickupMesh()
        {
            if (PickupMesh != null) PickupMesh.Model = null;
        }

        /// <summary>没有网格组件时补建一个（对应 UE5 构造器 <c>CreateDefaultSubobject&lt;UStaticMeshComponent&gt;</c>，:80）。</summary>
        protected virtual StaticModel EnsurePickupMesh()
        {
            if (Actor == null) return null;

            // 新建而不复用既有子网格：预制体里的 StaticModel 子对象可能是作者摆的装饰，
            // 把拾取物的模型/材质/偏移糊上去会静默改掉场景外观。
            var mesh = Actor.AddChild<StaticModel>();
            if (mesh == null)
            {
                NarrativeLog.LogWarning($"[ItemPickup] 无法为 {Actor.Name} 创建 pickup 网格子 Actor");
                return null;
            }

            mesh.Name = "PickupMesh";
            mesh.LocalTransform = Transform.Identity;
            PickupMesh = mesh;
            return mesh;
        }

        public override void OnEnable()
        {
            base.OnEnable();
            // 自动查找 PickupInteractable
            if (PickupInteractable == null)
            {
                PickupInteractable = Actor.GetScript<PickupInteractable>();
            }

            // UE5 依据（Private/Interaction/InteractableItemPickup.cpp:29-37）：BeginPlay 里
            // <c>if (!USaveSystemStatics::LoadSingleActor(this)) RefreshPickup(PickupConfig);</c> ——
            // 存档接管了本 Actor 就不再按关卡里的默认配置建视觉，一切交给 Load_Implementation（:156-160）。
            // 端口不直接调 LoadSingleActor：读档时记录由 NarrativeSaveSubsystem 统一遍历还原
            // （SaveSystem/Subsystems/NarrativeSaveSubsystem.cs:1371-1473 的 LoadActorFromRecord
            // 还原完状态字节后才回调本脚本的 Load()），而它对「记录已标销毁」的分支会当场 DestroyActor
            // （:1376-1381 → :1700-1708），子系统自己把这类销毁推迟到遍历结束（:706-712 的注释），
            // 在脚本自身的 OnEnable 里销毁会打乱那个约定。所以这里只问「有没有我的记录」。
            // 没有记录 = 全新游戏或首次进关：不刷新的话场景里摆的拾取物就是一团空气。
            if (bIsTaken) return;
            if (HasSaveRecord()) return;

            RefreshPickup(PickupConfig);
        }

        public override void OnDisable()
        {
            // UE5 依据（cpp:39-52）：EndPlay 的 Destroyed 分支 RemoveSingleActor（:44-47），
            // else 分支 SaveSingleActor（:48-51）。端口此前只有前半条：关卡卸载/被禁用时拾取物的
            // 剩余数量与取走状态从不写回记录，取走也就无从跨存档生效。
            // Flax 脚本侧没有 EEndPlayReason，OnDisable 是等价的拆除钩子，用 bIsTaken 充当
            // 「因被取走而销毁」的判据（TakePickup 扣完数量才会走到销毁，见 cpp:111-114）。
            var actor = Actor;
            if (actor != null)
            {
                if (bIsTaken)
                {
                    NarrativePro.SaveSystem.SaveSystemStatics.RemoveSingleActor(actor);
                }
                else if (!IsMarkedDestroyedInSave())
                {
                    // 读档时存档侧会以「记录已销毁」为由销毁场景里那件已取走的拾取物，
                    // 那次销毁同样会走到这里；此时若再 SaveSingleActor 就会用刚构造的默认状态
                    // 覆盖掉 bDestroyed 标记（NarrativeSaveSubsystem.cs:1001-1019 是无条件覆写），
                    // 下一份存档它又会复活。UE5 没有这个坑是因为它的销毁走 EndPlayReason::Destroyed。
                    NarrativePro.SaveSystem.SaveSystemStatics.SaveSingleActor(actor);
                }
            }

            base.OnDisable();
        }

        /// <summary>本拾取物在存档对象里是否已有记录。</summary>
        private bool HasSaveRecord()
        {
            var subsystem = NarrativePro.SaveSystem.Subsystems.NarrativeSaveSubsystem.Instance;
            return subsystem != null && subsystem.DoesRecordExist(PickupSaveGUID);
        }

        /// <summary>本拾取物的记录是否已被标为销毁（取走完成的语义即写在此标记上）。</summary>
        private bool IsMarkedDestroyedInSave()
        {
            var save = NarrativePro.SaveSystem.SaveSystemStatics.GetSaveObject();
            if (save?.RecordMap == null) return false;
            return save.RecordMap.TryGetValue(PickupSaveGUID, out var record)
                   && record != null && record.bDestroyed;
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
            // 名称数据源与 ItemPickup 侧一致：按 ItemClassId 命中 JSON 定义库（ItemFactory.LoadItem 要的是文件路径）
            NarrativeItemDatabase.Initialize();
            var item = NarrativeItemDatabase.GetTemplate(config.PickupClassId) ?? ItemFactory.LoadItem(config.PickupClassId);
            return item?.DisplayName ?? config.PickupClassId;
        }

        /// <summary>
        /// 交互（取走拾取物）。UE5 依据（Private/Interaction/InteractableItemPickup.cpp:258-268）：
        /// <c>UPickupInteractable::Interact</c> 覆盖基类虚函数 <c>Interact(Interactor, InteractionComp)</c>
        /// （基类实现见 Interaction/InteractableComponent.cpp:388-401），取 Outer 上的 AItemPickup、
        /// 把交互者转成 ANarrativeCharacter，然后 <c>return Pickup->TakePickup(NChar)</c>；
        /// 它不调用基类，所以 CanInteract 只是给玩家看的提示门，不是取走的必经之路。
        /// 端口里这条虚函数同样就是交互系统的全部入口：没有槽位的对象走
        /// Interaction/PlayerInteractionComponent.cs:216 的即时交互（对应 UE5
        /// Interaction/PlayerInteractionComponent.cpp:119），有槽位的走 GAS/NarrativeInteractAbility.cs:120。
        /// UE5 的 ANarrativeCharacter 转换在端口等价于「交互者身上带 NarrativeInventoryComponent」——
        /// TakePickup 本来就只经背包组件才给得出东西（cpp:103-105），没有它取不走任何东西。
        /// </summary>
        public override bool Interact(Actor interactor, NarrativeInteractionComponent interactionComp)
        {
            // OwningPickup 正常在 OnEnable 里取；两个脚本的启用顺序不利时这里补一次
            var pickup = OwningPickup ?? Actor?.GetScript<ItemPickup>();
            if (pickup == null) return false;
            OwningPickup = pickup;

            return pickup.TakePickup(interactor);
        }
    }
}
