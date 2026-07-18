using System;
using System.Collections.Generic;
using FlaxEngine;
using NarrativePro.Core;

namespace NarrativePro.Items
{
    /// <summary>
    /// 物品预览窗中可显示的属性。
    /// </summary>
    public class NarrativeItemStat
    {
        public string StatDisplayName { get; set; } = "Stat Display Name";
        public string StringVariable { get; set; } = "";
        public string StatTooltip { get; set; } = "";
    }

    /// <summary>
    /// 物品掉落时生成 pickup 的网格数据。
    /// </summary>
    public class PickupMeshData
    {
        /// <summary>pickup 显示的网格资源路径</summary>
        public string PickupMeshPath { get; set; } = "";
        /// <summary>pickup 网格的材质资源路径列表</summary>
        public List<string> PickupMeshMaterialPaths { get; set; } = new List<string>();
        /// <summary>掉落后额外的偏移变换</summary>
        public Transform PickupOffset { get; set; } = Transform.Identity;
    }

    /// <summary>
    /// 物品片段基类，支持组合式数据扩展。例如弹药片段可加到任何物品上。
    /// </summary>
    public abstract class NarrativeItemFragment
    {
        /// <summary>返回此片段提供的所有使用动作。</summary>
        public virtual List<NarrativeItemUseAction> GetItemUseActions() => new List<NarrativeItemUseAction>();
    }

    /// <summary>
    /// 定义物品的一种使用方式。物品通常至少有一种使用方式。
    /// </summary>
    public class NarrativeItemUseAction
    {
        public string ActionDisplayName { get; set; } = "Use";
        public EItemUseActionType ActionType { get; set; } = EItemUseActionType.Default;

        /// <summary>使用动作是否当前可用（不可用时在 UI 中隐藏）。</summary>
        public virtual bool IsEnabled() => true;

        /// <summary>执行物品使用逻辑。</summary>
        public virtual bool OnUse(NarrativeItem item, NarrativeItem otherItem) => true;

        /// <summary>是否需要另一个物品（例如给武器涂毒、安装配件）。</summary>
        public virtual bool IsMultiItemUse(NarrativeItem item) => false;

        /// <summary>若为多物品使用，返回此物品可作用的所有物品。</summary>
        public virtual bool GetItemsUsableWith(NarrativeItem item, List<NarrativeItem> outItems) => false;

        public virtual string GetActionDisplayName(NarrativeItem item) => ActionDisplayName;
    }

    /// <summary>
    /// 物品基类，可加入背包的所有物品的根类。
    /// 适配 UE5 UNarrativeItem，移除复制/RPC，改为本地逻辑 + 事件回调。
    /// GAS 相关（能力/效果）以字符串 ID 引用，待 GAS 阶段填充。
    /// </summary>
    public class NarrativeItem
    {
        /// <summary>物品类标识（对应 UE 的 TSubclassOf），用于从定义创建实例。</summary>
        public string ItemClassId { get; set; } = "";

        /// <summary>物品实例 GUID，主要用于按 GUID 保存/恢复引用。</summary>
        public Guid ItemGUID { get; set; } = Guid.NewGuid();

        /// <summary>缩略图资源路径</summary>
        public string ThumbnailPath { get; set; } = "";

        /// <summary>使用音效资源路径</summary>
        public string UseSoundPath { get; set; } = "";

        /// <summary>背包中显示名</summary>
        public string DisplayName { get; set; } = "";

        /// <summary>可选描述</summary>
        public string Description { get; set; } = "";

        /// <summary>物品重量（kg）</summary>
        public float Weight { get; set; } = 0f;

        /// <summary>物品基础价值，用于商人</summary>
        public int BaseValue { get; set; } = 0;

        /// <summary>物品"好坏"评分，AI 选择最佳武器/自动食用时使用</summary>
        public float BaseScore { get; set; } = 0f;

        /// <summary>物品标签</summary>
        public GameplayTagContainer ItemTags { get; set; } = new GameplayTagContainer();

        /// <summary>是否自动添加调用 Use 的默认使用动作</summary>
        public bool bAddDefaultUseOption { get; set; } = true;

        /// <summary>使用时是否消耗 1 个</summary>
        public virtual bool bConsumeOnUse { get; set; } = false;

        /// <summary>使用时是否需要选择另一个目标物品</summary>
        public virtual bool bUsedWithOtherItem { get; set; } = false;

        /// <summary>使用动作文本（装备/食用 等）</summary>
        public string UseActionText { get; set; } = "Use";

        /// <summary>两次使用之间的冷却时间（秒）</summary>
        public float UseRechargeDuration { get; set; } = 0f;

        /// <summary>是否可激活</summary>
        public bool bCanActivate { get; set; } = false;

        /// <summary>使用时是否切换激活/停用</summary>
        public bool bToggleActiveOnUse { get; set; } = false;

        /// <summary>是否激活</summary>
        public bool bActive { get; set; } = false;

        /// <summary>是否忙碌（如加载资源时）</summary>
        public bool bIsBusy { get; set; } = false;

        /// <summary>是否收藏</summary>
        public bool bFavourite { get; set; } = false;

        /// <summary>是否可堆叠</summary>
        public bool bStackable { get; set; } = false;

        /// <summary>最大堆叠数</summary>
        public int MaxStackSize { get; set; } = 2;

        /// <summary>是否需要 TickItem 调用</summary>
        public bool bWantsTickByDefault { get; set; } = false;

        /// <summary>拥有此物品的 AI 将获得这些活动（活动类 ID 列表）</summary>
        public List<string> ActivitiesToGrant { get; set; } = new List<string>();

        /// <summary>UI 中显示的属性列表</summary>
        public List<NarrativeItemStat> Stats { get; set; } = new List<NarrativeItemStat>();

        /// <summary>拥有此物品的背包</summary>
        public NarrativeInventoryComponent OwningInventory { get; set; }

        /// <summary>数量</summary>
        public int Quantity { get; protected set; } = 1;

        /// <summary>上次使用时间</summary>
        public float LastUseTime { get; protected set; } = -9999f;

        /// <summary>掉落 pickup 网格数据</summary>
        public PickupMeshData PickupMeshData { get; set; } = new PickupMeshData();

        /// <summary>片段列表（组合式数据）</summary>
        public List<NarrativeItemFragment> Fragments { get; set; } = new List<NarrativeItemFragment>();

        /// <summary>物品修改事件</summary>
        public event Action<NarrativeItem> OnItemModified;

        public NarrativeItem()
        {
            if (ItemGUID == Guid.Empty) ItemGUID = Guid.NewGuid();
        }

        public bool IsActive() => bActive;
        public int GetQuantity() => Quantity;
        public float GetStackWeight() => Quantity * Weight;
        public bool IsStackFull() => Quantity >= MaxStackSize;
        public int GetMaxStackSize() => bStackable ? MaxStackSize : 1;
        public int GetStackSpace() => GetMaxStackSize() - GetQuantity();
        public float GetLastUseTime() => LastUseTime;
        public float GetItemScore() => BaseScore > 0f ? BaseScore : BaseValue;

        public void SetActive(bool newActive, bool force = false)
        {
            if (bActive == newActive && !force) return;
            bool old = bActive;
            bActive = newActive;
            if (bActive) Activated();
            else Deactivated();
            OnRep_bActive(old);
        }

        public void SetBusy(bool newBusy) { bIsBusy = newBusy; NotifyModified(); }

        public void SetQuantity(int newQuantity)
        {
            int old = Quantity;
            Quantity = Math.Max(0, newQuantity);
            if (old != Quantity) OnRep_Quantity(old);
        }

        protected virtual void OnRep_bActive(bool oldActive) => NotifyModified();
        protected virtual void OnRep_Quantity(int oldQuantity) => NotifyModified();

        public void SetLastUseTime(float newLastUseTime) { LastUseTime = newLastUseTime; }

        /// <summary>若 bUsedWithOtherItem，定义此物品可用于哪些物品。</summary>
        public virtual bool CanUseItemWith(NarrativeItem testItem) => false;

        /// <summary>UI 中是否显示激活勾选。</summary>
        public virtual bool ShowActiveInUI() => bCanActivate;

        /// <summary>此物品是否可被移除（丢弃）。任务物品可覆盖返回 false。</summary>
        public virtual bool CanBeRemoved() => true;

        /// <summary>是否可使用。</summary>
        public virtual bool CanUse() => true;

        /// <summary>加入背包时是否自动使用。</summary>
        public virtual bool ShouldUseOnAdd() => false;

        /// <summary>是否在背包中显示。</summary>
        public virtual bool ShouldShowInInventory() => true;

        /// <summary>返回所有使用方式。</summary>
        public virtual List<NarrativeItemUseAction> GetItemUseActions()
        {
            var actions = new List<NarrativeItemUseAction>();
            if (bAddDefaultUseOption)
            {
                actions.Add(new NarrativeItemUseAction
                {
                    ActionDisplayName = string.IsNullOrEmpty(UseActionText) ? "Use" : UseActionText,
                    ActionType = EItemUseActionType.Default
                });
            }
            foreach (var frag in Fragments)
            {
                actions.AddRange(frag.GetItemUseActions());
            }
            return actions;
        }

        /// <summary>尝试使用物品。返回是否成功。</summary>
        public virtual bool TryUse(NarrativeItem otherItem = null)
        {
            if (!CanUse()) return false;

            float now = Time.GameTime;
            if (UseRechargeDuration > 0f && (now - LastUseTime) < UseRechargeDuration)
                return false;

            Use(otherItem);
            LastUseTime = now;

            if (bConsumeOnUse && OwningInventory != null)
            {
                OwningInventory.ConsumeItem(this, 1);
            }

            if (bCanActivate && bToggleActiveOnUse)
            {
                SetActive(!bActive);
            }

            OwningInventory?.NotifyItemUsed(this);
            return true;
        }

        /// <summary>使用时定义物品行为。</summary>
        /// <param name="otherItem">拖拽到的目标物品（例如配件安装目标武器）。多数物品不需要。</param>
        public virtual void Use(NarrativeItem otherItem = null) { }

        /// <summary>每帧调用（需 EnableItemTick）。</summary>
        public virtual void TickItem(float deltaTime) { }

        public virtual void AddedToInventory(NarrativeInventoryComponent inventory, bool fromLoad)
        {
            OwningInventory = inventory;
            if (!fromLoad && ShouldUseOnAdd())
            {
                TryUse();
            }
        }

        public virtual void RemovedFromInventory(NarrativeInventoryComponent inventory)
        {
            if (OwningInventory == inventory) OwningInventory = null;
        }

        /// <summary>背包加载完成后调用。</summary>
        public virtual void PostInventoryLoaded() { }

        public void NotifyModified() => OnItemModified?.Invoke(this);

        /// <summary>启用/禁用物品 Tick。</summary>
        public virtual void EnableItemTick(bool enable)
        {
            OwningInventory?.SetItemTickEnabled(this, enable);
        }

        /// <summary>原始描述（可被子类覆盖以动态生成）。</summary>
        public virtual string GetRawDescription() => Description;

        /// <summary>解析变量后的描述。</summary>
        public virtual string GetParsedDescription()
        {
            if (string.IsNullOrEmpty(Description)) return "";
            string result = Description;
            // 简单变量替换 {VarName}
            int start;
            while ((start = result.IndexOf('{')) >= 0)
            {
                int end = result.IndexOf('}', start);
                if (end < 0) break;
                string varName = result.Substring(start + 1, end - start - 1);
                string value = GetStringVariable(varName);
                result = result.Substring(0, start) + value + result.Substring(end + 1);
            }
            return result;
        }

        /// <summary>覆盖以支持描述中的自定义变量，例如 {PlayerName}。</summary>
        public virtual string GetStringVariable(string variableName)
        {
            switch (variableName)
            {
                case "ItemName": return DisplayName;
                case "Weight": return Weight.ToString("F1");
                case "StackWeight": return GetStackWeight().ToString("F1");
                case "Quantity": return Quantity.ToString();
                case "RechargeDuration": return UseRechargeDuration.ToString("F1");
                case "MaxStackSize": return MaxStackSize.ToString();
                default: return "";
            }
        }

        /// <summary>激活时调用。</summary>
        public virtual void Activated() { }

        /// <summary>停用时调用。</summary>
        public virtual void Deactivated() { }

        /// <summary>获取指定类型的片段。</summary>
        public T GetFragment<T>() where T : NarrativeItemFragment
        {
            foreach (var f in Fragments)
                if (f is T t) return t;
            return null;
        }

        /// <summary>获取指定类型的所有片段。</summary>
        public List<T> GetFragments<T>() where T : NarrativeItemFragment
        {
            var result = new List<T>();
            foreach (var f in Fragments)
                if (f is T t) result.Add(t);
            return result;
        }

        /// <summary>获取 pickup 网格数据。</summary>
        public virtual PickupMeshData GetPickupMeshData(int quantityToGet) => PickupMeshData;

        /// <summary>获取拥有者 Pawn（通过 OwningInventory）。</summary>
        public Actor GetOwningPawn() => OwningInventory?.GetOwningPawn();

        /// <summary>获取拥有者 Controller。</summary>
        public object GetOwningController() => OwningInventory?.GetOwningController();
    }
}
