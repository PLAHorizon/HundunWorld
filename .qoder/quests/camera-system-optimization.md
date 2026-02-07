# 相机系统震荡消除与性能优化设计

## 需求背景

当前第三人称相机系统在跟随角色过程中存在反复震荡问题，表现为：
- 震荡频率高但幅度小
- 造成阴影抖动和视觉重影
- 增加额外性能消耗

需要通过系统性优化，消除震荡问题，并提升整体性能，实现丝滑的相机跟随体验。

---

## 问题根源分析

### 核心问题诊断

通过代码分析，发现相机震荡的根本原因是**多个动态调整系统之间缺乏优先级协调和防抖机制**，导致以下问题链：

#### 1. 碰撞检测与弹性跟随的反馈循环

**问题表现**
- 碰撞检测系统持续调整相机距离（`_currentCollisionDistance`）
- 弹性跟随系统同时调整位置和旋转（`PositionElasticity`、`RotationElasticity`）
- FOV动态调整系统基于速度变化视野范围
- 三个系统同时作用于同一帧内的相机变换，产生相互干扰

**震荡产生机制**
```
帧N: 碰撞检测拉近相机 → _currentCollisionDistance减小
     ↓
帧N+1: 弹性跟随响应位置变化 → 重新计算focusPoint
     ↓
帧N+2: FOV系统检测到速度变化 → 调整视野范围
     ↓
帧N+3: 碰撞检测因位置变化重新触发 → 产生微小距离调整
     ↓
循环往复，形成持续震荡
```

#### 2. 平滑插值速度不匹配

**当前实现**
- 碰撞平滑速度：`CollisionSmoothSpeed * (2~5)倍`动态调整
- 弹性跟随速度：`PositionElasticity = 0.8`固定值
- FOV调整速度：`FOVSmoothSpeed = 3`固定值
- 距离恢复速度：碰撞状态与非碰撞状态速度倍数差异为2-5倍

**问题根源**
- 不同系统的插值速度未统一规划
- 进入和退出碰撞时速度差异过大（如`CollisionSmoothSpeed * 5`与`* 1`）
- 缺乏最小变化阈值，对微小变化频繁响应

#### 3. 缺乏防抖机制

**代码证据（Line 1136-1225）**
```
当前逻辑：
- 每帧都执行Lerp插值，无变化阈值检查
- 距离变化小于50cm时仍然进行平滑调整
- 碰撞状态切换存在0.2秒稳定时间，但距离调整没有对应防抖
```

**影响**
- 在复杂场景（多碰撞体边缘）产生高频微调
- 阴影系统对相机位置变化敏感，微小抖动导致阴影重新计算
- 渲染管线因相机矩阵持续变化增加计算开销

#### 4. 多系统竞争控制权

**ThirdPersonCamera与CameraSystem的职责冲突**
- ThirdPersonCamera：完整的相机逻辑实现（Line 958-1303）
- CameraSystem（ECS）：独立的相机位置计算（CameraSystem.cs Line 116-147）
- 两套系统可能同时尝试设置相机位置，产生竞态

**内存经验验证**
根据历史经验记忆：
> "当多个系统（如ThirdPersonCamera与CameraSystem）同时尝试控制相机位置时，会导致行为异常（如相机被拉回脚底）"

---

## 优化设计方案

### 总体策略

采用**分层优先级控制 + 多级防抖 + 系统协调**的综合方案：

1. **优先级控制**：建立明确的系统优先级，高优先级状态禁用低优先级功能
2. **多级防抖**：根据变化幅度采用分层防抖策略
3. **速度匹配**：统一各系统的平滑插值速度规范
4. **单一控制权**：明确相机控制权归属，避免多系统竞争

---

### 设计一：优先级控制系统

#### 设计目标
建立清晰的功能优先级层次，高优先级状态激活时自动禁用低优先级功能，防止系统间相互干扰。

#### 优先级定义

| 优先级 | 状态/功能 | 触发条件 | 禁用功能 |
|-------|----------|---------|---------|
| P0 | 碰撞检测激活 | 检测到障碍物 | 弹性跟随、FOV动态调整 |
| P1 | 拍照模式 | 用户触发拍照 | 所有自动调整 |
| P2 | 基准视角恢复 | 用户按下R键 | 所有自动调整 |
| P3 | 弹性跟随 | 正常跟随状态 | 无 |
| P4 | FOV动态调整 | 角色移动速度变化 | 无 |

#### 实现机制

**优先级状态机**
```
新增字段：
- _currentPriority: CameraPriority枚举
- _priorityChangeTime: 优先级切换时间戳

优先级枚举定义：
- CameraPriority.Collision (最高)
- CameraPriority.PhotoMode
- CameraPriority.BaselineReset
- CameraPriority.ElasticFollow
- CameraPriority.DynamicFOV
- CameraPriority.Normal (最低)
```

**优先级检查逻辑**
在每个系统执行前检查优先级：
```
碰撞检测系统：
- 检测到碰撞 → 设置_currentPriority = Collision
- 离开碰撞 → 延迟0.3秒后降级到Normal
- 激活时：禁用弹性跟随、FOV调整

弹性跟随系统：
- 执行前检查：if (_currentPriority <= ElasticFollow) 则跳过

FOV动态调整系统：
- 执行前检查：if (_currentPriority <= DynamicFOV) 则跳过
- 碰撞状态下强制恢复到BaseFOV
```

**优先级切换防抖**
```
优先级降级延迟机制：
- 高优先级状态结束后，等待stabilization_delay（0.3秒）
- 期间再次触发高优先级则重置计时器
- 避免在边界状态频繁切换优先级
```

---

### 设计二：多级防抖机制

#### 设计目标
根据变化幅度采用分层防抖策略，对微小变化完全静止，对中等变化中速响应，对大变化快速响应。

#### 防抖阈值分级

| 变化幅度 | 阈值范围 | 响应策略 | 插值速度倍数 |
|---------|---------|---------|------------|
| 微小变化 | < 10cm | 完全静止 | 0（不更新） |
| 小变化 | 10-30cm | 慢速响应 | 1x |
| 中等变化 | 30-80cm | 中速响应 | 2x |
| 大变化 | > 80cm | 快速响应 | 3x |

#### 实现细节

**距离变化防抖**
```
新增字段：
- _lastAppliedDistance: 上次真正应用到Actor的距离
- _distanceChangeAccumulator: 距离变化累积器

防抖逻辑：
每帧计算distanceChange = |targetDistance - _lastAppliedDistance|

if (distanceChange < 10cm):
    // 微小变化，完全静止
    return // 不执行任何更新
    
else if (distanceChange < 30cm):
    // 小变化，慢速响应
    smoothSpeed = CollisionSmoothSpeed * 1.0
    
else if (distanceChange < 80cm):
    // 中等变化，中速响应
    smoothSpeed = CollisionSmoothSpeed * 2.0
    
else:
    // 大变化，快速响应
    smoothSpeed = CollisionSmoothSpeed * 3.0
```

**位置变化防抖**
```
新增字段：
- _lastAppliedPosition: 上次应用的相机位置
- _positionStableFrames: 位置稳定帧计数

防抖逻辑：
positionChange = Vector3.Distance(newPosition, _lastAppliedPosition)

if (positionChange < 1cm):
    _positionStableFrames++
    if (_positionStableFrames > 3):
        return // 连续3帧变化小于1cm，停止更新
else:
    _positionStableFrames = 0
    // 执行位置更新
```

**旋转变化防抖**
```
新增字段：
- _lastAppliedYaw: 上次应用的Yaw角度
- _yawChangeThreshold: 旋转防抖阈值（1.0度）

防抖逻辑：
yawChange = |newYaw - _lastAppliedYaw|

if (yawChange < _yawChangeThreshold):
    return // 角度变化小于1度，不更新
```

---

### 设计三：平滑速度统一规范

#### 设计目标
统一各系统的插值速度标准，避免速度不匹配导致的反馈循环。

#### 速度规范表

| 系统 | 参数 | 推荐值 | 调整原则 |
|-----|------|-------|---------|
| 碰撞检测 | CollisionSmoothSpeed | 8.0 | 基准速度 |
| 碰撞恢复 | 退出碰撞速度 | 4.0 | 基准速度的0.5倍 |
| 弹性跟随 | PositionElasticity | 基于速度动态 | 见下文 |
| 旋转跟随 | RotationElasticity | 0.9（固定） | 快速响应 |
| FOV调整 | FOVSmoothSpeed | 5.0 | 中等速度 |
| 基准恢复 | ResetSmoothSpeed | 6.0 | 稍快速度 |

#### 弹性跟随动态速度

**基于碰撞状态的自适应速度**
```
计算逻辑：
if (_currentPriority == Collision):
    // 碰撞时禁用弹性跟随
    elasticity = 1.0 // 直接跟随，无延迟
    
else if (_targetVelocity.Length < 1.0): // 角色静止
    elasticity = 0.95 // 几乎完全跟随
    
else if (_targetVelocity.Length < 10.0): // 慢速移动
    elasticity = 0.85 // 轻微延迟
    
else: // 快速移动
    elasticity = 0.75 // 明显延迟感
```

**速度匹配检验公式**
```
确保系统间速度比例合理：
碰撞进入速度 / 碰撞退出速度 = 2.0（不超过2倍）
碰撞速度 / 弹性跟随速度 ≈ 1.5（相近但碰撞优先）
```

---

### 设计四：相机控制权单一化

#### 设计目标
明确相机控制权归属，避免ThirdPersonCamera与CameraSystem竞争。

#### 架构决策

**方案：ThirdPersonCamera为主控，CameraSystem仅用于ECS数据同步**

| 组件 | 职责 | 禁止操作 |
|-----|------|---------|
| ThirdPersonCamera | 相机逻辑主控制器<br>- 处理所有输入<br>- 执行碰撞检测<br>- 计算最终位置和朝向<br>- 直接设置Actor.Position | 无 |
| CameraSystem（ECS） | 仅用于状态同步<br>- 读取CameraComponent数据<br>- 同步到ECS查询<br>- 不执行位置计算 | **禁止**设置Actor.Position<br>**禁止**执行碰撞检测 |
| CameraComponent | ECS数据容器<br>- 存储相机参数<br>- 作为ECS查询目标 | **禁止**包含逻辑代码 |

#### 实现调整

**CameraSystem改造**
```
当前CameraSystem.Update的问题（Line 116-147）：
- 执行了完整的位置计算
- 直接调用cameraActor.Position赋值
- 与ThirdPersonCamera产生竞争

改造方案：
CameraSystem.Update仅执行：
1. 读取CameraComponent参数
2. 将参数同步到其他需要的ECS系统
3. **移除**所有位置计算和Actor.Position赋值
4. 改为读取ThirdPersonCamera计算后的位置
```

**职责分离验证**
```
执行流程：
每帧开始
  ↓
ThirdPersonCamera.OnUpdate()
  ├─ 处理输入
  ├─ 执行碰撞检测
  ├─ 计算最终位置
  ├─ 设置Actor.Position（唯一修改点）
  └─ 更新CameraComponent数据（用于ECS查询）
  ↓
CameraSystem.Update()
  ├─ 查询CameraComponent
  ├─ 同步数据到其他系统
  └─ **不修改**Actor.Position
```

---

### 设计五：碰撞状态稳定时间优化

#### 当前问题

代码Line 1145-1173实现了碰撞状态稳定机制（0.2秒），但存在不足：
- 仅检查碰撞状态变化（有/无碰撞）
- 未检查碰撞距离变化
- 在多碰撞体边缘区域仍可能频繁调整距离

#### 优化方案

**双重稳定检查**
```
新增字段：
- _collisionDistanceChangeTime: 碰撞距离变化时间戳
- _lastStableCollisionDistance: 最后稳定的碰撞距离

稳定条件：
满足以下所有条件才认为碰撞状态稳定：
1. 碰撞状态（有/无）保持不变超过0.2秒
2. 碰撞距离变化小于20cm超过0.2秒
3. 焦点位置变化小于50cm超过0.1秒

只有稳定后才应用新的碰撞距离
```

**稳定窗口机制**
```
实现逻辑：
if (|newCollisionDistance - _lastStableCollisionDistance| > 20cm):
    // 距离变化较大，重置稳定计时器
    _collisionDistanceChangeTime = currentTime
    
else:
    // 距离变化小，检查稳定时间
    if (currentTime - _collisionDistanceChangeTime > 0.2s):
        // 已稳定，应用新距离
        _currentCollisionDistance = Lerp(...)
        _lastStableCollisionDistance = newCollisionDistance
    else:
        // 未稳定，保持当前距离
        // 不执行任何调整
```

---

### 设计六：渲染优化与缓存增强

#### 优化目标
减少不必要的渲染更新，增强碰撞检测缓存效率。

#### 渲染更新优化

**最小变化阈值表**

| 属性 | 当前阈值 | 优化阈值 | 说明 |
|-----|---------|---------|------|
| Position | 无阈值 | 0.5cm | 低于此值不更新Actor.Position |
| Orientation | 无阈值 | 0.1度 | 低于此值不更新Actor.Orientation |
| FOV | 无阈值 | 0.05度 | 低于此值不更新Camera.FieldOfView |

**实现机制**
```
新增字段：
- _lastRenderedPosition: 上次渲染使用的位置
- _lastRenderedOrientation: 上次渲染使用的朝向
- _lastRenderedFOV: 上次渲染使用的FOV

更新检查：
if (Vector3.Distance(newPosition, _lastRenderedPosition) > 0.5cm):
    Actor.Position = newPosition
    _lastRenderedPosition = newPosition
    // 否则跳过更新，减少渲染管线压力
```

#### 碰撞缓存增强

**当前缓存有效性检查（Line 1746-1760）**
```
当前条件（三者全部满足）：
- 时间差 < 0.1秒
- 位置差 < 50cm
- 距离差 < 20cm
```

**优化方案：动态缓存时间**
```
缓存时间自适应：
if (场景复杂度低 && 角色速度低):
    cacheTimeout = 0.2s // 延长缓存时间
    
else if (场景复杂度高 || 角色高速移动):
    cacheTimeout = 0.05s // 缩短缓存时间
    
场景复杂度评估：
- 基于最近N帧碰撞检测的平均碰撞数
- 碰撞数 > 3认为复杂
```

**预测性缓存**
```
新增：预测下一帧碰撞状态
if (角色速度矢量已知):
    predictedPosition = currentPosition + velocity * deltaTime
    if (predictedPosition与缓存位置接近):
        // 预测命中，延长缓存有效期
        cacheTimeout *= 1.5
```

---

### 设计七：阴影抖动专项优化

#### 问题分析

阴影抖动的根本原因：
- 相机位置微小变化导致阴影投射矩阵重新计算
- 阴影贴图采样位置变化
- 级联阴影（CSM）的级别边界抖动

#### 优化策略

**阴影更新阈值独立控制**
```
新增配置：
- ShadowUpdateThreshold: 触发阴影更新的最小相机移动距离（默认5cm）
- ShadowUpdateInterval: 最小阴影更新间隔（默认0.1秒）

实现逻辑：
新增字段：
- _lastShadowUpdatePosition: 上次触发阴影更新的位置
- _lastShadowUpdateTime: 上次阴影更新时间

更新检查：
movementSinceShadowUpdate = Vector3.Distance(currentPosition, _lastShadowUpdatePosition)
timeSinceShadowUpdate = currentTime - _lastShadowUpdateTime

if (movementSinceShadowUpdate > ShadowUpdateThreshold 
    && timeSinceShadowUpdate > ShadowUpdateInterval):
    // 触发阴影更新
    triggerShadowUpdate()
    _lastShadowUpdatePosition = currentPosition
    _lastShadowUpdateTime = currentTime
else:
    // 跳过阴影更新，保持上一帧阴影
```

**时间平滑策略**
```
阴影位置使用独立的平滑插值：
shadowCameraPosition = Lerp(
    _lastShadowPosition, 
    actualCameraPosition, 
    deltaTime * ShadowSmoothSpeed // 更慢的平滑速度
)

ShadowSmoothSpeed建议值：2.0（比相机位置平滑慢）
```

---

## 配置参数调整建议

### 新增配置参数

```
[Header("震荡优化")]
[Tooltip("启用优先级控制系统")]
public bool EnablePriorityControl = true;

[Tooltip("启用多级防抖机制")]
public bool EnableMultiLevelAntiShake = true;

[Tooltip("距离变化微小阈值（cm，低于此值完全静止）")]
public float DistanceMicroChangeThreshold = 10.0f;

[Tooltip("位置变化微小阈值（cm）")]
public float PositionMicroChangeThreshold = 1.0f;

[Tooltip("旋转变化微小阈值（度）")]
public float RotationMicroChangeThreshold = 1.0f;

[Tooltip("碰撞状态稳定时间（秒）")]
public float CollisionStabilizationTime = 0.2f;

[Tooltip("碰撞距离稳定时间（秒）")]
public float CollisionDistanceStabilizationTime = 0.2f;

[Tooltip("优先级降级延迟（秒）")]
public float PriorityDowngradeDelay = 0.3f;

[Header("渲染优化")]
[Tooltip("渲染位置更新阈值（cm）")]
public float RenderPositionThreshold = 0.5f;

[Tooltip("渲染朝向更新阈值（度）")]
public float RenderOrientationThreshold = 0.1f;

[Tooltip("阴影更新移动阈值（cm）")]
public float ShadowUpdateThreshold = 5.0f;

[Tooltip("阴影更新最小间隔（秒）")]
public float ShadowUpdateInterval = 0.1f;

[Tooltip("阴影平滑速度")]
public float ShadowSmoothSpeed = 2.0f;
```

### 现有参数调整

| 参数 | 当前值 | 推荐值 | 调整理由 |
|-----|-------|-------|---------|
| CollisionSmoothSpeed | 10.0 | 8.0 | 降低基准速度，提升稳定性 |
| PositionElasticity | 0.8 | 动态值 | 根据状态和速度动态调整 |
| RotationElasticity | 0.9 | 0.9 | 保持不变，快速响应合理 |
| FOVSmoothSpeed | 3.0 | 5.0 | 提高速度，减少延迟感 |
| CollisionStateStableTime | 0.2 | 0.2 | 保持不变 |

---

## 性能提升预期

### 优化前性能基线（估算）

| 指标 | 当前状态 |
|-----|---------|
| 每帧碰撞检测调用 | 1次（Medium模式5射线） |
| 缓存命中率 | 约40-50% |
| 位置更新频率 | 100%（每帧） |
| FOV更新频率 | 100%（每帧） |
| 阴影更新频率 | 100%（每帧） |

### 优化后性能预期

| 指标 | 优化后预期 | 提升幅度 |
|-----|-----------|---------|
| 碰撞检测调用 | 减少30-40% | 通过缓存优化和防抖 |
| 缓存命中率 | 提升至70-80% | 通过预测性缓存 |
| 位置更新频率 | 减少60-70% | 通过防抖和阈值控制 |
| FOV更新频率 | 减少80% | 碰撞时完全禁用 |
| 阴影更新频率 | 减少80-90% | 独立阈值控制 |
| 整体帧耗时 | 降低20-30% | 综合优化效果 |

### 震荡消除效果

| 指标 | 优化前 | 优化后 |
|-----|-------|-------|
| 微小位置震荡 | 频繁出现 | 完全消除 |
| 阴影抖动 | 明显 | 基本消除 |
| 视觉重影 | 偶尔出现 | 完全消除 |
| 碰撞边缘震荡 | 高频 | 显著降低 |

---

## 实现优先级与阶段规划

### 阶段一：核心震荡消除（高优先级）

**预期时间：3-4小时**

1. **优先级控制系统实现**（1.5小时）
   - 定义CameraPriority枚举
   - 实现优先级状态机
   - 集成到各子系统执行前检查

2. **多级防抖机制实现**（1.5小时）
   - 实现距离变化防抖
   - 实现位置变化防抖
   - 实现旋转变化防抖

3. **验证测试**（1小时）
   - 在复杂场景测试震荡消除效果
   - 检查阴影抖动改善情况
   - 调整阈值参数

### 阶段二：平滑速度优化（中优先级）

**预期时间：2-3小时**

1. **速度规范实施**（1小时）
   - 调整各系统平滑速度参数
   - 实现弹性跟随动态速度

2. **碰撞状态稳定优化**（1小时）
   - 实现双重稳定检查
   - 实现稳定窗口机制

3. **测试与调优**（1小时）
   - 验证速度匹配效果
   - 调整速度比例参数

### 阶段三：性能优化（中优先级）

**预期时间：2-3小时**

1. **渲染更新优化**（1小时）
   - 实现渲染更新阈值检查
   - 实现阴影更新独立控制

2. **碰撞缓存增强**（1小时）
   - 实现动态缓存时间
   - 实现预测性缓存

3. **性能测试**（1小时）
   - 测量优化前后性能指标
   - 生成性能对比报告

### 阶段四：系统协调（低优先级）

**预期时间：1-2小时**

1. **CameraSystem改造**（1小时）
   - 移除位置计算逻辑
   - 改为纯数据同步

2. **集成测试**（1小时）
   - 验证ThirdPersonCamera与CameraSystem协作
   - 确认无竞态条件

---

## 测试验证方案

### 震荡测试场景

1. **碰撞边缘场景**
   - 场景描述：角色在狭窄走廊缓慢移动
   - 测试内容：检查相机是否出现高频微调
   - 通过标准：位置变化低于防抖阈值时完全静止

2. **复杂碰撞场景**
   - 场景描述：多个碰撞体密集区域
   - 测试内容：检查碰撞检测是否频繁触发
   - 通过标准：缓存命中率>70%

3. **阴影抖动场景**
   - 场景描述：强光照环境下角色移动
   - 测试内容：观察阴影是否稳定
   - 通过标准：阴影更新频率降低80%以上

### 性能测试指标

**采集指标**
- 每帧碰撞检测调用次数
- 缓存命中率
- 位置/FOV/阴影更新频率
- 平均帧耗时

**测试工具**
- 使用EnablePerformanceMonitoring开关
- 记录CameraPerformanceStats数据
- 生成60秒性能采样报告

**对比基线**
- 优化前：在相同场景记录基线数据
- 优化后：在相同场景记录优化数据
- 生成对比报告

---

## 风险评估与缓解

### 风险一：防抖阈值过大导致响应迟钝

**风险描述**
- 防抖阈值设置过大可能导致相机对快速移动响应不及时
- 用户感觉相机"粘滞"或"慢半拍"

**缓解措施**
- 采用分层防抖策略，大变化快速响应
- 提供配置参数供开发者调整
- 在测试阶段收集用户反馈，动态调整阈值

### 风险二：优先级系统过于复杂

**风险描述**
- 优先级逻辑可能增加代码复杂度
- 调试困难

**缓解措施**
- 提供详细的优先级切换日志（调试模式）
- 设计清晰的状态机图
- 提供EnablePriorityControl开关，可随时禁用

### 风险三：性能优化带来视觉质量下降

**风险描述**
- 渲染更新阈值可能在某些情况下导致视觉卡顿
- 阴影更新延迟可能在快速移动时明显

**缓解措施**
- 阈值设置为人眼难以察觉的级别（<1cm位置，<0.1度角度）
- 提供"高品质模式"配置，降低优化程度
- 在高速移动时自动降低防抖力度

### 风险四：ECS系统改造影响其他功能

**风险描述**
- CameraSystem职责变更可能影响依赖它的其他ECS系统

**缓解措施**
- 改造前全面梳理CameraSystem的调用关系
- 保留CameraComponent数据接口不变
- 渐进式改造，先并行运行后切换

---

## 后续优化方向

### 可选优化项（低优先级）

1. **机器学习预测**
   - 基于历史移动数据预测角色下一步位置
   - 进一步提升缓存命中率

2. **自适应质量调整**
   - 基于设备性能自动调整碰撞检测质量
   - 低端设备自动降级到Low模式

3. **物理碰撞体优化**
   - 为相机专门设计简化碰撞层
   - 减少碰撞检测计算量

4. **多线程异步检测**
   - 将碰撞检测移至后台线程
   - 主线程仅使用缓存结果

---

## 附录

### 关键代码位置索引

| 功能模块 | 文件 | 行号范围 |
|---------|------|---------|
| 碰撞检测主逻辑 | ThirdPersonCamera.cs | 1720-1880 |
| 碰撞距离调整 | ThirdPersonCamera.cs | 1135-1233 |
| 弹性跟随系统 | ThirdPersonCamera.cs | 1028-1078 |
| FOV动态调整 | ThirdPersonCamera.cs | 1914-1981 |
| OnUpdate主循环 | ThirdPersonCamera.cs | 958-1303 |
| CameraSystem位置计算 | CameraSystem.cs | 116-147 |
| 碰撞缓存检查 | ThirdPersonCamera.cs | 1745-1760 |
| 地面穿透防护 | ThirdPersonCamera.cs | 1308-1374 |

### 术语表

| 术语 | 定义 |
|-----|------|
| 震荡 | 相机位置/角度在目标值附近小幅度快速往复变化 |
| 防抖 | 通过阈值过滤微小变化，避免频繁更新 |
| 优先级控制 | 高优先级功能激活时禁用低优先级功能的机制 |
| 碰撞稳定时间 | 碰撞状态改变后需要保持的最短时间 |
| 弹性跟随 | 相机位置延迟跟随目标，产生惯性效果 |
| 渲染阈值 | 触发渲染更新的最小属性变化值 |
| 缓存命中率 | 碰撞检测使用缓存结果的比例 |
| 平滑插值 | 使用Lerp等函数在当前值和目标值间平滑过渡 |

