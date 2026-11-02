using System;
using FlaxEngine;
using FlaxEngine.GUI;
using HundunWorld.Game.Navigation;
using HundunWorld.Game.UI.Ink;
using HundunWorld.Game.UI.StyleSystem;

namespace HundunWorld.Game.UI.Ink.Components
{
    /// <summary>
    /// 小地图实体类型枚举。
    /// 用于 <see cref="InkMinimap.AddEntity"/> 标记实体阵营，
    /// 不同阵营绘制不同颜色的点位。
    /// </summary>
    public enum InkMinimapEntityType
    {
        /// <summary>玩家本体 — 鎏金主色</summary>
        Player,

        /// <summary>友方 — 翡翠亮色</summary>
        Friendly,

        /// <summary>敌方 — 朱红亮色</summary>
        Enemy,

        /// <summary>NPC — 鎏金亮色</summary>
        NPC,

        /// <summary>点击移动的目的地 — 纸白实心点 + 环描边，与阵营/交互点区分</summary>
        Destination
    }

    /// <summary>
    /// 圆形小地图外框样式。
    /// </summary>
    public enum InkMinimapFrameStyle
    {
        /// <summary>普通圆形（默认）：圆内透明，仅一圈细圆环轮廓 + 方位字，场景可从圆内透出。</summary>
        Plain,

        /// <summary>雷达：在圆内叠加同心圆网格、十字线与四方位点（旧样式）。</summary>
        Radar,

        /// <summary>贴图：用 <see cref="InkMinimap.FrameTexture"/> 作外框贴图，取代矢量细圆环。</summary>
        Texture
    }

    /// <summary>
    /// 圆形小地图。
    /// 默认 <see cref="InkMinimapFrameStyle.Plain"/>：圆内透明、只保留一圈细圆环，
    /// 实体点位与地形快照直接浮于场景之上；另有雷达网格样式与贴图外框（预留）。
    /// 实体坐标 relativeX/relativeZ 范围为 -1~1，线性映射到小地图半径内。
    /// </summary>
    public class InkMinimap : ContainerControl
    {
        /// <summary>圆形边框的分段数（越大越圆滑）</summary>
        private const int CircleSegments = 64;

        /// <summary>细圆环厚度（像素）</summary>
        private const float RingThickness = 1f;

        /// <summary>外框样式。默认普通圆形。</summary>
        public InkMinimapFrameStyle FrameStyle { get; set; } = InkMinimapFrameStyle.Plain;

        /// <summary>
        /// 外框贴图（预留接口）。<see cref="FrameStyle"/> 为 <see cref="InkMinimapFrameStyle.Texture"/> 时生效：
        /// 按控件矩形（再外扩 <see cref="FrameTextureInflate"/> 像素）拉伸绘制，取代矢量细圆环；
        /// 未赋值时自动回退为细圆环。贴图可用 <c>Content.LoadAsync&lt;Texture&gt;(路径)</c> 载入。
        /// </summary>
        public Texture FrameTexture { get; set; }

        /// <summary>外框贴图相对控件矩形的外扩像素（贴图自带外扩装饰时使用）。</summary>
        public float FrameTextureInflate { get; set; }

        /// <summary>实体点位距边框的内边距，避免点位贴边</summary>
        private const float EntityPadding = 8f;

        /// <summary>常规实体点半径（像素）</summary>
        private const float EntityDotRadius = 4f;

        /// <summary>玩家实体点半径（像素），略大以突出主体</summary>
        private const float PlayerDotRadius = 5f;

        /// <summary>地形兴趣点半径（像素）</summary>
        private const float LandmarkRadius = 2.5f;

        /// <summary>实体列表（类型 + 归一化坐标 x/z，范围 -1~1）</summary>
        private System.Collections.Generic.List<(InkMinimapEntityType type, float x, float z)> _entities
            = new System.Collections.Generic.List<(InkMinimapEntityType, float, float)>();

        /// <summary>地形快照/兴趣点列表（相对坐标 + 半径 + 颜色）</summary>
        private System.Collections.Generic.List<(float x, float z, float radius, Color color)> _landmarks
            = new System.Collections.Generic.List<(float, float, float, Color)>();

        /// <summary>当前玩家朝向角度（度），0=正北，顺时针增加</summary>
        public float PlayerYaw { get; set; } = 0f;

        /// <summary>导航路径的世界点列（空表示当前没有自动移动）</summary>
        private readonly System.Collections.Generic.List<Vector3> _navPathWorld
            = new System.Collections.Generic.List<Vector3>();

        /// <summary>
        /// 构造函数：透明背景、不裁剪子控件。
        /// 默认尺寸 160x160，可由外部修改 <see cref="Control.Size"/>。
        /// </summary>
        public InkMinimap()
        {
            BackgroundColor = Color.Transparent;
            ClipChildren = false;
            Size = new Float2(160f, 160f);
            AutoFocus = false;
        }

        /// <summary>
        /// 添加一个实体点位。
        /// 坐标会被钳制到 -1~1 范围，绘制时线性映射到小地图半径内。
        /// </summary>
        /// <param name="type">实体类型（决定点位颜色）</param>
        /// <param name="relativeX">相对 X 坐标，-1=最左，1=最右</param>
        /// <param name="relativeZ">相对 Z 坐标，-1=最上，1=最下（映射到屏幕 Y 轴）</param>
        public void AddEntity(InkMinimapEntityType type, float relativeX, float relativeZ)
        {
            float x = Mathf.Clamp(relativeX, -1f, 1f);
            float z = Mathf.Clamp(relativeZ, -1f, 1f);
            _entities.Add((type, x, z));
        }

        /// <summary>
        /// 清除所有已添加的实体点位。
        /// </summary>
        public void ClearEntities()
        {
            _entities.Clear();
        }

        /// <summary>小地图显示半径（世界单位）：以玩家为中心，超出该半径的标记不显示。</summary>
        public float NavigationRange { get; set; } = 30000f;

        /// <summary>小地图可视范围的最近/最远边界（世界单位）。</summary>
        public const float MinNavigationRange = 200f;
        public const float MaxNavigationRange = 8000f;

        /// <summary>
        /// 按倍率缩放小地图可视范围，并立即重投影点位。
        /// <paramref name="factor"/> &lt; 1 = 放大（看得更近），&gt; 1 = 缩小（看得更远）。
        /// </summary>
        public void ZoomBy(float factor)
        {
            if (factor <= 0f) return;

            float next = Mathf.Clamp(NavigationRange * factor, MinNavigationRange, MaxNavigationRange);
            if (Mathf.Abs(next - NavigationRange) < 0.01f) return;

            NavigationRange = next;

            // 不等 0.5s 的刷新节流：按钮按下去就该立刻看到远近变化
            RefreshFromNavigation();
        }

        /// <summary>从 NarrativePro 导航刷新点位的时间间隔（秒）。</summary>
        public float NavigationRefreshInterval { get; set; } = 0.5f;

        private float _navRefreshTimer;

        /// <summary>投影后的路径像素坐标（每帧复用，避免逐帧分配）</summary>
        private readonly System.Collections.Generic.List<Float2> _navPathScreen
            = new System.Collections.Generic.List<Float2>();

        private Vector3 _navPathOrigin = Vector3.Zero;
        private bool _hasNavPathOrigin;

        public override void Update(float dt)
        {
            base.Update(dt);

            _navRefreshTimer += dt;
            if (_navRefreshTimer < NavigationRefreshInterval) return;

            _navRefreshTimer = 0f;
            RefreshFromNavigation();
            RefreshNavigationPath();
        }

        /// <summary>
        /// 刷新自动移动的面包屑路径。与点位刷新同频：路径本身由插件按 4Hz 重算，
        /// 这里再高频也没有新数据。目的地清除后路径随之清空。
        /// </summary>
        private void RefreshNavigationPath()
        {
            if (!PluginNavigationBridge.TryGetMoveDestinationPath(_navPathWorld))
            {
                _hasNavPathOrigin = false;
                return;
            }

            // 世界点列必须锚在与点位同一个玩家坐标上，否则路径与目的地圆点会互相错位
            if (!PluginNavigationBridge.TryGetPlayerPose(out var playerPos, out _))
            {
                _navPathWorld.Clear();
                _hasNavPathOrigin = false;
                return;
            }

            _navPathOrigin = playerPos;
            _hasNavPathOrigin = true;
        }

        /// <summary>
        /// 左键点击圆盘内部（不含外圈装饰）时触发。战斗 HUD 用它打开大地图——
        /// 小地图只显示附近一小圈，玩家点它通常是想看更大的范围。
        /// </summary>
        public event Action Clicked;

        /// <inheritdoc />
        public override bool OnMouseUp(Float2 location, MouseButton button)
        {
            if (button == MouseButton.Left)
            {
                var center = new Float2(Width * 0.5f, Height * 0.5f);
                float radius = Mathf.Min(Width, Height) * 0.5f;
                if (radius > 0f && Float2.Distance(location, center) <= radius)
                {
                    Clicked?.Invoke();
                    return true;
                }
            }

            return base.OnMouseUp(location, button);
        }

        /// <summary>
        /// 从 NarrativePro 插件导航刷新玩家位置/朝向与 POI 点位（玩家为圆心，坐标归一化到 -1~1）。
        /// <para>
        /// 插件导航不可用（场景无 MapTileBoundsActor / 无 POI / 无导航组件）时返回 false 且**不清空**现有点位，
        /// 因此既有调用方（AddEntity 手工喂数据）的展示不受影响。
        /// </para>
        /// </summary>
        /// <returns>是否完成刷新。</returns>
        public bool RefreshFromNavigation()
        {
            var views = new System.Collections.Generic.List<MapMarkerView>();
            if (!PluginNavigationBridge.TryGetMarkers(views)) return false;
            if (!PluginNavigationBridge.TryGetPlayerPose(out var playerPos, out var yaw)) return false;

            PlayerYaw = yaw;
            ClearEntities();

            AddEntity(InkMinimapEntityType.Player, 0f, 0f);

            float range = NavigationRange <= 0f ? 1f : NavigationRange;
            foreach (var v in views)
            {
                // 迷雾语义：小地图只显示已发现的目标；未发现的 POI 由世界地图以「？」提示方位
                if (v.IsDiscoverable && !v.Discovered) continue;

                float x = (v.WorldPosition.X - playerPos.X) / range;
                float z = (v.WorldPosition.Z - playerPos.Z) / range;
                if (x < -1f || x > 1f || z < -1f || z > 1f) continue;

                // 支持快速旅行的 POI 用翡翠色（友方样式）区分，其余按 NPC 样式；
                // 移动目的地走独立样式（实心点 + 环）
                var type = v.Tag == PluginNavigationBridge.MoveDestinationTag
                    ? InkMinimapEntityType.Destination
                    : (v.SupportsFastTravel ? InkMinimapEntityType.Friendly : InkMinimapEntityType.NPC);
                AddEntity(type, x, z);
            }

            return true;
        }

        /// <summary>
        /// 添加一个地形快照/兴趣点。
        /// 用于在地图上绘制地形块（水域、山地、建筑等）。
        /// </summary>
        /// <param name="relativeX">相对 X 坐标，-1=最左，1=最右</param>
        /// <param name="relativeZ">相对 Z 坐标，-1=最上，1=最下</param>
        /// <param name="radius">相对半径（0~1），决定地形块在小地图上的大小</param>
        /// <param name="color">地形块颜色</param>
        public void AddLandmark(float relativeX, float relativeZ, float radius, Color color)
        {
            _landmarks.Add((
                Mathf.Clamp(relativeX, -1f, 1f),
                Mathf.Clamp(relativeZ, -1f, 1f),
                Mathf.Clamp(radius, 0.02f, 1f),
                color));
        }

        /// <summary>
        /// 清除所有地形快照/兴趣点。
        /// </summary>
        public void ClearLandmarks()
        {
            _landmarks.Clear();
        }

        /// <inheritdoc />
        public override void Draw()
        {
            if (!Visible || Width <= 0f || Height <= 0f)
                return;

            var center = new Float2(Width * 0.5f, Height * 0.5f);
            float radius = Mathf.Min(Width, Height) * 0.5f;
            if (radius <= 0f)
                return;

            float innerRadius = Mathf.Max(0f, radius - RingThickness);
            float entityRadius = Mathf.Max(0f, radius - RingThickness - EntityPadding);

            // 圆内底色：HUD 叠在 3D 世界上，亮场景（雪地/沙漠）里没有底色时圆盘与方位字几乎看不见。
            // 半透明墨底（0.5）既压住背景又保留一点透视感，与 HUD 其它浮层口径一致。
            var discBase = InkWashTheme.BaseDefault;
            InkRenderHelper.FillCircle(center, radius, new Color(discBase.R, discBase.G, discBase.B, 0.50f));

            // 1. 雷达样式专属：四方位点 + 同心圆网格（普通圆形不画，保持圆内干净）
            if (FrameStyle == InkMinimapFrameStyle.Radar)
            {
                DrawCompassMarks(center, radius);
                DrawRangeRings(center, innerRadius);
            }

            // 2. 地形快照 / 兴趣点（半透明块）
            for (int i = 0; i < _landmarks.Count; i++)
            {
                var lm = _landmarks[i];
                float px = center.X + lm.x * entityRadius;
                float py = center.Y + lm.z * entityRadius;
                var pos = new Float2(px, py);
                float lmR = Mathf.Max(1f, lm.radius * entityRadius);
                InkRenderHelper.FillCircle(pos, lmR, lm.color);
            }

            // 3. 导航路径（自动移动中）：线画在点位之下，目的地圆点压在线端上
            DrawNavPath(center, entityRadius);

            // 4. 实体点位
            for (int i = 0; i < _entities.Count; i++)
            {
                var entity = _entities[i];
                float px = center.X + entity.x * entityRadius;
                float py = center.Y + entity.z * entityRadius;
                var pos = new Float2(px, py);

                Color color = GetEntityColor(entity.type);
                float dotRadius = entity.type == InkMinimapEntityType.Player
                    ? PlayerDotRadius
                    : EntityDotRadius;

                // 玩家点位附加外发光与方向三角
                if (entity.type == InkMinimapEntityType.Player)
                {
                    Color glow = new Color(color.R, color.G, color.B, 0.35f);
                    InkRenderHelper.FillCircle(pos, dotRadius + 4f, glow);
                    DrawPlayerDirection(pos, dotRadius + 2f, PlayerYaw);
                }

                InkRenderHelper.FillCircle(pos, dotRadius, color);

                // 目的地：外套一圈环描边，避免与 NPC 的金色点混淆
                if (entity.type == InkMinimapEntityType.Destination)
                    DrawCircleOutline(pos, dotRadius + 4f, color, 1.5f);
            }

            // 5. 外框（最上层，覆盖所有内容）：优先用贴图，否则细圆环
            if (FrameStyle == InkMinimapFrameStyle.Texture && FrameTexture != null)
            {
                float pad = FrameTextureInflate;
                var frameRect = new Rectangle(-pad, -pad, Width + pad * 2f, Height + pad * 2f);
                Render2D.DrawTexture(FrameTexture, frameRect, Color.White);
            }
            else
            {
                DrawCircleOutline(center, radius, InkWashTheme.BorderGold, RingThickness);
            }

            // 6. 方向标签（N/E/S/W）
            DrawDirectionLabels(center, radius);
        }

        /// <summary>
        /// 绘制自动移动的面包屑路径：纸白虚线 + 半透明垫底，按小地图圆盘裁剪。
        /// 投影与 <see cref="RefreshFromNavigation"/> 的点位共用同一玩家锚点与范围，
        /// 所以线与目的地圆点不会各走各的。
        /// </summary>
        private void DrawNavPath(Float2 center, float drawRadius)
        {
            if (!_hasNavPathOrigin || drawRadius <= 0f || _navPathWorld.Count < 2) return;

            float range = NavigationRange <= 0f ? 1f : NavigationRange;
            _navPathScreen.Clear();

            for (int i = 0; i < _navPathWorld.Count; i++)
            {
                var world = _navPathWorld[i];
                float x = (world.X - _navPathOrigin.X) / range;
                float z = (world.Z - _navPathOrigin.Z) / range;
                _navPathScreen.Add(center + new Float2(x, z) * drawRadius);
            }

            var baseColor = InkWashTheme.PaperBright;

            // 垫底：圆内是透出的场景，细虚线单独放上去对比度不够
            InkNavPathPainter.DrawInDisc(_navPathScreen, center, drawRadius,
                new Color(baseColor.R, baseColor.G, baseColor.B, 0.22f),
                InkNavPathPainter.DefaultThickness + 3f,
                InkNavPathPainter.DefaultDashLength, InkNavPathPainter.DefaultGapLength);

            InkNavPathPainter.DrawInDisc(_navPathScreen, center, drawRadius,
                new Color(baseColor.R, baseColor.G, baseColor.B, 0.85f),
                InkNavPathPainter.DefaultThickness,
                InkNavPathPainter.DefaultDashLength, InkNavPathPainter.DefaultGapLength);
        }

        /// <summary>
        /// 绘制东南西北四个方位标记。
        /// </summary>
        private static void DrawCompassMarks(Float2 center, float radius)
        {
            float markRadius = radius - 6f;
            if (markRadius <= 0f)
                return;

            float markSize = 2.5f;
            Color markColor = InkWashTheme.BorderNeutralL3;

            // 北
            InkRenderHelper.FillCircle(center + new Float2(0f, -markRadius), markSize, markColor);
            // 南
            InkRenderHelper.FillCircle(center + new Float2(0f, markRadius), markSize, markColor);
            // 东
            InkRenderHelper.FillCircle(center + new Float2(markRadius, 0f), markSize, markColor);
            // 西
            InkRenderHelper.FillCircle(center + new Float2(-markRadius, 0f), markSize, markColor);
        }

        /// <summary>
        /// 绘制同心圆范围网格。
        /// </summary>
        private static void DrawRangeRings(Float2 center, float radius)
        {
            if (radius <= 0f)
                return;

            Color ringColor = InkWashTheme.BorderNeutralL3;

            for (int i = 1; i <= 3; i++)
            {
                float r = radius * (i / 4f);
                DrawCircleOutline(center, r, ringColor, 1f);
            }

            // 十字方向线
            Render2D.DrawLine(center + new Float2(-radius, 0f), center + new Float2(radius, 0f), ringColor, 1f);
            Render2D.DrawLine(center + new Float2(0f, -radius), center + new Float2(0f, radius), ringColor, 1f);
        }

        /// <summary>
        /// 绘制玩家朝向三角。
        /// </summary>
        private static void DrawPlayerDirection(Float2 center, float radius, float yawDeg)
        {
            if (radius <= 0f)
                return;

            float angleRad = Mathf.DegreesToRadians * yawDeg;
            float dirX = Mathf.Sin(angleRad);
            float dirY = -Mathf.Cos(angleRad);

            float tipLen = radius + 3f;
            float baseHalf = radius * 0.6f;

            var tip = center + new Float2(dirX * tipLen, dirY * tipLen);
            var baseCenter = center + new Float2(dirX * radius * 0.3f, dirY * radius * 0.3f);
            var perp = new Float2(-dirY, dirX) * baseHalf;
            var base1 = baseCenter + perp;
            var base2 = baseCenter - perp;

            var vertices = new Float2[] { tip, base1, base2 };
            Render2D.FillTriangles(vertices, InkWashTheme.GoldBright);
        }

        /// <summary>
        /// 绘制北/东/南/西方位文字标签。
        /// 对齐 combat-hud-v2.html 设计：
        /// 北方位使用鎏金亮色（GoldBright）突出主方位，
        /// 南/东/西使用褪色纸色（PaperFaded）作为次要方位，
        /// 字体采用 Display（毛笔书法）字号 11，与设计 .compass 一致。
        /// </summary>
        private static void DrawDirectionLabels(Float2 center, float radius)
        {
            if (radius <= 24f)
                return;

            var font = InkWashTheme.GetFont(InkWashTheme.FontRole.Display);
            if (font == null)
            {
                font = InkWashTheme.GetFont(InkWashTheme.FontRole.Heading);
            }
            if (font == null)
                return;

            var fontRef = new FontReference(font, 11f);
            float labelOffset = radius - 12f;

            // 北：鎏金亮色（主方位突出，对齐设计 .compass-n 的 ink-gold-bright）
            DrawLabel(fontRef, center + new Float2(0f, -labelOffset), "北", InkWashTheme.GoldBright);
            // 南/东/西：褪色纸色（次要方位，对齐设计 .compass-s/.compass-w/.compass-e 的 ink-paper-faded）
            Color fadedColor = InkWashTheme.PaperFaded;
            DrawLabel(fontRef, center + new Float2(0f, labelOffset), "南", fadedColor);
            DrawLabel(fontRef, center + new Float2(labelOffset, 0f), "东", fadedColor);
            DrawLabel(fontRef, center + new Float2(-labelOffset, 0f), "西", fadedColor);
        }

        private static void DrawLabel(FontReference fontRef, Float2 center, string text, Color color)
        {
            // 字号提升至 14px 以适配 11px 字体的毛笔书法字形
            float size = 14f;
            var rect = new Rectangle(center.X - size * 0.5f, center.Y - size * 0.5f, size, size);
            var font = fontRef.GetFont();
            if (font != null)
            {
                Render2D.DrawText(font, text, rect, color,
                    TextAlignment.Center, TextAlignment.Center, TextWrapping.NoWrap);
            }
        }

        /// <summary>
        /// 根据实体类型返回对应点位颜色。
        /// </summary>
        /// <param name="type">实体类型</param>
        /// <returns>对应主题色</returns>
        private static Color GetEntityColor(InkMinimapEntityType type)
        {
            return type switch
            {
                InkMinimapEntityType.Player => InkWashTheme.GoldPrimary,
                InkMinimapEntityType.Friendly => InkWashTheme.JadeBright,
                InkMinimapEntityType.Enemy => InkWashTheme.VermilionBright,
                InkMinimapEntityType.NPC => InkWashTheme.GoldBright,
                InkMinimapEntityType.Destination => InkWashTheme.PaperBright,
                _ => InkWashTheme.GoldBright
            };
        }

        /// <summary>
        /// 使用多段 <see cref="Render2D.DrawLine"/> 近似绘制圆环描边。
        /// </summary>
        /// <param name="center">圆心</param>
        /// <param name="radius">圆半径</param>
        /// <param name="color">描边颜色</param>
        /// <param name="thickness">线宽</param>
        private static void DrawCircleOutline(Float2 center, float radius, Color color, float thickness)
        {
            if (radius <= 0f)
                return;

            float angleStep = Mathf.TwoPi / CircleSegments;
            for (int i = 0; i < CircleSegments; i++)
            {
                float a1 = i * angleStep;
                float a2 = (i + 1) * angleStep;
                var p1 = center + new Float2(Mathf.Cos(a1) * radius, Mathf.Sin(a1) * radius);
                var p2 = center + new Float2(Mathf.Cos(a2) * radius, Mathf.Sin(a2) * radius);
                Render2D.DrawLine(p1, p2, color, thickness);
            }
        }
    }
}
