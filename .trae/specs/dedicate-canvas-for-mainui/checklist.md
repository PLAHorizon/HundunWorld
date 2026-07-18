# Checklist

## Task 1: FindOrCreateInkUICanvas 按名称精确查找
- [x] 方式 1（Actor 自身）仅在 `Actor.Name == "InkWashUICanvas"` 时返回 UICanvas
- [x] 方式 2（Actor 子级）遍历 `Actor.Children`，仅返回名为 `InkWashUICanvas` 的子 Actor 上的 UICanvas
- [x] 方式 3（父 Actor）仅在 `Actor.Parent.Name == "InkWashUICanvas"` 时返回 UICanvas
- [x] 方式 4（场景查找）同时按 `Name == "InkWashUICanvas"` 和 `Scene == Actor.Scene` 过滤
- [x] 方式 5（Level 全局查找）同时按 `Name == "InkWashUICanvas"` 和 `Scene == Actor.Scene` 过滤，无「用第一个」兜底
- [x] 方式 6（新建）`canvasActor.Name = "InkWashUICanvas"`、`canvasActor.Parent = Actor`、`uiCanvas.Name = "InkWashUICanvas"`
- [x] 所有查找分支命中时输出 Debug 日志
- [x] 方法中无任何 `GetScript<UICanvas>()` / `GetChild<UICanvas>()` 无名称过滤的调用

## Task 2: 验证与回归
- [x] `dotnet build HundunWorld\HundunWorld.sln -c Editor.Development -p:Platform=Win64 -t:Build` 0 错误（96 警告均为预存在的 XML 注释警告）
- [x] 静态检查：方法中无 `Actor.GetScript<UICanvas>()` 通用调用
- [x] 静态检查：方法中无 `Actor.GetChild<UICanvas>()` 通用调用
- [x] 静态检查：方法中无 `Actor.Parent.GetScript<UICanvas>()` 通用调用
- [x] 静态检查：方法中无 `Actor.Parent.GetChild<UICanvas>()` 通用调用
- [x] 静态检查：新建分支使用 `canvasActor.Parent = Actor` 而非 `Level.SpawnActor`
- [x] 静态检查：所有查找分支均使用 `Name == "InkWashUICanvas"` 作为过滤条件

## 整体行为验证
- [ ] 首次进入 GameWorld：新建 `InkWashUICanvas` Actor 挂在 `MainUIManager.Actor` 下
- [ ] 第二次进入 GameWorld（已存在 InkWashUICanvas）：复用，不新建
- [ ] RootScene 中存在 `MainUICanvas` / `GameMainUICanvas` 时：主 UI 不复用它们，依然使用独立的 `InkWashUICanvas`
- [ ] 子场景卸载时：`InkWashUICanvas` 随 `MainUIManager.Actor` 保留在 RootScene 中，不被销毁
