# 混沌世界游戏项目

## 项目概述

这是一个支持百万级并发在线用户的统一游戏世界原型项目，基于FlaxEngine 1.10开发，使用C#技术栈，结合TouchSocket + MemoryPacket作为网络通信基础，并利用Arch的ECS系统解耦客户端所有操作以提升性能。

## 项目结构

```
HundunWorld/Source/Game/
├── ECS/                    # ECS系统实现
│   ├── Components/         # 组件定义
│   │   ├── PositionComponent.cs
│   │   ├── VelocityComponent.cs
│   │   ├── HealthComponent.cs
│   │   ├── CameraComponent.cs
│   │   ├── CharacterControllerComponent.cs
│   │   └── InputComponent.cs
│   ├── Systems/            # 系统实现
│   │   ├── MovementSystem.cs
│   │   ├── RenderingSystem.cs
│   │   ├── HealthSystem.cs
│   │   ├── CameraSystem.cs
│   │   ├── CharacterControllerSystem.cs
│   │   └── InputSystem.cs
│   └── ECSManager.cs       # ECS管理器
├── Modules/                # 模块管理
│   ├── IGameModule.cs      # 模块接口
│   ├── BaseModule.cs       # 基础模块类
│   ├── ModuleManager.cs    # 模块管理器
│   └── SampleGameModule.cs # 示例模块
├── Network/                # 网络通信
│   ├── Messages/           # 消息定义
│   │   └── SampleMessages.cs
│   ├── NetworkManager.cs   # 网络管理器
│   ├── MessageSerializer.cs # 消息序列化器
│   ├── GatewayConnector.cs # 网关连接器
│   ├── SmartGatewayConnector.cs # 智能网关连接器
│   ├── GatewaySelector.cs  # 网关选择器
│   ├── GatewayInfo.cs      # 网关信息
│   ├── ReconnectConfig.cs  # 重连配置
│   ├── Example/            # 使用示例
│   │   └── NetworkExample.cs # 网络功能示例
│   └── Tests/              # 网络测试
│       ├── ReconnectConfigTests.cs
│       ├── GatewaySelectorTests.cs
│       └── GatewayConnectorIntegrationTests.cs
├── Worlds/                 # 世界管理
│   ├── WorldManager.cs     # 世界管理器
│   ├── WorldState.cs       # 世界状态
│   ├── PlayerPositionUpdater.cs # 玩家位置更新器
│   ├── EventBroadcaster.cs # 事件广播器
│   └── WorldDataManager.cs # 世界数据管理器
├── ThirdPersonCamera.cs    # 第三人称相机脚本
├── CharacterController.cs  # 角色控制器脚本
├── GameSceneInitializer.cs # 游戏场景初始化脚本
├── TestCameraShake.cs      # 测试相机抖动脚本
├── HundunWorldGame.cs      # 游戏主类
├── HundunWorldGameScript.cs # FlaxEngine脚本
├── Game.Build.cs           # 项目构建配置
├── ExitOnEsc.cs            # 退出脚本
└── FreeCamera.cs           # 自由摄像机（旧版）
```

## 核心功能

### 1. 网络通信模块
- 实现客户端与网关的基础连接
- 消息序列化与反序列化机制
- 断线重连功能（支持5分钟超时限制和指数退避策略）
- 智能网关选择机制（基于延迟和负载的最优网关选择）
- 基础消息收发功能
- 基于TouchSocket的完整消息处理流程

### 2. ECS系统集成（完全使用Arch ECS框架）
- 集成Arch ECS框架
- 基础组件定义（位置、速度、生命值、相机、角色控制器、输入等）
- 基础系统（移动系统、渲染系统、生命值系统、相机系统、角色控制器系统、输入系统等）
- 实体管理机制基于Arch ECS的Entity

### 3. 第三人称相机系统
- 借鉴剑侠情缘3和魔兽世界的相机特点
- 按住鼠标右键以角色为圆点转动相机
- 鼠标滚轮缩放视觉距离
- 45度俯视角默认跟随角色移动
- 支持相机抖动效果（物理效果带来的轻微抖动）

### 4. 角色控制器系统
- 支持鼠标左键点击地面行走
- 支持八方键移动和行走
- 支持跳跃功能
- 支持重力效果
- 支持相机空间移动（相对于相机方向移动）

### 5. 热插拔模块机制
- 模块加载与卸载功能
- 模块生命周期管理器
- 模块间通信接口
- 模块热重载机制

### 6. 统一世界功能
- 世界状态同步机制
- 玩家位置更新功能
- 游戏事件广播机制
- 基础的世界数据管理

## 使用方法

1. 确保已安装FlaxEngine 1.10
2. 确保所有依赖DLL文件已放置在Output/Windows目录中：
   - Arch.dll
   - TouchSocket.dll
   - TouchSocket.Core.dll
   - MemoryPack.Core.dll
   - Horizon.Game.Message.dll
   - K4os.Compression.LZ4.dll
3. 构建项目
4. 在FlaxEditor中打开项目并运行

## 扩展开发

### 添加新的ECS组件
1. 在`ECS/Components/`目录下创建新的组件结构体
2. 实现`IComponent`接口
3. 在系统中使用新组件

### 添加新的ECS系统
1. 在`ECS/Systems/`目录下创建新的系统类
2. 继承`BaseSystem`类
3. 实现`Update`和`Render`方法
4. 在`HundunWorldGameScript`中注册系统

### 添加新的游戏模块
1. 在`Modules/`目录下创建新的模块类
2. 继承`BaseModule`类
3. 实现模块的生命周期方法
4. 使用`ModuleManager`加载模块

### 发送网络消息
1. 在`Network/Messages/`目录下创建新的消息类
2. 继承`MessageUnion`并实现`INetworkMessage`接口
3. 使用`NetworkManager.SendMessageAsync`发送消息

### 配置重连参数
1. 创建`ReconnectConfig`实例
2. 设置重连参数（最大重连时间、重连策略等）
3. 在创建`GatewayConnector`或`SmartGatewayConnector`时传入配置

### 使用网关选择器
1. 创建`GatewaySelector`实例并传入网关列表
2. 调用`SelectBestGatewayAsync`方法选择最佳网关
3. 使用选择的网关进行连接

### 使用智能网关连接器
1. 创建`SmartGatewayConnector`实例并传入网关列表和重连配置
2. 调用`ConnectToBestGatewayAsync`方法连接到最佳网关
3. 智能网关连接器会自动处理断线重连和网关重新选择

### 使用第三人称相机系统
1. 在场景中添加相机Actor并附加`ThirdPersonCamera`脚本
2. 设置相机的目标角色
3. 调整相机参数（距离、角度等）
4. 相机支持鼠标右键控制和滚轮缩放

### 使用角色控制器系统
1. 在场景中添加角色Actor并附加`CharacterController`脚本
2. 设置角色的移动速度和跳跃力度
3. 关联相机引用以支持相机空间移动
4. 角色支持八方键移动和鼠标点击移动

## 依赖项

- FlaxEngine 1.10
- Arch ECS框架
- TouchSocket网络库
- MemoryPack序列化库
- Horizon.Game.Message消息库
- K4os.Compression.LZ4压缩库