using FlaxEngine;
using FlaxEngine.GUI;
using HundunWorld.Game.Navigation;
using NarrativePro.Navigation;
using HundunWorld.Game.UI.StyleSystem;
using System;
using System.Threading.Tasks;

namespace HundunWorld.Game.UI.Ink.Pages
{
    /// <summary>
    /// 系统菜单：Esc 在战斗 HUD 上打开的模态入口（继续游戏 / 设置 / 退出到角色选择）。
    /// <para>
    /// 取代 NarrativePro 插件那个盖在 HUD 上的旧「暂停」框——它既不停游戏（「继续游戏」只是
    /// <c>Close()</c>），第二项还指向已被合并掉的旧任务日志面板。这里的每一项都接真行为，
    /// 暂停态由 <see cref="MainUIManager"/> 统一同步给 <see cref="UIMouseCapture.KeyboardCaptured"/> 消费。
    /// </para>
    /// </summary>
    public class SystemMenuPage : ContainerControl, IInkPage, IInkNavigationSource
    {
        private const float PanelWidth = 340f;
        private const float PanelHeight = 430f;
        private const float ButtonWidth = 260f;
        private const float ButtonHeight = 42f;
        private const float ButtonGap = 12f;

        /// <summary>导航请求（设置页等真页面）。</summary>
        public event Action<string> NavigationRequested;

        /// <summary>关闭本菜单（继续游戏）。</summary>
        public event Action CloseRequested;

        /// <summary>退出到角色选择。</summary>
        public event Action ExitRequested;

        public InkParticleSystem ParticleSystem { get; set; }

        private InkPanelElevated _panel;
        private Label _titleLabel;
        private Label _hintLabel;
        private InkButton _resumeButton;
        private InkButton _settingsButton;
        private InkButton _reconnectButton;
        private InkButton _resetSceneButton;
        private InkButton _safeEscapeButton;
        private InkButton _exitButton;

        public SystemMenuPage()
        {
            AnchorPreset = AnchorPresets.StretchAll;
            Offsets = Margin.Zero;
            // 浮层不额外压暗：外壳已经给了模态叠加层的层次，这里只铺居中的面板。
            BackgroundColor = Color.Transparent;
            ClipChildren = false;
            AutoFocus = false;

            _panel = new InkPanelElevated
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Size = new Float2(PanelWidth, PanelHeight),
            };
            AddChild(_panel);

            _titleLabel = new Label
            {
                Text = "系统菜单",
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Display, 22f),
                TextColor = InkWashTheme.GoldPrimary,
                AnchorPreset = AnchorPresets.TopLeft,
                Size = new Float2(PanelWidth, 34f),
                HorizontalAlignment = TextAlignment.Center,
                VerticalAlignment = TextAlignment.Center,
                AutoFocus = false,
            };
            _panel.AddChild(_titleLabel);

            _resumeButton = MakeButton("继续游戏", InkButtonVariant.Primary);
            _resumeButton.ButtonClicked += _ => CloseRequested?.Invoke();

            _settingsButton = MakeButton("设置", InkButtonVariant.Secondary);
            _settingsButton.ButtonClicked += _ => NavigationRequested?.Invoke(InkPageDomIds.NavSettings);

            _reconnectButton = MakeButton("重新连接", InkButtonVariant.Secondary);
            _reconnectButton.ButtonClicked += b => { _ = ReconnectAsync(); };

            _resetSceneButton = MakeButton("复位场景状态", InkButtonVariant.Ghost);
            _resetSceneButton.ButtonClicked += _ =>
            {
                GameSceneManager.Instance?.ForceResetTransition("system_menu_manual");
                _resetSceneButton.Text = "已复位场景状态";
            };

            _safeEscapeButton = MakeButton("回到最近传送点", InkButtonVariant.Ghost);
            _safeEscapeButton.ButtonClicked += _ => SafeEscapeToNearestPoi();

            _exitButton = MakeButton("退出到角色选择", InkButtonVariant.Danger);
            _exitButton.ButtonClicked += _ => ExitRequested?.Invoke();

            _hintLabel = new Label
            {
                Text = "按 ESC 返回游戏 · 网络异常可重连",
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Body, 11f),
                TextColor = InkWashTheme.TextTertiary,
                AnchorPreset = AnchorPresets.TopLeft,
                Size = new Float2(PanelWidth, 16f),
                HorizontalAlignment = TextAlignment.Center,
                AutoFocus = false,
            };
            _panel.AddChild(_hintLabel);
        }

        private InkButton MakeButton(string text, InkButtonVariant variant)
        {
            var button = new InkButton
            {
                Text = text,
                Variant = variant,
                ButtonSize = InkButtonSize.Lg,
                AnchorPreset = AnchorPresets.TopLeft,
                Size = new Float2(ButtonWidth, ButtonHeight),
            };
            _panel.AddChild(button);
            return button;
        }

        private void SafeEscapeToNearestPoi()
        {
            var game = HundunWorldGame.Instance;
            var player = game?.LocalPlayerActor;
            var nav = PluginNavigationBridge.FindNavigationComponent();
            var subsystem = NavigationSubsystem.Instance;
            if (player == null || nav == null || subsystem == null)
            {
                _safeEscapeButton.Text = "场景/导航不可用";
                return;
            }

            var pos = player.Position;
            POIData best = null;
            var bestDist = float.MaxValue;
            foreach (var poi in subsystem.GetAllPOIs())
            {
                if (poi == null || !poi.bSupportsFastTravel) continue;
                if (poi.bIsDiscoverable && !nav.HasDiscoveredPOI(poi.POITag)) continue;

                var d = poi.POILocation - pos;
                var dist = d.X * d.X + d.Y * d.Y + d.Z * d.Z;
                if (dist >= bestDist) continue;
                bestDist = dist;
                best = poi;
            }

            if (best == null)
            {
                _safeEscapeButton.Text = "附近没有可用传送点";
                return;
            }

            _safeEscapeButton.Text = nav.RequestFastTravel(best)
                ? "已传送到最近传送点"
                : "传送失败（目标不可用）";
        }

        private async Task ReconnectAsync()
        {
            var nm = HundunWorldGame.Instance?.NetworkManager;
            if (nm == null)
            {
                _reconnectButton.Text = "网络未初始化";
                return;
            }

            _reconnectButton.Enabled = false;
            _reconnectButton.Text = "重连中…";
            try
            {
                var ok = await nm.ManualReconnectAsync();
                _reconnectButton.Text = ok ? "已重新连接" : "重连失败";
            }
            catch (Exception ex)
            {
                _reconnectButton.Text = "重连失败";
                Debug.LogWarning($"[SystemMenuPage] 手动重连失败: {ex.Message}");
            }
            finally
            {
                _reconnectButton.Enabled = true;
            }
        }

        /// <inheritdoc />
        public void RefreshLayout()
        {
            if (_panel == null) return;

            // 外壳在 AddChild 之后立刻调一次 RefreshLayout，那一刻 StretchAll 还没把尺寸落下来；
            // 按设计尺寸兜底，面板才不会停在负坐标上"注册成功但看不见"。
            float w = Width > 0f ? Width : 1920f;
            float h = Height > 0f ? Height : 1080f;

            _panel.Location = new Float2(
                (w - PanelWidth) * 0.5f,
                (h - PanelHeight) * 0.5f);

            _titleLabel.Location = new Float2(0f, 22f);

            // 三枚按钮整组垂直居中于面板中部，标题与页脚各占一端。
            float groupHeight = 6f * ButtonHeight + 5f * ButtonGap;
            float y = 22f + 34f + (PanelHeight - 22f - 34f - 30f - groupHeight) * 0.5f;
            float x = (PanelWidth - ButtonWidth) * 0.5f;

            _resumeButton.Location = new Float2(x, y);
            _settingsButton.Location = new Float2(x, y + ButtonHeight + ButtonGap);
            _reconnectButton.Location = new Float2(x, y + 2f * (ButtonHeight + ButtonGap));
            _resetSceneButton.Location = new Float2(x, y + 3f * (ButtonHeight + ButtonGap));
            _safeEscapeButton.Location = new Float2(x, y + 4f * (ButtonHeight + ButtonGap));
            _exitButton.Location = new Float2(x, y + 5f * (ButtonHeight + ButtonGap));

            _hintLabel.Location = new Float2(0f, PanelHeight - 30f);
        }

        /// <inheritdoc />
        public override void OnParentResized()
        {
            base.OnParentResized();
            RefreshLayout();
        }
    }
}
