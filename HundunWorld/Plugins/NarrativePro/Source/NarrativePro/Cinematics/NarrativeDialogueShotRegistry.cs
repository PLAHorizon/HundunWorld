// NarrativePro —— 对话镜头（Dialogue Shot）JSON 数据资产 + 按名解析注册表
//
// UE5 依据（只读参考工程 C:\Works\GitHubProjects\20260801\HundunWorld\Plugins\NarrativePro）：
//   - Content/Pro/Core/Tales/Shots/DS_*.uasset + Shot_*.uasset：一个对话镜头 =
//     UNarrativeDialogueSequence 的蓝图实例化对象。对 DS_CloseUp / DS_OverTheShoulder /
//     DS_Medium_TwoShot 三个 uasset 提取属性名核实携带：FriendlyShotName、SequenceAssets、
//     AnchorOriginRule / AnchorRotationRule、bUse180DegreeRule / UnitsY180DegreeRule /
//     DegreesYaw180DegreeRule、LookAtTrackingSettings(AvatarToTrack)、PlaybackSettings。
//   - 对话侧通过 Instanced 对象引用镜头（FDialogueLine::Shot、FSpeakerInfo::SelectingReplyShot）。
//
// 端口差异（刻意替换，不是未完成项）：
//   1) Flax 无蓝图 / Instanced UObject / UClass 软引用，"按对象引用镜头"无从谈起。
//      改用仓库既有"JSON 数据资产 + 按名反查注册表"模式（见 GAS/NarrativeGASAssetRegistry.cs、
//      Weapons/NarrativeWeaponFXRegistry、Items/ItemFactory.cs）：镜头 JSON 放
//      Content/NarrativePro/Shots/，代码用 shotName（含别名，大小写不敏感）解析。
//   2) UE5 的取景本身住在镜头绑定的 Level Sequence 相机轨里（Shot_*.uasset 的
//      MovieScene3DTransformTrack / CameraCutTrack，FOV 与 PlateCropSettings 在 CineCamera 上）；
//      Flax 1.12 无 Level Sequence 脚本 API（已在 FlaxEngine.CSharp.xml 核实），因此本 DTO
//      只暴露端口 NarrativeDialogueSequence 真正消费的参数：180 度规则（Apply180DegreeRule /
//      GetShotAnchorTransform）、锚点解析（ResolveAnchorActor）、电影呈现（ApplyPresentation：
//      黑边 / 隐藏 HUD / 门禁 / 震动，遮幅经 PlateCropSettings 换算）、时长（GetSequenceDurationSeconds）。
//      SequenceAssetPaths / LookAtActor / FocusActor / AnchorOriginNudge / AnchorRotationRule
//      在端口当前没有消费方，不进 JSON，避免造出"写了也不生效"的假旋钮。
//   3) Resolve 每次返回全新序列实例：端口 NarrativeDialogueSequence 自带运行期状态
//      （_player、呈现标志位），跨对话共享会互相踩；定义模板只读缓存。
//      未命中名字只告警一次并返回 null，调用方回退默认镜头（与接线前行为一致）。

using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using NarrativePro.Core;
using NarrativePro.Data;
using NarrativePro.Tales;

namespace NarrativePro.Cinematics
{
    /// <summary>
    /// 对话镜头注册表：扫描 <see cref="ShotDirectory"/> 下的 JSON，按名（含别名，大小写不敏感）
    /// 解析出可直接播放的 <see cref="NarrativeDialogueSequence"/>。
    /// </summary>
    public static class NarrativeDialogueShotRegistry
    {
        /// <summary>镜头 JSON 目录（工程相对，经 <see cref="NarrativeDataPaths"/> 归一）。</summary>
        public static string ShotDirectory = "Content/NarrativePro/Shots";

        private static readonly Dictionary<string, DialogueShotDefinition> _byName =
            new Dictionary<string, DialogueShotDefinition>(StringComparer.OrdinalIgnoreCase);
        private static readonly HashSet<string> _missingNames =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private static bool _bScanned;

        private static readonly JsonSerializerOptions _jsonOptions = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            ReadCommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = true,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        };

        /// <summary>注册一条镜头定义；shotName / 别名 / 传入键 全部建索引，重复覆盖。</summary>
        public static void Register(string key, DialogueShotDefinition definition)
        {
            if (definition == null) return;
            if (!string.IsNullOrEmpty(key)) _byName[NormalizeKey(key)] = definition;
            if (!string.IsNullOrEmpty(definition.ShotName)) _byName[NormalizeKey(definition.ShotName)] = definition;
            if (definition.Aliases != null)
            {
                foreach (var alias in definition.Aliases)
                {
                    if (!string.IsNullOrEmpty(alias)) _byName[NormalizeKey(alias)] = definition;
                }
            }
            if (!string.IsNullOrEmpty(definition.ShotName)) _missingNames.Remove(NormalizeKey(definition.ShotName));
        }

        /// <summary>
        /// 按镜头名解析。空名返回 null（静默，等价"未配置镜头"）；
        /// 非空未命中返回 null 并写一次警告（同一名字只报一次，避免每行对话刷屏），
        /// 调用方按原有回退语义落到默认镜头 —— 与接线前行为一致。
        /// </summary>
        public static NarrativeDialogueSequence Resolve(string shotName)
        {
            if (string.IsNullOrEmpty(shotName)) return null;

            EnsureScanned();
            string key = NormalizeKey(shotName);
            if (_byName.TryGetValue(key, out var definition)) return definition.ToSequence();

            if (_missingNames.Add(key))
            {
                NarrativeLog.LogWarning($"[DialogueShot] 无法解析对话镜头名 '{shotName}'（目录: {ShotDirectory}），回退默认镜头");
            }
            return null;
        }

        /// <summary>清空缓存（热重载或测试之间使用）。</summary>
        public static void ClearCaches()
        {
            _byName.Clear();
            _missingNames.Clear();
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
            string dir = NarrativeDataPaths.Resolve(ShotDirectory);
            if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir))
            {
                // 目录未落地属于配置状态，与 WeaponFX/GAS 注册表同口径静默
                return;
            }

            string[] files;
            try { files = Directory.GetFiles(dir, "*.json", SearchOption.AllDirectories); }
            catch (Exception ex)
            {
                NarrativeLog.LogWarning($"[DialogueShot] 扫描镜头目录失败 '{dir}': {ex.Message}");
                return;
            }

            foreach (var path in files)
            {
                try
                {
                    string json = File.ReadAllText(path);
                    var definition = JsonSerializer.Deserialize<DialogueShotDefinition>(json, _jsonOptions);
                    if (definition == null) continue;
                    Register(Path.GetFileNameWithoutExtension(path), definition);
                }
                catch (Exception ex)
                {
                    NarrativeLog.LogWarning($"[DialogueShot] 解析镜头定义 '{path}' 失败: {ex.Message}");
                }
            }
        }

        /// <summary>归一化键：去扩展名、反斜杠转正斜杠、取末段；大小写不敏感由字典比较器负责。</summary>
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
    }

    /// <summary>
    /// 一条对话镜头的 JSON 传输模型（对应 UE5 DS_* 蓝图实例化对象在端口可消费的那半数据）。
    /// 字段与 <see cref="NarrativeDialogueSequence"/> 一一对应，只列端口有真实消费方的参数，
    /// 理由见文件头「端口差异」第 2 条。取景距离用 UnitsY180DegreeRule 表达
    /// （Apply180DegreeRule 把说话者/听者沿银幕横轴分置到该间距），场景单位为厘米。
    /// </summary>
    [Serializable]
    public class DialogueShotDefinition
    {
        [JsonPropertyName("shotName")] public string ShotName { get; set; } = "";

        /// <summary>别名（UE5 资产名 DS_* / Shot_* 及 "Shot.X" 风格写法都能命中）。</summary>
        [JsonPropertyName("aliases")] public List<string> Aliases { get; set; }

        [JsonPropertyName("friendlyShotName")] public string FriendlyShotName { get; set; } = "";

        [JsonPropertyName("sequenceDurationSeconds")] public float SequenceDurationSeconds { get; set; } = 0f;
        [JsonPropertyName("autoPlay")] public bool AutoPlay { get; set; } = true;
        [JsonPropertyName("loop")] public bool Loop { get; set; } = false;
        [JsonPropertyName("playbackSpeed")] public float PlaybackSpeed { get; set; } = 1f;
        [JsonPropertyName("shouldRestart")] public bool ShouldRestart { get; set; } = false;

        [JsonPropertyName("anchorOriginRule")] public string AnchorOriginRule { get; set; } = "";
        [JsonPropertyName("anchorAvatarCustomId")] public string AnchorAvatarCustomId { get; set; } = "";

        [JsonPropertyName("use180DegreeRule")] public bool Use180DegreeRule { get; set; } = false;
        [JsonPropertyName("unitsY180DegreeRule")] public float UnitsY180DegreeRule { get; set; } = 50f;
        [JsonPropertyName("degreesYaw180DegreeRule")] public float DegreesYaw180DegreeRule { get; set; } = 15f;

        [JsonPropertyName("showCinematicBars")] public bool ShowCinematicBars { get; set; } = false;
        [JsonPropertyName("hideHUD")] public bool HideHUD { get; set; } = false;
        [JsonPropertyName("hideEvenEssentialHUD")] public bool HideEvenEssentialHUD { get; set; } = false;
        [JsonPropertyName("canSkip")] public bool CanSkip { get; set; } = true;

        [JsonPropertyName("cropMode")] public string CropMode { get; set; } = "";
        [JsonPropertyName("cropAspectRatio")] public float CropAspectRatio { get; set; } = 2.35f;
        [JsonPropertyName("cropAmount")] public float CropAmount { get; set; } = 0f;

        [JsonPropertyName("shakeAmplitude")] public float ShakeAmplitude { get; set; } = 0f;
        [JsonPropertyName("shakeDurationSeconds")] public float ShakeDurationSeconds { get; set; } = 0f;
        [JsonPropertyName("shakeFrequencyHz")] public float ShakeFrequencyHz { get; set; } = 8f;

        [JsonPropertyName("walkToStartTransform")] public bool WalkToStartTransform { get; set; } = false;

        /// <summary>构造一份可播放序列。每次调用返回新实例（运行期状态不跨对话共享）。</summary>
        public NarrativeDialogueSequence ToSequence()
        {
            var sequence = new NarrativeDialogueSequence
            {
                FriendlyShotName = !string.IsNullOrEmpty(FriendlyShotName) ? FriendlyShotName : (ShotName ?? ""),
                SequenceDurationSeconds = SequenceDurationSeconds,
                bAutoPlay = AutoPlay,
                bLoop = Loop,
                PlaybackSpeed = PlaybackSpeed > 0f ? PlaybackSpeed : 1f,
                bShouldRestart = ShouldRestart,
                AnchorOriginRule = ParseAnchorOrigin(AnchorOriginRule),
                AnchorAvatarCustomID = AnchorAvatarCustomId ?? "",
                bUse180DegreeRule = Use180DegreeRule,
                UnitsY180DegreeRule = UnitsY180DegreeRule,
                DegreesYaw180DegreeRule = DegreesYaw180DegreeRule,
                bShowCinematicBars = ShowCinematicBars,
                bHideHUD = HideHUD,
                bHideEvenEssentialHUD = HideEvenEssentialHUD,
                bCanSkip = CanSkip,
                ShakeAmplitude = ShakeAmplitude,
                ShakeDurationSeconds = ShakeDurationSeconds,
                ShakeFrequencyHz = ShakeFrequencyHz > 0f ? ShakeFrequencyHz : 8f,
                bWalkToStartTransform = WalkToStartTransform,
            };

            sequence.CropSettings = new PlateCropSettings
            {
                CropMode = ParseCropMode(CropMode),
                AspectRatio = CropAspectRatio > 0f ? CropAspectRatio : 2.35f,
                CropAmount = CropAmount,
            };
            return sequence;
        }

        private static EAnchorOriginRule ParseAnchorOrigin(string raw)
        {
            // 未写 / 写错时保持 UE5 构造函数默认（AOR_Disabled），不让一条脏数据改变既有呈现
            if (string.IsNullOrEmpty(raw)) return EAnchorOriginRule.AOR_Disabled;
            return Enum.TryParse<EAnchorOriginRule>(raw, true, out var parsed)
                ? parsed
                : EAnchorOriginRule.AOR_Disabled;
        }

        private static ECineCameraCropMode ParseCropMode(string raw)
        {
            if (string.IsNullOrEmpty(raw)) return ECineCameraCropMode.Default;
            return Enum.TryParse<ECineCameraCropMode>(raw, true, out var parsed)
                ? parsed
                : ECineCameraCropMode.Default;
        }
    }
}
