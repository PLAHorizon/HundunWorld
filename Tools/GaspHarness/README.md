# GASP 离线核心验证

不依赖编辑器/PIE，直接验证 Motion Matching 的核心链路：

1. 读取 `HundunWorld/Content/Gasp/gasp_anims.bin`（由 `Tools/GaspBakeAnimations.py` 生成）；
2. 读取 `Tools/GaspSkeletonExport.py` 从 Ada `.flax` 解出的真实骨架（343 节点、层级、绑定姿态）；
3. 构建逐帧特征库，检查：
   - 剪辑/通道/采样率/有限值；
   - 每个剪辑中间帧能检索回自身/同族；
   - Walk/Run/Sprint 正向查询命中对应族；
   - 标称速度与前向偏航校准（Ada+ALS 实测前向约 180°）；
   - 连续 2 秒同速查询时剪辑不频繁切换。

## 运行

```bash
# 1) 先确保 Game.CSharp.dll 是当前源码编出来的（Editor.Windows.Debug）
#    见 docs/GASP_ANIMATION_SYSTEM_DESIGN.md 的构建命令
# 2) 运行
bash Tools/GaspHarness/run_gasp_harness.sh
```

退出码 0 = 全部断言通过；非 0 时逐条看 `[FAIL]`。

## 说明

- `FlaxRoot` 默认 `C:\Program Files (x86)\Flax\Flax_1.13`，可覆盖：
  `dotnet build Tools/GaspHarness/GaspHarness.csproj -p:FlaxRoot=<你的 Flax 目录>`
- `GameConfig` 默认 `Debug`；编辑器占着 Debug DLL 时可改用 Release：
  `dotnet build Tools/GaspHarness/GaspHarness.csproj -p:GameConfig=Release`
  `dotnet run --project Tools/GaspHarness/GaspHarness.csproj -p:GameConfig=Release --no-build`
- 这个 harness 只验证纯 C# 核心与数据；`SetCurrentPose` 等引擎写回与脚部射线仍需 PIE 取证。
