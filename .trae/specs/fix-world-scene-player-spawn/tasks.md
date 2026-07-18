# Tasks

- [x] Task 1: 创建 `WorldSceneInitializer` 脚本
  - 新建文件 `c:\Works\GitHubProjects\HundunWorld\HundunWorld\Source\Game\WorldSceneInitializer.cs`
  - 继承 `Script`，挂载到 World 场景的 ScriptsActor 或新建 Actor
  - `OnStart()`：记录场景加载完成时间，检查 `HundunWorldGame.Instance` 是否有待生成的本地玩家请求
  - `OnUpdate()`：每秒检查一次，如果场景加载后 5 秒内仍未生成角色，调用 `HundunWorldGame.Instance.CreateLocalPlayerActor()` 兜底生成
  - 角色生成后自动查找场景中的 `ThirdPersonCamera`，设置其 Target 为生成的角色 Actor
  - 生成成功后输出日志，自身禁用（不再更新）
  - **验证**: 编译通过

- [x] Task 2: 修改 `HundunWorldGame.RequestCreateLocalPlayerActor` 增加超时兜底
  - 读取 `c:\Works\GitHubProjects\HundunWorld\HundunWorld\Source\Game\HundunWorldGame.cs` 中 `RequestCreateLocalPlayerActor` 方法
  - 增加超时逻辑：订阅 `TransitionCompleted` 后，如果超过 10 秒仍未生成角色，使用本地缓存的角色数据调用 `CreateLocalPlayerActor()` 兜底
  - 输出警告日志说明触发了超时兜底
  - **验证**: 编译通过

- [x] Task 3: 修改 `CharacterManager.EnterGameAsync` 增加场景切换后检查
  - 读取 `c:\Works\GitHubProjects\HundunWorld\HundunWorld\Source\Game\UI\Character\CharacterManager.cs` 中 `EnterGameAsync` 方法
  - 在 `GameSceneManager.TransitionTo` 调用后，订阅 `TransitionCompleted` 事件
  - 场景切换完成后，检查本地玩家 Actor 是否已生成，如果未生成则等待服务器响应
  - 输出日志说明场景切换完成，等待角色生成
  - **验证**: 编译通过

- [x] Task 4: 编译验证
  - `dotnet build` 编译通过
  - 0 错误 0 警告

## Task Dependencies
- [Task 4] depends on [Task 1, Task 2, Task 3]