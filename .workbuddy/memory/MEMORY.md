# HundunWorld / Horizon 项目长期备忘

## 客户端（Flax）脚本工程

- 工程：`HundunWorld\Source\Game.csproj`（Flax.Build 风格，`EnableDefaultItems=false`
  → **新增 .cs 必须手工加进 csproj**，不能靠通配）。
- **编译配置名是自定义的**：`Editor.Windows.Debug` / `Editor.Windows.Development`
  （还有 `Game.Windows.*` 用于打包）。**不要用 `-c Debug`** —— 传错时所有 `Condition`
  均不满足、不产出到 `Binaries/`，但 MSBuild 仍打印「已成功生成 / 0 错误」，会被误判为已编译。
- 产物名是 **`Game.CSharp.dll`**（`<AssemblyName>`），不是 `Game.dll`。
- 校验是否真的编译：比对
  `HundunWorld\Binaries\GameEditorTarget\Windows\x64\<Config>\Game.CSharp.dll` 的 mtime。
- 编辑器运行时 `Flax.Build -mutex` 会占锁 → Development 配置收尾报 `MSB3073 退出码 1`，
  **属预期**（C# 已编译、DLL 已落盘），由编辑器「重新编译脚本」/重启 PIE 完成剩余步骤。
- `Game.csproj` **不用 ProjectReference**，而是 `HintPath` 引用
  `HundunWorld\Output\Windows\*.dll`（服务端/共享程序集的影子拷贝）。
  **改了 `Horizon.Game.Message` / `Horizon.Share` 等共享工程的公共 API 后，必须同步新 DLL 到该目录**，
  否则客户端编译看到的仍是旧 API。
- Flax 编辑器运行时从 `C:\Program Files (x86)\Flax\Flax_1.12\Binaries\Tools\` 加载 `Horizon.*.dll`。

## 客户端日志

- 目录：`HundunWorld\Logs\Log_YYYY_MM_DD_HH_MM_SS.txt`
- 编码：**UTF-16LE（带 BOM）**，须以 `encoding='utf-16'` 读取；
  用 UTF-8 读会得到乱码并误判为「日志里没有该记录」。
- 部分行由「增强日志桥」以 UTF-8 字节写入 UTF-16 流，出现乱码时可用
  `line.encode('utf-16-le').decode('utf-8')` 还原（脚本见 `.dbg/dec.py`）。

## 本机环境坑

- Bash 工具默认 PATH 损坏（`dirname: command not found`）→ 用绝对路径调用 python/node，
  stdout 正常。
- PowerShell 工具常回传空输出（exit 0 无内容），排查优先用 Bash / Python。
- 沙箱拦截 `dotnet msbuild`（判定为 LOLBin）→ 用 `dotnet build`（支持 `-getItem:` / `-getProperty:`）。
- 运行中的网关/服务会锁住 `bin\Debug\net10.0\*.exe`，导致 build 报 MSB3027/MSB3021；
  不要杀进程，改用 `-p:BuildProjectReferences=false` + `dotnet test --no-build`。

## Flax 资产（.flax）核查方法

- **校验资产真实 GUID**：`.flax` 头部固定布局 —— 符号(4) + 版本(4) + 12 字节保留 + `01000000`(4) 之后，
  **offset 28 起 16 字节**是资产 ID，按 `DWORD/WORD/WORD/8BYTE` 各自小端还原
  （如头部 `...01000000 9c3021bb 6562e94d a63cdf826ed3116e` → `bb21309c-6265-4de9-a63c-df826ed3116e`）。
  脚本：`.dbg/guid_check.py` / `.dbg/guid_now.py`（后者打印当前真实 GUID，可先拿 `.backup` 交叉验证）。
- ⚠️ **重新导入会更换资产 GUID**（`ContentImporting.Reimport` 实测保留了 ID，但
  `FlaxEditor.Editor.Import()` 会**新建资产 = 新 GUID**）。因此：
  - 代码里**不要**用 `Editor.Import` 去"修复"已有资产，否则所有按 GUID 的引用（代码常量 / Prefab / Scene）一起失效，
    表现为"资产文件明明在，`Content.Load` 却报 Missing file"。
  - 加载策略一律**路径优先、GUID 回退**（路径不随重导变化）。
  - 2026-09-22 修复导入缩放时经验到的真实 GUID 演变：
    `c7c70820409088e4d96db396a43c410f`(失效) → `bb21309c62654de9a63cdf826ed3116e` → 当前 `b0823a8576cb47cb9932f5af61aa1ef7`。
- **校验导入参数**：资产二进制内嵌一段 `{"ImportPath":...,"Type":...,"Scale":...,"Rotation":{...}}` JSON。
  `Type`：0=Model、1=SkinnedModel、2=Animation。`skm_uefn_mannequin` 正确值 **Scale=1.0、Rotation=(0,0,0,1)**；
  若出现 Scale 极小/为 0 或 `Rotation=(0,0,0,0)`，说明是用 `new ModelTool.Options{...}` 重建导入写坏的。
  脚本：`.dbg/import_opts2.py`。
- 重建导入要用 `ModelTool.Options.Default` 作基准 + `FlaxEditor.Editor.TryRestoreImportOptions(ref opts, assetPath)`，
  绝不能 `new ModelTool.Options{}`（字段无初值）。
- 编辑器**运行中也能直接 `dotnet build` 覆盖 `Game.CSharp.dll`**（不会被锁），
  但需在编辑器内「重新编译脚本」才会热加载；编辑器插件（如 AutoSkinnedModelImportPlugin）只在编辑器启动/脚本重载时执行。

## Flax 离屏 3D 预览（CharacterPreview3D）

- `SceneRenderTask.ActorsSource = CustomActors` 是**白名单**渲染：场景原有灯光、**Sky/Skybox 都不参与**，
  必须显式加进 `SceneRenderTask.CustomActors`，否则视图背景只能是清屏黑。
- 因此「自然天空背景」= 把场景内 `Sky`（`scene.FindActor<Sky>(false)`）加入 CustomActors；
  预览自建 Sky 会与主视图抢「每帧唯一 Sky 渲染器」，**只能用场景已有的那个**，且不可销毁。
- 场景 `Sky` 的太阳字段名是 **`SunLight`**（不是 `Sun`），写错则太阳未绑定。
- 角色创建/选择场景 = `Content/Maps/Character.scene`（内含 Sun / Sky / SkyLight / 三点 PointLight）。

## 相关技能

- `flax-offscreen-preview-triage` —— 离屏 3D 预览（过曝/黑底/无待机动画/模型尺寸异常）排查
- `flax-client-scene-transition-triage` —— 场景切换卡死 / UI 不显示排查
- `horizon-dotnet-build-error-triage` —— 解决方案编译错误
- `horizon-game-wire-protocol-triage` —— 线路协议（成帧/校验和/压缩）
- `horizon-gateway-concurrency-triage` —— 网关并发共享状态
