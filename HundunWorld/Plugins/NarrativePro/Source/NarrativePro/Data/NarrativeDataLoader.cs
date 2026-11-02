using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using NarrativePro.Audio;
using NarrativePro.CharacterCreator;
using NarrativePro.Core;
using NarrativePro.Items;
using NarrativePro.Music;
using NarrativePro.Settings;
using NarrativePro.Tales;
using NarrativePro.Tales.Data;
using NarrativePro.Tales.Dialogue;
using NarrativePro.Tales.Quest;

namespace NarrativePro.Data
{
    /// <summary>
    /// NarrativePro JSON 数据的统一加载入口。
    /// <para>
    /// 所有文件路径参数都会先经 <see cref="NarrativeDataPaths.Resolve"/> 归一为绝对路径
    /// （编辑器 / PIE / 打包构建工作目录不一致的修复点）；文件缺失时按 UE5 资产缺失口径
    /// 写 "Has it been renamed?" 诊断。
    /// </para>
    /// <para>
    /// <see cref="InitializeAll"/> 是插件级幂等预热入口，设计为在
    /// <c>NarrativeProPlugin.Initialize()</c> 里调用一行即可接通全部数据通路。
    /// </para>
    /// </summary>
    public static class NarrativeDataLoader
    {
        // ===================================================================
        // 单文件加载（路径一律先 Resolve）
        // ===================================================================

        public static Quest LoadQuest(string questFilePath)
        {
            string resolved = NarrativeDataPaths.ResolveForRead(questFilePath, "quest");
            if (!File.Exists(resolved))
            {
                // 给出比 File.ReadAllText 的 FileNotFoundException 更明确的定位信息后照常抛异常，
                // 保持既有调用方（TalesComponent.MakeQuestInstance 的 try/catch）语义不变。
                throw new FileNotFoundException(
                    $"Quest JSON not found. Resolved: '{resolved}' (original path: '{questFilePath}')", resolved);
            }

            var quest = QuestFactory.LoadQuest(resolved);

            // 预热成功后顺带进缓存，供 InitializeAll / 后续实例化复用。
            if (quest != null)
            {
                lock (_cacheLock)
                {
                    _quests[Path.GetFileNameWithoutExtension(resolved)] = quest;
                }
            }
            return quest;
        }

        public static Dialogue LoadDialogue(string dialogueFilePath)
        {
            string resolved = NarrativeDataPaths.ResolveForRead(dialogueFilePath, "dialogue");
            if (!File.Exists(resolved))
            {
                throw new FileNotFoundException(
                    $"Dialogue JSON not found. Resolved: '{resolved}' (original path: '{dialogueFilePath}')", resolved);
            }

            var dialogue = DialogueFactory.LoadDialogue(resolved);
            if (dialogue != null)
            {
                lock (_cacheLock)
                {
                    _dialogues[Path.GetFileNameWithoutExtension(resolved)] = dialogue;
                }
            }
            return dialogue;
        }

        // ===== 角色创建器（CharacterCreator）配置加载与创建路由 =====

        /// <summary>加载角色创建器配置 JSON。对应 UE5 UCharacterCreatorConfiguration DataAsset 引用。</summary>
        public static CharacterCreatorConfiguration LoadCharacterCreatorConfiguration(string configFilePath)
        {
            return CharacterCreatorFactory.LoadConfiguration(NarrativeDataPaths.Resolve(configFilePath));
        }

        /// <summary>加载目录下所有角色创建器配置（文件名 → 配置）。</summary>
        public static Dictionary<string, CharacterCreatorConfiguration> LoadAllCharacterCreatorConfigurations(string directoryPath)
        {
            return CharacterCreatorFactory.LoadAllConfigurations(NarrativeDataPaths.ResolveDirectory(directoryPath));
        }

        /// <summary>加载角色创建器色板 JSON。</summary>
        public static CreatorColorSwatch LoadCreatorColorSwatch(string swatchPath)
        {
            return CharacterCreatorFactory.LoadColorSwatch(NarrativeDataPaths.Resolve(swatchPath));
        }

        /// <summary>
        /// 创建路由：按配置 + 表单标签生成默认角色属性集，
        /// 供 NarrativeCharacterVisual.InitializeFromCharacterAndAttributes 应用到角色。
        /// </summary>
        public static CharacterCreatorAttributeSet CreateCharacterAttributes(string configFilePath, GameplayTag formTag)
        {
            return CharacterCreatorFactory.CreateDefaultAttributeSet(NarrativeDataPaths.Resolve(configFilePath), formTag);
        }

        /// <summary>创建路由：直接由已加载的表单生成默认角色属性集。</summary>
        public static CharacterCreatorAttributeSet CreateCharacterAttributes(CharacterCreatorForm form)
        {
            return CharacterCreatorFactory.CreateDefaultAttributeSet(form);
        }

        public static NarrativeDataTask LoadDataTask(string taskFilePath)
        {
            try
            {
                string resolved = NarrativeDataPaths.Resolve(taskFilePath);
                if (!File.Exists(resolved))
                {
                    NarrativeLog.LogError($"Data task file is missing: '{resolved}'. Has it been renamed? (original: {taskFilePath})");
                    return null;
                }
                string json = File.ReadAllText(resolved);
                return JsonSerializer.Deserialize<NarrativeDataTask>(json);
            }
            catch (Exception ex)
            {
                NarrativeLog.LogError($"Failed to load data task from {taskFilePath}: {ex.Message}");
                return null;
            }
        }

        public static Dictionary<string, Quest> LoadAllQuests(string directoryPath)
        {
            string resolved = ReportIfDirectoryMissing(directoryPath, "quest");
            if (resolved == null) return new Dictionary<string, Quest>();

            var quests = QuestFactory.LoadAllQuests(resolved);
            if (quests.Count == 0)
            {
                NarrativeLog.LogWarning($"[Data] 任务目录 '{resolved}' 下没有加载成功任何 quest JSON。");
            }
            return quests;
        }

        public static Dictionary<string, Dialogue> LoadAllDialogues(string directoryPath)
        {
            string resolved = ReportIfDirectoryMissing(directoryPath, "dialogue");
            if (resolved == null) return new Dictionary<string, Dialogue>();

            var dialogues = DialogueFactory.LoadAllDialogues(resolved);
            if (dialogues.Count == 0)
            {
                NarrativeLog.LogWarning($"[Data] 对话目录 '{resolved}' 下没有加载成功任何 dialogue JSON。");
            }
            return dialogues;
        }

        public static Dictionary<string, NarrativeDataTask> LoadAllDataTasks(string directoryPath)
        {
            var tasks = new Dictionary<string, NarrativeDataTask>();
            string resolved = NarrativeDataPaths.ResolveDirectory(directoryPath);
            if (!Directory.Exists(resolved))
            {
                NarrativeLog.LogWarning($"[Data] 数据任务目录不存在: '{resolved}' (配置值: {directoryPath})。Has it been renamed or moved?");
                return tasks;
            }

            foreach (var file in Directory.GetFiles(resolved, "*.json"))
            {
                try
                {
                    var task = LoadDataTask(file);
                    if (task != null)
                    {
                        string key = Path.GetFileNameWithoutExtension(file);
                        tasks[key] = task;
                    }
                }
                catch (Exception ex)
                {
                    NarrativeLog.LogError($"Failed to load data task from {file}: {ex.Message}");
                }
            }
            return tasks;
        }

        public static NarrativeSettings LoadSettings(string settingsFilePath)
        {
            try
            {
                string resolved = NarrativeDataPaths.Resolve(settingsFilePath);
                if (!File.Exists(resolved)) return new NarrativeSettings();
                string json = File.ReadAllText(resolved);
                return JsonSerializer.Deserialize<NarrativeSettings>(json) ?? new NarrativeSettings();
            }
            catch (Exception ex)
            {
                NarrativeLog.LogError($"Failed to load settings from {settingsFilePath}: {ex.Message}");
                return new NarrativeSettings();
            }
        }

        public static void SaveSettings(NarrativeSettings settings, string settingsFilePath)
        {
            try
            {
                var options = new JsonSerializerOptions { WriteIndented = true };
                string json = JsonSerializer.Serialize(settings, options);
                string directory = Path.GetDirectoryName(settingsFilePath);
                if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
                {
                    Directory.CreateDirectory(directory);
                }
                File.WriteAllText(settingsFilePath, json);
            }
            catch (Exception ex)
            {
                NarrativeLog.LogError($"Failed to save settings to {settingsFilePath}: {ex.Message}");
            }
        }

        // ===================================================================
        // 缓存快照
        // ===================================================================

        private static readonly object _cacheLock = new object();
        private static readonly Dictionary<string, Quest> _quests = new Dictionary<string, Quest>(StringComparer.Ordinal);
        private static readonly Dictionary<string, Dialogue> _dialogues = new Dictionary<string, Dialogue>(StringComparer.Ordinal);

        /// <summary>已加载任务快照（文件名 → Quest 原型，实例化仍由 TalesComponent.MakeQuestInstance 负责）。</summary>
        public static IReadOnlyDictionary<string, Quest> LoadedQuests
        {
            get { lock (_cacheLock) return new Dictionary<string, Quest>(_quests); }
        }

        /// <summary>已加载对话快照。</summary>
        public static IReadOnlyDictionary<string, Dialogue> LoadedDialogues
        {
            get { lock (_cacheLock) return new Dictionary<string, Dialogue>(_dialogues); }
        }

        /// <summary>按 ID（文件名）取缓存任务；未命中返回 null。</summary>
        public static Quest FindCachedQuest(string questId)
        {
            if (string.IsNullOrEmpty(questId)) return null;
            lock (_cacheLock)
            {
                return _quests.TryGetValue(questId, out var quest) ? quest : null;
            }
        }

        /// <summary>按 ID（文件名）取缓存对话；未命中返回 null。</summary>
        public static Dialogue FindCachedDialogue(string dialogueId)
        {
            if (string.IsNullOrEmpty(dialogueId)) return null;
            lock (_cacheLock)
            {
                return _dialogues.TryGetValue(dialogueId, out var dialogue) ? dialogue : null;
            }
        }

        // ===================================================================
        // 触发器集 / 标签对话集 / 触发器调度（Content/NarrativePro/Triggers）
        // ===================================================================

        private static readonly Dictionary<string, TriggerSet> _triggerSets =
            new Dictionary<string, TriggerSet>(StringComparer.OrdinalIgnoreCase);
        private static readonly Dictionary<string, TaggedDialogueSet> _taggedDialogueSets =
            new Dictionary<string, TaggedDialogueSet>(StringComparer.OrdinalIgnoreCase);
        private static readonly Dictionary<string, List<TriggerSet>> _triggerSetsByNpc =
            new Dictionary<string, List<TriggerSet>>(StringComparer.OrdinalIgnoreCase);
        private static readonly Dictionary<string, TaggedDialogueSet> _taggedDialogueSetsByNpc =
            new Dictionary<string, TaggedDialogueSet>(StringComparer.OrdinalIgnoreCase);

        /// <summary>默认触发器数据目录（TriggerSet / TaggedDialogueSet / TriggerSchedule JSON 混放，按根字段形态判别）。</summary>
        public static string DefaultTriggerDirectory = "Content/NarrativePro/Triggers";

        /// <summary>按路径加载 TriggerSet（绝对/工程相对皆可，带缓存与 NPC 绑定登记）。</summary>
        public static TriggerSet LoadTriggerSet(string triggerSetPath)
        {
            string resolved = NarrativeDataPaths.ResolveForRead(triggerSetPath, "trigger set");
            if (string.IsNullOrEmpty(resolved) || !File.Exists(resolved)) return null;

            lock (_cacheLock)
            {
                TriggerSet cached;
                if (_triggerSets.TryGetValue(resolved, out cached)) return cached;
            }

            TriggerSet set = null;
            try
            {
                set = TriggerSetFactory.LoadTriggerSetFile(resolved);
            }
            catch (Exception ex)
            {
                NarrativeLog.LogError($"[Data] 加载 TriggerSet 失败 {resolved}: {ex.Message}");
            }

            if (set != null)
            {
                lock (_cacheLock)
                {
                    _triggerSets[resolved] = set;
                    BindTriggerSetLocked(set);
                }
            }
            return set;
        }

        /// <summary>按路径加载 TaggedDialogueSet（对应 UE5 UTaggedDialogueSet 资产）。</summary>
        public static TaggedDialogueSet LoadTaggedDialogueSet(string taggedDialogueSetPath)
        {
            string resolved = NarrativeDataPaths.ResolveForRead(taggedDialogueSetPath, "tagged dialogue set");
            if (string.IsNullOrEmpty(resolved) || !File.Exists(resolved)) return null;

            lock (_cacheLock)
            {
                TaggedDialogueSet cached;
                if (_taggedDialogueSets.TryGetValue(resolved, out cached)) return cached;
            }

            TaggedDialogueSet set = null;
            try
            {
                set = TriggerSetFactory.LoadTaggedDialogueSetFile(resolved);
            }
            catch (Exception ex)
            {
                NarrativeLog.LogError($"[Data] 加载 TaggedDialogueSet 失败 {resolved}: {ex.Message}");
            }

            if (set != null)
            {
                lock (_cacheLock)
                {
                    _taggedDialogueSets[resolved] = set;
                    BindTaggedDialogueSetLocked(set);
                }
            }
            return set;
        }

        /// <summary>取按数据绑定（appliesToNpcIds）命中指定 NPC 的触发器集；无绑定返回 null。</summary>
        public static List<TriggerSet> GetTriggerSetsForNpc(string npcId)
        {
            if (string.IsNullOrEmpty(npcId)) return null;
            lock (_cacheLock)
            {
                List<TriggerSet> sets;
                return _triggerSetsByNpc.TryGetValue(npcId, out sets) && sets.Count > 0 ? sets : null;
            }
        }

        /// <summary>取按数据绑定（appliesToNpcIds）命中指定 NPC 的标签对话集；无绑定返回 null。</summary>
        public static TaggedDialogueSet GetTaggedDialogueSetForNpc(string npcId)
        {
            if (string.IsNullOrEmpty(npcId)) return null;
            lock (_cacheLock)
            {
                TaggedDialogueSet set;
                return _taggedDialogueSetsByNpc.TryGetValue(npcId, out set) ? set : null;
            }
        }

        /// <summary>
        /// 预热触发器目录：根含 "triggers" 按 TriggerSet 解析、含 "taggedDialogues" 按 TaggedDialogueSet 解析、
        /// 含 "triggerSetPaths" 按 TriggerSchedule 展开其成员集（TriggerSchedule 自此有真实消费者）。
        /// 目录不存在只记 Info，不报错、不阻断启动。
        /// </summary>
        public static void WarmTriggers()
        {
            string dir = NarrativeDataPaths.ResolveDirectory(DefaultTriggerDirectory);
            if (!Directory.Exists(dir))
            {
                NarrativeLog.Log($"[Data] 触发器目录不存在，跳过预热: {dir}");
                return;
            }

            int sets = 0, tagSets = 0, schedules = 0;
            try
            {
                foreach (var file in Directory.GetFiles(dir, "*.json", SearchOption.AllDirectories))
                {
                    string full = Path.GetFullPath(file);
                    try
                    {
                        using (var doc = System.Text.Json.JsonDocument.Parse(
                                   File.ReadAllText(full),
                                   new System.Text.Json.JsonDocumentOptions
                                   {
                                       CommentHandling = System.Text.Json.JsonCommentHandling.Skip,
                                       AllowTrailingCommas = true,
                                   }))
                        {
                            var root = doc.RootElement;
                            if (TriggerSetFactory.IsTriggerSetJson(root))
                            {
                                var set = TriggerSetFactory.CreateTriggerSet(root, full);
                                if (set != null)
                                {
                                    lock (_cacheLock)
                                    {
                                        _triggerSets[full] = set;
                                        BindTriggerSetLocked(set);
                                    }
                                    sets++;
                                }
                            }
                            else if (TriggerSetFactory.IsTaggedDialogueSetJson(root))
                            {
                                var tset = TriggerSetFactory.CreateTaggedDialogueSet(root, full);
                                if (tset != null)
                                {
                                    lock (_cacheLock)
                                    {
                                        _taggedDialogueSets[full] = tset;
                                        BindTaggedDialogueSetLocked(tset);
                                    }
                                    tagSets++;
                                }
                            }
                            else if (TriggerSetFactory.IsTriggerScheduleJson(root))
                            {
                                var schedule = TriggerSetFactory.CreateTriggerSchedule(root, full);
                                if (schedule != null && schedule.LoadedTriggerSets != null)
                                {
                                    foreach (var mset in schedule.LoadedTriggerSets)
                                    {
                                        if (mset == null) continue;
                                        lock (_cacheLock) BindTriggerSetLocked(mset);
                                        sets++;
                                    }
                                    schedules++;
                                }
                            }
                            else
                            {
                                NarrativeLog.LogWarning(
                                    $"[Data] 无法识别的触发器 JSON（需含 triggers / taggedDialogues / triggerSetPaths 之一）: {full}");
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        NarrativeLog.LogError($"[Data] 预热触发器文件失败 {full}: {ex.Message}");
                    }
                }
            }
            catch (Exception ex)
            {
                NarrativeLog.LogError($"[Data] 触发器目录枚举失败 {dir}: {ex.Message}");
            }

            NarrativeLog.Log($"[Data] 触发器预热完成：TriggerSet {sets} 个、TaggedDialogueSet {tagSets} 个、TriggerSchedule {schedules} 个（目录 {dir}）。");
        }

        /// <summary>登记触发器集的 NPC 绑定（调用方持锁）。</summary>
        private static void BindTriggerSetLocked(TriggerSet set)
        {
            if (set?.AppliesToNpcIds == null) return;
            foreach (var npcId in set.AppliesToNpcIds)
            {
                if (string.IsNullOrWhiteSpace(npcId)) continue;
                List<TriggerSet> list;
                if (!_triggerSetsByNpc.TryGetValue(npcId, out list))
                {
                    list = new List<TriggerSet>();
                    _triggerSetsByNpc[npcId] = list;
                }
                if (!list.Contains(set)) list.Add(set);
            }
        }

        /// <summary>登记标签对话集的 NPC 绑定（调用方持锁）。
        /// 对应 UE5 NPCDefinition.TaggedDialogueSet 资产引用（CharacterDefinition 数据侧）；
        /// Flax 里 NPC 既可显式写 TaggedDialogueSetPath，也可由标签对话 JSON 反向绑定 NPCID。</summary>
        private static void BindTaggedDialogueSetLocked(TaggedDialogueSet set)
        {
            if (set?.AppliesToNpcIds == null) return;
            foreach (var npcId in set.AppliesToNpcIds)
            {
                if (string.IsNullOrWhiteSpace(npcId)) continue;
                if (!_taggedDialogueSetsByNpc.ContainsKey(npcId)) _taggedDialogueSetsByNpc[npcId] = set;
            }
        }

        // ===================================================================
        // 插件级幂等预热入口
        // ===================================================================

        private static bool _bInitialized;

        /// <summary>InitializeAll 是否已执行过。</summary>
        public static bool IsInitialized => _bInitialized;

        /// <summary>默认角色创建器配置目录。</summary>
        public static string DefaultCharacterCreatorDirectory = "Content/NarrativePro/CharacterCreator";

        /// <summary>
        /// 幂等预热：加载开发者设置 JSON、任务/对话/数据任务目录、角色创建器配置、
        /// 物品与战利品表数据库、foley 音效库与默认音乐集，并把结果写入各自的全局入口。
        /// 设计为在 <c>NarrativeProPlugin.Initialize()</c> 内调用一次：
        /// <code>NarrativePro.Data.NarrativeDataLoader.InitializeAll();</code>
        /// 缺失数据一律写 Error 级日志（不抛异常、不阻断启动），保证可诊断。
        /// </summary>
        public static bool InitializeAll(bool force = false)
        {
            if (_bInitialized && !force) return true;
            _bInitialized = true;

            if (force)
            {
                lock (_cacheLock)
                {
                    _quests.Clear();
                    _dialogues.Clear();
                    _triggerSets.Clear();
                    _taggedDialogueSets.Clear();
                    _triggerSetsByNpc.Clear();
                    _taggedDialogueSetsByNpc.Clear();
                }
            }

            NarrativeLog.Log($"[Data] NarrativeDataLoader.InitializeAll 开始。工程目录: {NarrativeDataPaths.ProjectDirectory}");

            WarmDeveloperSettings();

            // 触发器/标签对话数据预热（Content/NarrativePro/Triggers）：
            // NPC 定义就绪时经 NarrativeDataLoader 缓存取集并实例化触发器。
            WarmTriggers();

            var settings = new NarrativeSettings();

            // 任务目录
            var quests = LoadAllQuests(settings.DefaultQuestDirectory);
            lock (_cacheLock)
            {
                foreach (var kv in quests) _quests[kv.Key] = kv.Value;
            }
            ReportDirectoryLoad("任务(Quest)", settings.DefaultQuestDirectory, quests.Count);

            // 对话目录
            var dialogues = LoadAllDialogues(settings.DefaultDialogueDirectory);
            lock (_cacheLock)
            {
                foreach (var kv in dialogues) _dialogues[kv.Key] = kv.Value;
            }
            ReportDirectoryLoad("对话(Dialogue)", settings.DefaultDialogueDirectory, dialogues.Count);

            // 数据任务（走 NarrativeTaskManager 的幂等缓存，内部会经本加载器解析目录）
            try
            {
                NarrativeTaskManager.CacheNarrativeTasks();
            }
            catch (Exception ex)
            {
                NarrativeLog.LogError($"[Data] 预热数据任务失败: {ex.Message}");
            }

            // 角色创建器配置（目录缺失只报告，不报错到崩溃）
            try
            {
                var creatorConfigs = LoadAllCharacterCreatorConfigurations(DefaultCharacterCreatorDirectory);
                ReportDirectoryLoad("角色创建器配置", DefaultCharacterCreatorDirectory, creatorConfigs.Count);
            }
            catch (Exception ex)
            {
                NarrativeLog.LogError($"[Data] 预热角色创建器配置失败: {ex.Message}");
            }

            // 技能树（SkillTrees 的内容头：Skill_*/Perk_* 反射资产换成 JSON；
            // 没有这一步，SkillTreeComponent 的 SkillTreeSkills 永远是空表，HasPerk/BuyPerk 无从谈起）
            try
            {
                NarrativePro.SkillTrees.SkillTreeLoader.EnsureActive(force);
            }
            catch (Exception ex)
            {
                NarrativeLog.LogError($"[Data] 预热技能树失败: {ex.Message}");
            }

            // 物品 + 战利品表
            NarrativeItemDatabase.Initialize(force);

            // NPC / 行人定义 + 群体清单（NPC 通路的头部：没有定义就没有 SetNPCDefinition；
            // 行人定义同时登记进 NarrativePedAssetResolver，供 MassPedSpawnerSubsystem 换取）
            RegisterNpcDefinitions(force);

            // Foley 音效库（装入 FoleyAudioBank.Active，供 NarrativeFoleyPlayback 消费）
            FoleyAudioBankLoader.EnsureActive(force);

            // 默认音乐集（TaggedMusicSet，NarrativeMusicSubsystem.OnEnable 会兜底取用）
            TaggedMusicSetLoader.EnsureDefaultLoaded(force);

            // UI 主题设置（走 Flax JsonAsset 引用路径 "Content/GUI/NarrativeUISettings"，
            // 由 CommonUI 自身用 Content.Load 读取；这里只触发一次单例构建便于诊断）。
            try
            {
                NarrativePro.CommonUI.NarrativeUIDeveloperSettings.GetMutableDefault();
            }
            catch (Exception ex)
            {
                NarrativeLog.LogWarning($"[Data] UI 设置资产预热异常（回退默认主题）: {ex.Message}");
            }

            NarrativeLog.Log($"[Data] InitializeAll 完成：任务 {quests.Count} 个、对话 {dialogues.Count} 个、" +
                             $"物品定义 {System.Linq.Enumerable.Count(NarrativeItemDatabase.LoadedItemClassIds)} 条、" +
                             $"NPC 定义 {NarrativeNPCDatabase.Count} 条（行人 {NarrativeNPCDatabase.PedDefinitionPaths.Count} 条）。");
            return true;
        }

        /// <summary>
        /// 预热开发者设置。Settings/ 下三类（Combat/Input/TimeOfDay）的 LoadFromFile 已内置路径解析，
        /// 静态 Instance 触发即生效；Core/ArsenalSettings 与 Items/NarrativeInventorySettings 不在本目录
        /// 所有权范围内，改用其公开的 LoadFromFile + Instance setter 以解析后的绝对路径重新装载一次。
        /// </summary>
        private static void WarmDeveloperSettings()
        {
            // 触发各单例的 LoadDefault（内部已走 NarrativeDataPaths 解析）。
            _ = NarrativeCombatDeveloperSettings.Instance;
            _ = NarrativeInputSettings.Instance;
            _ = NarrativeTimeOfDaySettings.Instance;
            _ = NarrativeDeveloperSettings.Instance;

            ReportSettingsFile("战斗设置", NarrativeCombatDeveloperSettings.DefaultConfigPath);
            ReportSettingsFile("输入设置", NarrativeInputSettings.DefaultConfigPath);
            ReportSettingsFile("昼夜设置", NarrativeTimeOfDaySettings.DefaultConfigPath);

            var arsenal = ArsenalSettings.LoadFromFile(NarrativeDataPaths.Resolve(ArsenalSettings.DefaultConfigPath));
            if (arsenal != null) ArsenalSettings.Instance = arsenal;
            ReportSettingsFile("Arsenal 全局设置", ArsenalSettings.DefaultConfigPath);

            var inventory = NarrativeInventorySettings.LoadFromFile(NarrativeDataPaths.Resolve(NarrativeInventorySettings.DefaultConfigPath));
            if (inventory != null) NarrativeInventorySettings.Instance = inventory;
            ReportSettingsFile("背包设置", NarrativeInventorySettings.DefaultConfigPath);
        }

        /// <summary>
        /// 预热 NPC / 行人定义与群体生成清单。
        /// <para>
        /// 这是 NPC 通路的头部：<see cref="NarrativePro.AI.NPCDefinition"/> 此前既没有反序列化入口、
        /// 磁盘上也没有 NPC 内容，于是 <c>SpawnNPC</c> 无定义可用、<c>SetNPCDefinition</c> 无调用者，
        /// 活动调度 / 目标评分 / 攻击令牌 / 掩体 / 装备 / 战利品全线不可达。
        /// 本步骤把 <c>Content/NarrativePro/NPCs</c> 读进 <see cref="NarrativeNPCDatabase"/>（按 NPCID 建索引），
        /// 行人定义登记进 <see cref="NarrativePro.AI.Mass.Peds.NarrativePedAssetResolver"/>，
        /// 并预读 <c>Content/NarrativePro/Crowd</c> 的 <see cref="NarrativeCrowdConfig"/> 清单
        /// （实际生成由运行期宿主上的 <see cref="NarrativePro.Spawners.NPCCrowdSpawner"/> 执行）。
        /// </para>
        /// </summary>
        private static void RegisterNpcDefinitions(bool force = false)
        {
            try
            {
                NarrativeNPCDatabase.Initialize(force);
                ReportDirectoryLoad("NPC 定义", NarrativeNPCDatabase.DefaultNpcsDirectory, NarrativeNPCDatabase.Count);

                var configs = NarrativeCrowdConfig.LoadAll(force);
                if (configs.Count == 0)
                {
                    NarrativeLog.Log($"[Data] 群体清单为空（{NarrativeDataPaths.ResolveDirectory(NarrativeCrowdConfig.DefaultCrowdDirectory)}）：" +
                                     "NPC/行人不会自动生成。");
                    return;
                }

                int entries = 0;
                foreach (var config in configs) entries += config.Entries != null ? config.Entries.Count : 0;
                NarrativeLog.Log($"[Data] 群体清单 {configs.Count} 份、生成项 {entries} 条；" +
                                 $"行人定义 {NarrativeNPCDatabase.PedDefinitionPaths.Count} 条。");
            }
            catch (Exception ex)
            {
                NarrativeLog.LogError($"[Data] 预热 NPC 定义/群体清单失败: {ex.Message}");
            }
        }

        private static void ReportSettingsFile(string label, string relativePath)
        {
            string resolved = NarrativeDataPaths.Resolve(relativePath);
            if (File.Exists(resolved))
            {
                NarrativeLog.Log($"[Data] {label} 已就绪: {resolved}");
            }
            else
            {
                NarrativeLog.LogError($"[Data] {label} JSON 缺失: '{resolved}'（将按代码默认值运行）。Has it been renamed or moved?");
            }
        }

        private static void ReportDirectoryLoad(string label, string directory, int loadedCount)
        {
            string resolved = NarrativeDataPaths.ResolveDirectory(directory);
            if (!Directory.Exists(resolved))
            {
                NarrativeLog.LogError($"[Data] {label}目录不存在: '{resolved}' (配置值: {directory})。Has it been renamed or moved?");
            }
            else if (loadedCount == 0)
            {
                NarrativeLog.LogError($"[Data] {label}目录 '{resolved}' 存在但没有加载到任何可用 JSON（文件缺失或全部解析失败）。");
            }
        }

        private static string ReportIfDirectoryMissing(string directoryPath, string label)
        {
            string resolved = NarrativeDataPaths.ResolveDirectory(directoryPath);
            if (!Directory.Exists(resolved))
            {
                NarrativeLog.LogError($"[Data] {label}目录不存在: '{resolved}' (配置值: {directoryPath})。Has it been renamed or moved?");
                return null;
            }
            return resolved;
        }
    }
}
