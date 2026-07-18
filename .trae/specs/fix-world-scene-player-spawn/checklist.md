# Checklist

- [x] WorldSceneInitializer 脚本创建，场景加载后 5 秒兜底生成角色
- [x] WorldSceneInitializer 角色生成后自动设置 ThirdPersonCamera.Target
- [x] HundunWorldGame.RequestCreateLocalPlayerActor 增加超时兜底（10 秒）
- [x] CharacterManager.EnterGameAsync 场景切换后检查角色是否生成
- [x] 兜底生成使用本地缓存的角色数据
- [x] 所有兜底路径输出警告/信息日志
- [x] `dotnet build` 编译通过，0 错误 0 警告