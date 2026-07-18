# 水墨登录界面 UE5 MCP 资产落地计划

## 摘要

通过 UE5 MCP 的 `execute_python_code` 工具，在 UE5 编辑器中完成水墨登录界面的三项资产落地任务：导入水墨背景纹理、创建 WBP_InkWashLogin Widget Blueprint（父类 InkWashLoginWidget_C）、配置 Start 关卡中 LoginScreenManager Actor 的 UseInkWashDesign=true。

## 当前状态分析

### 前置条件已确认

| 项目 | 状态 | 路径 |
|------|------|------|
| InkWashLoginWidget_C 类 | 已编译注册 | `/Script/UnrealSharp.InkWashLoginWidget_C` |
| InkWashLoginTheme_C 类 | 已编译注册 | `/Script/UnrealSharp.InkWashLoginTheme_C` |
| LoginScreenManager_C 类 | 已编译注册 | `/Script/UnrealSharp.LoginScreenManager_C` |
| 水墨背景图源文件 | 存在 | `SourceAssets/InkWashLogin/Textures/T_LoginBG_InkWash.jpg` |
| WebUI HTML | 已部署 | `WebUI/InkWashLogin/login.html` |
| Start 地图 | 存在 | `/Game/Maps/Start` |
| WBP_LoginScreen | 存在（父类 UserWidget） | `/Game/UI/WBP_LoginScreen` |
| `/Game/UI/Login` 目录 | 空（待创建） | - |
| UE5 MCP | 可用 | `execute_python_code` 可执行 unreal Python |
| UE5 编辑器 | 运行中 | 项目路径确认正确 |

### C# 代码中的关键路径常量（必须与资产路径精确匹配）

| C# 常量 | 值 | 对应任务 |
|---------|-----|---------|
| `InkWashLoginTheme.BackgroundTexturePath` | `/Game/UI/Login/Textures/T_LoginBG_InkWash.T_LoginBG_InkWash` | 任务 A |
| `LoginScreenManager.InkWashWidgetClassPath` | `/Game/UI/WBP_InkWashLogin.WBP_InkWashLogin_C` | 任务 B |

## 实施方案

### 任务 A — 导入水墨背景图为 Texture2D

**目标**：将 `SourceAssets/InkWashLogin/Textures/T_LoginBG_InkWash.jpg` 导入为 Texture2D，目标路径 `/Game/UI/Login/Textures/T_LoginBG_InkWash`。

**MCP 执行策略**：通过 `execute_python_code` 执行 Python 脚本，使用 `unreal.AssetImportTask` + `unreal.AssetToolsHelpers.get_asset_tools()` 导入纹理。

**关键步骤**：
1. 构建源文件绝对路径（`unreal.Paths.get_project_directory()` + 相对路径）
2. 创建目标目录 `/Game/UI/Login/Textures`（`unreal.EditorAssetLibrary.make_directory`）
3. 创建 `AssetImportTask`，设置 filename / destination_path / destination_name / replace_existing / automated / save
4. 尝试设置 `TextureFactory`（非致命，引擎自动识别 jpg）
5. 执行 `asset_tools.import_asset_tasks([import_task])`
6. 加载导入后的纹理，设置 `texture_group = TEXTUREGROUP_UI`、`compression_settings = TC_Default`、`srgb = True`
7. 保存资产

**验证**：`unreal.EditorAssetLibrary.does_asset_exist("/Game/UI/Login/Textures/T_LoginBG_InkWash")` 返回 True，且资产类型为 Texture2D。

### 任务 B — 创建 WBP_InkWashLogin Widget Blueprint

**目标**：创建 Widget Blueprint `WBP_InkWashLogin`，父类设为 `InkWashLoginWidget_C`，保存到 `/Game/UI/`。WidgetTree 保持空白（模式 A：WebBrowser 保真渲染）。

**MCP 执行策略**：通过 `execute_python_code` 执行 Python 脚本，使用 `unreal.WidgetBlueprintFactory` + `asset_tools.create_asset` 创建蓝图。

**关键步骤**：
1. 加载父类 `InkWashLoginWidget_C`（路径 `/Script/UnrealSharp.InkWashLoginWidget_C`，已验证可加载）
2. 删除已存在的同名资产（如有）
3. 创建 `WidgetBlueprintFactory`，设置 `parent_class = parent_class_obj`
4. 调用 `asset_tools.create_asset("WBP_InkWashLogin", "/Game/UI", unreal.WidgetBlueprint, factory)`
5. 验证 `blueprint.get_editor_property("parent_class")` 名称含 "InkWashLoginWidget"
6. WidgetTree 保持空白（不添加控件，C# 的 `TryInitWebBrowserMode()` 会自动创建 WebBrowser）
7. 编译蓝图 `unreal.BlueprintEditorLibrary.compile_blueprint(blueprint)`
8. 保存资产

**回退方案**：如 `WidgetBlueprintFactory` 无法直接设置 C# 父类，先创建默认 `UserWidget` 父类蓝图，再手动 `blueprint.set_editor_property("parent_class", parent_class)` 后重新编译。

**验证**：`does_asset_exist("/Game/UI/WBP_InkWashLogin")` 返回 True；父类名称含 "InkWashLoginWidget"；`load_class(None, "/Game/UI/WBP_InkWashLogin.WBP_InkWashLogin_C")` 可加载。

### 任务 C — 配置 Start 关卡中 LoginScreenManager Actor

**目标**：加载 `/Game/Maps/Start` 关卡，确认存在 `LoginScreenManager` Actor，设置 `UseInkWashDesign = true`，保存关卡。

**MCP 执行策略**：通过 `execute_python_code` 执行 Python 脚本，使用 `unreal.EditorLevelLibrary.load_level` 加载关卡，`unreal.EditorActorSubsystem.get_all_level_actors()` 遍历 Actor。

**关键步骤**：
1. 加载 Start 关卡 `unreal.EditorLevelLibrary.load_level("/Game/Maps/Start")`
2. 获取编辑器 World `unreal.get_editor_subsystem(unreal.UnrealEditorSubsystem).get_editor_world()`
3. 获取所有关卡 Actor `unreal.get_editor_subsystem(unreal.EditorActorSubsystem).get_all_level_actors()`
4. 查找类名含 "LoginScreenManager" 的 Actor
5. 如未找到，加载 `LoginScreenManager_C` 类（`unreal.load_class(None, "/Script/UnrealSharp.LoginScreenManager_C")`）并 `spawn_actor_from_class`
6. 设置 `UseInkWashDesign = true`（尝试 PascalCase `UseInkWashDesign` 和 snake_case `use_ink_wash_design`）
7. 标记 Actor 为 dirty（`actor.modify(True)`）
8. 保存关卡（`unreal.EditorLoadingAndSavingUtils.save_dirty_packages(True, True)`）

**注意**：C# 默认值 `_useInkWashDesign = true`，新生成的 Actor 自动为 true；如已存在的 Actor 已为 true 则跳过。

**验证**：重新加载 Start 关卡，遍历 Actor 确认 LoginScreenManager 存在且 `UseInkWashDesign = true`。

### 执行顺序

A → B → C（顺序执行，每步验证后再进行下一步）

## 端到端验证

所有任务完成后，执行一次性验证脚本检查：
- `/Game/UI/Login/Textures/T_LoginBG_InkWash` 存在且为 Texture2D
- `/Game/UI/WBP_InkWashLogin` 存在，父类含 "InkWashLoginWidget"
- `/Game/UI/WBP_InkWashLogin.WBP_InkWashLogin_C` 可加载（C# 引用的路径）
- `/Game/Maps/Start` 中 LoginScreenManager Actor 存在，UseInkWashDesign = true
- 路径一致性：C# 常量引用的所有路径均有对应资产

## 假设与决策

1. **MCP 可用性**：UE5 编辑器正在运行，MCP `execute_python_code` 已验证可执行 Python 脚本
2. **C# 类已编译**：InkWashLoginWidget_C / InkWashLoginTheme_C / LoginScreenManager_C 均已注册到 `/Script/UnrealSharp` 模块
3. **模式 A（WebBrowser）为默认**：WidgetTree 保持空白，C# 自动创建 WebBrowser 加载 HTML，实现 100% 设计保真
4. **UnrealSharp 类加载路径**：已验证 `/Script/UnrealSharp.InkWashLoginWidget_C` 可通过 `unreal.load_class` 加载
5. **属性名兼容**：C# `[UProperty]` 在 Python 中可能为 PascalCase 或 snake_case，代码尝试两种变体
6. **关卡保存策略**：使用多策略保存（save_dirty_packages / save_all_dirty_levels / save_current_level）确保保存成功
