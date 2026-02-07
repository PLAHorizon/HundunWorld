# 简化版网络系统说明

## 概述

本网络系统经过简化和重构，遵循职责单一原则，每个组件都有明确的职责：

1. **NetworkManager** - 核心网络管理器，负责连接管理、消息收发
2. **GatewayReconnectionPlugin** - 网关重连插件，处理自动重连逻辑
3. **NetworkStateMonitor** - 网络状态监控器，检测网络连接状态变化
4. **GatewaySelector** - 网关选择器，选择最佳网关服务器
5. **NetworkAvailabilityChecker** - 网络可用性检查器，检查网关可用性
6. **HeartbeatManager** - 心跳包管理器，负责发送心跳包

## 主要改进

1. **简化代码结构** - 移除了不必要的复杂性，使代码更易理解和维护
2. **职责单一原则** - 每个类都有明确的职责，避免功能混杂
3. **增强日志输出** - 使用增强日志系统确保日志完整输出
4. **改进重连机制** - 简化重连流程，提高重连成功率
5. **心跳包管理** - 独立的心跳包管理器确保连接保持

## 使用方法

### 初始化网络管理器

```csharp
var gatewayList = new List<GatewayInfo>
{
    new GatewayInfo { IP = "127.0.0.1", Port = 7789, Region = "本地开发网关" }
};

var networkManager = new NetworkManager(gatewayList);
```

### 订阅事件

```csharp
networkManager.ConnectionStatusChanged += OnConnectionStatusChanged;
networkManager.MessageReceived += OnMessageReceived;
networkManager.ConnectionError += OnConnectionError;
```

### 发送消息

```csharp
bool success = await networkManager.SendMessageAsync(message);
```

### 手动重连

```csharp
bool success = await networkManager.ManualReconnectAsync();
```

## 测试

使用 `SimpleNetworkTestScript` 或 `HeartbeatTestScript` 进行测试：

- F9: 开始连接测试
- F10: 断开连接
- F11: 重新创建网络管理器
- F12: 检查网络状态

## 诊断

使用 `DiagnoseNetworkStatusAsync()` 方法获取详细的网络状态信息。