using FlaxEngine;
using FlaxEngine.GUI;
using Game.Character.Attributes;
using HundunWorld.Game.UI.Ink;
using HundunWorld.Game.UI.StyleSystem;

namespace HundunWorld.Game.UI.GameMain
{
    /// <summary>
    /// 名牌宿主：由 <see cref="MainUIManager"/> 在水墨 UICanvas（InkWashUICanvas）根下创建的
    /// 全屏容器，绘制在最上层。所有角色的名牌控件都挂在其下；其 Visible 由 MainUIManager
    /// 按当前页面（仅战斗 HUD 现场页显示）统一开关，从而一次隐藏全部名牌。
    /// </summary>
    public static class NameplateHost
    {
        /// <summary>当前宿主容器；未进入游戏世界时为 null。</summary>
        public static ContainerControl Current;
    }

    /// <summary>
    /// 角色头顶名牌组件：挂在每个角色 Actor 上（本地玩家 + 远程玩家），每帧把自身头顶世界坐标
    /// 投影到屏幕，在共享宿主 <see cref="NameplateHost.Current"/> 内定位并绘制血条/名称/等级/称号。
    /// 控件随 Actor 销毁（OnDestroy）自动回收。
    /// 注意：Flax 世界单位是厘米，HeadOffset / MaxDistance 均为厘米。
    /// </summary>
    public class NameplateUI : Script
    {
        // 头顶高度（厘米）：角色约 180cm 高，名牌上浮到 215cm 处
        private const float HeadOffset = 215f;
        // 超过此距离隐藏名牌（厘米），避免远处密集名牌堆叠
        private const float MaxDistance = 6000f;

        private NameplateWidget _widget;

        public override void OnUpdate()
        {
            var host = NameplateHost.Current;
            if (host == null || !host.Visible)
            {
                _widget?.SetVisible(false);
                return;
            }

            if (_widget == null)
                _widget = new NameplateWidget(host);

            var camera = Camera.MainCamera;
            if (camera == null)
            {
                _widget.SetVisible(false);
                return;
            }

            var attrs = Actor.GetScript<CharacterAttributesComponent>();
            string name = attrs != null && !string.IsNullOrEmpty(attrs.Nickname)
                ? attrs.Nickname
                : (Actor.GetScript<RemotePlayerActor>()?.PlayerName ?? Actor.Name);
            int level = attrs != null ? attrs.Level : 0;
            float curHp = attrs != null ? attrs.CurrentHealth : 1f;
            float maxHp = attrs != null && attrs.MaxHealth > 0f ? attrs.MaxHealth : 1f;

            // 头顶世界坐标 → 屏幕坐标（世界单位：厘米）
            Vector3 head = Actor.Position + Vector3.Up * HeadOffset;
            var camT = camera.Transform;
            float dot = Vector3.Dot(head - camT.Translation, camT.Forward);
            float dist = Vector3.Distance(head, camT.Translation);
            camera.ProjectPoint(head, out var screen);

            // 视口边界用宿主画布的像素尺寸判定：Flax 的 Camera.Viewport 是归一化 [0..1]，
            // 拿它和 ProjectPoint 返回的像素坐标比较会把所有名牌误判为“屏幕外”而永不显示。
            bool inBounds = screen.X >= 0f && screen.X <= host.Width && screen.Y >= 0f && screen.Y <= host.Height;

            if (dot <= 0f || dist > MaxDistance || !inBounds)
            {
                _widget.SetVisible(false);
                return;
            }

            _widget.SetVisible(true);
            _widget.Location = new Float2(screen.X - NameplateWidget.PlateWidth * 0.5f,
                                          screen.Y - NameplateWidget.PlateHeight);
            _widget.SetContent(name, level, curHp / maxHp, attrs?.Title);
        }

        public override void OnDestroy()
        {
            _widget?.Dispose();
            _widget = null;
        }
    }

    /// <summary>单个角色名牌控件：称号 + 名称/等级 + 血条。</summary>
    internal class NameplateWidget
    {
        internal const float PlateWidth = 160f;
        internal const float PlateHeight = 58f;
        private const float BarWidth = 132f;
        private const float BarHeight = 8f;

        private readonly Panel _root;
        private readonly Label _title;
        private readonly Label _name;
        private readonly RoundedPanel _barFill;
        private float _lastRatio = -1f;

        internal NameplateWidget(ContainerControl parent)
        {
            _root = new Panel
            {
                Parent = parent,
                AnchorPreset = AnchorPresets.TopLeft,
                Size = new Float2(PlateWidth, PlateHeight),
                BackgroundColor = Color.Transparent,
                ClipChildren = false,
            };

            _title = new Label
            {
                Parent = _root,
                Bounds = new Rectangle(0, 0, PlateWidth, 16),
                Text = string.Empty,
                TextColor = Color.White,
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Body, 11f),
                HorizontalAlignment = TextAlignment.Center,
                VerticalAlignment = TextAlignment.Center,
            };

            _name = new Label
            {
                Parent = _root,
                Bounds = new Rectangle(0, 17, PlateWidth, 18),
                Text = string.Empty,
                TextColor = Color.White,
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Body, 13f),
                HorizontalAlignment = TextAlignment.Center,
                VerticalAlignment = TextAlignment.Center,
            };

            float barX = (PlateWidth - BarWidth) * 0.5f;
            var barBg = new RoundedPanel
            {
                Parent = _root,
                Bounds = new Rectangle(barX, 38, BarWidth, BarHeight),
                BackgroundColor = new Color(0.1f, 0.05f, 0.05f, 0.85f),
                CornerRadius = 3f,
            };

            _barFill = new RoundedPanel
            {
                Parent = barBg,
                Bounds = new Rectangle(0, 0, BarWidth, BarHeight),
                BackgroundColor = new Color(0.2f, 0.8f, 0.25f, 0.95f),
                CornerRadius = 3f,
            };
        }

        internal Float2 Location
        {
            get => _root.Location;
            set => _root.Location = value;
        }

        internal void SetVisible(bool visible) => _root.Visible = visible;

        internal void SetContent(string name, int level, float hpRatio, string title = null)
        {
            hpRatio = Mathf.Clamp(hpRatio, 0f, 1f);

            // 服务端下发的称号优先；未佩戴/旧服务端无此字段时回退按等级推导的阶梯称号。
            _title.Text = string.IsNullOrEmpty(title) ? TitleForLevel(level) : title;
            _title.TextColor = TitleColorForLevel(level);
            _name.Text = level > 0 ? $"{name} Lv.{level}" : name;

            if (Mathf.Abs(hpRatio - _lastRatio) > 0.001f)
            {
                _lastRatio = hpRatio;
                var b = _barFill.Bounds;
                b.Size.X = BarWidth * hpRatio;
                _barFill.Bounds = b;

                _barFill.BackgroundColor = hpRatio > 0.5f
                    ? new Color(0.2f, 0.8f, 0.25f, 0.95f)
                    : hpRatio > 0.25f
                        ? new Color(0.9f, 0.8f, 0.2f, 0.95f)
                        : new Color(0.85f, 0.2f, 0.2f, 0.95f);
            }
        }

        internal void Dispose()
        {
            if (_root != null && _root.Parent != null)
                _root.Parent.RemoveChild(_root);
        }

        /// <summary>修炼称号阶梯：按等级映射称号与配色（低阶灰、高阶鎏金）。</summary>
        private static string TitleForLevel(int level)
        {
            if (level <= 9) return "杂役";
            if (level <= 29) return "外门弟子";
            if (level <= 59) return "内门弟子";
            if (level <= 99) return "真传弟子";
            if (level <= 149) return "长老";
            if (level <= 199) return "宗主";
            return "天骄";
        }

        private static Color TitleColorForLevel(int level)
        {
            if (level <= 9) return new Color(0.65f, 0.65f, 0.65f);       // 灰
            if (level <= 29) return new Color(0.45f, 0.75f, 0.45f);       // 绿
            if (level <= 59) return new Color(0.45f, 0.65f, 0.95f);       // 蓝
            if (level <= 99) return new Color(0.75f, 0.5f, 0.9f);         // 紫
            if (level <= 149) return new Color(0.95f, 0.7f, 0.25f);       // 橙金
            if (level <= 199) return new Color(0.95f, 0.35f, 0.3f);       // 赤
            return new Color(1f, 0.85f, 0.3f);                            // 鎏金（天骄）
        }
    }
}
