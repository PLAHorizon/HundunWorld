// NarrativePro —— 武器 FX 数据资产 + 解析注册表
//
// UE5 依据（只读参考工程 C:\Works\GitHubProjects\20260801\HundunWorld\Plugins\NarrativePro\Source\NarrativeArsenal）：
//   - Public/Weapons/NarrativeWeaponFX.h:11-21        FInstancedWeaponFX（UPROPERTY(Instanced) TObjectPtr<UNarrativeWeaponFX> WeaponFX）
//   - Public/Weapons/NarrativeWeaponFX.h:23-28       类头注释：standalone object 解耦 FX 与宿主类；
//                                                    "Currently we roll the actual implementation in BP,
//                                                    but have it defined in C++ for flexibility."
//   - Public/Weapons/NarrativeWeaponFX.h:29-30       UCLASS(Abstract, Blueprintable, EditInlineNew) 依赖 Instanced UObject 机制
//   - Public/Weapons/NarrativeWeaponFX.h:37-59       GetWorld() override 通过 Outer 链取 UWorld，为 FX 提供世界上下文
//
// 端口差异（刻意保留，不是修 UE5 的怪癖）：
//   1) Flax 无 Instanced UObject / EditInlineNew / Blueprintable —— 一个 FX 定义资产无法在运行时被宿主"实例化"，
//      因此这里改用仓库既有的"JSON 数据资产 + 按 ID 反查的注册表"模式（见
//      GAS/NarrativeGASAssetRegistry.cs、Audio/FoleyAudioBankLoader.cs、Items/ItemFactory.cs）。
//      对应的 FInstancedWeaponFX 端口只留"引用槽"（fxId + 每个宿主可覆写的 scale / offset），
//      行为由注册表按 fxId 解析。武器侧的挂载点由 WeaponItem.ItemClassId 反查 fxId（见
//      NarrativeWeaponFXRegistry.ResolveForWeaponItemClassId）—— 现有 item JSON 里已经有 item_sword / item_bow，
//      本注册表不改动 item 数据结构，只让 FX JSON 声明它覆盖哪些 itemId。
//   2) UE5 用 BP 子类自定义"哪个事件在哪个 socket 上播什么"，C++ 层不枚举 slot ——
//      端口必须把 slot 显式化，否则 JSON 无法描述。EWeaponFXSlot 就是这份枚举化后的最小通用集。
//   3) UE5 GetWorld() 通过 Outer 链反查 —— Flax 中等价物是 `Actor.Scene` / 显式传入宿主 Actor，
//      故本端口不需要 Outer 链；播放层的 spawn 点由调用方提供父 Actor 或直接给世界变换。

using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using FlaxEngine;
using NarrativePro.Core;
using NarrativePro.Data;
using NarrativePro.Items;

namespace NarrativePro.Weapons
{
    /// <summary>
    /// 武器 FX 槽位 —— 对应 UE5 BP 子类里自定义的"事件"命名。
    /// UE5 C++ 层只声明了抽象基类（见文件头 UE5 依据 :23-30），具体事件在 BP 里定；
    /// 端口没有 BP，因此把 BP 里最常见的事件枚举出来，JSON 才能描述。
    /// </summary>
    public enum EWeaponFXSlot
    {
        /// <summary>近战攻击窗口开启的一次性挥击/whoosh（对应 UE5 AnimNotifyState_AttackAnim 的 Begin）。</summary>
        Attack = 0,
        /// <summary>攻击窗口存续期间持续挂接的拖尾（Begin 时激活，End/Cleanup 时销毁 —— 对应 UE5 Trail/Ribbon 生命周期）。</summary>
        Trail = 1,
        /// <summary>远程开火的枪口闪光。</summary>
        MuzzleFlash = 2,
        /// <summary>命中目标（近战扫描或远程追踪命中）。</summary>
        Impact = 3,
        /// <summary>枪械抛壳。</summary>
        ShellEject = 4,
        /// <summary>弓的拉弦阶段（charge）。</summary>
        Draw = 5,
        /// <summary>弓的放弦（release，投射物生成时刻）。</summary>
        Release = 6,
        /// <summary>换弹。</summary>
        Reload = 7,
    }

    /// <summary>
    /// FX 挂载 socket/骨骼描述 —— 对应 UE5 BP 子类里 "GetSocketTransform(SocketName)" 那一层。
    /// 端口中 socket 解析走 WeaponVisual.TryGetBonePose（Flax 的骨骼节点即 socket 的等价物）。
    /// </summary>
    [Serializable]
    public class NarrativeWeaponFXSocket
    {
        /// <summary>目标骨骼/Socket 名（空 = 直接使用宿主 Actor 世界变换）。</summary>
        public string SocketName = "";

        /// <summary>相对 socket 的位置偏移（厘米，socket 本地空间）。</summary>
        public Vector3 RelativeLocation = Vector3.Zero;

        /// <summary>相对 socket 的欧拉角偏移（度，与 Flax Quaternion.Euler 口径一致）。</summary>
        public Vector3 RelativeRotationEulerDegrees = Vector3.Zero;

        /// <summary>FX 缩放（对应 UE5 Niagara 的 scaler 或 BP 里的 SetWorldScale3D）。</summary>
        public Vector3 Scale = Vector3.One;
    }

    /// <summary>
    /// 单条 FX 槽位配置。UE5 的一条 BP 事件对应这里的一条记录。
    /// 资源以 Flax 资产引用路径存放（Particles/*.flx 的 ParticleSystem 与 AudioClip），
    /// 装载时经 <see cref="Content.Load{T}"/> 拿真实资产对象，取不到只告警、不抛（与 Audio/FoleyAudioBankLoader 一致）。
    /// </summary>
    [Serializable]
    public class NarrativeWeaponFXSlotEntry
    {
        public EWeaponFXSlot Slot = EWeaponFXSlot.Attack;

        /// <summary>粒子资产路径（空 = 该 slot 无粒子）。</summary>
        public string ParticleSystemPath = "";

        /// <summary>音频资产路径（空 = 该 slot 无音）。</summary>
        public string AudioClipPath = "";

        /// <summary>挂载 socket/骨骼。</summary>
        public NarrativeWeaponFXSocket Socket = new NarrativeWeaponFXSocket();

        /// <summary>一次性 FX 的自动回收时长（秒）；&lt;=0 走 Playback 默认值。持久型 slot 忽略。</summary>
        public float AutoDestroySeconds = 1.5f;

        /// <summary>是否循环 —— 描述持久型 slot（如 Trail）。开启时 Playback 走激活/反激活生命周期。</summary>
        public bool bLoopWhileActive = false;

        /// <summary>音量。</summary>
        public float Volume = 1f;

        /// <summary>音高。</summary>
        public float Pitch = 1f;

        // 解析后的资产引用（不进 JSON 序列化，装载阶段填充）
        [NonSerialized] public ParticleSystem ResolvedParticle;
        [NonSerialized] public AudioClip ResolvedAudio;
        [NonSerialized] public bool bAssetsResolved;
    }

    /// <summary>
    /// 一份武器 FX 定义（对应 UE5 <c>UNarrativeWeaponFX</c> BP 子类的数据侧）。
    /// </summary>
    [Serializable]
    public class NarrativeWeaponFX
    {
        /// <summary>FX 定义 ID（JSON 里的 fxId，与文件名对齐）。</summary>
        public string FxId = "";

        /// <summary>显示名（调试用）。</summary>
        public string DisplayName = "";

        /// <summary>本 FX 集覆盖的武器 itemClassId 列表（注册表按此反查）。</summary>
        public List<string> WeaponItemClassIds = new List<string>();

        /// <summary>各事件的 slot 配置。</summary>
        public List<NarrativeWeaponFXSlotEntry> Slots = new List<NarrativeWeaponFXSlotEntry>();

        /// <summary>按 slot 取条目；不存在返回 null。</summary>
        public NarrativeWeaponFXSlotEntry GetSlot(EWeaponFXSlot slot)
        {
            if (Slots == null) return null;
            for (int i = 0; i < Slots.Count; i++)
            {
                if (Slots[i] != null && Slots[i].Slot == slot) return Slots[i];
            }
            return null;
        }
    }

    /// <summary>
    /// 每宿主一份的 FX 引用 —— 对应 UE5 <c>FInstancedWeaponFX</c>（NarrativeWeaponFX.h:11-21）。
    /// UE5 用 Instanced 让同一份 UNarrativeWeaponFX 类在不同宿主上有独立状态；
    /// Flax 无 Instanced，端口把它降为"引用 + 每宿主覆盖 scale/offset"的轻量 struct。
    /// </summary>
    [Serializable]
    public class InstancedWeaponFX
    {
        /// <summary>所引用的 FX 资产 ID（空表示未配置）。</summary>
        public string FxId = "";

        /// <summary>宿主级整体缩放覆盖（乘算 asset 里 socket 的 Scale）。</summary>
        public Vector3 InstanceScale = Vector3.One;

        /// <summary>宿主级整体位移覆盖（叠加到 socket 解析后的位置）。</summary>
        public Vector3 InstanceOffset = Vector3.Zero;

        /// <summary>是否配置了有效引用。</summary>
        public bool IsValid => !string.IsNullOrEmpty(FxId);

        /// <summary>解析引用到实际 FX 定义；空/未注册返回 null。</summary>
        public NarrativeWeaponFX Resolve() => NarrativeWeaponFXRegistry.Resolve(FxId);
    }

    /// <summary>
    /// 武器 FX 注册表。加载 <c>Content/NarrativePro/Weapons/*.json</c>，
    /// 建 fxId→定义 与 weaponItemClassId→fxId 两张表；未命中缓存负例避免每次磁盘探测。
    /// </summary>
    public static class NarrativeWeaponFXRegistry
    {
        /// <summary>FX JSON 定义目录（工程相对，经 <see cref="NarrativeDataPaths"/> 归一）。</summary>
        public static string FXDirectory = "Content/NarrativePro/Weapons";

        private static readonly Dictionary<string, NarrativeWeaponFX> _byId =
            new Dictionary<string, NarrativeWeaponFX>(StringComparer.Ordinal);
        private static readonly Dictionary<string, string> _byItemId =
            new Dictionary<string, string>(StringComparer.Ordinal);
        private static readonly JsonSerializerOptions _jsonOptions = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            ReadCommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = true,
            IncludeFields = true,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        };
        private static bool _bScanned;

        /// <summary>注册一份 FX 定义；重复 ID 覆盖。同时把 WeaponItemClassIds 里的项建反查。</summary>
        public static void Register(NarrativeWeaponFX fx)
        {
            if (fx == null || string.IsNullOrEmpty(fx.FxId)) return;
            string key = NormalizeKey(fx.FxId);
            _byId[key] = fx;
            if (fx.WeaponItemClassIds != null)
            {
                foreach (var itemId in fx.WeaponItemClassIds)
                {
                    if (string.IsNullOrEmpty(itemId)) continue;
                    _byItemId[NormalizeKey(itemId)] = key;
                }
            }
        }

        /// <summary>按 fxId 解析。首次调用会懒扫目录。</summary>
        public static NarrativeWeaponFX Resolve(string fxId)
        {
            EnsureScanned();
            if (string.IsNullOrEmpty(fxId)) return null;
            _byId.TryGetValue(NormalizeKey(fxId), out var fx);
            return fx;
        }

        /// <summary>
        /// 按武器物品反查 FX。约定：fxId 与 itemClassId 同名（如 <c>weaponfx_&#42;</c> 里
        /// weaponItemClassIds 声明）优先；未命中时回退查 <c>itemClassId</c> 同名 fxId，
        /// 便于纯命名约定而不显式声明映射的资产也能被解析。
        /// </summary>
        public static NarrativeWeaponFX ResolveForWeaponItemClassId(string itemClassId)
        {
            EnsureScanned();
            if (string.IsNullOrEmpty(itemClassId)) return null;
            string key = NormalizeKey(itemClassId);
            if (_byItemId.TryGetValue(key, out var fxKey) && _byId.TryGetValue(fxKey, out var fx))
                return fx;
            _byId.TryGetValue(key, out fx);
            return fx;
        }

        /// <summary>武器物品直接版：从 WeaponItem 的 ItemClassId 反查 FX。</summary>
        public static NarrativeWeaponFX ResolveForWeapon(WeaponItem weapon)
        {
            if (weapon == null) return null;
            return ResolveForWeaponItemClassId(weapon.ItemClassId);
        }

        /// <summary>清缓存（热重载/测试之间用）。</summary>
        public static void ClearCaches()
        {
            _byId.Clear();
            _byItemId.Clear();
            _bScanned = false;
        }

        private static void EnsureScanned()
        {
            if (_bScanned) return;
            _bScanned = true;
            ScanDirectory();
        }

        private static void ScanDirectory()
        {
            string dir = NarrativeDataPaths.Resolve(FXDirectory);
            if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir))
            {
                // 目录尚未落地属于配置状态，不是错误：ItemFactory / GAS registry 同样在缺失时静默
                return;
            }

            string[] files;
            try { files = Directory.GetFiles(dir, "*.json", SearchOption.AllDirectories); }
            catch (Exception ex)
            {
                NarrativeLog.LogWarning($"[WeaponFX] 扫描 FX 目录失败 '{dir}': {ex.Message}");
                return;
            }

            foreach (var path in files)
            {
                try
                {
                    string json = File.ReadAllText(path);
                    var dto = JsonSerializer.Deserialize<WeaponFXAssetJson>(json, _jsonOptions);
                    var fx = dto?.ToRuntime();
                    if (fx != null) Register(fx);
                }
                catch (Exception ex)
                {
                    NarrativeLog.LogWarning($"[WeaponFX] 解析 '{path}' 失败: {ex.Message}");
                }
            }
        }

        private static string NormalizeKey(string raw)
        {
            if (string.IsNullOrEmpty(raw)) return "";
            string key = raw.Trim().Replace('\\', '/');
            int slash = key.LastIndexOf('/');
            if (slash >= 0 && slash < key.Length - 1) key = key.Substring(slash + 1);
            if (key.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
                key = key.Substring(0, key.Length - 5);
            return key;
        }

        /// <summary>JSON 传输模型 —— 与运行时解耦，避免 System.Text.Json 反序列化 Flax Asset 造出空壳资产。</summary>
        [Serializable]
        public class WeaponFXAssetJson
        {
            public string fxId;
            public string displayName;
            public List<string> weaponItemClassIds = new List<string>();
            public List<SlotJson> slots = new List<SlotJson>();

            public NarrativeWeaponFX ToRuntime()
            {
                if (string.IsNullOrEmpty(fxId)) return null;
                var fx = new NarrativeWeaponFX
                {
                    FxId = fxId,
                    DisplayName = displayName ?? "",
                    WeaponItemClassIds = weaponItemClassIds ?? new List<string>(),
                };
                if (slots != null)
                {
                    foreach (var s in slots)
                    {
                        if (s == null) continue;
                        fx.Slots.Add(new NarrativeWeaponFXSlotEntry
                        {
                            Slot = ParseSlot(s.slot),
                            ParticleSystemPath = s.particleSystemPath ?? "",
                            AudioClipPath = s.audioClipPath ?? "",
                            AutoDestroySeconds = s.autoDestroySeconds,
                            bLoopWhileActive = s.loopWhileActive,
                            Volume = s.volume,
                            Pitch = s.pitch,
                            Socket = new NarrativeWeaponFXSocket
                            {
                                SocketName = s.socketName ?? "",
                                RelativeLocation = ReadVec3(s.relativeLocation, Vector3.Zero),
                                RelativeRotationEulerDegrees = ReadVec3(s.relativeRotationEulerDegrees, Vector3.Zero),
                                Scale = ReadVec3(s.scale, Vector3.One),
                            },
                        });
                    }
                }
                return fx;
            }

            private static EWeaponFXSlot ParseSlot(string raw)
            {
                if (string.IsNullOrEmpty(raw)) return EWeaponFXSlot.Attack;
                return Enum.TryParse<EWeaponFXSlot>(raw, true, out var v) ? v : EWeaponFXSlot.Attack;
            }

            private static Vector3 ReadVec3(float[] arr, Vector3 fallback)
            {
                if (arr == null || arr.Length < 3) return fallback;
                return new Vector3(arr[0], arr[1], arr[2]);
            }
        }

        /// <summary>单条 slot 的磁盘视图（数值数组走 float[] 以规避 Flax Vector3 的 JSON 反序列化歧义）。</summary>
        [Serializable]
        public class SlotJson
        {
            public string slot;
            public string particleSystemPath;
            public string audioClipPath;
            public string socketName;
            public float[] relativeLocation;
            public float[] relativeRotationEulerDegrees;
            public float[] scale;
            public float autoDestroySeconds = 1.5f;
            public bool loopWhileActive;
            public float volume = 1f;
            public float pitch = 1f;
        }
    }
}
