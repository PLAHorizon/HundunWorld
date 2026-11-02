// 模块关系说明：
// 本文件属于 NarrativePro.SaveSystem（UE5 NarrativeSaveSystem 移植）。
// UE5 中 Actor/组件状态由 FObjectAndNameAsStringProxyArchive + UPROPERTY(SaveGame) 反射归档
// （见 NarrativeSaveSubsystem.cpp:714-741 的 Actor->Serialize(Ar) 与组件循环）。
// Flax 没有 SaveGame 反射归档，本文件提供等价的托管实现：
//   - NarrativeSaveGameAttribute：对应 UPROPERTY(SaveGame)，显式标记参与归档的成员；
//   - NarrativeNoSaveAttribute：显式排除成员；
//   - NarrativeSaveStateArchive：把任意托管对象（Actor 与其挂载的 Script）的公共字段/可写属性
//     编码为 JSON 字节，并可还原回对象。引擎值类型（Vector3/Quaternion/Transform 等）按字段显式编码；
//     引擎对象引用（FlaxEngine.Object 派生：Actor/Script/资产/GUI 控件）与委托等不可持久化成员编码为 null，
//     因此不会序列化原生指针，也不会因对象图而崩溃；
//   - NarrativeSaveStateProviderComponent：具体可用的 INarrativeSaveStateProvider 组件，
//     挂载到 Actor 上即拥有完整状态归档能力（无需修改其它脚本源码）。
//
// 与 NarrativePro.Save 模块的关系：Save 做业务级 JSON 存档，本文件做对象级状态归档，互不替代。

using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.Json;
using FlaxEngine;
using NarrativePro.Core;
using NarrativePro.SaveSystem.Subsystems;

namespace NarrativePro.SaveSystem
{
    /// <summary>
    /// 标记字段/属性参与存档归档，对应 UE5 的 UPROPERTY(SaveGame)。
    /// 若一个类型上存在至少一个被此特性标记的成员，则只归档被标记的成员（严格 UE5 语义）；
    /// 否则归档该类型全部可安全持久化的公共成员（Flax 端没有 SaveGame 标记时的保守等价行为）。
    /// </summary>
    [AttributeUsage(AttributeTargets.Field | AttributeTargets.Property, AllowMultiple = false)]
    public sealed class NarrativeSaveGameAttribute : Attribute
    {
    }

    /// <summary>将字段/属性显式排除出存档归档。</summary>
    [AttributeUsage(AttributeTargets.Field | AttributeTargets.Property, AllowMultiple = false)]
    public sealed class NarrativeNoSaveAttribute : Attribute
    {
    }

    /// <summary>归档目标：一个参与状态捕获的托管对象及其在存档中的稳定键。</summary>
    public struct NarrativeStateTarget
    {
        /// <summary>稳定键（通常为类型全名，重名时附加 #索引）。</summary>
        public string Key;

        /// <summary>被归档的对象（Actor 或挂载的 Script）。</summary>
        public System.Object Object;

        /// <summary>构造归档目标。</summary>
        public NarrativeStateTarget(string key, System.Object target)
        {
            Key = key;
            Object = target;
        }
    }

    /// <summary>读档条目定位的纯裁决结果码（见 NarrativeSaveStateArchive.DecideTargetResolution）。</summary>
    public enum ENarrativeTargetResolve
    {
        /// <summary>按 id 与按名字都没有命中任何对象 —— 该条直接跳过。</summary>
        NotFound = 0,
        /// <summary>命中了对象，但载荷指纹与记录不符 —— 拒绝应用字节（新档闸门）。</summary>
        RefusedFingerprintMismatch = 1,
        /// <summary>命中且允许应用（新档指纹一致，或老档无指纹）。</summary>
        ResolvedCandidate = 2,
        /// <summary>id 命中的对象指纹不符，改用按名字命中的对象（其指纹相符）。</summary>
        ResolvedByKeyOverId = 3,
    }

    /// <summary>
    /// <see cref="ENarrativeTargetResolve"/> 的裁决载体：要应用字节的对象、是否拒绝、
    /// 以及调用方需要补绑的 slot id。纯数据，无副作用。
    /// </summary>
    public struct NarrativeTargetResolveResult
    {
        /// <summary>裁决结果码。</summary>
        public ENarrativeTargetResolve Verdict;

        /// <summary>首选候选（按 id 命中优先，其次按名字命中）；未命中时为 null，仅用于告警定位。</summary>
        public System.Object Candidate;

        /// <summary>裁决为允许应用时，实际写入字节的对象；拒绝/未命中时为 null。</summary>
        public System.Object Target;

        /// <summary>非空时调用方需要执行 <c>NarrativeComponentKeyRegistry.Bind(Target, BindSlotId)</c>。</summary>
        public string BindSlotId;

        /// <summary>候选对象的载荷指纹（拒绝时用于告警文案）。</summary>
        public string CandidateFingerprint;

        /// <summary>该记录没有载荷指纹（补写指纹之前存下的老档），必须容忍、不得拒绝。</summary>
        public bool bLegacyRecord;
    }

    /// <summary>
    /// 存档状态归档器，对应 UE5 FObjectAndNameAsStringProxyArchive 的 Flax 等价实现。
    /// 产出 UTF-8 JSON 字节，写入 NarrativeActorRecord.ByteData / NarrativeSaveComponent.ByteData。
    /// </summary>
    public static class NarrativeSaveStateArchive
    {
        private const int PayloadVersion = 1;

        private static readonly JsonSerializerOptions _options = CreateOptions();

        // ===== 引擎值类型白名单（按名称解析，避免与引擎版本产生编译耦合） =====

        private static readonly string[] EngineValueTypeNames =
        {
            "FlaxEngine.Vector2", "FlaxEngine.Vector3", "FlaxEngine.Vector4",
            "FlaxEngine.Int2", "FlaxEngine.Int3", "FlaxEngine.Int4",
            "FlaxEngine.Float2", "FlaxEngine.Float3",
            "FlaxEngine.Quaternion", "FlaxEngine.Matrix", "FlaxEngine.Transform",
            "FlaxEngine.Color", "FlaxEngine.Color3",
            "FlaxEngine.BoundingBox", "FlaxEngine.BoundingSphere", "FlaxEngine.Plane", "FlaxEngine.Ray",
            "FlaxEngine.Rectangle", "FlaxEngine.RectangleInt",
        };

        private static Dictionary<Type, FieldInfo[]> _engineValueTypes;

        private static Dictionary<Type, FieldInfo[]> EngineValueTypes
        {
            get
            {
                if (_engineValueTypes == null)
                {
                    _engineValueTypes = new Dictionary<Type, FieldInfo[]>();
                    foreach (var name in EngineValueTypeNames)
                    {
                        var type = Type.GetType(name + ", FlaxEngine.CSharp", false);
                        if (type == null || !type.IsValueType) continue;
                        var fields = type.GetFields(BindingFlags.Public | BindingFlags.Instance)
                            .Where(f => !f.IsInitOnly)
                            .ToArray();
                        if (fields.Length == 0) continue;
                        _engineValueTypes[type] = fields;
                    }
                }
                return _engineValueTypes;
            }
        }

        private static JsonSerializerOptions CreateOptions()
        {
            var options = new JsonSerializerOptions
            {
                IncludeFields = true,
                WriteIndented = false,
                MaxDepth = 24,
                ReferenceHandler = System.Text.Json.Serialization.ReferenceHandler.IgnoreCycles,
                NumberHandling = System.Text.Json.Serialization.JsonNumberHandling.AllowNamedFloatingPointLiterals,
                DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.Never,
            };
            // 顺序重要：先处理引擎值类型（按字段编码），再把不可持久化类型统一写成 null。
            options.Converters.Add(new FlaxValueStructConverterFactory());
            options.Converters.Add(new NonPersistentTypeConverterFactory());
            return options;
        }

        /// <summary>
        /// 创建带引擎值类型支持（Vector3/Quaternion/Transform 等按字段编码）的序列化选项。
        /// 存档文件本身也必须用它，否则 Flax 结构体的公共字段不会被 System.Text.Json 写入。
        /// </summary>
        public static JsonSerializerOptions CreateSaveFileOptions(bool writeIndented)
        {
            var options = CreateOptions();
            options.WriteIndented = writeIndented;
            return options;
        }

        // ===== 公共 API =====

        /// <summary>
        /// 捕获一组目标对象的状态。返回 JSON 字节；无状态可捕获时返回 null。
        /// 实现 INarrativeSaveStateProvider 的目标改由其 GetSaveStateBytes() 产出字节（不再反射，避免重复）。
        /// </summary>
        public static byte[] Capture(IList<NarrativeStateTarget> targets)
        {
            if (targets == null || targets.Count == 0) return null;

            var targetEntries = new List<Dictionary<string, object>>();
            var providerEntries = new List<Dictionary<string, object>>();
            var seenKeys = new HashSet<string>(StringComparer.Ordinal);

            foreach (var target in targets)
            {
                var obj = target.Object;
                if (obj == null) continue;

                string key = string.IsNullOrEmpty(target.Key) ? GetKeyForObject(obj) : target.Key;
                if (!seenKeys.Add(key))
                {
                    // 调用方给出的键必须唯一；重复键会让读档侧的键→对象映射串位，直接丢弃后者
                    NarrativeLog.LogWarning($"[SaveSystem] 归档目标键重复，已忽略：{key}");
                    continue;
                }

                // 稳定身份：确定性 slot id（"i" 字段）= 归档键本身，写读两侧同规则可重算，跨重启有效；
                // 会话内注册表命中时用首次固定的 id。键（"k"）保持旧语义，老档解析路径不变。
                // "f" 字段是该对象载荷指纹（声明类型+成员数+有序成员名散列），读档不匹配即拒绝应用。
                string slotId = NarrativeComponentKeyRegistry.GetOrCreateSlotId(obj, key);
                string fingerprint = ComputeFingerprintForObject(obj);

                if (obj is INarrativeSaveStateProvider provider)
                {
                    byte[] raw = null;
                    try
                    {
                        raw = provider.GetSaveStateBytes();
                    }
                    catch (Exception ex)
                    {
                        NarrativeLog.LogWarning($"[SaveSystem] GetSaveStateBytes 失败 {key}: {ex.Message}");
                    }

                    if (raw != null && raw.Length > 0)
                    {
                        providerEntries.Add(new Dictionary<string, object>
                        {
                            ["k"] = key,
                            ["i"] = slotId,
                            ["f"] = fingerprint,
                            ["b"] = Convert.ToBase64String(raw),
                        });
                        continue;
                    }
                }

                var members = CaptureMembers(obj);
                if (members != null && members.Count > 0)
                {
                    targetEntries.Add(new Dictionary<string, object>
                    {
                        ["k"] = key,
                        ["i"] = slotId,
                        ["f"] = fingerprint,
                        ["m"] = members,
                    });
                }
            }

            if (targetEntries.Count == 0 && providerEntries.Count == 0) return null;

            var payload = new Dictionary<string, object>
            {
                ["v"] = PayloadVersion,
            };
            if (targetEntries.Count > 0) payload["t"] = targetEntries;
            if (providerEntries.Count > 0) payload["p"] = providerEntries;

            try
            {
                return Encoding.UTF8.GetBytes(JsonSerializer.Serialize(payload, _options));
            }
            catch (Exception ex)
            {
                // 整体序列化失败时降级：逐目标重试，确保单点异常不会吞掉全部状态。
                NarrativeLog.LogWarning($"[SaveSystem] 状态归档整体序列化失败，改用逐目标归档: {ex.Message}");
                return CaptureFallback(targetEntries, providerEntries);
            }
        }

        private static byte[] CaptureFallback(List<Dictionary<string, object>> targetEntries, List<Dictionary<string, object>> providerEntries)
        {
            var kept = new List<Dictionary<string, object>>();
            foreach (var entry in targetEntries)
            {
                try
                {
                    JsonSerializer.Serialize(entry, _options);
                    kept.Add(entry);
                }
                catch (Exception ex)
                {
                    entry.TryGetValue("k", out var keyObject);
                    NarrativeLog.LogWarning($"[SaveSystem] 目标状态无法归档 {keyObject}: {ex.Message}");
                }
            }

            if (kept.Count == 0 && providerEntries.Count == 0) return null;

            var payload = new Dictionary<string, object> { ["v"] = PayloadVersion };
            if (kept.Count > 0) payload["t"] = kept;
            if (providerEntries.Count > 0) payload["p"] = providerEntries;

            try
            {
                return Encoding.UTF8.GetBytes(JsonSerializer.Serialize(payload, _options));
            }
            catch (Exception ex)
            {
                NarrativeLog.LogError($"[SaveSystem] 状态归档序列化失败: {ex.Message}");
                return null;
            }
        }

        /// <summary>捕获单个对象的状态（键为类型全名）。无状态时返回 null。</summary>
        public static byte[] CaptureSingle(System.Object target)
        {
            return CaptureSingle(GetKeyForObject(target), target);
        }

        /// <summary>
        /// 用显式归档键捕获单个对象状态。键需与读档侧的解析器一致（组件记录应使用位置化键）。
        /// </summary>
        public static byte[] CaptureSingle(string key, System.Object target)
        {
            if (target == null) return null;
            return Capture(new List<NarrativeStateTarget>
            {
                new NarrativeStateTarget(string.IsNullOrEmpty(key) ? GetKeyForObject(target) : key, target),
            });
        }

        /// <summary>
        /// 还原 Capture 产出的字节。
        /// </summary>
        /// <param name="data">归档字节（可为 null）。</param>
        /// <param name="targetResolver">按归档键查找运行期对象；返回 null 表示对象不存在。</param>
        public static void Apply(byte[] data, Func<string, System.Object> targetResolver)
        {
            Apply(data, targetResolver, null);
        }

        /// <summary>
        /// 还原 Capture 产出的字节，并限定 slot id 命中的对象必须落在合法作用域内。
        /// 注册表是进程全局的：确定性 id 让同名键在不同 Actor 间可能同形，
        /// idScopeValidator（例如“脚本归属该 Actor 或其子/父链”）把跨 Actor 的同型误绑挡掉，
        /// 挡不住时回退按键定位。校验器为 null 时维持旧行为（全局接受，仍受指纹闸门约束）。
        /// </summary>
        public static void Apply(byte[] data, Func<string, System.Object> targetResolver, Func<System.Object, bool> idScopeValidator)
        {
            if (data == null || data.Length == 0 || targetResolver == null) return;

            Dictionary<string, JsonElement> payload;
            try
            {
                payload = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(
                    Encoding.UTF8.GetString(data), _options);
            }
            catch (Exception ex)
            {
                NarrativeLog.LogError($"[SaveSystem] 状态归档解析失败: {ex.Message}");
                return;
            }

            if (payload == null) return;

            if (payload.TryGetValue("t", out var targetsEl) && targetsEl.ValueKind == JsonValueKind.Array)
            {
                foreach (var entryEl in targetsEl.EnumerateArray())
                {
                    if (entryEl.ValueKind != JsonValueKind.Object) continue;
                    if (!entryEl.TryGetProperty("k", out var keyEl)) continue;
                    string key = keyEl.GetString();
                    if (string.IsNullOrEmpty(key)) continue;

                    var obj = ResolveTarget(targetResolver, key, entryEl, idScopeValidator);
                    if (obj == null) continue;
                    if (obj is INarrativeSaveStateProvider) continue; // provider 由 providers 段处理

                    if (!entryEl.TryGetProperty("m", out var membersEl)) continue;
                    if (membersEl.ValueKind != JsonValueKind.Object) continue;

                    ApplyMembers(obj, membersEl);
                }
            }

            if (payload.TryGetValue("p", out var providersEl) && providersEl.ValueKind == JsonValueKind.Array)
            {
                foreach (var entryEl in providersEl.EnumerateArray())
                {
                    if (entryEl.ValueKind != JsonValueKind.Object) continue;
                    if (!entryEl.TryGetProperty("k", out var keyEl)) continue;
                    string key = keyEl.GetString();
                    if (string.IsNullOrEmpty(key)) continue;

                    var obj = ResolveTarget(targetResolver, key, entryEl, idScopeValidator);
                    if (obj is not INarrativeSaveStateProvider provider) continue;
                    if (!entryEl.TryGetProperty("b", out var bytesEl)) continue;
                    var b64 = bytesEl.GetString();
                    if (string.IsNullOrEmpty(b64)) continue;

                    try
                    {
                        provider.LoadSaveStateBytes(Convert.FromBase64String(b64));
                    }
                    catch (Exception ex)
                    {
                        NarrativeLog.LogWarning($"[SaveSystem] LoadSaveStateBytes 失败 {key}: {ex.Message}");
                    }
                }
            }
        }

        /// <summary>
        /// 还原时的目标定位（指纹闸门版）。id 是确定性键（=归档键同形），写读两侧同规则可重算，
        /// 因此全新进程里没有会话内绑定也无需“退回名字将就”：名字通路本身就是确定性定位。
        /// 会话内注册表命中 id 时以 id 为准（脚本中途重排仍找回原对象）；
        /// 记录带 "f" 指纹（新档）时必须与候选对象的载荷指纹一致才返回，不一致一律拒绝应用（返回 null），
        /// 调用方跳过该条，对象保留默认值 —— 宁可不还原，也绝不把字节静默写进错对象；
        /// 无 "f" 的老档维持旧通路（按键还原并补绑 id），下次保存补写 id 与指纹。
        /// <para>
        /// 「选哪个对象 / 是否拒绝」的裁决本身在 <see cref="DecideTargetResolution"/>（纯函数，无副作用），
        /// 这里只负责按裁决执行注册表补绑与告警；harness 驱动的是同一个裁决函数，
        /// 并且经由本函数被 <see cref="Apply(byte[], Func{string, System.Object}, Func{System.Object, bool})"/>
        /// 调用，所以“测试验的”与“读档跑的”是同一份判定。
        /// </para>
        /// </summary>
        private static System.Object ResolveTarget(
            Func<string, System.Object> targetResolver,
            string key,
            JsonElement entryEl,
            Func<System.Object, bool> idScopeValidator)
        {
            var byKey = targetResolver != null ? targetResolver(key) : null;
            string slotId = ReadEntryString(entryEl, "i");
            string fingerprint = ReadEntryString(entryEl, "f");

            var byId = string.IsNullOrEmpty(slotId) ? null : NarrativeComponentKeyRegistry.Resolve(slotId);
            if (byId != null && idScopeValidator != null && !idScopeValidator(byId)) byId = null;
            if (byId != null && byKey != null && !ReferenceEquals(byId, byKey))
            {
                NarrativeLog.LogWarning(
                    $"[SaveSystem] 归档键 {key} 按名字命中的对象与 slot id 绑定的对象不一致，按 id 还原（顺序变化已被阻止）");
            }

            var verdict = DecideTargetResolution(byId, byKey, key, slotId, fingerprint);

            switch (verdict.Verdict)
            {
                case ENarrativeTargetResolve.NotFound:
                    return null;

                case ENarrativeTargetResolve.RefusedFingerprintMismatch:
                    NarrativeLog.LogWarning(
                        $"[SaveSystem] 归档键 {key} 状态指纹不匹配（存档 {fingerprint}，当前 {verdict.CandidateFingerprint}），" +
                        "拒绝应用该条字节，保留对象默认值");
                    return null;

                case ENarrativeTargetResolve.ResolvedByKeyOverId:
                    NarrativeLog.LogWarning(
                        $"[SaveSystem] 归档键 {key}：slot id 命中的对象指纹不匹配，已改用名字定位的对象");
                    break;
            }

            if (!string.IsNullOrEmpty(verdict.BindSlotId) && verdict.Target != null)
            {
                NarrativeComponentKeyRegistry.Bind(verdict.Target, verdict.BindSlotId);
            }
            return verdict.Target;
        }

        /// <summary>
        /// 读档条目定位的纯裁决（无副作用、无引擎依赖）：给定「按 id 命中的对象」「按名字命中的对象」
        /// 与记录里的键/序号/载荷指纹，决定这条存档字节能不能应用、应用到哪个对象、以及是否要把 id 补绑到对象上。
        /// <para>
        /// 规则（与历史行为逐条对应）：
        /// 1) 没有任何候选 → <see cref="ENarrativeTargetResolve.NotFound"/>；
        /// 2) 记录带指纹：候选指纹一致 → 用候选（id 未命中时把 id 补绑到候选）；
        ///    候选（按 id 取得）不一致但按名字命中的对象一致 → <see cref="ENarrativeTargetResolve.ResolvedByKeyOverId"/>；
        ///    两者都不一致 → <see cref="ENarrativeTargetResolve.RefusedFingerprintMismatch"/>，绝不应用字节；
        /// 3) 记录没有指纹（老档）→ 必须容忍、不得拒绝：按名字命中且与键自洽时用名字结果（必要时补绑 id），
        ///    否则退回 id 结果。
        /// </para>
        /// </summary>
        public static NarrativeTargetResolveResult DecideTargetResolution(
            System.Object byId,
            System.Object byKey,
            string key,
            string slotId,
            string recordedFingerprint)
        {
            var candidate = byId ?? byKey;
            if (candidate == null)
            {
                return new NarrativeTargetResolveResult { Verdict = ENarrativeTargetResolve.NotFound };
            }

            bool bLegacy = string.IsNullOrEmpty(recordedFingerprint);

            if (!bLegacy)
            {
                string candidateFingerprint = ComputeFingerprintForObject(candidate);
                if (FingerprintMatches(recordedFingerprint, candidateFingerprint))
                {
                    return new NarrativeTargetResolveResult
                    {
                        Verdict = ENarrativeTargetResolve.ResolvedCandidate,
                        Candidate = candidate,
                        CandidateFingerprint = candidateFingerprint,
                        Target = candidate,
                        // 未经 id 命中才需要补绑；Bind 对空 id 是空操作，因此直接传 slotId 即可。
                        BindSlotId = byId == null ? slotId : null,
                    };
                }

                // id 命中了错对象（会话内注册表被序号挪位污染等）：名字结果指纹相符则改用名字
                if (byId != null && byKey != null && !ReferenceEquals(byId, byKey))
                {
                    string byKeyFingerprint = ComputeFingerprintForObject(byKey);
                    if (FingerprintMatches(recordedFingerprint, byKeyFingerprint))
                    {
                        return new NarrativeTargetResolveResult
                        {
                            Verdict = ENarrativeTargetResolve.ResolvedByKeyOverId,
                            Candidate = candidate,
                            CandidateFingerprint = candidateFingerprint,
                            Target = byKey,
                        };
                    }
                }

                return new NarrativeTargetResolveResult
                {
                    Verdict = ENarrativeTargetResolve.RefusedFingerprintMismatch,
                    Candidate = candidate,
                    CandidateFingerprint = candidateFingerprint,
                };
            }

            // 老档（无指纹）：维持旧行为 —— 命中即还原；仅在键与对象自洽时补绑 id，
            // 不再把 id 绑到已被证明不自洽的候选上（旧版 386-390 的缺陷）。
            if (byKey != null && (string.IsNullOrEmpty(slotId) || ObjectMatchesKey(byKey, key)))
            {
                return new NarrativeTargetResolveResult
                {
                    Verdict = ENarrativeTargetResolve.ResolvedCandidate,
                    Candidate = byKey,
                    Target = byKey,
                    bLegacyRecord = true,
                    BindSlotId = string.IsNullOrEmpty(slotId) ? null : slotId,
                };
            }

            var fallback = byId ?? byKey;
            return new NarrativeTargetResolveResult
            {
                Verdict = ENarrativeTargetResolve.ResolvedCandidate,
                Candidate = fallback,
                Target = fallback,
                bLegacyRecord = true,
            };
        }

        private static string ReadEntryString(JsonElement entryEl, string property)
        {
            if (entryEl.ValueKind != JsonValueKind.Object) return null;
            if (!entryEl.TryGetProperty(property, out var el)) return null;
            return el.ValueKind == JsonValueKind.String ? el.GetString() : null;
        }

        /// <summary>
        /// 载荷指纹的纯计算核：声明类型全名 + 成员数 + 有序成员名的 FNV-1a 散列。
        /// 不反射、不依赖引擎，输入是一张显式的成员名表 —— 因此「增/删/改名可归档成员会改变指纹」
        /// 这条规则可以被进程外 harness 直接断言，而 <see cref="ComputeFingerprintForObject"/>
        /// 只负责把反射得到的成员名表喂给它（同一份算法，不存在两份实现）。
        /// </summary>
        public static string ComputeFingerprint(string typeFullName, IList<string> memberNames)
        {
            int count = memberNames?.Count ?? 0;
            ulong hash = 14695981039346656037UL; // FNV-1a 64
            if (memberNames != null)
            {
                for (int i = 0; i < count; i++)
                {
                    var name = memberNames[i];
                    if (name == null) continue;
                    for (int c = 0; c < name.Length; c++)
                    {
                        unchecked
                        {
                            hash = (hash ^ name[c]) * 1099511628211UL;
                        }
                    }
                    unchecked
                    {
                        hash = (hash ^ '|') * 1099511628211UL;
                    }
                }
            }
            return typeFullName + "|M|" + count + "|" + hash.ToString("X16");
        }

        /// <summary>
        /// 计算对象的可归档载荷指纹：声明类型全名 + 成员数 + 有序成员名的 FNV-1a 散列。
        /// provider 类对象（自绘字节）没有反射成员可哈希，用类型全名 + "P" 标记。
        /// 同一类型在同一程序集内恒定；类型改名、增删可归档成员都会改变指纹 ——
        /// 正是这些变化会让“按位置/按键找错对象”变得致命，因此读档侧据此拒绝应用。
        /// </summary>
        public static string ComputeFingerprintForObject(System.Object target)
        {
            if (target == null) return string.Empty;
            var type = target.GetType();
            if (target is INarrativeSaveStateProvider)
            {
                return type.FullName + "|P|0|0";
            }

            var members = GetArchiveInfo(type).Members;
            var names = new string[members.Length];
            for (int i = 0; i < members.Length; i++) names[i] = members[i].Name;
            return ComputeFingerprint(type.FullName, names);
        }

        /// <summary>
        /// 校验候选对象与记录指纹是否一致。指纹为空（老档尚未补写）视为一致 —— 保持旧档可读；
        /// 但下次保存该条目会随 Capture 补写指纹与确定性 id。
        /// </summary>
        public static bool FingerprintMatches(System.Object target, string fingerprint)
        {
            return FingerprintMatches(fingerprint, ComputeFingerprintForObject(target));
        }

        /// <summary>
        /// 指纹比对的纯函数形式：记录里的指纹 vs 已经算出来的候选指纹。
        /// 空记录指纹（老档）一律视为一致（必须容忍、不得拒绝）；
        /// 候选指纹为空（对象为 null）在非空记录面前一律判否。
        /// </summary>
        public static bool FingerprintMatches(string recordedFingerprint, string computedCandidateFingerprint)
        {
            if (string.IsNullOrEmpty(recordedFingerprint)) return true;
            if (string.IsNullOrEmpty(computedCandidateFingerprint)) return false;
            return string.Equals(computedCandidateFingerprint, recordedFingerprint, StringComparison.Ordinal);
        }

        /// <summary>将归档字节还原到指定对象（按对象自身类型名匹配键）。</summary>
        public static void ApplySingle(byte[] data, System.Object target)
        {
            if (target == null) return;
            string key = GetKeyForObject(target);
            Apply(data, k => k == key ? target : null);
        }

        /// <summary>对象是否存在可归档状态（实现 INarrativeSaveStateProvider 的对象始终视为可归档）。</summary>
        public static bool HasArchivableState(System.Object target)
        {
            if (target == null) return false;
            if (target is INarrativeSaveStateProvider) return true;
            return GetArchiveInfo(target.GetType()).Members.Length > 0;
        }

        /// <summary>对象是否存在 [NarrativeSaveGame] 显式标记成员（用于组件的保守归档策略）。</summary>
        public static bool HasExplicitSaveMarkers(System.Object target)
        {
            if (target == null) return false;
            return GetArchiveInfo(target.GetType()).AttributeOnly;
        }

        /// <summary>
        /// 对象默认归档键：显式声明的键优先（<see cref="NarrativeSaveKeyAttribute"/> / SaveComponentKey 成员），
        /// 其次是类型全名。显式键让组件身份与 Actor.GetScripts() 的枚举顺序无关。
        /// </summary>
        public static string GetKeyForObject(System.Object target)
        {
            if (target == null) return string.Empty;
            string explicitKey = NarrativeComponentKeyRegistry.GetExplicitKey(target);
            if (!string.IsNullOrEmpty(explicitKey)) return explicitKey;
            return target.GetType().FullName ?? target.GetType().Name;
        }

        /// <summary>
        /// 判断对象是否能用给定归档键定位到（显式键 / 类型全名 / 去掉 #索引后的类型全名 / 短名 / "Actor:子名"）。
        /// 用于校验“按 slot id 找回的对象”与记录里的键是否仍然自洽，避免把字节写进错类型的对象。
        /// </summary>
        public static bool ObjectMatchesKey(System.Object target, string key)
        {
            if (target == null || string.IsNullOrEmpty(key)) return false;

            if (key.StartsWith("Actor:", StringComparison.Ordinal))
            {
                var childActor = target as Actor;
                if (childActor != null)
                {
                    return childActor.Name == key.Substring("Actor:".Length);
                }
                var childScript = target as Script;
                return childScript != null && childScript.Actor != null
                       && childScript.Actor.Name == key.Substring("Actor:".Length);
            }

            string typeName = StripIndexSuffix(key);

            if (string.Equals(GetKeyForObject(target), key, StringComparison.Ordinal)) return true;

            var type = target.GetType();
            if (string.Equals(type.FullName, typeName, StringComparison.Ordinal)) return true;
            if (string.Equals(type.Name, typeName, StringComparison.Ordinal)) return true;
            return false;
        }

        /// <summary>去掉键尾部的 "#索引" 位置化后缀（无后缀时原样返回）。</summary>
        private static string StripIndexSuffix(string key)
        {
            return NarrativeSaveComponentKeys.StripOrdinalSuffix(key);
        }

        internal sealed class MemberAccessor
        {
            public string Name;
            public Type MemberType;
            public bool Explicit;
            public Func<System.Object, System.Object> Get;
            public Action<System.Object, System.Object> Set;
        }

        private sealed class TypeArchiveInfo
        {
            public MemberAccessor[] Members = Array.Empty<MemberAccessor>();
            public bool AttributeOnly;
        }

        private static readonly Dictionary<Type, TypeArchiveInfo> _typeInfoCache = new Dictionary<Type, TypeArchiveInfo>();
        private static readonly Dictionary<Type, bool> _recordableCache = new Dictionary<Type, bool>();
        private static readonly object _cacheLock = new object();

        private static TypeArchiveInfo GetArchiveInfo(Type type)
        {
            if (type == null) return new TypeArchiveInfo();
            lock (_cacheLock)
            {
                if (_typeInfoCache.TryGetValue(type, out var cached)) return cached;
            }

            var info = BuildArchiveInfo(type);
            lock (_cacheLock)
            {
                _typeInfoCache[type] = info;
            }
            return info;
        }

        private static TypeArchiveInfo BuildArchiveInfo(Type type)
        {
            var result = new TypeArchiveInfo();
            if (type == null) return result;
            if (IsEngineDeclaredType(type)) return result;

            var all = new List<MemberAccessor>();
            var seen = new HashSet<string>(StringComparer.Ordinal);

            for (var t = type; t != null && t != typeof(System.Object); t = t.BaseType)
            {
                // 引擎基类（Script/Actor/Object）不带玩法状态，遇到即停止向上收集
                if (IsFlaxObjectType(t) || IsEngineDeclaredType(t)) break;

                foreach (var field in t.GetFields(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly))
                {
                    if (field.IsInitOnly || field.IsStatic) continue;
                    if (!seen.Add(field.Name)) continue;
                    if (HasExcludedAttribute(field)) continue;

                    bool explicitMark = field.IsDefined(typeof(NarrativeSaveGameAttribute), true);
                    if (!IsRecordableType(field.FieldType)) continue;

                    var f = field;
                    all.Add(new MemberAccessor
                    {
                        Name = f.Name,
                        MemberType = f.FieldType,
                        Explicit = explicitMark,
                        Get = instance => f.GetValue(instance),
                        Set = (instance, value) => f.SetValue(instance, value),
                    });
                }

                foreach (var prop in t.GetProperties(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly))
                {
                    if (!prop.CanRead || !prop.CanWrite) continue;
                    if (prop.GetIndexParameters().Length > 0) continue;
                    if (!seen.Add(prop.Name)) continue;
                    if (HasExcludedAttribute(prop)) continue;

                    var getter = prop.GetGetMethod(false);
                    var setter = prop.GetSetMethod(false);
                    if (getter == null || setter == null) continue;

                    bool explicitMark = prop.IsDefined(typeof(NarrativeSaveGameAttribute), true);
                    if (!IsRecordableType(prop.PropertyType)) continue;

                    var p = prop;
                    all.Add(new MemberAccessor
                    {
                        Name = p.Name,
                        MemberType = p.PropertyType,
                        Explicit = explicitMark,
                        Get = instance => p.GetValue(instance, null),
                        Set = (instance, value) => p.SetValue(instance, value, null),
                    });
                }
            }

            int explicitCount = all.Count(m => m.Explicit);
            if (explicitCount > 0)
            {
                result.Members = all.Where(m => m.Explicit).ToArray();
                result.AttributeOnly = true;
            }
            else
            {
                result.Members = all.ToArray();
                result.AttributeOnly = false;
            }

            return result;
        }

        private static bool HasExcludedAttribute(MemberInfo member)
        {
            if (member.IsDefined(typeof(NarrativeNoSaveAttribute), true)) return true;
            if (member.IsDefined(typeof(NonSerializedAttribute), true)) return true;
            if (member.IsDefined(typeof(ObsoleteAttribute), true)) return true;
            return false;
        }

        private static Dictionary<string, object> CaptureMembers(System.Object target)
        {
            var info = GetArchiveInfo(target.GetType());
            if (info.Members.Length == 0) return null;

            var dict = new Dictionary<string, object>();
            foreach (var member in info.Members)
            {
                try
                {
                    dict[member.Name] = member.Get(target);
                }
                catch
                {
                    // 取值异常（例如引擎对象已销毁）时跳过该成员
                }
            }

            return dict.Count == 0 ? null : dict;
        }

        private static void ApplyMembers(System.Object target, JsonElement membersEl)
        {
            var info = GetArchiveInfo(target.GetType());
            foreach (var member in info.Members)
            {
                if (!membersEl.TryGetProperty(member.Name, out var valueEl)) continue;

                System.Object decoded;
                try
                {
                    decoded = valueEl.Deserialize(member.MemberType, _options);
                }
                catch (Exception ex)
                {
                    NarrativeLog.LogWarning($"[SaveSystem] 成员还原失败 {target.GetType().Name}.{member.Name}: {ex.Message}");
                    continue;
                }

                if (decoded == null && member.MemberType.IsValueType && Nullable.GetUnderlyingType(member.MemberType) == null)
                {
                    continue;
                }

                try
                {
                    member.Set(target, decoded);
                }
                catch (Exception ex)
                {
                    NarrativeLog.LogWarning($"[SaveSystem] 成员写入失败 {target.GetType().Name}.{member.Name}: {ex.Message}");
                }
            }
        }

        // ===== 类型可持久化判定 =====

        /// <summary>
        /// 判断类型能否进入存档：值类型/基元/字符串/枚举/Guid/DateTime 与集合可直接编码；
        /// 引擎对象与委托由 NonPersistentTypeConverterFactory 统一编码为 null（可安全保留成员位置）；
        /// 系统/第三方程序集中的复杂类型保守拒绝。
        /// </summary>
        private static bool IsRecordableType(Type type, int depth = 0)
        {
            if (type == null) return false;
            if (depth > 6) return true; // 深图交给 IgnoreCycles / MaxDepth 处理

            lock (_cacheLock)
            {
                if (depth == 0 && _recordableCache.TryGetValue(type, out var cached)) return cached;
            }

            bool recordable = CheckRecordableType(type, depth);

            if (depth == 0)
            {
                lock (_cacheLock)
                {
                    _recordableCache[type] = recordable;
                }
            }
            return recordable;
        }

        private static bool CheckRecordableType(Type type, int depth)
        {
            if (type.IsPointer || type.IsByRef) return false;
            if (type == typeof(IntPtr) || type == typeof(UIntPtr)) return false;
            if (type.IsGenericTypeDefinition) return false;
            if (type == typeof(System.Object)) return false;

            if (type == typeof(string)) return true;
            if (type.IsPrimitive) return true;
            if (type.IsEnum) return true;

            // 引擎对象引用（Actor/Script/资产/GUI 控件）与委托不可持久化：整类成员直接跳过，
            // 否则读档时会把运行期引用写成 null，反而清掉活对象上的引用。
            if (IsFlaxObjectType(type)) return false;
            if (typeof(MulticastDelegate).IsAssignableFrom(type)) return false;

            var nullableUnder = Nullable.GetUnderlyingType(type);
            if (nullableUnder != null) return IsRecordableType(nullableUnder, depth + 1);
            if (type == typeof(decimal) || type == typeof(Guid) || type == typeof(DateTime)
                || type == typeof(DateTimeOffset) || type == typeof(TimeSpan)) return true;
            if (EngineValueTypes.ContainsKey(type)) return true;

            // 其余 FlaxEngine / FlaxEditor 命名空间下的类型（GUI 控件、资产、渲染相关结构等）
            // 无法跨会话还原，保守跳过
            if (IsEngineDeclaredType(type)) return false;

            if (type.IsArray)
            {
                if (type.GetArrayRank() != 1) return false;
                return IsRecordableType(type.GetElementType(), depth + 1);
            }

            if (type.IsInterface)
            {
                // 集合类接口允许（STJ 以具体集合还原），其它接口无法实例化
                if (type.IsGenericType)
                {
                    var def = type.GetGenericTypeDefinition();
                    if (def == typeof(List<>) || def == typeof(IList<>) || def == typeof(ICollection<>)
                        || def == typeof(IEnumerable<>) || def == typeof(IReadOnlyList<>) || def == typeof(IReadOnlyCollection<>)
                        || def == typeof(HashSet<>) || def == typeof(Queue<>) || def == typeof(Stack<>)
                        || def == typeof(IDictionary<,>) || def == typeof(IReadOnlyDictionary<,>))
                    {
                        return true;
                    }
                }
                return false;
            }

            if (type.IsGenericType)
            {
                var def = type.GetGenericTypeDefinition();
                if (def == typeof(List<>) || def == typeof(IList<>) || def == typeof(ICollection<>)
                    || def == typeof(IEnumerable<>) || def == typeof(IReadOnlyList<>) || def == typeof(IReadOnlyCollection<>)
                    || def == typeof(HashSet<>) || def == typeof(Queue<>) || def == typeof(Stack<>)
                    || def == typeof(LinkedList<>))
                {
                    return IsRecordableType(type.GetGenericArguments()[0], depth + 1);
                }
                if (def == typeof(Dictionary<,>) || def == typeof(IDictionary<,>) || def == typeof(IReadOnlyDictionary<,>)
                    || def == typeof(SortedDictionary<,>))
                {
                    var args = type.GetGenericArguments();
                    return IsRecordableType(args[0], depth + 1) && IsRecordableType(args[1], depth + 1);
                }
                if (def == typeof(KeyValuePair<,>))
                {
                    var args = type.GetGenericArguments();
                    return IsRecordableType(args[0], depth + 1) && IsRecordableType(args[1], depth + 1);
                }
                if (def == typeof(System.Threading.Tasks.Task<>) || def == typeof(System.Threading.CancellationTokenSource))
                {
                    return false;
                }
            }

            if (type == typeof(Type) || type == typeof(MemberInfo) || type == typeof(Assembly)
                || typeof(System.Threading.Thread).IsAssignableFrom(type)
                || typeof(Stream).IsAssignableFrom(type)
                || type == typeof(System.Threading.CancellationToken))
            {
                return false;
            }

            if (IsBlacklistedAssembly(type)) return false;
            if (type.IsAbstract && type.IsSealed) return false; // static 类型
            if (type.IsAbstract) return false;
            if (!type.IsValueType && type.GetConstructor(Type.EmptyTypes) == null) return false;

            // 递归检查成员：只要成员本身可编码即可（引擎对象成员会被写成 null）
            foreach (var member in GetMembersForValidation(type))
            {
                if (!IsRecordableType(member, depth + 1)) return false;
            }
            return true;
        }

        private static IEnumerable<Type> GetMembersForValidation(Type type)
        {
            foreach (var field in type.GetFields(BindingFlags.Public | BindingFlags.Instance))
            {
                if (field.IsStatic) continue;
                yield return field.FieldType;
            }
            foreach (var prop in type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
            {
                if (!prop.CanRead || !prop.CanWrite) continue;
                if (prop.GetIndexParameters().Length > 0) continue;
                yield return prop.PropertyType;
            }
        }

        private static bool IsBlacklistedAssembly(Type type)
        {
            var asm = type.Assembly;
            if (asm == null) return true;
            string name = asm.GetName().Name ?? string.Empty;
            if (name.StartsWith("System", StringComparison.Ordinal)) return true;
            if (name.StartsWith("Microsoft", StringComparison.Ordinal)) return true;
            if (name.StartsWith("Mono", StringComparison.Ordinal)) return true;
            if (name.Equals("mscorlib", StringComparison.Ordinal)) return true;
            if (name.Equals("netstandard", StringComparison.Ordinal)) return true;
            if (name.Equals("FlaxEngine.CSharp", StringComparison.Ordinal)) return true;
            if (name.Equals("FlaxEditor.CSharp", StringComparison.Ordinal)) return true;
            if (name.Equals("Newtonsoft.Json", StringComparison.Ordinal)) return true;
            return false;
        }

        private static bool IsEngineDeclaredType(Type type)
        {
            if (type == null) return false;
            var ns = type.Namespace;
            return ns != null && (ns.StartsWith("FlaxEngine", StringComparison.Ordinal) || ns.StartsWith("FlaxEditor", StringComparison.Ordinal));
        }

        internal static bool IsFlaxObjectType(Type type)
        {
            if (type == null) return false;
            for (var t = type; t != null; t = t.BaseType)
            {
                if (t.FullName == "FlaxEngine.Object") return true;
                if (t.FullName == "System.Object") break;
            }
            return false;
        }

        // ===== 目标键解析（把归档键映射回运行期对象） =====

        /// <summary>
        /// 构造 Actor 作用域的键解析器：支持类型全名（含 #索引消歧）、"Actor:&lt;name&gt;" 子 Actor 键。
        /// </summary>
        public static Func<string, System.Object> MakeActorTargetResolver(Actor actor)
        {
            return key => ResolveKeyInActor(actor, key);
        }

        /// <summary>
        /// 对象是否属于该 Actor 的作用域：Actor 本身、其上的脚本、其（递归）子 Actor 及其脚本，
        /// 以及 provider 聚合语义允许的父链上的对象。用于限制进程全局 id 注册表的命中范围：
        /// 确定性 id 在不同 Actor 的同型脚本间可能同形，跨 Actor 的误绑必须挡掉。
        /// </summary>
        public static bool IsInActorScopeOf(System.Object obj, Actor actor)
        {
            if (obj == null || actor == null) return false;
            if (ReferenceEquals(obj, actor)) return true;

            Actor owner = null;
            if (obj is Script script) owner = script.Actor;
            else if (obj is Actor childActor) owner = childActor;
            if (owner == null) return false;

            // 沿父链向上：命中 actor 本身，或命中 actor 的任一子级（childActor 型对象），均视为同作用域
            for (var a = owner; a != null; a = a.Parent)
            {
                if (ReferenceEquals(a, actor)) return true;
            }
            for (var a = actor; a != null; a = a.Parent)
            {
                if (ReferenceEquals(a, owner)) return true;
            }
            return false;
        }

        private static System.Object ResolveKeyInActor(Actor actor, string key)
        {
            if (actor == null || string.IsNullOrEmpty(key)) return null;

            if (key.StartsWith("Actor:", StringComparison.Ordinal))
            {
                string childName = key.Substring("Actor:".Length);
                foreach (var child in actor.GetChildren<Actor>())
                {
                    if (child != null && child.Name == childName) return child;
                }
                return null;
            }

            if (string.Equals(key, actor.GetType().FullName, StringComparison.Ordinal)) return actor;

            NarrativeSaveComponentKeys.SplitKey(key, out string typeName, out int index);

            var scripts = actor.GetScripts<Script>();
            if (scripts == null) return null;

            // 读档侧的「按键选中」全部走 NarrativeSaveComponentKeys 的纯函数，
            // 与写侧（AddStateTargets / CollectSavableComponents / CaptureActorState）的
            // NextKey 计数规则共用同一份实现；harness 直接驱动这些纯函数。
            var ordered = new List<Script>(scripts);
            var fullNames = new string[ordered.Count];
            var shortNames = new string[ordered.Count];
            var explicitKeys = new string[ordered.Count];
            for (int i = 0; i < ordered.Count; i++)
            {
                var script = ordered[i];
                if (script == null) continue;
                var t = script.GetType();
                fullNames[i] = t.FullName;
                shortNames[i] = t.Name;
                explicitKeys[i] = NarrativeComponentKeyRegistry.GetExplicitKey(script);
            }

            // 第一优先：显式声明的稳定键（[NarrativeSaveKey] / SaveComponentKey）。
            // 这类键与 Actor.GetScripts() 的枚举顺序无关，脚本增删/重排后仍指向同一组件。
            int explicitHit = NarrativeSaveComponentKeys.IndexOfExplicitKey(explicitKeys, typeName);
            if (explicitHit >= 0) return ordered[explicitHit];

            int ordinalHit = NarrativeSaveComponentKeys.IndexOfOrdinal(fullNames, typeName, index);
            if (ordinalHit >= 0) return ordered[ordinalHit];

            // 回退：短名匹配（兼容手工编辑的记录）
            int shortHit = NarrativeSaveComponentKeys.IndexOfKey(shortNames, typeName);
            if (shortHit >= 0) return ordered[shortHit];

            // 回退：子 Actor 上的同名脚本
            foreach (var child in actor.GetChildren<Actor>())
            {
                if (child == null) continue;
                var childScripts = child.GetScripts<Script>();
                if (childScripts == null) continue;
                foreach (var script in childScripts)
                {
                    if (script != null && string.Equals(script.GetType().FullName, typeName, StringComparison.Ordinal)) return script;
                }
            }

            return null;
        }

        // ===== JSON 转换器 =====

        /// <summary>引擎值类型（Vector3/Quaternion/Transform 等）按公共可写字段显式编码。</summary>
        private sealed class FlaxValueStructConverterFactory : System.Text.Json.Serialization.JsonConverterFactory
        {
            public override bool CanConvert(Type typeToConvert)
            {
                return typeToConvert != null && EngineValueTypes.ContainsKey(typeToConvert);
            }

            public override System.Text.Json.Serialization.JsonConverter CreateConverter(Type typeToConvert, JsonSerializerOptions options)
            {
                var converterType = typeof(FlaxValueStructConverter<>).MakeGenericType(typeToConvert);
                return (System.Text.Json.Serialization.JsonConverter)Activator.CreateInstance(converterType);
            }
        }

        private sealed class FlaxValueStructConverter<T> : System.Text.Json.Serialization.JsonConverter<T>
        {
            private static FieldInfo[] _fields;

            private static FieldInfo[] Fields => _fields ??= GetFields();

            private static FieldInfo[] GetFields()
            {
                if (EngineValueTypes.TryGetValue(typeof(T), out var cached)) return cached;
                return typeof(T).GetFields(BindingFlags.Public | BindingFlags.Instance).Where(f => !f.IsInitOnly).ToArray();
            }

            public override T Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
            {
                if (reader.TokenType == JsonTokenType.Null) return default;
                if (reader.TokenType != JsonTokenType.StartObject)
                {
                    reader.Skip();
                    return default;
                }

                object boxed = Activator.CreateInstance(typeToConvert);
                var fields = Fields;

                while (reader.Read())
                {
                    if (reader.TokenType == JsonTokenType.EndObject) break;
                    if (reader.TokenType != JsonTokenType.PropertyName)
                    {
                        reader.Skip();
                        continue;
                    }

                    string name = reader.GetString();
                    reader.Read();

                    FieldInfo field = null;
                    for (int i = 0; i < fields.Length; i++)
                    {
                        if (fields[i].Name == name)
                        {
                            field = fields[i];
                            break;
                        }
                    }

                    if (field == null)
                    {
                        reader.Skip();
                        continue;
                    }

                    try
                    {
                        var value = JsonSerializer.Deserialize(ref reader, field.FieldType, options);
                        if (value != null) field.SetValue(boxed, value);
                    }
                    catch
                    {
                        reader.Skip();
                    }
                }

                return (T)boxed;
            }

            public override void Write(Utf8JsonWriter writer, T value, JsonSerializerOptions options)
            {
                writer.WriteStartObject();
                var fields = Fields;
                object boxed = value;
                foreach (var field in fields)
                {
                    System.Object fv;
                    try
                    {
                        fv = field.GetValue(boxed);
                    }
                    catch
                    {
                        continue;
                    }
                    writer.WritePropertyName(field.Name);
                    JsonSerializer.Serialize(writer, fv, field.FieldType, options);
                }
                writer.WriteEndObject();
            }
        }

        /// <summary>不可持久化类型（引擎对象引用 / 委托 / IntPtr 等）统一编码为 null。</summary>
        private sealed class NonPersistentTypeConverterFactory : System.Text.Json.Serialization.JsonConverterFactory
        {
            public override bool CanConvert(Type typeToConvert)
            {
                if (typeToConvert == null) return false;
                if (EngineValueTypes.ContainsKey(typeToConvert)) return false;
                if (IsFlaxObjectType(typeToConvert)) return true;
                if (typeof(MulticastDelegate).IsAssignableFrom(typeToConvert)) return true;
                if (typeToConvert == typeof(IntPtr) || typeToConvert == typeof(UIntPtr)) return true;
                return false;
            }

            public override System.Text.Json.Serialization.JsonConverter CreateConverter(Type typeToConvert, JsonSerializerOptions options)
            {
                var converterType = typeof(NonPersistentTypeConverter<>).MakeGenericType(typeToConvert);
                return (System.Text.Json.Serialization.JsonConverter)Activator.CreateInstance(converterType);
            }
        }

        private sealed class NonPersistentTypeConverter<T> : System.Text.Json.Serialization.JsonConverter<T>
        {
            public override T Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
            {
                if (reader.TokenType != JsonTokenType.Null)
                {
                    reader.Skip();
                }
                return default;
            }

            public override void Write(Utf8JsonWriter writer, T value, JsonSerializerOptions options)
            {
                writer.WriteNullValue();
            }
        }
    }

    /// <summary>
    /// 具体可用的状态提供者组件：挂载到任意 Actor 上即可让其（及兄弟脚本）的状态进入存档，
    /// 无需修改其它脚本源码。对应 UE5 中 Actor 自动归档 UPROPERTY(SaveGame) 的行为。
    /// </summary>
    public class NarrativeSaveStateProviderComponent : Script, INarrativeSaveStateProvider, INarrativeSavableComponent
    {
        /// <summary>状态归属 Actor。为空时取本组件所在 Actor 的父 Actor，再退化为自身 Actor。</summary>
        [NarrativeNoSave]
        public Actor StateOwner;

        /// <summary>是否同时归档归属 Actor 自身类型上的可存档成员。</summary>
        public bool bIncludeOwnerActor = true;

        /// <summary>是否同时归档所有兄弟脚本（默认为真，等价 UE5 归档 Actor 上的组件属性）。</summary>
        public bool bIncludeSiblingScripts = true;

        private Actor ResolveOwner()
        {
            if (StateOwner != null) return StateOwner;
            var self = Actor;
            if (self == null) return null;
            return self.Parent ?? self;
        }

        private List<NarrativeStateTarget> CollectTargets()
        {
            var targets = new List<NarrativeStateTarget>();
            var owner = ResolveOwner();
            if (owner == null) return targets;

            NarrativeSavableActorResolver.AddStateTargets(owner, targets, bIncludeOwnerActor, this);
            return targets;
        }

        /// <summary>获取当前状态字节（JSON）。</summary>
        public byte[] GetSaveStateBytes()
        {
            return NarrativeSaveStateArchive.Capture(CollectTargets());
        }

        /// <summary>状态字节还原（provider 自绘载荷）：作用域限定为归属 Actor，防止全局 id 表跨 Actor 误绑。</summary>
        public void LoadSaveStateBytes(byte[] data)
        {
            var owner = ResolveOwner();
            if (owner == null) return;
            NarrativeSaveStateArchive.Apply(
                data,
                NarrativeSaveStateArchive.MakeActorTargetResolver(owner),
                obj => NarrativeSaveStateArchive.IsInActorScopeOf(obj, owner));
        }

        /// <summary>组件即将保存（默认无需预处理）。</summary>
        public void PrepareForSave()
        {
        }

        /// <summary>组件已从存档加载。</summary>
        public void Load()
        {
        }
    }
}
