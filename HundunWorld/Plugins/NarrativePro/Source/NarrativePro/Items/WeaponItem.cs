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
    /// 装备武器时持有者胶囊体的旋转方式。对应 UE5 <c>ECapsuleRotationSetting</c>
    /// （NarrativeCharacter.h:229-245）。这些模式互斥，故收敛为单一枚举。
    /// </summary>
    public enum ECapsuleRotationSetting
    {
        /// <summary>CMC 不做任何旋转 —— 适合由外部（如根运动/载具）控制胶囊体。</summary>
        NoRotation,

        /// <summary>CMC 按自身旋转速率朝移动方向转向胶囊体。</summary>
        OrientTowardsMovement,

        /// <summary>bUseControllerRotationYaw=true，胶囊体直接吸附到控制器 Yaw。</summary>
        UseControllerYawDirect,

        /// <summary>同上，但不吸附，而是用 CMC 旋转速率平滑转到控制器 Yaw。</summary>
        UseControllerYawSmoothed,
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

        /// <summary>
        /// 能力 → 输入标签覆盖（键为能力 ID/路径，值为标签全名，如 "Narrative.Input.AltAttack"）。
        /// 未覆盖的按能力数据定义（<c>Content/NarrativePro/GAS/Abilities/*.json</c> 的 <c>inputTag</c>）取，
        /// 两处都没有时保持 None（该能力只能由代码/事件激活）。
        /// </summary>
        public Dictionary<string, string> WeaponAbilityInputTags { get; set; } = new Dictionary<string, string>(StringComparer.Ordinal);

        /// <summary>主手持握时授予的能力 ID 列表</summary>
        public List<string> MainhandWeaponAbilities { get; set; } = new List<string>();

        /// <summary>副手持握时授予的能力 ID 列表</summary>
        public List<string> OffhandWeaponAbilities { get; set; } = new List<string>();

        /// <summary>已授予能力的句柄（GAS 阶段填充）</summary>
        public List<string> WeaponAbilityHandles { get; set; } = new List<string>();

        /// <summary>武器跟随控制器旋转时的朝向方式。对应 UE5 <c>OwnerCapsuleRotationSetting</c>（WeaponItem.h:207），UE5 构造默认为 UseControllerYawSmoothed（WeaponItem.cpp:46）。</summary>
        public ECapsuleRotationSetting OwnerCapsuleRotationSetting { get; set; } = ECapsuleRotationSetting.UseControllerYawSmoothed;

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

        /// <summary>
        /// 本发开火所用弹药物品上的 <see cref="AmmoFragment"/>，由 <see cref="ConsumeAmmo"/> 记录；无弹药片段时为 null。
        /// 对应 UE5 开火路径从弹药 Item 读取 UAmmoFragment 的伤害/追踪/投射物覆盖。
        /// </summary>
        public AmmoFragment FiredAmmoFragment { get; private set; }

        /// <summary>取当前弹夹弹药源上的 AmmoFragment（对应 UE5 UAmmoItem 上的 UAmmoFragment）。</summary>
        public AmmoFragment GetLoadedAmmoFragment() => WeaponClipState?.AmmoItemSource?.GetFragment<AmmoFragment>();

        /// <summary>消耗弹药。返回是否成功。对应 UE5 <c>UWeaponItem::ConsumeAmmo</c>。</summary>
        public virtual bool ConsumeAmmo(int amount = 1)
        {
            if (ClipSize <= 0)
            {
                // 无弹夹武器不消耗，但仍记录本发弹药片段，供伤害/追踪覆盖读取
                FiredAmmoFragment = GetLoadedAmmoFragment();
                return true;
            }
            if (WeaponClipState.AmmoInClip < amount) return false;
            WeaponClipState.AmmoInClip -= amount;
            FiredAmmoFragment = GetLoadedAmmoFragment();
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

        /// <summary>
        /// 取武器视觉挂载骨骼名。对应 UE5 <c>UWeaponItem::GetWeaponVisualAttachBone</c>（WeaponItem.cpp:776-790）：
        /// 优先当前持握槽位的附加配置，缺失时回退主手配置，再无则返回空串（UE5 空 FName）。
        /// </summary>
        public string GetWeaponVisualAttachBone()
        {
            if (WieldedSlot.IsValid() && WieldAttachmentConfigs.TryGetValue(WieldedSlot.TagName, out var cfg))
            {
                return cfg.SocketName;
            }

            // 回退：仅返回主手配置
            var mainhand = NarrativePro.Core.NarrativeGameplayTags.Weapon_WieldSlot_Mainhand;
            if (mainhand.IsValid() && WieldAttachmentConfigs.TryGetValue(mainhand.TagName, out var mainCfg))
            {
                return mainCfg.SocketName;
            }
            return "";
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

        /// <summary>
        /// 主手轻击连击动画集注册键（对应 UE5 武器上的 UNarrativeAnimSet 数据资产引用；
        /// 由 <see cref="NarrativePro.GAS.NarrativeCombatAnimSets.Resolve"/> 解析成实例）。空 = 无连击动画。
        /// </summary>
        public string ComboAnimSetId { get; set; } = "";

        /// <summary>副手连击动画集注册键；空时回退 <see cref="ComboAnimSetId"/>（对应 UE5 主/副手 AnimSet 区分）。</summary>
        public string OffhandComboAnimSetId { get; set; } = "";

        /// <summary>重击连击动画集注册键；空时取 <see cref="ComboAnimSetId"/> 解析集的 HeavyAttackCombos 段（对应 UE5 重击独立 AnimSet）。</summary>
        public string HeavyComboAnimSetId { get; set; } = "";

        /// <summary>按当前持握手侧 + 轻/重解析连击动画集。无键或解析不到返回 null。</summary>
        public NarrativePro.GAS.AttackComboAnimSet ResolveComboAnimSet(bool bHeavyAttack)
        {
            bool bOffhand = WieldedSlot.IsValid() &&
                WieldedSlot == NarrativeGameplayTags.Equipment.WieldSlot_Offhand;

            string key;
            if (bHeavyAttack && !string.IsNullOrEmpty(HeavyComboAnimSetId)) key = HeavyComboAnimSetId;
            else if (bOffhand && !string.IsNullOrEmpty(OffhandComboAnimSetId)) key = OffhandComboAnimSetId;
            else key = ComboAnimSetId;

            return NarrativePro.GAS.NarrativeCombatAnimSets.Resolve(key);
        }

        /// <summary>
        /// 返回本武器的连击段动画集。对应 UE5 <c>UWeaponItem::GetComboAnims</c> 返回
        /// <c>TArray&lt;UNarrativeAnimSet*&gt;</c>（WeaponItem.h:337；基类实现返回空数组，WeaponItem.cpp:717）。
        /// 消费者：<c>NarrativeCombatAbility.BeginMeleeAttackWindow / CreateAttackNotifyEvent</c>
        /// （连击轮转与本次挥击动画选择）。
        /// </summary>
        public virtual List<NarrativePro.GAS.NarrativeAnimSet> GetComboAnims(bool bHeavyAttack)
        {
            var set = ResolveComboAnimSet(bHeavyAttack);
            if (set == null) return new List<NarrativePro.GAS.NarrativeAnimSet>();

            if (bHeavyAttack)
            {
                // 独立重击动画集资产：其轻击段即重击挥击序列（UE5 惯例是重击 AnimSet 单独成资产）
                bool bStandaloneHeavy = !string.IsNullOrEmpty(HeavyComboAnimSetId);
                if (bStandaloneHeavy && set.AttackCombos != null && set.AttackCombos.Count > 0)
                    return set.AttackCombos;
                if (set.HeavyAttackCombos != null && set.HeavyAttackCombos.Count > 0)
                    return set.HeavyAttackCombos;
            }

            return set.AttackCombos ?? new List<NarrativePro.GAS.NarrativeAnimSet>();
        }

        public bool IsHolstered() => !WieldedSlot.IsValid();
        public bool IsWielded() => WieldedSlot.IsValid();
        public bool WantsOrientRotationToMovement() => bPawnOrientsRotationToMovement;
        public bool WantsUseControllerRotationYaw() => bPawnFollowsControlRotation;

        /// <summary>
        /// 解析手持本武器时持有者胶囊体应采用的旋转设置。
        /// 对应 UE5 NarrativeCharacter.cpp:1574（<c>GetWeapon(true)->OwnerCapsuleRotationSetting</c>）。
        /// </summary>
        public virtual ECapsuleRotationSetting ResolveOwnerCapsuleRotation() => OwnerCapsuleRotationSetting;

        /// <summary>
        /// 把武器胶囊体旋转设置解析成一个目标 Yaw 旋转（供武器视觉/朝向计算消费）。
        /// NoRotation 时原样返回当前胶囊旋转；控制器类模式用 <paramref name="controllerYawDegrees"/>；
        /// 移动朝向类模式用 <paramref name="movementYawDegrees"/>。角度为度（UE5 FRotator 口径）。
        /// </summary>
        public Quaternion ResolveCapsuleRotation(float controllerYawDegrees, float movementYawDegrees, Quaternion currentCapsuleRotation)
        {
            switch (ResolveOwnerCapsuleRotation())
            {
                case ECapsuleRotationSetting.UseControllerYawDirect:
                case ECapsuleRotationSetting.UseControllerYawSmoothed:
                    return Quaternion.RotationY(controllerYawDegrees * Mathf.DegreesToRadians);
                case ECapsuleRotationSetting.OrientTowardsMovement:
                    return Quaternion.RotationY(movementYawDegrees * Mathf.DegreesToRadians);
                case ECapsuleRotationSetting.NoRotation:
                default:
                    return currentCapsuleRotation;
            }
        }

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
            if (attachment == null) return;
            if (attachment.WeaponAttachmentSlot.IsValid())
                WeaponAttachments[attachment.WeaponAttachmentSlot.TagName] = attachment;
            attachment.HandleAttach(this);

            // 武器已持握时补上配件才触发挥执行为（对应 UE5 在已持握武器上装配件立即生效）
            if (IsWielded()) attachment.HandleWield();
            AddAttachmentVisual(attachment);
        }

        protected virtual void RemoveAttachment(WeaponAttachmentItem attachment)
        {
            if (attachment == null) return;
            if (attachment.WeaponAttachmentSlot.IsValid())
                WeaponAttachments.Remove(attachment.WeaponAttachmentSlot.TagName);
            if (IsWielded()) attachment.HandleUnWield();
            RemoveAttachmentVisual(attachment);
            attachment.HandleDetach(this);
        }

        // UE5 中为 public virtual（WeaponItem.h:140-141）；WeaponVisual 需从外部驱动配件视觉，故保持公开。
        // 实现：把请求转发给该武器当前挂载的 WeaponVisual（对应 UE5 武器视觉组件的
        // HandleAddAttachment/HandleRemoveAttachment 分支）。武器视觉尚未挂载（未持握）时安全跳过，
        // 持握挂载后 WeaponVisual.OnRep/Refresh 会再次经由本方法补齐全部配件视觉。
        public virtual void AddAttachmentVisual(WeaponAttachmentItem attachment)
        {
            if (attachment == null) return;
            var visual = ResolveWeaponVisual();
            if (visual == null) return;

            var slot = attachment.WeaponAttachmentSlot;
            WeaponAttachmentSlotConfig cfg =
                slot.IsValid() && WeaponAttachmentConfiguration != null &&
                WeaponAttachmentConfiguration.TryGetValue(slot.TagName, out var found) && found != null
                    ? found
                    : new WeaponAttachmentSlotConfig();
            visual.HandleAddAttachment(attachment, cfg);
        }

        public virtual void RemoveAttachmentVisual(WeaponAttachmentItem attachment)
        {
            if (attachment == null) return;
            var visual = ResolveWeaponVisual();
            visual?.HandleRemoveAttachment_Implementation(attachment);
        }

        /// <summary>取本武器当前挂载的武器视觉脚本（持握时由 WeaponVisualMount 挂载）。</summary>
        protected NarrativePro.Weapons.WeaponVisual ResolveWeaponVisual()
        {
            // GetOwningPawn() 在本端口直接返回宿主 Actor（见 WieldWeaponVisual 的用法），无二次 .Actor
            return NarrativePro.Weapons.WeaponVisual.FindVisualForWeapon(this, GetOwningPawn());
        }

        /// <summary>全部已装配件提供的攻击伤害加成之和（配件 stat 修饰经伤害链生效）。</summary>
        public float GetAttachmentDamageBonus()
        {
            float bonus = 0f;
            if (WeaponAttachments != null)
            {
                foreach (var attachment in WeaponAttachments.Values)
                {
                    if (attachment != null) bonus += attachment.DamageBonus;
                }
            }
            return bonus;
        }

        /// <summary>测试是否允许添加配件。</summary>
        public virtual bool WeaponAllowsAttachment(WeaponAttachmentItem attachment) => attachment != null;

        /// <summary>是否能与另一武器双持。</summary>
        public virtual bool CanDualWieldWith(WeaponItem other) => WeaponHand == EWeaponHandRule.Either && other != null && other.WeaponHand == EWeaponHandRule.Either;

        /// <summary>
        /// 持握时调用。对应 UE5 <c>HandleWield_Implementation</c>（WeaponItem.cpp:327-354）：
        /// 通知全部配件 → 应用装备属性 → 授予武器能力 → 初始化弹药源。
        /// 没有拥有者时记一条警告（等价 UE5 的 "wield skipped due to invalid owner"）。
        /// </summary>
        public virtual void HandleWield()
        {
            if (GetOwningPawn() == null)
            {
                NarrativeLog.LogWarning($"[Weapon] '{DisplayName}' 无有效拥有者，持握被跳过");
                return;
            }

            foreach (var attachment in WeaponAttachments.Values)
            {
                if (attachment != null) attachment.HandleWield();
            }

            ApplyEquipmentAttributes();

            // 先挂视觉再授能力：近战攻击在授予后立刻触发时也要能按武器反查到 WeaponVisual
            WieldWeaponVisual();

            GiveWeaponAbilities();
            InitAmmoSource();
        }

        /// <summary>
        /// 收起时调用。对应 UE5 <c>HandleUnWield_Implementation</c>（:356-380）：
        /// 撤装备属性 → 收回武器能力 → 通知全部配件。
        /// </summary>
        public virtual void HandleUnWield()
        {
            RemoveEquipmentAttributes();
            RemoveWeaponAbilities();
            UnwieldWeaponVisual();

            foreach (var attachment in WeaponAttachments.Values)
            {
                if (attachment != null) attachment.HandleUnWield();
            }
        }

        /// <summary>
        /// 武器能否"加进背包即自动持握"（拾取/初始装备走这条，
        /// 商店交易与容器转移传 bCheckAutoUse=false 时不会自动换装）。
        /// </summary>
        public override bool ShouldUseOnAdd() => true;

        /// <summary>
        /// 使用武器：未持握则装备并持握到本武器应有的手侧，已持握则收起。
        /// 对应 UE5 背包里点"装备/持握"（<c>UseAction_Equip::OnUse</c> → <c>EquipItem</c> →
        /// <c>UEquipmentComponent::WieldWeapon</c>），端口此前没有任何调用方，武器永远不会进持握态。
        /// </summary>
        public override void Use(NarrativeItem otherItem = null)
        {
            var equipment = GetEquipmentComponent();
            if (IsWielded())
            {
                var heldSlot = WieldedSlot;
                equipment?.UnwieldWeapon(heldSlot);
                UnequipItem();
                return;
            }

            var wieldSlot = ResolveWieldSlot();
            if (!wieldSlot.IsValid()) return;

            // 武器把自己的持握槽位声明为可用装备槽（数据没写 equippableSlots 时也能装备）
            AddEquippableSlot(wieldSlot);

            if (!EquipItem(wieldSlot))
            {
                NarrativeLog.LogWarning($"[Weapon] '{DisplayName}' 装备到槽位 '{wieldSlot.TagName}' 失败");
                return;
            }

            if (equipment == null)
            {
                NarrativeLog.LogWarning($"[Weapon] '{DisplayName}' 找不到 EquipmentComponent，无法持握");
                return;
            }

            // EquipItem → HandleEquip 已经把它持握上去了（对应 UE5 装备即持握）；
            // 只有那条路没走通时才在这里补一次 WieldWeapon → WieldInSlot → HandleWield
            if (!IsWielded())
            {
                equipment.WieldWeapon(this, wieldSlot);
            }
        }

        /// <summary>
        /// 本武器应持握到的槽位标签。对应 UE5 按 <c>WeaponHand</c> 选主/副手。
        /// </summary>
        public virtual GameplayTag ResolveWieldSlot()
        {
            switch (WeaponHand)
            {
                case EWeaponHandRule.Offhand:
                    return NarrativeGameplayTags.Equipment.WieldSlot_Offhand;
                case EWeaponHandRule.Mainhand:
                case EWeaponHandRule.Both:
                case EWeaponHandRule.Either:
                default:
                    return NarrativeGameplayTags.Equipment.WieldSlot_Mainhand;
            }
        }

        /// <summary>
        /// 武器是否需要弹药：声明了弹药类型、或有弹夹容量。
        /// 对应 UE5 只有需要弹药的武器才在攻击前过 HasAmmo 门槛（NarrativeCombatAbility.cpp:95-108）。
        /// </summary>
        public virtual bool NeedsAmmo() => !string.IsNullOrEmpty(RequiredAmmo) || ClipSize > 0;

        /// <summary>
        /// 武器提供给攻击者的基础攻击伤害（含弹药伤害覆盖与配件伤害加成）。
        /// 攻击能力据此算伤害，重击再乘 <see cref="HeavyAttackDamageMultiplier"/>。
        /// </summary>
        public float GetBaseAttackDamage() => GetEquipmentAttackDamage() + GetAttachmentDamageBonus();

        /// <summary>挂载武器视觉（持握时调用，对应 UE5 AddWeaponVisual）。</summary>
        protected virtual void WieldWeaponVisual()
        {
            var character = GetOwningPawn()?.GetScript<NarrativePro.UnrealFramework.NarrativeCharacter>();
            NarrativePro.Weapons.WeaponVisualMount.Attach(this, character);
        }

        /// <summary>收起武器视觉（对应 UE5 RemoveWeaponVisual）。</summary>
        protected virtual void UnwieldWeaponVisual()
        {
            NarrativePro.Weapons.WeaponVisualMount.Detach(this);
        }

        /// <summary>武器提供的攻击伤害走装备效果（对应 UE5 ModifyEquipmentEffectSpec 的 SetByCaller_AttackDamage）。</summary>
        /// <summary>
        /// 装备提供的攻击伤害：武器基础伤害；若本发弹药带 AmmoFragment 且设置了 AmmoDamageOverride，
        /// 则使用弹药的伤害覆盖。对应 UE5 开火时读取 UAmmoFragment::AmmoDamageOverride。
        /// </summary>
        protected override float GetEquipmentAttackDamage()
        {
            var ammo = FiredAmmoFragment ?? GetLoadedAmmoFragment();
            if (ammo != null && ammo.AmmoDamageOverride > 0f) return ammo.AmmoDamageOverride;
            return AttackDamage;
        }

        /// <summary>
        /// 武器覆写装备效果 Spec：把 AttackDamage 写进 SetByCaller。
        /// 对应 UE5 <c>UWeaponItem::ModifyEquipmentEffectSpec</c>（:382-391）。
        /// </summary>
        public override void ModifyEquipmentEffectSpec(NarrativePro.GAS.GameplayEffectSpec spec)
        {
            base.ModifyEquipmentEffectSpec(spec);
            if (spec != null)
            {
                spec.SetSetByCallerMagnitude(
                    NarrativePro.Core.NarrativeGameplayTags.SetByCaller_AttackDamage,
                    AttackDamage + GetAttachmentDamageBonus());
            }
        }

        /// <summary>
        /// 授予武器能力。对应 UE5 <c>UWeaponItem::GiveWeaponAbilities</c>（WeaponItem.cpp:393-409）：
        /// 双持时按当前持握的手侧取对应能力表，单持用 <see cref="WeaponAbilities"/>；
        /// 授予来源记录为本武器，供 <see cref="RemoveWeaponAbilities"/> 精确回滚。
        /// </summary>
        public virtual void GiveWeaponAbilities()
        {
            WeaponAbilityHandles = new List<string>();

            var character = GetOwningPawn();
            if (character == null) return;

            var asc = character.GetScript<NarrativePro.GAS.NarrativeAbilitySystemComponent>();
            if (asc == null) return;

            // 用整体持握状态而非本武器的槽位来判断是否双持（UE5 同注释）
            var equipment = character.GetScript<EquipmentComponent>();
            int wieldCount = equipment?.WieldedWeapons?.Count ?? 0;
            if (wieldCount == 0 && IsWielded()) wieldCount = 1;

            List<string> abilitiesToGrant = WeaponAbilities;
            if (wieldCount > 1)
            {
                abilitiesToGrant = WieldedSlot == NarrativeGameplayTags.Equipment.WieldSlot_Mainhand
                    ? MainhandWeaponAbilities
                    : OffhandWeaponAbilities;
            }

            if (abilitiesToGrant == null || abilitiesToGrant.Count == 0) return;

            // 逐条解析输入标签：武器覆盖优先，缺位交给 ASC（它会再回退能力数据定义与实例自带的 InputTag）。
            // 这是"按键 → spec.InputTag → AbilityInputTagPressed 命中"这条链上原本缺失的一环。
            var inputTags = new List<GameplayTag>(abilitiesToGrant.Count);
            foreach (var abilityPath in abilitiesToGrant)
            {
                inputTags.Add(ResolveAbilityInputTag(abilityPath));
            }

            var handles = asc.GrantAbilities(abilitiesToGrant, this, 1f, inputTags);
            foreach (var handle in handles)
            {
                // 无效句柄也保留占位记录，保持与能力列表索引一致，便于排查配置缺项
                WeaponAbilityHandles.Add(handle.IsValid ? handle.HandleId.ToString() : "");
            }

            if (WeaponAbilityHandles.Count > 0)
            {
                NarrativeLog.Log($"[Weapon] '{DisplayName}' 授予 {WeaponAbilityHandles.Count} 个能力");
            }
        }

        /// <summary>
        /// 解析某条武器能力的输入标签：
        /// 先看 <see cref="WeaponAbilityInputTags"/> 的显式覆盖，再问能力数据定义，都没有返回 None。
        /// </summary>
        public virtual GameplayTag ResolveAbilityInputTag(string abilityPath)
        {
            if (string.IsNullOrEmpty(abilityPath)) return GameplayTag.None;

            if (WeaponAbilityInputTags != null &&
                WeaponAbilityInputTags.TryGetValue(abilityPath, out var overrideName) &&
                !string.IsNullOrEmpty(overrideName))
            {
                return new GameplayTag(overrideName);
            }

            return NarrativePro.GAS.NarrativeGASAssetRegistry.ResolveAbilityInputTag(abilityPath);
        }

        /// <summary>
        /// 移除武器授予的能力。对应 UE5 <c>UWeaponItem::RemoveWeaponAbilities</c>（:411-417）。
        /// </summary>
        public virtual void RemoveWeaponAbilities()
        {
            var character = GetOwningPawn();
            if (character == null)
            {
                WeaponAbilityHandles = new List<string>();
                return;
            }

            var asc = character.GetScript<NarrativePro.GAS.NarrativeAbilitySystemComponent>();
            if (asc == null)
            {
                WeaponAbilityHandles = new List<string>();
                return;
            }

            if (WeaponAbilityHandles != null && WeaponAbilityHandles.Count > 0)
            {
                var handles = new List<NarrativePro.GAS.GameplayAbilitySpecHandle>();
                foreach (var raw in WeaponAbilityHandles)
                {
                    if (string.IsNullOrEmpty(raw)) continue;
                    if (int.TryParse(raw, out int id)) handles.Add(new NarrativePro.GAS.GameplayAbilitySpecHandle(id));
                }
                asc.RemoveAbilities(handles);
            }
            else
            {
                // 句柄记录丢失（例如存档恢复后直接收起）时按授予来源兜底清理，避免能力残留
                asc.ClearAbilitiesFromSource(this);
            }

            WeaponAbilityHandles = new List<string>();
        }

        /// <summary>
        /// 持握到指定槽位。对应 UE5 EquipItem → WieldWeapon → HandleWield：
        /// HandleWield 内部已经做了能力授予，这里不再重复调用。
        /// </summary>
        public virtual void WieldInSlot(GameplayTag desiredSlot)
        {
            GameplayTag old = WieldedSlot;
            WieldedSlot = desiredSlot;
            if (desiredSlot.IsValid()) HandleWield();
            else HandleUnWield();
        }

        public override void HandleEquip()
        {
            base.HandleEquip();
            InitAmmoSource();

            // 装备即持握（对应 UE5 EquipItem → WieldWeapon → HandleWield）：
            // 背包点"装备"、拾取自动使用、快捷键换武都只调 EquipItem，
            // 武器必须在这里自己进入持握态，否则能力授予与武器视觉都不会发生。
            var slot = WieldedSlot.IsValid() ? WieldedSlot : ResolveWieldSlot();
            if (slot.IsValid() && !IsWielded())
            {
                GetEquipmentComponent()?.WieldWeapon(this, slot);
            }
        }

        public override void HandleUnequip(GameplayTag oldSlot)
        {
            // 卸下武器时同步退出持握态：否则武器留在 WieldedSlot 上、能力与视觉都不回收
            if (IsWielded())
            {
                var heldSlot = WieldedSlot;
                var equipment = GetEquipmentComponent();
                if (equipment != null)
                {
                    equipment.UnwieldWeapon(heldSlot);
                }
                else
                {
                    WieldedSlot = GameplayTag.None;
                    HandleUnWield();
                }
            }

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
