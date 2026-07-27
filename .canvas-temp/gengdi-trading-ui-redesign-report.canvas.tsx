import { Stack, Row, Grid, H1, H2, H3, Text, Divider, Stat, Table, Tag, Callout, Timeline, Card, CardHeader, CardBody, Pill } from 'qoder/canvas';

export default function GengDiTradingUIRedesignReport() {
  return (
    <Stack gap={24}>
      <Stack gap={8}>
        <H1>GengDi Trading UI Redesign</H1>
        <Text tone="secondary">
          Horizon.Game.GengDi (Avalonia UI) 全面 UI 重构 — 基于 gengdi-trading-ui-redesign HTML 原型，暗色专业终端风格，品牌蓝 #2962ff，背景 #0a0e17
        </Text>
      </Stack>

      <Grid columns={4} gap={16}>
        <Stat value="60+" label="文件修改" />
        <Stat value="0" label="编译错误" tone="success" />
        <Stat value="10" label="阶段完成" tone="success" />
        <Stat value="4" label="风险点" tone="warning" />
      </Grid>

      <Divider />

      <H2>核心变更</H2>
      <Table
        headers={['区域', '变更内容', '设计依据']}
        rows={[
          ['Shell 导航框架', '56px 顶栏（渐变 Logo + 图标导航 + 通知红点 + 用户胶囊）+ 44px 副导航（图标 + badge + pill active）', 'project-shell.html'],
          ['HomeView 主页', '平面天气卡（gradient 背景）+ 4 列天气子卡 + 节气横幅 + 6 列快速访问', 'home-preview.html'],
          ['LoginView 登录', '3:2 双栏（brand 渐变 hero + 装饰几何 + 信息 chip + 表单 + 第三方登录）', 'main-pages-auth.html'],
          ['RegisterView 注册', '匹配登录页风格的 3:2 双栏布局', 'main-pages-auth.html'],
          ['FlowerDashboard 仪表盘', '指标卡图标 + mono 字体 + 预警卡左边框装饰', 'flower-market-data.html'],
          ['FlowerDataScreen 数据大屏', '4列 screen-metric 统计卡（左边框3px primary + 图标 + mono数值）', 'flower-market-data.html'],
          ['FlowerAlertCenter 预警中心', '统计栏图标容器 + 预警卡 36x36 表面色图标容器（按级别着色）+ mono 触发值', 'flower-market-data.html'],
          ['FlowerShop 商城', '商品卡 180px 渐变图片区(brand-600→400)+ 半透明品种标签 + GdPill 筛选', 'flower-trade-flow.html'],
          ['FlowerCart 购物车', '56px 渐变图标 + 数量步进器 + 结算栏重排', 'flower-trade-flow.html'],
          ['FlowerProductDetail 详情', '商品图渐变封面 + 对比图渐变(两种色阶)', 'flower-trade-flow.html'],
          ['FlowerOrderCenter 订单', '订单卡渐变迷你封面 + border 分隔线 + GdPill 状态筛选', 'flower-trade-flow.html'],
          ['FlowerSpeciesDetail 品种详情', '关联商品 48px 渐变封面', 'flower-manage.html'],
          ['MiniPlayer 播放器', '48px 渐变封面 + 32px 圆形 brand-500 播放按钮 + mono 时间标签', 'chrome-fixedbar.html'],
          ['MusicDiscover 发现', '渐变歌曲/歌单封面 + 排行榜 tab active 态 + 行内渐变迷你封面', 'music-module.html'],
          ['MusicPlayer 全屏播放器', '260px 圆形渐变封面(brand-400→800)+shadow + 歌词区 + 56px 圆形播放按钮', 'music-module.html'],
          ['PlaylistManage 歌单', '左侧歌单列表渐变封面 + 歌曲行 gd-row muted 背景 + 渐变迷你封面', 'music-module.html'],
          ['MusicSearch 搜索', '搜索结果 gd-row 背景 + 渐变迷你封面', 'music-module.html'],
          ['MusicStory 音乐故事', '渐变故事封面(brand-600→900) + 引用块左边框强调', 'music-module.html'],
          ['ToastContainer', '360px 宽度 + 左边框 3px 强调色（按类型）', 'overlays-dialogs.html'],
          ['全局样式层', 'GdControls + ControlsGalleryStyles + Resources 图标 + GalleryPage 轻量模板', 'colors_and_type.css'],
        ]}
      />

      <Divider />

      <H2>实施阶段时间线</H2>
      <Timeline
        events={[
          { id: 'p1', title: 'Phase 1: Shell 框架重构', description: 'MainView 顶栏 + 副导航 + 导航样式', timestamp: '完成' },
          { id: 'p2', title: 'Phase 2: HomeView 主页', description: '平面天气卡 + 子卡网格 + 节气横幅 + 快速访问', timestamp: '完成' },
          { id: 'p3', title: 'Phase 3: 认证页面', description: 'LoginView + RegisterView 双栏重构', timestamp: '完成' },
          { id: 'p4', title: 'Phase 4: 花卉市场数据', description: 'Dashboard 指标卡 + DataScreen + AlertCenter', timestamp: '完成' },
          { id: 'p5', title: 'Phase 5: 花卉交易流程', description: 'Shop + Cart + Order + Address + ProductDetail', timestamp: '完成' },
          { id: 'p6', title: 'Phase 6-10: 其余页面', description: '花卉管理 + 音乐 + 抽屉 + 固定栏 + 内容页（全局样式覆盖）', timestamp: '完成' },
        ]}
      />

      <Divider />

      <H2>风险点与待确认项</H2>
      <Grid columns={2} gap={12}>
        <Callout tone="warning" title="图标近似">
          FluentAvalonia Symbol 枚举有限，SproutIcon/CartIcon/AIIcon 为近似替代，后续可替换为 PathIcon 自绘 SVG
        </Callout>
        <Callout tone="warning" title="图表占位">
          HomeView 温度趋势图为占位符，需接入 LiveCharts2 或自绘 Path
        </Callout>
        <Callout tone="info" title="快速访问命令">
          HomeView 6 列快速访问中花卉/音乐相关项暂绑定占位命令，需在 ViewModel 层补充导航命令
        </Callout>
        <Callout tone="info" title="字体可用性">
          设计指定 Inter + JetBrains Mono，当前为 FontFamily fallback 声明，需确认运行环境已安装
        </Callout>
      </Grid>

      <Divider />

      <H2>验证步骤</H2>
      <Card>
        <CardBody>
          <Stack gap={8}>
            <Row gap={8}>
              <Tag tone="success">编译</Tag>
              <Text>dotnet build Horizon.Game.GengDi/Horizon.Game.GengDi.csproj — 0 错误通过</Text>
            </Row>
            <Row gap={8}>
              <Tag tone="info">运行</Tag>
              <Text>dotnet run --project Horizon.Game.GengDi/Horizon.Game.GengDi.csproj</Text>
            </Row>
            <Row gap={8}>
              <Tag>视觉比对</Tag>
              <Text>逐页对照 gengdi-trading-ui-redesign/pages/*.html 进行还原度检查</Text>
            </Row>
          </Stack>
        </CardBody>
      </Card>

      <Text tone="secondary" size="small">
        设计源: gengdi-trading-ui-redesign/ (13 HTML 页面 + project-shell + colors_and_type.css) | 目标: Horizon.Game.GengDi (Avalonia UI + FluentAvalonia)
      </Text>
    </Stack>
  );
}
