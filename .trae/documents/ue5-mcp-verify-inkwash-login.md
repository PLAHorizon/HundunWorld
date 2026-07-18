# 水墨登录界面 UE5 MCP 落地验证计划

## 摘要

通过 UE5 MCP 的 `execute_python_code` 工具，执行端到端验证脚本，确认前三项资产落地任务（纹理导入、Widget Blueprint 创建、关卡 Actor 配置）均正确完成。验证通过后，进入 PIE（Play In Editor）模式截图确认水墨登录界面的实际渲染效果。

## 当前状态分析

### 已完成任务（根据上一轮执行记录）

| 任务 | 状态 | 产出 |
|------|------|------|
| 任务 A — 导入水墨背景纹理 | 已完成 | `/Game/UI/Login/Textures/T_LoginBG_InkWash`（Texture2D, TC_Default, sRGB=True） |
| 任务 B — 创建 WBP_InkWashLogin | 已完成 | `/Game/UI/WBP_InkWashLogin`（父类 InkWashLoginWidget_C, WidgetTree 空白, 已编译, `_C` 类可加载） |
| 任务 C — 配置 Start 关卡 Actor | 已完成 | Start 关卡中 LoginScreenManager_C Actor 的 `UseInkWashDesign` 已从 False 设为 True, 关卡已保存 |

### 待验证项

| 验证点 | 预期结果 | 关联 C# 常量 |
|--------|---------|-------------|
| 纹理资产存在 | `/Game/UI/Login/Textures/T_LoginBG_InkWash` 存在且为 Texture2D | `InkWashLoginTheme.BackgroundTexturePath` |
| Widget Blueprint 存在 | `/Game/UI/WBP_InkWashLogin` 存在 | - |
| Widget 父类正确 | 父类名称含 "InkWashLoginWidget" | - |
| Widget 生成类可加载 | `/Game/UI/WBP_InkWashLogin.WBP_InkWashLogin_C` 可通过 `load_class` 加载 | `LoginScreenManager.InkWashWidgetClassPath` |
| 关卡 Actor 配置 | Start 关卡有 LoginScreenManager Actor 且 `UseInkWashDesign = true` | `LoginScreenManager.UseInkWashDesign` |
| C# 类已注册 | InkWashLoginWidget_C / InkWashLoginTheme_C / LoginScreenManager_C 均可加载 | - |

## 实施方案

### 步骤 1 — 端到端资产验证

**目标**：通过 MCP `execute_python_code` 执行一次性验证脚本，检查所有资产路径、类型、父类、属性配置。

**MCP 执行策略**：执行 Python 脚本，使用 `unreal.EditorAssetLibrary`、`unreal.load_class`、`unreal.load_object` 等只读 API 验证。

**验证脚本逻辑**：
1. 检查纹理：`does_asset_exist("/Game/UI/Login/Textures/T_LoginBG_InkWash")` → True，加载后确认类型为 `Texture2D`
2. 检查 Widget BP：`does_asset_exist("/Game/UI/WBP_InkWashLogin")` → True
3. 检查 Widget 父类：加载 Blueprint 资产，读取 `parent_class` 属性，确认名称含 "InkWashLoginWidget"
4. 检查生成类：`load_class(None, "/Game/UI/WBP_InkWashLogin.WBP_InkWashLogin_C")` 不为 None
5. 检查 C# 类：`load_class(None, "/Script/UnrealSharp.InkWashLoginWidget_C")` / `InkWashLoginTheme_C` / `LoginScreenManager_C` 均不为 None
6. 检查关卡 Actor：加载 `/Game/Maps/Start`，遍历 Actor 找到 LoginScreenManager，读取 `UseInkWashDesign` 确认为 True
7. 路径一致性：对比 C# 代码中的路径常量与实际资产路径

**输出**：结构化验证报告（每项 PASS / FAIL + 详情），便于诊断问题。

### 步骤 2 — 问题修复（如有）

如果步骤 1 中任何验证点为 FAIL：
- 根据失败原因重新执行对应任务的 MCP 脚本
- 常见问题及修复策略：
  - 纹理路径不匹配 → 重新导入或调整 C# 常量
  - Widget 父类未设置 → 重新设置 `parent_class` 并编译
  - 关卡 Actor 属性未保存 → 重新设置并多策略保存
  - C# 类未注册 → 提示用户重新编译 C# 项目

### 步骤 3 — PIE 测试与截图

**目标**：在 PIE 模式下实际运行 Start 关卡，触发 LoginScreenManager 加载水墨登录 Widget，截图确认视觉效果。

**MCP 执行策略**：
1. 通过 Python 脚本设置 PIE 模式参数（`unreal.EditorLevelUtils` 或 `unreal.LevelEditorSubsystem`）
2. 启动 PIE：`unreal.EditorLevelLibrary` 或 `unreal.UnrealEditorSubsystem` 相关 API
3. 等待 Widget 加载和 WebBrowser 渲染 HTML（短暂延时）
4. 截取视口截图：`unreal.SystemLibrary.capture_window_to_texture` 或视口截图 API
5. 保存截图到项目目录
6. 退出 PIE

**回退方案**：如果 PIE 自动化 API 不可用或 WebBrowser 渲染需要较长时间，改为：
- 仅截取编辑器视口截图（非 PIE）
- 或提示用户手动 PIE 后通过 MCP 截图

### 步骤 4 — 生成落地完成报告

**目标**：汇总所有验证结果和截图，生成最终落地完成报告。

**报告内容**：
- 三项资产的最终路径和属性确认
- C# 代码路径常量与实际资产的一致性确认
- PIE 截图（如获取成功）
- 已知限制或后续优化建议（如 Mode B 原生 UMG 控件补充、响应式适配等）

## 假设与决策

1. **UE5 编辑器持续运行**：MCP 连接保持可用，编辑器未关闭
2. **前三项任务产出仍在**：上一轮创建的资产未被删除或修改
3. **PIE 可用**：项目可正常进入 PIE 模式（如不可用则跳过步骤 3，仅做资产验证）
4. **WebBrowser 渲染**：Mode A 依赖 WebBrowser 插件，如项目未启用则 Widget 显示空白（非致命，记录为已知限制）
5. **验证优先**：即使 PIE 截图不可用，资产验证通过即可确认落地任务完成

## 验证步骤

1. 执行步骤 1 验证脚本，确认所有 7 个验证点全部 PASS
2. 如有 FAIL，执行步骤 2 修复后重新验证
3. 执行步骤 3 PIE 截图（可选）
4. 生成步骤 4 完成报告
5. 最终确认：所有资产路径与 C# 常量一致，Start 关卡 UseInkWashDesign=true
