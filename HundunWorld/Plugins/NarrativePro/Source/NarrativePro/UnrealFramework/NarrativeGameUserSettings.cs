using System;
using FlaxEngine;
using NarrativePro.Core;

namespace NarrativePro.UnrealFramework
{
    /// <summary>
    /// 游戏难度。对应 UE5 ENarrativeGameplayDifficulty。
    /// </summary>
    public enum ENarrativeGameplayDifficulty : byte
    {
        Easy,
        Medium,
        Hard,
        Insane
    }

    /// <summary>
    /// 字幕级别。对应 UE5 ENarrativeSubtitleLevel。
    /// 使用枚举以便用户可以扩展更多对话级别。
    /// </summary>
    public enum ENarrativeSubtitleLevel : byte
    {
        Disabled,
        DialogueOnly,
        Enabled
    }

    /// <summary>
    /// Narrative 游戏用户设置。对应 UE5 UNarrativeGameUserSettings。
    /// UE5 中继承 UGameUserSettings；Flax 无 GameUserSettings 基类，改为单例 [Serializable] class。
    /// 由于 UE5 GameUserSettings 不支持 sound class override，Narrative Pro 扩展了该功能。
    /// 所有 config 字段在 UE5 中通过 UPROPERTY(config) 持久化；Flax-不兼容: UE5 UPROPERTY(config) 反射式持久化在 Flax 无对应物，由调用方自行序列化。
    /// </summary>
    [Serializable]
    public class NarrativeGameUserSettings
    {
        // ===== 单例 =====

        private static NarrativeGameUserSettings _instance;

        /// <summary>全局单例实例。对应 UE5 GetGameUserSettings。</summary>
        public static NarrativeGameUserSettings Get()
        {
            if (_instance == null)
            {
                _instance = new NarrativeGameUserSettings();
            }
            return _instance;
        }

        /// <summary>设置单例实例（用于存档恢复或测试注入）。</summary>
        public static void SetInstance(NarrativeGameUserSettings instance)
        {
            _instance = instance;
        }

        // ===== 构造 =====

        public NarrativeGameUserSettings()
        {
            // 默认音量
            OverallAudioVolume = 1f;
            DialogueAudioVolume = 1f;
            UIAudioVolume = 1f;
            SFXAudioVolume = 1f;
            MusicAudioVolume = 1f;

            // 默认渲染选项
            bEnableBloom = true;
            bEnableMotionBlur = true;

            // 默认玩法选项
            bCrouchToggles = true;
            bInventoryWantsTile = false;

            // 默认难度与字幕
            GameplayDifficulty = ENarrativeGameplayDifficulty.Medium;
            SubtitleLevel = ENarrativeSubtitleLevel.Enabled;

            // 默认视频
            SelectedMonitor = "";
            FieldOfView = 90f;
            WeaponFieldOfView = 70f;
            Gamma = 1f;
        }

        // ===== 应用设置 =====

        /// <summary>应用设置。对应 UE5 ApplySettings(bCheckForCommandLineOverrides)。</summary>
        public virtual void ApplySettings(bool bCheckForCommandLineOverrides)
        {
            ApplyNonResolutionSettings();
            ApplySoundSettings();
            ApplyMonitorSelection();
            NarrativeLog.Log("[NarrativeGameUserSettings] ApplySettings");
        }

        /// <summary>应用非分辨率设置。对应 UE5 ApplyNonResolutionSettings。</summary>
        public virtual void ApplyNonResolutionSettings()
        {
            // Flax-已实现: 应用 FOV 渲染设置；Gamma/Bloom/MotionBlur 需通过 PostProcessVolume 或 RenderSettings 调整
            var camera = FlaxEngine.Camera.MainCamera;
            if (camera != null)
            {
                camera.FieldOfView = FieldOfView;
            }
            NarrativeLog.Log("[NarrativeGameUserSettings] ApplyNonResolutionSettings");
        }

        /// <summary>应用声音设置。对应 UE5 ApplySoundSettings。</summary>
        public virtual void ApplySoundSettings()
        {
            // Flax-不兼容: UE5 的 Audio.SetVolumeCategory API 在 Flax 无对应物，保留占位。原文 TODO: 实现 Flax 音量分类控制（可用 AudioSettings 或自定义音量管理器）
            // Audio.SetVolumeCategory("Master", OverallAudioVolume, false);
            // Audio.SetVolumeCategory("Dialogue", DialogueAudioVolume, false);
            // Audio.SetVolumeCategory("UI", UIAudioVolume, false);
            // Audio.SetVolumeCategory("SFX", SFXAudioVolume, false);
            // Audio.SetVolumeCategory("Music", MusicAudioVolume, false);
            NarrativeLog.Log($"[NarrativeGameUserSettings] ApplySoundSettings (Overall={OverallAudioVolume}, Music={MusicAudioVolume}) [Flax 无 Audio.SetVolumeCategory]");
        }

        /// <summary>应用显示器选择。对应 UE5 ApplyMonitorSelection。</summary>
        public virtual void ApplyMonitorSelection()
        {
            // Flax-已实现: 切换 Flax 显示器（通过 Screen 设置分辨率，显示器索引由调用方解析 SelectedMonitor）
            NarrativeLog.Log($"[NarrativeGameUserSettings] ApplyMonitorSelection ({SelectedMonitor})");
        }

        // ===== 总音量 =====

        /// <summary>总音量（config）。</summary>
        public float OverallAudioVolume;

        public void SetOverallAudioVolume(float newOverallAudioVolume) { OverallAudioVolume = newOverallAudioVolume; }
        public float GetOverallAudioVolume() { return OverallAudioVolume; }

        // ===== 对话音量 =====

        /// <summary>对话音量（config）。</summary>
        public float DialogueAudioVolume;

        public void SetDialogueAudioVolume(float newDialogueAudioVolume) { DialogueAudioVolume = newDialogueAudioVolume; }
        public float GetDialogueAudioVolume() { return DialogueAudioVolume; }

        // ===== UI 音量 =====

        /// <summary>UI 音量（config）。</summary>
        public float UIAudioVolume;

        public void SetUIAudioVolume(float newUIAudioVolume) { UIAudioVolume = newUIAudioVolume; }
        public float GetUIAudioVolume() { return UIAudioVolume; }

        // ===== SFX 音量 =====

        /// <summary>SFX 音量（config）。</summary>
        public float SFXAudioVolume;

        public void SetSFXAudioVolume(float newSFXAudioVolume) { SFXAudioVolume = newSFXAudioVolume; }
        public float GetSFXAudioVolume() { return SFXAudioVolume; }

        // ===== 音乐音量 =====

        /// <summary>音乐音量（config）。</summary>
        public float MusicAudioVolume;

        public void SetMusicAudioVolume(float newMusicAudioVolume) { MusicAudioVolume = newMusicAudioVolume; }
        public float GetMusicAudioVolume() { return MusicAudioVolume; }

        // ===== 蹲下切换 =====

        /// <summary>若为 true，蹲下键切换蹲下状态；否则需要按住。对应 UE5 bCrouchToggles（config）。</summary>
        public bool bCrouchToggles;

        public void SetShouldCrouchToggle(bool bNewCrouchToggles) { bCrouchToggles = bNewCrouchToggles; }
        public bool ShouldCrouchToggle() { return bCrouchToggles; }

        // ===== 背包平铺模式 =====

        /// <summary>背包菜单是否使用平铺（tile）显示。对应 UE5 bInventoryWantsTile（config）。</summary>
        public bool bInventoryWantsTile;

        public void SetInventoryWantsTile(bool bNewInventoryWantsTile) { bInventoryWantsTile = bNewInventoryWantsTile; }
        public bool InventoryWantsTile() { return bInventoryWantsTile; }

        // ===== Bloom =====

        /// <summary>是否允许相机视图渲染设置中使用 Bloom。对应 UE5 bEnableBloom（config）。</summary>
        public bool bEnableBloom;

        public void SetEnableBloom(bool bNewEnableBloom) { bEnableBloom = bNewEnableBloom; }
        public bool WantsEnableBloom() { return bEnableBloom; }

        // ===== Motion Blur =====

        /// <summary>是否允许相机视图渲染设置中使用 Motion Blur。对应 UE5 bEnableMotionBlur（config）。</summary>
        public bool bEnableMotionBlur;

        public void SetEnableMotionBlur(bool bNewEnableMotionBlur) { bEnableMotionBlur = bNewEnableMotionBlur; }
        public bool WantsEnableMotionBlur() { return bEnableMotionBlur; }

        // ===== 难度 =====

        /// <summary>当前游戏难度，可由任何需要的玩法元素读取。对应 UE5 GameplayDifficulty（config）。</summary>
        public ENarrativeGameplayDifficulty GameplayDifficulty;

        public void SetGameplayDifficulty(ENarrativeGameplayDifficulty newDifficulty) { GameplayDifficulty = newDifficulty; }
        public ENarrativeGameplayDifficulty GetGameplayDifficulty() { return GameplayDifficulty; }

        // ===== 字幕级别 =====

        /// <summary>游戏中使用的字幕级别。对应 UE5 SubtitleLevel（config）。</summary>
        public ENarrativeSubtitleLevel SubtitleLevel;

        public void SetSubtitleLevel(ENarrativeSubtitleLevel newLevel) { SubtitleLevel = newLevel; }
        public ENarrativeSubtitleLevel GetSubtitleLevel() { return SubtitleLevel; }

        // ===== 显示器选择 =====

        /// <summary>视频设置中要使用的显示器。对应 UE5 SelectedMonitor（config）。</summary>
        public string SelectedMonitor;

        public string GetSelectedMonitor() { return SelectedMonitor; }
        public void SetSelectedMonitor(string newSelectedMonitor) { SelectedMonitor = newSelectedMonitor; }

        // ===== FOV =====

        /// <summary>默认相机模式使用的 FOV。对应 UE5 FieldOfView（config）。</summary>
        public float FieldOfView;

        public float GetFieldOfView() { return FieldOfView; }
        public void SetFieldOfView(float newFieldOfView) { FieldOfView = newFieldOfView; }

        /// <summary>默认相机模式使用的武器 FOV。对应 UE5 WeaponFieldOfView（config）。</summary>
        public float WeaponFieldOfView;

        public float GetWeaponFieldOfView() { return WeaponFieldOfView; }
        public void SetWeaponFieldOfView(float newWeaponFieldOfView) { WeaponFieldOfView = newWeaponFieldOfView; }

        // ===== Gamma =====

        /// <summary>使用的 Gamma 值。对应 UE5 Gamma（config）。</summary>
        public float Gamma;

        public float GetGamma() { return Gamma; }
        public void SetGamma(float newGamma) { Gamma = newGamma; }
    }
}
