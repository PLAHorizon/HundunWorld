# 耕地 HarmonyOS ArkTS 项目 — DevEco Studio 编译运行指南

## 项目位置

```
c:\Works\GitHubProjects\20260801\Horizon.Game.GengDi.HarmonyOS\ArkTS
```

## 环境要求

- DevEco Studio 6.1.1 或更高版本
- HarmonyOS SDK 6.1.1 (API 24)
- Java JDK 17+（DevEco Studio 自带）
- Node.js 18+（DevEco Studio 自带）
- HarmonyOS 模拟器 或 真机设备

## 编译运行步骤

### 1. 导入项目

1. 打开 DevEco Studio
2. `File` > `Open` > 选择 `ArkTS` 目录
3. 等待 IDE 自动同步依赖（`ohpm install`），底部状态栏显示 "Sync finished"

### 2. 配置 SDK

如果提示 SDK 未配置：
1. `File` > `Project Structure` > `Project` > `SDK`
2. 选择 HarmonyOS SDK 6.1.1(24)
3. 点击 `Apply` > `OK`

> **注意**：`build-profile.json5` 中 `compatibleSdkVersion` 已设置为 `6.1.1(24)`，与 DevEco Studio 6.1.1 内置 SDK 匹配。

### 3. 配置签名（真机调试需要）

1. `File` > `Project Structure` > `Signing Configs`
2. 勾选 `Automatically generate signature`（调试模式自动签名）
3. 或手动配置 `.p12` 证书和 `.cer` 配置文件
4. 模拟器调试可跳过此步

### 4. 启动模拟器

1. `Tools` > `Device Manager`
2. 创建或启动一个 Phone 模拟器（API 12）
3. 等待模拟器启动完成

### 5. 编译并运行

1. 点击工具栏 `Run` 按钮（绿色三角形）
2. 或使用快捷键 `Shift + F10`
3. 选择目标设备（模拟器或真机）
4. 等待编译完成并自动安装到设备

### 6. 命令行编译（可选）

项目根目录已包含 `hvigorw.bat` 脚本，自动配置 DevEco Studio 内置工具链路径：

```bash
# 进入项目根目录
cd c:\Works\GitHubProjects\20260801\Horizon.Game.GengDi.HarmonyOS\ArkTS

# 编译 debug 版本 HAP（打包阶段依赖 java，需先指向 DevEco 自带 JBR）
export JAVA_HOME="C:\ProgramData\Huawei\DevEco Studio\jbr"
export PATH="$JAVA_HOME/bin:$PATH"
hvigorw.bat assembleHap --mode module -p product=default -p buildMode=debug --no-daemon

# 清理后重新编译
hvigorw.bat clean --no-daemon
hvigorw.bat assembleHap --mode module -p product=default -p buildMode=debug --no-daemon
```

构建产物位于：`entry/build/default/outputs/default/entry-default-unsigned.hap`

## 验证要点

### WebView 页面加载验证

| 验证项 | 操作方式 | 预期结果 |
|--------|---------|---------|
| 首页加载 | 应用启动后默认显示 | 暗色背景 + 市场速览卡片 + 导航栏 |
| Tab 切换 | 点击底部 5 个标签 | WebView 切换到对应 HTML 页面 |
| 更多菜单 | 点击底部"更多"按钮 | 底部弹窗显示 3 个次级页面入口 |
| 次级页面 | 点击弹窗中的页面项 | 跳转到 WebViewPage + 顶部返回栏 |
| 返回导航 | 点击 WebViewPage 左上角"‹" | 返回主页 |
| CDN 资源 | 检查页面样式 | Tailwind CSS 样式正常渲染 |
| Chart.js | 查看市场/天气页 | 图表正常绘制 |
| Lucide 图标 | 查看各页面图标 | 图标正常显示 |

### 常见问题排查

#### 1. CDN 资源加载失败
**现象**：页面无样式，图表不显示
**原因**：设备无网络或 CDN 被墙
**解决**：
- 确保设备/模拟器有网络连接
- 模拟器需配置 DNS：`Settings` > `Network` > DNS 设为 `8.8.8.8`
- 或将 CDN 资源下载到 rawfile 目录，修改 HTML 引用为本地路径

#### 2. rawfile 资源 404
**现象**：WebView 显示空白或 404
**原因**：HTML 文件名不匹配
**验证**：检查 `TabConfig.ets` 中的 `page` 字段与 `rawfile/` 目录下文件名一致

#### 3. 签名错误
**现象**：`ERROR: sign signature failed`
**解决**：在 `Project Structure` > `Signing Configs` 中重新生成签名

#### 4. SDK 版本不匹配
**现象**：`compatibleSdkVersion` 报错
**解决**：修改 `build-profile.json5` 中的 `compatibleSdkVersion` 为本地已安装的 SDK 版本

#### 5. ohpm 依赖安装失败
**现象**：`ohpm install` 超时或报错
**解决**：
```bash
# 配置 ohpm 代理
ohpm config set registry https://ohpm.openharmony.cn/ohpm/
# 或设置 npm 镜像
ohpm config set strict_ssl false
```

## 项目架构概览

```
ArkTS/
├── AppScope/
│   ├── app.json5                          # 应用配置（bundleName, version）
│   └── resources/base/
│       ├── element/string.json            # 应用名称
│       └── media/app_icon.png             # 应用图标
├── entry/
│   ├── src/main/
│   │   ├── ets/
│   │   │   ├── entryability/
│   │   │   │   └── EntryAbility.ets       # 入口 Ability（窗口管理）
│   │   │   ├── model/
│   │   │   │   └── TabConfig.ets          # 标签页配置（8 个页面路由）
│   │   │   └── pages/
│   │   │       ├── Index.ets              # 主页（WebView + TabBar + 更多弹窗）
│   │   │       └── WebViewPage.ets        # 次级页面容器（带返回导航）
│   │   ├── resources/
│   │   │   ├── base/
│   │   │   │   ├── element/
│   │   │   │   │   ├── color.json         # 颜色资源
│   │   │   │   │   └── string.json        # 字符串资源
│   │   │   │   ├── media/app_icon.png     # 模块图标
│   │   │   │   └── profile/main_pages.json # 页面路由配置
│   │   │   └── rawfile/                   # 8 个 HTML 原型
│   │   │       ├── harmony-home.html      # 首页
│   │   │       ├── harmony-weather.html   # 天气
│   │   │       ├── harmony-market.html    # 市场
│   │   │       ├── harmony-discover.html  # 发现
│   │   │       ├── harmony-profile.html   # 我的
│   │   │       ├── harmony-music.html     # 音乐播放器
│   │   │       ├── harmony-forecast.html  # 市场预报
│   │   │       └── harmony-alerts.html    # 商家预警
│   │   └── module.json5                   # 模块配置（权限、Ability）
│   ├── build-profile.json5                # 模块构建配置
│   ├── hvigorfile.ts                      # 模块构建脚本
│   ├── obfuscation-rules.txt              # 混淆规则
│   └── oh-package.json5                   # 模块依赖
├── build-profile.json5                    # 项目构建配置
├── hvigor-config.json5                    # Hvigor 构建引擎配置
├── hvigorfile.ts                          # 项目构建脚本
└── oh-package.json5                       # 项目依赖
```

## WebView 技术配置说明

| 配置项 | 值 | 作用 |
|--------|---|------|
| `javaScriptAccess` | `true` | 执行 HTML 中的 JS（Tailwind/Lucide/Chart.js） |
| `domStorageAccess` | `true` | localStorage 持久化 |
| `fileAccess` | `true` | 读取 rawfile 本地资源 |
| `imageAccess` | `true` | 加载图片资源 |
| `onlineImageAccess` | `true` | 加载 CDN 在线图片 |
| `mixedMode` | `MixedMode.All` | 允许 HTTPS 内容在本地资源上下文中加载 |
| `zoomAccess` | `false` | 禁用缩放，保持移动端体验 |
| 资源协议 | `resource://rawfile/` | HarmonyOS rawfile 资源访问协议 |
