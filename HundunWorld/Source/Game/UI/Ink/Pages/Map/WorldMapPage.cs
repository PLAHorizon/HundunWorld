using FlaxEngine;
using FlaxEngine.GUI;
using Game.Character.Attributes;
using HundunWorld.Game.Navigation;
using HundunWorld.Game.Network;
using HundunWorld.Game.Selection;
using HundunWorld.Game.UI.Ink;
using HundunWorld.Game.UI.Ink.Components;
using HundunWorld.Game.UI.StyleSystem;
using System;
using System.Collections.Generic;

namespace HundunWorld.Game.UI.Ink.Pages.Map
{
    /// <summary>
    /// 世界地图页面 — 对应设计方案 world-map.html。
    /// 全屏网格布局（3 列 188/1fr/256 × 3 行 56/1fr/56，padding/gap 10px）：
    /// 顶栏（区域Tab+标题+单人/多人+关闭）/ 左筛选面板 / 中央地图画布 / 右区域信息 / 底搜索栏。
    /// 严格遵循水墨主题 Token，禁止硬编码色值。
    /// </summary>
    public class WorldMapPage : ContainerControl, IInkPage, IInkNavigationSource
    {
        private const float Edge = 10f;
        private const float Gp = 10f;
        private const float HdrH = 56f;
        private const float SrchH = 56f;
        private const float FltrW = 188f;
        private const float InfoW = 256f;

        private static readonly string[] Regions = { "清河", "开封", "凉州", "江南", "燕北" };

        public event Action<string> NavigationRequested;
        public InkParticleSystem ParticleSystem { get; set; }

        private CharacterAttributesComponent _boundChar;

        // 顶栏
        private InkPanel _hdr;
        private RegionTab[] _regionTabs;
        private int _regionIdx;
        private ModeBtn _modeS, _modeM;
        private InkButton _close;

        // 左筛选
        private InkPanel _fltr;
        private FilterCheck[] _fchecks;
        private FilterCheck[] _gchecks;
        private Label _layerTitle;
        private FilterCheck[] _lchecks;

        // 中央地图
        private MapCanvas _canvas;

        // 右信息
        private InkPanel _info;
        private Label _infoRegName;
        private Label _infoCoordLabel;
        private InkTextBlock _infoExploreTitle;
        private InkStatCell _infoProgress;
        private InkTextBlock _infoCategories1;
        private InkTextBlock _infoCategories2;
        private InkButton _infoExploreBtn;
        private float _coordRefreshTimer;
        private readonly List<Control> _regionWidgets = new List<Control>();

        // 地点卡（点击地图标记后展示；数据只来自导航 POI）
        private ContainerControl _placeCard;
        private Label _placeTitle;
        private Label _placeKind;
        private Label _placeSubtitle;
        private Label _placeCoord;
        private InkButton _placeTravelBtn;
        private InkButton _placeGoBtn;
        private InkButton _placeBackBtn;
        private LocationSelection _selectedLocation;

        /// <summary>地图上被选中地点的只读信息（演示标记不会产生选择，因此这里必然是导航真实数据）。</summary>
        public struct LocationSelection
        {
            public string Tag;
            public string Title;
            public string Subtitle;
            public Vector3 World;
            public bool SupportsFastTravel;
            public bool Discovered;
        }

        // 底搜索
        private InkPanel _srch;
        private TextBox _searchBox;
        private QuickTag[] _qtags;
        private bool[] _qtagStates = { true, true, false, false, true };

        public void BindCharacter(CharacterAttributesComponent c)
        {
            _boundChar = c;
        }

        public WorldMapPage()
        {
            AnchorPreset = AnchorPresets.StretchAll;
            Offsets = Margin.Zero;
            // 全屏铺底由外壳的墨底背景层统一提供（本页为沉浸式全屏页），页面不再自绘。
            BackgroundColor = Color.Transparent;
            ClipChildren = false;
            AutoFocus = false;

            BuildHdr();
            BuildFltr();
            BuildMap();
            BuildInfo();
            BuildSrch();

            // 画布的默认筛选必须来自面板（而不是两边各写一份默认值）：初始也要走同一条同步
            OnMarkerFilterChanged();
            SyncLayers();
        }

        // ===================================================================
        // 顶栏：区域Tab + 标题 + 单人/多人 + 关闭
        // ===================================================================

        private void BuildHdr()
        {
            _hdr = new InkPanel { AnchorPreset = AnchorPresets.TopLeft };
            AddChild(_hdr);

            _regionTabs = new RegionTab[Regions.Length];
            for (int i = 0; i < Regions.Length; i++)
            {
                int ci = i;
                var tab = new RegionTab(Regions[i], i == 0) { AnchorPreset = AnchorPresets.TopLeft };
                tab.Clicked += () => OnRegion(ci);
                _hdr.AddChild(tab);
                _regionTabs[i] = tab;
            }
            _regionIdx = 0;

            var title = new Label
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Text = "天下舆图",
                TextColor = InkWashTheme.GoldPrimary,
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Display, 22f),
                HorizontalAlignment = TextAlignment.Center,
                VerticalAlignment = TextAlignment.Center,
                AutoFocus = false,
            };
            _hdr.AddChild(title);

            // 单人/多人 切换组（金边容器 + 两个 ModeBtn）
            var modeBox = new ContainerControl
            {
                AnchorPreset = AnchorPresets.TopLeft,
                BackgroundColor = Color.Transparent,
                AutoFocus = false,
            };
            _hdr.AddChild(modeBox);

            _modeS = new ModeBtn("单人", true) { AnchorPreset = AnchorPresets.TopLeft };
            _modeS.Clicked += () => SetMode(true);
            modeBox.AddChild(_modeS);
            _modeM = new ModeBtn("多人", false) { AnchorPreset = AnchorPresets.TopLeft };
            _modeM.Clicked += () => SetMode(false);
            modeBox.AddChild(_modeM);
            // 金边由 ModeToggleBorder 绘制
            modeBox.AddChild(new ModeToggleBorder());

            _close = new InkButton
            {
                Variant = InkButtonVariant.Ghost,
                ButtonSize = InkButtonSize.Sm,
                Text = "×",
                AnchorPreset = AnchorPresets.TopLeft,
            };
            _close.ButtonClicked += (b) => NavigationRequested?.Invoke(InkPageDomIds.BackOneLevel);
            _hdr.AddChild(_close);
        }

        /// <summary>
        /// 单人/多人地图切换。多人层的数据来自本地可选中实体注册表（附近玩家）与队伍回执（队友高亮）；
        /// 没有远端玩家时如实提示，不画假点。
        /// </summary>
        private void SetMode(bool single)
        {
            _modeS.IsActive = single;
            _modeM.IsActive = !single;

            int others = _canvas?.SetMultiplayerMode(!single) ?? 0;
            Debug.Log($"[WorldMapPage] 地图模式 single={single} others={others}");
            if (!single)
            {
                UIHelper.ShowToast(others > 0 ? $"多人地图：附近 {others} 名同游" : "多人地图：附近暂无其他玩家",
                    ToastType.Info);
            }
        }

        private void OnRegion(int idx)
        {
            if (_regionIdx == idx) return;

            // 当前只有本地世界一张图（POI/投影都来自 MapTileBounds），
            // 切到别的区域只会改标题、地图与点位却不变——那是在骗人。
            if (idx != 0)
            {
                HundunWorld.Game.UI.UIHelper.ShowToast("该区域地图尚未接入", HundunWorld.Game.UI.ToastType.Info);
                return;
            }

            _regionIdx = idx;
            for (int i = 0; i < _regionTabs.Length; i++)
                _regionTabs[i].IsActive = (i == idx);
            if (_infoRegName != null)
                _infoRegName.Text = Regions[idx];
        }

        // ===================================================================
        // 左筛选面板
        // ===================================================================

        private void BuildFltr()
        {
            _fltr = new InkPanel { AnchorPreset = AnchorPresets.TopLeft };
            AddChild(_fltr);

            var ftitle = new Label
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Text = "◆ 筛选标记",
                TextColor = InkWashTheme.GoldBright,
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Display, 14f),
                HorizontalAlignment = TextAlignment.Near,
                VerticalAlignment = TextAlignment.Center,
                AutoFocus = false,
            };
            _fltr.AddChild(ftitle);

            var fdefs = new (string text, string sub, bool def)[]
            {
                ("界碑", "传送点", true),
                ("蹊跷", "收集品", true),
                ("宝箱", "需天赋", false),
                ("江湖故人", "", true),
            };
            _fchecks = new FilterCheck[fdefs.Length];
            for (int i = 0; i < fdefs.Length; i++)
            {
                var fc = new FilterCheck(fdefs[i].text, fdefs[i].sub, fdefs[i].def, Color.Transparent, false)
                {
                    AnchorPreset = AnchorPresets.TopLeft,
                };
                // 一次勾选 = 一次重算；四个开关的当前状态一起交给画布，画布不自己猜顺序
                fc.Clicked += OnMarkerFilterChanged;
                _fltr.AddChild(fc);
                _fchecks[i] = fc;
            }

            var gtitle = new Label
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Text = "采集标记",
                TextColor = InkWashTheme.TextTertiary,
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Body, 11f),
                HorizontalAlignment = TextAlignment.Near,
                VerticalAlignment = TextAlignment.Center,
                AutoFocus = false,
            };
            _fltr.AddChild(gtitle);

            var gdefs = new (string text, Color dot, bool def)[]
            {
                ("草药", InkWashTheme.ElementWood, true),
                ("树木", InkWashTheme.JadeDeep, true),
                ("走兽", InkWashTheme.ElementEarth, false),
                ("飞禽", InkWashTheme.ElementWater, false),
                ("矿物", InkWashTheme.GoldPrimary, true),
            };
            _gchecks = new FilterCheck[gdefs.Length];
            for (int i = 0; i < gdefs.Length; i++)
            {
                var fc = new FilterCheck(gdefs[i].text, null, gdefs[i].def, gdefs[i].dot, true)
                {
                    AnchorPreset = AnchorPresets.TopLeft,
                };
                fc.Clicked += SyncGatherTags;
                _fltr.AddChild(fc);
                _gchecks[i] = fc;
            }

            _layerTitle = new Label
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Text = "图层",
                TextColor = InkWashTheme.TextTertiary,
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Body, 11f),
                HorizontalAlignment = TextAlignment.Near,
                VerticalAlignment = TextAlignment.Center,
                AutoFocus = false,
            };
            _fltr.AddChild(_layerTitle);

            var ldefs = new (string text, bool def)[]
            {
                ("地名", true),
                ("导航路径", true),
                ("玩家位置", true),
            };
            _lchecks = new FilterCheck[ldefs.Length];
            for (int i = 0; i < ldefs.Length; i++)
            {
                var lc = new FilterCheck(ldefs[i].text, null, ldefs[i].def, Color.Transparent, false)
                {
                    AnchorPreset = AnchorPresets.TopLeft,
                };
                lc.Clicked += SyncLayers;
                _fltr.AddChild(lc);
                _lchecks[i] = lc;
            }

            var hint = new Label
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Text = "仅显示已勾选标记",
                TextColor = InkWashTheme.TextTertiary,
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Body, 11f),
                HorizontalAlignment = TextAlignment.Center,
                VerticalAlignment = TextAlignment.Center,
                AutoFocus = false,
            };
            _fltr.AddChild(hint);
        }

        // ===================================================================
        // 中央地图
        // ===================================================================

        private void BuildMap()
        {
            _canvas = new MapCanvas { AnchorPreset = AnchorPresets.TopLeft };
            _canvas.LocationSelected += OnLocationSelected;
            AddChild(_canvas);
        }

        // ===================================================================
        // 右区域信息
        // ===================================================================

        private void BuildInfo()
        {
            _info = new InkPanel { AnchorPreset = AnchorPresets.TopLeft };
            AddChild(_info);

            _info.AddChild(MakeInfoLabel("当前区域", InkWashTheme.TextTertiary, 11f, InkWashTheme.FontRole.Body));
            // 区域名 / 名望 / 任务数 / 区域简介都没有服务端来源：一律「—」，不摆「X:1234 Y:5678 / 3 个可接」
            _infoRegName = MakeInfoLabel("—", InkWashTheme.GoldPrimary, 32f, InkWashTheme.FontRole.Display);
            _info.AddChild(_infoRegName);
            _infoCoordLabel = MakeInfoLabel("◎ 坐标    —", InkWashTheme.TextDefault, 13f, InkWashTheme.FontRole.Body);
            _info.AddChild(_infoCoordLabel);
            _info.AddChild(MakeInfoLabel("◆ 名望等级    —", InkWashTheme.TextSecondary, 13f, InkWashTheme.FontRole.Body));
            _info.AddChild(MakeInfoLabel("◆ 众生任务    —", InkWashTheme.GoldBright, 13f, InkWashTheme.FontRole.Number));
            _info.AddChild(MakeInfoLabel("区域概览", InkWashTheme.TextTertiary, 11f, InkWashTheme.FontRole.Body));
            _info.AddChild(MakeInfoLabel("区域简介尚未接入服务端", InkWashTheme.TextSecondary, 12f, InkWashTheme.FontRole.Body));

            _infoExploreTitle = new InkTextBlock(InkTextStyle.Heading)
            {
                Text = "区域探索",
                TextColor = InkWashTheme.GoldBright,
                HorizontalAlignment = TextAlignment.Near,
                VerticalAlignment = TextAlignment.Center,
                BackgroundColor = Color.Transparent,
            };
            _infoExploreTitle.Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Heading, 14f);
            _info.AddChild(_infoExploreTitle);

            _infoProgress = new InkStatCell("完成度", "—", "收集/探索服务未接入", InkWashTheme.GoldBright);
            _info.AddChild(_infoProgress);

            _infoCategories1 = new InkTextBlock(InkTextStyle.Caption)
            {
                Text = "镇守  侠迹  明暗故事  众生",
                TextColor = InkWashTheme.TextTertiary,
                HorizontalAlignment = TextAlignment.Near,
                VerticalAlignment = TextAlignment.Center,
                BackgroundColor = Color.Transparent,
            };
            _info.AddChild(_infoCategories1);

            _infoCategories2 = new InkTextBlock(InkTextStyle.Caption)
            {
                Text = "万事知  据点  野外首领    —",
                TextColor = InkWashTheme.TextTertiary,
                HorizontalAlignment = TextAlignment.Near,
                VerticalAlignment = TextAlignment.Center,
                BackgroundColor = Color.Transparent,
            };
            _info.AddChild(_infoCategories2);

            _infoExploreBtn = new InkButton
            {
                Variant = InkButtonVariant.Ghost,
                ButtonSize = InkButtonSize.Sm,
                Text = "查看武林录",
            };
            _infoExploreBtn.Clicked += () => NavigationRequested?.Invoke(InkPageDomIds.NavBestiary);
            _info.AddChild(_infoExploreBtn);

            // 区域信息块默认可见；选中地点后整块让位给地点卡，关闭卡片再恢复。
            for (int i = 0; i < _info.ChildrenCount; i++)
                _regionWidgets.Add(_info.GetChild(i));

            BuildPlaceCard();
        }

        /// <summary>
        /// 地点卡：点击地图标记后替换右侧区域信息。
        /// 只摆导航给得出的字段（标题/副标题/世界坐标/是否可传送）；名望、任务数等没有来源的项不出现。
        /// </summary>
        private void BuildPlaceCard()
        {
            _placeCard = new ContainerControl
            {
                AnchorPreset = AnchorPresets.TopLeft,
                BackgroundColor = Color.Transparent,
                AutoFocus = false,
                Visible = false,
            };
            _info.AddChild(_placeCard);

            _placeTitle = MakeInfoLabel("—", InkWashTheme.GoldPrimary, 22f, InkWashTheme.FontRole.Display);
            _placeCard.AddChild(_placeTitle);

            _placeKind = MakeInfoLabel("—", InkWashTheme.TextSecondary, 12f, InkWashTheme.FontRole.Body);
            _placeCard.AddChild(_placeKind);

            _placeSubtitle = MakeInfoLabel(string.Empty, InkWashTheme.TextTertiary, 12f, InkWashTheme.FontRole.Body);
            _placeCard.AddChild(_placeSubtitle);

            _placeCoord = MakeInfoLabel("◎ 坐标    —", InkWashTheme.TextDefault, 13f, InkWashTheme.FontRole.Body);
            _placeCard.AddChild(_placeCoord);

            _placeTravelBtn = new InkButton
            {
                Variant = InkButtonVariant.Ghost,
                ButtonSize = InkButtonSize.Sm,
                Text = "传送至此",
                AnchorPreset = AnchorPresets.TopLeft,
            };
            _placeTravelBtn.Clicked += OnTravelToSelected;
            _placeCard.AddChild(_placeTravelBtn);

            _placeGoBtn = new InkButton
            {
                Variant = InkButtonVariant.Ghost,
                ButtonSize = InkButtonSize.Sm,
                Text = "前往此处",
                AnchorPreset = AnchorPresets.TopLeft,
            };
            _placeGoBtn.Clicked += OnGoToSelected;
            _placeCard.AddChild(_placeGoBtn);

            _placeBackBtn = new InkButton
            {
                Variant = InkButtonVariant.Ghost,
                ButtonSize = InkButtonSize.Sm,
                Text = "返回区域信息",
                AnchorPreset = AnchorPresets.TopLeft,
            };
            _placeBackBtn.Clicked += HidePlaceCard;
            _placeCard.AddChild(_placeBackBtn);
        }

        private void OnLocationSelected(LocationSelection info)
        {
            _selectedLocation = info;

            _placeTitle.Text = string.IsNullOrEmpty(info.Title) ? "未命名地点" : info.Title;
            _placeKind.Text = info.SupportsFastTravel
                ? "界碑 · 可传送"
                : (info.Discovered ? "标记 · 已发现" : "标记 · 未发现");
            _placeSubtitle.Text = info.Subtitle ?? string.Empty;
            _placeSubtitle.Visible = !string.IsNullOrEmpty(_placeSubtitle.Text);
            _placeCoord.Text = $"◎ 坐标    X:{info.World.X:0}  Z:{info.World.Z:0}";

            _placeTravelBtn.Enabled = info.SupportsFastTravel;
            _placeTravelBtn.Text = info.SupportsFastTravel ? "传送至此" : "此处不可传送";

            for (int i = 0; i < _regionWidgets.Count; i++)
                _regionWidgets[i].Visible = false;
            _placeCard.Visible = true;
            RefreshLayout();
        }

        private void HidePlaceCard()
        {
            if (_placeCard != null) _placeCard.Visible = false;
            for (int i = 0; i < _regionWidgets.Count; i++)
                _regionWidgets[i].Visible = true;
            _selectedLocation = default;
            RefreshLayout();
        }

        private void OnTravelToSelected()
        {
            if (string.IsNullOrEmpty(_selectedLocation.Tag)) return;
            bool ok = PluginNavigationBridge.TryRequestFastTravel(_selectedLocation.Tag);
            UIHelper.ShowToast(ok ? "已开始传送" : "传送失败：该地点当前不可用", ok ? ToastType.Success : ToastType.Warning);
        }

        private void OnGoToSelected()
        {
            bool ok = PluginNavigationBridge.TryGetLocalPlayerController(out var controller)
                && controller.TryStartClickToMove(_selectedLocation.World);
            UIHelper.ShowToast(ok ? "已设为目的地，启程前往" : "无法前往：本地玩家不可用", ok ? ToastType.Success : ToastType.Warning);
        }

        private Label MakeInfoLabel(string text, Color color, float size, InkWashTheme.FontRole role)
        {
            return new Label
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Text = text,
                TextColor = color,
                Font = InkRenderHelper.GetFontRef(role, size),
                HorizontalAlignment = TextAlignment.Near,
                VerticalAlignment = TextAlignment.Near,
                AutoFocus = false,
            };
        }

        // ===================================================================
        // 底搜索栏
        // ===================================================================

        private void BuildSrch()
        {
            _srch = new InkPanel { AnchorPreset = AnchorPresets.TopLeft };
            AddChild(_srch);

            // 搜索输入框（自绘底 + 图标；文字由叠在上面的 TextBox 负责）
            var input = new SearchInput { AnchorPreset = AnchorPresets.TopLeft };
            _srch.AddChild(input);

            _searchBox = new TextBox
            {
                AnchorPreset = AnchorPresets.TopLeft,
                WatermarkText = "搜索地点 / 标记",
                WatermarkTextColor = InkWashTheme.TextTertiary,
                TextColor = InkWashTheme.TextDefault,
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Body, 13f),
                BackgroundColor = Color.Transparent,
                BackgroundSelectedColor = Color.Transparent,
                BorderColor = Color.Transparent,
                BorderSelectedColor = Color.Transparent,
                AutoFocus = false,
            };
            _searchBox.TextChanged += () => _canvas?.SetSearchQuery(_searchBox.Text);
            _srch.AddChild(_searchBox);

            var qdefs = new (string text, Color dot)[]
            {
                ("草药", InkWashTheme.ElementWood),
                ("树木", InkWashTheme.JadeDeep),
                ("走兽", InkWashTheme.ElementEarth),
                ("飞禽", InkWashTheme.ElementWater),
                ("矿物", InkWashTheme.GoldPrimary),
            };
            _qtags = new QuickTag[qdefs.Length];
            for (int i = 0; i < qdefs.Length; i++)
            {
                int ci = i;
                // 初始状态以左栏「采集标记」为准，避免两处各写一份默认值而悄悄分叉
                bool initial = _gchecks != null && i < _gchecks.Length ? _gchecks[i].IsChecked : _qtagStates[i];
                _qtagStates[i] = initial;

                var q = new QuickTag(qdefs[i].text, qdefs[i].dot, initial)
                {
                    AnchorPreset = AnchorPresets.TopLeft,
                };
                q.Clicked += () => ToggleTag(ci);
                _srch.AddChild(q);
                _qtags[i] = q;
            }
        }

        private void ToggleTag(int idx)
        {
            _qtagStates[idx] = !_qtagStates[idx];
            _qtags[idx].IsActive = _qtagStates[idx];

            // 底部快捷标签与左栏「采集标记」是同一个设置的两种呈现，必须同步
            if (_gchecks != null && idx < _gchecks.Length)
                _gchecks[idx].SetChecked(_qtagStates[idx]);
        }

        /// <summary>
        /// 把左栏「采集标记」的勾选状态同步到下方快捷标签。
        /// <para>
        /// 目前导航 POI 视图没有采集类别字段，这两组控件只表达"想看哪类采集物"，不参与地图过滤；
        /// 但两份控件各说各话（左边勾着、右边关着）本身就是可见的 UI 缺陷。
        /// </para>
        /// </summary>
        private void SyncGatherTags()
        {
            if (_gchecks == null || _qtags == null) return;

            int count = Math.Min(_gchecks.Length, _qtags.Length);
            for (int i = 0; i < count; i++)
            {
                bool on = _gchecks[i].IsChecked;
                _qtagStates[i] = on;
                _qtags[i].IsActive = on;
            }
        }

        /// <summary>
        /// 图层开关：地名 / 导航路径 / 玩家位置。三者都只影响绘制，不改变标记数据本身，
        /// 关掉再开回来不需要重新拉导航。
        /// </summary>
        private void SyncLayers()
        {
            if (_canvas == null || _lchecks == null || _lchecks.Length < 3) return;

            _canvas.SetLayerVisibility(
                _lchecks[0].IsChecked,
                _lchecks[1].IsChecked,
                _lchecks[2].IsChecked);
        }

        /// <summary>
        /// 把四个筛选勾选框的当前状态交给地图画布（初始与每次点击都走这里）。
        /// 顺序与 <see cref="BuildFltr"/> 的 fdefs 一致：界碑 / 蹊跷 / 宝箱 / 江湖故人。
        /// </summary>
        private void OnMarkerFilterChanged()
        {
            if (_canvas == null || _fchecks == null || _fchecks.Length < 4) return;

            _canvas.SetMarkerFilter(
                _fchecks[0].IsChecked,
                _fchecks[1].IsChecked,
                _fchecks[2].IsChecked,
                _fchecks[3].IsChecked);
        }

        // ===================================================================
        // 布局
        // ===================================================================

        /// <summary>坐标刷新间隔（秒）。</summary>
        private const float CoordRefreshInterval = 0.5f;

        /// <summary>
        /// 右侧信息面板的坐标取玩家真实世界坐标（平面轴是 X/Z，纵向 Y 不显示在地图上）。
        /// 区域名 / 名望 / 任务数没有数据源，保持「—」。
        /// </summary>
        public override void Update(float dt)
        {
            base.Update(dt);
            TryRunCardSelftest(dt);

            _coordRefreshTimer += dt;
            if (_coordRefreshTimer < CoordRefreshInterval) return;
            _coordRefreshTimer = 0f;

            if (_infoCoordLabel == null) return;
            _infoCoordLabel.Text = PluginNavigationBridge.TryGetPlayerPose(out var pos, out _)
                ? $"◎ 坐标    X:{pos.X:0}  Z:{pos.Z:0}"
                : "◎ 坐标    —";
        }

        private const string SelftestMarkerName = "map-card-selftest.on";
        private const string SelftestReceiptName = "map-card-selftest.out";
        private int _cardSelftestTries;
        private float _cardSelftestCooldown;

        /// <summary>
        /// 地点卡自测：标记文件存在时，用第一个可见真实 POI 的像素坐标走一遍
        /// <c>OnMouseUp</c> 命中链路（与真实点击同一条路径），把结果写回执。
        /// 每 0.5 秒试一次、最多 20 次：页面布局与导航标记都要几帧才就绪，按帧重试会在就绪前耗光次数。
        /// </summary>
        private void TryRunCardSelftest(float dt)
        {
            if (_canvas == null) return;
            if (_cardSelftestTries >= 20) return;

            _cardSelftestCooldown += dt;
            if (_cardSelftestCooldown < 0.5f) return;
            _cardSelftestCooldown = 0f;

            string dir = System.IO.Path.Combine(
                System.Environment.GetFolderPath(System.Environment.SpecialFolder.ApplicationData),
                "HundunWorld");
            if (!System.IO.File.Exists(System.IO.Path.Combine(dir, SelftestMarkerName))) return;

            _cardSelftestTries++;

            if (!_canvas.RunCardSelftest(out var tag)) return;

            string title = string.IsNullOrEmpty(_selectedLocation.Title) ? "-" : _selectedLocation.Title;
            string line = $"[map-card-selftest] ok=True tag={tag} card={_placeCard != null && _placeCard.Visible} " +
                         $"title={title} world=({_selectedLocation.World.X:0},{_selectedLocation.World.Z:0}) " +
                         $"fastTravel={_selectedLocation.SupportsFastTravel} discovered={_selectedLocation.Discovered}";
            try { System.IO.File.AppendAllText(System.IO.Path.Combine(dir, SelftestReceiptName), line + "\n"); }
            catch { }
            Debug.Log(line);
            _cardSelftestTries = 20;
        }

        public void RefreshLayout()
        {
            try
            {
                float w = Width;
                float h = Height;
                float px = Edge;
                float pw = w - Edge * 2f;

                float contentTop = Edge + HdrH + Gp;
                float contentH = h - Edge - SrchH - Gp - contentTop;
                if (contentH < 100f) contentH = 100f;

                LayoutHdr(px, pw);
                LayoutFltr(px, contentTop, contentH);
                LayoutCanvas(px, pw, contentTop, contentH);
                LayoutInfo(px, pw, contentTop, contentH);
                LayoutSrch(px, pw, h);
            }
            catch (Exception ex)
            {
                FlaxEngine.Debug.LogError($"[WorldMapPage] RefreshLayout: {ex.Message}");
            }
        }

        private void LayoutHdr(float px, float pw)
        {
            if (_hdr == null) return;
            _hdr.Location = new Float2(px, Edge);
            _hdr.Size = new Float2(pw, HdrH);

            float tabX = 16f;
            for (int i = 0; i < _regionTabs.Length; i++)
            {
                _regionTabs[i].Location = new Float2(tabX, 0f);
                _regionTabs[i].Size = new Float2(56f, HdrH);
                tabX += 58f;
            }

            // 标题居中
            var title = _hdr.GetChild(5) as Label;
            if (title != null)
            {
                title.Location = new Float2((pw - 200f) * 0.5f, 0f);
                title.Size = new Float2(200f, HdrH);
            }

            // 模式切换组（右侧，关闭按钮左边）
            float closeX = pw - 16f - 32f;
            _close.Location = new Float2(closeX, (HdrH - 24f) * 0.5f);
            _close.Size = new Float2(32f, 24f);

            var modeBox = _hdr.GetChild(6) as ContainerControl;
            if (modeBox != null)
            {
                float mw = 120f, mh = 26f;
                modeBox.Location = new Float2(closeX - 12f - mw, (HdrH - mh) * 0.5f);
                modeBox.Size = new Float2(mw, mh);
                _modeS.Location = Float2.Zero;
                _modeS.Size = new Float2(mw * 0.5f, mh);
                _modeM.Location = new Float2(mw * 0.5f, 0f);
                _modeM.Size = new Float2(mw * 0.5f, mh);
            }
        }

        private void LayoutFltr(float px, float contentTop, float contentH)
        {
            if (_fltr == null) return;
            _fltr.Location = new Float2(px, contentTop);
            _fltr.Size = new Float2(FltrW, contentH);

            float fy = 14f;
            // 标题
            var ftitle = _fltr.GetChild(0) as Label;
            if (ftitle != null)
            {
                ftitle.Location = new Float2(12f, fy);
                ftitle.Size = new Float2(FltrW - 24f, 22f);
                fy += 30f;
            }

            for (int i = 0; i < _fchecks.Length; i++)
            {
                _fchecks[i].Location = new Float2(6f, fy);
                _fchecks[i].Size = new Float2(FltrW - 12f, 24f);
                fy += 26f;
            }

            fy += 6f;
            var gtitle = _fltr.GetChild(1 + _fchecks.Length) as Label;
            if (gtitle != null)
            {
                gtitle.Location = new Float2(16f, fy);
                gtitle.Size = new Float2(FltrW - 28f, 18f);
                fy += 20f;
            }

            for (int i = 0; i < _gchecks.Length; i++)
            {
                _gchecks[i].Location = new Float2(14f, fy);
                _gchecks[i].Size = new Float2(FltrW - 20f, 22f);
                fy += 23f;
            }

            fy += 8f;
            if (_layerTitle != null)
            {
                _layerTitle.Location = new Float2(16f, fy);
                _layerTitle.Size = new Float2(FltrW - 28f, 18f);
                fy += 20f;
            }

            for (int i = 0; i < _lchecks.Length; i++)
            {
                _lchecks[i].Location = new Float2(14f, fy);
                _lchecks[i].Size = new Float2(FltrW - 20f, 22f);
                fy += 23f;
            }

            var hint = _fltr.GetChild(_fltr.ChildrenCount - 1) as Label;
            if (hint != null)
            {
                hint.Location = new Float2(6f, contentH - 28f);
                hint.Size = new Float2(FltrW - 12f, 18f);
            }
        }

        private void LayoutCanvas(float px, float pw, float contentTop, float contentH)
        {
            if (_canvas == null) return;
            float mapX = px + FltrW + Gp;
            float mapW = pw - FltrW - InfoW - Gp * 2f;
            _canvas.Location = new Float2(mapX, contentTop);
            _canvas.Size = new Float2(mapW, contentH);
        }

        private void LayoutInfo(float px, float pw, float contentTop, float contentH)
        {
            if (_info == null) return;
            _info.Location = new Float2(px + pw - InfoW, contentTop);
            _info.Size = new Float2(InfoW, contentH);

            float iy = 18f;
            var labels = new List<Label>();
            for (int i = 0; i < _info.ChildrenCount; i++)
            {
                // 新增的 InkTextBlock/InkStatCell/InkButton 单独布局，不参与旧标签的索引规则。
                if (_info.GetChild(i) is Label lbl && _info.GetChild(i) is not InkTextBlock)
                    labels.Add(lbl);
            }

            for (int i = 0; i < labels.Count; i++)
            {
                float lh;
                if (i == 1) lh = 46f;                 // 区域名 32px
                else if (i == labels.Count - 1) lh = 90f; // 描述
                else lh = 22f;
                labels[i].Location = new Float2(16f, iy);
                labels[i].Size = new Float2(InfoW - 32f, lh);
                iy += lh + (i == 1 ? 10f : 8f);
            }

            if (_infoExploreTitle != null)
            {
                _infoExploreTitle.Location = new Float2(16f, iy + 2f);
                _infoExploreTitle.Size = new Float2(InfoW - 32f, 20f);
                iy += 26f;
            }
            if (_infoProgress != null)
            {
                _infoProgress.Location = new Float2(16f, iy);
                _infoProgress.Size = new Float2(InfoW - 32f, 64f);
                iy += 72f;
            }
            if (_infoCategories1 != null)
            {
                _infoCategories1.Location = new Float2(16f, iy);
                _infoCategories1.Size = new Float2(InfoW - 32f, 16f);
                iy += 18f;
            }
            if (_infoCategories2 != null)
            {
                _infoCategories2.Location = new Float2(16f, iy);
                _infoCategories2.Size = new Float2(InfoW - 32f, 16f);
                iy += 22f;
            }
            if (_infoExploreBtn != null)
            {
                _infoExploreBtn.Location = new Float2(16f, iy);
                _infoExploreBtn.Size = new Float2(110f, 28f);
            }

            LayoutPlaceCard();
        }

        private void LayoutPlaceCard()
        {
            if (_placeCard == null) return;

            float w = InfoW - 32f;
            _placeCard.Location = new Float2(16f, 18f);
            _placeCard.Size = new Float2(w, 250f);

            float y = 0f;
            _placeTitle.Location = new Float2(0f, y);
            _placeTitle.Size = new Float2(w, 32f);
            y += 38f;

            _placeKind.Location = new Float2(0f, y);
            _placeKind.Size = new Float2(w, 18f);
            y += 24f;

            _placeSubtitle.Location = new Float2(0f, y);
            _placeSubtitle.Size = new Float2(w, 18f);
            y += 30f;

            _placeCoord.Location = new Float2(0f, y);
            _placeCoord.Size = new Float2(w, 20f);
            y += 34f;

            _placeTravelBtn.Location = new Float2(0f, y);
            _placeTravelBtn.Size = new Float2(110f, 30f);
            y += 38f;

            _placeGoBtn.Location = new Float2(0f, y);
            _placeGoBtn.Size = new Float2(110f, 30f);
            y += 38f;

            _placeBackBtn.Location = new Float2(0f, y);
            _placeBackBtn.Size = new Float2(110f, 28f);
        }

        private void LayoutSrch(float px, float pw, float h)
        {
            if (_srch == null) return;
            _srch.Location = new Float2(px, h - Edge - SrchH);
            _srch.Size = new Float2(pw, SrchH);

            // 搜索输入框
            var input = _srch.GetChild(0) as SearchInput;
            if (input != null)
            {
                input.Location = new Float2(14f, (SrchH - 34f) * 0.5f);
                input.Size = new Float2(320f, 34f);
            }

            // 真正的输入控件：叠在自绘框上，左右留出搜索/筛选图标的边距
            if (_searchBox != null && input != null)
            {
                _searchBox.Location = new Float2(input.Location.X + 34f, input.Location.Y);
                _searchBox.Size = new Float2(Mathf.Max(80f, input.Width - 64f), input.Height);
            }

            float qx = 14f + 320f + 16f;
            for (int i = 0; i < _qtags.Length; i++)
            {
                _qtags[i].Location = new Float2(qx, (SrchH - 26f) * 0.5f);
                _qtags[i].Size = new Float2(68f, 26f);
                qx += 76f;
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

        /// <summary>区域 Tab（15px Display，激活金亮 + 2px 金色渐变下划线）。</summary>
        private sealed class RegionTab : Control
        {
            private readonly string _text;
            private bool _isActive;
            private bool _isHovered;

            public event Action Clicked;
            public bool IsActive { get => _isActive; set => _isActive = value; }

            public RegionTab(string text, bool active)
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
                var font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Display, 15f).GetFont();
                if (font != null)
                    Render2D.DrawText(font, _text, new Rectangle(Float2.Zero, Size), color,
                        TextAlignment.Center, TextAlignment.Center, TextWrapping.NoWrap);
                if (_isActive)
                {
                    // 2px 金色渐变下划线（透明→金→透明，三段近似）
                    float y = Height - 3f;
                    float seg = (Width - 28f) / 3f;
                    float x0 = 14f;
                    var g = InkWashTheme.GoldPrimary;
                    var c1 = new Color(g.R, g.G, g.B, 0.25f);
                    var c2 = new Color(g.R, g.G, g.B, 1f);
                    Render2D.FillRectangle(new Rectangle(x0, y, seg, 2f), c1);
                    Render2D.FillRectangle(new Rectangle(x0 + seg, y, seg, 2f), c2);
                    Render2D.FillRectangle(new Rectangle(x0 + seg * 2f, y, seg, 2f), c1);
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

        /// <summary>单人/多人切换按钮（激活=金底反色文）。</summary>
        private sealed class ModeBtn : Control
        {
            private readonly string _text;
            private bool _isActive;

            public event Action Clicked;
            public bool IsActive { get => _isActive; set => _isActive = value; }

            public ModeBtn(string text, bool active)
            {
                _text = text;
                _isActive = active;
                AutoFocus = false;
            }

            public override void Draw()
            {
                base.Draw();
                if (!Visible) return;
                var rect = new Rectangle(Float2.Zero, Size);
                if (_isActive)
                    Render2D.FillRectangle(rect, InkWashTheme.GoldPrimary);
                Color tc = _isActive ? InkWashTheme.TextInverse : InkWashTheme.TextSecondary;
                var font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Body, 12f).GetFont();
                if (font != null)
                    Render2D.DrawText(font, _text, rect, tc,
                        TextAlignment.Center, TextAlignment.Center, TextWrapping.NoWrap);
            }

            public override bool OnMouseUp(Float2 location, MouseButton button)
            {
                if (button == MouseButton.Left && ContainsPoint(ref location))
                    Clicked?.Invoke();
                return base.OnMouseUp(location, button);
            }
        }

        /// <summary>模式切换组金边框（radius sm）。</summary>
        private sealed class ModeToggleBorder : Control
        {
            public ModeToggleBorder()
            {
                AutoFocus = false;
                AnchorPreset = AnchorPresets.StretchAll;
                Offsets = Margin.Zero;
            }

            public override void Draw()
            {
                base.Draw();
                if (!Visible) return;
                InkRenderHelper.DrawRoundedRectangle(new Rectangle(Float2.Zero, Size), 2f, InkWashTheme.BorderGold, 1f);
            }
        }

        /// <summary>筛选复选项（14x14 复选框 + 可选色点 + 标签 + 副标）。</summary>
        private sealed class FilterCheck : Control
        {
            private readonly string _text;
            private readonly string _sub;
            private readonly Color _dot;
            private readonly bool _showDot;
            private bool _checked;
            private bool _isHovered;

            public bool IsChecked => _checked;

            /// <summary>勾选状态变化（切换后触发一次）。</summary>
            public event Action Clicked;

            /// <summary>由外部（如底部快捷标签）同步勾选状态；不触发 <see cref="Clicked"/>。</summary>
            public void SetChecked(bool value) => _checked = value;

            public FilterCheck(string text, string sub, bool defChecked, Color dot, bool showDot)
            {
                _text = text;
                _sub = sub;
                _dot = dot;
                _showDot = showDot;
                _checked = defChecked;
                AutoFocus = false;
            }

            public override void Draw()
            {
                base.Draw();
                if (!Visible) return;
                if (_isHovered)
                    InkRenderHelper.FillRoundedRectangle(new Rectangle(Float2.Zero, Size), 2f, InkWashTheme.BgHover);

                // 复选框 14x14
                float by = (Height - 14f) * 0.5f;
                var boxRect = new Rectangle(6f, by, 14f, 14f);
                if (_checked)
                {
                    InkRenderHelper.FillRoundedRectangle(boxRect, 2f, InkWashTheme.GoldPrimary);
                    InkRenderHelper.DrawRoundedRectangle(boxRect, 2f, InkWashTheme.GoldBright, 1f);
                    // 反色对勾（两短线近似）
                    Render2D.DrawLine(new Float2(9f, by + 7f), new Float2(12f, by + 10f), InkWashTheme.TextInverse, 1.5f);
                    Render2D.DrawLine(new Float2(12f, by + 10f), new Float2(17f, by + 4f), InkWashTheme.TextInverse, 1.5f);
                }
                else
                {
                    InkRenderHelper.FillRoundedRectangle(boxRect, 2f,
                        new Color(InkWashTheme.Void.R, InkWashTheme.Void.G, InkWashTheme.Void.B, 0.70f));
                    InkRenderHelper.DrawRoundedRectangle(boxRect, 2f, InkWashTheme.GoldDim, 1f);
                }

                float lx = 28f;
                // 色点（采集项）
                if (_showDot)
                {
                    InkRenderHelper.FillCircle(new Float2(lx + 4f, Height * 0.5f), 4f, _dot);
                    lx += 14f;
                }

                // 标签
                Color tc = _checked ? (_showDot ? InkWashTheme.TextSecondary : InkWashTheme.TextDefault)
                                    : InkWashTheme.TextFaint;
                var font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Body, _showDot ? 12f : 13f).GetFont();
                if (font != null)
                    Render2D.DrawText(font, _text, new Rectangle(lx, 0f, Width - lx - 60f, Height), tc,
                        TextAlignment.Near, TextAlignment.Center, TextWrapping.NoWrap);

                // 副标
                if (!string.IsNullOrEmpty(_sub))
                {
                    var sf = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Body, 11f).GetFont();
                    if (sf != null)
                        Render2D.DrawText(sf, _sub, new Rectangle(Width - 58f, 0f, 52f, Height), InkWashTheme.TextTertiary,
                            TextAlignment.Near, TextAlignment.Center, TextWrapping.NoWrap);
                }
            }

            public override void OnMouseEnter(Float2 location) { _isHovered = true; base.OnMouseEnter(location); }
            public override void OnMouseLeave() { _isHovered = false; base.OnMouseLeave(); }

            public override bool OnMouseUp(Float2 location, MouseButton button)
            {
                if (button == MouseButton.Left && ContainsPoint(ref location))
                {
                    _checked = !_checked;
                    Clicked?.Invoke();
                }
                return base.OnMouseUp(location, button);
            }
        }

        /// <summary>搜索输入框（深底 + 搜索图标 + 占位文字 + 筛选图标）。</summary>
        private sealed class SearchInput : Control
        {
            public SearchInput() { AutoFocus = false; }

            public override void Draw()
            {
                base.Draw();
                if (!Visible) return;
                var rect = new Rectangle(Float2.Zero, Size);
                InkRenderHelper.FillRoundedRectangle(rect, 2f, InkWashTheme.BaseDefault);
                InkRenderHelper.DrawRoundedRectangle(rect, 2f, InkWashTheme.BorderFaint, 1f);

                var iconF = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Body, 15f).GetFont();
                if (iconF != null)
                    Render2D.DrawText(iconF, "⌕", new Rectangle(10f, 0f, 20f, Height), InkWashTheme.TextTertiary,
                        TextAlignment.Center, TextAlignment.Center, TextWrapping.NoWrap);
                // 占位文字由叠加的 TextBox.WatermarkText 绘制（真正可输入）
                if (iconF != null)
                    Render2D.DrawText(iconF, "≡", new Rectangle(Width - 28f, 0f, 20f, Height), InkWashTheme.TextTertiary,
                        TextAlignment.Center, TextAlignment.Center, TextWrapping.NoWrap);
            }
        }

        /// <summary>采集快捷标签（色点 + 文字，激活金底金边）。</summary>
        private sealed class QuickTag : Control
        {
            private readonly string _text;
            private readonly Color _dot;
            private bool _isActive;

            public event Action Clicked;
            public bool IsActive { get => _isActive; set => _isActive = value; }

            public QuickTag(string text, Color dot, bool active)
            {
                _text = text;
                _dot = dot;
                _isActive = active;
                AutoFocus = false;
            }

            public override void Draw()
            {
                base.Draw();
                if (!Visible) return;
                var rect = new Rectangle(Float2.Zero, Size);
                if (_isActive)
                {
                    InkRenderHelper.FillRoundedRectangle(rect, 2f, InkWashTheme.GoldTrace);
                    InkRenderHelper.DrawRoundedRectangle(rect, 2f, InkWashTheme.BorderGold, 1f);
                }
                else
                {
                    InkRenderHelper.DrawRoundedRectangle(rect, 2f, InkWashTheme.BorderNeutralL2, 1f);
                }

                InkRenderHelper.FillCircle(new Float2(12f, Height * 0.5f), 4f, _dot);
                Color tc = _isActive ? InkWashTheme.GoldBright : InkWashTheme.TextSecondary;
                var font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Body, 12f).GetFont();
                if (font != null)
                    Render2D.DrawText(font, _text, new Rectangle(22f, 0f, Width - 26f, Height), tc,
                        TextAlignment.Near, TextAlignment.Center, TextWrapping.NoWrap);
            }

            public override bool OnMouseUp(Float2 location, MouseButton button)
            {
                if (button == MouseButton.Left && ContainsPoint(ref location))
                    Clicked?.Invoke();
                return base.OnMouseUp(location, button);
            }
        }

        /// <summary>地图画布（水墨底 + 标记 + 导航路径 + 罗盘/缩放/图例，全部自绘）。</summary>
        private sealed class MapCanvas : Control
        {
            private enum Mk { Teleport, Oddity, Chest, Npc, Monster, Player, Destination, Place }

            private struct MData
            {
                public Mk Kind;
                public string Tag;
                public string Title;
                public float X, Y;
                public string Subtitle;
                public Vector3 World;
                public bool CanTravel;
                public bool Discovered;
                /// <summary>是否来自导航真实 POI（演示标记为 false，不可选中、不弹地点卡）。</summary>
                public bool IsReal;
            }

            /// <summary>点击真实标记后触发；演示标记永远不会触发。</summary>
            public event Action<LocationSelection> LocationSelected;

            /// <summary>当前选中的标记 Tag（高亮环用）；空表示无选中。</summary>
            private string _selectedTag = string.Empty;

            /// <summary>内置演示标记（NarrativePro 导航不可用时的回退，保证既有观感不变）</summary>
            private static readonly MData[] DemoMarkers =
            {
                new MData { Kind = Mk.Teleport, X = 0.30f, Y = 0.30f },
                new MData { Kind = Mk.Teleport, X = 0.64f, Y = 0.58f },
                new MData { Kind = Mk.Teleport, X = 0.78f, Y = 0.22f },
                new MData { Kind = Mk.Oddity,   X = 0.22f, Y = 0.46f },
                new MData { Kind = Mk.Oddity,   X = 0.45f, Y = 0.36f },
                new MData { Kind = Mk.Oddity,   X = 0.55f, Y = 0.70f },
                new MData { Kind = Mk.Oddity,   X = 0.72f, Y = 0.44f },
                new MData { Kind = Mk.Oddity,   X = 0.38f, Y = 0.62f },
                new MData { Kind = Mk.Chest,    X = 0.50f, Y = 0.50f },
                new MData { Kind = Mk.Chest,    X = 0.84f, Y = 0.64f },
                new MData { Kind = Mk.Npc,      X = 0.36f, Y = 0.40f },
                new MData { Kind = Mk.Npc,      X = 0.60f, Y = 0.34f },
                new MData { Kind = Mk.Npc,      X = 0.48f, Y = 0.76f },
                new MData { Kind = Mk.Npc,      X = 0.68f, Y = 0.50f },
                new MData { Kind = Mk.Monster,  X = 0.26f, Y = 0.64f },
                new MData { Kind = Mk.Monster,  X = 0.74f, Y = 0.38f },
                new MData { Kind = Mk.Monster,  X = 0.42f, Y = 0.24f },
                new MData { Kind = Mk.Player,   X = 0.52f, Y = 0.54f },
            };

            private static readonly (string text, float x, float y, Mk kind)[] DemoPlaces =
            {
                ("清河", 0.26f, 0.20f, Mk.Place),
                ("落雁峰", 0.60f, 0.64f, Mk.Place),
                ("北山道", 0.80f, 0.30f, Mk.Place),
            };

            /// <summary>地图内边距（与 Draw 中的 mx/my 保持一致，命中测试复用）</summary>
            private const float Pad = 16f;

            /// <summary>传送点点击命中半径（像素）</summary>
            private const float TeleportHitRadius = 16f;

            /// <summary>地名标签最大数量（避免 POI 过多时标签堆叠、破坏留白设计）</summary>
            private const int MaxPlaceLabels = 6;

            /// <summary>
            /// 完整标记/地名（未过滤）。筛选只改下面两份"可见"副本，
            /// 因此取消勾选再勾回来不需要重新拉导航数据。
            /// </summary>
            private MData[] _rawMarkers = DemoMarkers;
            private (string text, float x, float y, Mk kind)[] _rawPlaces = DemoPlaces;

            private MData[] _markers = DemoMarkers;
            private (string text, float x, float y)[] _places = System.Array.Empty<(string, float, float)>();

            // 筛选面板四个开关的镜像（设计稿：界碑/蹊跷/宝箱/江湖故人）。
            // 妖魔、玩家、目的地、地名不在这四个开关里，恒为可见。
            private bool _showTeleport = true;
            private bool _showOddity = true;
            private bool _showChest;
            private bool _showNpc = true;

            // 图层开关（只影响绘制，不改标记数据）
            private bool _showPlacesLayer = true;
            private bool _showNavPathLayer = true;
            private bool _showPlayerLayer = true;

            // 多人层：其他玩家来自 SelectableEntityTag 注册表（远端实体真实位置），
            // 队友按 TeamClientService 回执的角色 ID 高亮。
            private bool _multiplayerMode;
            private readonly List<Vector3> _remoteWorld = new List<Vector3>();
            private readonly List<Float2> _remoteViews = new List<Float2>();
            private readonly List<string> _remoteNames = new List<string>();
            private readonly List<bool> _remoteIsParty = new List<bool>();
            private readonly HashSet<ulong> _partyIds = new HashSet<ulong>();

            /// <summary>搜索框文本（空 = 不过滤）。匹配 POI 标题与地名，大小写不敏感。</summary>
            private string _searchQuery = string.Empty;

            /// <summary>搜索词变化：与勾选筛选共用同一条重算路径。</summary>
            public void SetSearchQuery(string query)
            {
                string next = (query ?? string.Empty).Trim();
                if (next == _searchQuery) return;

                _searchQuery = next;
                ApplyMarkerFilter();
            }

            private bool MatchesQuery(string text)
            {
                if (string.IsNullOrEmpty(_searchQuery)) return true;
                return !string.IsNullOrEmpty(text)
                    && text.IndexOf(_searchQuery, System.StringComparison.OrdinalIgnoreCase) >= 0;
            }

            /// <summary>导航路径的归一化坐标点列（0~1，空表示当前没有自动移动）</summary>
            private readonly List<Float2> _navPath = new List<Float2>();

            /// <summary>导航路径的世界坐标缓冲</summary>
            private readonly List<Vector3> _navPathWorld = new List<Vector3>();

            /// <summary>标记与路径的刷新节流（秒）。只在地图真正打开时计时。</summary>
            private float _navRefreshTimer;

            private const float NavRefreshInterval = 0.35f;

            private float _pulse;

            // 缩放/平移：设计稿 scale 0.6~2.0、步进 0.08、初始 108%。
            // 视图坐标 = 0.5 + (归一化 - 0.5) * zoom + pan；绘制、命中、反投影共用这一套。
            private const float ZoomMin = 0.6f;
            private const float ZoomMax = 2f;
            private const float ZoomStep = 0.08f;
            private const float DefaultZoom = 1.08f;

            private float _zoom = DefaultZoom;
            private Float2 _pan;
            private bool _isPanning;

            /// <summary>当前是否回退到内置演示标记（导航不可用时为 true）。</summary>
            private bool _usingDemoMarkers;
            private Float2 _panStartMouse;
            private Float2 _panStartValue;

            public MapCanvas()
            {
                AutoFocus = false;
                BuildMarkerData();
            }

            /// <summary>归一化地图坐标 → 视图坐标（含缩放与平移）。</summary>
            private Float2 ToView(Float2 normalized)
            {
                return new Float2(
                    0.5f + (normalized.X - 0.5f) * _zoom + _pan.X,
                    0.5f + (normalized.Y - 0.5f) * _zoom + _pan.Y);
            }

            /// <summary>视图坐标 → 归一化地图坐标（点击反投影）。</summary>
            private Float2 FromView(Float2 view)
            {
                return new Float2(
                    (view.X - 0.5f - _pan.X) / _zoom + 0.5f,
                    (view.Y - 0.5f - _pan.Y) / _zoom + 0.5f);
            }

            /// <summary>视图坐标是否落在地图可视区内（区外的点不画、不可点）。</summary>
            private static bool IsViewVisible(Float2 view)
            {
                return view.X >= 0f && view.X <= 1f && view.Y >= 0f && view.Y <= 1f;
            }

            private static Float2 ViewToPixel(Float2 view, float mx, float my, float mw, float mh)
            {
                return new Float2(mx + view.X * mw, my + view.Y * mh);
            }

            private static Float2 PixelToView(Float2 location, float mx, float my, float mw, float mh)
            {
                return new Float2((location.X - mx) / mw, (location.Y - my) / mh);
            }

            /// <summary>
            /// 设置缩放并把平移夹回合法范围：zoom &gt; 1 时平移不得超过露出的边缘，
            /// zoom &lt;= 1 时内容比视口小，强制居中（否则会偏到一侧露出大片空白）。
            /// </summary>
            private void SetZoom(float zoom)
            {
                _zoom = Mathf.Clamp(zoom, ZoomMin, ZoomMax);

                if (_zoom <= 1f)
                {
                    _pan = Float2.Zero;
                    return;
                }

                float maxPan = (_zoom - 1f) * 0.5f;
                _pan = new Float2(
                    Mathf.Clamp(_pan.X, -maxPan, maxPan),
                    Mathf.Clamp(_pan.Y, -maxPan, maxPan));
            }

            private bool IsInMapRect(Float2 location)
            {
                return location.X >= Pad && location.X <= Width - Pad
                    && location.Y >= Pad && location.Y <= Height - Pad;
            }

            /// <summary>右下缩放的 +/− 命中（上半个是放大、下半个是缩小；中间百分数区也按上下半算）。</summary>
            private bool TryHandleZoomClick(Float2 location)
            {
                const float zw = 40f, zh = 84f;
                float zx = Width - 12f - zw;
                float zy = Height - 12f - zh;

                if (location.X < zx || location.X > zx + zw || location.Y < zy || location.Y > zy + zh)
                    return false;

                if (location.Y <= zy + zh * 0.5f)
                    SetZoom(_zoom + ZoomStep);
                else
                    SetZoom(_zoom - ZoomStep);

                return true;
            }

            public override bool OnMouseDown(Float2 location, MouseButton button)
            {
                // 右键拖动平移（左键留给"点哪儿走哪儿"）
                if (button == MouseButton.Right && IsInMapRect(location))
                {
                    _isPanning = true;
                    _panStartMouse = location;
                    _panStartValue = _pan;
                    StartMouseCapture();
                    return true;
                }

                return base.OnMouseDown(location, button);
            }

            public override void OnMouseMove(Float2 location)
            {
                if (_isPanning && _zoom > 1f)
                {
                    float mw = Width - Pad * 2f;
                    float mh = Height - Pad * 2f;
                    if (mw > 0f && mh > 0f)
                    {
                        var delta = location - _panStartMouse;
                        float maxPan = (_zoom - 1f) * 0.5f;
                        _pan = new Float2(
                            Mathf.Clamp(_panStartValue.X + delta.X / mw, -maxPan, maxPan),
                            Mathf.Clamp(_panStartValue.Y + delta.Y / mh, -maxPan, maxPan));
                    }
                }

                base.OnMouseMove(location);
            }

            /// <summary>
            /// 标记数据源：优先取 NarrativePro 插件导航中的 POI（世界坐标 → 归一化地图坐标），
            /// 不可用时保留内置演示数据。视觉样式与既有设计完全一致，仅数据来源变化。
            /// 无论数据来自哪一路，最后都过一遍筛选面板的勾选状态。
            /// </summary>
            private void BuildMarkerData()
            {
                var views = new List<MapMarkerView>();
                if (!PluginNavigationBridge.TryGetMarkers(views))
                {
                    _rawMarkers = DemoMarkers;
                    _rawPlaces = DemoPlaces;
                    _usingDemoMarkers = true;
                    ApplyMarkerFilter();
                    return;
                }

                _usingDemoMarkers = false;

                var markers = new List<MData>(views.Count + 1);
                var places = new List<(string text, float x, float y, Mk kind)>(views.Count);

                foreach (var v in views)
                {
                    float x = Mathf.Clamp(v.Normalized.X, 0.02f, 0.98f);
                    float y = Mathf.Clamp(v.Normalized.Y, 0.02f, 0.98f);

                    // 移动目的地独立样式（与小地图一致）；其余：支持快速旅行 → 界碑，已发现 → NPC，未发现 → 异闻
                    Mk kind = v.Tag == PluginNavigationBridge.MoveDestinationTag
                        ? Mk.Destination
                        : (v.SupportsFastTravel ? Mk.Teleport : (v.Discovered ? Mk.Npc : Mk.Oddity));
                    markers.Add(new MData
                    {
                        Kind = kind,
                        Tag = v.Tag,
                        Title = v.Title,
                        Subtitle = v.Subtitle,
                        X = x,
                        Y = y,
                        World = v.WorldPosition,
                        CanTravel = v.SupportsFastTravel,
                        Discovered = v.Discovered,
                        // 目的地是运行时状态而非地点，不参与地点卡
                        IsReal = !string.IsNullOrEmpty(v.Tag) && v.Tag != PluginNavigationBridge.MoveDestinationTag,
                    });

                    // 目的地不配地名标签，否则会盖住真实 POI 的名字
                    if (!string.IsNullOrEmpty(v.Title) && kind != Mk.Destination)
                    {
                        places.Add((v.Title, x, y, kind));
                    }
                }

                if (PluginNavigationBridge.TryGetPlayerNormalizedPosition(out var player))
                {
                    markers.Add(new MData
                    {
                        Kind = Mk.Player,
                        X = Mathf.Clamp(player.X, 0.02f, 0.98f),
                        Y = Mathf.Clamp(player.Y, 0.02f, 0.98f),
                    });
                }

                _rawMarkers = markers.ToArray();
                _rawPlaces = places.ToArray();
                ApplyMarkerFilter();
            }

            /// <summary>
            /// 按筛选面板的四个勾选框过滤标记与对应地名。
            /// <para>
            /// 只隐藏"面板里有开关"的四类；妖魔/玩家/目的地/地名始终可见——
            /// 设计稿的筛选面板本来就没有它们的开关，凭空多出开关等于改设计。
            /// </para>
            /// </summary>
            public void SetMarkerFilter(bool teleport, bool oddity, bool chest, bool npc)
            {
                _showTeleport = teleport;
                _showOddity = oddity;
                _showChest = chest;
                _showNpc = npc;
                ApplyMarkerFilter();
            }

            /// <summary>图层开关：地名 / 导航路径 / 玩家位置。</summary>
            public void SetLayerVisibility(bool places, bool navPath, bool player)
            {
                _showPlacesLayer = places;
                _showNavPathLayer = navPath;
                _showPlayerLayer = player;
            }

            /// <summary>切换多人层，返回当前可见的其他玩家数量（供页面提示）。</summary>
            public int SetMultiplayerMode(bool on)
            {
                _multiplayerMode = on;
                if (!on)
                {
                    ClearRemotePlayers();
                    return 0;
                }

                RefreshRemotePlayers();
                return _remoteViews.Count;
            }

            private void ClearRemotePlayers()
            {
                _remoteWorld.Clear();
                _remoteViews.Clear();
                _remoteNames.Clear();
                _remoteIsParty.Clear();
            }

            /// <summary>
            /// 采集其他玩家：实体注册表只收 <see cref="SelectableKind.Player"/> 且排除本地玩家；
            /// 队友按队伍回执里的角色 ID 打标。没有地图投影时清空，不猜位置。
            /// </summary>
            private void RefreshRemotePlayers()
            {
                ClearRemotePlayers();

                var game = HundunWorldGame.Instance;
                ulong localId = game?.PlayerId ?? 0UL;
                var localActor = game?.LocalPlayerActor;
                var worldScene = HundunWorldGame.FindGameWorldScene();

                _partyIds.Clear();
                var team = TeamClientService.Instance.CurrentTeam;
                if (team?.Members != null)
                {
                    for (int i = 0; i < team.Members.Count; i++)
                    {
                        var member = team.Members[i];
                        if (member != null && member.CharacterId != 0UL)
                            _partyIds.Add(member.CharacterId);
                    }
                }

                var tags = SelectableEntityTag.Active;
                for (int i = 0; i < tags.Count; i++)
                {
                    var tag = tags[i];
                    if (tag == null || tag.Kind != SelectableKind.Player) continue;

                    var actor = tag.Actor;
                    if (actor == null || !actor.IsActive) continue;
                    if (worldScene != null && actor.Scene != worldScene) continue;
                    if (localActor != null && actor == localActor) continue;
                    if (localId != 0UL && tag.EntityId == localId) continue;

                    _remoteWorld.Add(actor.Position);
                    _remoteNames.Add(!string.IsNullOrEmpty(tag.DisplayName) ? tag.DisplayName : actor.Name ?? string.Empty);
                    _remoteIsParty.Add(tag.EntityId != 0UL && _partyIds.Contains(tag.EntityId));
                }

                if (!PluginNavigationBridge.TryProjectToWorldMapNormalized(_remoteWorld, _remoteViews))
                    ClearRemotePlayers();
            }

            /// <summary>多人层绘制：队友金环、其他玩家青点；名字最多 8 个避免堆叠。</summary>
            private void DrawRemotePlayers(float mx, float my, float mw, float mh)
            {
                var font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Body, 10f).GetFont();
                int labeled = 0;

                for (int i = 0; i < _remoteViews.Count; i++)
                {
                    var view = ToView(_remoteViews[i]);
                    if (!IsViewVisible(view)) continue;

                    var pos = ViewToPixel(view, mx, my, mw, mh);
                    bool party = _remoteIsParty[i];

                    InkRenderHelper.FillCircle(pos, party ? 5f : 4f,
                        party ? InkWashTheme.GoldBright : InkWashTheme.JadeBright);
                    InkRenderHelper.DrawCircle(pos, party ? 7.5f : 6f,
                        party ? InkWashTheme.GoldPrimary : InkWashTheme.JadeDeep, 1f);

                    if (font != null && labeled < 8 && !string.IsNullOrEmpty(_remoteNames[i]))
                    {
                        Render2D.DrawText(font, _remoteNames[i],
                            new Rectangle(pos.X - 50f, pos.Y - 24f, 100f, 14f),
                            party ? InkWashTheme.GoldBright : InkWashTheme.TextSecondary,
                            TextAlignment.Center, TextAlignment.Near, TextWrapping.NoWrap);
                        labeled++;
                    }
                }
            }

            private bool IsKindVisible(Mk kind)
            {
                switch (kind)
                {
                    case Mk.Teleport: return _showTeleport;
                    case Mk.Oddity: return _showOddity;
                    case Mk.Chest: return _showChest;
                    case Mk.Npc: return _showNpc;
                    default: return true;
                }
            }

            private void ApplyMarkerFilter()
            {
                var visibleMarkers = new List<MData>(_rawMarkers.Length);
                for (int i = 0; i < _rawMarkers.Length; i++)
                {
                    var m = _rawMarkers[i];
                    if (!IsKindVisible(m.Kind)) continue;

                    // 搜索时玩家与目的地始终保留（否则"我在哪/导航终点"会凭空消失）；
                    // 其余标记按标题匹配，设计稿示例标记没有标题，搜索时自然被过滤掉。
                    if (!string.IsNullOrEmpty(_searchQuery)
                        && m.Kind != Mk.Player && m.Kind != Mk.Destination
                        && !MatchesQuery(m.Title))
                        continue;

                    visibleMarkers.Add(m);
                }
                _markers = visibleMarkers.ToArray();

                var visiblePlaces = new List<(string text, float x, float y)>(_rawPlaces.Length);
                for (int i = 0; i < _rawPlaces.Length; i++)
                {
                    if (visiblePlaces.Count >= MaxPlaceLabels) break;

                    var p = _rawPlaces[i];
                    if (IsKindVisible(p.kind) && MatchesQuery(p.text))
                        visiblePlaces.Add((p.text, p.x, p.y));
                }
                _places = visiblePlaces.ToArray();
            }

            public override void Update(float dt)
            {
                base.Update(dt);
                _pulse += dt;

                // 关闭状态一律不刷新：标记与路径都来自运行时导航数据，
                // 但只有打开地图时才需要（也才看得见），避免白烧 CPU。
                if (!VisibleInHierarchy) return;

                _navRefreshTimer += dt;
                if (_navRefreshTimer < NavRefreshInterval) return;

                _navRefreshTimer = 0f;
                BuildMarkerData();
                if (_multiplayerMode) RefreshRemotePlayers();
                RefreshNavigationPath();
            }

            /// <summary>
            /// 刷新自动移动路径（世界点列 → 归一化地图坐标）。目的地清空后路径随之消失。
            /// </summary>
            private void RefreshNavigationPath()
            {
                if (!PluginNavigationBridge.TryGetMoveDestinationPath(_navPathWorld)
                    || !PluginNavigationBridge.TryProjectToWorldMapNormalized(_navPathWorld, _navPath))
                {
                    _navPath.Clear();
                }
            }

            /// <summary>
            /// 命中地图上的覆盖控件区（右下缩放、左下图例、右上罗盘）。
            /// 这些区域是画布内自绘的 UI，不是可走的地面。
            /// </summary>
            private bool IsOverlayHit(Float2 location)
            {
                // 右下缩放：宽 40、高 84，距边 12
                if (location.X >= Width - 52f && location.Y >= Height - 96f) return true;
                // 左下图例：宽 200、高 28，距边 12
                if (location.X <= 212f && location.Y >= Height - 40f) return true;
                // 右上罗盘：46×46，距边 12
                if (location.X >= Width - 58f && location.Y <= 58f) return true;
                return false;
            }

            /// <summary>
            /// 命中真实 POI 标记（演示标记不可选中：它们没有 Tag，也没有可展示的真实数据）。
            /// 命中测试复用 Draw 中的同一套归一化→像素映射。
            /// </summary>
            private bool TryHitMarker(Float2 location, out MData hit)
            {
                hit = default;
                if (_markers == null) return false;

                float mx = Pad, my = Pad;
                float mw = Width - Pad * 2f, mh = Height - Pad * 2f;
                if (mw <= 0f || mh <= 0f) return false;

                for (int i = _markers.Length - 1; i >= 0; i--)
                {
                    var m = _markers[i];
                    if (!m.IsReal) continue;

                    var view = ToView(new Float2(m.X, m.Y));
                    if (!IsViewVisible(view)) continue;

                    if (Float2.Distance(ViewToPixel(view, mx, my, mw, mh), location) <= TeleportHitRadius)
                    {
                        hit = m;
                        return true;
                    }
                }
                return false;
            }

            /// <summary>
            /// 自测入口：取第一个可见真实标记，用它的像素坐标调用 <see cref="OnMouseUp"/>，
            /// 走完整的命中 → 选中 → 地点卡链路（不绕过 TryHitMarker）。
            /// </summary>
            public bool RunCardSelftest(out string tag)
            {
                tag = null;
                if (_markers == null) return false;

                float mx = Pad, my = Pad;
                float mw = Width - Pad * 2f, mh = Height - Pad * 2f;
                if (mw <= 0f || mh <= 0f) return false;

                for (int i = 0; i < _markers.Length; i++)
                {
                    var m = _markers[i];
                    if (!m.IsReal) continue;

                    var view = ToView(new Float2(m.X, m.Y));
                    if (!IsViewVisible(view)) continue;

                    var pos = ViewToPixel(view, mx, my, mw, mh);
                    if (!OnMouseUp(pos, MouseButton.Left)) return false;

                    tag = m.Tag;
                    return true;
                }
                return false;
            }

            private void SelectMarker(MData m)
            {
                _selectedTag = m.Tag ?? string.Empty;
                LocationSelected?.Invoke(new LocationSelection
                {
                    Tag = m.Tag,
                    Title = m.Title,
                    Subtitle = m.Subtitle,
                    World = m.World,
                    SupportsFastTravel = m.CanTravel,
                    Discovered = m.Discovered,
                });
            }

            /// <summary>
            /// 双击界碑 = 快速旅行；单击只出地点卡。
            /// 这样"想看清这个点是什么"与"想立刻过去"是两件不同的事，不会因为凑近看细节被直接传走。
            /// </summary>
            public override bool OnMouseDoubleClick(Float2 location, MouseButton button)
            {
                if (button == MouseButton.Left && TryHitMarker(location, out var hit))
                {
                    if (hit.CanTravel && !string.IsNullOrEmpty(hit.Tag))
                    {
                        if (PluginNavigationBridge.TryRequestFastTravel(hit.Tag))
                            Debug.Log($"[WorldMapPage] 双击快速旅行至 {hit.Tag}");
                        else
                            Debug.LogWarning($"[WorldMapPage] 无法传送到 {hit.Tag}（未发现/不支持快速旅行/无安全落点）");
                    }
                    return true;
                }

                return base.OnMouseDoubleClick(location, button);
            }

            /// <summary>
            /// 左键：先命中真实标记 → 出地点卡（传送/前往由卡片按钮负责）；
            /// 未命中任何标记时，地图范围内的点击作为移动目的地，与 Alt+左键走同一条寻路/跟随管线。
            /// </summary>
            public override bool OnMouseUp(Float2 location, MouseButton button)
            {
                if (button == MouseButton.Right)
                {
                    if (_isPanning)
                    {
                        _isPanning = false;
                        EndMouseCapture();
                        return true;
                    }
                    return base.OnMouseUp(location, button);
                }

                if (button == MouseButton.Left && _markers != null)
                {
                    float mx = Pad, my = Pad;
                    float mw = Width - Pad * 2f, mh = Height - Pad * 2f;

                    if (mw > 0f && mh > 0f)
                    {
                        // 右下 +/− 先吃掉点击，不能落到"点哪儿走哪儿"
                        if (TryHandleZoomClick(location))
                            return true;

                        // 标记命中：弹地点卡，本次点击不再触发走路
                        if (TryHitMarker(location, out var hit))
                        {
                            SelectMarker(hit);
                            return true;
                        }

                        // 覆盖在地图上的控件区（图例/罗盘）不是地面
                        if (IsOverlayHit(location))
                            return base.OnMouseUp(location, button);

                        // 地图范围内：点哪儿走哪儿（先反缩放/反平移回归一化坐标）
                        if (location.X >= mx && location.X <= mx + mw
                            && location.Y >= my && location.Y <= my + mh)
                        {
                            var view = PixelToView(location, mx, my, mw, mh);
                            // 缩小到 1.0 以下时四周是空白边，不属于地图
                            if (!IsViewVisible(view))
                                return base.OnMouseUp(location, button);

                            var normalized = FromView(view);

                            float worldY = 0f;
                            if (PluginNavigationBridge.TryGetPlayerPose(out var pose, out _))
                                worldY = pose.Y;

                            if (PluginNavigationBridge.TryUnprojectWorldMapNormalized(normalized, worldY, out var world)
                                && PluginNavigationBridge.TryGetLocalPlayerController(out var controller))
                            {
                                return controller.TryStartClickToMove(world);
                            }

                            Debug.LogWarning("[WorldMapPage] 地图点击未能发起移动（地图未配置或本地玩家无 PlayerController）");
                        }
                    }
                }

                return base.OnMouseUp(location, button);
            }

            public override void Draw()
            {
                base.Draw();
                if (!Visible || Width <= 0f || Height <= 0f) return;

                DrawBase();

                float mx = Pad, my = Pad;
                float mw = Width - Pad * 2f, mh = Height - Pad * 2f;
                if (mw <= 0f || mh <= 0f) return;

                if (_showPlacesLayer) DrawPlaces(mx, my, mw, mh);
                if (_showNavPathLayer) DrawNavPath(mx, my, mw, mh);
                foreach (var m in _markers)
                {
                    if (m.Kind == Mk.Player && !_showPlayerLayer) continue;
                    var view = ToView(new Float2(m.X, m.Y));
                    if (!IsViewVisible(view)) continue;
                    var pos = ViewToPixel(view, mx, my, mw, mh);
                    DrawMarker(pos, m.Kind);
                    if (m.IsReal && m.Tag == _selectedTag)
                        DrawSelectionRing(pos);
                }

                if (_multiplayerMode) DrawRemotePlayers(mx, my, mw, mh);

                DrawSearchEmptyHint(mx, my, mw, mh);

                // 导航不可用时地图会回退到内置演示标记：必须让玩家知道这些点位是示意的
                if (_usingDemoMarkers)
                {
                    var font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Body, 11f).GetFont();
                    if (font != null)
                    {
                        Render2D.DrawText(font, "导航数据不可用 · 当前为示意图标",
                            new Rectangle(mx + 8f, my + mh - 22f, 260f, 18f),
                            InkWashTheme.TextTertiary, TextAlignment.Near, TextAlignment.Center,
                            TextWrapping.NoWrap);
                    }
                }

                DrawCompass();
                DrawZoom();
                DrawLegend();
                DrawMoveHint();

                // 金边框（radius lg）
                InkRenderHelper.DrawRoundedRectangle(new Rectangle(Float2.Zero, Size),
                    InkWashTheme.RadiusLg, InkWashTheme.BorderGold, 1f);
            }

            /// <summary>水墨底：深渊底 + 柔光 + 晕影。</summary>
            private void DrawBase()
            {
                Render2D.FillRectangle(new Rectangle(Float2.Zero, Size), InkWashTheme.Abyss);

                // 上方金柔光
                var gold = InkWashTheme.GoldPrimary;
                InkRenderHelper.FillRadialGradient(
                    new Float2(Width * 0.5f, Height * -0.10f), Height * 0.7f,
                    new Color(gold.R, gold.G, gold.B, 0.05f), new Color(gold.R, gold.G, gold.B, 0f), 20);
                // 左下青柔光
                var jade = InkWashTheme.JadeDeep;
                InkRenderHelper.FillRadialGradient(
                    new Float2(Width * 0.0f, Height * 1.0f), Height * 0.6f,
                    new Color(jade.R, jade.G, jade.B, 0.04f), new Color(jade.R, jade.G, jade.B, 0f), 20);
                // 晕影（中心透明 → 边缘深）
                var ab = InkWashTheme.Abyss;
                InkRenderHelper.FillRadialGradient(
                    new Float2(Width * 0.5f, Height * 0.4f),
                    Mathf.Max(Width, Height) * 0.75f,
                    new Color(ab.R, ab.G, ab.B, 0f), new Color(ab.R, ab.G, ab.B, 0.55f), 24);
            }

            private void DrawPlaces(float mx, float my, float mw, float mh)
            {
                var font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Display, 20f).GetFont();
                if (font == null) return;
                var c = new Color(InkWashTheme.TextDefault.R, InkWashTheme.TextDefault.G, InkWashTheme.TextDefault.B, 0.55f);
                foreach (var p in _places)
                {
                    var view = ToView(new Float2(p.x, p.y));
                    if (!IsViewVisible(view)) continue;

                    var pos = ViewToPixel(view, mx, my, mw, mh);
                    // 地名画在标记图标下方：图标占半径 ~7px，居中的旧写法会让图标压住名字中间的字。
                    Render2D.DrawText(font, p.text, new Rectangle(pos.X - 60f, pos.Y + 9f, 120f, 26f), c,
                        TextAlignment.Center, TextAlignment.Near, TextWrapping.NoWrap);
                }
            }

            /// <summary>导航路径的画布像素坐标缓冲（每帧复用）</summary>
            private readonly List<Float2> _navPathScreen = new List<Float2>();

            /// <summary>
            /// 绘制自动移动路径：纸白虚线 + 半透明垫底，按地图绘图区矩形裁剪。
            /// 归一化→像素与标记命中测试用的是同一套映射，所以线端与目的地圆点必然重合。
            /// </summary>
            private void DrawNavPath(float mx, float my, float mw, float mh)
            {
                if (_navPath.Count < 2) return;

                _navPathScreen.Clear();
                for (int i = 0; i < _navPath.Count; i++)
                {
                    var n = _navPath[i];
                    _navPathScreen.Add(ViewToPixel(ToView(n), mx, my, mw, mh));
                }

                var clip = new Rectangle(mx, my, mw, mh);
                var baseColor = InkWashTheme.PaperBright;

                InkNavPathPainter.DrawInRect(_navPathScreen, clip,
                    new Color(baseColor.R, baseColor.G, baseColor.B, 0.20f),
                    InkNavPathPainter.DefaultThickness + 3f,
                    InkNavPathPainter.DefaultDashLength, InkNavPathPainter.DefaultGapLength);

                InkNavPathPainter.DrawInRect(_navPathScreen, clip,
                    new Color(baseColor.R, baseColor.G, baseColor.B, 0.85f),
                    InkNavPathPainter.DefaultThickness,
                    InkNavPathPainter.DefaultDashLength, InkNavPathPainter.DefaultGapLength);
            }

            /// <summary>选中标记的高亮环（金亮内环 + 半透明外环）。</summary>
            private void DrawSelectionRing(Float2 pos)
            {
                var g = InkWashTheme.GoldBright;
                InkRenderHelper.DrawCircle(pos, 14f, g, 1.5f);
                InkRenderHelper.DrawCircle(pos, 17f, new Color(g.R, g.G, g.B, 0.35f), 1f);
            }

            private void DrawMarker(Float2 pos, Mk kind)
            {
                switch (kind)
                {
                    case Mk.Teleport:
                        DrawTeleport(pos);
                        break;
                    case Mk.Oddity:
                        InkRenderHelper.FillCircle(pos, 5f, InkWashTheme.JadeBright);
                        InkRenderHelper.DrawCircle(pos, 5f, InkWashTheme.JadeGlow, 1f);
                        break;
                    case Mk.Chest:
                    {
                        var g = InkWashTheme.GoldPrimary;
                        InkRenderHelper.FillCircle(pos, 6f, new Color(g.R, g.G, g.B, 0.40f));
                        var lf = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Body, 9f).GetFont();
                        if (lf != null)
                            Render2D.DrawText(lf, "锁", new Rectangle(pos.X - 10f, pos.Y - 20f, 20f, 12f),
                                InkWashTheme.BloodBright, TextAlignment.Center, TextAlignment.Center, TextWrapping.NoWrap);
                        break;
                    }
                    case Mk.Npc:
                        InkRenderHelper.FillCircle(pos, 4.5f, InkWashTheme.GoldBright);
                        InkRenderHelper.DrawCircle(pos, 4.5f, InkWashTheme.GoldDeep, 1f);
                        break;
                    case Mk.Monster:
                        InkRenderHelper.FillCircle(pos, 4.5f, InkWashTheme.BloodBright);
                        InkRenderHelper.DrawCircle(pos, 4.5f, InkWashTheme.BloodDeep, 1f);
                        break;
                    case Mk.Player:
                        DrawPlayer(pos);
                        break;
                    case Mk.Destination:
                        // 与小地图同一语义：纸白实心点 + 一圈描边，压住虚线路径的末端
                        InkRenderHelper.FillCircle(pos, 4.5f, InkWashTheme.PaperBright);
                        InkRenderHelper.DrawCircle(pos, 8f, InkWashTheme.PaperBright, 1.5f);
                        break;
                }
            }

            /// <summary>界碑：金色菱形（空心） + 脉冲环。</summary>
            private void DrawTeleport(Float2 pos)
            {
                // 脉冲环（2.4s 扩散淡出）
                float t = (_pulse % 2.4f) / 2.4f;
                float pr = Mathf.Lerp(5.4f, 16.2f, t);
                float pa = 0.9f * (1f - t);
                var g = InkWashTheme.GoldPrimary;
                InkRenderHelper.DrawCircle(pos, pr, new Color(g.R, g.G, g.B, pa), 1f);

                // 菱形（外金内空）
                DrawDiamond(pos, 9f, InkWashTheme.GoldPrimary);
                DrawDiamond(pos, 4.5f, InkWashTheme.Void);
            }

            private static void DrawDiamond(Float2 c, float hs, Color color)
            {
                var v1 = c + new Float2(0f, -hs);
                var v2 = c + new Float2(hs, 0f);
                var v3 = c + new Float2(0f, hs);
                var v4 = c + new Float2(-hs, 0f);
                Render2D.FillTriangle(v1, v2, c, color);
                Render2D.FillTriangle(v2, v3, c, color);
                Render2D.FillTriangle(v3, v4, c, color);
                Render2D.FillTriangle(v4, v1, c, color);
            }

            /// <summary>玩家：青玉五角星 + 朝上箭头 + 辉光脉冲。</summary>
            private void DrawPlayer(Float2 pos)
            {
                float glowR = 14f + 3f * Mathf.Sin(_pulse * 3.5f);
                var jade = InkWashTheme.JadeBright;
                InkRenderHelper.FillCircle(pos, glowR, new Color(jade.R, jade.G, jade.B, 0.20f));

                // 五角星（外半径 11 / 内半径 5）
                float outer = 11f, inner = 5f;
                var pts = new Float2[10];
                for (int i = 0; i < 10; i++)
                {
                    float angle = (i * Mathf.TwoPi / 10f) - Mathf.PiOverTwo;
                    float r = (i % 2 == 0) ? outer : inner;
                    pts[i] = pos + new Float2(Mathf.Cos(angle) * r, Mathf.Sin(angle) * r);
                }
                for (int i = 0; i < 10; i++)
                {
                    int a = i, b = (i + 1) % 10;
                    Render2D.FillTriangle(pts[a], pts[b], pos, InkWashTheme.JadeBright);
                }

                // 朝上箭头
                var a1 = pos + new Float2(-5f, -16f);
                var a2 = pos + new Float2(5f, -16f);
                var a3 = pos + new Float2(0f, -24f);
                Render2D.FillTriangle(a1, a2, a3, InkWashTheme.JadeBright);
            }

            /// <summary>罗盘（右上 46x46 圆，面板底 + 金边 + 北字 + 指针）。</summary>
            private void DrawCompass()
            {
                float cx = Width - 12f - 46f;
                float cy = 12f;
                var center = new Float2(cx + 23f, cy + 23f);
                InkRenderHelper.FillCircle(center, 23f, InkWashTheme.Panel);
                InkRenderHelper.DrawCircle(center, 23f, InkWashTheme.BorderGold, 1f);

                var nf = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Display, 11f).GetFont();
                if (nf != null)
                    Render2D.DrawText(nf, "北", new Rectangle(cx, cy + 4f, 46f, 16f), InkWashTheme.GoldBright,
                        TextAlignment.Center, TextAlignment.Center, TextWrapping.NoWrap);

                // 指针（东北向三角）
                var p1 = center + new Float2(6f, 2f);
                var p2 = center + new Float2(-4f, 10f);
                var p3 = center + new Float2(2f, -2f);
                Render2D.FillTriangle(p1, p2, p3, InkWashTheme.GoldPrimary);
            }

            /// <summary>缩放控件（右下竖列：+ / 108% / -）。</summary>
            private void DrawZoom()
            {
                float zw = 40f, zh = 84f;
                float zx = Width - 12f - zw;
                float zy = Height - 12f - zh;
                var rect = new Rectangle(zx, zy, zw, zh);
                InkRenderHelper.FillRoundedRectangle(rect, 4f, InkWashTheme.Panel);
                InkRenderHelper.DrawRoundedRectangle(rect, 4f, InkWashTheme.BorderGold, 1f);

                // + 按钮
                var plusRect = new Rectangle(zx + 6f, zy + 6f, 28f, 22f);
                InkRenderHelper.DrawRoundedRectangle(plusRect, 2f, InkWashTheme.GoldTrace, 1f);
                var bf = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Body, 14f).GetFont();
                if (bf != null)
                {
                    // 到达上下限时把对应按钮压暗，避免"点了没反应"的错觉
                    var plusColor = _zoom >= ZoomMax - 0.001f ? InkWashTheme.TextDisabled : InkWashTheme.TextSecondary;
                    var minusColor = _zoom <= ZoomMin + 0.001f ? InkWashTheme.TextDisabled : InkWashTheme.TextSecondary;

                    Render2D.DrawText(bf, "+", plusRect, plusColor,
                        TextAlignment.Center, TextAlignment.Center, TextWrapping.NoWrap);
                    Render2D.DrawText(bf, "−", new Rectangle(zx + 6f, zy + zh - 28f, 28f, 22f), minusColor,
                        TextAlignment.Center, TextAlignment.Center, TextWrapping.NoWrap);
                }
                var lf = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Number, 10f).GetFont();
                if (lf != null)
                    Render2D.DrawText(lf, Mathf.RoundToInt(_zoom * 100f) + "%",
                        new Rectangle(zx, zy + 34f, zw, 16f), InkWashTheme.TextTertiary,
                        TextAlignment.Center, TextAlignment.Center, TextWrapping.NoWrap);
            }

            /// <summary>搜索无结果时在画布中央给一句提示（玩家/目的地不算搜索结果）。</summary>
            private void DrawSearchEmptyHint(float mx, float my, float mw, float mh)
            {
                if (string.IsNullOrEmpty(_searchQuery)) return;

                for (int i = 0; i < _markers.Length; i++)
                {
                    var kind = _markers[i].Kind;
                    if (kind != Mk.Player && kind != Mk.Destination)
                        return;
                }
                if (_places.Length > 0) return;

                var font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Body, 13f).GetFont();
                if (font == null) return;

                Render2D.DrawText(font, $"未找到匹配「{_searchQuery}」的标记",
                    new Rectangle(mx, my + mh * 0.5f - 12f, mw, 24f),
                    InkWashTheme.TextTertiary,
                    TextAlignment.Center, TextAlignment.Center, TextWrapping.NoWrap);
            }

            /// <summary>底部操作提示（居中，避开左下图例与右下缩放控件）。</summary>
            private void DrawMoveHint()
            {
                var font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Body, 11f).GetFont();
                if (font == null) return;

                Render2D.DrawText(font, "左键选中/寻路 · 双击界碑传送 · 右键拖动平移 · 右下 +/− 缩放",
                    new Rectangle(Width * 0.5f - 250f, Height - 12f - 20f, 500f, 20f),
                    InkWashTheme.TextTertiary,
                    TextAlignment.Center, TextAlignment.Center, TextWrapping.NoWrap);
            }

            /// <summary>图例（左下横排：界碑/故人/妖魔）。</summary>
            private void DrawLegend()
            {
                float lw = _multiplayerMode ? 250f : 200f, lh = 28f;
                float lx = 12f, ly = Height - 12f - lh;
                var rect = new Rectangle(lx, ly, lw, lh);
                InkRenderHelper.FillRoundedRectangle(rect, 4f, InkWashTheme.Panel);
                InkRenderHelper.DrawRoundedRectangle(rect, 4f, InkWashTheme.BorderGold, 1f);

                var font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Body, 11f).GetFont();
                float cy = ly + lh * 0.5f;

                // 界碑（菱形）
                DrawDiamond(new Float2(lx + 16f, cy), 5f, InkWashTheme.GoldPrimary);
                if (font != null)
                    Render2D.DrawText(font, "界碑", new Rectangle(lx + 26f, ly, 40f, lh), InkWashTheme.TextSecondary,
                        TextAlignment.Near, TextAlignment.Center, TextWrapping.NoWrap);

                // 故人（金点）
                InkRenderHelper.FillCircle(new Float2(lx + 82f, cy), 4f, InkWashTheme.GoldBright);
                if (font != null)
                    Render2D.DrawText(font, "故人", new Rectangle(lx + 92f, ly, 40f, lh), InkWashTheme.TextSecondary,
                        TextAlignment.Near, TextAlignment.Center, TextWrapping.NoWrap);

                // 妖魔（血点）
                InkRenderHelper.FillCircle(new Float2(lx + 148f, cy), 4f, InkWashTheme.BloodBright);
                if (font != null)
                    Render2D.DrawText(font, "妖魔", new Rectangle(lx + 158f, ly, 40f, lh), InkWashTheme.TextSecondary,
                        TextAlignment.Near, TextAlignment.Center, TextWrapping.NoWrap);

                // 同游（青点金环，仅多人层）
                if (_multiplayerMode)
                {
                    InkRenderHelper.FillCircle(new Float2(lx + 214f, cy), 4f, InkWashTheme.JadeBright);
                    InkRenderHelper.DrawCircle(new Float2(lx + 214f, cy), 6f, InkWashTheme.GoldPrimary, 1f);
                    if (font != null)
                        Render2D.DrawText(font, "同游", new Rectangle(lx + 224f, ly, 40f, lh), InkWashTheme.TextSecondary,
                            TextAlignment.Near, TextAlignment.Center, TextWrapping.NoWrap);
                }
            }
        }
    }
}
