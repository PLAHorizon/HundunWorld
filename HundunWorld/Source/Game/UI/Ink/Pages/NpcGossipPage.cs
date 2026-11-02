using System;
using FlaxEngine;
using FlaxEngine.GUI;
using HundunWorld.Game.Selection;
using HundunWorld.Game.UI.StyleSystem;

namespace HundunWorld.Game.UI.Ink.Pages
{
    /// <summary>
    /// NPC 对话菜单（WoW 里"点一下 NPC 先看到能做什么"的那个窗口）。
    /// <para>
    /// <b>为什么必须有这一页</b>：以前交互层按服务位**各自**开窗——一个既有货架、又教艺、又有炉砧的 NPC，
    /// 三次导航连着发，最后打开的那个赢。玩家看到的是"铁匠只能买东西"，而另两个入口没有任何提示。
    /// 菜单把"能做什么"摆出来，由玩家选，再各自走原来的服务端通路。
    /// </para>
    /// <para>
    /// 菜单项来自内容表（<c>gossip_options</c>）。**配了台词就念台词**（023 的 <c>text</c> 列），
    /// 没配才按动作种类合成标签（老表没有这一键，读不到就退回合成）；每一项执行时仍由服务端仲裁
    /// （买得到吗 / 学得会吗 / 做得了吗）。
    /// </para>
    /// </summary>
    public class NpcGossipPage : ContainerControl, IInkPage, IInkNavigationSource
    {
        private const float PanelWidth = 380f;
        private const float ButtonWidth = 300f;
        private const float ButtonHeight = 42f;
        private const float ButtonGap = 10f;
        private const int MaxOptions = 6;

        /// <summary>导航请求（商店/技能/制作/任务页）。</summary>
        public event Action<string> NavigationRequested;

        /// <summary>关闭本菜单。</summary>
        public event Action CloseRequested;

        public InkParticleSystem ParticleSystem { get; set; }

        private readonly InkPanelElevated _panel;
        private readonly Label _nameLabel;
        private Label _hintLabel;
        private readonly InkButton _closeButton;

        public NpcGossipPage()
        {
            AnchorPreset = AnchorPresets.StretchAll;
            Offsets = Margin.Zero;
            BackgroundColor = Color.Transparent;
            ClipChildren = false;
            AutoFocus = false;

            var request = NpcGossipService.Take();
            var options = BuildOptions(request);

            var panelHeight = 92f + options.Count * (ButtonHeight + ButtonGap) + 56f;

            _panel = new InkPanelElevated
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Size = new Float2(PanelWidth, panelHeight),
            };
            AddChild(_panel);

            _nameLabel = new Label
            {
                Text = string.IsNullOrEmpty(request?.DisplayName) ? "交谈" : request!.DisplayName,
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Display, 22f),
                TextColor = InkWashTheme.GoldPrimary,
                AnchorPreset = AnchorPresets.TopLeft,
                Size = new Float2(PanelWidth, 34f),
                HorizontalAlignment = TextAlignment.Center,
                VerticalAlignment = TextAlignment.Center,
                AutoFocus = false,
            };
            _panel.AddChild(_nameLabel);

            for (var i = 0; i < options.Count; i++)
            {
                var option = options[i];
                var button = new InkButton
                {
                    Text = option.Text,
                    Variant = i == 0 ? InkButtonVariant.Primary : InkButtonVariant.Secondary,
                    ButtonSize = InkButtonSize.Lg,
                    AnchorPreset = AnchorPresets.TopLeft,
                    Size = new Float2(ButtonWidth, ButtonHeight),
                    Enabled = option.Enabled,
                };
                button.ButtonClicked += _ => Dispatch(request, option.Kind, option.Text);
                _panel.AddChild(button);
                _buttons.Add(button);
            }

            _closeButton = new InkButton
            {
                Text = "离开",
                Variant = InkButtonVariant.Secondary,
                ButtonSize = InkButtonSize.Lg,
                AnchorPreset = AnchorPresets.TopLeft,
                Size = new Float2(ButtonWidth, ButtonHeight),
            };
            _closeButton.ButtonClicked += _ => CloseRequested?.Invoke();
            _panel.AddChild(_closeButton);

            _hintLabel = new Label
            {
                Text = "每一项仍由服务端裁决（价钱、门槛、库存）",
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Body, 11f),
                TextColor = InkWashTheme.TextTertiary,
                AnchorPreset = AnchorPresets.TopLeft,
                Size = new Float2(PanelWidth, 16f),
                HorizontalAlignment = TextAlignment.Center,
                AutoFocus = false,
            };
            _panel.AddChild(_hintLabel);

            LayoutButtons();
        }

        private readonly System.Collections.Generic.List<InkButton> _buttons = new();

        /// <summary>菜单项（文字 + 动作种类 + 是否可点）。</summary>
        private readonly struct MenuItem
        {
            public readonly string Text;
            public readonly int Kind;
            public readonly bool Enabled;

            public MenuItem(string text, int kind, bool enabled)
            {
                Text = text;
                Kind = kind;
                Enabled = enabled;
            }
        }

        /// <summary>
        /// 把内容表的动作种类翻成玩家看得懂的条目。
        /// <para>种类与 <c>NpcGossipCore</c> 同值：0 文本 1 商店 2 训练 3 制作 4 交任务 5 子菜单。</para>
        /// </summary>
        private static System.Collections.Generic.List<MenuItem> BuildOptions(NpcGossipService.Request request)
        {
            var items = new System.Collections.Generic.List<MenuItem>();
            if (request == null || request.Options.Count == 0)
            {
                items.Add(new MenuItem("（无话可说）", 0, false));
                return items;
            }

            for (var i = 0; i < request.Options.Count && items.Count < MaxOptions; i++)
            {
                var option = request.Options[i];
                var levelOk = (option.MinLevel <= 0 || request.PlayerLevel >= option.MinLevel)
                              && (option.MaxLevel <= 0 || request.PlayerLevel <= option.MaxLevel);

                // 配了台词就念台词；没配才退回按种类合成的标签（老表没有 text 键，必须走这一支）。
                if (!string.IsNullOrEmpty(option.Text))
                {
                    var textOnly = option.Kind == 0;
                    items.Add(new MenuItem(
                        textOnly || levelOk ? option.Text : option.Text + "（等级不足）",
                        option.Kind,
                        textOnly || levelOk));
                    continue;
                }

                switch (option.Kind)
                {
                    case 1:
                        items.Add(new MenuItem(levelOk ? "看看货架" : "看看货架（等级不足）", 1, levelOk));
                        break;
                    case 2:
                        items.Add(new MenuItem(levelOk ? "请教武艺" : "请教武艺（等级不足）", 2, levelOk));
                        break;
                    case 3:
                        items.Add(new MenuItem(levelOk ? "借用炉砧" : "借用炉砧（等级不足）", 3, levelOk));
                        break;
                    case 4:
                        items.Add(new MenuItem(levelOk ? "复命交差" : "复命交差（等级不足）", 4, levelOk));
                        break;
                    case 5:
                        items.Add(new MenuItem("还有别的事（尚未开放）", 5, false));
                        break;
                    default:
                        items.Add(new MenuItem("闲谈两句", 0, true));
                        break;
                }
            }

            return items;
        }

        /// <summary>执行一个菜单项：**只**发起请求与导航，不本地改状态。</summary>
        private void Dispatch(NpcGossipService.Request request, int kind, string authoredText)
        {
            if (request == null) return;

            switch (kind)
            {
                case 1:
                    Network.ShopClientService.Instance.RequestOpen(request.EntityId);
                    NavigationRequested?.Invoke(InkPageDomIds.NavSocialShop);
                    break;
                case 2:
                    Network.TrainerClientService.Instance.Bind(request.EntityId, request.NpcId);
                    NavigationRequested?.Invoke(InkPageDomIds.NavSkillTree);
                    break;
                case 3:
                    Network.CraftClientService.Instance.RequestList(request.EntityId);
                    NavigationRequested?.Invoke(InkPageDomIds.NavCrafting);
                    break;
                case 4:
                    Network.QuestClientService.Instance.RequestList(request.EntityId);
                    NavigationRequested?.Invoke(InkPageDomIds.NavQuests);
                    break;
                case 0:
                    UIHelper.ShowInfo(string.IsNullOrEmpty(authoredText)
                        ? "他低声说了几句，此处暂无可办之事。"
                        : authoredText);
                    break;
                default:
                    UIHelper.ShowInfo("此事尚未开放。");
                    break;
            }
        }

        private void LayoutButtons()
        {
            for (var i = 0; i < _buttons.Count; i++)
            {
                _buttons[i].Location = new Float2((PanelWidth - ButtonWidth) * 0.5f, 86f + i * (ButtonHeight + ButtonGap));
            }

            _closeButton.Location = new Float2(
                (PanelWidth - ButtonWidth) * 0.5f,
                86f + _buttons.Count * (ButtonHeight + ButtonGap) + 6f);

            _hintLabel.Location = new Float2(0f, _panel.Size.Y - 28f);
        }

        /// <inheritdoc />
        public void RefreshLayout()
        {
            if (_panel == null) return;

            float w = Width > 0f ? Width : 1920f;
            float h = Height > 0f ? Height : 1080f;

            _panel.Location = new Float2((w - PanelWidth) * 0.5f, (h - _panel.Size.Y) * 0.5f);
            _nameLabel.Location = new Float2(0f, 22f);
            LayoutButtons();
        }

        /// <inheritdoc />
        public override void OnParentResized()
        {
            base.OnParentResized();
            RefreshLayout();
        }
    }
}
