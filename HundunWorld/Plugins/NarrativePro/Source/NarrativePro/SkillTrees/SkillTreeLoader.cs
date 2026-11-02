using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using NarrativePro.Core;

namespace NarrativePro.SkillTrees
{
    /// <summary>
    /// 技能树 JSON 资产的加载入口——UE5 侧的“内容头”在这里换形。
    /// <para>
    /// UE5 把技能与 Perk 的数据放在两处反射资产里：<c>Content/Pro/Core/Data/SkillTrees/&lt;Tree&gt;/Skill_&lt;Tree&gt;.uasset</c>
    /// （<c>UTreeSkill</c> 子类，<c>EditInlineNew</c>，被 <c>USkillTreeComponent::SkillTreeSkills</c> 在
    /// 玩家状态蓝图里引用）与同目录 <c>Perks/Perk_*.uasset</c>（<c>UTreePerk</c> 子类 CDO）。
    /// Flax 没有 UClass 引用与 Instanced 内联对象，改为一份 JSON 描述一棵树：
    /// <c>{ "skills": [TreeSkill...], "perks": [TreePerk 字段...] }</c>。
    /// </para>
    /// <para>
    /// 解析产物是模板：<see cref="ApplyTo"/> 每次反序列化出<b>新实例</b>，因为 <c>SkillLevel</c>/
    /// <c>PerkLevel</c> 是按玩家递增的运行时状态，多个组件不能共享同一份对象。
    /// </para>
    /// </summary>
    public static class SkillTreeLoader
    {
        /// <summary>默认技能树内容目录（相对工程根）。</summary>
        public const string DefaultDirectory = "Content/NarrativePro/SkillTrees";

        private static readonly object _lock = new object();

        /// <summary>已解析的技能模板 JSON 原文（每个玩家实例化一份）。</summary>
        private static List<string> _skillTemplates;

        private static bool _loaded;

        private static readonly JsonSerializerOptions _options = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            // TreeSkill / FPerkConfig 的作者数据全部落在公共字段上；不开 IncludeFields 会静默解出空列表。
            IncludeFields = true
        };

        /// <summary>内容目录是否已加载过（供幂等判断与诊断）。</summary>
        public static bool IsLoaded
        {
            get { lock (_lock) return _loaded; }
        }

        /// <summary>已注册到工厂的 Perk 类路径数（诊断用）。</summary>
        public static int RegisteredPerkCount => TreePerkFactory.RegisteredPathCount;

        /// <summary>
        /// 解析单棵技能树 JSON 文件。返回其中的技能模板。
        /// <c>perks[]</c> 不直接返回，而是当场登记进 <see cref="TreePerkFactory"/>
        /// （对应 UE5 反射按 <c>TSubclassOf&lt;UTreePerk&gt;</c> 找类的职责）。
        /// </summary>
        public static List<TreeSkill> LoadFile(string filePath)
        {
            var skills = new List<TreeSkill>();
            if (string.IsNullOrEmpty(filePath) || !File.Exists(filePath))
            {
                NarrativeLog.LogWarning($"[SkillTree] 技能树 JSON 不存在: {filePath}");
                return skills;
            }

            string text = File.ReadAllText(filePath);
            using (JsonDocument doc = JsonDocument.Parse(text))
            {
                JsonElement root = doc.RootElement;

                RegisterPerkDefinitions(root, filePath);

                JsonElement skillsNode;
                if (root.TryGetProperty("skills", out skillsNode) && skillsNode.ValueKind == JsonValueKind.Array)
                {
                    foreach (JsonElement node in skillsNode.EnumerateArray())
                    {
                        try
                        {
                            TreeSkill skill = JsonSerializer.Deserialize<TreeSkill>(node.GetRawText(), _options);
                            if (skill != null) skills.Add(skill);
                        }
                        catch (Exception ex)
                        {
                            NarrativeLog.LogError($"[SkillTree] 技能节点解析失败 ({filePath}): {ex.Message}");
                        }
                    }
                }
            }

            ValidateTree(filePath, skills);
            return skills;
        }

        /// <summary>枚举目录下全部技能树 JSON（合并技能模板并按文件名字典序保证确定性）。</summary>
        public static List<TreeSkill> LoadAll(string directoryPath)
        {
            var all = new List<TreeSkill>();
            string dir = Path.IsPathRooted(directoryPath)
                ? directoryPath
                : Path.GetFullPath(Path.Combine(NarrativeDataDirectoryFallback(), directoryPath ?? ""));

            if (!Directory.Exists(dir))
            {
                NarrativeLog.LogWarning($"[SkillTree] 技能树目录不存在: {dir}");
                return all;
            }

            string[] files = Directory.GetFiles(dir, "*.json", SearchOption.AllDirectories);
            Array.Sort(files, StringComparer.Ordinal);

            foreach (string file in files)
            {
                try
                {
                    all.AddRange(LoadFile(file));
                }
                catch (Exception ex)
                {
                    NarrativeLog.LogError($"[SkillTree] 预热技能树失败 {file}: {ex.Message}");
                }
            }

            return all;
        }

        /// <summary>
        /// 幂等预热：把默认目录的技能模板缓存起来，供 <see cref="ApplyTo"/> 与 <see cref="ActiveSkillCount"/> 使用。
        /// </summary>
        public static bool EnsureActive(bool force = false, string directoryPath = null)
        {
            lock (_lock)
            {
                if (_loaded && !force) return _skillTemplates != null && _skillTemplates.Count > 0;
                if (force) _skillTemplates = null;
            }

            string dir = directoryPath ?? DefaultDirectory;
            List<TreeSkill> parsed = LoadAll(dir);

            lock (_lock)
            {
                _loaded = true;
                _skillTemplates = SerializeTemplates(parsed);
                _lastSkillCount = parsed.Count;
            }

            NarrativeLog.Log($"[SkillTree] 技能树预热完成：技能 {parsed.Count} 个、" +
                             $"Perk 定义 {TreePerkFactory.RegisteredPathCount} 条（目录 {dir}）。");
            return parsed.Count > 0;
        }

        private static List<string> SerializeTemplates(List<TreeSkill> skills)
        {
            var list = new List<string>();
            if (skills == null) return list;
            foreach (TreeSkill skill in skills)
            {
                if (skill == null) continue;
                try
                {
                    list.Add(JsonSerializer.Serialize(skill, _options));
                }
                catch (Exception ex)
                {
                    NarrativeLog.LogError($"[SkillTree] 技能模板缓存失败: {ex.Message}");
                }
            }
            return list;
        }

        private static int _lastSkillCount;

        /// <summary>预热到的技能个数（0 表示内容目录为空或未预热）。</summary>
        public static int ActiveSkillCount
        {
            get { lock (_lock) return _lastSkillCount; }
        }

        /// <summary>
        /// 把内容头的技能模板灌进组件——对应 UE5 在玩家状态蓝图里给
        /// <c>USkillTreeComponent::SkillTreeSkills</c> 赋值的那一步。
        /// 组件已在场景里作者化过技能时不覆盖（作者优先，与其余 JSON 通路一致）。
        /// </summary>
        public static int ApplyTo(SkillTreeComponent component)
        {
            if (component == null) return 0;
            if (component.SkillTreeSkills == null) component.SkillTreeSkills = new List<TreeSkill>();
            if (component.SkillTreeSkills.Count > 0) return 0;

            EnsureActive();

            int applied = 0;
            List<string> templates;
            lock (_lock) templates = _skillTemplates;
            if (templates == null) return 0;

            foreach (string raw in templates)
            {
                try
                {
                    TreeSkill skill = JsonSerializer.Deserialize<TreeSkill>(raw, _options);
                    if (skill != null)
                    {
                        component.SkillTreeSkills.Add(skill);
                        applied++;
                    }
                }
                catch (Exception ex)
                {
                    NarrativeLog.LogError($"[SkillTree] 技能模板实例化失败: {ex.Message}");
                }
            }

            return applied;
        }

        // ===================================================================
        // 内部：Perk 定义登记与资产自检
        // ===================================================================

        private static void RegisterPerkDefinitions(JsonElement root, string sourcePath)
        {
            JsonElement perksNode;
            if (!root.TryGetProperty("perks", out perksNode) || perksNode.ValueKind != JsonValueKind.Array)
            {
                return;
            }

            foreach (JsonElement node in perksNode.EnumerateArray())
            {
                string path = GetString(node, "perkClassPath");
                if (string.IsNullOrEmpty(path)) path = GetString(node, "perk");
                if (string.IsNullOrEmpty(path))
                {
                    NarrativeLog.LogError($"[SkillTree] Perk 定义缺少 perkClassPath（{sourcePath}），该条被跳过");
                    continue;
                }

                string typeName = GetString(node, "perkType");
                Type perkType = TreePerkFactory.ResolvePerkType(typeName);
                if (perkType == null)
                {
                    NarrativeLog.LogError(
                        $"[SkillTree] Perk '{path}' 的类型 '{typeName}' 无法解析（{sourcePath}），按 TreePerk 基类登记");
                    perkType = typeof(TreePerk);
                }

                TreePerkFactory.RegisterPerkType(path, perkType);
                TreePerkFactory.RegisterPerkDefinition(path, node);
            }
        }

        private static void ValidateTree(string sourcePath, List<TreeSkill> skills)
        {
            foreach (TreeSkill skill in skills)
            {
                if (skill?.Perks == null) continue;
                foreach (FPerkConfig config in skill.Perks)
                {
                    if (config == null || string.IsNullOrEmpty(config.Perk)) continue;
                    if (!TreePerkFactory.IsRegistered(config.Perk))
                    {
                        NarrativeLog.LogWarning(
                            $"[SkillTree] 技能 '{skill.SkillClassPath}' 引用了未定义的 Perk '{config.Perk}'（{sourcePath}）");
                    }

                    if (config.LinkedTo == null) continue;
                    foreach (string linkedTo in config.LinkedTo)
                    {
                        if (!string.IsNullOrEmpty(linkedTo) && !TreePerkFactory.IsRegistered(linkedTo))
                        {
                            NarrativeLog.LogWarning(
                                $"[SkillTree] 技能 '{skill.SkillClassPath}' 链接到未定义的 Perk '{linkedTo}'（{sourcePath}）");
                        }
                    }
                }
            }
        }

        private static string GetString(JsonElement node, string property)
        {
            JsonElement value;
            if (!node.TryGetProperty(property, out value)) return "";
            return value.ValueKind == JsonValueKind.String ? value.GetString() : "";
        }

        /// <summary>相对路径的兜底基准：harness 与编辑器都从工程根起算。</summary>
        private static string NarrativeDataDirectoryFallback()
        {
            try
            {
                return NarrativePro.Data.NarrativeDataPaths.ProjectDirectory;
            }
            catch
            {
                return Directory.GetCurrentDirectory();
            }
        }
    }
}
