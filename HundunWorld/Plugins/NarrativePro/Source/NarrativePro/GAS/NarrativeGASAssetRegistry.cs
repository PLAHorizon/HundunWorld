using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using FlaxEngine;
using NarrativePro.Core;

namespace NarrativePro.GAS
{
    /// <summary>
    /// GameplayEffect / GameplayAbility 的按 ID 或路径解析注册表。
    /// <para>
    /// 对应 UE5 中 <c>TSubclassOf&lt;UGameplayEffect&gt;</c> 与 <c>TSubclassOf&lt;UNarrativeGameplayAbility&gt;</c>
    /// 直接引用资产类的做法：Flax 没有蓝图类，因此这里提供等价的「软引用 → 运行时对象」解析层，
    /// 供 ASC 的 DefaultAttributes/Startup 效果、武器的能力授予、效果物品的 Use 等入口共用。
    /// </para>
    /// 解析顺序：
    /// 1. 代码注册表（<see cref="RegisterEffect"/> / <see cref="RegisterAbilityType"/>，含程序集自动扫描）；
    /// 2. JSON 定义文件（<c>*.json</c>，位于 <see cref="EffectDirectory"/> / 绝对路径）；
    /// 3. 失败返回 null 并写一条警告（不抛异常，保持调用侧行为不变）。
    /// </summary>
    public static class NarrativeGASAssetRegistry
    {
        // ===== 常量与目录 =====

        /// <summary>效果 JSON 定义默认目录（相对项目目录，可用 <see cref="SetEffectDirectory"/> 覆盖）。</summary>
        public static string EffectDirectory = "Content/NarrativePro/GAS/Effects";

        /// <summary>能力 JSON 定义目录（数据驱动能力：<c>*.json</c> 描述「输入标签 + 实现类 + 战斗参数」）。</summary>
        public static string AbilityDirectory = "Content/NarrativePro/GAS/Abilities";

        /// <summary>能力配置（默认属性效果 / 启动效果 / 默认能力）JSON 目录。</summary>
        public static string AbilityConfigurationDirectory = "Content/NarrativePro/GAS/Configs";

        // ===== 内部状态 =====

        private static readonly Dictionary<string, GameplayEffect> _effects = new Dictionary<string, GameplayEffect>(StringComparer.Ordinal);
        private static readonly Dictionary<string, Type> _abilityTypes = new Dictionary<string, Type>(StringComparer.Ordinal);
        private static readonly Dictionary<string, AbilityDefinition> _abilityDefinitions = new Dictionary<string, AbilityDefinition>(StringComparer.Ordinal);
        private static readonly HashSet<string> _missingEffectKeys = new HashSet<string>(StringComparer.Ordinal);
        private static readonly HashSet<string> _missingAbilityKeys = new HashSet<string>(StringComparer.Ordinal);
        private static bool _bScannedAbilityTypes;

        private static readonly JsonSerializerOptions _jsonOptions = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            ReadCommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = true,
            IncludeFields = true,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
        };

        /// <summary>覆盖效果定义目录（对应 UE5 项目设置里的资产路径配置）。</summary>
        public static void SetEffectDirectory(string directory)
        {
            if (!string.IsNullOrEmpty(directory)) EffectDirectory = directory;
        }

        /// <summary>覆盖能力定义目录。</summary>
        public static void SetAbilityDirectory(string directory)
        {
            if (!string.IsNullOrEmpty(directory)) AbilityDirectory = directory;
        }

        /// <summary>覆盖能力配置目录。</summary>
        public static void SetAbilityConfigurationDirectory(string directory)
        {
            if (!string.IsNullOrEmpty(directory)) AbilityConfigurationDirectory = directory;
        }

        // ===== 效果注册 / 解析 =====

        /// <summary>注册一个效果定义。键可以是 ID、类型名或路径，重复注册覆盖。</summary>
        public static void RegisterEffect(string key, GameplayEffect effect)
        {
            if (string.IsNullOrEmpty(key) || effect == null) return;
            _effects[NormalizeKey(key)] = effect;
            _missingEffectKeys.Remove(NormalizeKey(key));
        }

        /// <summary>注销效果定义。</summary>
        public static bool UnregisterEffect(string key)
        {
            if (string.IsNullOrEmpty(key)) return false;
            return _effects.Remove(NormalizeKey(key));
        }

        /// <summary>
        /// 解析效果定义。返回 null 表示未找到。
        /// 命中注册表时返回共享实例（等价 UE5 共享 CDO），调用方不应修改返回对象的修饰器列表；
        /// 需要按实例定制（SetByCaller 等）时先 <see cref="GameplayEffect"/> 复制再传给 Spec。
        /// </summary>
        public static GameplayEffect ResolveEffect(string keyOrPath)
        {
            if (string.IsNullOrEmpty(keyOrPath)) return null;

            string key = NormalizeKey(keyOrPath);
            if (_effects.TryGetValue(key, out var registered)) return registered;
            if (_missingEffectKeys.Contains(key)) return null;

            // 尝试从 JSON 定义文件加载
            var loaded = LoadEffectJson(keyOrPath, key);
            if (loaded != null)
            {
                _effects[key] = loaded;
                return loaded;
            }

            _missingEffectKeys.Add(key);
            NarrativeLog.LogWarning($"[GAS] 无法解析 GameplayEffect '{keyOrPath}'（注册表与 JSON 定义均未命中）");
            return null;
        }

        /// <summary>克隆一份效果定义，避免调用方就地修改共享实例。</summary>
        public static GameplayEffect CloneEffect(GameplayEffect source)
        {
            if (source == null) return null;
            var clone = new GameplayEffect(source.EffectName)
            {
                DurationType = source.DurationType,
                Duration = source.Duration,
                Period = source.Period,
                ExecuteCalcTypeId = source.ExecuteCalcTypeId,
                StackingType = source.StackingType,
                MaxStacks = source.MaxStacks
            };
            if (source.Modifiers != null)
            {
                foreach (var mod in source.Modifiers)
                {
                    if (mod == null) continue;
                    clone.Modifiers.Add(new GameplayModifierInfo
                    {
                        AttributeName = mod.AttributeName,
                        ModifierOp = mod.ModifierOp,
                        Magnitude = mod.Magnitude,
                        SetByCallerTag = mod.SetByCallerTag
                    });
                }
            }
            CopyTags(source.GrantedTags, clone.GrantedTags);
            CopyTags(source.AssetTags, clone.AssetTags);
            CopyTags(source.RequiredTags, clone.RequiredTags);
            return clone;
        }

        private static void CopyTags(Items.GameplayTagContainer from, Items.GameplayTagContainer to)
        {
            if (from == null || to == null) return;
            foreach (var tag in from.GetTags()) to.AddTag(new Items.GameplayTag(tag));
        }

        private static GameplayEffect LoadEffectJson(string keyOrPath, string key)
        {
            string path = ResolveJsonPath(keyOrPath, EffectDirectory, key);
            if (path == null) return null;

            try
            {
                string json = File.ReadAllText(path);
                var dto = JsonSerializer.Deserialize<GameplayEffectDefinition>(json, _jsonOptions);
                return dto?.ToEffect();
            }
            catch (Exception ex)
            {
                NarrativeLog.LogWarning($"[GAS] 解析效果定义 '{path}' 失败: {ex.Message}");
                return null;
            }
        }

        // ===== 能力注册 / 实例化 =====

        /// <summary>注册能力类型。键可以是 ID、类型名或路径。</summary>
        public static void RegisterAbilityType(string key, Type abilityType)
        {
            if (string.IsNullOrEmpty(key) || abilityType == null) return;
            if (!typeof(NarrativeGameplayAbility).IsAssignableFrom(abilityType))
            {
                NarrativeLog.LogError($"[GAS] 无法注册能力 '{key}': {abilityType.Name} 不是 NarrativeGameplayAbility 的子类");
                return;
            }
            _abilityTypes[NormalizeKey(key)] = abilityType;
        }

        /// <summary>按类型名与全名注册能力类型，便于 JSON/配置里两种写法都能命中。</summary>
        public static void RegisterAbilityType(Type abilityType)
        {
            if (abilityType == null) return;
            RegisterAbilityType(abilityType.Name, abilityType);
            RegisterAbilityType(abilityType.FullName, abilityType);
        }

        /// <summary>解析能力类型（先扫程序集，再查注册表，最后按全名硬解析）。</summary>
        public static Type ResolveAbilityType(string keyOrPath)
        {
            if (string.IsNullOrEmpty(keyOrPath)) return null;
            EnsureAbilityTypesScanned();

            string key = NormalizeKey(keyOrPath);
            if (_abilityTypes.TryGetValue(key, out var type)) return type;

            // 允许 "Package.Path/AbilityName" 这类 UE 风格路径：取末段再试一次
            string lastSegment = GetLastSegment(key);
            if (lastSegment != null && lastSegment != key && _abilityTypes.TryGetValue(lastSegment, out type)) return type;

            try
            {
                var direct = Type.GetType(keyOrPath, false, true);
                if (direct != null && typeof(NarrativeGameplayAbility).IsAssignableFrom(direct)) return direct;
            }
            catch
            {
                // 非法类型名，按未找到处理
            }

            return null;
        }

        /// <summary>
        /// 按**能力 id**（内容里的 JSON 文件名/注册键）解析实现类型：
        /// 先读 <c>Content/NarrativePro/GAS/Abilities/&lt;id&gt;.json</c> 的 <c>abilityClass</c>，
        /// 再按那个类名走类型注册表；没有定义文件时把入参本身当类名再试一次。
        /// <para>
        /// 与 <see cref="ResolveAbilityType"/> 的区别是必要的：后者只认类名/UE 风格路径，
        /// 拿内容 id（<c>ability_interact_mount</c> 这类）去问它永远返回 null。
        /// 这条 id→类型 的换算此前只藏在 <see cref="CreateAbilityInstance"/> 内部，
        /// 任何"只想知道 id 指向哪个类、还不想往 Actor 上挂脚本"的作者面都够不到它
        /// （交互槽位行为 <c>GetInteractAbility()</c> 正是第一个消费者）。
        /// </para>
        /// </summary>
        public static Type ResolveAbilityTypeById(string keyOrPath)
        {
            if (string.IsNullOrEmpty(keyOrPath)) return null;

            var definition = ResolveAbilityDefinition(keyOrPath);
            string typeKey = definition != null && !string.IsNullOrEmpty(definition.AbilityClass)
                ? definition.AbilityClass
                : keyOrPath;

            return ResolveAbilityType(typeKey);
        }

        /// <summary>
        /// 在宿主 Actor 上创建能力脚本实例。
        /// 先按 <see cref="AbilityDirectory"/> 的 JSON 定义解析（定义里的 <c>abilityClass</c> 指向实现类），
        /// 再回退到类型注册表（类名 / 全名）。
        /// 复用同类型已有实例（Flax 中能力是挂在 Actor 上的 Script，重复挂载会泄漏），
        /// 与 <c>InteractionSlotBehavior.ResolveInteractAbilityInstance</c> 的做法保持一致。
        /// 返回 null 表示类型无法解析或挂载失败。
        /// </summary>
        public static NarrativeGameplayAbility CreateAbilityInstance(Actor ownerActor, string keyOrPath)
        {
            var definition = ResolveAbilityDefinition(keyOrPath);
            var type = ResolveAbilityTypeById(keyOrPath);
            if (type == null)
            {
                NarrativeLog.LogWarning($"[GAS] 无法解析 GameplayAbility '{keyOrPath}'");
                return null;
            }

            NarrativeGameplayAbility ability = null;
            if (ownerActor != null)
            {
                foreach (var script in ownerActor.GetScripts(type))
                {
                    if (script is NarrativeGameplayAbility existing) ability = existing;
                }

                if (ability == null && ownerActor.AddScript(type) is NarrativeGameplayAbility created)
                {
                    created.Enabled = true;
                    ability = created;
                }

                if (ability == null) return null;
            }
            else
            {
                // 无宿主 Actor（例如离线构建 spec）：直接构造，OwningASC 由 GiveAbility 回填
                try
                {
                    ability = (NarrativeGameplayAbility)Activator.CreateInstance(type);
                }
                catch (Exception ex)
                {
                    NarrativeLog.LogError($"[GAS] 构造能力实例失败 '{keyOrPath}': {ex.Message}");
                    return null;
                }
            }

            // 数据定义的参数覆盖：每次授予都重放一遍，保证同类型实例被复用时配置仍来自本条定义
            definition?.ApplyTo(ability, keyOrPath);
            return ability;
        }

        /// <summary>
        /// 取某个能力定义（ID / 路径 / 类型名）声明的输入标签。
        /// 武器授予能力时用它把 <c>spec.InputTag</c> 填成控制器真正按下的标签；
        /// 没有数据定义（纯类型授予）时返回 <see cref="Items.GameplayTag.None"/>，由调用方按约定回退。
        /// </summary>
        public static Items.GameplayTag ResolveAbilityInputTag(string keyOrPath)
        {
            var definition = ResolveAbilityDefinition(keyOrPath);
            if (definition == null || string.IsNullOrEmpty(definition.InputTag)) return Items.GameplayTag.None;
            return new Items.GameplayTag(definition.InputTag);
        }

        /// <summary>
        /// 解析能力的 JSON 定义（<c>AbilityDirectory</c> 下的 <c>{key}.json</c>）。
        /// 未命中返回 null 并缓存缺失键，避免每次授予都做一次磁盘探测。
        /// </summary>
        public static AbilityDefinition ResolveAbilityDefinition(string keyOrPath)
        {
            if (string.IsNullOrEmpty(keyOrPath)) return null;

            string key = NormalizeKey(keyOrPath);
            if (_abilityDefinitions.TryGetValue(key, out var registered)) return registered;
            if (_missingAbilityKeys.Contains(key)) return null;

            string path = ResolveJsonPath(keyOrPath, AbilityDirectory, key);
            if (path != null)
            {
                try
                {
                    string json = File.ReadAllText(path);
                    var dto = JsonSerializer.Deserialize<AbilityDefinition>(json, _jsonOptions);
                    if (dto != null && !string.IsNullOrEmpty(dto.AbilityClass))
                    {
                        _abilityDefinitions[key] = dto;
                        return dto;
                    }
                }
                catch (Exception ex)
                {
                    NarrativeLog.LogWarning($"[GAS] 解析能力定义 '{path}' 失败: {ex.Message}");
                }
            }

            _missingAbilityKeys.Add(key);
            return null;
        }

        /// <summary>注册一条能力定义（测试或代码驱动的等价数据配置入口）。</summary>
        public static void RegisterAbilityDefinition(string key, AbilityDefinition definition)
        {
            if (string.IsNullOrEmpty(key) || definition == null) return;
            _abilityDefinitions[NormalizeKey(key)] = definition;
            _missingAbilityKeys.Remove(NormalizeKey(key));
        }

        /// <summary>
        /// 加载能力配置 JSON（对应 UE5 <c>UAbilityConfiguration</c> 资产）。
        /// 传入空 ID 时按 <see cref="AbilityConfigurationDirectory"/> 下的 <c>{id}.json</c> 解析。
        /// </summary>
        public static AbilityConfiguration LoadAbilityConfiguration(string keyOrPath)
        {
            if (string.IsNullOrEmpty(keyOrPath)) return null;

            string path = ResolveJsonPath(keyOrPath, AbilityConfigurationDirectory, NormalizeKey(keyOrPath));
            if (path == null)
            {
                NarrativeLog.LogWarning($"[GAS] 无法解析 AbilityConfiguration '{keyOrPath}'（目录: {AbilityConfigurationDirectory}）");
                return null;
            }

            try
            {
                return AbilityConfiguration.LoadFromJson(File.ReadAllText(path));
            }
            catch (Exception ex)
            {
                NarrativeLog.LogWarning($"[GAS] 解析能力配置 '{path}' 失败: {ex.Message}");
                return null;
            }
        }

        /// <summary>创建能力的同时指定输入标签，返回句柄（无效句柄表示失败）。</summary>
        public static GameplayAbilitySpecHandle CreateAndGiveAbility(NarrativeAbilitySystemComponent asc, string keyOrPath, float level = 1f, Items.GameplayTag inputTag = default)
        {
            if (asc == null) return GameplayAbilitySpecHandle.Invalid;
            var ability = CreateAbilityInstance(asc.Actor, keyOrPath);
            if (ability == null) return GameplayAbilitySpecHandle.Invalid;
            return asc.GiveAbility(ability, level, inputTag);
        }

        private static void EnsureAbilityTypesScanned()
        {
            if (_bScannedAbilityTypes) return;
            _bScannedAbilityTypes = true;

            try
            {
                var assembly = typeof(NarrativeGameplayAbility).Assembly;
                foreach (var type in assembly.GetTypes())
                {
                    if (type == null || type.IsAbstract || type.IsInterface) continue;
                    if (!typeof(NarrativeGameplayAbility).IsAssignableFrom(type)) continue;
                    if (type.GetConstructor(Type.EmptyTypes) == null) continue;
                    RegisterAbilityType(type);
                }
            }
            catch (Exception ex)
            {
                NarrativeLog.LogWarning($"[GAS] 能力类型扫描失败: {ex.Message}");
            }
        }

        // ===== 工具 =====

        /// <summary>归一化键：去掉扩展名、反斜杠转正斜杠、去掉 UE 包前缀，便于多种写法互相命中。</summary>
        private static string NormalizeKey(string raw)
        {
            if (string.IsNullOrEmpty(raw)) return "";
            string key = raw.Trim().Replace('\\', '/');
            key = GetLastSegment(key) ?? key;
            if (key.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
            {
                key = key.Substring(0, key.Length - 5);
            }
            return key;
        }

        private static string GetLastSegment(string key)
        {
            if (string.IsNullOrEmpty(key)) return null;
            int slash = key.LastIndexOf('/');
            if (slash < 0 || slash == key.Length - 1) return slash < 0 ? key : null;
            return key.Substring(slash + 1);
        }

        /// <summary>
        /// 把 ID/路径映射到磁盘上的 JSON 文件。支持绝对路径与相对目录两种写法。
        /// 找不到返回 null。
        /// </summary>
        private static string ResolveJsonPath(string keyOrPath, string fallbackDirectory, string normalizedKey)
        {
            var candidates = new List<string>();

            string trimmed = keyOrPath.Trim().Replace('\\', '/');
            bool looksLikePath = trimmed.Contains("/") || trimmed.EndsWith(".json", StringComparison.OrdinalIgnoreCase);
            if (looksLikePath)
            {
                if (!trimmed.EndsWith(".json", StringComparison.OrdinalIgnoreCase)) trimmed += ".json";
                candidates.Add(trimmed);
            }
            if (!string.IsNullOrEmpty(fallbackDirectory))
            {
                candidates.Add(Path.Combine(fallbackDirectory, normalizedKey + ".json"));
            }

            foreach (var candidate in candidates)
            {
                try
                {
                    // 工程相对目录（"Content/NarrativePro/GAS/..."）必须经 NarrativeDataPaths 归一到工程根，
                    // 否则 File.Exists 按进程当前工作目录解析，编辑器/PIE/打包三种 CWD 下会查不到定义文件
                    // （见 Data/NarrativeDataPaths.cs 头注释；物品库已走同一条解析，见 NarrativeItemDatabase.ResolveDirOrReport）。
                    string resolved = NarrativePro.Data.NarrativeDataPaths.Resolve(candidate);
                    if (File.Exists(resolved)) return resolved;
                }
                catch
                {
                    // 非法路径字符，跳过
                }
            }
            return null;
        }

        /// <summary>清空解析缓存（热重载或测试之间使用）。</summary>
        public static void ClearCaches()
        {
            _effects.Clear();
            _missingEffectKeys.Clear();
            _abilityTypes.Clear();
            _abilityDefinitions.Clear();
            _missingAbilityKeys.Clear();
            _bScannedAbilityTypes = false;
        }

        /// <summary>
        /// 能力的 JSON 传输模型（对应 UE5 蓝图攻击能力的类默认值面板）。
        /// <para>
        /// UE5 里"一个攻击能力"是一个蓝图资产；Flax 端口把能力实现留在 C# 类里，
        /// 用这份 DTO 提供"同一实现、不同数据"的那一半：输入标签、近战/重击开关、
        /// 伤害效果引用、动画通知窗口与 Bot 攻击参数。
        /// </para>
        /// 反序列化走 <see cref="_jsonOptions"/>（属性名大小写不敏感），因此 JSON 用 camelCase 即可。
        /// </summary>
        public class AbilityDefinition
        {
            /// <summary>能力显示名（调试用）。</summary>
            public string AbilityName { get; set; } = "";

            /// <summary>实现类名（<see cref="NarrativeGameplayAbility"/> 的短名或全名）。</summary>
            public string AbilityClass { get; set; } = "";

            /// <summary>输入标签全名，如 "Narrative.Input.Attack"。</summary>
            public string InputTag { get; set; } = "";

            /// <summary>是否近战（true 走 WeaponVisual 动画通知扫描窗口）。</summary>
            public bool IsMelee { get; set; } = false;

            /// <summary>是否重击。</summary>
            public bool IsHeavyAttack { get; set; } = false;

            /// <summary>是否需要弹药；不写（null）时由武器数据决定（对应 UE5 只有需要弹药的武器才过弹药门槛）。</summary>
            public bool? RequiresAmmo { get; set; }

            /// <summary>默认攻击伤害（属性集缺失时使用）。</summary>
            public float DefaultAttackDamage { get; set; } = 10f;

            /// <summary>伤害效果定义 ID/路径（对应 UE5 攻击能力上配置的 Damage GE 软引用）。</summary>
            public string DamageEffectPath { get; set; } = "";

            /// <summary>近战扫描半径（厘米）。</summary>
            public float MeleeSweepRadius { get; set; } = 80f;

            /// <summary>攻击通知触发时间（秒）。</summary>
            public float AttackNotifyTriggerTime { get; set; } = 0.1f;

            /// <summary>攻击通知持续时长（秒）。</summary>
            public float AttackNotifyDuration { get; set; } = 0.35f;

            /// <summary>攻击动画播放速率。</summary>
            public float AttackPlayRate { get; set; } = 1f;

            /// <summary>Bot 攻击频率（次/秒）。</summary>
            public float BotAttackFrequency { get; set; } = 1f;

            /// <summary>Bot 攻击范围。</summary>
            public float BotAttackRange { get; set; } = 300f;

            /// <summary>武器散射角（度）。</summary>
            public float WeaponSpread { get; set; } = 0f;

            /// <summary>能力自身的标签（用于 ClearAbilitiesWithTag 与伤害类型判定）。</summary>
            public List<string> AbilityTags { get; set; }

            // ===== 交互能力（NarrativeInteractAbility）的作者数据 =====
            // UE5 把这些值写在每个交互能力的蓝图 CDO 上。全部用可空类型：
            // 资产没写的那一项保持类字段默认值，而不是被 DTO 默认值悄悄覆盖。
            // 没有这一段时，"同一实现、不同数据"对交互能力根本不成立——
            // 所有交互都会共用 NarrativeInteractAbility 的类默认值。

            /// <summary>触发方式（Press/Hold/…）。对应 UE5 交互能力蓝图上的 InteractType。</summary>
            public EInteractType? InteractType { get; set; }

            /// <summary>可交互距离（厘米）。</summary>
            public float? InteractionRange { get; set; }

            /// <summary>按住时长（秒，Hold 用）。</summary>
            public float? InteractionTime { get; set; }

            /// <summary>交互总时长（秒）。</summary>
            public float? InteractionDuration { get; set; }

            /// <summary>交互开始前的延迟（秒）。</summary>
            public float? InteractionDelay { get; set; }

            /// <summary>两次交互之间的冷却（秒）。</summary>
            public float? InteractionCooldown { get; set; }

            /// <summary>需要重复触发几次才算完成（0 = 一次）。</summary>
            public int? NumInteractions { get; set; }

            /// <summary>是否允许与阻挡 Actor 交互。</summary>
            public bool? bInteractWithBlockingActors { get; set; }

            /// <summary>射线通道名。</summary>
            public string TraceChannel { get; set; }

            /// <summary>碰撞模板名。</summary>
            public string CollisionProfileName { get; set; }

            /// <summary>把这份数据写进能力实例。只覆盖有对应字段的战斗能力，其余能力原样返回。</summary>
            public void ApplyTo(NarrativeGameplayAbility ability, string definitionKey)
            {
                if (ability == null) return;

                ability.AbilityDefinitionKey = definitionKey ?? "";
                if (!string.IsNullOrEmpty(AbilityName)) ability.AbilityName = AbilityName;
                if (!string.IsNullOrEmpty(InputTag)) ability.InputTag = new Items.GameplayTag(InputTag);
                if (AbilityTags != null && ability.AbilityTags != null)
                {
                    foreach (var tag in AbilityTags)
                    {
                        if (!string.IsNullOrEmpty(tag)) ability.AbilityTags.AddTag(new Items.GameplayTag(tag));
                    }
                }

                if (!(ability is NarrativeCombatAbility combat))
                {
                    // 交互能力的"同一实现、不同数据"那一半：资产写了才覆盖，没写保持类默认。
                    if (ability is NarrativeInteractAbility interact)
                    {
                        if (InteractType.HasValue) interact.InteractType = InteractType.Value;
                        if (InteractionRange.HasValue) interact.InteractionRange = InteractionRange.Value;
                        if (InteractionTime.HasValue) interact.InteractionTime = InteractionTime.Value;
                        if (InteractionDuration.HasValue) interact.InteractionDuration = InteractionDuration.Value;
                        if (InteractionDelay.HasValue) interact.InteractionDelay = InteractionDelay.Value;
                        if (InteractionCooldown.HasValue) interact.InteractionCooldown = InteractionCooldown.Value;
                        if (NumInteractions.HasValue) interact.NumInteractions = NumInteractions.Value;
                        if (bInteractWithBlockingActors.HasValue)
                            interact.bInteractWithBlockingActors = bInteractWithBlockingActors.Value;
                        if (!string.IsNullOrEmpty(TraceChannel)) interact.TraceChannel = TraceChannel;
                        if (!string.IsNullOrEmpty(CollisionProfileName))
                            interact.CollisionProfileName = CollisionProfileName;
                    }
                    return;
                }

                combat.IsMelee = IsMelee;
                combat.IsHeavyAttack = IsHeavyAttack;
                combat.DefaultAttackDamage = DefaultAttackDamage;
                combat.MeleeSweepRadius = MeleeSweepRadius;
                combat.AttackNotifyTriggerTime = AttackNotifyTriggerTime;
                combat.AttackNotifyDuration = AttackNotifyDuration;
                combat.AttackPlayRate = AttackPlayRate > 0f ? AttackPlayRate : 1f;
                combat.DefaultBotAttackFrequency = BotAttackFrequency;
                combat.DefaultBotAttackRange = BotAttackRange;
                combat.WeaponSpread = WeaponSpread;
                // RequiresAmmo 为 null 时保留能力类自身的默认（近战能力 = false），
                // 显式写了才覆盖，与 UE5"只有需要弹药的武器才检查弹药"一致。
                if (RequiresAmmo.HasValue) combat.bRequiresAmmo = RequiresAmmo.Value;
                if (!string.IsNullOrEmpty(DamageEffectPath)) combat.DamageEffectPath = DamageEffectPath;
            }
        }

        /// <summary>
        /// 效果的 JSON 传输模型。对应 UE5 GameplayEffect 资产的可序列化子集。
        /// 用独立 DTO 而不是直接反序列化 <see cref="GameplayEffect"/>，
        /// 因为 <see cref="Items.GameplayTagContainer"/> 内部是无公开成员去重集合，直接反序列化会静默丢标签。
        /// </summary>
        private class GameplayEffectDefinition
        {
            public string EffectName { get; set; } = "";
            public string DurationType { get; set; } = "Instant";
            public float Duration { get; set; } = 0f;
            public float Period { get; set; } = 0f;
            public string ExecuteCalcTypeId { get; set; } = "";
            public string StackingType { get; set; } = "None";
            public int MaxStacks { get; set; } = 1;
            public List<string> GrantedTags { get; set; }
            public List<string> AssetTags { get; set; }
            public List<string> RequiredTags { get; set; }
            public List<ModifierDefinition> Modifiers { get; set; }

            public class ModifierDefinition
            {
                public string AttributeName { get; set; } = "";
                public string ModifierOp { get; set; } = "Add";
                public float Magnitude { get; set; } = 0f;
                public string SetByCallerTag { get; set; } = "";
            }

            public GameplayEffect ToEffect()
            {
                var effect = new GameplayEffect(EffectName ?? "")
                {
                    Duration = Duration,
                    Period = Period,
                    ExecuteCalcTypeId = ExecuteCalcTypeId ?? "",
                    MaxStacks = MaxStacks <= 0 ? 1 : MaxStacks
                };
                effect.DurationType = ParseEnum(DurationType, EGameplayEffectDurationType.Instant);
                effect.StackingType = ParseEnum(StackingType, EGameplayEffectStackingType.None);

                if (GrantedTags != null)
                    foreach (var t in GrantedTags) effect.GrantedTags.AddTag(new Items.GameplayTag(t));
                if (AssetTags != null)
                    foreach (var t in AssetTags) effect.AssetTags.AddTag(new Items.GameplayTag(t));
                if (RequiredTags != null)
                    foreach (var t in RequiredTags) effect.RequiredTags.AddTag(new Items.GameplayTag(t));

                if (Modifiers != null)
                {
                    foreach (var mod in Modifiers)
                    {
                        if (mod == null || string.IsNullOrEmpty(mod.AttributeName)) continue;
                        effect.Modifiers.Add(new GameplayModifierInfo
                        {
                            AttributeName = mod.AttributeName,
                            ModifierOp = ParseEnum(mod.ModifierOp, EGameplayModOp.Add),
                            Magnitude = mod.Magnitude,
                            SetByCallerTag = string.IsNullOrEmpty(mod.SetByCallerTag)
                                ? Items.GameplayTag.None
                                : new Items.GameplayTag(mod.SetByCallerTag)
                        });
                    }
                }
                return effect;
            }

            private static T ParseEnum<T>(string value, T fallback) where T : struct
            {
                if (string.IsNullOrEmpty(value)) return fallback;
                return Enum.TryParse<T>(value, true, out var parsed) ? parsed : fallback;
            }
        }
    }
}
