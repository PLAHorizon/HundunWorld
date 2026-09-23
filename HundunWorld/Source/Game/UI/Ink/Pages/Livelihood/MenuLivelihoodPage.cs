using FlaxEngine;
using FlaxEngine.GUI;
using HundunWorld.Game.UI.Ink;
using HundunWorld.Game.UI.StyleSystem;
using System;
using System.Collections.Generic;

namespace HundunWorld.Game.UI.Ink.Pages.Livelihood
{
    /// <summary>
    /// 菜单·营生（采集制造，谋生之道）。
    /// 对应设计稿 hundun-yy-ui/pages/menu-livelihood.html：
    /// 240px 侧边导航栏（主菜单 7 项 / 角色 4 项 / 底部设置）
    /// + 60px 顶栏（返回 / 头像 / 名号与门派 / 三种货币）
    /// + 页面标题行（营生 + 「采集制造，谋生之道」+ 技能总数 / 已掌握 标签）
    /// + 左 35% 营生技能列表面板（6 项技能，含等级标签与经验条，点击切换）
    /// + 右 65% 技能详情面板（经验值 / 可采集配方列表 / 工具耐久底栏 + 修理按钮）
    /// + 28px 底部状态栏（服务器 / 延迟 / 版本）。
    /// 全部颜色取自 <see cref="InkWashTheme"/> 设计 Token，无硬编码色值。
    /// </summary>
    public class MenuLivelihoodPage : ContainerControl, IInkPage
    {
        // ───────────── 数据模型 ─────────────

        /// <summary>标签配色皮肤（对应设计稿 .ink-tag / .ink-tag-brand / .ink-tag-vermilion / .ink-tag-jade / .ink-tag-common 与品质色标签）。</summary>
        private enum TagSkin
        {
            Default,
            Brand,
            Vermilion,
            Jade,
            Common,
            Rare,
            Epic
        }

        /// <summary>可采集配方数据。</summary>
        private class RecipeData
        {
            public string Name;
            public string Glyph;
            public TagSkin Skin;
            public bool Unlocked;
            public string StatusText;
            public string Output;
        }

        /// <summary>营生技能数据。</summary>
        private class SkillData
        {
            public string Name;
            public string Glyph;
            public Color GlyphColor;
            public Color IconBorder;
            public TagSkin LevelSkin;
            public InkBarFillVariant BarVariant;
            public int Level;
            public int ExpCur;
            public int ExpMax;
            public string Description;
            public string ToolName;
            public string ToolQuality;
            public int ToolDurability;
            public int ToolMaxDurability;
            public RecipeData[] Recipes;
        }

        // ───────────── 自定义控件 ─────────────

        /// <summary>可点击面板（技能条目 / 配方条目）。</summary>
        private class ClickablePanel : Panel
        {
            public event Action Clicked;
            public event Action<bool> HoverChanged;

            public Color NormalBackground = Color.Transparent;
            public Color HoverBackground = Color.Transparent;

            public void ApplyNormal()
            {
                BackgroundColor = NormalBackground;
            }

            public override bool OnMouseUp(Float2 location, MouseButton button)
            {
                if (button == MouseButton.Left)
                    Clicked?.Invoke();
                return base.OnMouseUp(location, button);
            }

            public override void OnMouseEnter(Float2 location)
            {
                BackgroundColor = HoverBackground;
                HoverChanged?.Invoke(true);
                base.OnMouseEnter(location);
            }

            public override void OnMouseLeave()
            {
                BackgroundColor = NormalBackground;
                HoverChanged?.Invoke(false);
                base.OnMouseLeave();
            }
        }

        /// <summary>描边标签（设计稿 .ink-tag 系列皮肤 + 品质色标签）。</summary>
        private class SkinTag : Label
        {
            public Color OutlineColor = InkWashTheme.BorderNeutralL2;

            private TagSkin _skin = TagSkin.Default;

            public TagSkin Skin
            {
                get => _skin;
                set
                {
                    _skin = value;
                    ApplySkin();
                }
            }

            public SkinTag()
            {
                HorizontalAlignment = TextAlignment.Center;
                VerticalAlignment = TextAlignment.Center;
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Body, 11f);
                ApplySkin();
            }

            private void ApplySkin()
            {
                switch (_skin)
                {
                    case TagSkin.Brand:
                        BackgroundColor = Tint(InkWashTheme.GoldPrimary, 0.12f);
                        OutlineColor = InkWashTheme.BorderGold;
                        TextColor = InkWashTheme.TextBrand;
                        break;

                    case TagSkin.Vermilion:
                        BackgroundColor = Tint(InkWashTheme.VermilionPrimary, 0.12f);
                        OutlineColor = InkWashTheme.BorderVermilion;
                        TextColor = InkWashTheme.TextVermilion;
                        break;

                    case TagSkin.Jade:
                        BackgroundColor = Tint(InkWashTheme.JadePrimary, 0.12f);
                        OutlineColor = Tint(InkWashTheme.JadePrimary, 0.3f);
                        TextColor = InkWashTheme.JadeBright;
                        break;

                    case TagSkin.Common:
                        BackgroundColor = Tint(InkWashTheme.QualityCommon, 0.1f);
                        OutlineColor = InkWashTheme.BorderNeutralL2;
                        TextColor = InkWashTheme.TextTertiary;
                        break;

                    case TagSkin.Rare:
                        BackgroundColor = Tint(InkWashTheme.QualityRare, 0.1f);
                        OutlineColor = Tint(InkWashTheme.QualityRare, 0.3f);
                        TextColor = InkWashTheme.QualityRare;
                        break;

                    case TagSkin.Epic:
                        BackgroundColor = Tint(InkWashTheme.QualityEpic, 0.1f);
                        OutlineColor = Tint(InkWashTheme.QualityEpic, 0.3f);
                        TextColor = InkWashTheme.QualityEpic;
                        break;

                    default: // Default
                        BackgroundColor = Tint(InkWashTheme.GoldPrimary, 0.08f);
                        OutlineColor = InkWashTheme.BorderNeutralL2;
                        TextColor = InkWashTheme.TextSecondary;
                        break;
                }
            }

            public override void Draw()
            {
                base.Draw();
                if (OutlineColor.A > 0f && Width > 0f && Height > 0f)
                    Render2D.DrawRectangle(new Rectangle(0, 0, Width, Height), OutlineColor, 1f);
            }
        }

        /// <summary>描边图标框（设计稿技能 / 配方 / 工具图标底框，品质色描边 + 同色淡底）。</summary>
        private class OutlineIconBox : Panel
        {
            public Color OutlineColor = Color.Transparent;

            public override void Draw()
            {
                base.Draw();
                if (OutlineColor.A > 0f && Width > 0f && Height > 0f)
                    Render2D.DrawRectangle(new Rectangle(0, 0, Width, Height), OutlineColor, 1f);
            }
        }

        /// <summary>技能条目子控件引用集。</summary>
        private class SkillItemView
        {
            public ClickablePanel Root;
            public OutlineIconBox Icon;
            public Label Glyph;
            public Label Name;
            public SkinTag Level;
            public InkBar Bar;
            public Label Exp;
        }

        /// <summary>配方条目子控件引用集。</summary>
        private class RecipeView
        {
            public ClickablePanel Root;
            public OutlineIconBox Icon;
            public Label Glyph;
            public Label Name;
            public SkinTag Status;
            public Label Output;
            public Label Tool;
            public bool Unlocked;
            public string RecipeName;
        }

        // ───────────── 字段 ─────────────

        private Float2 _screenSize;

        private InkBackgroundLayer _bgLayer;
        private InkVignette _vignette;

        // 侧边导航栏
        private Panel _sidebar;
        private Label _logoLabel;
        private Panel _navDivider;
        private Panel _settingsDivider;
        private InkButton _settingsButton;
        private readonly List<InkButton> _navButtons = new List<InkButton>();
        private readonly List<InkButton> _navMutedButtons = new List<InkButton>();

        // 主区域
        private Panel _mainArea;

        // 顶栏
        private Panel _topBar;
        private InkButton _backBtn;
        private Panel _topDivider;
        private Panel _avatar;
        private Label _playerNameLabel;
        private SkinTag _levelTag;
        private Label _sectLabel;
        private readonly Panel[] _currencyDots = new Panel[3];
        private readonly Label[] _currencyValues = new Label[3];

        // 页面标题行
        private Label _pageTitle;
        private Label _pageSubtitle;
        private SkinTag _skillCountTag;
        private SkinTag _masteredTag;

        // 左：营生技能面板
        private InkPanel _skillPanel;
        private InkCornerDeco _skillCorners;
        private InkPanelTitle _skillPanelTitle;
        private Label _skillPanelHint;
        private readonly List<SkillItemView> _skillViews = new List<SkillItemView>();

        // 右：技能详情面板
        private InkPanel _detailPanel;
        private InkCornerDeco _detailCorners;
        private OutlineIconBox _detailIcon;
        private Label _detailGlyph;
        private Label _detailName;
        private SkinTag _detailLevelTag;
        private Label _detailDesc;
        private Label _expCaption;
        private Label _expValue;
        private InkBar _expBar;
        private Panel _detailHeaderDivider;
        private InkPanelTitle _recipeTitle;
        private Label _recipeMasteredHint;
        private Panel _recipeList;
        private readonly List<RecipeView> _recipeViews = new List<RecipeView>();

        private Panel _toolDivider;
        private OutlineIconBox _toolIcon;
        private Label _toolGlyph;
        private Label _toolName;
        private SkinTag _toolQualityTag;
        private Label _toolDurabilityCaption;
        private InkBar _toolDurabilityBar;
        private Label _toolDurabilityValue;
        private InkButton _repairButton;

        // 底部状态栏
        private Panel _footer;
        private Label _serverLabel;
        private Label _versionLabel;

        private List<SkillData> _skills;
        private int _activeSkillIdx;

        // 尺寸常量
        private const float SidebarW = 240f;
        private const float TopBarH = 60f;
        private const float FooterH = 28f;
        private const float ContentPad = 24f;
        private const float ColumnGap = 20f;
        private const float LeftRatio = 0.35f;
        private const float PanelTitleH = 44f;
        private const float SkillItemH = 76f;
        private const float SkillItemGap = 8f;
        private const float RecipeItemH = 52f;
        private const float RecipeItemGap = 8f;
        private const float DetailHeaderH = 96f;
        private const float ToolBarH = 64f;
        private const float NavItemH = 36f;
        private const float NavItemStep = 40f;
        private const float NavSidePad = 12f;

        public event Action<string> NavigationRequested;

        // ───────────── 构造函数 ─────────────

        public MenuLivelihoodPage()
        {
            _screenSize = FlaxEngine.Screen.Size;
            if (_screenSize.X <= 0f || _screenSize.Y <= 0f)
            {
                _screenSize = new Float2(1920f, 1080f);
            }

            AnchorPreset = AnchorPresets.StretchAll;
            BackgroundColor = Color.Transparent;
            ClipChildren = false;
            AutoFocus = false;
            Location = Float2.Zero;
            Size = _screenSize;

            BuildData();

            try
            {
                BuildBackground();
                BuildSidebar();
                BuildMainArea();
                BuildTopBar();
                BuildPageHeader();
                BuildSkillPanel();
                BuildDetailPanel();
                BuildFooter();

                _activeSkillIdx = 0;
                RefreshAllData();
                ApplyLayout();
            }
            catch (Exception ex)
            {
                FlaxEngine.Debug.LogError($"[MenuLivelihoodPage] 初始化失败: {ex.Message}");
            }
        }

        /// <summary>同色降透明度（用于 .ink-tag 系列的 8%/10%/12% 淡底）。</summary>
        private static Color Tint(Color color, float alpha)
        {
            return new Color(color.R, color.G, color.B, alpha);
        }

        // ───────────── 数据 ─────────────

        private void BuildData()
        {
            _skills = new List<SkillData>
            {
                new SkillData
                {
                    Name = "采药", Glyph = "药",
                    GlyphColor = InkWashTheme.VermilionBright, IconBorder = InkWashTheme.VermilionDeep,
                    LevelSkin = TagSkin.Vermilion, BarVariant = InkBarFillVariant.Vermilion,
                    Level = 8, ExpCur = 2400, ExpMax = 3200,
                    Description = "于山野间辨识百草，采集入药",
                    ToolName = "采药锄", ToolQuality = "普通",
                    ToolDurability = 85, ToolMaxDurability = 100,
                    Recipes = new[]
                    {
                        new RecipeData { Name = "常见草药", Glyph = "草", Skin = TagSkin.Common, Unlocked = true, StatusText = "已掌握", Output = "草药 ×3" },
                        new RecipeData { Name = "雪莲", Glyph = "莲", Skin = TagSkin.Rare, Unlocked = true, StatusText = "已掌握", Output = "雪莲 ×1" },
                        new RecipeData { Name = "灵芝", Glyph = "芝", Skin = TagSkin.Epic, Unlocked = true, StatusText = "已掌握", Output = "灵芝 ×1" },
                        new RecipeData { Name = "寒梅", Glyph = "梅", Skin = TagSkin.Jade, Unlocked = true, StatusText = "已掌握", Output = "寒梅 ×2" },
                        new RecipeData { Name = "天山雪菊", Glyph = "菊", Skin = TagSkin.Common, Unlocked = false, StatusText = "Lv.10 解锁", Output = "雪菊 ×2" },
                        new RecipeData { Name = "九死还魂草", Glyph = "魂", Skin = TagSkin.Common, Unlocked = false, StatusText = "Lv.15 解锁", Output = "还魂草 ×1" }
                    }
                },
                new SkillData
                {
                    Name = "钓鱼", Glyph = "渔",
                    GlyphColor = InkWashTheme.Info, IconBorder = InkWashTheme.Info,
                    LevelSkin = TagSkin.Common, BarVariant = InkBarFillVariant.Gold,
                    Level = 5, ExpCur = 1800, ExpMax = 3000,
                    Description = "临水垂纶，静待鱼汛，所得可烹可售",
                    ToolName = "青竹钓竿", ToolQuality = "普通",
                    ToolDurability = 92, ToolMaxDurability = 100,
                    Recipes = new[]
                    {
                        new RecipeData { Name = "河鲫", Glyph = "鲫", Skin = TagSkin.Common, Unlocked = true, StatusText = "已掌握", Output = "鲫鱼 ×3" },
                        new RecipeData { Name = "锦鲤", Glyph = "鲤", Skin = TagSkin.Common, Unlocked = true, StatusText = "已掌握", Output = "锦鲤 ×1" },
                        new RecipeData { Name = "银鲈", Glyph = "鲈", Skin = TagSkin.Rare, Unlocked = true, StatusText = "已掌握", Output = "银鲈 ×2" },
                        new RecipeData { Name = "乌鳢", Glyph = "鳢", Skin = TagSkin.Common, Unlocked = false, StatusText = "Lv.8 解锁", Output = "乌鳢 ×1" }
                    }
                },
                new SkillData
                {
                    Name = "采矿", Glyph = "矿",
                    GlyphColor = InkWashTheme.PaperDark, IconBorder = InkWashTheme.PaperDark,
                    LevelSkin = TagSkin.Common, BarVariant = InkBarFillVariant.Gold,
                    Level = 3, ExpCur = 800, ExpMax = 2000,
                    Description = "凿石取矿，为锻造提供原材",
                    ToolName = "铁镐", ToolQuality = "普通",
                    ToolDurability = 64, ToolMaxDurability = 100,
                    Recipes = new[]
                    {
                        new RecipeData { Name = "铁矿", Glyph = "铁", Skin = TagSkin.Common, Unlocked = true, StatusText = "已掌握", Output = "铁矿 ×3" },
                        new RecipeData { Name = "铜矿", Glyph = "铜", Skin = TagSkin.Common, Unlocked = true, StatusText = "已掌握", Output = "铜矿 ×2" },
                        new RecipeData { Name = "银矿", Glyph = "银", Skin = TagSkin.Rare, Unlocked = false, StatusText = "Lv.6 解锁", Output = "银矿 ×1" }
                    }
                },
                new SkillData
                {
                    Name = "锻造", Glyph = "锻",
                    GlyphColor = InkWashTheme.GoldPrimary, IconBorder = InkWashTheme.GoldDeep,
                    LevelSkin = TagSkin.Brand, BarVariant = InkBarFillVariant.Gold,
                    Level = 6, ExpCur = 2160, ExpMax = 3000,
                    Description = "以炉火锻打兵刃，淬炼成器",
                    ToolName = "铁匠锤", ToolQuality = "精良",
                    ToolDurability = 78, ToolMaxDurability = 100,
                    Recipes = new[]
                    {
                        new RecipeData { Name = "铁剑", Glyph = "剑", Skin = TagSkin.Common, Unlocked = true, StatusText = "已掌握", Output = "铁剑 ×1" },
                        new RecipeData { Name = "精铁刀", Glyph = "刀", Skin = TagSkin.Common, Unlocked = true, StatusText = "已掌握", Output = "精铁刀 ×1" },
                        new RecipeData { Name = "玄铁重剑", Glyph = "玄", Skin = TagSkin.Epic, Unlocked = false, StatusText = "Lv.12 解锁", Output = "玄铁剑 ×1" }
                    }
                },
                new SkillData
                {
                    Name = "烹饪", Glyph = "烹",
                    GlyphColor = InkWashTheme.JadeBright, IconBorder = InkWashTheme.JadePrimary,
                    LevelSkin = TagSkin.Jade, BarVariant = InkBarFillVariant.Jade,
                    Level = 4, ExpCur = 1000, ExpMax = 2000,
                    Description = "调和五味，烹制羹汤以补气血",
                    ToolName = "铜锅", ToolQuality = "普通",
                    ToolDurability = 88, ToolMaxDurability = 100,
                    Recipes = new[]
                    {
                        new RecipeData { Name = "素面", Glyph = "面", Skin = TagSkin.Common, Unlocked = true, StatusText = "已掌握", Output = "素面 ×2" },
                        new RecipeData { Name = "炙肉", Glyph = "肉", Skin = TagSkin.Common, Unlocked = true, StatusText = "已掌握", Output = "炙肉 ×2" },
                        new RecipeData { Name = "八珍羹", Glyph = "羹", Skin = TagSkin.Rare, Unlocked = false, StatusText = "Lv.9 解锁", Output = "八珍羹 ×1" }
                    }
                },
                new SkillData
                {
                    Name = "缝纫", Glyph = "缝",
                    GlyphColor = InkWashTheme.Info, IconBorder = InkWashTheme.Info,
                    LevelSkin = TagSkin.Common, BarVariant = InkBarFillVariant.Gold,
                    Level = 2, ExpCur = 250, ExpMax = 1000,
                    Description = "裁布制衣，缝补破损的江湖行装",
                    ToolName = "绣花针", ToolQuality = "精良",
                    ToolDurability = 70, ToolMaxDurability = 100,
                    Recipes = new[]
                    {
                        new RecipeData { Name = "粗布衣", Glyph = "布", Skin = TagSkin.Common, Unlocked = true, StatusText = "已掌握", Output = "粗布衣 ×1" },
                        new RecipeData { Name = "锦缎袍", Glyph = "缎", Skin = TagSkin.Rare, Unlocked = false, StatusText = "Lv.7 解锁", Output = "锦缎袍 ×1" }
                    }
                }
            };
        }

        /// <summary>已掌握技能数（设计稿「已掌握 4」— 等级达到 4 级视为已掌握）。</summary>
        private int CountMasteredSkills()
        {
            int count = 0;
            foreach (SkillData skill in _skills)
            {
                if (skill.Level >= 4)
                    count++;
            }
            return count;
        }

        private static int CountUnlocked(RecipeData[] recipes)
        {
            int count = 0;
            foreach (RecipeData recipe in recipes)
            {
                if (recipe.Unlocked)
                    count++;
            }
            return count;
        }

        // ───────────── Build: 背景 ─────────────

        private void BuildBackground()
        {
            _bgLayer = new InkBackgroundLayer
            {
                Parent = this
            };

            _vignette = new InkVignette
            {
                Parent = this
            };
        }

        // ───────────── Build: 侧边导航栏 ─────────────

        private void BuildSidebar()
        {
            _sidebar = new Panel
            {
                BackgroundColor = InkWashTheme.Void,
                Parent = this
            };

            _logoLabel = new Label
            {
                Text = "混沌世界",
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Display, 20f),
                TextColor = InkWashTheme.TextBrand,
                HorizontalAlignment = TextAlignment.Center,
                VerticalAlignment = TextAlignment.Center,
                Parent = _sidebar
            };

            string[] mainItems = { "任务", "博物志", "武林录", "营生", "组队", "邮箱", "商店" };
            string[] mainPaths =
            {
                InkPageDomIds.NavQuests, InkPageDomIds.NavBestiary, InkPageDomIds.NavMartialRecord,
                InkPageDomIds.NavLivelihood, InkPageDomIds.NavTeam, InkPageDomIds.NavMail, InkPageDomIds.NavShop
            };

            for (int i = 0; i < mainItems.Length; i++)
            {
                bool active = mainPaths[i] == InkPageDomIds.NavLivelihood;
                string path = mainPaths[i];
                var btn = new InkButton
                {
                    Text = mainItems[i],
                    ButtonSize = InkButtonSize.Sm,
                    Variant = InkButtonVariant.Ghost,
                    BackgroundColor = active ? Tint(InkWashTheme.GoldPrimary, 0.1f) : Color.Transparent,
                    TextColor = active ? InkWashTheme.TextBrand : InkWashTheme.TextSecondary,
                    BorderColor = active ? InkWashTheme.GoldPrimary : Color.Transparent,
                    Parent = _sidebar
                };
                if (!active)
                    btn.Clicked += () => NavigationRequested?.Invoke(path);
                _navButtons.Add(btn);
            }

            _navDivider = new Panel
            {
                BackgroundColor = InkWashTheme.Divider,
                Parent = _sidebar
            };

            string[] mutedItems = { "角色", "装备", "武学", "好友" };
            string[] mutedPaths =
            {
                InkPageDomIds.NavPersonalInfo, InkPageDomIds.NavEquipment,
                InkPageDomIds.NavSkillPanel, InkPageDomIds.NavFriends
            };

            for (int i = 0; i < mutedItems.Length; i++)
            {
                string path = mutedPaths[i];
                var btn = new InkButton
                {
                    Text = mutedItems[i],
                    ButtonSize = InkButtonSize.Sm,
                    Variant = InkButtonVariant.Ghost,
                    BackgroundColor = Color.Transparent,
                    TextColor = InkWashTheme.TextTertiary,
                    BorderColor = Color.Transparent,
                    Parent = _sidebar
                };
                btn.Clicked += () => NavigationRequested?.Invoke(path);
                _navMutedButtons.Add(btn);
            }

            _settingsDivider = new Panel
            {
                BackgroundColor = InkWashTheme.Divider,
                Parent = _sidebar
            };

            _settingsButton = new InkButton
            {
                Text = "设置",
                ButtonSize = InkButtonSize.Sm,
                Variant = InkButtonVariant.Ghost,
                BackgroundColor = Color.Transparent,
                TextColor = InkWashTheme.TextTertiary,
                BorderColor = Color.Transparent,
                Parent = _sidebar
            };
            _settingsButton.Clicked += () => NavigationRequested?.Invoke(InkPageDomIds.NavSettings);
        }

        // ───────────── Build: 主区域 ─────────────

        private void BuildMainArea()
        {
            _mainArea = new Panel
            {
                BackgroundColor = Color.Transparent,
                ClipChildren = true,
                Parent = this
            };
        }

        // ───────────── Build: 顶栏 ─────────────

        private void BuildTopBar()
        {
            _topBar = new Panel
            {
                BackgroundColor = InkWashTheme.PanelSolid,
                Parent = _mainArea
            };

            _backBtn = new InkButton
            {
                Text = "返回",
                ButtonSize = InkButtonSize.Sm,
                Variant = InkButtonVariant.Ghost,
                Parent = _topBar
            };
            _backBtn.Clicked += () => NavigationRequested?.Invoke(InkPageDomIds.CombatHud);

            _topDivider = new Panel
            {
                BackgroundColor = InkWashTheme.Divider,
                Parent = _topBar
            };

            _avatar = new Panel
            {
                BackgroundColor = InkWashTheme.GoldDeep,
                Parent = _topBar
            };
            var avatarGlyph = new Label
            {
                Text = "侠",
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Heading, 15f),
                TextColor = InkWashTheme.BaseDefault,
                HorizontalAlignment = TextAlignment.Center,
                VerticalAlignment = TextAlignment.Center,
                AnchorPreset = AnchorPresets.StretchAll,
                Parent = _avatar
            };

            _playerNameLabel = new Label
            {
                Text = "无名侠",
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Heading, 15f),
                TextColor = InkWashTheme.TextDefault,
                VerticalAlignment = TextAlignment.Center,
                Parent = _topBar
            };

            _levelTag = new SkinTag
            {
                Text = "Lv.42",
                Skin = TagSkin.Brand,
                Parent = _topBar
            };

            _sectLabel = new Label
            {
                Text = "逍遥派 · 内门弟子",
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Body, 11f),
                TextColor = InkWashTheme.TextTertiary,
                VerticalAlignment = TextAlignment.Center,
                Parent = _topBar
            };

            string[] currencyValues = { "12,450", "328", "15" };
            Color[] currencyColors = { InkWashTheme.GoldPrimary, InkWashTheme.JadeBright, InkWashTheme.VermilionBright };
            for (int i = 0; i < 3; i++)
            {
                _currencyDots[i] = new Panel
                {
                    Size = new Float2(14f, 14f),
                    BackgroundColor = currencyColors[i],
                    Parent = _topBar
                };
                _currencyValues[i] = new Label
                {
                    Text = currencyValues[i],
                    Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Number, 13f),
                    TextColor = InkWashTheme.TextDefault,
                    VerticalAlignment = TextAlignment.Center,
                    Parent = _topBar
                };
            }
        }

        // ───────────── Build: 页面标题行 ─────────────

        private void BuildPageHeader()
        {
            _pageTitle = new Label
            {
                Text = "营生",
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Display, 28f),
                TextColor = InkWashTheme.TextBrand,
                VerticalAlignment = TextAlignment.Center,
                Parent = _mainArea
            };

            _pageSubtitle = new Label
            {
                Text = "采集制造，谋生之道",
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Body, 13f),
                TextColor = InkWashTheme.TextTertiary,
                VerticalAlignment = TextAlignment.Center,
                Parent = _mainArea
            };

            _skillCountTag = new SkinTag
            {
                Skin = TagSkin.Default,
                Parent = _mainArea
            };

            _masteredTag = new SkinTag
            {
                Skin = TagSkin.Brand,
                Parent = _mainArea
            };
        }

        // ───────────── Build: 左栏营生技能 ─────────────

        private void BuildSkillPanel()
        {
            _skillPanel = new InkPanel
            {
                Parent = _mainArea
            };

            _skillCorners = new InkCornerDeco
            {
                Parent = _skillPanel
            };

            _skillPanelTitle = new InkPanelTitle
            {
                Title = "营生技能",
                Parent = _skillPanel
            };

            _skillPanelHint = new Label
            {
                Text = "点击切换",
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Body, 11f),
                TextColor = InkWashTheme.TextTertiary,
                HorizontalAlignment = TextAlignment.Far,
                VerticalAlignment = TextAlignment.Center,
                Parent = _skillPanel
            };

            for (int i = 0; i < _skills.Count; i++)
                _skillViews.Add(BuildSkillItem(i));
        }

        private SkillItemView BuildSkillItem(int idx)
        {
            SkillData skill = _skills[idx];
            var view = new SkillItemView();

            view.Root = new ClickablePanel
            {
                NormalBackground = Color.Transparent,
                HoverBackground = Tint(InkWashTheme.GoldPrimary, 0.05f),
                Parent = _skillPanel
            };
            view.Root.Clicked += () => SelectSkill(idx);

            view.Icon = new OutlineIconBox
            {
                Size = new Float2(32f, 32f),
                BackgroundColor = Tint(skill.GlyphColor, 0.15f),
                OutlineColor = skill.IconBorder,
                Parent = view.Root
            };

            view.Glyph = new Label
            {
                Text = skill.Glyph,
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Display, 16f),
                TextColor = skill.GlyphColor,
                HorizontalAlignment = TextAlignment.Center,
                VerticalAlignment = TextAlignment.Center,
                AnchorPreset = AnchorPresets.StretchAll,
                Parent = view.Icon
            };

            view.Name = new Label
            {
                Text = skill.Name,
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Heading, 14f),
                TextColor = InkWashTheme.TextDefault,
                VerticalAlignment = TextAlignment.Center,
                Parent = view.Root
            };

            view.Level = new SkinTag
            {
                Text = $"Lv.{skill.Level}",
                Skin = skill.LevelSkin,
                Parent = view.Root
            };

            view.Bar = new InkBar
            {
                Height = 8f,
                FillVariant = skill.BarVariant,
                Parent = view.Root
            };
            view.Bar.Value = skill.ExpMax > 0 ? skill.ExpCur / (float)skill.ExpMax : 0f;

            view.Exp = new Label
            {
                Text = $"{skill.ExpCur}/{skill.ExpMax}",
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Number, 11f),
                TextColor = InkWashTheme.TextTertiary,
                HorizontalAlignment = TextAlignment.Far,
                VerticalAlignment = TextAlignment.Center,
                Parent = view.Root
            };

            return view;
        }

        // ───────────── Build: 右栏技能详情 ─────────────

        private void BuildDetailPanel()
        {
            _detailPanel = new InkPanel
            {
                Parent = _mainArea
            };

            _detailCorners = new InkCornerDeco
            {
                Parent = _detailPanel
            };

            _detailIcon = new OutlineIconBox
            {
                Size = new Float2(40f, 40f),
                Parent = _detailPanel
            };

            _detailGlyph = new Label
            {
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Display, 20f),
                HorizontalAlignment = TextAlignment.Center,
                VerticalAlignment = TextAlignment.Center,
                AnchorPreset = AnchorPresets.StretchAll,
                Parent = _detailIcon
            };

            _detailName = new Label
            {
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Heading, 20f),
                TextColor = InkWashTheme.TextDefault,
                VerticalAlignment = TextAlignment.Center,
                Parent = _detailPanel
            };

            _detailLevelTag = new SkinTag
            {
                Parent = _detailPanel
            };

            _detailDesc = new Label
            {
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Body, 11f),
                TextColor = InkWashTheme.TextTertiary,
                VerticalAlignment = TextAlignment.Center,
                Wrapping = TextWrapping.WrapWords,
                Parent = _detailPanel
            };

            _expCaption = new Label
            {
                Text = "经验值",
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Body, 11f),
                TextColor = InkWashTheme.TextTertiary,
                HorizontalAlignment = TextAlignment.Far,
                VerticalAlignment = TextAlignment.Center,
                Parent = _detailPanel
            };

            _expValue = new Label
            {
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Number, 13f),
                TextColor = InkWashTheme.TextDefault,
                HorizontalAlignment = TextAlignment.Far,
                VerticalAlignment = TextAlignment.Center,
                Parent = _detailPanel
            };

            _expBar = new InkBar
            {
                Height = 8f,
                Parent = _detailPanel
            };

            _detailHeaderDivider = new Panel
            {
                BackgroundColor = InkWashTheme.Divider,
                Parent = _detailPanel
            };

            _recipeTitle = new InkPanelTitle
            {
                Title = "可采集配方",
                Parent = _detailPanel
            };

            _recipeMasteredHint = new Label
            {
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Body, 11f),
                TextColor = InkWashTheme.TextTertiary,
                HorizontalAlignment = TextAlignment.Far,
                VerticalAlignment = TextAlignment.Center,
                Parent = _detailPanel
            };

            _recipeList = new Panel
            {
                BackgroundColor = Color.Transparent,
                ClipChildren = true,
                Parent = _detailPanel
            };

            // 工具耐久底栏
            _toolDivider = new Panel
            {
                BackgroundColor = InkWashTheme.Divider,
                Parent = _detailPanel
            };

            _toolIcon = new OutlineIconBox
            {
                Size = new Float2(24f, 24f),
                BackgroundColor = Color.Transparent,
                OutlineColor = InkWashTheme.BorderGold,
                Parent = _detailPanel
            };

            _toolGlyph = new Label
            {
                Text = "锄",
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Display, 13f),
                TextColor = InkWashTheme.GoldPrimary,
                HorizontalAlignment = TextAlignment.Center,
                VerticalAlignment = TextAlignment.Center,
                AnchorPreset = AnchorPresets.StretchAll,
                Parent = _toolIcon
            };

            _toolName = new Label
            {
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Heading, 14f),
                TextColor = InkWashTheme.TextDefault,
                VerticalAlignment = TextAlignment.Center,
                Parent = _detailPanel
            };

            _toolQualityTag = new SkinTag
            {
                Skin = TagSkin.Common,
                Parent = _detailPanel
            };

            _toolDurabilityCaption = new Label
            {
                Text = "耐久",
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Body, 11f),
                TextColor = InkWashTheme.TextTertiary,
                VerticalAlignment = TextAlignment.Center,
                Parent = _detailPanel
            };

            _toolDurabilityBar = new InkBar
            {
                Height = 8f,
                Width = 160f,
                FillVariant = InkBarFillVariant.Gold,
                Parent = _detailPanel
            };

            _toolDurabilityValue = new Label
            {
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Number, 11f),
                TextColor = InkWashTheme.TextDefault,
                VerticalAlignment = TextAlignment.Center,
                Parent = _detailPanel
            };

            _repairButton = new InkButton
            {
                Text = "修理",
                Width = 96f,
                Height = 36f,
                ButtonSize = InkButtonSize.Md,
                Variant = InkButtonVariant.Primary,
                Parent = _detailPanel
            };
            _repairButton.Clicked += OnRepairClicked;
        }

        // ───────────── Build: 底部状态栏 ─────────────

        private void BuildFooter()
        {
            _footer = new Panel
            {
                BackgroundColor = InkWashTheme.Void,
                Parent = _mainArea
            };

            _serverLabel = new Label
            {
                Text = "服务器：逍遥江湖",
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Body, 11f),
                TextColor = InkWashTheme.TextTertiary,
                VerticalAlignment = TextAlignment.Center,
                Parent = _footer
            };

            _versionLabel = new Label
            {
                Text = "延迟 32ms · 版本 v1.2.4",
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Number, 11f),
                TextColor = InkWashTheme.TextTertiary,
                HorizontalAlignment = TextAlignment.Far,
                VerticalAlignment = TextAlignment.Center,
                Parent = _footer
            };
        }

        // ───────────── 交互 ─────────────

        /// <summary>切换选中的营生技能（设计稿左栏「点击切换」）。</summary>
        private void SelectSkill(int idx)
        {
            if (idx < 0 || idx >= _skills.Count || idx == _activeSkillIdx)
                return;

            _activeSkillIdx = idx;
            RefreshAllData();
            ApplyLayout();
            FlaxEngine.Debug.Log($"[MenuLivelihoodPage] 切换营生技能: {_skills[_activeSkillIdx].Name}");
        }

        /// <summary>修理当前工具：耐久恢复至上限。</summary>
        private void OnRepairClicked()
        {
            SkillData skill = _skills[_activeSkillIdx];
            skill.ToolDurability = skill.ToolMaxDurability;
            UpdateToolBar();
            FlaxEngine.Debug.Log($"[MenuLivelihoodPage] 修理 {skill.ToolName}，耐久 {skill.ToolDurability}/{skill.ToolMaxDurability}");
        }

        /// <summary>点击配方条目：已掌握则采集，未解锁则提示等级门槛。</summary>
        private void OnRecipeClicked(RecipeView view)
        {
            if (view.Unlocked)
                FlaxEngine.Debug.Log($"[MenuLivelihoodPage] 采集 {view.RecipeName}");
            else
                FlaxEngine.Debug.Log($"[MenuLivelihoodPage] {view.RecipeName} 尚未解锁");
        }

        // ───────────── 数据刷新 ─────────────

        private void RefreshAllData()
        {
            SkillData active = _skills[_activeSkillIdx];

            // 左栏：等级 / 经验 / 选中态
            for (int i = 0; i < _skillViews.Count; i++)
            {
                SkillItemView view = _skillViews[i];
                SkillData skill = _skills[i];
                bool on = i == _activeSkillIdx;

                view.Level.Text = $"Lv.{skill.Level}";
                view.Exp.Text = $"{skill.ExpCur}/{skill.ExpMax}";
                view.Bar.Value = skill.ExpMax > 0 ? skill.ExpCur / (float)skill.ExpMax : 0f;

                view.Root.NormalBackground = on ? Tint(InkWashTheme.GoldPrimary, 0.08f) : Color.Transparent;
                view.Root.HoverBackground = on ? Tint(InkWashTheme.GoldPrimary, 0.1f) : Tint(InkWashTheme.GoldPrimary, 0.05f);
                view.Root.ApplyNormal();

                view.Name.TextColor = InkWashTheme.TextDefault;
            }

            // 标题行标签
            _skillCountTag.Text = $"技能总数 {_skills.Count}";
            _masteredTag.Text = $"已掌握 {CountMasteredSkills()}";

            // 右栏：技能详情头部
            _detailGlyph.Text = active.Glyph;
            _detailGlyph.TextColor = active.GlyphColor;
            _detailIcon.BackgroundColor = Tint(active.GlyphColor, 0.15f);
            _detailIcon.OutlineColor = active.IconBorder;

            _detailName.Text = active.Name;
            _detailLevelTag.Text = $"Lv.{active.Level}";
            _detailLevelTag.Skin = active.LevelSkin;
            _detailDesc.Text = active.Description;

            _expValue.Text = $"{active.ExpCur:N0} / {active.ExpMax:N0}";
            _expBar.FillVariant = active.BarVariant;
            _expBar.Value = active.ExpMax > 0 ? active.ExpCur / (float)active.ExpMax : 0f;

            _recipeMasteredHint.Text = $"已掌握 {CountUnlocked(active.Recipes)} / {active.Recipes.Length}";

            RebuildRecipeList();
            UpdateToolBar();
        }

        private void UpdateToolBar()
        {
            SkillData active = _skills[_activeSkillIdx];

            _toolName.Text = active.ToolName;
            _toolQualityTag.Text = active.ToolQuality;
            if (_toolGlyph != null)
            {
                _toolGlyph.Text = string.IsNullOrEmpty(active.ToolName)
                    ? "具"
                    : active.ToolName.Substring(active.ToolName.Length - 1);
            }
            _toolDurabilityValue.Text = $"{active.ToolDurability}/{active.ToolMaxDurability}";
            _toolDurabilityBar.Value = active.ToolMaxDurability > 0
                ? active.ToolDurability / (float)active.ToolMaxDurability
                : 0f;
        }

        private void RebuildRecipeList()
        {
            foreach (RecipeView view in _recipeViews)
                view.Root.Dispose();
            _recipeViews.Clear();

            SkillData active = _skills[_activeSkillIdx];
            foreach (RecipeData recipe in active.Recipes)
                _recipeViews.Add(BuildRecipeItem(recipe, active));
        }

        private RecipeView BuildRecipeItem(RecipeData recipe, SkillData skill)
        {
            var view = new RecipeView
            {
                Unlocked = recipe.Unlocked,
                RecipeName = recipe.Name
            };

            view.Root = new ClickablePanel
            {
                NormalBackground = Color.Transparent,
                HoverBackground = Tint(InkWashTheme.GoldPrimary, 0.04f),
                Parent = _recipeList
            };
            view.Root.Clicked += () => OnRecipeClicked(view);

            Color iconColor = recipe.Unlocked
                ? InkWashTheme.QualityColor(QualityOf(recipe.Skin))
                : InkWashTheme.TextTertiary;

            view.Icon = new OutlineIconBox
            {
                Size = new Float2(32f, 32f),
                BackgroundColor = Tint(iconColor, 0.1f),
                OutlineColor = recipe.Unlocked ? iconColor : InkWashTheme.BorderNeutralL2,
                Parent = view.Root
            };

            view.Glyph = new Label
            {
                Text = recipe.Glyph,
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Display, 15f),
                TextColor = iconColor,
                HorizontalAlignment = TextAlignment.Center,
                VerticalAlignment = TextAlignment.Center,
                AnchorPreset = AnchorPresets.StretchAll,
                Parent = view.Icon
            };

            view.Name = new Label
            {
                Text = recipe.Name,
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Heading, 14f),
                TextColor = recipe.Unlocked ? InkWashTheme.TextDefault : InkWashTheme.TextTertiary,
                VerticalAlignment = TextAlignment.Center,
                Parent = view.Root
            };

            view.Status = new SkinTag
            {
                Text = recipe.StatusText,
                Skin = recipe.Skin,
                Parent = view.Root
            };

            view.Output = new Label
            {
                Text = recipe.Output,
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Body, 12f),
                TextColor = recipe.Unlocked ? InkWashTheme.TextTertiary : InkWashTheme.TextFaint,
                HorizontalAlignment = TextAlignment.Far,
                VerticalAlignment = TextAlignment.Center,
                Parent = view.Root
            };

            view.Tool = new Label
            {
                Text = skill.ToolName,
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Body, 12f),
                TextColor = recipe.Unlocked ? InkWashTheme.TextTertiary : InkWashTheme.TextFaint,
                HorizontalAlignment = TextAlignment.Center,
                VerticalAlignment = TextAlignment.Center,
                Parent = view.Root
            };

            return view;
        }

        /// <summary>标签皮肤 → 品质等级（用于配方图标底色/描边）。</summary>
        private static InkWashTheme.InkQuality QualityOf(TagSkin skin)
        {
            switch (skin)
            {
                case TagSkin.Epic: return InkWashTheme.InkQuality.Epic;
                case TagSkin.Rare: return InkWashTheme.InkQuality.Rare;
                case TagSkin.Jade: return InkWashTheme.InkQuality.Uncommon;
                case TagSkin.Brand: return InkWashTheme.InkQuality.Legendary;
                default: return InkWashTheme.InkQuality.Common;
            }
        }

        // ───────────── 布局 ─────────────

        private void ApplyLayout()
        {
            float sw = _screenSize.X;
            float sh = _screenSize.Y;
            float mainW = sw - SidebarW;

            if (_bgLayer != null)
            {
                _bgLayer.Location = Float2.Zero;
                _bgLayer.Size = new Float2(sw, sh);
            }
            if (_vignette != null)
            {
                _vignette.Location = Float2.Zero;
                _vignette.Size = new Float2(sw, sh);
            }

            LayoutSidebar(sh);
            LayoutMainArea(mainW, sh);
            LayoutTopBar(mainW);

            float contentTop = TopBarH + ContentPad;
            float titleH = 40f;
            float columnsTop = contentTop + titleH + 20f;
            float columnsH = Mathf.Max(240f, sh - FooterH - columnsTop - ContentPad);

            LayoutPageHeader(mainW, contentTop, titleH);

            float colW = Mathf.Max(320f, mainW - ContentPad * 2f - ColumnGap);
            float leftW = colW * LeftRatio;
            float rightW = colW - leftW;
            float leftX = ContentPad;
            float rightX = leftX + leftW + ColumnGap;

            LayoutSkillPanel(leftX, columnsTop, leftW, columnsH);
            LayoutDetailPanel(rightX, columnsTop, rightW, columnsH);
            LayoutFooter(mainW, sh);
        }

        private void LayoutSidebar(float sh)
        {
            if (_sidebar == null)
                return;

            _sidebar.Location = Float2.Zero;
            _sidebar.Size = new Float2(SidebarW, sh);

            if (_logoLabel != null)
            {
                _logoLabel.Location = Float2.Zero;
                _logoLabel.Size = new Float2(SidebarW, TopBarH);
            }

            float navW = SidebarW - NavSidePad * 2f;
            float navY = TopBarH + 12f;

            foreach (InkButton btn in _navButtons)
            {
                btn.Location = new Float2(NavSidePad, navY);
                btn.Size = new Float2(navW, NavItemH);
                navY += NavItemStep;
            }

            navY += 4f;
            if (_navDivider != null)
            {
                _navDivider.Location = new Float2(NavSidePad + 4f, navY);
                _navDivider.Size = new Float2(navW - 8f, 1f);
            }
            navY += 12f;

            foreach (InkButton btn in _navMutedButtons)
            {
                btn.Location = new Float2(NavSidePad, navY);
                btn.Size = new Float2(navW, NavItemH);
                navY += NavItemStep;
            }

            float settingsTop = sh - 8f - NavItemH;
            if (_settingsDivider != null)
            {
                _settingsDivider.Location = new Float2(NavSidePad + 4f, settingsTop - 10f);
                _settingsDivider.Size = new Float2(navW - 8f, 1f);
            }
            if (_settingsButton != null)
            {
                _settingsButton.Location = new Float2(NavSidePad, settingsTop);
                _settingsButton.Size = new Float2(navW, NavItemH);
            }
        }

        private void LayoutMainArea(float mainW, float sh)
        {
            if (_mainArea == null)
                return;

            _mainArea.Location = new Float2(SidebarW, 0f);
            _mainArea.Size = new Float2(mainW, sh);
        }

        private void LayoutTopBar(float mainW)
        {
            if (_topBar == null)
                return;

            _topBar.Location = Float2.Zero;
            _topBar.Size = new Float2(mainW, TopBarH);

            float centerY = TopBarH * 0.5f;
            float x = 20f;

            if (_backBtn != null)
            {
                _backBtn.Location = new Float2(x, centerY - 14f);
                _backBtn.Size = new Float2(72f, 28f);
            }
            x += 84f;

            if (_topDivider != null)
            {
                _topDivider.Location = new Float2(x, centerY - 16f);
                _topDivider.Size = new Float2(1f, 32f);
            }
            x += 12f;

            if (_avatar != null)
            {
                _avatar.Location = new Float2(x, centerY - 18f);
                _avatar.Size = new Float2(36f, 36f);
            }
            x += 48f;

            if (_playerNameLabel != null)
            {
                _playerNameLabel.Location = new Float2(x, centerY - 20f);
                _playerNameLabel.Size = new Float2(96f, 20f);
            }
            if (_levelTag != null)
            {
                _levelTag.Location = new Float2(x + 84f, centerY - 20f);
                _levelTag.Size = new Float2(48f, 20f);
            }
            if (_sectLabel != null)
            {
                _sectLabel.Location = new Float2(x, centerY + 2f);
                _sectLabel.Size = new Float2(220f, 16f);
            }

            // 右侧三种货币：色点 + 数值，组间距 24
            const float valueW = 64f;
            const float dotW = 14f;
            const float dotGap = 6f;
            const float groupGap = 24f;
            float groupW = dotW + dotGap + valueW;
            float totalW = groupW * 3f + groupGap * 2f;
            float startX = mainW - ContentPad - totalW;

            for (int i = 0; i < 3; i++)
            {
                float gx = startX + i * (groupW + groupGap);
                if (_currencyDots[i] != null)
                {
                    _currencyDots[i].Location = new Float2(gx, centerY - 7f);
                    _currencyDots[i].Size = new Float2(dotW, dotW);
                }
                if (_currencyValues[i] != null)
                {
                    _currencyValues[i].Location = new Float2(gx + dotW + dotGap, centerY - 11f);
                    _currencyValues[i].Size = new Float2(valueW, 22f);
                }
            }
        }

        private void LayoutPageHeader(float mainW, float contentTop, float titleH)
        {
            if (_pageTitle != null)
            {
                _pageTitle.Location = new Float2(ContentPad, contentTop - 4f);
                _pageTitle.Size = new Float2(120f, titleH);
            }

            if (_pageSubtitle != null)
            {
                _pageSubtitle.Location = new Float2(ContentPad + 116f, contentTop + 2f);
                _pageSubtitle.Size = new Float2(260f, titleH - 8f);
            }

            float tagY = contentTop + 8f;
            const float masteredW = 84f;
            const float countW = 96f;

            if (_masteredTag != null)
            {
                _masteredTag.Location = new Float2(mainW - ContentPad - masteredW, tagY);
                _masteredTag.Size = new Float2(masteredW, 22f);
            }
            if (_skillCountTag != null)
            {
                _skillCountTag.Location = new Float2(mainW - ContentPad - masteredW - 10f - countW, tagY);
                _skillCountTag.Size = new Float2(countW, 22f);
            }
        }

        private void LayoutSkillPanel(float x, float y, float w, float h)
        {
            if (_skillPanel == null)
                return;

            _skillPanel.Location = new Float2(x, y);
            _skillPanel.Size = new Float2(w, h);

            if (_skillCorners != null)
            {
                _skillCorners.Location = Float2.Zero;
                _skillCorners.Size = new Float2(w, h);
            }
            if (_skillPanelTitle != null)
            {
                _skillPanelTitle.Location = Float2.Zero;
                _skillPanelTitle.Size = new Float2(w, PanelTitleH);
            }
            if (_skillPanelHint != null)
            {
                _skillPanelHint.Location = new Float2(w - 12f - 80f, 0f);
                _skillPanelHint.Size = new Float2(80f, PanelTitleH);
            }

            float listTop = PanelTitleH + 8f;
            float itemX = 12f;
            float itemW = Mathf.Max(120f, w - itemX * 2f);

            int n = _skillViews.Count;
            float gap = SkillItemGap;
            float itemH = SkillItemH;
            float available = Mathf.Max(120f, h - listTop - 12f);
            float need = n * itemH + Mathf.Max(0, n - 1) * gap;
            if (n > 0 && need > available)
            {
                gap = 4f;
                itemH = Mathf.Clamp((available + gap) / n - gap, 56f, SkillItemH);
            }

            for (int i = 0; i < n; i++)
            {
                SkillItemView view = _skillViews[i];
                view.Root.Location = new Float2(itemX, listTop + i * (itemH + gap));
                view.Root.Size = new Float2(itemW, itemH);

                if (view.Icon != null)
                {
                    view.Icon.Location = new Float2(12f, 12f);
                    view.Icon.Size = new Float2(32f, 32f);
                }
                if (view.Name != null)
                {
                    view.Name.Location = new Float2(54f, 12f);
                    view.Name.Size = new Float2(Mathf.Max(40f, itemW - 54f - 12f - 64f), 32f);
                }
                if (view.Level != null)
                {
                    view.Level.Location = new Float2(itemW - 12f - 56f, 18f);
                    view.Level.Size = new Float2(56f, 20f);
                }
                if (view.Bar != null)
                {
                    view.Bar.Location = new Float2(12f, itemH - 22f);
                    view.Bar.Size = new Float2(Mathf.Max(60f, itemW - 24f - 80f), 8f);
                }
                if (view.Exp != null)
                {
                    view.Exp.Location = new Float2(itemW - 12f - 76f, itemH - 28f);
                    view.Exp.Size = new Float2(76f, 18f);
                }
            }
        }

        private void LayoutDetailPanel(float x, float y, float w, float h)
        {
            if (_detailPanel == null)
                return;

            _detailPanel.Location = new Float2(x, y);
            _detailPanel.Size = new Float2(w, h);

            if (_detailCorners != null)
            {
                _detailCorners.Location = Float2.Zero;
                _detailCorners.Size = new Float2(w, h);
            }

            // 详情头部
            if (_detailIcon != null)
            {
                _detailIcon.Location = new Float2(24f, 16f);
                _detailIcon.Size = new Float2(40f, 40f);
            }

            float nameW = 0f;
            if (_detailName != null)
            {
                nameW = Mathf.Max(40f, _detailName.Text != null ? _detailName.Text.ToString().Length * 22f : 44f);
                _detailName.Location = new Float2(76f, 16f);
                _detailName.Size = new Float2(nameW, 26f);
            }
            if (_detailLevelTag != null)
            {
                _detailLevelTag.Location = new Float2(76f + nameW + 10f, 19f);
                _detailLevelTag.Size = new Float2(48f, 20f);
            }
            if (_detailDesc != null)
            {
                _detailDesc.Location = new Float2(76f, 46f);
                _detailDesc.Size = new Float2(Mathf.Max(120f, w - 76f - 200f), 20f);
            }

            if (_expCaption != null)
            {
                _expCaption.Location = new Float2(w - 24f - 160f, 14f);
                _expCaption.Size = new Float2(160f, 18f);
            }
            if (_expValue != null)
            {
                _expValue.Location = new Float2(w - 24f - 160f, 34f);
                _expValue.Size = new Float2(160f, 22f);
            }
            if (_expBar != null)
            {
                _expBar.Location = new Float2(24f, DetailHeaderH - 24f);
                _expBar.Size = new Float2(Mathf.Max(80f, w - 48f), 8f);
            }
            if (_detailHeaderDivider != null)
            {
                _detailHeaderDivider.Location = new Float2(0f, DetailHeaderH - 1f);
                _detailHeaderDivider.Size = new Float2(w, 1f);
            }

            // 配方区标题
            if (_recipeTitle != null)
            {
                _recipeTitle.Location = new Float2(0f, DetailHeaderH);
                _recipeTitle.Size = new Float2(w, PanelTitleH);
            }
            if (_recipeMasteredHint != null)
            {
                _recipeMasteredHint.Location = new Float2(w - 12f - 160f, DetailHeaderH);
                _recipeMasteredHint.Size = new Float2(160f, PanelTitleH);
            }

            // 配方列表
            float listX = 16f;
            float listTop = DetailHeaderH + PanelTitleH + 12f;
            float listW = Mathf.Max(160f, w - listX * 2f);
            if (_recipeList != null)
            {
                _recipeList.Location = new Float2(listX, listTop);
                _recipeList.Size = new Float2(listW, Mathf.Max(40f, h - listTop - ToolBarH - 12f));
            }

            int rn = _recipeViews.Count;
            float rGap = RecipeItemGap;
            float rH = RecipeItemH;
            float rAvail = Mathf.Max(40f, h - listTop - ToolBarH - 12f);
            float rNeed = rn * rH + Mathf.Max(0, rn - 1) * rGap;
            if (rn > 0 && rNeed > rAvail)
            {
                rGap = 4f;
                rH = Mathf.Clamp((rAvail + rGap) / rn - rGap, 40f, RecipeItemH);
            }

            for (int i = 0; i < rn; i++)
            {
                RecipeView view = _recipeViews[i];
                view.Root.Location = new Float2(0f, i * (rH + rGap));
                view.Root.Size = new Float2(listW, rH);

                if (view.Icon != null)
                {
                    view.Icon.Location = new Float2(14f, (rH - 32f) * 0.5f);
                    view.Icon.Size = new Float2(32f, 32f);
                }
                if (view.Name != null)
                {
                    view.Name.Location = new Float2(56f, 0f);
                    view.Name.Size = new Float2(110f, rH);
                }
                if (view.Status != null)
                {
                    view.Status.Location = new Float2(174f, (rH - 20f) * 0.5f);
                    view.Status.Size = new Float2(84f, 20f);
                }

                const float toolW = 96f;
                const float outputW = 130f;
                float toolX = listW - 14f - toolW;
                float outputX = Mathf.Max(266f, toolX - 16f - outputW);

                if (view.Output != null)
                {
                    view.Output.Location = new Float2(outputX, 0f);
                    view.Output.Size = new Float2(outputW, rH);
                }
                if (view.Tool != null)
                {
                    view.Tool.Location = new Float2(toolX, 0f);
                    view.Tool.Size = new Float2(toolW, rH);
                }
            }

            // 工具耐久底栏
            float toolTop = h - ToolBarH;
            if (_toolDivider != null)
            {
                _toolDivider.Location = new Float2(0f, toolTop);
                _toolDivider.Size = new Float2(w, 1f);
            }
            if (_toolIcon != null)
            {
                _toolIcon.Location = new Float2(24f, toolTop + ToolBarH * 0.5f - 12f);
                _toolIcon.Size = new Float2(24f, 24f);
            }

            float tx = 60f;
            float toolNameW = 0f;
            if (_toolName != null)
            {
                toolNameW = Mathf.Max(36f, _toolName.Text != null ? _toolName.Text.ToString().Length * 16f : 64f);
                _toolName.Location = new Float2(tx, toolTop + 12f);
                _toolName.Size = new Float2(toolNameW, 20f);
            }
            if (_toolQualityTag != null)
            {
                _toolQualityTag.Location = new Float2(tx + toolNameW + 8f, toolTop + 12f);
                _toolQualityTag.Size = new Float2(48f, 20f);
            }
            if (_toolDurabilityCaption != null)
            {
                _toolDurabilityCaption.Location = new Float2(tx, toolTop + 38f);
                _toolDurabilityCaption.Size = new Float2(38f, 16f);
            }
            if (_toolDurabilityBar != null)
            {
                _toolDurabilityBar.Location = new Float2(tx + 38f, toolTop + 42f);
                _toolDurabilityBar.Size = new Float2(160f, 8f);
            }
            if (_toolDurabilityValue != null)
            {
                _toolDurabilityValue.Location = new Float2(tx + 38f + 160f + 10f, toolTop + 36f);
                _toolDurabilityValue.Size = new Float2(80f, 18f);
            }
            if (_repairButton != null)
            {
                _repairButton.Location = new Float2(w - 24f - 96f, toolTop + ToolBarH * 0.5f - 18f);
                _repairButton.Size = new Float2(96f, 36f);
            }
        }

        private void LayoutFooter(float mainW, float sh)
        {
            if (_footer == null)
                return;

            _footer.Location = new Float2(0f, sh - FooterH);
            _footer.Size = new Float2(mainW, FooterH);

            if (_serverLabel != null)
            {
                _serverLabel.Location = new Float2(ContentPad, 0f);
                _serverLabel.Size = new Float2(260f, FooterH);
            }
            if (_versionLabel != null)
            {
                _versionLabel.Location = new Float2(mainW - ContentPad - 260f, 0f);
                _versionLabel.Size = new Float2(260f, FooterH);
            }
        }

        // ───────────── IInkPage ─────────────

        /// <summary>基于当前控件实际尺寸刷新布局（父容器尺寸变化时由页面壳调用）。</summary>
        public void RefreshLayout()
        {
            _screenSize = new Float2(Width, Height);
            if (_screenSize.X <= 0f || _screenSize.Y <= 0f)
                _screenSize = new Float2(1920f, 1080f);

            ApplyLayout();
        }
    }
}
