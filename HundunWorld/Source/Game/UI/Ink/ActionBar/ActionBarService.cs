using System;
using System.Collections.Generic;
using FlaxEngine;
using Game.Character.Attributes;
using Game.Combat.Skills;
using HundunWorld.Game.Character;
using HundunWorld.Game.Combat;
using HundunWorld.Game.Combat.Skills;
using HundunWorld.Game.Inventory;
using HundunWorld.Game.Selection;
using HundunWorld.Game.Services;
using HundunWorld.Game.UI.StyleSystem;
using Horizon.Game.Message.Network;

namespace HundunWorld.Game.UI.Ink.ActionBar
{
    /// <summary>槽位解析出的可渲染信息（名称/字形/配色/图标/角标）。</summary>
    public sealed class ActionBarDisplay
    {
        public string Name = "";
        public string Glyph = "";
        public Color Tint = InkWashTheme.GoldPrimary;
        public Texture Icon;
        /// <summary>右下角角标：技能显示等级、物品显示持有数量。</summary>
        public string Corner = "";
        /// <summary>悬浮说明行。</summary>
        public readonly List<string> DetailLines = new();
    }

    /// <summary>
    /// 技能栏的取数与执行层：把槽位内容解析成可渲染信息，并把数字键/点击落到既有的
    /// 施放与使用链路上（<see cref="CombatReporter.ReportSkillCastById"/> 与
    /// <see cref="PlayerInventoryService.RequestUseItemAsync"/>），不自己另开一条协议。
    /// </summary>
    public sealed class ActionBarService
    {
        private static ActionBarService _instance;
        public static ActionBarService Instance => _instance ??= new ActionBarService();

        /// <summary>物品没有服务端冷却，但同一瓶连点会刷屏请求，本地给一个极短的发射手柄期。</summary>
        private const float ItemFireInterval = 0.4f;

        private readonly Dictionary<string, ActionBarDisplay> _displayCache = new();
        private readonly Dictionary<string, Texture> _iconCache = new();

        /// <summary>技能本地冷却：skillId → (开始时刻, 时长)。服务端才是权威判定，这里只驱动扇形遮罩。</summary>
        private readonly Dictionary<int, (float Start, float Duration)> _cooldowns = new();

        private readonly Dictionary<int, float> _lastItemFireAt = new();

        /// <summary>
        /// 已被相生演化封存掉的源技集合（懒算，目录变化时失效）。
        /// 栏上留着它们没意义：服务端不会认，玩家点了只会被冷却/目标之类的理由误导，
        /// 所以选择器不列、按下去直接说"已融于演化式"。
        /// </summary>
        private HashSet<int> _retired;

        private ActionBarService() { }

        public bool IsRetired(int skillId)
        {
            var retired = _retired ??= HundunWorld.Game.Combat.Skills.WuxingSkillTree.ComputeRetired(
                HundunWorld.Game.Combat.Skills.WuxingSkillTree.LearnedIds());
            return retired.Contains(skillId);
        }

        // ===================================================================
        // 显示解析
        // ===================================================================

        public ActionBarDisplay Resolve(ActionBarSlotContent content)
        {
            if (content.IsEmpty) return null;
            string key = (int)content.Kind + ":" + content.RefId;
            if (_displayCache.TryGetValue(key, out var cached))
                return RefreshDynamic(cached, content);

            var display = content.Kind == ActionBarSlotKind.Skill
                ? BuildSkillDisplay(content.RefId)
                : BuildItemDisplay(content.RefId);
            _displayCache[key] = display;
            return RefreshDynamic(display, content);
        }

        /// <summary>绑定关系变了（学了新技能/吃药吃完/演化封存）时丢弃缓存，下一次 Resolve 重建。</summary>
        public void InvalidateDisplays()
        {
            _displayCache.Clear();
            _retired = null;
        }

        private static ActionBarDisplay RefreshDynamic(ActionBarDisplay display, ActionBarSlotContent content)
        {
            if (content.Kind == ActionBarSlotKind.Item)
            {
                int count = Instance.GetItemCount(content.RefId);
                display.Corner = count > 1 ? count.ToString() : string.Empty;
            }
            return display;
        }

        private ActionBarDisplay BuildSkillDisplay(int skillId)
        {
            var cfg = SkillDatabase.GetSkill(skillId);
            var learned = SkillCatalogService.Instance.Learned.TryGetValue(skillId, out var info) ? info : null;

            string name = !string.IsNullOrEmpty(learned?.SkillName) ? learned.SkillName
                : !string.IsNullOrEmpty(cfg?.SkillName) ? cfg.SkillName
                : $"技能 {skillId}";

            var display = new ActionBarDisplay
            {
                Name = name,
                Glyph = FirstChar(name, "技"),
                Tint = ElementColor(cfg?.Element),
            };

            if (cfg != null)
            {
                display.Icon = LoadIcon(cfg.IconPath);
                if (cfg.Grade >= 1)
                    display.DetailLines.Add($"五行阶梯 {WuxingSkillTree.PositionLabel(cfg)}");
                if (cfg.EnergyCost > 0f)
                    display.DetailLines.Add($"耗气 {cfg.EnergyCost:F0}");
                if (cfg.Cooldown > 0f)
                    display.DetailLines.Add("冷却 " + FormatSeconds(cfg.Cooldown));
            }
            if (IsRetired(skillId))
                display.DetailLines.Add("已融于演化式（散功可解封）");
            if (learned != null)
                display.Corner = learned.Level > 0 ? learned.Level.ToString() : string.Empty;

            return display;
        }

        private ActionBarDisplay BuildItemDisplay(int templateId)
        {
            var def = ItemCatalog.Resolve(templateId);
            var display = new ActionBarDisplay
            {
                Name = def.Name,
                Glyph = string.IsNullOrEmpty(def.Glyph) ? FirstChar(def.Name, "药") : def.Glyph,
                Tint = InkWashTheme.QualityColor((InkWashTheme.InkQuality)Mathf.Clamp(def.Quality, 0, 6)),
            };
            if (def.HpRestore > 0f) display.DetailLines.Add($"回复气血 {def.HpRestore:F0}");
            if (def.EnergyRestore > 0f) display.DetailLines.Add($"回复内力 {def.EnergyRestore:F0}");
            display.DetailLines.Add(def.IsUsable ? "可直接消耗" : "非消耗品");
            return display;
        }

        private Texture LoadIcon(string path)
        {
            if (string.IsNullOrEmpty(path)) return null;
            if (_iconCache.TryGetValue(path, out var cached)) return cached;

            Texture texture = null;
            try
            {
                texture = Content.LoadAsync<Texture>(path);
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[ActionBarService] 图标加载失败 {path}: {ex.Message}");
            }
            _iconCache[path] = texture;
            return texture;
        }

        private static string FirstChar(string text, string fallback) =>
            string.IsNullOrEmpty(text) ? fallback : text.Substring(0, 1);

        private static Color ElementColor(string element) => element switch
        {
            "Metal" => InkWashTheme.ElementMetal,
            "Wood" => InkWashTheme.ElementWood,
            "Water" => InkWashTheme.ElementWater,
            "Fire" => InkWashTheme.ElementFire,
            "Earth" => InkWashTheme.ElementEarth,
            _ => InkWashTheme.GoldPrimary,
        };

        private static string FormatSeconds(float seconds) =>
            seconds >= 1f ? $"{seconds:F0} 秒" : $"{seconds * 1000f:F0} 毫秒";

        // ===================================================================
        // 冷却
        // ===================================================================

        public bool IsOnCooldown(ActionBarSlotContent content) => GetCooldownProgress(content) > 0f;

        /// <summary>剩余冷却比例 0~1（1 = 刚按下、整圈皆冷却）。</summary>
        public float GetCooldownProgress(ActionBarSlotContent content)
        {
            if (content.IsEmpty) return 0f;
            if (content.Kind == ActionBarSlotKind.Item) return 0f;

            if (!_cooldowns.TryGetValue(content.RefId, out var entry)) return 0f;

            float elapsed = Time.GameTime - entry.Start;
            if (elapsed < 0f) elapsed = 0f;
            if (elapsed >= entry.Duration)
            {
                _cooldowns.Remove(content.RefId);
                return 0f;
            }
            return 1f - elapsed / entry.Duration;
        }

        public string GetCooldownText(ActionBarSlotContent content)
        {
            if (content.Kind != ActionBarSlotKind.Skill) return string.Empty;
            if (!_cooldowns.TryGetValue(content.RefId, out var entry)) return string.Empty;

            float remain = entry.Duration - (Time.GameTime - entry.Start);
            if (remain <= 0f) return string.Empty;
            return remain >= 10f ? remain.ToString("F0") : remain.ToString("F1");
        }

        private void StartCooldown(int skillId)
        {
            float duration = SkillDatabase.GetSkill(skillId)?.Cooldown ?? 1f;
            if (duration <= 0f) duration = 0.2f;
            _cooldowns[skillId] = (Time.GameTime, duration);
        }

        // ===================================================================
        // 触发
        // ===================================================================

        /// <summary>
        /// 按下槽位：技能走既有施放上报链路，物品走背包使用链路。
        /// 返回 false 时 <paramref name="reason"/> 给出可直接展示给玩家的拒绝原因。
        /// </summary>
        public bool TryActivate(ActionBarSlotContent content, out string reason)
        {
            reason = null;
            if (content.IsEmpty) return false;

            return content.Kind == ActionBarSlotKind.Skill
                ? TryCastSkill(content.RefId, out reason)
                : TryUseItem(content.RefId, out reason);
        }

        private bool TryCastSkill(int skillId, out string reason)
        {
            reason = null;
            if (IsRetired(skillId))
            {
                reason = "该式已融于演化式，需先散功";
                return false;
            }
            if (IsOnCooldown(ActionBarSlotContent.OfSkill(skillId)))
            {
                reason = "冷却中";
                return false;
            }

            var selected = WorldSelectionService.Instance?.Selected;
            var targetActor = selected?.Actor;
            ulong targetId = CombatReporter.ResolveNetworkTarget(targetActor);
            if (targetId == 0UL)
            {
                reason = "请先选择目标";
                return false;
            }

            // 打不到就别发这一手：目标正在回位时服务端会整手拒掉（AC 的 IsEvadingAttacks 挂在受击方那侧）。
            // 挡在 StartCooldown **之前**——白吃一次冷却比白挨一次拒绝更难归因（玩家只会觉得技能坏了）。
            // 权威仍然在服务端：这里只是把"按下去什么都没发生"换成人话。
            if (!MonsterCombatActionRules.CanDamage(true, selected?.Selectable ?? false,
                                                   selected?.Unattackable ?? false))
            {
                reason = MonsterCombatActionRules.DamageHint(true, selected?.Selectable ?? false,
                                                             selected?.Unattackable ?? false);
                return false;
            }

            // 沉默/眩晕：**这一手根本发不出去**（AC 的 CheckCasterAuras 见 UNIT_FLAG_SILENCED/STUNNED
            // 直接回 SPELL_FAILED_SILENCED/STUNNED）。挡在 StartCooldown **之前**——
            // 白吃一次冷却比一次明确的拒绝更难归因，这与上面那条"打不到就别发"同一个理由。
            // 权威位来自快照（LocalPlayerActorSyncSystem 镜像进 ControlStateManager），服务端结算侧仍是最终裁决。
            var localId = HundunWorldGame.Instance?.PlayerId ?? 0UL;
            if (localId != 0UL)
            {
                var control = ControlStateManager.Instance;
                if (control.HasControlState(localId, ControlState.Stunned)
                    || control.HasControlState(localId, ControlState.Silenced))
                {
                    reason = control.HasControlState(localId, ControlState.Stunned) ? "被眩晕，无法施法" : "被沉默，无法施法";
                    return false;
                }
            }

            var cfg = SkillDatabase.GetSkill(skillId);
            StartCooldown(skillId);
            var element = cfg != null ? cfg.GetWuxingElement() : WuxingElement.None;

            // 本地玩家身上挂着 SkillBase 实例时优先走它：会带动画与特效，并在施放完成时自行上报。
            var local = HundunWorldGame.Instance?.LocalPlayerActor;
            if (local != null)
            {
                var scripts = local.GetScripts<SkillBase>();
                for (int i = 0; i < scripts.Length; i++)
                {
                    var skill = scripts[i];
                    if (skill == null || skill.Data == null || skill.Data.SkillId != skillId) continue;
                    if (skill.TryCast(targetActor))
                    {
                        WuxingCycleTracker.Instance.NoteCast(element);
                        return true;
                    }
                    reason = "施放被打断";
                    return false;
                }
            }

            CombatReporter.ReportSkillCastById(targetId, skillId, cfg?.EnergyCost ?? 0f);
            WuxingCycleTracker.Instance.NoteCast(element);
            return true;
        }

        private bool TryUseItem(int templateId, out string reason)
        {
            reason = null;
            float sinceLast = Time.GameTime - (_lastItemFireAt.TryGetValue(templateId, out var at) ? at : -999f);
            if (sinceLast < ItemFireInterval)
            {
                reason = null;
                return false;
            }

            var item = FindItem(templateId);
            if (item == null)
            {
                reason = "背包中已无该物品";
                return false;
            }

            _lastItemFireAt[templateId] = Time.GameTime;
            var service = PlayerInventoryService.Instance;
            if (service == null)
            {
                reason = "背包服务未就绪";
                return false;
            }

            _ = service.RequestUseItemAsync(item, 1);
            return true;
        }

        // ===================================================================
        // 选择器数据源
        // ===================================================================

        /// <summary>
        /// 可绑定的技能列表。以服务端已学目录为准；未同步到时退回本地模板表，
        /// 否则开发期技能栏会是空的、无法验证交互。
        /// </summary>
        public List<ActionBarSlotContent> BuildSkillOptions()
        {
            var options = new List<ActionBarSlotContent>();
            var learned = SkillCatalogService.Instance.Learned;
            if (learned.Count > 0)
            {
                foreach (var pair in learned)
                {
                    if (IsRetired(pair.Key)) continue;          // 演化换代后源技不再可选
                    options.Add(ActionBarSlotContent.OfSkill(pair.Key));
                }
            }
            else
            {
                var templates = SkillDatabase.GetAllSkills();
                if (templates != null)
                {
                    foreach (var cfg in templates)
                        if (cfg != null) options.Add(ActionBarSlotContent.OfSkill(cfg.SkillId));
                }
            }

            options.Sort((a, b) => string.Compare(
                Resolve(a).Name, Resolve(b).Name, StringComparison.Ordinal));
            return options;
        }

        /// <summary>可绑定的消耗品列表（按模板去重，装备/材料不可上栏）。</summary>
        public List<ActionBarSlotContent> BuildItemOptions()
        {
            var options = new List<ActionBarSlotContent>();
            var seen = new HashSet<int>();
            var items = PlayerInventoryService.Instance?.Items;
            if (items == null) return options;

            for (int i = 0; i < items.Count; i++)
            {
                var item = items[i];
                if (item == null || item.Count <= 0) continue;
                if (!ItemCatalog.Resolve(item.TemplateId).IsUsable) continue;
                if (!seen.Add(item.TemplateId)) continue;
                options.Add(ActionBarSlotContent.OfItem(item.TemplateId));
            }
            return options;
        }

        private static ItemInfo FindItem(int templateId)
        {
            var items = PlayerInventoryService.Instance?.Items;
            if (items == null) return null;

            for (int i = 0; i < items.Count; i++)
            {
                var item = items[i];
                if (item != null && item.TemplateId == templateId && item.Count > 0)
                    return item;
            }
            return null;
        }

        public int GetItemCount(int templateId)
        {
            var item = FindItem(templateId);
            return item?.Count ?? 0;
        }
    }
}
