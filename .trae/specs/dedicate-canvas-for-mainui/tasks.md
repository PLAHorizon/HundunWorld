# Tasks

- [x] Task 1: 重写 FindOrCreateInkUICanvas 查找逻辑为按名称精确匹配
  - [x] SubTask 1.1: 修改方式 1（Actor 自身查找）：从 `Actor.GetScript<UICanvas>()` 改为检查 `Actor.Name == "InkWashUICanvas"` 时才返回 `Actor.GetScript<UICanvas>()`，否则继续后续查找
  - [x] SubTask 1.2: 修改方式 2（Actor 子级查找）：从 `Actor.GetChild<UICanvas>()` 改为遍历 `Actor.Children`，仅返回名为 `InkWashUICanvas` 的子 Actor 上的 UICanvas
  - [x] SubTask 1.3: 修改方式 3（父 Actor 查找）：从 `Actor.Parent.GetScript<UICanvas>()` / `Actor.Parent.GetChild<UICanvas>()` 改为检查 `Actor.Parent.Name == "InkWashUICanvas"` 时才返回其 UICanvas
  - [x] SubTask 1.4: 修改方式 4（场景查找）：从 `Level.GetActors<UICanvas>()` + `Scene == Actor.Scene` 过滤，改为同时要求 `Name == "InkWashUICanvas"` 和 `Scene == Actor.Scene`
  - [x] SubTask 1.5: 修改方式 5（Level 全局查找）：同 SubTask 1.4，按 `Name == "InkWashUICanvas"` 和 `Scene == Actor.Scene` 过滤，移除「用第一个」兜底
  - [x] SubTask 1.6: 方式 6（新建）保持不变：`canvasActor.Name = "InkWashUICanvas"`、`canvasActor.Parent = Actor`、`uiCanvas.Name = "InkWashUICanvas"`
  - [x] SubTask 1.7: 在每个查找分支命中时输出 Debug 日志，包含 Canvas Name 与所在 Actor 路径，便于排查

- [x] Task 2: 验证与回归检查
  - [x] SubTask 2.1: 编译解决方案 `dotnet build HundunWorld\HundunWorld.sln -c Editor.Development -p:Platform=Win64 -t:Build`，确保 0 错误（96 警告均为预存在的 XML 注释警告）
  - [x] SubTask 2.2: 静态检查 `FindOrCreateInkUICanvas` 方法中无任何 `GetScript<UICanvas>()` / `GetChild<UICanvas>()` 无名称过滤的调用
  - [x] SubTask 2.3: 静态检查新建分支使用 `canvasActor.Parent = Actor` 而非 `Level.SpawnActor`
  - [x] SubTask 2.4: 静态检查所有查找分支均使用 `Name == "InkWashUICanvas"` 作为过滤条件

# Task Dependencies
- Task 2 依赖 Task 1 完成

# Parallelizable Work
- 无并行任务，Task 1 必须串行

# 实现备注
- Flax `Actor.GetChild<T>()` 无带名称重载，需通过 `Actor.Children` 遍历按 Name 过滤后再 `GetScript<UICanvas>()`
- 查找时同时校验 `Scene == Actor.Scene`，避免命中子场景中的同名 Canvas（虽然子场景不应有 InkWashUICanvas，但防御性校验）
- 不修改其他 UI 的 Canvas 查找逻辑（AuthenticationUI / GameMainUI），本 spec 仅解决主 UI 的隔离问题
