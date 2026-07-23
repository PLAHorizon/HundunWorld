using FlaxEngine;
using FlaxEngine.GUI;
using Game.Character.Attributes;
using HundunWorld.Game.UI.StyleSystem;
using System;

namespace HundunWorld.Game.UI.Ink.Pages.Quest
{
    /// <summary>
    /// 任务日志页面 — 对应设计方案 quest-log.html。
    /// 1400x900 居中面板：顶栏（标题+分类Tab+关闭）+ 左栏（450px 任务分组列表+总进度）
    /// + 右栏（任务详情：标题/描述卷轴/目标/奖励/操作按钮）。
    /// 严格遵循水墨主题 Token，禁止硬编码色值。
    /// </summary>
    public class QuestLogPage : ContainerControl, IInkPage
    {
        private static readonly Float2 MainPanelSize = new Float2(1400f, 900f);
        private const float HeaderHeight = 88f;
        private const float TitleRowHeight = 38f;
        private const float TabRowHeight = 40f;
        private const float LeftWidth = 450f;
        private const float GroupHeaderHeight = 32f;
        private const float QuestItemHeight = 40f;
        private const float Pad = 12f;
        private const float DetailPadX = 28f;
        private const float BottomProgressHeight = 64f;

        public event Action<string> NavigationRequested;
        public InkParticleSystem ParticleSystem { get; set; }

        private CharacterAttributesComponent _boundCharacter;

        private InkPanelElevated _mainPanel;
        private QTab[] _tabs;
        private InkButton _closeBtn;

        // 左栏
        private ContainerControl _leftCol;
        private ContainerControl _groupList;

        // 右栏
        private ContainerControl _rightCol;

        public void BindCharacter(CharacterAttributesComponent component)
        {
            _boundCharacter = component;
        }

        public QuestLogPage()
        {
            try
            {
                AnchorPreset = AnchorPresets.StretchAll;
                Offsets = Margin.Zero;
                BackgroundColor = InkWashTheme.Scrim;
                ClipChildren = false;
                AutoFocus = false;

                BuildMainPanel();
                BuildHeader();
                BuildLeftColumn();
                BuildRightColumn();
            }
            catch (Exception ex)
            {
                FlaxEngine.Debug.LogError($"[QuestLogPage] init failed: {ex.Message}");
            }
        }

        private void BuildMainPanel()
        {
            _mainPanel = new InkPanelElevated
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Size = MainPanelSize,
            };
            AddChild(_mainPanel);
        }

        // ===================================================================
        // 顶栏：标题 + QUEST LOG + 关闭 + 分类Tab
        // ===================================================================

        private void BuildHeader()
        {
            var header = new ContainerControl
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = Float2.Zero,
                Size = new Float2(MainPanelSize.X, HeaderHeight),
                BackgroundColor = VoidBg(0.40f),
            };
            _mainPanel.AddChild(header);

            // 底部 gold-subtle 边线
            header.AddChild(new ContainerControl
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(0f, HeaderHeight - 1f),
                Size = new Float2(MainPanelSize.X, 1f),
                BackgroundColor = InkWashTheme.BorderGoldSubtle,
            });

            // 标题图标块（代替 scroll-text 图标）
            header.AddChild(new ContainerControl
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(24f, 14f + 4f),
                Size = new Float2(20f, 20f),
                BackgroundColor = InkWashTheme.GoldPrimary,
            });
            header.AddChild(MakeLabel("任务日志", 56f, 14f, 160f, TitleRowHeight - 8f,
                InkWashTheme.GoldPrimary, 22f, InkWashTheme.FontRole.Display, TextAlignment.Near));
            header.AddChild(MakeLabel("QUEST LOG", 224f, 14f + 8f, 120f, TitleRowHeight - 16f,
                InkWashTheme.TextTertiary, 11f, InkWashTheme.FontRole.Body, TextAlignment.Near));

            // 关闭按钮（32x32 ghost）
            _closeBtn = new InkButton
            {
                Variant = InkButtonVariant.Ghost,
                ButtonSize = InkButtonSize.Md,
                Text = "✕",
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(MainPanelSize.X - 24f - 32f, 14f),
                Size = new Float2(32f, 32f),
            };
            _closeBtn.ButtonClicked += (b) => NavigationRequested?.Invoke(InkPageDomIds.CombatHud);
            header.AddChild(_closeBtn);

            // 分类 Tab（gap 36px）
            var tabDefs = new (string name, string count, bool zero)[]
            {
                ("主线", "1", false),
                ("支线", "1", false),
                ("日常", "1", false),
                ("周常", "0", true),
                ("活动", "0", true),
            };
            _tabs = new QTab[tabDefs.Length];
            float tabX = 24f;
            float tabTop = 14f + TitleRowHeight + 10f;
            for (int i = 0; i < tabDefs.Length; i++)
            {
                var t = tabDefs[i];
                var tab = new QTab(t.name, t.count, t.zero, i == 0)
                {
                    AnchorPreset = AnchorPresets.TopLeft,
                    Location = new Float2(tabX, tabTop),
                    Size = new Float2(80f, TabRowHeight),
                };
                int idx = i;
                tab.Clicked += () => SelectTab(idx);
                _tabs[i] = tab;
                header.AddChild(tab);
                tabX += 80f + 36f;
            }
        }

        private void SelectTab(int index)
        {
            for (int i = 0; i < _tabs.Length; i++)
                _tabs[i].IsActive = (i == index);
        }

        // ===================================================================
        // 左栏：任务分组列表 + 总进度（450px）
        // ===================================================================

        private void BuildLeftColumn()
        {
            _leftCol = new ContainerControl
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(0f, HeaderHeight),
                Size = new Float2(LeftWidth, MainPanelSize.Y - HeaderHeight),
                BackgroundColor = InkBg(0.92f),
            };
            _mainPanel.AddChild(_leftCol);

            // 分组列表区
            _groupList = new ContainerControl
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = Float2.Zero,
                Size = new Float2(LeftWidth, MainPanelSize.Y - HeaderHeight - BottomProgressHeight),
                BackgroundColor = Color.Transparent,
            };
            _leftCol.AddChild(_groupList);

            float innerW = LeftWidth - Pad * 2f;
            float cy = Pad;

            // 主线任务（3）
            cy = BuildGroup(cy, innerW, "主线任务", "3", new (string name, string prog, string state)[]
            {
                ("初入江湖", "进行中 · 2/5", "active"),
                ("拜师学艺", "未开始", "pending"),
                ("江湖初探", "未开始", "pending"),
            });

            // 支线任务（2）
            cy = BuildGroup(cy, innerW, "支线任务", "2", new (string name, string prog, string state)[]
            {
                ("寻人启事", "进行中 · 1/3", "active"),
                ("采集药材", "已完成", "done"),
            });

            // 日常任务（1）
            cy = BuildGroup(cy, innerW, "日常任务", "1", new (string name, string prog, string state)[]
            {
                ("每日修行", "进行中 · 3/5", "active"),
            });

            // 周常任务（1）
            cy = BuildGroup(cy, innerW, "周常任务", "1", new (string name, string prog, string state)[]
            {
                ("门派试炼", "未开始", "pending"),
            });

            // 底部总进度
            BuildBottomProgress();
        }

        /// <summary>构建一个任务分组（组头 + 任务项），返回下一个 y 坐标。</summary>
        private float BuildGroup(float y, float innerW, string title, string count,
            (string name, string prog, string state)[] quests)
        {
            // 组头
            var groupHeader = new QuestGroupHeader(title, count)
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(Pad, y),
                Size = new Float2(innerW, GroupHeaderHeight),
            };
            _groupList.AddChild(groupHeader);
            y += GroupHeaderHeight + 4f;

            // 任务项
            for (int i = 0; i < quests.Length; i++)
            {
                var q = quests[i];
                bool isActive = q.state == "active";
                bool isDone = q.state == "done";
                bool tracked = (q.name == "初入江湖");
                var item = new QuestItem(q.name, q.prog, isActive, isDone, tracked)
                {
                    AnchorPreset = AnchorPresets.TopLeft,
                    Location = new Float2(Pad + 6f, y),
                    Size = new Float2(innerW - 12f, QuestItemHeight),
                };
                _groupList.AddChild(item);
                y += QuestItemHeight + 2f;
            }

            return y + 8f;
        }

        private void BuildBottomProgress()
        {
            float bpY = MainPanelSize.Y - HeaderHeight - BottomProgressHeight;
            var bp = new ContainerControl
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(0f, bpY),
                Size = new Float2(LeftWidth, BottomProgressHeight),
                BackgroundColor = VoidBg(0.50f),
            };
            _leftCol.AddChild(bp);

            // 顶部 gold-subtle 边线
            bp.AddChild(new ContainerControl
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = Float2.Zero,
                Size = new Float2(LeftWidth, 1f),
                BackgroundColor = InkWashTheme.BorderGoldSubtle,
            });

            bp.AddChild(MakeLabel("总进度", 16f, 10f, 100f, 18f,
                InkWashTheme.TextSecondary, 12f, InkWashTheme.FontRole.Display, TextAlignment.Near));
            bp.AddChild(MakeLabel("8", LeftWidth - 16f - 70f, 8f, 24f, 22f,
                InkWashTheme.GoldPrimary, 15f, InkWashTheme.FontRole.Number, TextAlignment.Far));
            bp.AddChild(MakeLabel("/24", LeftWidth - 16f - 44f, 12f, 44f, 18f,
                InkWashTheme.TextTertiary, 12f, InkWashTheme.FontRole.Number, TextAlignment.Near));

            // 进度条（33.3%，gold-deep → gold-primary）
            float barW = LeftWidth - 32f;
            var track = new ContainerControl
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(16f, 40f),
                Size = new Float2(barW, 4f),
                BackgroundColor = VoidBg(0.50f),
            };
            bp.AddChild(track);
            track.AddChild(new HGradientBar(0.333f, InkWashTheme.GoldDeep, InkWashTheme.GoldPrimary)
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = Float2.Zero,
                Size = new Float2(barW, 4f),
            });
        }

        // ===================================================================
        // 右栏：任务详情
        // ===================================================================

        private void BuildRightColumn()
        {
            float rightX = LeftWidth + 1f;
            float rightW = MainPanelSize.X - rightX;
            _rightCol = new ContainerControl
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(rightX, HeaderHeight),
                Size = new Float2(rightW - 1f, MainPanelSize.Y - HeaderHeight),
                BackgroundColor = AbyssBg(0.40f),
            };
            _mainPanel.AddChild(_rightCol);

            float innerW = rightW - DetailPadX * 2f;
            float cy = 20f;

            // ── 标题 + meta + 标签 ──
            _rightCol.AddChild(MakeLabel("初入江湖", DetailPadX, cy, 400f, 40f,
                InkWashTheme.GoldPrimary, 28f, InkWashTheme.FontRole.Display, TextAlignment.Near));
            _rightCol.AddChild(MakeLabel("◎ 开封城   ◆ 王铁匠   ◌ 无时限", DetailPadX, cy + 46f, 420f, 16f,
                InkWashTheme.TextSecondary, 11f, InkWashTheme.FontRole.Body, TextAlignment.Near));

            // 标签：主线（brand）+ 普通（neutral）
            var mainTag = new TagPill("主线", InkWashTheme.TextOnBrand,
                InkWashTheme.GoldPrimary, InkWashTheme.GoldPrimary)
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(DetailPadX + innerW - 60f - 8f - 60f, cy + 8f),
                Size = new Float2(60f, 22f),
            };
            _rightCol.AddChild(mainTag);
            var normalTag = new TagPill("普通", InkWashTheme.TextSecondary,
                InkWashTheme.BorderFaint, VoidBg(0.40f))
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(DetailPadX + innerW - 60f, cy + 8f),
                Size = new Float2(60f, 22f),
            };
            _rightCol.AddChild(normalTag);
            cy += 46f + 16f + 16f;

            // ── 描述卷轴（纸色 + 角饰）──
            var scroll = new InkScrollBox
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(DetailPadX, cy),
                Size = new Float2(innerW, 110f),
            };
            _rightCol.AddChild(scroll);
            scroll.AddChild(MakeLabel(
                "你初到开封城，听闻城中有位隐世高人，身怀绝世武学。前往城中各处探访，拜访各派长老，了解武林格局，或可寻得机缘。城郊山贼为患，亦可借此磨砺武艺。",
                20f, 16f, innerW - 40f, 78f,
                InkWashTheme.TextDefault, 13f, InkWashTheme.FontRole.Body, TextAlignment.Near));
            cy += 110f + 20f;

            // ── 任务目标 ──
            cy = BuildSectionTitle(cy, "任务目标");
            var obj1 = new QuestObjective("前往开封城", "已完成", "done", -1f)
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(DetailPadX, cy),
                Size = new Float2(innerW, 38f),
            };
            _rightCol.AddChild(obj1);
            cy += 38f + 8f;

            var obj2 = new QuestObjective("与NPC对话", "进行中", "active", -1f)
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(DetailPadX, cy),
                Size = new Float2(innerW, 38f),
            };
            _rightCol.AddChild(obj2);
            cy += 38f + 8f;

            var obj3 = new QuestObjective("击败山贼", "0/5", "active", 0.0f)
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(DetailPadX, cy),
                Size = new Float2(innerW, 48f),
            };
            _rightCol.AddChild(obj3);
            cy += 48f + 20f;

            // ── 任务奖励 ──
            cy = BuildSectionTitle(cy, "任务奖励");
            var rewards = new ContainerControl
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(DetailPadX, cy),
                Size = new Float2(innerW, 64f),
                BackgroundColor = Color.Transparent,
            };
            _rightCol.AddChild(rewards);
            rewards.AddChild(new QBox(InkBg(0.60f), InkWashTheme.BorderGoldSubtle, 6f));

            // 经验
            BuildReward(rewards, 16f, "经", InkWashTheme.JadeBright, "经验", "+5000", false);
            rewards.AddChild(new ContainerControl
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(160f, 18f),
                Size = new Float2(1f, 28f),
                BackgroundColor = InkWashTheme.Divider,
            });
            // 银两
            BuildReward(rewards, 176f, "银", InkWashTheme.GoldBright, "银两", "+200", false);
            rewards.AddChild(new ContainerControl
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(320f, 18f),
                Size = new Float2(1f, 28f),
                BackgroundColor = InkWashTheme.Divider,
            });
            // 装备（品质边框）
            BuildReward(rewards, 336f, "盾", InkWashTheme.QualityUncommon, "装备", "精铁护腕", true);
            cy += 64f + 24f;

            // ── 操作按钮行 ──
            var actionLine = new ContainerControl
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(DetailPadX, cy),
                Size = new Float2(innerW, 1f),
                BackgroundColor = InkWashTheme.BorderFaint,
            };
            _rightCol.AddChild(actionLine);
            cy += 16f;

            var abandonBtn = new InkButton
            {
                Variant = InkButtonVariant.Danger,
                ButtonSize = InkButtonSize.Md,
                Text = "放弃任务",
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(DetailPadX, cy),
                Size = new Float2(100f, 32f),
            };
            _rightCol.AddChild(abandonBtn);
            var trackBtn = new InkButton
            {
                Variant = InkButtonVariant.Brand,
                ButtonSize = InkButtonSize.Md,
                Text = "取消追踪",
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(DetailPadX + 100f + 12f, cy),
                Size = new Float2(100f, 32f),
            };
            _rightCol.AddChild(trackBtn);
            _rightCol.AddChild(MakeLabel("任务等级 Lv.1", DetailPadX + innerW - 140f, cy + 6f, 140f, 20f,
                InkWashTheme.TextTertiary, 11f, InkWashTheme.FontRole.Body, TextAlignment.Far));
        }

        /// <summary>区段标题（14px 金色 + 左侧 3px 金色装饰条）。</summary>
        private float BuildSectionTitle(float y, string title)
        {
            _rightCol.AddChild(new ContainerControl
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(DetailPadX, y + 3f),
                Size = new Float2(3f, 14f),
                BackgroundColor = InkWashTheme.GoldPrimary,
            });
            _rightCol.AddChild(MakeLabel(title, DetailPadX + 10f, y, 200f, 20f,
                InkWashTheme.GoldPrimary, 14f, InkWashTheme.FontRole.Display, TextAlignment.Near));
            return y + 20f + 10f;
        }

        /// <summary>奖励条目（32x32 图标 + 标签 + 数值，可选品质边框）。</summary>
        private void BuildReward(ContainerControl parent, float x, string glyph, Color color,
            string label, string value, bool itemBorder)
        {
            var icon = new RewardIcon(glyph, color, itemBorder)
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(x, 16f),
            };
            parent.AddChild(icon);
            parent.AddChild(MakeLabel(label, x + 40f, 14f, 80f, 14f,
                itemBorder ? color : InkWashTheme.TextTertiary, 10f, InkWashTheme.FontRole.Body, TextAlignment.Near));
            parent.AddChild(MakeLabel(value, x + 40f, 30f, 100f, 18f,
                itemBorder ? color : InkWashTheme.TextDefault, 13f, InkWashTheme.FontRole.Number, TextAlignment.Near));
        }

        // ===================================================================
        // 辅助色（内联 alpha，均派生自主题 Token）
        // ===================================================================

        private static Color VoidBg(float alpha)
        {
            var c = InkWashTheme.Void;
            return new Color(c.R, c.G, c.B, alpha);
        }

        private static Color InkBg(float alpha)
        {
            var c = InkWashTheme.BaseSecondary;
            return new Color(c.R, c.G, c.B, alpha);
        }

        private static Color AbyssBg(float alpha)
        {
            var c = InkWashTheme.Abyss;
            return new Color(c.R, c.G, c.B, alpha);
        }

        private Label MakeLabel(string text, float x, float y, float w, float h,
            Color color, float fontSize, InkWashTheme.FontRole role, TextAlignment hAlign)
        {
            return new Label
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(x, y),
                Size = new Float2(w, h),
                Text = text,
                TextColor = color,
                Font = InkRenderHelper.GetFontRef(role, fontSize),
                HorizontalAlignment = hAlign,
                VerticalAlignment = TextAlignment.Center,
                AutoFocus = false,
            };
        }

        public void RefreshLayout()
        {
            float sw = Width;
            float sh = Height;

            if (_mainPanel != null)
            {
                float panelX = (sw - MainPanelSize.X) * 0.5f;
                float panelY = (sh - MainPanelSize.Y) * 0.5f;
                _mainPanel.Location = new Float2(
                    panelX > 0f ? panelX : 0f,
                    panelY > 0f ? panelY : 0f);
            }
        }

        public override void OnParentResized()
        {
            base.OnParentResized();
            RefreshLayout();
        }

        // ===============================================================
        // 嵌套自绘控件
        // ===============================================================

        /// <summary>自绘圆角背景 + 边框盒子。</summary>
        private sealed class QBox : Control
        {
            private readonly Color _bg;
            private readonly Color _border;
            private readonly float _radius;

            public QBox(Color bg, Color border, float radius)
            {
                _bg = bg;
                _border = border;
                _radius = radius;
                AutoFocus = false;
                AnchorPreset = AnchorPresets.StretchAll;
                Offsets = Margin.Zero;
            }

            public override void Draw()
            {
                base.Draw();
                if (!Visible) return;
                var rect = new Rectangle(Float2.Zero, Size);
                if (_bg.A > 0f)
                    InkRenderHelper.FillRoundedRectangle(rect, _radius, _bg);
                if (_border.A > 0f)
                    InkRenderHelper.DrawRoundedRectangle(rect, _radius, _border, 1f);
            }
        }

        /// <summary>分类 Tab（14px Display + 18px 圆形计数徽章 + 激活金色 2px 下划线）。</summary>
        private sealed class QTab : Control
        {
            private readonly string _name;
            private readonly string _count;
            private readonly bool _zero;
            private bool _isActive;
            private bool _isHovered;

            public event Action Clicked;
            public bool IsActive { get => _isActive; set => _isActive = value; }

            public QTab(string name, string count, bool zero, bool active)
            {
                _name = name;
                _count = count;
                _zero = zero;
                _isActive = active;
                AutoFocus = false;
            }

            public override void Draw()
            {
                base.Draw();
                if (!Visible) return;
                Color color = _isActive ? InkWashTheme.GoldBright
                    : (_isHovered ? InkWashTheme.TextDefault : InkWashTheme.TextSecondary);
                var font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Display, 14f).GetFont();
                if (font != null)
                    Render2D.DrawText(font, _name, new Rectangle(0f, 0f, Width, Height), color,
                        TextAlignment.Near, TextAlignment.Center, TextWrapping.NoWrap);

                // 计数徽章（18x18 圆）
                float bx = 34f + 9f;
                float by = Height * 0.5f - 1f;
                Color badgeBg = _zero
                    ? new Color(InkWashTheme.Void.R, InkWashTheme.Void.G, InkWashTheme.Void.B, 0.50f)
                    : new Color(InkWashTheme.GoldPrimary.R, InkWashTheme.GoldPrimary.G, InkWashTheme.GoldPrimary.B, 0.20f);
                Color badgeBorder = _zero ? InkWashTheme.BorderFaint : InkWashTheme.BorderGold;
                Color badgeText = _zero ? InkWashTheme.TextTertiary : InkWashTheme.GoldBright;
                InkRenderHelper.FillCircle(new Float2(bx, by), 9f, badgeBg);
                InkRenderHelper.DrawCircle(new Float2(bx, by), 9f, badgeBorder, 1f);
                var nf = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Number, 10f).GetFont();
                if (nf != null)
                    Render2D.DrawText(nf, _count, new Rectangle(bx - 9f, by - 9f, 18f, 18f), badgeText,
                        TextAlignment.Center, TextAlignment.Center, TextWrapping.NoWrap);

                if (_isActive)
                    Render2D.FillRectangle(new Rectangle(0f, Height - 2f, Width, 2f), InkWashTheme.GoldPrimary);
            }

            public override void OnMouseEnter(Float2 location) { _isHovered = true; base.OnMouseEnter(location); }
            public override void OnMouseLeave() { _isHovered = false; base.OnMouseLeave(); }

            public override bool OnMouseUp(Float2 location, MouseButton button)
            {
                if (button == MouseButton.Left && ContainsPoint(ref location))
                    Clicked?.Invoke();
                return base.OnMouseUp(location, button);
            }
        }

        /// <summary>任务分组组头（▼ 折叠箭头 + 标题 + 计数药丸）。</summary>
        private sealed class QuestGroupHeader : Control
        {
            private readonly string _title;
            private readonly string _count;

            public QuestGroupHeader(string title, string count)
            {
                _title = title;
                _count = count;
                AutoFocus = false;
            }

            public override void Draw()
            {
                base.Draw();
                if (!Visible) return;
                var cf = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Body, 9f).GetFont();
                if (cf != null)
                    Render2D.DrawText(cf, "▼", new Rectangle(0f, 0f, 14f, Height), InkWashTheme.TextTertiary,
                        TextAlignment.Center, TextAlignment.Center, TextWrapping.NoWrap);
                var tf = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Display, 13f).GetFont();
                if (tf != null)
                    Render2D.DrawText(tf, _title, new Rectangle(18f, 0f, 200f, Height), InkWashTheme.TextDefault,
                        TextAlignment.Near, TextAlignment.Center, TextWrapping.NoWrap);

                // 计数药丸（右对齐）
                float pw = 26f, ph = 16f;
                float px = Width - pw - 4f;
                float py = (Height - ph) * 0.5f;
                var pillRect = new Rectangle(px, py, pw, ph);
                InkRenderHelper.FillRoundedRectangle(pillRect, 8f,
                    new Color(InkWashTheme.Void.R, InkWashTheme.Void.G, InkWashTheme.Void.B, 0.40f));
                InkRenderHelper.DrawRoundedRectangle(pillRect, 8f, InkWashTheme.GoldTrace, 1f);
                var nf = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Number, 10f).GetFont();
                if (nf != null)
                    Render2D.DrawText(nf, _count, pillRect, InkWashTheme.TextSecondary,
                        TextAlignment.Center, TextAlignment.Center, TextWrapping.NoWrap);
            }
        }

        /// <summary>任务项（状态图标 + 名 + 进度 + 追踪图标；active 金底金边 / done 灰化）。</summary>
        private sealed class QuestItem : Control
        {
            private readonly string _name;
            private readonly string _prog;
            private readonly bool _isActive;
            private readonly bool _isDone;
            private readonly bool _tracked;
            private bool _isHovered;

            public event Action Clicked;

            public QuestItem(string name, string prog, bool isActive, bool isDone, bool tracked)
            {
                _name = name;
                _prog = prog;
                _isActive = isActive;
                _isDone = isDone;
                _tracked = tracked;
                AutoFocus = false;
            }

            public override void Draw()
            {
                base.Draw();
                if (!Visible) return;
                var rect = new Rectangle(Float2.Zero, Size);

                if (_isActive)
                {
                    InkRenderHelper.FillRoundedRectangle(rect, 4f, InkWashTheme.GoldTrace);
                    InkRenderHelper.DrawRoundedRectangle(rect, 4f, InkWashTheme.BorderGold, 1f);
                }
                else if (_isHovered)
                {
                    InkRenderHelper.FillRoundedRectangle(rect, 4f, InkWashTheme.BgHover);
                }

                // 状态图标
                string icon = _isActive ? "★" : (_isDone ? "✓" : "○");
                Color iconColor = _isActive ? InkWashTheme.GoldPrimary
                    : (_isDone ? InkWashTheme.JadeDim : InkWashTheme.TextTertiary);
                var sf = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Body, 13f).GetFont();
                if (sf != null)
                    Render2D.DrawText(sf, icon, new Rectangle(8f, 0f, 18f, Height), iconColor,
                        TextAlignment.Center, TextAlignment.Center, TextWrapping.NoWrap);

                // 名称
                Color nameColor = _isActive ? InkWashTheme.GoldBright
                    : (_isDone ? InkWashTheme.TextTertiary : InkWashTheme.TextDefault);
                var nf = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Display, 13f).GetFont();
                if (nf != null)
                    Render2D.DrawText(nf, _name, new Rectangle(30f, 2f, Width - 60f, Height * 0.5f), nameColor,
                        TextAlignment.Near, TextAlignment.Center, TextWrapping.NoWrap);

                // 进度文字
                var pf = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Body, 10f).GetFont();
                if (pf != null)
                    Render2D.DrawText(pf, _prog, new Rectangle(30f, Height * 0.5f, Width - 60f, Height * 0.5f - 2f),
                        InkWashTheme.TextTertiary, TextAlignment.Near, TextAlignment.Center, TextWrapping.NoWrap);

                // 追踪图标
                if (_tracked)
                {
                    var tf = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Body, 12f).GetFont();
                    if (tf != null)
                        Render2D.DrawText(tf, "◎", new Rectangle(Width - 26f, 0f, 18f, Height),
                            InkWashTheme.GoldPrimary, TextAlignment.Center, TextAlignment.Center, TextWrapping.NoWrap);
                }
            }

            public override void OnMouseEnter(Float2 location) { _isHovered = true; base.OnMouseEnter(location); }
            public override void OnMouseLeave() { _isHovered = false; base.OnMouseLeave(); }

            public override bool OnMouseUp(Float2 location, MouseButton button)
            {
                if (button == MouseButton.Left && ContainsPoint(ref location))
                    Clicked?.Invoke();
                return base.OnMouseUp(location, button);
            }
        }

        /// <summary>任务目标行（状态图标 + 文本 + 状态；barRatio>=0 时显示进度条 + 计数）。</summary>
        private sealed class QuestObjective : Control
        {
            private readonly string _text;
            private readonly string _status;
            private readonly string _state;
            private readonly float _barRatio;

            public QuestObjective(string text, string status, string state, float barRatio)
            {
                _text = text;
                _status = status;
                _state = state;
                _barRatio = barRatio;
                AutoFocus = false;
            }

            public override void Draw()
            {
                base.Draw();
                if (!Visible) return;
                bool done = _state == "done";
                bool hasBar = _barRatio >= 0f;
                float rowH = hasBar ? 22f : Height;

                // 状态图标
                string icon = done ? "✓" : "○";
                Color iconColor = done ? InkWashTheme.JadeDim : InkWashTheme.GoldPrimary;
                var sf = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Body, 13f).GetFont();
                if (sf != null)
                    Render2D.DrawText(sf, icon, new Rectangle(0f, 0f, 20f, rowH), iconColor,
                        TextAlignment.Center, TextAlignment.Center, TextWrapping.NoWrap);

                // 目标文本
                Color textColor = done ? InkWashTheme.TextTertiary : InkWashTheme.TextDefault;
                var tf = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Body, 13f).GetFont();
                if (tf != null)
                    Render2D.DrawText(tf, _text, new Rectangle(26f, 0f, Width - 150f, rowH), textColor,
                        TextAlignment.Near, TextAlignment.Center, TextWrapping.NoWrap);

                if (hasBar)
                {
                    // 计数（右对齐）
                    var cf = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Number, 11f).GetFont();
                    if (cf != null)
                        Render2D.DrawText(cf, _status, new Rectangle(Width - 70f, 0f, 70f, rowH),
                            InkWashTheme.GoldPrimary, TextAlignment.Far, TextAlignment.Center, TextWrapping.NoWrap);

                    // 进度条（4px，金渐变）
                    float barY = rowH + 6f;
                    float barW = Width - 26f;
                    Render2D.FillRectangle(new Rectangle(26f, barY, barW, 4f),
                        new Color(InkWashTheme.Void.R, InkWashTheme.Void.G, InkWashTheme.Void.B, 0.50f));
                    float fillW = barW * Mathf.Clamp(_barRatio, 0f, 1f);
                    if (fillW > 0f)
                        Render2D.FillRectangle(new Rectangle(26f, barY, fillW, 4f), InkWashTheme.GoldPrimary);
                }
                else
                {
                    // 状态文字（右对齐）
                    Color statusColor = done ? InkWashTheme.JadeDim : InkWashTheme.GoldPrimary;
                    var stf = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Body, 11f).GetFont();
                    if (stf != null)
                        Render2D.DrawText(stf, _status, new Rectangle(Width - 110f, 0f, 110f, rowH), statusColor,
                            TextAlignment.Far, TextAlignment.Center, TextWrapping.NoWrap);
                }
            }
        }

        /// <summary>奖励图标（32x32，color 15% 混纸色底 + 品质/金边 + 字符）。</summary>
        private sealed class RewardIcon : Control
        {
            private readonly string _glyph;
            private readonly Color _color;
            private readonly bool _itemBorder;

            public RewardIcon(string glyph, Color color, bool itemBorder)
            {
                _glyph = glyph;
                _color = color;
                _itemBorder = itemBorder;
                AutoFocus = false;
                Size = new Float2(32f, 32f);
            }

            public override void Draw()
            {
                base.Draw();
                if (!Visible) return;
                var rect = new Rectangle(Float2.Zero, Size);
                Color bg = Color.Lerp(InkWashTheme.BaseTertiary, _color, 0.15f);
                InkRenderHelper.FillRoundedRectangle(rect, 6f, bg);
                Color border = _itemBorder ? _color : InkWashTheme.GoldFaint;
                InkRenderHelper.DrawRoundedRectangle(rect, 6f, border, 1f);
                var font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Display, 14f).GetFont();
                if (font != null)
                    Render2D.DrawText(font, _glyph, rect, _color,
                        TextAlignment.Center, TextAlignment.Center, TextWrapping.NoWrap);
            }
        }

        /// <summary>标签药丸（radius 2，自定义文字/边框/底色）。</summary>
        private sealed class TagPill : Control
        {
            private readonly string _text;
            private readonly Color _tc;
            private readonly Color _border;
            private readonly Color _bg;

            public TagPill(string text, Color textColor, Color border, Color bg)
            {
                _text = text;
                _tc = textColor;
                _border = border;
                _bg = bg;
                AutoFocus = false;
            }

            public override void Draw()
            {
                base.Draw();
                if (!Visible) return;
                var rect = new Rectangle(Float2.Zero, Size);
                if (_bg.A > 0f)
                    InkRenderHelper.FillRoundedRectangle(rect, 2f, _bg);
                if (_border.A > 0f)
                    InkRenderHelper.DrawRoundedRectangle(rect, 2f, _border, 1f);
                var font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Display, 11f).GetFont();
                if (font != null)
                    Render2D.DrawText(font, _text, rect, _tc,
                        TextAlignment.Center, TextAlignment.Center, TextWrapping.NoWrap);
            }
        }

        /// <summary>描述卷轴（暗纸色 50% 底 + 金发丝边 + 四角金饰）。</summary>
        private sealed class InkScrollBox : ContainerControl
        {
            public InkScrollBox()
            {
                AutoFocus = false;
                BackgroundColor = Color.Transparent;
            }

            public override void Draw()
            {
                var rect = new Rectangle(Float2.Zero, Size);
                var paper = InkWashTheme.BaseTertiary;
                InkRenderHelper.FillRoundedRectangle(rect, 4f,
                    new Color(paper.R, paper.G, paper.B, 0.50f));
                InkRenderHelper.DrawRoundedRectangle(rect, 4f, InkWashTheme.BorderFaint, 1f);

                // 四角金饰（L 形 8px）
                var gold = InkWashTheme.GoldPrimary;
                var corner = new Color(gold.R, gold.G, gold.B, 0.35f);
                float w = Width, h = Height;
                Render2D.FillRectangle(new Rectangle(0f, 0f, 8f, 1f), corner);
                Render2D.FillRectangle(new Rectangle(0f, 0f, 1f, 8f), corner);
                Render2D.FillRectangle(new Rectangle(w - 8f, 0f, 8f, 1f), corner);
                Render2D.FillRectangle(new Rectangle(w - 1f, 0f, 1f, 8f), corner);
                Render2D.FillRectangle(new Rectangle(0f, h - 1f, 8f, 1f), corner);
                Render2D.FillRectangle(new Rectangle(0f, h - 8f, 1f, 8f), corner);
                Render2D.FillRectangle(new Rectangle(w - 8f, h - 1f, 8f, 1f), corner);
                Render2D.FillRectangle(new Rectangle(w - 1f, h - 8f, 1f, 8f), corner);

                base.Draw();
            }
        }

        /// <summary>水平渐变进度条（按 fillRatio 填充）。</summary>
        private sealed class HGradientBar : Control
        {
            private readonly float _fillRatio;
            private readonly Color[] _gradient;

            public HGradientBar(float fillRatio, params Color[] gradient)
            {
                _fillRatio = Mathf.Clamp(fillRatio, 0f, 1f);
                _gradient = gradient;
                AutoFocus = false;
            }

            public override void Draw()
            {
                base.Draw();
                if (!Visible) return;
                float fillW = Width * _fillRatio;
                if (fillW <= 0f || _gradient == null || _gradient.Length == 0) return;
                int steps = Mathf.Clamp(Mathf.FloorToInt(fillW), 2, 64);
                float stepW = fillW / steps;
                for (int i = 0; i < steps; i++)
                {
                    float t = steps == 1 ? 0f : (float)i / (steps - 1);
                    Render2D.FillRectangle(new Rectangle(i * stepW, 0f, stepW + 0.5f, Height), Sample(t));
                }
            }

            private Color Sample(float t)
            {
                if (_gradient.Length == 1) return _gradient[0];
                float scaled = t * (_gradient.Length - 1);
                int idx = Mathf.FloorToInt(scaled);
                if (idx >= _gradient.Length - 1) return _gradient[_gradient.Length - 1];
                return Color.Lerp(_gradient[idx], _gradient[idx + 1], scaled - idx);
            }
        }
    }
}
