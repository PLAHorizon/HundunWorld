using System;
using FlaxEngine;
using NarrativePro.Core;
using NarrativePro.Items;

namespace NarrativePro.Music
{
    /// <summary>
    /// 音乐驱动入口。
    /// <para>
    /// 为什么要它：<see cref="NarrativeMusicSubsystem"/> 本身是完整可用的（状态机 + 双轨淡入淡出），
    /// 但 UE5 里 <c>SetTheme</c> 的调用方全是蓝图（C++ 侧零调用），而 Flax 端口没有蓝图，
    /// 于是这个子系统既没有宿主持有它、也没有任何代码调用它 —— 音乐等于不存在。
    /// 本类提供代码侧入口：把子系统挂到运行期宿主上，并把主题切换接到**确实会发火**的游戏信号上
    /// （<see cref="Tales.TalesComponent"/> 的 OnQuestStarted / OnQuestSucceeded / OnQuestFailed，
    /// 由 NarrativeRuntimeBootstrap 已有的事件订阅转发过来）。
    /// </para>
    /// <para>
    /// 主题取自带标签音乐集（Content/NarrativePro/Music/MusicSet_Default.json 的 entries[].theme），
    /// 未登记的主题只记一条警告并保持当前音乐不变，不会打断播放。
    /// </para>
    /// </summary>
    public static class NarrativeMusicDriver
    {
        /// <summary>基础（常态）主题。对应 NativeMusicThemeTags.TAG_MUSIC_AMBIENT。</summary>
        public static readonly GameplayTag ThemeAmbient = NativeMusicThemeTags.TAG_MUSIC_AMBIENT;

        /// <summary>紧张/战斗主题。对应 NativeMusicThemeTags.TAG_MUSIC_COMBAT。</summary>
        public static readonly GameplayTag ThemeCombat = NativeMusicThemeTags.TAG_MUSIC_COMBAT;

        /// <summary>任务完成短乐句。对应 MusicSet_Default.json 的 "Music.Quest.Success"。</summary>
        public static readonly GameplayTag ThemeQuestSuccess = new GameplayTag("Music.Quest.Success");

        /// <summary>短乐句/紧张段落结束后回落到基础主题的时长（秒）。</summary>
        private const float ReturnToBaseSeconds = 10f;

        private static float _returnTimer;
        private static GameplayTag _baseTheme = ThemeAmbient;
        private static bool _bSubsystemReported;

        /// <summary>
        /// 把音乐子系统挂到运行期宿主上并起底一段基础主题（幂等：已有实例只复用）。
        /// 由 <c>NarrativeRuntimeBootstrap.EnsureMusicSubsystem()</c> 调用 —— Flax 没有
        /// GameInstanceSubsystem，脚本必须显式挂在某个 Actor 上才会被引擎驱动 OnUpdate（淡入淡出靠它）。
        /// </summary>
        public static NarrativeMusicSubsystem AttachTo(Actor host)
        {
            if (host == null) return null;

            var subsystem = NarrativeMusicSubsystem.Instance;
            if (subsystem == null)
            {
                subsystem = host.GetScript<NarrativeMusicSubsystem>()
                            ?? host.AddScript<NarrativeMusicSubsystem>();
            }

            if (subsystem == null)
            {
                Core.NarrativeLog.LogWarning("[Music] 音乐子系统挂载失败（宿主 Actor 不可用）");
                return null;
            }

            // 起底：常态主题。SetTheme 内部首次播放走立即切换（_activeTrackID == -1 分支）
            bool started = Play(subsystem, _baseTheme, true);
            if (!_bSubsystemReported)
            {
                _bSubsystemReported = true;
                Core.NarrativeLog.Log(
                    $"[Music] 音乐子系统已挂载到 {host.Name}（音乐集={(subsystem.GetActiveMusicSet() != null ? "已装载" : "缺失")}，" +
                    $"起底主题 {(_baseTheme.IsValid() ? _baseTheme.TagName : "-")} = {(started ? "已播放" : "未播放")}）");
            }

            return subsystem;
        }

        /// <summary>每帧驱动回落计时（由 NarrativeRuntimeBootstrap 的泵调用，子系统自身的 OnUpdate 只管 fade）。</summary>
        public static void Tick()
        {
            if (_returnTimer <= 0f) return;

            _returnTimer -= Time.DeltaTime;
            if (_returnTimer > 0f) return;

            _returnTimer = 0f;

            var subsystem = NarrativeMusicSubsystem.Instance;
            if (subsystem == null) return;
            if (!string.Equals(subsystem.GetActiveTheme().TagName, _baseTheme.TagName, StringComparison.Ordinal))
            {
                Play(subsystem, _baseTheme, false);
            }
        }

        // ===== 信号入口（由引导的 Tales 事件订阅转发） =====

        /// <summary>任务开始：回到常态（可能正停在上一段短乐句上）。</summary>
        public static void OnQuestStarted()
        {
            _returnTimer = 0f;
            var subsystem = NarrativeMusicSubsystem.Instance;
            if (subsystem == null) return;
            Play(subsystem, _baseTheme, false);
        }

        /// <summary>任务成功：播短乐句，随后自动回落常态。</summary>
        public static void OnQuestSucceeded()
        {
            Stinger(ThemeQuestSuccess);
        }

        /// <summary>任务失败：切到紧张段落，随后自动回落常态。</summary>
        public static void OnQuestFailed()
        {
            Stinger(ThemeCombat);
        }

        /// <summary>把常态主题换成指定主题（例如玩法侧按区域切换氛围）。</summary>
        public static void SetBaseTheme(GameplayTag theme)
        {
            if (!theme.IsValid()) return;
            _baseTheme = theme;

            var subsystem = NarrativeMusicSubsystem.Instance;
            if (subsystem != null) Play(subsystem, theme, false);
        }

        /// <summary>当前会回落到的常态主题。</summary>
        public static GameplayTag BaseTheme => _baseTheme;

        /// <summary>运行期重置（场景/宿主卸载时调用，避免把上一场景的回落计时带进下一场景）。</summary>
        public static void Reset()
        {
            _returnTimer = 0f;
            _bSubsystemReported = false;
            _baseTheme = ThemeAmbient;
        }

        private static void Stinger(GameplayTag theme)
        {
            _returnTimer = ReturnToBaseSeconds;

            var subsystem = NarrativeMusicSubsystem.Instance;
            if (subsystem == null)
            {
                Core.NarrativeLog.LogWarning($"[Music] 主题 {theme.TagName} 信号到达，但音乐子系统尚未挂载（被忽略）");
                return;
            }

            if (!Play(subsystem, theme, false))
            {
                // 音乐集里没有这个主题：立刻取消回落计时，别让一段没播起来的短乐句把常态主题挤掉
                _returnTimer = 0f;
            }
        }

        /// <summary>
        /// 播放主题：只在当前音乐集确实登记了该主题时才调用 SetTheme，
        /// 避免子系统内部 "Theme not found" 之后什么也没发生。
        /// </summary>
        private static bool Play(NarrativeMusicSubsystem subsystem, GameplayTag theme, bool immediate)
        {
            if (subsystem == null || !theme.IsValid()) return false;

            var musicSet = subsystem.GetActiveMusicSet();
            if (musicSet == null || !musicSet.Has(theme))
            {
                Core.NarrativeLog.LogWarning(
                    $"[Music] 音乐集未登记主题 {theme.TagName}（保持当前播放不变）");
                return false;
            }

            return subsystem.SetTheme(theme, immediate);
        }
    }
}
