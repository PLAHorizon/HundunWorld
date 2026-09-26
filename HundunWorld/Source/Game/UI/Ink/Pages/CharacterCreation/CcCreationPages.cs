using FlaxEngine;
using FlaxEngine.GUI;
using HundunWorld.Game.UI.StyleSystem;
using System;
using System.Collections.Generic;

namespace HundunWorld.Game.UI.Ink.Pages.CharacterCreation
{
    /// <summary>
    /// 面容预设表。
    /// <para>
    /// 名称沿用旧创建流程 <c>UI/Character/FacePresetCard.cs</c> 的 8 项表，
    /// 索引即 <c>AppearanceInfo.HairModel / FaceModel</c> 的取值，保持与后端一致。
    /// 前 4 项归「风雅」、后 4 项归「写实」，对应参考图 图片/2-5.png 的两个分类页签。
    /// </para>
    /// </summary>
    internal static class CcFacePresets
    {
        /// <summary>风格分类名（下标即 <see cref="CcSession.StyleCategory"/>）。</summary>
        public static readonly string[] Categories = { "风雅", "写实" };

        /// <summary>衣装 / 体型预设名，下标写入 <c>AppearanceInfo.Clothing</c>（参考图 图片/5.png 最右一列）。</summary>
        public static readonly string[] Outfits = { "布衣", "劲装", "道袍", "儒衫", "甲胄", "蓑衣" };

        // 下面三组色板分别对应 AppearanceInfo 的 HairColor / EyeColor / SkinColor（离散下标）。
        // 旧创建流程这三个字段全传 0，所以这是第一次真正把它们带上值。

        /// <summary>发色。</summary>
        public static readonly Color[] HairColors =
        {
            new Color(0.10f, 0.10f, 0.12f), new Color(0.28f, 0.18f, 0.12f), new Color(0.45f, 0.28f, 0.16f),
            new Color(0.78f, 0.78f, 0.80f), new Color(0.92f, 0.90f, 0.86f), new Color(0.14f, 0.18f, 0.30f),
        };

        /// <summary>瞳色。</summary>
        public static readonly Color[] EyeColors =
        {
            new Color(0.32f, 0.20f, 0.12f), new Color(0.12f, 0.12f, 0.14f), new Color(0.62f, 0.42f, 0.16f),
            new Color(0.24f, 0.48f, 0.44f), new Color(0.32f, 0.40f, 0.54f),
        };

        /// <summary>肤色。</summary>
        public static readonly Color[] SkinColors =
        {
            new Color(0.93f, 0.85f, 0.76f), new Color(0.86f, 0.72f, 0.58f), new Color(0.78f, 0.62f, 0.46f),
            new Color(0.97f, 0.91f, 0.84f), new Color(0.48f, 0.34f, 0.26f), new Color(0.90f, 0.86f, 0.82f),
        };

        private static readonly string[] MaleNames = { "清秀", "儒雅", "俊朗", "阳光", "英气", "刚毅", "豪迈", "冷峻" };
        private static readonly string[] FemaleNames = { "温婉", "端庄", "清丽", "甜美", "灵动", "妩媚", "冷艳", "温柔" };

        /// <summary>取某性别、某分类下的预设（名 + 全局索引）。</summary>
        public static List<(string Name, int Index)> GetByCategory(int gender, int category)
        {
            string[] table = gender == 1 ? FemaleNames : MaleNames;
            int baseIndex = category == 1 ? 4 : 0;

            var list = new List<(string, int)>(4);
            for (int i = baseIndex; i < baseIndex + 4; i++)
                list.Add((table[i], i));
            return list;
        }

        /// <summary>全部 8 项预设（「更多面容」展开时用）。</summary>
        public static List<(string Name, int Index)> GetAll(int gender)
        {
            string[] table = gender == 1 ? FemaleNames : MaleNames;

            var list = new List<(string, int)>(table.Length);
            for (int i = 0; i < table.Length; i++)
                list.Add((table[i], i));
            return list;
        }
    }

    /// <summary>
    /// 分类标题（参考图 图片/2.png「写实 ◅」/ 图片/3.png「▸ 风雅」）：
    /// 一次只显示当前分类，点标题或箭头翻到另一类，标题下方一条细横线。
    /// </summary>
    internal class CcCategoryTitle : ContainerControl
    {
        private const float FontSize = 22f;
        private bool _pressed;

        /// <summary>当前分类名。</summary>
        public string Title = string.Empty;

        /// <summary>箭头在标题右侧（◅）还是左侧（▸）。</summary>
        public bool ArrowOnRight = true;

        /// <summary>点击切换分类。</summary>
        public event Action Activated;

        public CcCategoryTitle()
        {
            BackgroundColor = Color.Transparent;
            ClipChildren = false;
            Size = new Float2(200f, 40f);
        }

        public override bool OnMouseDown(Float2 location, MouseButton button)
        {
            if (button != MouseButton.Left)
                return false;
            _pressed = true;
            return true;
        }

        public override bool OnMouseUp(Float2 location, MouseButton button)
        {
            if (!_pressed)
                return false;
            _pressed = false;
            Activated?.Invoke();
            return true;
        }

        public override void Draw()
        {
            if (Width > 0f)
            {
                float titleW = CcDraw.MeasureText(Title, FontSize);
                float titleX = ArrowOnRight ? 0f : 26f;

                CcDraw.Text(Title, new Rectangle(titleX, 0f, titleW + 10f, Height - 8f), InkWashTheme.FontRole.Display, FontSize, CcPalette.InkText);
                CcDraw.Text(ArrowOnRight ? "◅" : "▸", new Rectangle(ArrowOnRight ? titleX + titleW + 6f : 0f, 0f, 22f, Height - 8f),
                    InkWashTheme.FontRole.Body, 14f, CcPalette.InkIdle);

                Render2D.DrawLine(new Float2(0f, Height - 6f), new Float2(Width, Height - 6f),
                    new Color(CcPalette.InkText.R, CcPalette.InkText.G, CcPalette.InkText.B, 0.25f), 1f);
            }

            base.Draw();
        }
    }

    /// <summary>
    /// 创角第一步：性别（参考图 图片/6.png、7.png）。
    /// 左侧一条水墨笔触双档开关选「男 / 女」，右侧全身立绘，右下角「Space 下一步」。
    /// </summary>
    public class CcGenderPage : CcStagePage
    {
        private CcBrushToggle _toggle;

        public CcGenderPage()
        {
            _toggle = new CcBrushToggle
            {
                Parent = this,
            };
            _toggle.Selected = CcSession.Current.Gender;
            _toggle.SelectedChanged += index =>
            {
                CcSession.Current.Gender = index;

                // 目前 Content/ 下只有一具通用身体模型（skm_uefn_mannequin），
                // 没有独立的female身体/发型网格，因此切换性别暂不改变预览模型；
                // 模型资源到位后在此处按性别换 SetCharacter 的目标即可。
                FlaxEngine.Debug.Log($"[CcGenderPage] 性别 -> {(index == 1 ? "女" : "男")}");
            };
        }

        protected override string ConfirmText => "下一步";

        public override void RefreshLayout()
        {
            base.RefreshLayout();

            if (_toggle == null || Width <= 0f)
                return;

            // 实测参考图（图片/6、7.png）：笔触整体从 x≈75 延伸到 x≈846（两段各约 371，中缝 28），
            // 腹部中心在屏幕高约 43% 处——按此定位，而不是凭眼估。
            _toggle.Size = new Float2(770f, 120f);
            _toggle.Location = new Float2(75f, Height * 0.368f);
        }
    }

    /// <summary>
    /// 创角第二步：面容风格预设（参考图 图片/2.png、3.png）。
    /// 左上「风雅 / 写实」分类标题，下方一排竖版水墨卡，选中卡打朱红印章。
    /// </summary>
    public class CcFacePresetPage : CcStagePage
    {
        private const float CardW = 165f;
        private const float CardH = 500f;   // 实测参考图 图片/2、3.png 竖卡约 195~700
        private const float CardGap = 20f;   // 4 张卡右边缘 855，留出预览区（860 起）的余量
        private const float CardsTop = 200f;

        private readonly List<CcPresetCard> _cards = new List<CcPresetCard>();

        private CcCategoryTitle _title;

        public CcFacePresetPage()
        {
            _title = new CcCategoryTitle
            {
                Parent = this,
            };
            _title.Activated += FlipCategory;

            RebuildCards();
        }

        protected override string ConfirmText => "下一步";

        /// <summary>这一步看的是脸：相机推近并对准头肩（参考图 图片/2、3.png）。</summary>
        protected override float PreviewFramingScale => 0.55f;
        protected override float PreviewFramingHeight => 0.78f;

        /// <summary>Q/E 或 Z/C 翻分类，与点标题箭头等效。</summary>
        protected override void HandleShortcuts()
        {
            if (Input.GetKeyDown(KeyboardKeys.Q) || Input.GetKeyDown(KeyboardKeys.E) ||
                Input.GetKeyDown(KeyboardKeys.Z) || Input.GetKeyDown(KeyboardKeys.C))
                FlipCategory();
        }

        private void FlipCategory()
        {
            CcSession.Current.StyleCategory = CcSession.Current.StyleCategory == 0 ? 1 : 0;
            RebuildCards();
        }

        /// <summary>按当前分类重建卡面（分类切换 / 首次进入）。</summary>
        private void RebuildCards()
        {
            int category = CcSession.Current.StyleCategory;

            _title.Title = CcFacePresets.Categories[category];
            // 参考图：选「写实」时箭头在右（◅），选「风雅」时箭头在左（▸）
            _title.ArrowOnRight = category == 1;

            foreach (var card in _cards)
                card.Dispose();
            _cards.Clear();

            foreach (var preset in CcFacePresets.GetByCategory(CcSession.Current.Gender, category))
            {
                int index = preset.Index;
                var card = new CcPresetCard
                {
                    Name = preset.Name,
                    PresetIndex = preset.Index,
                    Selected = CcSession.Current.FacePresetIndex == preset.Index,
                    Size = new Float2(CardW, CardH),
                    Parent = this,
                };
                card.Activated += () => SelectPreset(index);
                _cards.Add(card);
            }
        }

        private void SelectPreset(int index)
        {
            CcSession.Current.FacePresetIndex = index;
            foreach (var card in _cards)
                card.Selected = card.PresetIndex == index;
        }

        public override void RefreshLayout()
        {
            base.RefreshLayout();

            if (Width <= 0f)
                return;

            _title.Size = new Float2(220f, 40f);
            _title.Location = new Float2(RailX + 55f, 130f);

            for (int i = 0; i < _cards.Count; i++)
            {
                _cards[i].Size = new Float2(CardW, CardH);
                _cards[i].Location = new Float2(RailX + 55f + i * (CardW + CardGap), CardsTop);
            }
        }
    }

    /// <summary>
    /// 创角第三步：捏脸（参考图 图片/4.png、5.png）。
    /// 顶部「捏脸 / 智能捏脸」页签（Q/E），二级「风雅 / 写实」页签（Z/C），
    /// 左侧四张横版预设卡 + 「更多面容」，右侧「万相集 / 分享」与三个圆形功能钮。
    /// </summary>
    public class CcFaceSculptPage : CcStagePage
    {
        // 尺寸按参考图实测（图片/4、5.png 左栏四张横卡：单卡高约 152、间距 18、首卡顶 196）
        private const float CardW = 375f;
        private const float CardH = 152f;
        private const float CardGap = 18f;
        private const float CardsLeft = 94f;
        private const float CardsTop = 196f;

        /// <summary>展开「更多面容」放 8 张时的紧凑卡高与间距（保证列表底仍在 876 以内）。</summary>
        private const float CompactCardH = 74f;
        private const float CompactCardGap = 10f;

        private CcTabRow _modeTabs;
        private CcTabRow _categoryTabs;
        private readonly List<CcPresetCard> _cards = new List<CcPresetCard>();
        private CcHint _importHint;
        private CcIconEntry _galleryEntry;
        private CcIconEntry _shareEntry;
        private CcIconEntry _bodyEntry;
        private CcIconEntry _hairEntry;
        private CcIconEntry _outfitEntry;
        private CcHint _moreHint;
        private CcSwatchRow _hairRow;
        private CcSwatchRow _eyeRow;
        private CcSwatchRow _skinRow;
        private readonly List<CcPresetCard> _outfitThumbs = new List<CcPresetCard>();
        private bool _showAllPresets;

        public CcFaceSculptPage()
        {
            _modeTabs = new CcTabRow
            {
                Tabs = new[] { "捏脸", "智能捏脸" },
                LeftKey = "Q",
                RightKey = "E",
                Parent = this,
            };
            // 智能捏脸需要图像/算法后端，当前仅记录页签态；后续接入时在此分流。
            _modeTabs.TabClicked += i => FlaxEngine.Debug.Log($"[CcFaceSculptPage] 模式页签 -> {(i == 1 ? "智能捏脸" : "捏脸")}");

            _categoryTabs = new CcTabRow
            {
                Tabs = CcFacePresets.Categories,
                Selected = CcSession.Current.StyleCategory,
                LeftKey = "Z",
                RightKey = "C",
                Parent = this,
            };
            _categoryTabs.TabClicked += i =>
            {
                CcSession.Current.StyleCategory = i;
                RebuildCards();
            };

            _importHint = new CcHint
            {
                Key = "V",
                Text = "导入捏脸",
                TextColor = CcPalette.InkText,
                Parent = this,
            };
            _importHint.Activated += ImportFacePreset;

            _moreHint = new CcHint
            {
                Text = "更多面容 ▾",
                TextColor = CcPalette.InkText,
                Parent = this,
            };
            _moreHint.Activated += ToggleShowAll;

            _galleryEntry = NewEntry("万", "万相集", false);
            _shareEntry = NewEntry("⇗", "分享", false);
            _bodyEntry = NewEntry("衣", string.Empty, true);
            _hairEntry = NewEntry("发", string.Empty, true);
            _outfitEntry = NewEntry("姿", string.Empty, true);

            // 「衣」圆钮等价于切最右一列衣装（同一个 Clothing 值，真实生效）。
            _bodyEntry.Activated += CycleOutfit;
            // 其余入口按参考图保留在版面上，但本项目暂无对应资源/后端：点了只记日志，不做假反应。
            _galleryEntry.Activated += () => NotImplemented("万相集（面容收藏）");
            _shareEntry.Activated += () => NotImplemented("分享捏脸");
            _hairEntry.Activated += () => NotImplemented("发型编辑（无发型网格资源）");
            _outfitEntry.Activated += () => NotImplemented("姿势编辑（无姿势数据）");

            RebuildCards();
            RebuildOutfitThumbs();

            _hairRow = NewSwatch("发色", CcFacePresets.HairColors, CcSession.Current.HairColorIndex, v => CcSession.Current.HairColorIndex = v);
            _eyeRow = NewSwatch("瞳色", CcFacePresets.EyeColors, CcSession.Current.EyeColorIndex, v => CcSession.Current.EyeColorIndex = v);
            _skinRow = NewSwatch("肤色", CcFacePresets.SkinColors, CcSession.Current.SkinColorIndex, v => CcSession.Current.SkinColorIndex = v);
        }

        private CcSwatchRow NewSwatch(string label, Color[] options, int selected, Action<int> onPick)
        {
            var row = new CcSwatchRow
            {
                Label = label,
                Options = options,
                Selected = selected,
                Parent = this,
            };
            row.SelectedChanged += onPick;
            return row;
        }

        protected override string ConfirmText => "下一步";

        /// <summary>
        /// 最右一列衣装 / 体型缩略（参考图 图片/5.png 右侧竖排小图）。
        /// 选中下标写进 <c>AppearanceInfo.Clothing</c>；客户端暂无对应网格可换，
        /// 但选择会随创建请求真实上报到服务端。
        /// </summary>
        private void RebuildOutfitThumbs()
        {
            for (int i = 0; i < CcFacePresets.Outfits.Length; i++)
            {
                int index = i;
                var thumb = new CcPresetCard
                {
                    PresetIndex = index,
                    Selected = CcSession.Current.BodyIndex == index,
                    Parent = this,
                };
                thumb.Activated += () => SelectOutfit(index);
                _outfitThumbs.Add(thumb);
            }
        }

        private void SelectOutfit(int index)
        {
            CcSession.Current.BodyIndex = index;
            foreach (var thumb in _outfitThumbs)
                thumb.Selected = thumb.PresetIndex == index;
        }

        private void CycleOutfit()
        {
            SelectOutfit((CcSession.Current.BodyIndex + 1) % CcFacePresets.Outfits.Length);
        }

        private static void NotImplemented(string what)
        {
            FlaxEngine.Debug.Log($"[CcFaceSculptPage] {what}：暂无对应资源/后端，未接入");
        }

        /// <summary>捏脸同样是头肩特写（参考图 图片/4、5.png）。</summary>
        protected override float PreviewFramingScale => 0.55f;
        protected override float PreviewFramingHeight => 0.78f;

        /// <summary>Q/E 切捏脸模式，Z/C 切风格分类（与页签两侧键帽一致）。</summary>
        protected override void HandleShortcuts()
        {
            if (Input.GetKeyDown(KeyboardKeys.Q))
                _modeTabs.SelectTab(0);
            else if (Input.GetKeyDown(KeyboardKeys.E))
                _modeTabs.SelectTab(1);
            else if (Input.GetKeyDown(KeyboardKeys.Z))
                _categoryTabs.SelectTab(0);
            else if (Input.GetKeyDown(KeyboardKeys.C))
                _categoryTabs.SelectTab(1);
            else if (Input.GetKeyDown(KeyboardKeys.V))
                ImportFacePreset();
        }

        /// <summary>
        /// 导入捏脸（参考图「V 导入捏脸」）。
        /// 面容数据尚无对外格式（AppearanceInfo 只有几个 int，没有捏脸码/分享码定义），
        /// 先保留入口并记录，待格式确定后在此接入。
        /// </summary>
        private void ImportFacePreset()
        {
            FlaxEngine.Debug.Log("[CcFaceSculptPage] 导入捏脸：面容数据格式未定义，暂未接入");
        }
        private CcIconEntry NewEntry(string glyph, string caption, bool filled)
        {
            return new CcIconEntry
            {
                Glyph = glyph,
                Caption = caption,
                FilledCircle = filled,
                Parent = this,
            };
        }

        /// <summary>重建左侧预设卡（分类切换或展开「更多面容」）。</summary>
        private void RebuildCards()
        {
            foreach (var card in _cards)
                card.Dispose();
            _cards.Clear();

            var presets = _showAllPresets
                ? CcFacePresets.GetAll(CcSession.Current.Gender)
                : CcFacePresets.GetByCategory(CcSession.Current.Gender, CcSession.Current.StyleCategory);

            foreach (var preset in presets)
            {
                int index = preset.Index;
                var card = new CcPresetCard
                {
                    Wide = true,
                    Name = preset.Name,
                    PresetIndex = preset.Index,
                    Selected = CcSession.Current.FacePresetIndex == preset.Index,
                    Size = new Float2(CardW, CardH),
                    Parent = this,
                };
                card.Activated += () => SelectPreset(index);
                _cards.Add(card);
            }
        }

        private void SelectPreset(int index)
        {
            CcSession.Current.FacePresetIndex = index;
            foreach (var card in _cards)
                card.Selected = card.PresetIndex == index;
        }

        /// <summary>展开 / 收起全部 8 项面容（参考图「更多面容」）。</summary>
        private void ToggleShowAll()
        {
            _showAllPresets = !_showAllPresets;
            _moreHint.Text = _showAllPresets ? "收起面容 ▴" : "更多面容 ▾";
            _moreHint.ApplyWidth();
            RebuildCards();
        }

        public override void RefreshLayout()
        {
            base.RefreshLayout();

            if (Width <= 0f)
                return;

            _modeTabs.Size = new Float2(520f, 44f);
            _modeTabs.Location = new Float2(75f, 26f);

            _categoryTabs.Size = new Float2(420f, 40f);
            _categoryTabs.Location = new Float2(CardsLeft, 130f);

            // 展开「更多面容」时切紧凑卡高：8 张全尺寸卡（152+18）会把按钮与色板推到 1080 设计高之外。
            float cardH = _showAllPresets ? CompactCardH : CardH;
            float cardGap = _showAllPresets ? CompactCardGap : CardGap;
            float listBottom = CardsTop;

            for (int i = 0; i < _cards.Count; i++)
            {
                _cards[i].Size = new Float2(CardW, cardH);
                _cards[i].Location = new Float2(CardsLeft, CardsTop + i * (cardH + cardGap));
                listBottom = CardsTop + (i + 1) * (cardH + cardGap);
            }

            _moreHint.ApplyWidth();
            _moreHint.Location = new Float2(CardsLeft + (CardW - _moreHint.Width) * 0.5f, listBottom + 2f);
            _importHint.Location = new Float2(CardsLeft + 120f, Height - 72f);
            _importHint.ApplyWidth();

            // 三组色板横排成一行：竖排三行会顶到右下确认块（实测确认块占 y 979~1035）
            float swatchY = listBottom + 40f;
            float sx = CardsLeft;
            foreach (var row in new[] { _hairRow, _eyeRow, _skinRow })
            {
                if (row == null)
                    continue;

                row.Size = new Float2(row.PreferredWidth, 30f);
                row.Location = new Float2(sx, swatchY);
                sx += row.Width + 20f;
            }

            float railX = Width - 135f;
            Place(_galleryEntry, railX, 150f);
            Place(_shareEntry, railX, 245f);
            Place(_bodyEntry, railX, Height - 300f);
            Place(_hairEntry, railX, Height - 240f);
            Place(_outfitEntry, railX, Height - 180f);

            // 最右一列衣装缩略（参考图 图片/5.png 右侧竖排小图）
            for (int i = 0; i < _outfitThumbs.Count; i++)
            {
                _outfitThumbs[i].Size = new Float2(72f, 92f);
                _outfitThumbs[i].Location = new Float2(Width - 215f, 150f + i * 100f);
            }
        }

        private static void Place(CcIconEntry entry, float x, float y)
        {
            entry.Size = new Float2(56f, string.IsNullOrEmpty(entry.Caption) ? 36f : 66f);
            entry.Location = new Float2(x, y);
        }
    }

    /// <summary>
    /// 创角最后一步：命名（沿用同一舞台版式）。
    /// <para>
    /// 参考图未给出命名页，故只沿用其舞台语言（雾白底 / 左侧留白 / 右下确认块），
    /// 并且**不再提供门派选择**：出身门派由角色进入游戏后自行创建或加入时才显示。
    /// </para>
    /// </summary>
    public class CcNamePage : CcStagePage
    {
        private const float FieldW = 420f;

        private Label _titleLabel;
        private Panel _fieldWrap;
        private TextBox _nameInput;
        private Label _countLabel;
        private Label _hintLabel;
        private CcHint _randomHint;

        /// <summary>当前输入的角色名。</summary>
        public string CharacterName => _nameInput != null ? _nameInput.Text : CcSession.Current.CharacterName;

        public CcNamePage()
        {
            _titleLabel = NewLabel("姓 名", InkWashTheme.FontRole.Heading, 20f, CcPalette.InkText);

            _fieldWrap = new Panel
            {
                BackgroundColor = new Color(1f, 1f, 1f, 0.55f),
                Parent = this,
            };

            _nameInput = new TextBox
            {
                Text = CcSession.Current.CharacterName,
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Heading, 22f),
                TextColor = CcPalette.InkText,
                BackgroundColor = Color.Transparent,
                MaxLength = 6,
                Parent = _fieldWrap,
            };
            _nameInput.TextChanged += () =>
            {
                CcSession.Current.CharacterName = _nameInput.Text;
                _countLabel.Text = $"{_nameInput.Text.Length}/6";
            };

            _countLabel = new Label
            {
                Text = $"{_nameInput.Text.Length}/6",
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Number, 13f),
                TextColor = CcPalette.InkIdle,
                Parent = _fieldWrap,
            };

            _hintLabel = NewLabel("汉字二至六字；门派不入江湖户籍，入门由你日后自行抉择", InkWashTheme.FontRole.Body, 13f, CcPalette.InkIdle);

            _randomHint = new CcHint
            {
                Text = "天机赐名",
                TextColor = CcPalette.TextGold,
                Parent = this,
            };
            _randomHint.Activated += GenerateRandomName;
        }

        /// <summary>命名页里空格属于名字本身，不能当作「下一步」。</summary>
        protected override bool ConfirmOnSpace => false;

        protected override string ConfirmText => "踏入江湖";

        private Label NewLabel(string text, InkWashTheme.FontRole role, float fontSize, Color color)
        {
            return new Label
            {
                Text = text,
                Font = InkRenderHelper.GetFontRef(role, fontSize),
                TextColor = color,
                Parent = this,
            };
        }

        private static readonly string[] Surnames = { "李", "王", "张", "赵", "萧", "楚", "林", "陈", "叶", "苏" };
        private static readonly string[] GivenNames = { "忘生", "长风", "无极", "青衫", "惊鸿", "孤云", "听雨", "凌霄", "落尘", "问剑" };

        private void GenerateRandomName()
        {
            var rng = new Random();
            _nameInput.Text = Surnames[rng.Next(Surnames.Length)] + GivenNames[rng.Next(GivenNames.Length)];
            CcSession.Current.CharacterName = _nameInput.Text;
            _countLabel.Text = $"{_nameInput.Text.Length}/6";
        }

        public override void RefreshLayout()
        {
            base.RefreshLayout();

            if (Width <= 0f)
                return;

            float x = RailX + 55f;
            float y = Height * 0.34f;

            _titleLabel.Location = new Float2(x, y);
            _titleLabel.Size = new Float2(FieldW, 22f);

            _fieldWrap.Location = new Float2(x, y + 32f);
            _fieldWrap.Size = new Float2(FieldW, 52f);

            _nameInput.Location = new Float2(14f, 12f);
            _nameInput.Size = new Float2(FieldW - 80f, 28f);
            _countLabel.Location = new Float2(FieldW - 60f, 18f);
            _countLabel.Size = new Float2(50f, 16f);

            _hintLabel.Location = new Float2(x, y + 96f);
            _hintLabel.Size = new Float2(FieldW + 120f, 20f);

            _randomHint.Location = new Float2(x, y + 136f);
            _randomHint.ApplyWidth();
        }
    }
}
