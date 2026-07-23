using FlaxEngine;
using FlaxEngine.GUI;
using Game.Character.Attributes;
using HundunWorld.Game.UI.StyleSystem;
using System;

namespace HundunWorld.Game.UI.Ink.Pages.MountPet
{
    /// <summary>
    /// 坐骑灵兽面板 — 对应设计方案 mount-pet.html。
    /// 1400x900 居中面板：顶栏（双Tab+标题+返回）+ 左栏（坐骑名册列表）
    /// + 中栏（预览舞台+名称+属性网格+技能槽+典故）+ 右栏（出战/驯养/研习/幻化/移速）。
    /// 严格遵循水墨主题 Token，禁止硬编码色值。
    /// </summary>
    public class MountPetPage : ContainerControl, IInkPage
    {
        private static readonly Float2 MainPanelSize = new Float2(1400f, 900f);
        private const float HeaderHeight = 56f;
        private const float LeftWidth = 300f;
        private const float RightWidth = 340f;
        private const float ListHeaderHeight = 40f;
        private const float Pad = 12f;
        private const float MidPadX = 24f;
        private const float MidPadY = 20f;
        private const float PreviewStageHeight = 280f;
        private const float RightPad = 16f;

        public event Action<string> NavigationRequested;
        public InkParticleSystem ParticleSystem { get; set; }

        private CharacterAttributesComponent _boundCharacter;

        private InkPanelElevated _mainPanel;
        private MpTab _tabMount;
        private MpTab _tabPet;
        private InkButton _backBtn;

        // 左栏
        private ContainerControl _leftCol;
        private ContainerControl _mountList;
        private MountListItem[] _mountItems;
        private int _selectedMount = 0;

        // 中栏
        private ContainerControl _midCol;

        // 右栏
        private ContainerControl _rightCol;

        public void BindCharacter(CharacterAttributesComponent component)
        {
            _boundCharacter = component;
        }

        public MountPetPage()
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
                BuildMiddleColumn();
                BuildRightColumn();
            }
            catch (Exception ex)
            {
                FlaxEngine.Debug.LogError($"[MountPetPage] init failed: {ex.Message}");
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
        // 顶栏：双Tab（坐骑/灵兽）+ 中央标题（带分隔线）+ 返回
        // ===================================================================

        private void BuildHeader()
        {
            var header = new ContainerControl
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = Float2.Zero,
                Size = new Float2(MainPanelSize.X, HeaderHeight),
                BackgroundColor = Color.Transparent,
            };
            _mainPanel.AddChild(header);

            // 底部 faint 边线
            header.AddChild(new ContainerControl
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(0f, HeaderHeight - 1f),
                Size = new Float2(MainPanelSize.X, 1f),
                BackgroundColor = InkWashTheme.BorderFaint,
            });

            // 左侧双 Tab（gap 32px）
            _tabMount = new MpTab("坐骑", true)
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(20f, 0f),
                Size = new Float2(60f, HeaderHeight),
            };
            _tabMount.Clicked += () => SelectTab(true);
            header.AddChild(_tabMount);

            _tabPet = new MpTab("灵兽", false)
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(20f + 60f + 32f, 0f),
                Size = new Float2(60f, HeaderHeight),
            };
            _tabPet.Clicked += () => SelectTab(false);
            header.AddChild(_tabPet);

            // 中央：分隔线 + 标题 + 分隔线
            float titleW = 160f;
            float titleX = (MainPanelSize.X - titleW) * 0.5f;
            header.AddChild(new ContainerControl
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(titleX - 16f, (HeaderHeight - 24f) * 0.5f),
                Size = new Float2(1f, 24f),
                BackgroundColor = InkWashTheme.BorderGold,
            });
            header.AddChild(MakeLabel("坐骑灵兽", titleX, 0f, titleW, HeaderHeight,
                InkWashTheme.GoldPrimary, 20f, InkWashTheme.FontRole.Display, TextAlignment.Center));
            header.AddChild(new ContainerControl
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(titleX + titleW + 16f, (HeaderHeight - 24f) * 0.5f),
                Size = new Float2(1f, 24f),
                BackgroundColor = InkWashTheme.BorderGold,
            });

            // 右侧返回按钮（36x36 ghost）
            _backBtn = new InkButton
            {
                Variant = InkButtonVariant.Ghost,
                ButtonSize = InkButtonSize.Md,
                Text = "✕",
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(MainPanelSize.X - 20f - 36f, (HeaderHeight - 36f) * 0.5f),
                Size = new Float2(36f, 36f),
            };
            _backBtn.ButtonClicked += (b) => NavigationRequested?.Invoke(InkPageDomIds.CombatHud);
            header.AddChild(_backBtn);
        }

        private void SelectTab(bool mount)
        {
            _tabMount.IsActive = mount;
            _tabPet.IsActive = !mount;
        }

        // ===================================================================
        // 左栏：坐骑名册列表（300px）
        // ===================================================================

        private void BuildLeftColumn()
        {
            _leftCol = new ContainerControl
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(0f, HeaderHeight),
                Size = new Float2(LeftWidth, MainPanelSize.Y - HeaderHeight),
                BackgroundColor = Color.Transparent,
            };
            _mainPanel.AddChild(_leftCol);

            // 右边框 faint 线
            _leftCol.AddChild(new ContainerControl
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(LeftWidth - 1f, 0f),
                Size = new Float2(1f, MainPanelSize.Y - HeaderHeight),
                BackgroundColor = InkWashTheme.BorderFaint,
            });

            // 列表头（40px）：坐骑名册 + 数量
            _leftCol.AddChild(MakeLabel("坐骑名册", 16f, 0f, 160f, ListHeaderHeight,
                InkWashTheme.TextDefault, 13f, InkWashTheme.FontRole.Display, TextAlignment.Near));
            _leftCol.AddChild(MakeLabel("06", LeftWidth - 60f, 0f, 44f, ListHeaderHeight,
                InkWashTheme.TextTertiary, 12f, InkWashTheme.FontRole.Number, TextAlignment.Far));

            // 列表头底部 faint 线
            _leftCol.AddChild(new ContainerControl
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(0f, ListHeaderHeight - 1f),
                Size = new Float2(LeftWidth, 1f),
                BackgroundColor = InkWashTheme.BorderFaint,
            });

            // 坐骑列表
            _mountList = new ContainerControl
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(0f, ListHeaderHeight),
                Size = new Float2(LeftWidth, MainPanelSize.Y - HeaderHeight - ListHeaderHeight),
                BackgroundColor = Color.Transparent,
            };
            _leftCol.AddChild(_mountList);

            var mounts = new (string glyph, string name, string badge, int lvl, int speed,
                              InkWashTheme.InkQuality quality, bool deployed)[]
            {
                ("麟", "墨麒麟",   "出战", 85, 320, InkWashTheme.InkQuality.Legendary, true),
                ("骓", "踏雪乌骓", "史诗", 72, 280, InkWashTheme.InkQuality.Epic,      false),
                ("鹤", "雪羽鹤",   "飞禽", 68, 265, InkWashTheme.InkQuality.Epic,      false),
                ("骢", "青骢追风", "稀有", 60, 250, InkWashTheme.InkQuality.Rare,      false),
                ("马", "黄骠马",   "良好", 45, 210, InkWashTheme.InkQuality.Uncommon,  false),
                ("鹿", "雪原驯鹿", "普通", 30, 180, InkWashTheme.InkQuality.Common,    false),
            };

            _mountItems = new MountListItem[mounts.Length];
            float itemW = LeftWidth - Pad * 2f;
            float itemH = 60f;
            float cy = Pad;
            for (int i = 0; i < mounts.Length; i++)
            {
                var m = mounts[i];
                var item = new MountListItem(m.glyph, m.name, m.badge, m.lvl, m.speed, m.quality, m.deployed)
                {
                    AnchorPreset = AnchorPresets.TopLeft,
                    Location = new Float2(Pad, cy),
                    Size = new Float2(itemW, itemH),
                };
                int idx = i;
                item.Clicked += () => SelectMount(idx);
                _mountItems[i] = item;
                _mountList.AddChild(item);
                cy += itemH + 6f;
            }
        }

        private void SelectMount(int index)
        {
            _selectedMount = index;
            for (int i = 0; i < _mountItems.Length; i++)
                _mountItems[i].IsSelected = (i == index);
        }

        // ===================================================================
        // 中栏：预览舞台 + 名称 + 属性网格 + 技能槽 + 典故
        // ===================================================================

        private void BuildMiddleColumn()
        {
            float midW = MainPanelSize.X - LeftWidth - RightWidth;
            _midCol = new ContainerControl
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(LeftWidth, HeaderHeight),
                Size = new Float2(midW, MainPanelSize.Y - HeaderHeight),
                BackgroundColor = Color.Transparent,
            };
            _mainPanel.AddChild(_midCol);

            float innerW = midW - MidPadX * 2f;
            float cy = MidPadY;

            // ── 预览舞台（280px）──
            var stage = new PreviewStage("麟", "传说")
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(MidPadX, cy),
                Size = new Float2(innerW, PreviewStageHeight),
            };
            _midCol.AddChild(stage);
            cy += PreviewStageHeight + 16f;

            // ── 名称行 ──
            _midCol.AddChild(MakeLabel("墨麒麟", MidPadX, cy, 300f, 36f,
                InkWashTheme.TextDefault, 28f, InkWashTheme.FontRole.Display, TextAlignment.Near));
            _midCol.AddChild(MakeLabel("瑞兽 · 异兽类 · 已驯服", MidPadX, cy + 40f, 320f, 18f,
                InkWashTheme.TextTertiary, 12f, InkWashTheme.FontRole.Body, TextAlignment.Near));

            // 右侧标签：五行 · 金 + 坐骑
            var wuxingTag = new TagPill("五行 · 金", InkWashTheme.ElementMetal,
                InkWashTheme.ElementMetal, MetalFaintBg())
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(MidPadX + innerW - 90f - 8f - 64f, cy + 8f),
                Size = new Float2(90f, 22f),
            };
            _midCol.AddChild(wuxingTag);
            var typeTag = new TagPill("坐骑", InkWashTheme.TextSecondary,
                InkWashTheme.BorderFaint, InkWashTheme.BaseTertiary)
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(MidPadX + innerW - 64f, cy + 8f),
                Size = new Float2(64f, 22f),
            };
            _midCol.AddChild(typeTag);
            cy += 40f + 18f + 16f;

            // ── 属性网格（3列）──
            float attrGap = 12f;
            float attrW = (innerW - attrGap * 2f) / 3f;
            float attrH = 84f;
            BuildAttrCard(_midCol, MidPadX, cy, attrW, attrH,
                "速度", "320", "尺/息", InkWashTheme.GoldPrimary, InkWashTheme.GoldBright, -1f);
            BuildAttrCard(_midCol, MidPadX + attrW + attrGap, cy, attrW, attrH,
                "耐力", "4500", "/4500", InkWashTheme.JadeBright, InkWashTheme.TextDefault, 1.0f);
            BuildAttrCard(_midCol, MidPadX + (attrW + attrGap) * 2f, cy, attrW, attrH,
                "跳跃力", "8.5", "丈", InkWashTheme.BloodBright, InkWashTheme.TextDefault, -1f);
            cy += attrH + 16f;

            // ── 技能槽（3）──
            _midCol.AddChild(MakeLabel("特殊技能", MidPadX, cy, 120f, 20f,
                InkWashTheme.TextDefault, 13f, InkWashTheme.FontRole.Display, TextAlignment.Near));
            _midCol.AddChild(new ContainerControl
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(MidPadX + 120f + 8f, cy + 9f),
                Size = new Float2(innerW - 120f - 8f - 60f, 1f),
                BackgroundColor = InkWashTheme.Divider,
            });
            _midCol.AddChild(MakeLabel("2 / 3", MidPadX + innerW - 60f, cy, 60f, 20f,
                InkWashTheme.TextTertiary, 11f, InkWashTheme.FontRole.Number, TextAlignment.Far));
            cy += 28f;

            float slotSize = 52f;
            float slotH = 64f;
            float slotGap = 12f;
            var slot1 = new SkillSlot("火", "踏火穿云", true)
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(MidPadX, cy),
                Size = new Float2(slotSize, slotH),
            };
            _midCol.AddChild(slot1);
            var slot2 = new SkillSlot("撞", "神威冲撞", true)
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(MidPadX + slotSize + slotGap, cy),
                Size = new Float2(slotSize, slotH),
            };
            _midCol.AddChild(slot2);
            var slot3 = new SkillSlot("+", "未学习", false)
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(MidPadX + (slotSize + slotGap) * 2f, cy),
                Size = new Float2(slotSize, slotH),
            };
            _midCol.AddChild(slot3);

            // 技能详情框
            float detailX = MidPadX + (slotSize + slotGap) * 3f;
            float detailW = innerW - (slotSize + slotGap) * 3f;
            var detail = new ContainerControl
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(detailX, cy),
                Size = new Float2(detailW, slotH),
                BackgroundColor = Color.Transparent,
            };
            _midCol.AddChild(detail);
            detail.AddChild(new MpBox(InkWashTheme.BaseTertiary, InkWashTheme.BorderFaint, 4f));
            detail.AddChild(MakeLabel("踏火穿云", 10f, 6f, 140f, 18f,
                InkWashTheme.GoldBright, 12f, InkWashTheme.FontRole.Display, TextAlignment.Near));
            detail.AddChild(MakeLabel("Lv.3", detailW - 60f, 6f, 50f, 18f,
                InkWashTheme.TextTertiary, 11f, InkWashTheme.FontRole.Number, TextAlignment.Far));
            detail.AddChild(MakeLabel("奔腾时蹄生烈焰，无视减速地形，持续 8 息。", 10f, 28f, detailW - 20f, 30f,
                InkWashTheme.TextSecondary, 11f, InkWashTheme.FontRole.Body, TextAlignment.Near));
            cy += slotH + 16f;

            // ── 典故 ──
            var lore = new ContainerControl
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(MidPadX, cy),
                Size = new Float2(innerW, 56f),
                BackgroundColor = InkWashTheme.BgMist,
            };
            _midCol.AddChild(lore);
            lore.AddChild(new ContainerControl
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = Float2.Zero,
                Size = new Float2(2f, 56f),
                BackgroundColor = InkWashTheme.GoldFaint,
            });
            lore.AddChild(MakeLabel("麒麟踏火而出，墨鳞如甲，乃瑞兽之首。性烈而忠，唯修为深厚者可驭。",
                14f, 8f, innerW - 26f, 40f,
                InkWashTheme.TextSecondary, 12f, InkWashTheme.FontRole.Display, TextAlignment.Near));
        }

        /// <summary>属性卡片（border faint + paper 底 + 图标色标签 + 大数值 + 可选进度条）。</summary>
        private void BuildAttrCard(ContainerControl parent, float x, float y, float w, float h,
            string label, string value, string unit, Color iconColor, Color valueColor, float barRatio)
        {
            var card = new ContainerControl
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(x, y),
                Size = new Float2(w, h),
                BackgroundColor = Color.Transparent,
            };
            parent.AddChild(card);
            card.AddChild(new MpBox(InkWashTheme.BaseTertiary, InkWashTheme.BorderFaint, 4f));

            // 图标色块（代替 lucide 图标）
            card.AddChild(new ContainerControl
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(12f, 13f),
                Size = new Float2(10f, 10f),
                BackgroundColor = iconColor,
            });
            card.AddChild(MakeLabel(label, 28f, 10f, w - 40f, 16f,
                InkWashTheme.TextTertiary, 11f, InkWashTheme.FontRole.Body, TextAlignment.Near));

            card.AddChild(MakeLabel(value, 12f, 30f, 140f, 26f,
                valueColor, 24f, InkWashTheme.FontRole.Number, TextAlignment.Near));
            card.AddChild(MakeLabel(unit, 12f + 90f, 38f, w - 110f, 18f,
                InkWashTheme.TextTertiary, 11f, InkWashTheme.FontRole.Body, TextAlignment.Near));

            if (barRatio >= 0f)
            {
                float barW = w - 24f;
                var track = new ContainerControl
                {
                    AnchorPreset = AnchorPresets.TopLeft,
                    Location = new Float2(12f, h - 16f),
                    Size = new Float2(barW, 6f),
                    BackgroundColor = AbyssTrack(),
                };
                card.AddChild(track);
                track.AddChild(new HGradientBar(barRatio, InkWashTheme.GoldDeep, InkWashTheme.GoldBright)
                {
                    AnchorPreset = AnchorPresets.TopLeft,
                    Location = Float2.Zero,
                    Size = new Float2(barW, 6f),
                });
            }
        }

        // ===================================================================
        // 右栏：出战 / 驯养喂养 / 技能研习 / 外观幻化 / 移速加成（340px）
        // ===================================================================

        private void BuildRightColumn()
        {
            float rightX = MainPanelSize.X - RightWidth;
            _rightCol = new ContainerControl
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(rightX, HeaderHeight),
                Size = new Float2(RightWidth, MainPanelSize.Y - HeaderHeight),
                BackgroundColor = Color.Transparent,
            };
            _mainPanel.AddChild(_rightCol);

            // 左边框 faint 线
            _rightCol.AddChild(new ContainerControl
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = Float2.Zero,
                Size = new Float2(1f, MainPanelSize.Y - HeaderHeight),
                BackgroundColor = InkWashTheme.BorderFaint,
            });

            float innerW = RightWidth - RightPad * 2f;
            float cy = RightPad;

            // ── 出战状态卡 ──
            var deploy = new ContainerControl
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(RightPad, cy),
                Size = new Float2(innerW, 86f),
                BackgroundColor = Color.Transparent,
            };
            _rightCol.AddChild(deploy);
            deploy.AddChild(new MpBox(GoldPaperBg(), InkWashTheme.BorderGold, 4f));

            // 金色状态点
            deploy.AddChild(new ContainerControl
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(12f, 17f),
                Size = new Float2(8f, 8f),
                BackgroundColor = InkWashTheme.GoldBright,
            });
            deploy.AddChild(MakeLabel("出战中", 26f, 12f, 100f, 20f,
                InkWashTheme.GoldBright, 13f, InkWashTheme.FontRole.Display, TextAlignment.Near));

            var restBtn = new DeployToggle("休息")
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(innerW - 12f - 72f, 10f),
                Size = new Float2(72f, 28f),
            };
            deploy.AddChild(restBtn);

            deploy.AddChild(MakeLabel("出战期间持续消耗耐力，归厩后自动恢复。", 12f, 44f, innerW - 24f, 32f,
                InkWashTheme.TextTertiary, 11f, InkWashTheme.FontRole.Body, TextAlignment.Near));
            cy += 86f + 14f;

            // ── 驯养喂养 ──
            _rightCol.AddChild(MakeLabel("驯养喂养", RightPad, cy, 120f, 20f,
                InkWashTheme.TextDefault, 13f, InkWashTheme.FontRole.Display, TextAlignment.Near));
            cy += 28f;

            float halfW = (innerW - 8f) * 0.5f;
            var feedBtn = new InkButton
            {
                Variant = InkButtonVariant.Secondary,
                ButtonSize = InkButtonSize.Md,
                Text = "喂养",
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(RightPad, cy),
                Size = new Float2(halfW, 34f),
            };
            _rightCol.AddChild(feedBtn);
            var trainBtn = new InkButton
            {
                Variant = InkButtonVariant.Primary,
                ButtonSize = InkButtonSize.Md,
                Text = "驯养",
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(RightPad + halfW + 8f, cy),
                Size = new Float2(halfW, 34f),
            };
            _rightCol.AddChild(trainBtn);
            cy += 34f + 8f;

            // 消耗材料行
            var costRow = new ContainerControl
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(RightPad, cy),
                Size = new Float2(innerW, 32f),
                BackgroundColor = Color.Transparent,
            };
            _rightCol.AddChild(costRow);
            costRow.AddChild(new MpBox(InkWashTheme.BaseTertiary, InkWashTheme.BorderFaint, 4f));
            costRow.AddChild(MakeLabel("消耗材料", 10f, 0f, 80f, 32f,
                InkWashTheme.TextTertiary, 11f, InkWashTheme.FontRole.Body, TextAlignment.Near));
            costRow.AddChild(MakeLabel("灵草 ×3", innerW - 170f, 0f, 70f, 32f,
                InkWashTheme.JadeBright, 11f, InkWashTheme.FontRole.Number, TextAlignment.Near));
            costRow.AddChild(MakeLabel("驯兽丹 ×1", innerW - 90f, 0f, 80f, 32f,
                InkWashTheme.GoldPrimary, 11f, InkWashTheme.FontRole.Number, TextAlignment.Near));
            cy += 32f + 14f;

            // ── 技能研习 ──
            _rightCol.AddChild(MakeLabel("技能研习", RightPad, cy, 120f, 20f,
                InkWashTheme.TextDefault, 13f, InkWashTheme.FontRole.Display, TextAlignment.Near));
            _rightCol.AddChild(new ContainerControl
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(RightPad + 120f + 8f, cy + 9f),
                Size = new Float2(innerW - 120f - 8f, 1f),
                BackgroundColor = InkWashTheme.Divider,
            });
            cy += 28f;

            // 技能升级行
            var skillRow = new ContainerControl
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(RightPad, cy),
                Size = new Float2(innerW, 56f),
                BackgroundColor = Color.Transparent,
            };
            _rightCol.AddChild(skillRow);
            skillRow.AddChild(new MpBox(InkWashTheme.BaseTertiary, InkWashTheme.BorderFaint, 4f));

            var skillIcon = new SkillSlot("火", null, true)
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(8f, 8f),
                Size = new Float2(40f, 40f),
            };
            skillRow.AddChild(skillIcon);

            skillRow.AddChild(MakeLabel("踏火穿云", 56f, 8f, 120f, 18f,
                InkWashTheme.TextDefault, 12f, InkWashTheme.FontRole.Display, TextAlignment.Near));
            skillRow.AddChild(MakeLabel("Lv.3 → 4", innerW - 56f - 90f, 8f, 80f, 18f,
                InkWashTheme.TextTertiary, 11f, InkWashTheme.FontRole.Number, TextAlignment.Far));

            float progW = innerW - 56f - 8f - 90f;
            var progTrack = new ContainerControl
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(56f, 34f),
                Size = new Float2(progW, 6f),
                BackgroundColor = AbyssTrack(),
            };
            skillRow.AddChild(progTrack);
            progTrack.AddChild(new HGradientBar(0.72f, InkWashTheme.GoldDeep, InkWashTheme.GoldBright)
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = Float2.Zero,
                Size = new Float2(progW, 6f),
            });

            var upBtn = new InkButton
            {
                Variant = InkButtonVariant.Secondary,
                ButtonSize = InkButtonSize.Sm,
                Text = "升级",
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(innerW - 8f - 60f, 13f),
                Size = new Float2(60f, 30f),
            };
            skillRow.AddChild(upBtn);
            cy += 56f + 8f;

            var learnBtn = new InkButton
            {
                Variant = InkButtonVariant.Secondary,
                ButtonSize = InkButtonSize.Md,
                Text = "+ 学习新技能",
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(RightPad, cy),
                Size = new Float2(innerW, 34f),
            };
            _rightCol.AddChild(learnBtn);
            cy += 34f + 14f;

            // ── 外观幻化 ──
            _rightCol.AddChild(MakeLabel("外观幻化", RightPad, cy, 120f, 20f,
                InkWashTheme.TextDefault, 13f, InkWashTheme.FontRole.Display, TextAlignment.Near));
            cy += 28f;

            float morphGap = 8f;
            float morphW = (innerW - morphGap * 2f) / 3f;
            var morphs = new (string glyph, Color color, bool active)[]
            {
                ("麟", InkWashTheme.GoldBright, true),
                ("焰", InkWashTheme.BloodBright, false),
                ("霜", InkWashTheme.JadeBright, false),
            };
            for (int i = 0; i < morphs.Length; i++)
            {
                var thumb = new MorphThumb(morphs[i].glyph, morphs[i].color, morphs[i].active)
                {
                    AnchorPreset = AnchorPresets.TopLeft,
                    Location = new Float2(RightPad + i * (morphW + morphGap), cy),
                    Size = new Float2(morphW, morphW),
                };
                _rightCol.AddChild(thumb);
            }
            cy += morphW + 14f;

            // ── 移速加成 ──
            var speed = new ContainerControl
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(RightPad, cy),
                Size = new Float2(innerW, 118f),
                BackgroundColor = Color.Transparent,
            };
            _rightCol.AddChild(speed);
            speed.AddChild(new MpBox(InkWashTheme.BaseTertiary, InkWashTheme.BorderFaint, 4f));

            speed.AddChild(new ContainerControl
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(12f, 15f),
                Size = new Float2(10f, 10f),
                BackgroundColor = InkWashTheme.GoldPrimary,
            });
            speed.AddChild(MakeLabel("移速加成", 28f, 12f, 120f, 18f,
                InkWashTheme.TextDefault, 13f, InkWashTheme.FontRole.Display, TextAlignment.Near));

            speed.AddChild(MakeLabel("基础移速", 12f, 38f, 80f, 14f,
                InkWashTheme.TextTertiary, 10f, InkWashTheme.FontRole.Body, TextAlignment.Near));
            speed.AddChild(MakeLabel("100", 12f, 52f, 80f, 20f,
                InkWashTheme.TextSecondary, 16f, InkWashTheme.FontRole.Number, TextAlignment.Near));

            speed.AddChild(MakeLabel("→", 100f, 52f, 30f, 20f,
                InkWashTheme.TextTertiary, 14f, InkWashTheme.FontRole.Body, TextAlignment.Center));

            speed.AddChild(MakeLabel("骑乘移速", 136f, 38f, 100f, 14f,
                InkWashTheme.TextTertiary, 10f, InkWashTheme.FontRole.Body, TextAlignment.Near));
            speed.AddChild(MakeLabel("160", 136f, 52f, 60f, 22f,
                InkWashTheme.GoldBright, 20f, InkWashTheme.FontRole.Number, TextAlignment.Near));
            speed.AddChild(MakeLabel("+60%", 136f + 60f, 56f, 60f, 18f,
                InkWashTheme.JadeBright, 12f, InkWashTheme.FontRole.Number, TextAlignment.Near));

            float speedBarW = innerW - 24f;
            var speedTrack = new ContainerControl
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(12f, 80f),
                Size = new Float2(speedBarW, 6f),
                BackgroundColor = AbyssTrack(),
            };
            speed.AddChild(speedTrack);
            speedTrack.AddChild(new HGradientBar(0.80f, InkWashTheme.GoldDeep, InkWashTheme.GoldBright)
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = Float2.Zero,
                Size = new Float2(speedBarW, 6f),
            });

            speed.AddChild(MakeLabel("骑乘后角色移动速度提升，受耐力与地形影响。", 12f, 92f, innerW - 24f, 20f,
                InkWashTheme.TextTertiary, 11f, InkWashTheme.FontRole.Body, TextAlignment.Near));
        }

        // ===================================================================
        // 辅助
        // ===================================================================

        /// <summary>金属性淡底（element-metal 12% 透明）。</summary>
        private static Color MetalFaintBg()
        {
            var c = InkWashTheme.ElementMetal;
            return new Color(c.R, c.G, c.B, 0.12f);
        }

        /// <summary>进度条轨道底（abyss 70% 透明）。</summary>
        private static Color AbyssTrack()
        {
            var c = InkWashTheme.Abyss;
            return new Color(c.R, c.G, c.B, 0.70f);
        }

        /// <summary>出战卡背景（gold 6% + paper 混合，近似 gold 6% 透明）。</summary>
        private static Color GoldPaperBg()
        {
            var c = InkWashTheme.GoldPrimary;
            return new Color(c.R, c.G, c.B, 0.06f);
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

        /// <summary>自绘圆角背景 + 边框（StretchAll）。</summary>
        private sealed class MpBox : Control
        {
            private readonly Color _bg;
            private readonly Color _border;
            private readonly float _radius;

            public MpBox(Color bg, Color border, float radius)
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

        /// <summary>顶栏 Tab（16px Display，激活金亮 + 2px 金色下划线）。</summary>
        private sealed class MpTab : Control
        {
            private readonly string _text;
            private bool _isActive;
            private bool _isHovered;

            public event Action Clicked;
            public bool IsActive { get => _isActive; set => _isActive = value; }

            public MpTab(string text, bool active)
            {
                _text = text;
                _isActive = active;
                AutoFocus = false;
            }

            public override void Draw()
            {
                base.Draw();
                if (!Visible) return;
                Color color = _isActive ? InkWashTheme.GoldBright
                    : (_isHovered ? InkWashTheme.TextDefault : InkWashTheme.TextSecondary);
                var font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Display, 16f).GetFont();
                if (font != null)
                    Render2D.DrawText(font, _text, new Rectangle(Float2.Zero, Size), color,
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

        /// <summary>坐骑列表项（图标品质边框 + 名 + 徽章 + 等级/速度，可选中/悬停）。</summary>
        private sealed class MountListItem : Control
        {
            private readonly string _glyph;
            private readonly string _name;
            private readonly string _badge;
            private readonly int _lvl;
            private readonly int _speed;
            private readonly InkWashTheme.InkQuality _quality;
            private readonly bool _deployed;
            private bool _isSelected;
            private bool _isHovered;

            public event Action Clicked;
            public bool IsSelected { get => _isSelected; set => _isSelected = value; }

            public MountListItem(string glyph, string name, string badge, int lvl, int speed,
                InkWashTheme.InkQuality quality, bool deployed)
            {
                _glyph = glyph;
                _name = name;
                _badge = badge;
                _lvl = lvl;
                _speed = speed;
                _quality = quality;
                _deployed = deployed;
                _isSelected = deployed;
                AutoFocus = false;
            }

            public override void Draw()
            {
                base.Draw();
                if (!Visible) return;
                var rect = new Rectangle(Float2.Zero, Size);
                Color qc = InkWashTheme.QualityColor(_quality);

                // 行背景 + 边框
                if (_isSelected)
                {
                    InkRenderHelper.FillRoundedRectangle(rect, 4f, InkWashTheme.GoldTrace);
                    InkRenderHelper.DrawRoundedRectangle(rect, 4f, InkWashTheme.BorderGold, 1f);
                }
                else
                {
                    if (_isHovered)
                        InkRenderHelper.FillRoundedRectangle(rect, 4f, InkWashTheme.BgHover);
                }

                // 图标盒（44x44）
                var iconRect = new Rectangle(8f, 8f, 44f, 44f);
                Color iconBg = _deployed
                    ? Color.Lerp(InkWashTheme.BaseTertiary, InkWashTheme.GoldDeep, 0.20f)
                    : Color.Lerp(InkWashTheme.BaseTertiary, qc, 0.14f);
                InkRenderHelper.FillRoundedRectangle(iconRect, 4f, iconBg);
                InkRenderHelper.DrawRoundedRectangle(iconRect, 4f, _deployed ? InkWashTheme.QualityLegendary : qc,
                    _deployed ? 2f : 1f);
                var iconFont = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Display, 22f).GetFont();
                if (iconFont != null)
                    Render2D.DrawText(iconFont, _glyph, iconRect,
                        _deployed ? InkWashTheme.GoldBright : qc,
                        TextAlignment.Center, TextAlignment.Center, TextWrapping.NoWrap);

                // 名称
                var nameFont = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Display, 14f).GetFont();
                if (nameFont != null)
                    Render2D.DrawText(nameFont, _name, new Rectangle(60f, 8f, Width - 60f - 52f, 20f),
                        InkWashTheme.TextDefault, TextAlignment.Near, TextAlignment.Center, TextWrapping.NoWrap);

                // 徽章
                var badgeRect = new Rectangle(Width - 48f, 10f, 40f, 18f);
                if (_deployed)
                {
                    InkRenderHelper.FillRoundedRectangle(badgeRect, 2f, InkWashTheme.GoldPrimary);
                    var bf = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Display, 10f).GetFont();
                    if (bf != null)
                        Render2D.DrawText(bf, _badge, badgeRect, InkWashTheme.TextInverse,
                            TextAlignment.Center, TextAlignment.Center, TextWrapping.NoWrap);
                }
                else
                {
                    var qbg = new Color(qc.R, qc.G, qc.B, 0.18f);
                    InkRenderHelper.FillRoundedRectangle(badgeRect, 2f, qbg);
                    InkRenderHelper.DrawRoundedRectangle(badgeRect, 2f, qc, 1f);
                    var bf = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Display, 10f).GetFont();
                    if (bf != null)
                        Render2D.DrawText(bf, _badge, badgeRect, qc,
                            TextAlignment.Center, TextAlignment.Center, TextWrapping.NoWrap);
                }

                // 等级 + 速度
                var numFont = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Number, 11f).GetFont();
                if (numFont != null)
                {
                    Render2D.DrawText(numFont, "Lv." + _lvl, new Rectangle(60f, 32f, 52f, 18f),
                        InkWashTheme.TextSecondary, TextAlignment.Near, TextAlignment.Center, TextWrapping.NoWrap);
                    Render2D.DrawText(numFont, "· 速 " + _speed, new Rectangle(112f, 32f, 90f, 18f),
                        _isSelected ? InkWashTheme.GoldPrimary : InkWashTheme.TextTertiary,
                        TextAlignment.Near, TextAlignment.Center, TextWrapping.NoWrap);
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

        /// <summary>预览舞台（径向渐变底 + 四角装饰 + 中心辉光圆 + 品质标签）。</summary>
        private sealed class PreviewStage : Control
        {
            private readonly string _glyph;
            private readonly string _qualityName;

            public PreviewStage(string glyph, string qualityName)
            {
                _glyph = glyph;
                _qualityName = qualityName;
                AutoFocus = false;
            }

            public override void Draw()
            {
                base.Draw();
                if (!Visible) return;
                var rect = new Rectangle(Float2.Zero, Size);
                var center = new Float2(Width * 0.5f, Height * 0.5f);

                // 径向渐变底（gold 8% paper → abyss）
                InkRenderHelper.FillRoundedRectangle(rect, 8f, InkWashTheme.Abyss);
                float maxR = Mathf.Sqrt(center.X * center.X + center.Y * center.Y);
                Color inner = Color.Lerp(InkWashTheme.BaseTertiary, InkWashTheme.GoldPrimary, 0.08f);
                InkRenderHelper.FillRadialGradient(center, maxR, inner, InkWashTheme.Abyss);
                InkRenderHelper.DrawRoundedRectangle(rect, 8f, InkWashTheme.BorderFaint, 1f);

                // 四角装饰（24x24，金暗线）
                float cs = 24f;
                float off = 8f;
                Color dim = InkWashTheme.GoldDim;
                Render2D.FillRectangle(new Rectangle(off, off, cs, 1f), dim);
                Render2D.FillRectangle(new Rectangle(off, off, 1f, cs), dim);
                Render2D.FillRectangle(new Rectangle(Width - off - cs, off, cs, 1f), dim);
                Render2D.FillRectangle(new Rectangle(Width - off - 1f, off, 1f, cs), dim);
                Render2D.FillRectangle(new Rectangle(off, Height - off - 1f, cs, 1f), dim);
                Render2D.FillRectangle(new Rectangle(off, Height - off - cs, 1f, cs), dim);
                Render2D.FillRectangle(new Rectangle(Width - off - cs, Height - off - 1f, cs, 1f), dim);
                Render2D.FillRectangle(new Rectangle(Width - off - 1f, Height - off - cs, 1f, cs), dim);

                // 中心辉光圆（120px）
                InkRenderHelper.FillRadialGradient(center, 60f,
                    new Color(InkWashTheme.GoldPrimary.R, InkWashTheme.GoldPrimary.G, InkWashTheme.GoldPrimary.B, 0.16f),
                    Color.Transparent);
                InkRenderHelper.DrawCircle(center, 60f, InkWashTheme.GoldFaint, 1f);

                // 中心字
                var glyphFont = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Display, 64f).GetFont();
                if (glyphFont != null)
                    Render2D.DrawText(glyphFont, _glyph,
                        new Rectangle(center.X - 60f, center.Y - 60f, 120f, 120f),
                        InkWashTheme.GoldBright, TextAlignment.Center, TextAlignment.Center, TextWrapping.NoWrap);

                // 底部提示
                var hintFont = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Display, 11f).GetFont();
                if (hintFont != null)
                    Render2D.DrawText(hintFont, "拖拽旋转 · 滚轮缩放",
                        new Rectangle(0f, Height - 34f, Width, 18f), InkWashTheme.TextTertiary,
                        TextAlignment.Center, TextAlignment.Center, TextWrapping.NoWrap);

                // 顶部品质标签
                float tagW = 64f;
                var tagRect = new Rectangle((Width - tagW) * 0.5f, 14f, tagW, 22f);
                var tagBg = new Color(InkWashTheme.GoldDeep.R, InkWashTheme.GoldDeep.G, InkWashTheme.GoldDeep.B, 0.24f);
                InkRenderHelper.FillRoundedRectangle(tagRect, 2f, tagBg);
                InkRenderHelper.DrawRoundedRectangle(tagRect, 2f, InkWashTheme.QualityLegendary, 1f);
                var tagFont = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Display, 11f).GetFont();
                if (tagFont != null)
                    Render2D.DrawText(tagFont, _qualityName, tagRect, InkWashTheme.GoldBright,
                        TextAlignment.Center, TextAlignment.Center, TextWrapping.NoWrap);
            }
        }

        /// <summary>标签药丸（11px Display，radius2，自定义文本/边框/底色）。</summary>
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

        /// <summary>技能槽（已学金边金底 / 空闲淡边深底）。</summary>
        private sealed class SkillSlot : Control
        {
            private readonly string _glyph;
            private readonly string _name;
            private readonly bool _filled;

            public SkillSlot(string glyph, string name, bool filled)
            {
                _glyph = glyph;
                _name = name;
                _filled = filled;
                AutoFocus = false;
            }

            public override void Draw()
            {
                base.Draw();
                if (!Visible) return;
                var rect = new Rectangle(Float2.Zero, Size);
                if (_filled)
                {
                    Color bg = Color.Lerp(InkWashTheme.BaseTertiary, InkWashTheme.GoldDeep, 0.18f);
                    InkRenderHelper.FillRoundedRectangle(rect, 4f, bg);
                    InkRenderHelper.DrawRoundedRectangle(rect, 4f, InkWashTheme.GoldPrimary, 1f);
                }
                else
                {
                    var bg = new Color(InkWashTheme.Abyss.R, InkWashTheme.Abyss.G, InkWashTheme.Abyss.B, 0.60f);
                    InkRenderHelper.FillRoundedRectangle(rect, 4f, bg);
                    InkRenderHelper.DrawRoundedRectangle(rect, 4f, InkWashTheme.GoldFaint, 1f);
                }

                bool hasName = !string.IsNullOrEmpty(_name);
                float glyphH = hasName ? Height * 0.62f : Height;
                var glyphFont = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Display,
                    hasName ? 20f : 18f).GetFont();
                if (glyphFont != null)
                    Render2D.DrawText(glyphFont, _glyph, new Rectangle(0f, 0f, Width, glyphH),
                        _filled ? InkWashTheme.GoldBright : InkWashTheme.GoldFaint,
                        TextAlignment.Center, TextAlignment.Center, TextWrapping.NoWrap);
                if (hasName)
                {
                    var nf = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Body, 9f).GetFont();
                    if (nf != null)
                        Render2D.DrawText(nf, _name, new Rectangle(0f, glyphH, Width, Height - glyphH),
                            _filled ? InkWashTheme.TextSecondary : InkWashTheme.TextTertiary,
                            TextAlignment.Center, TextAlignment.Center, TextWrapping.NoWrap);
                }
            }
        }

        /// <summary>外观幻化缩略图（方形，选中金边，可点击）。</summary>
        private sealed class MorphThumb : Control
        {
            private readonly string _glyph;
            private readonly Color _color;
            private bool _isActive;
            private bool _isHovered;

            public event Action Clicked;
            public bool IsActive { get => _isActive; set => _isActive = value; }

            public MorphThumb(string glyph, Color color, bool active)
            {
                _glyph = glyph;
                _color = color;
                _isActive = active;
                AutoFocus = false;
            }

            public override void Draw()
            {
                base.Draw();
                if (!Visible) return;
                var rect = new Rectangle(Float2.Zero, Size);
                InkRenderHelper.FillRoundedRectangle(rect, 4f, InkWashTheme.BaseTertiary);
                Color border = _isActive ? InkWashTheme.GoldPrimary
                    : (_isHovered ? InkWashTheme.GoldPrimary : InkWashTheme.BorderFaint);
                InkRenderHelper.DrawRoundedRectangle(rect, 4f, border, _isActive ? 2f : 1f);
                var font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Display, 20f).GetFont();
                if (font != null)
                    Render2D.DrawText(font, _glyph, rect, _color,
                        TextAlignment.Center, TextAlignment.Center, TextWrapping.NoWrap);
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

        /// <summary>出战切换按钮（金边金底，金亮文字）。</summary>
        private sealed class DeployToggle : Control
        {
            private readonly string _text;
            private bool _isHovered;

            public event Action Clicked;

            public DeployToggle(string text)
            {
                _text = text;
                AutoFocus = false;
            }

            public override void Draw()
            {
                base.Draw();
                if (!Visible) return;
                var rect = new Rectangle(Float2.Zero, Size);
                var gold = InkWashTheme.GoldPrimary;
                Color bg = new Color(gold.R, gold.G, gold.B, _isHovered ? 0.20f : 0.12f);
                InkRenderHelper.FillRoundedRectangle(rect, 4f, bg);
                InkRenderHelper.DrawRoundedRectangle(rect, 4f, InkWashTheme.BorderGold, 1f);
                var font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Display, 13f).GetFont();
                if (font != null)
                    Render2D.DrawText(font, _text, rect, InkWashTheme.GoldBright,
                        TextAlignment.Center, TextAlignment.Center, TextWrapping.NoWrap);
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
