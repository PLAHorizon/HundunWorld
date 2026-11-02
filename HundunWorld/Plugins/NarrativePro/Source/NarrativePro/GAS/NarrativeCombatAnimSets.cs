using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using NarrativePro.Core;

namespace NarrativePro.GAS
{
    /// <summary>
    /// 战斗动画集注册表。UE5 中 <c>UNarrativeAnimSet</c> / <c>AttackComboAnimSet</c> 是数据资产，
    /// 武器（<c>UWeaponItem::GetComboAnims</c>，WeaponItem.h:337）返回资产引用数组；
    /// Flax 端口用"注册表键"替代资产引用：武器 JSON 写 <c>comboAnimSetId</c>，
    /// 攻击能力（<c>NarrativeCombatAbility.BeginMeleeAttackWindow / CreateAttackNotifyEvent</c>）
    /// 经 <see cref="Resolve"/> 取回连击段数据，驱动连击轮转与本次挥击的动画对选择。
    /// <para>
    /// 数据来源（与 <see cref="NarrativeGASAssetRegistry"/> 同口径）：
    /// 1. JSON 目录 <c>Content/NarrativePro/GAS/AnimSets/*.json</c>（文件名即键，
    ///    可被 <c>AnimSetId</c> 字段显式覆盖；camelCase 绑定，PropertyNameCaseInsensitive+IncludeFields）；
    /// 2. 内联种子（等价 <c>ExecutionCalculationRegistry</c> 的静态注册写法，NarrativeExecCalc.cs:238），
    ///    以及 <see cref="RegisterComboSet"/> / <see cref="RegisterAnimSet"/> 的代码入口。
    /// </para>
    /// </summary>
    public static class NarrativeCombatAnimSets
    {
        /// <summary>动画集 JSON 目录（相对工程根，经 NarrativeDataPaths 归一）。</summary>
        public static string AnimSetDirectory = "Content/NarrativePro/GAS/AnimSets";

        // 与 NarrativeGASAssetRegistry._jsonOptions 同绑定模式：大小写不敏感 + 包含字段（本项目已两次踩 camelCase 绑定坑）
        private static readonly JsonSerializerOptions _jsonOptions = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            ReadCommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = true,
            IncludeFields = true
        };

        private static readonly Dictionary<string, AttackComboAnimSet> _comboSets =
            new Dictionary<string, AttackComboAnimSet>(StringComparer.OrdinalIgnoreCase);

        private static readonly Dictionary<string, NarrativeAnimSet> _animSets =
            new Dictionary<string, NarrativeAnimSet>(StringComparer.OrdinalIgnoreCase);

        private static readonly HashSet<string> _missingKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        private static bool _bDirectoryScanned;

        static NarrativeCombatAnimSets()
        {
            // 内联种子：与 ExecutionCalculationRegistry 的静态初始化同款写法，
            // 保证 JSON 缺失时近战仍有默认三段轻击 + 一段重击的轮转数据。
            _comboSets["melee_generic_1h"] = BuildGenericMeleeCombo();
        }

        /// <summary>按注册键解析连击动画集。空键或未命中返回 null（武器基类口径：UE5 基实现返回空数组）。</summary>
        public static AttackComboAnimSet Resolve(string keyOrPath)
        {
            if (string.IsNullOrEmpty(keyOrPath)) return null;
            EnsureScanned();

            string key = NormalizeKey(keyOrPath);
            if (_comboSets.TryGetValue(key, out var set)) return set;

            // 兼容"键指向单个动画集文件"的写法
            if (_animSets.TryGetValue(key, out var single))
            {
                var wrapper = new AttackComboAnimSet { AnimSetId = key };
                wrapper.AttackCombos.Add(single);
                return wrapper;
            }

            if (_missingKeys.Add(key))
            {
                NarrativeLog.LogWarning($"[GAS] 连击动画集 '{keyOrPath}' 未注册（目录: {AnimSetDirectory}），武器将按无连击动画处理");
            }
            return null;
        }

        /// <summary>按注册键解析单个动画集（供魔法武器等逐段引用）。未命中返回 null。</summary>
        public static NarrativeAnimSet ResolveAnimSet(string keyOrPath)
        {
            if (string.IsNullOrEmpty(keyOrPath)) return null;
            EnsureScanned();

            string key = NormalizeKey(keyOrPath);
            if (_animSets.TryGetValue(key, out var set)) return set;
            if (_comboSets.TryGetValue(key, out var combo) &&
                combo.AttackCombos != null && combo.AttackCombos.Count > 0)
            {
                return combo.AttackCombos[0];
            }
            if (_missingKeys.Add(key))
            {
                NarrativeLog.LogWarning($"[GAS] 动画集 '{keyOrPath}' 未注册（目录: {AnimSetDirectory}）");
            }
            return null;
        }

        /// <summary>注册（或覆盖）一条连击动画集。key 为空时用 AnimSetId。</summary>
        public static void RegisterComboSet(string key, AttackComboAnimSet set)
        {
            if (set == null) return;
            string k = NormalizeKey(string.IsNullOrEmpty(key) ? set.AnimSetId : key);
            if (string.IsNullOrEmpty(k)) return;
            _comboSets[k] = set;
            _missingKeys.Remove(k);
        }

        /// <summary>注册（或覆盖）一条单段动画集。</summary>
        public static void RegisterAnimSet(string key, NarrativeAnimSet set)
        {
            if (string.IsNullOrEmpty(key) || set == null) return;
            _animSets[NormalizeKey(key)] = set;
            _missingKeys.Remove(NormalizeKey(key));
        }

        /// <summary>清空解析缓存（热重载或测试之间使用）。</summary>
        public static void ClearCaches()
        {
            _comboSets.Clear();
            _animSets.Clear();
            _missingKeys.Clear();
            _bDirectoryScanned = false;
            _comboSets["melee_generic_1h"] = BuildGenericMeleeCombo();
        }

        private static string NormalizeKey(string keyOrPath)
        {
            if (string.IsNullOrEmpty(keyOrPath)) return "";
            string trimmed = keyOrPath.Trim().Replace('\\', '/');
            int slash = trimmed.LastIndexOf('/');
            if (slash >= 0) trimmed = trimmed.Substring(slash + 1);
            if (trimmed.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
                trimmed = trimmed.Substring(0, trimmed.Length - 5);
            return trimmed;
        }

        /// <summary>
        /// 扫描 AnimSets 目录：能按 <see cref="AttackComboAnimSet"/>（连击段非空）反序列化的进连击表，
        /// 否则按 <see cref="NarrativeAnimSet"/>（characterAnims 非空）进单段表。只扫一次。
        /// </summary>
        private static void EnsureScanned()
        {
            if (_bDirectoryScanned) return;
            _bDirectoryScanned = true;

            try
            {
                // 工程相对目录必须经 NarrativeDataPaths 归一到工程根（同 NarrativeGASAssetRegistry.ResolveJsonPath 注释）
                string dir = NarrativePro.Data.NarrativeDataPaths.Resolve(AnimSetDirectory);
                if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir)) return;

                foreach (string path in Directory.GetFiles(dir, "*.json"))
                {
                    try
                    {
                        string json = File.ReadAllText(path);
                        string fileKey = Path.GetFileNameWithoutExtension(path);

                        var combo = JsonSerializer.Deserialize<AttackComboAnimSet>(json, _jsonOptions);
                        if (combo != null &&
                            ((combo.AttackCombos != null && combo.AttackCombos.Count > 0) ||
                             (combo.HeavyAttackCombos != null && combo.HeavyAttackCombos.Count > 0)))
                        {
                            string key = string.IsNullOrEmpty(combo.AnimSetId) ? fileKey : NormalizeKey(combo.AnimSetId);
                            _comboSets[key] = combo;
                            _missingKeys.Remove(key);
                            continue;
                        }

                        var single = JsonSerializer.Deserialize<NarrativeAnimSet>(json, _jsonOptions);
                        if (single != null && single.CharacterAnims != null && single.CharacterAnims.Count > 0)
                        {
                            string key = string.IsNullOrEmpty(single.AnimSetId) ? fileKey : NormalizeKey(single.AnimSetId);
                            _animSets[key] = single;
                            _missingKeys.Remove(key);
                        }
                    }
                    catch (Exception ex)
                    {
                        NarrativeLog.LogWarning($"[GAS] 解析动画集定义 '{path}' 失败: {ex.Message}");
                    }
                }
            }
            catch (Exception ex)
            {
                NarrativeLog.LogWarning($"[GAS] 扫描动画集目录失败: {ex.Message}");
            }
        }

        /// <summary>默认近战连击种子：三段轻击 + 一段重击（蒙太奇路径占位，同 AttackComboAnimSet.cs 的简化口径）。</summary>
        private static AttackComboAnimSet BuildGenericMeleeCombo()
        {
            var set = new AttackComboAnimSet { AnimSetId = "melee_generic_1h" };
            set.AttackCombos.Add(Stage("Combat/GenericMelee/Jab1"));
            set.AttackCombos.Add(Stage("Combat/GenericMelee/Jab2"));
            set.AttackCombos.Add(Stage("Combat/GenericMelee/Jab3"));
            set.HeavyAttackCombos.Add(Stage("Combat/GenericMelee/Heavy"));
            return set;
        }

        private static NarrativeAnimSet Stage(string montage3P)
        {
            var stage = new NarrativeAnimSet();
            stage.CharacterAnims.Add(new NarrativeCharacterAnimation("", montage3P));
            return stage;
        }
    }
}
