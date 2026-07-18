# 网络同步配置操作指南

> 面向开发者与运维的"从零配置到可运行"操作手册。
>
> 本指南与以下既有文档互补：
> - `docs/NETCODE.md`：网络同步架构原理
> - `docs/NETWORK_PROTOCOL.md`：SyncPacket 协议规范
> - `docs/NETWORK_PERFORMANCE.md`：性能基线与调优
>
> 技术栈：.NET 10 C# + Orleans + SqlServer + Redis + EFCore + TouchSocket + MemoryPack + Arch ECS + UnrealSharp（UE5 C# 互操作）。

---

## 目录

1. [环境准备](#第-1-节环境准备)
2. [服务端配置](#第-2-节服务端配置)
3. [客户端配置](#第-3-节客户端配置)
4. [UE5 层配置](#第-4-节ue5-层配置)
5. [ECS 系统装配](#第-5-节ecs-系统装配)
6. [启动验证](#第-6-节启动验证)
7. [故障排查](#第-7-节故障排查)

---

## 第 1 节：环境准备

本节列出运行 HundunWorld 网络同步方案所需的全部依赖与验证方法。所有命令均针对 Windows PowerShell。

### 1.1 .NET 10 SDK

- **要求**：.NET 10 SDK（项目目标框架 `net10.0`）
- **安装**：从 https://dotnet.microsoft.com/download/dotnet/10.0 下载安装
- **验证**：

```powershell
dotnet --version
# 预期输出：10.x.x
```

### 1.2 SQL Server

- **要求**：SQL Server 2019 及以上（用于 Orleans 集群成员资格存储与业务数据库）
- **安装**：安装 SQL Server Developer / Express 版本，启用混合模式认证（sa 账号）
- **验证**：

```powershell
sqlcmd -S . -Q "SELECT @@VERSION"
# 预期输出：Microsoft SQL Server 2019 (RTM) ... 一行
```

### 1.3 Redis

- **要求**：Redis 6.x 及以上（主从 + 哨兵部署，端口与密码需与 `appsettings.json` 的 `DataBase` 区段一致）
- **安装**：Windows 下推荐使用 Memurai 或 WSL2 部署 Redis
- **验证**：

```powershell
redis-cli -h 127.0.0.1 -p 9379 -a DB65F7F9C ping
# 预期输出：PONG
```

> 默认主节点端口 `9379`、密码 `DB65F7F9C`（取自 `appsettings.json` 的 `DataBase.RedisMasters[0]`）。

### 1.4 UE5 编辑器

- **要求**：Unreal Engine 5.8（与 NarrativePro 插件兼容，项目使用 VibeUE 5.0 生成的 AI 配置）
- **安装**：通过 Epic Games Launcher 安装 UE 5.8，并确保项目插件（NarrativePro / NarrativeArsenal / UnrealSharp / VibeUE）已正确启用
- **验证**：打开 `HundunWorld.uproject`，编辑器能正常加载且无编译错误

---

## 第 2 节：服务端配置

服务端配置文件位于 `Horizon.Game.Gateway\appsettings.json`。下表按区段列出每个字段的键路径、类型、当前值（从实际文件提取）、说明与示例。

### 2.1 配置项总览

#### Network 区段（TouchSocket 网络监听）

| 键路径 | 类型 | 当前值 | 说明 |
| --- | --- | --- | --- |
| `Network.TcpPort` | int | `7789` | TCP 监听端口（客户端主连接端口） |
| `Network.UdpPort` | int | `8889` | UDP 监听端口 |
| `Network.WebSocketPort` | int | `8890` | WebSocket 监听端口 |
| `Network.IpAddress` | string | `192.168.1.78` | 监听 IP 地址（部署时改为实际网卡 IP） |
| `Network.NoDelay` | bool | `true` | 是否禁用 Nagle 算法（低延迟场景建议 true） |
| `Network.SendBufferSize` | int | `32768` | 发送缓冲区大小（字节） |
| `Network.ReceiveBufferSize` | int | `32768` | 接收缓冲区大小（字节） |
| `Network.Backlog` | int | `100` | 连接积压队列长度 |
| `Network.KeepAliveInterval` | int | `30000` | Keep-Alive 探测间隔（毫秒） |
| `Network.KeepAliveTimeout` | int | `5000` | Keep-Alive 超时（毫秒） |
| `Network.EnableSsl` | bool | `false` | 是否启用 SSL/TLS |
| `Network.SslCertificatePath` | string? | `null` | SSL 证书路径（EnableSsl=true 时必填） |
| `Network.SslCertificatePassword` | string? | `null` | SSL 证书密码 |

#### Gateway 区段（业务网关参数）

| 键路径 | 类型 | 当前值 | 说明 |
| --- | --- | --- | --- |
| `Gateway.Name` | string | `混沌世界游戏网关` | 网关显示名称 |
| `Gateway.GatewayId` | string | `TYMYD-Gateway-001` | 网关唯一 ID（需与客户端 `[GameGateway].InstanceId` 一致） |
| `Gateway.Region` | string | `华东` | 所属区域 |
| `Gateway.MaxConnections` | int | `10000` | 最大并发连接数 |
| `Gateway.ConnectionTimeout` | int | `300` | 连接超时（秒） |
| `Gateway.HeartbeatInterval` | int | `30` | 心跳间隔（秒） |
| `Gateway.EnableCompression` | bool | `true` | 是否启用消息压缩 |
| `Gateway.EnableEncryption` | bool | `true` | 是否启用加密 |
| `Gateway.BufferSize` | int | `8192` | 网关缓冲区大小（字节） |
| `Gateway.StatisticsInterval` | int | `60` | 统计上报间隔（秒） |
| `Gateway.EnableVerboseLogging` | bool | `false` | 是否启用详细日志 |
| `Gateway.UseSyncPacketDispatch` | bool | `true` | 是否启用 SyncPacket 分派（`GatewaySyncDispatcher`） |

#### Orleans 区段（Orleans 客户端连接）

| 键路径 | 类型 | 当前值 | 说明 |
| --- | --- | --- | --- |
| `Orleans.ClusterId` | string | `dev` | 集群 ID（须与 Silo 端一致） |
| `Orleans.ServiceId` | string | `BaseService` | 服务 ID |
| `Orleans.SiloEndpoints` | string[] | `["192.168.1.78:11111"]` | Silo 网关端点列表 |
| `Orleans.GatewayPort` | int | `30000` | Orleans Gateway 端口 |
| `Orleans.RetryCount` | int | `0` | 连接重试次数（0 表示无限重试） |
| `Orleans.RetryInterval` | int | `1000` | 重试间隔（毫秒） |
| `Orleans.ResponseTimeout` | int | `30000` | 响应超时（毫秒） |
| `Orleans.EnableStatistics` | bool | `true` | 是否启用统计 |
| `Orleans.EnablePerformanceCounters` | bool | `true` | 是否启用性能计数器 |

#### ClusterOptions 区段（Orleans 集群选项，须与 Silo 端一致）

| 键路径 | 类型 | 当前值 | 说明 |
| --- | --- | --- | --- |
| `ClusterOptions.ClusterId` | string | `dev` | 集群 ID |
| `ClusterOptions.ServiceId` | string | `BaseService` | 服务 ID |

#### DataBase 区段（Redis 主从/哨兵）

| 键路径 | 类型 | 当前值 | 说明 |
| --- | --- | --- | --- |
| `DataBase.RedisMasters[0].Host` | string | `127.0.0.1` | Redis 主节点主机 |
| `DataBase.RedisMasters[0].Port` | string | `9379` | Redis 主节点端口 |
| `DataBase.RedisMasters[0].Password` | string | `DB65F7F9C` | Redis 主节点密码 |
| `DataBase.RedisSlaves[*].Host` | string | `127.0.0.1` | Redis 从节点主机 |
| `DataBase.RedisSlaves[*].Port` | string | `9679` / `9779` / `9879` | Redis 从节点端口 |
| `DataBase.RedisSlaves[*].Password` | string | `DB65F7F9C` | Redis 从节点密码 |
| `DataBase.RedisSentinels[*].Host` | string | `127.0.0.1` | 哨兵主机 |
| `DataBase.RedisSentinels[*].Port` | string | `6379` | 哨兵端口 |

> 部署时需将 `Host` 改为实际 Redis 服务器地址，`Password` 改为生产环境强密码。

#### ClusteringSiloOptions 区段（Silo 集群持久化）

| 键路径 | 类型 | 当前值 | 说明 |
| --- | --- | --- | --- |
| `ClusteringSiloOptions.OrleansSiloHost` | string | `192.168.1.78` | Silo 主机地址 |
| `ClusteringSiloOptions.SqlServer.ConnectionString` | string | `Data Source=.;Initial Catalog=Orleans;...` | SQL Server 连接串（Orleans 集群表） |
| `ClusteringSiloOptions.SqlServer.Invariant` | string | `Microsoft.Data.SqlClient` | ADO.NET 驱动不变名 |
| `ClusteringSiloOptions.Npgsql.ConnectionString` | string | `User Id=postgres;...Database=Orleans;...` | PostgreSQL 连接串（备选） |
| `ClusteringSiloOptions.Npgsql.Invariant` | string | `Npgsql` | Npgsql 驱动不变名 |
| `ClusteringSiloOptions.Mysql.ConnectionString` | string | `""` | MySQL 连接串（当前为空，未启用） |
| `ClusteringSiloOptions.Mysql.Invariant` | string | `MySql.Data.MySqlClient` | MySQL 驱动不变名 |
| `ClusteringSiloOptions.Oracle.ConnectionString` | string | `""` | Oracle 连接串（当前为空，未启用） |
| `ClusteringSiloOptions.Oracle.Invariant` | string | `Oracle.ManagedDataAccess.Client` | Oracle 驱动不变名 |

#### DatabaseOptions 区段（业务数据库，Type=0 表示 SqlServer）

| 键路径 | 类型 | 当前值 | 说明 |
| --- | --- | --- | --- |
| `DatabaseOptions.Basic.ConnectionString` | string | `Data Source=.;Initial Catalog=Basic;...` | 基础库连接串 |
| `DatabaseOptions.Basic.Type` | int | `0` | 数据库类型（0=SqlServer） |
| `DatabaseOptions.Game.ConnectionString` | string | `Data Source=.;Initial Catalog=Game;...` | 游戏库连接串 |
| `DatabaseOptions.Game.Type` | int | `0` | 数据库类型 |
| `DatabaseOptions.Article.ConnectionString` | string | `Data Source=.;Initial Catalog=Basic;...` | 物品库连接串 |
| `DatabaseOptions.Article.Type` | int | `0` | 数据库类型 |
| `DatabaseOptions.Support.ConnectionString` | string | `Data Source=.;Initial Catalog=Basic;...` | 客服库连接串 |
| `DatabaseOptions.Support.Type` | int | `0` | 数据库类型 |
| `DatabaseOptions.Xingguang.ConnectionString` | string | `Data Source=.;Initial Catalog=Basic;...` | 星光库连接串 |
| `DatabaseOptions.Xingguang.Type` | int | `0` | 数据库类型 |

#### DashboardOptions 区段（Orleans Dashboard）

| 键路径 | 类型 | 当前值 | 说明 |
| --- | --- | --- | --- |
| `DashboardOptions.Host` | string | `192.168.1.78` | Dashboard 监听主机 |
| `DashboardOptions.Port` | int | `1199` | Dashboard 端口 |
| `DashboardOptions.Username` | string | `Horizon` | 登录用户名 |
| `DashboardOptions.Password` | string | `351055536577273` | 登录密码 |

#### Security 区段（安全策略）

| 键路径 | 类型 | 当前值 | 说明 |
| --- | --- | --- | --- |
| `Security.MaxLoginAttempts` | int | `5` | 最大登录尝试次数 |
| `Security.LoginAttemptsWindowMinutes` | int | `15` | 登录尝试窗口（分钟） |
| `Security.SessionTimeoutHours` | int | `24` | 会话超时（小时） |
| `Security.RequiredClientVersion` | string | `1.0.0` | 客户端最低版本 |
| `Security.EnableAccountValidation` | bool | `true` | 是否启用账号校验 |
| `Security.EnableIPWhitelist` | bool | `false` | 是否启用 IP 白名单 |
| `Security.EnableRateLimiting` | bool | `true` | 是否启用限流 |
| `Security.MaxRequestsPerMinute` | int | `60` | 每分钟最大请求数 |

#### Authentication 区段（鉴权策略）

| 键路径 | 类型 | 当前值 | 说明 |
| --- | --- | --- | --- |
| `Authentication.TokenExpirationMinutes` | int | `1440` | Access Token 有效期（分钟，1440=24h） |
| `Authentication.RefreshTokenExpirationHours` | int | `168` | Refresh Token 有效期（小时，168=7d） |
| `Authentication.EnableMultipleDeviceLogin` | bool | `true` | 是否允许多设备登录 |
| `Authentication.MaxConcurrentSessions` | int | `3` | 最大并发会话数 |

### 2.2 SQL 建库建表脚本执行步骤

`scripts/sql/` 目录下的脚本需按编号顺序执行。先在 SQL Server 中创建对应数据库（`Orleans`、`Basic`、`Game`），再依次执行：

| 顺序 | 脚本文件 | 用途 |
| --- | --- | --- |
| 1 | `scripts/sql/001_world_state.sql` | 世界状态表（Orleans 集群成员资格 + 业务世界状态） |
| 2 | `scripts/sql/002_flower_ai_stored_procedures.sql` | AI 花相关存储过程 |
| 3 | `scripts/sql/003_fix_orleans_storage_pk_violation.sql` | 修复 Orleans 存储 PK 冲突 |
| 4 | `scripts/sql/004_scene_object_state.sql` | 场景对象状态表（`SceneObjectSyncPacket` 持久化） |

执行命令（PowerShell）：

```powershell
# 1. 创建数据库（首次部署）
sqlcmd -S . -Q "CREATE DATABASE Orleans; CREATE DATABASE Basic; CREATE DATABASE Game;"

# 2. 按顺序执行脚本
sqlcmd -S . -d Orleans -i "scripts\sql\001_world_state.sql"
sqlcmd -S . -d Game    -i "scripts\sql\002_flower_ai_stored_procedures.sql"
sqlcmd -S . -d Orleans -i "scripts\sql\003_fix_orleans_storage_pk_violation.sql"
sqlcmd -S . -d Game    -i "scripts\sql\004_scene_object_state.sql"
```

> 注：脚本归属数据库需根据脚本内 `USE` 语句判断；若脚本未指定 `USE`，请按上表 `-d` 参数指定目标库。Orleans 集群表必须建在 `Orleans` 库中，与 `ClusteringSiloOptions.SqlServer.ConnectionString` 的 `Initial Catalog` 保持一致。

### 2.3 Redis 部署要点

- **主从复制**：部署 1 主（端口 `9379`）+ 多从（`9679` / `9779` / `9879`），从节点配置 `replicaof <master_host> 9379` 并设置 `masterauth DB65F7F9C`。
- **哨兵**：部署至少 1 个哨兵进程（端口 `6379`），监控主节点；哨兵配置 `sentinel monitor mymaster <master_host> 9379 1` 与 `sentinel auth-pass mymaster DB65F7F9C`。
- **密码**：所有节点设置 `requirepass DB65F7F9C`，生产环境务必替换为强密码，并同步更新 `appsettings.json` 的 `DataBase.RedisMasters[*].Password` 等字段。

### 2.4 服务端启动命令

```powershell
dotnet run --project Horizon.Game.Gateway
```

预期输出（关键日志行）：

```
[... INF] 网关已启动: GatewayId=TYMYD-Gateway-001, TcpPort=7789
[... INF] Gateway started, listening on 192.168.1.78:7789
[... INF] Orleans client connected to cluster dev
```

验证 Dashboard 可访问：浏览器打开 `http://192.168.1.78:1199`，使用 `Horizon` / `351055536577273` 登录，应能看到 Silo 成员与 Grain 统计。

---

## 第 3 节：客户端配置

客户端配置文件位于 `HundunWorld\Config\HorizonGame.ini`，由耕地（GengDi）启动器通过 `GameModelIniWriter` 自动生成。下表按区段列出字段、类型、校验规则、当前值与说明。

### 3.1 配置项总览

#### [Game] 区段

| 名称 | 类型 | 校验规则 | 当前值 | 说明 |
| --- | --- | --- | --- | --- |
| `GameId` | int | 必须为正整数（>0），否则 `MissingRequiredField` | `1001` | 游戏 ID |
| `AppType` | int | 可选，默认 369 | `369` | 应用类型标识 |
| `AreaId` | int | 必须为正整数（>0），否则 `InvalidFieldFormat` | `1` | 大区 ID |
| `ServerId` | int | 必须为正整数（>0），否则 `InvalidFieldFormat` | `1` | 服务器 ID |
| `Name` | string | 可选 | `Horizon Adventure` | 游戏名称 |
| `Version` | string | 可选 | `1.0.0` | 客户端版本（须 ≥ `Security.RequiredClientVersion`） |
| `InstallationPath` | string | 可选 | `C:\ProgramData\Horizon Adventure` | 安装路径 |

#### [User] 区段

| 名称 | 类型 | 校验规则 | 当前值 | 说明 |
| --- | --- | --- | --- | --- |
| `PassportId` | string | 不能为空，否则 `MissingRequiredField` | `1010776` | 通行证 ID |
| `UserId` | long | 必须为正整数（>0），否则 `MissingRequiredField` | `96` | 用户 ID（服务端用于路由 input 到对应 grain） |

#### [Auth] 区段

| 名称 | 类型 | 校验规则 | 当前值 | 说明 |
| --- | --- | --- | --- | --- |
| `AuthToken` | string | 不能为空（`MissingRequiredField`）；UTF-8 字节长度 ≥ 32，否则 `InvalidFieldFormat` | `zEbSBqiZCwql4ttt...IA==` | 鉴权 Token，注入到 `NetworkClient.UpdateAuthContext` |

#### [GameGateway] 区段（必填）

| 名称 | 类型 | 校验规则 | 当前值 | 说明 |
| --- | --- | --- | --- | --- |
| `Type` | string | 可选 | `Game` | 网关类型 |
| `Host` | string | 不能为空（`MissingRequiredField`）；必须为合法 IPv4 或域名，否则 `InvalidFieldFormat` | `192.168.1.78` | 网关主机（兼容旧字段 `Address`） |
| `Port` | int | 不能为 0（`MissingRequiredField`）；范围 1-65535，否则 `InvalidFieldFormat` | `7789` | 网关端口（须与服务端 `Network.TcpPort` 一致） |
| `InstanceId` | string | 可选 | `TYMYD-Gateway-001` | 网关实例 ID（须与服务端 `Gateway.GatewayId` 一致） |

#### [IMGateway] 区段（可选，缺失仅告警不阻断）

| 名称 | 类型 | 校验规则 | 当前值 | 说明 |
| --- | --- | --- | --- | --- |
| `Type` | string | 可选 | `IM` | IM 网关类型 |
| `Host` | string | 为空仅告警；非空时必须为合法 IPv4/域名，否则 `InvalidFieldFormat` | `192.168.1.78` | IM 网关主机 |
| `Port` | int | 范围 1-65535；为 0 仅告警 | `31000` | IM 网关端口 |
| `InstanceId` | string | 可选 | `TYMYD-IMGateway-001` | IM 网关实例 ID |
| `PersistHistory` | bool | 可选（`true`/`false`/`1`/`0`） | `false` | 是否持久化 IM 历史消息 |

### 3.2 配置文件查找顺序

`HorizonGameIniReader.Load` 按以下顺序定位 `HorizonGame.ini`（找到即停止）：

1. `overridePath` 参数（非空时优先；找不到不回退，直接返回 `FileNotFound`）
2. `AppDomain.CurrentDomain.BaseDirectory`（打包后可执行文件目录）
3. `Directory.GetCurrentDirectory()`（编辑器运行环境）

三处均未找到时返回 `ConfigLoadErrorCode.FileNotFound`。

### 3.3 耕地启动器写入流程

- 由耕地（GengDi）启动器的 `GameModelIniWriter` 在每次启动时**覆盖写入** `HorizonGame.ini`，文件头含固定注释：

```ini
; HorizonGame 配置文件 - 由耕地(GengDi)启动器自动生成，请勿手动修改
; 生成时间: 2026-05-28T14:06:54.2694352Z
```

- 因此**手动修改会被下次启动覆盖**；如需自定义配置（如测试），应通过 `HorizonGameIniReader.Load(overridePath)` 传入自定义路径绕过启动器写入。

### 3.4 完整 HorizonGame.ini 示例

```ini
; HorizonGame 配置文件 - 由耕地(GengDi)启动器自动生成，请勿手动修改
; 生成时间: 2026-05-28T14:06:54.2694352Z

[Game]
GameId=1001
AppType=369
AreaId=1
ServerId=1
Name=Horizon Adventure
Version=1.0.0
InstallationPath=C:\ProgramData\Horizon Adventure

[User]
PassportId=1010776
UserId=96

[Auth]
AuthToken=zEbSBqiZCwql4tttnofp7vcONyFTT8eSGkvkKlAKAytQxkGDbFnxTlHqPu/FwutMYmtOjNuxeJAn0dAW0Q1BwLORWCNI1HGPIcZRZOOU7MxSQVFbjlCBq6hofCnFA4rYOJoBLSIzfUO+5bEM7P42aynG/bgTwxekS9cgzCL8GXGLx7B1jzQvwwN0CEAqodkmfSRDjypPwkOYSMQoRRbq38ieR255k2qXxE4madkWIDvKmoV1julUD8F5kLgq9sqQkJBtHj8zBBoP53NHbWsIIA==

[GameGateway]
Type=Game
Host=192.168.1.78
Port=7789
InstanceId=TYMYD-Gateway-001

[IMGateway]
Type=IM
Host=192.168.1.78
Port=31000
InstanceId=TYMYD-IMGateway-001
PersistHistory=false
```

### 3.5 配置校验错误码对照表（ConfigLoadErrorCode）

| 错误码 | 值 | 现象 | 原因 |
| --- | --- | --- | --- |
| `FileNotFound` | 1 | `SecureConfig.Success=false`，`Errors` 含 `Field="File"` | `overridePath` / BaseDirectory / CurrentDirectory 三处均未找到 `HorizonGame.ini`；或文件所在目录不存在 |
| `MissingRequiredField` | 2 | `Errors` 含 `Field="Auth.AuthToken"` / `GameGateway.Host` / `GameGateway.Port` / `User.PassportId` / `User.UserId` / `Game.GameId` | 必填字段为空或缺失；`UserId=0`、`GameId=0`、`[Auth]` 区段缺失等 |
| `InvalidFieldFormat` | 3 | `Errors` 含 `Field="GameGateway.Port"` / `Auth.AuthToken` / `GameGateway.Host` 等 | Port 超出 1-65535；Host 非合法 IPv4/域名；AuthToken UTF-8 字节 < 32；`AreaId`/`ServerId` ≤ 0 |
| `FileAccessDenied` | 4 | `Errors` 含 `Field="File"`，附带 `UnauthorizedAccessException` 信息 | 当前进程无读取配置文件权限 |
| `IoError` | 5 | `Errors` 含 `Field="File"`，附带 `IOException` 信息 | 磁盘错误、文件被其他进程锁定、网络驱动器不可达等 |

> `Unknown=0` 为保留值，正常流程不应出现。

---

## 第 4 节：UE5 层配置

### 4.1 AWorldSyncActor 蓝图属性

`AWorldSyncActor`（`HundunWorld\Script\ManagedHundunWorld\WorldSyncActor.cs`）是 UE5 主线程驱动同步的入口 Actor。其蓝图可编辑属性如下：

| 属性 | 类型 | 标志 | 默认值 | 推荐值 | 作用 |
| --- | --- | --- | --- | --- | --- |
| `AutoInitializeSync` | bool | `BlueprintReadWrite \| EditAnywhere` | `true` | `true` | BeginPlay 时自动调用 `InitializeSync()`，蓝图无需手动调用 |
| `AutoTickSync` | bool | `BlueprintReadWrite \| EditAnywhere` | `true` | `true` | Actor Tick 时自动调用 `TickSync(δt)`，并开启 Actor Tick（`ActorTickInterval=0`） |
| `EcsTickRate` | float | `BlueprintReadWrite \| EditAnywhere` | `0.05` | `0.05` | ECS 推进节拍（秒），0.05s = 50ms = 20Hz，与服务端位置快照频率对齐 |

此外，ECS 后端选择由全局静态开关 `EcsBackendOptions.UseArchEcs`（`ECS\Arch\EcsBackendOptions.cs`）控制，**默认 `true`**。该开关**不是** `AWorldSyncActor` 的蓝图属性，而是 `InitializeSync()` 内读取的全局配置：

| 开关 | 类型 | 默认值 | 作用 |
| --- | --- | --- | --- |
| `EcsBackendOptions.UseArchEcs` | bool | `true` | `true` → 走 Arch ECS 新路径（`ArchEcsRuntime.Instance.EnsureDefaultSystems`）；`false` → 回退到旧 `EcsWorld` + `NetworkSyncSystem`（灰度回滚用） |

### 4.2 关卡中放置 AWorldSyncActor

1. 在 UE5 编辑器中打开目标关卡（如 `/Game/Maps/Start.Start`）。
2. 在 Content Browser 中找到 `AWorldSyncActor` 的蓝图子类（或通过 UnrealSharp 生成的 C++ 类直接拖放）。
3. 将其拖入视口，放置在场景任意位置（Actor 无需特定 Transform）。
4. 选中场景中的 `AWorldSyncActor`，在 Details 面板设置：
   - `Auto Initialize Sync` = `true`
   - `Auto Tick Sync` = `true`
   - `Ecs Tick Rate` = `0.05`
5. 保存关卡。

### 4.3 DefaultEngine.ini 的 GameMode 配置

`HundunWorld\Config\DefaultEngine.ini` 的 `[/Script/EngineSettings.GameMapsSettings]` 区段配置：

```ini
[/Script/EngineSettings.GameMapsSettings]
EditorStartupMap=/Game/Maps/Start.Start
GameInstanceClass=/NarrativePro/Pro/Core/BP/Framework/BP_NarrativeGameInstance.BP_NarrativeGameInstance_C
GameDefaultMap=/NarrativePro/Pro/Core/Maps/MainMenu/MainMenuMap.MainMenuMap
GlobalDefaultGameMode=/NarrativePro/Pro/Core/BP/Framework/BP_NarrativeGameMode.BP_NarrativeGameMode_C
GlobalDefaultServerGameMode=None
```

- **GlobalDefaultGameMode** 指向 `BP_NarrativeGameMode`（NarrativePro 框架 GameMode）。
- **GameInstanceClass** 指向 `BP_NarrativeGameInstance`。

### 4.4 BP_NarrativePlayerController 与 GameMode 的关联

- 玩家控制器蓝图路径：`/NarrativePro/Pro/Core/BP/Framework/BP_NarrativePlayerController.BP_NarrativePlayerController_C`
- 关联方式：`BP_NarrativeGameMode` 蓝图内将 `PlayerController Class` 属性设置为 `BP_NarrativePlayerController_C`（GameMode 通过 `PlayerControllerClass` 字段指定）。客户端连接后由 GameMode 为每个 Player 实例化该控制器。

### 4.5 AWorldSyncActor 初始化流程（BeginPlay）

当 `AutoInitializeSync=true` 时，`BeginPlay` 自动调用 `InitializeSync()`，流程如下：

1. `AWorldSyncActor.BeginPlay()` 检测 `AutoInitializeSync=true`，调用 `InitializeSync()`。
2. `InitializeSync()` 读取 `EcsBackendOptions.UseArchEcs`：
   - `true`（默认）→ 取 `ArchEcsRuntime.Instance` 单例，调用 `EnsureDefaultSystems(NetworkRuntime.Instance.WorldState)` 装配默认网络系统。
   - `false` → 构造旧 `EcsWorld` + `NetworkSyncSystem`（回退路径）。
3. 实例化 `UnrealNarrativeBridge` 并注册到 `NetworkRuntime.InteractionNotifySink` / `NarrativeBridge`，供 `InteractionApplySystem` 下行回调与 UE5 C++ 上行调用。
4. 若 `AutoTickSync=true`，设置 `ActorTickInterval=0`、`ActorTickEnabled=true`。

> 注：`InitializeSync()` 本身不建立网络连接；连接建立由 `NetworkRuntime.InitializeConnectionAsync()` 在握手阶段完成（见第 6 节）。配置加载（`HorizonGameIniReader.Load`）由 `NetworkRuntime.InitializeSync()` 触发。

### 4.6 AWorldSyncActor Tick 驱动流程

当 `AutoTickSync=true` 时，`Tick(deltaSeconds)` 自动调用 `TickSync(deltaSeconds)`：

1. 累加 `_timeSinceLastEcsTick += deltaSeconds`。
2. 当 `_ecsTickRate <= 0` 或 `_timeSinceLastEcsTick >= _ecsTickRate`（默认 0.05s）时：
   - 计算 `dt = TimeSpan.FromSeconds(_timeSinceLastEcsTick)`。
   - 调用 `ArchEcsRuntime.Tick(dt)`（或旧 `EcsWorld.Tick(dt)`）推进一帧。
   - 重置 `_timeSinceLastEcsTick = 0`。

---

## 第 5 节：ECS 系统装配

### 5.1 已装配系统清单

`ArchEcsRuntime.EnsureDefaultSystems(syncState)`（`HundunWorld\Script\ManagedHundunWorld\ECS\Arch\ArchEcsRuntime.cs`）按以下顺序装配系统，调用幂等（重复调用直接返回）：

| 顺序 | 系统名称 | 系统组 | 职责 |
| --- | --- | --- | --- |
| 1 | `ArchNetworkReceiveSystem` | NetworkReceive | 消费 `SyncInbox`（SyncPacket v2 收件箱），把解码后的快照/diff 写入本地 `WorldSyncState` |
| 2 | `ArchInterpolationSystem` | Render | 对远程实体位置做插值，补偿网络延迟（基于 `JitterBuffer` 的自适应延迟） |
| 3 | `InteractionApplySystem` | NetworkReceive | 消费 `NetworkRuntime.InteractionSyncEvents` 队列，回调 `IInteractionNotifySink`（下行通知 UE5 侧） |
| 4 | `SceneObjectApplySystem` | NetworkReceive（Order 20） | 消费 `SceneObjectEvents` 队列，回调 `ISceneObjectNotifySink`（驱动宝箱/开关/门/拉杆/传送门表现） |

> **如实说明**：当前 `EnsureDefaultSystems` 仅装配上述 4 个系统。代码中**不存在** `LocalSimulationSystem` / `NetworkSendSystem` 等系统（上行 input 由 `NetworkClient` 直接发送，无需独立 ECS 系统装配）。若后续新增系统，需在此清单同步更新。

### 5.2 系统组执行顺序

`ArchWorldHost` 的系统组执行顺序（每 tick 一次）：

```
NetworkReceive → FixedUpdate → Update → Render → NetworkSend
```

- **NetworkReceive**：`ArchNetworkReceiveSystem` / `InteractionApplySystem` / `SceneObjectApplySystem` 在此组消费网络数据。
- **Render**：`ArchInterpolationSystem` 在此组对位置做插值，供 UE5 渲染读取。
- `FixedUpdate` / `Update` / `NetworkSend` 当前无装配系统，为预留扩展点。

### 5.3 线程模型说明

- **ECS 系统**在 **UE5 主线程**执行，由 `AWorldSyncActor.Tick` 按 `EcsTickRate`（默认 50ms）驱动 `ArchEcsRuntime.Tick`。
- **网络回调**（TouchSocket 接收）在**网络线程**触发，通过 `ConcurrentQueue`（如 `SyncPacketInbox`、`InteractionSyncEvents`、`EventReceiveBuffer`）跨线程传递，ECS 线程在 `NetworkReceive` 组 dequeue 消费，避免锁竞争。
- **下行通知**（`IInteractionNotifySink` / `ISceneObjectNotifySink`）由 ECS 线程回调，UE5 侧实现（`UnrealNarrativeBridge`）将通知入队，由主线程轮询消费（`BridgeTryDequeue*` UFunction）。

### 5.4 ECS 运行时启动方式

- 由 `AWorldSyncActor.BeginPlay` → `InitializeSync()` 自动触发 `ArchEcsRuntime.Instance.EnsureDefaultSystems(WorldState)`，**无需手动启动**。
- `ArchEcsRuntime` 为单例（`LazyInstance`），整个客户端生命周期共享一个实例。
- 推进由 `AWorldSyncActor.TickSync` 调用 `ArchEcsRuntime.Tick(dt)`。

---

## 第 6 节：启动验证

### 6.1 启动顺序

```
1. Silo（Orleans 集群）  →  2. Gateway（游戏网关）  →  3. 客户端（UE5 编辑器 Play）
```

### 6.2 启动 Silo

```powershell
dotnet run --project Horizon.Orleans.Silo
```

预期输出：

```
[... INF] Silo started, ClusterId=dev, ServiceId=BaseService
[... INF] 集群成员上线: 192.168.1.78:11111
[... INF] Silo Gateway active on port 30000
```

> 验证 Silo 在 SQL Server `Orleans` 库的成员资格表中已注册一行（`MembershipTable`）。

### 6.3 启动 Gateway

```powershell
dotnet run --project Horizon.Game.Gateway
```

预期输出：

```
[... INF] 网关已启动: GatewayId=TYMYD-Gateway-001
[... INF] Gateway started, listening on 192.168.1.78:7789
[... INF] Orleans client connected to cluster dev
[... INF] Dashboard listening on http://192.168.1.78:1199
```

### 6.4 启动客户端

在 UE5 编辑器中点击 **Play**（或运行打包后的客户端）。客户端启动后：

1. `AWorldSyncActor.BeginPlay` → `NetworkRuntime.InitializeSync()` 加载 `HorizonGame.ini`（成功则 `IsConfigLoaded=true`）。
2. `NetworkRuntime.InitializeConnectionAsync()` 建立 TCP 连接到 `192.168.1.78:7789`。
3. 注入 `UserId` / `GameId` / `AreaId` / `ServerId` 与 `AuthToken` + `MachineGuid`。
4. 发送 `HandshakePacket`（携带 `ProtocolVersion=5`）。

### 6.5 握手协议 v5 校验

- **当前协议版本**：`SyncProtocolVersion.Current = 5`（`Horizon.Game.Message\Sync\SyncPackets.cs`）。
- 客户端 `HandshakePacket.ProtocolVersion` 默认填入 `5`，服务端 `SyncPacketHandler.HandleHandshakeAsync` 据此**严格拒绝**协议版本不匹配的客户端。
- 客户端日志（`HandshakeReceived` 事件）：
  - 成功：`[NetworkRuntime] InitializeConnectionAsync: 已连接 192.168.1.78:7789, UserId=96`
  - 失败：握手被拒，连接被服务端关闭。

### 6.6 SyncPacketKind 枚举对照

握手校验通过后，后续同步包类型（`SyncPacketKind`）：

| Kind | 值 | 包类型 | 方向 |
| --- | --- | --- | --- |
| `Handshake` | 1 | `HandshakePacket` | 客户端→服务端 |
| `Snapshot` | 2 | `SnapshotPacket` | 服务端→客户端 |
| `Input` | 3 | `InputPacket` | 客户端→服务端 |
| `Event` | 4 | `EventPacket` | 服务端→客户端 |
| `WorldChunkDiff` | 5 | `WorldChunkDiffPacket` | 服务端→客户端 |
| `WorldPatchManifest` | 6 | `WorldPatchManifestPacket` | 服务端→客户端 |
| `InputAck` | 7 | `InputAckPacket` | 服务端→客户端 |
| `ReconnectResume` | 8 | `ReconnectResumePacket` | 客户端→服务端 |
| `InteractionSync` | 9 | `InteractionSyncPacket` | 服务端→客户端 |
| `SceneObjectSync` | 10 | `SceneObjectSyncPacket` | 服务端→客户端 |

### 6.7 诊断日志路径

客户端诊断日志位于 `%LOCALAPPDATA%\HundunWorld\`，PowerShell 查看：

```powershell
Get-Content "$env:LOCALAPPDATA\HundunWorld\diag_merchant.log" -Tail 50
```

> 实际文件名以运行时输出为准；`NetworkRuntime` 通过 `System.Diagnostics.Debug.WriteLine` 输出，Debug 版本可在 IDE 输出窗口看到。

### 6.8 连通性检查命令

```powershell
# 1. TCP 端口（Gateway 主连接）
Test-NetConnection -ComputerName 192.168.1.78 -Port 7789
# 预期：TcpTestSucceeded : True

# 2. Redis（主节点）
redis-cli -h 127.0.0.1 -p 9379 -a DB65F7F9C ping
# 预期：PONG

# 3. SQL Server
sqlcmd -S . -Q "SELECT 1"
# 预期：1 列 1 行

# 4. Orleans Dashboard
# 浏览器访问 http://192.168.1.78:1199
```

### 6.9 同步频率常量（验证用）

服务端下发频率（`Horizon.Game.Message\Sync\CharacterSyncConfig.cs`）：

| 同步类型 | 频率 | 间隔 | 说明 |
| --- | --- | --- | --- |
| 位置（Transform） | 20Hz | 50ms | 每 tick 下发，匹配客户端 100ms 插值延迟窗口 |
| 移动状态（MovementState） | 10Hz | 100ms | 心跳 + 变化触发 |
| 属性（EntityState 扩展） | 1Hz | 1s | 心跳 + 变化触发，保证最终一致性 |

### 6.10 抖动缓冲与带宽守门参数

- **JitterBuffer 自适应插值延迟**（`Network\Sync\JitterBuffer.cs`）：80-200ms 区间，EMA 平滑系数 `α=0.2`，公式 `delay = Clamp(emaRtt * 1.5 + sqrt(rttVariance), 80, 200)`。
- **GatewaySyncDispatcher 带宽守门**（`Horizon.Game.Core\Sim\Server\GatewaySyncDispatcher.cs`）：阈值 `100kbps`，超阈值降频到 `10Hz`，连续 `3` 秒低于阈值回升到 `20Hz`（正常）。

---

## 第 7 节：故障排查

错误现象 → 原因 → 解决方案对照表。

| # | 现象 | 原因 | 解决方案 |
| --- | --- | --- | --- |
| 1 | 连接失败，`Test-NetConnection` 返回 `TcpTestSucceeded: False` | TCP 连不上 Gateway：`Network.IpAddress`/`TcpPort` 配置错误；防火墙拦截；Gateway 未启动 | 确认 `appsettings.json` 的 `Network.IpAddress=192.168.1.78` 与 `TcpPort=7789`；启动 Gateway 后用 `Test-NetConnection -ComputerName 192.168.1.78 -Port 7789` 验证；放行防火墙入站规则 |
| 2 | 握手被服务端拒绝，连接立即关闭 | 协议版本不匹配：客户端 `HandshakePacket.ProtocolVersion != 5` | 确认客户端与服务端使用同一份 `Horizon.Game.Message` 程序集（`SyncProtocolVersion.Current=5`）；重新编译两端 |
| 3 | 同步卡顿、远程实体位置跳跃 | JitterBuffer 自适应延迟配置不当或网络抖动剧烈 | 检查 `JitterBuffer.GetStats()`：若 `Jitter` 远大于 `EmaRttMs`，说明网络抖动大；确认 `AdaptiveMinDelayMs=80` / `AdaptiveMaxDelayMs=200` 未被外部覆盖；排查网络链路丢包 |
| 4 | 带宽超限降频，远程实体变"卡" | 单 session 带宽超过 `100kbps`，`GatewaySyncDispatcher` 降频到 `10Hz` | 查看 Gateway 日志 `带宽超阈值限流`；排查 AOI 范围内实体数量是否过大；调整 `BandwidthThresholdKbps` 或缩小 AOI Interest Set |
| 5 | 客户端启动即报配置错误，`IsConfigLoaded=false` | 配置缺失：`AuthToken` UTF-8 字节 < 32（`InvalidFieldFormat`）；`UserId=0`（`MissingRequiredField`）；`[GameGateway].Host` 为空（`MissingRequiredField`） | 用 `SecureConfig.Errors` 定位字段；重新通过耕地启动器生成 `HorizonGame.ini`；确保 `AuthToken` ≥ 32 字节、`UserId>0`、`Host` 非空且为合法 IPv4/域名 |
| 6 | Orleans 集群无法形成，Silo 启动后 Dashboard 看不到成员 | `ClusteringSiloOptions.SqlServer` 配置错误或 SQL Server 不可达 | 验证 `sqlcmd -S . -Q "SELECT 1"` 可用；确认 `Orleans` 库已建表（`001_world_state.sql`）；确认 `ClusterId`/`ServiceId` 在 Silo 与 Gateway 间一致 |
| 7 | Redis 连接失败，启动报 `RedisMasters` 连接异常 | `DataBase.RedisMasters` 的 Host/Port/Password 错误 | 用 `redis-cli -h 127.0.0.1 -p 9379 -a DB65F7F9C ping` 验证；确认密码与 `requirepass` 一致；确认主从复制状态正常 |
| 8 | SQL 建表失败 | 权限不足或数据库不存在 | 用 sa 账号执行；确认 `Orleans` / `Basic` / `Game` 库已 `CREATE DATABASE`；按 `scripts/sql/` 顺序执行（001→004） |
| 9 | `AWorldSyncActor` 未初始化，`IsConfigLoaded=false` 且无网络 | 关卡未放置 `AWorldSyncActor`；或 `AutoInitializeSync=false` | 在关卡中放置 `AWorldSyncActor`；Details 面板设置 `Auto Initialize Sync=true`；保存关卡后重启 PIE |
| 10 | ECS 系统未装配，`ArchEcsRuntime.EnsureDefaultSystems` 未调用 | `AWorldSyncActor.InitializeSync()` 未执行（关卡无 Actor 或 `AutoInitializeSync=false`）；或 `EcsBackendOptions.UseArchEcs=false` 走了旧路径 | 确保关卡放置 `AWorldSyncActor` 且 `AutoInitializeSync=true`；确认 `EcsBackendOptions.UseArchEcs=true`（默认） |

### 7.1 ConfigLoadErrorCode 全部错误码排查指引

| 错误码 | 现象 | 原因 | 解决方案 |
| --- | --- | --- | --- |
| `FileNotFound` (1) | `NetworkRuntime.InitializeSync` 返回 false，`SecureConfig.Errors[0].FieldName="File"` | `overridePath` / BaseDirectory / CurrentDirectory 三处均无 `HorizonGame.ini`；或 `overridePath` 指定的路径不存在 | 确认 `HorizonGame.ini` 在可执行文件目录或当前工作目录；自定义路径时确保 `overridePath` 文件真实存在 |
| `MissingRequiredField` (2) | `Errors` 含 `Auth.AuthToken` / `GameGateway.Host` / `GameGateway.Port` / `User.PassportId` / `User.UserId` / `Game.GameId` | 必填字段为空或缺失；`UserId=0`、`GameId=0`、`[Auth]` 区段缺失 | 重新通过耕地启动器生成配置；手动补齐缺失字段 |
| `InvalidFieldFormat` (3) | `Errors` 含 `GameGateway.Port` / `GameGateway.Host` / `Auth.AuthToken` / `Game.AreaId` / `Game.ServerId` | Port 超出 1-65535；Host 非合法 IPv4/域名；AuthToken UTF-8 字节 < 32；`AreaId`/`ServerId` ≤ 0 | 修正对应字段值；AuthToken 用 Base64 串确保 ≥ 32 字节；Host 用 IPv4 或合法域名 |
| `FileAccessDenied` (4) | `Errors` 含 `File`，附带 `UnauthorizedAccessException` | 当前进程无读取配置文件权限 | 以管理员身份运行；或调整文件 ACL 赋予当前用户读权限 |
| `IoError` (5) | `Errors` 含 `File`，附带 `IOException` | 磁盘错误、文件被其他进程锁定、网络驱动器不可达 | 关闭占用该文件的进程（如启动器仍在写入）；检查磁盘与网络驱动器状态 |

---

## 附录：关键文件索引

| 文件 | 用途 |
| --- | --- |
| `Horizon.Game.Gateway\appsettings.json` | 服务端配置（网络/Orleans/数据库/Gateway/安全/鉴权） |
| `HundunWorld\Config\HorizonGame.ini` | 客户端配置（游戏/用户/鉴权/网关） |
| `HundunWorld\Script\ManagedHundunWorld\Config\HorizonGameIniReader.cs` | 客户端配置安全读取器（校验规则） |
| `HundunWorld\Script\ManagedHundunWorld\Config\ConfigLoadErrorCode.cs` | 配置加载错误码枚举 |
| `HundunWorld\Script\ManagedHundunWorld\WorldSyncActor.cs` | UE5 同步入口 Actor |
| `HundunWorld\Script\ManagedHundunWorld\ECS\Arch\ArchEcsRuntime.cs` | Arch ECS 运行时单例 |
| `HundunWorld\Script\ManagedHundunWorld\ECS\Arch\EcsBackendOptions.cs` | ECS 后端开关（`UseArchEcs`） |
| `HundunWorld\Script\ManagedHundunWorld\Network\NetworkRuntime.cs` | 客户端网络共享运行时 |
| `HundunWorld\Script\ManagedHundunWorld\Network\Sync\JitterBuffer.cs` | 抖动缓冲（自适应插值延迟） |
| `HundunWorld\Config\DefaultEngine.ini` | UE5 引擎配置（GameMode/GameInstance） |
| `Horizon.Game.Message\Sync\SyncPackets.cs` | 同步协议版本与包种类枚举 |
| `Horizon.Game.Message\Sync\CharacterSyncConfig.cs` | 同步频率常量 |
| `Horizon.Game.Core\Sim\Server\GatewaySyncDispatcher.cs` | 服务端带宽守门与降频策略 |
| `scripts\sql\` | SQL 建库建表脚本（001-004） |
