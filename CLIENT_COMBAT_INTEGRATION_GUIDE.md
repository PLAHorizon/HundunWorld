# 客户端战斗系统集成指南

**创建日期**: 2026年2月12日  
**状态**: ✅ Phase 0 代码层完成  

---

## 📦 已完成的功能模块

### 1. 目标选择系统 ✅
**文件**: [TargetSelectionSystem.cs](file:///c:/Works/GitHubProjects/HundunWorld/HundunWorld/Source/Game/Combat/TargetSelectionSystem.cs)

**功能特性**:
- ✅ Tab键切换目标（循环）
- ✅ Shift+Tab 反向切换
- ✅ Ctrl+鼠标左键 点击选择
- ✅ ESC键取消选择
- ✅ 目标高亮显示（地面圆圈+箭头+名称）
- ✅ 自动过滤距离外的目标
- ✅ 目标失效自动取消选择

**集成方法**:
```csharp
// 在 PlayerController 或 CombatController 中添加
public TargetSelectionSystem TargetSelection;

public override void OnStart()
{
    // 方法1: 自动查找
    TargetSelection = Scene.FindScript<TargetSelectionSystem>();
    
    // 方法2: 手动添加
    TargetSelection = Actor.AddScript<TargetSelectionSystem>();
    TargetSelection.MaxSelectDistance = 50f;
    TargetSelection.ShowSelectionBox = true;
    
    // 订阅目标切换事件
    TargetSelection.OnTargetChanged += OnTargetChanged;
}

private void OnTargetChanged(Actor newTarget)
{
    if (newTarget != null)
    {
        Debug.Log($"选中新目标: {newTarget.Name}");
        // 更新UI显示目标信息
    }
    else
    {
        Debug.Log("取消目标选择");
    }
}

// 在技能释放时使用当前目标
private void CastSkill(SkillInfo skill)
{
    var target = TargetSelection.CurrentTarget;
    if (target == null)
    {
        Debug.LogWarning("没有选中目标");
        return;
    }
    
    // 创建攻击动作
    var attack = new AttackAction
    {
        AttackerId = playerEntityId,
        DefenderId = GetEntityId(target),
        Skill = skill,
        AttackPosition = target.Position
    };
    
    CombatSystemManager.Instance.ProcessAttack(attack);
}
```

---

### 2. AOE范围指示器 ✅
**文件**: [AOEIndicatorSystem.cs](file:///c:/Works/GitHubProjects/HundunWorld/HundunWorld/Source/Game/Combat/AOEIndicatorSystem.cs)

**功能特性**:
- ✅ 圆形指示器（火球术爆炸）
- ✅ 扇形指示器（火焰风暴）
- ✅ 矩形指示器（剑气斩）
- ✅ 直线指示器（冲锋路径）
- ✅ 颜色区分（绿色=有效范围，红色=超出范围）
- ✅ 跟随鼠标位置

**集成方法**:
```csharp
// 在技能系统中添加
public AOEIndicatorSystem AOEIndicator;

public override void OnStart()
{
    AOEIndicator = Scene.FindScript<AOEIndicatorSystem>();
    // 或
    AOEIndicator = Actor.AddScript<AOEIndicatorSystem>();
}

// 释放AOE技能时显示范围
private void PrepareAOESkill(SkillInfo skill)
{
    // 根据技能类型显示不同形状
    if (skill.Name == "火球术")
    {
        // 圆形，半径4米，最大射程25米
        AOEIndicator.ShowIndicator(
            AOEIndicatorSystem.IndicatorShape.Circle, 
            radius: 4f, 
            maxRange: 25f
        );
    }
    else if (skill.Name == "火焰风暴")
    {
        // 扇形，半径8米，角度120度
        AOEIndicator.ShowIndicator(
            AOEIndicatorSystem.IndicatorShape.Sector, 
            radius: 8f, 
            angle: 120f, 
            maxRange: 15f
        );
    }
    else if (skill.Name == "剑气斩")
    {
        // 矩形，宽度2米，长度10米
        AOEIndicator.ShowIndicator(
            AOEIndicatorSystem.IndicatorShape.Rectangle, 
            radius: 2f,  // 宽度
            length: 10f, // 长度
            maxRange: 15f
        );
    }
}

// 玩家点击确认释放技能
private void OnSkillConfirm()
{
    if (AOEIndicator.IsShowing && AOEIndicator.IsInRange)
    {
        Vector3 targetPosition = AOEIndicator.GetIndicatorPosition();
        CastAOESkill(currentSkill, targetPosition);
        
        // 隐藏指示器
        AOEIndicator.HideIndicator();
    }
    else
    {
        Debug.LogWarning("目标位置超出范围");
    }
}

// 取消技能释放
private void OnSkillCancel()
{
    AOEIndicator.HideIndicator();
}
```

---

### 3. Buff/Debuff UI ✅
**文件**: [BuffBarUI.cs](file:///c:/Works/GitHubProjects/HundunWorld/HundunWorld/Source/Game/UI/GameMain/BuffBarUI.cs)

**功能特性**:
- ✅ 显示Buff/Debuff图标
- ✅ 显示剩余时间倒计时
- ✅ 显示效果层数
- ✅ 颜色区分（绿色边框=Buff，红色边框=Debuff）
- ✅ 时间快结束时变红提示
- ✅ 鼠标悬停显示详细信息

**集成方法**:
```csharp
// 在 HUD 或 MainUIManager 中添加
public BuffBarUI PlayerBuffBar;    // 玩家Buff栏
public BuffBarUI TargetBuffBar;    // 目标Buff栏

public override void OnStart()
{
    // 创建玩家Buff栏
    PlayerBuffBar = new BuffBarUI
    {
        Anchor = AnchorPresets.TopRight,
        Offsets = new Margin(10, 10, 300, 60),
        MaxDisplayCount = 10,
        Parent = this
    };
    
    // 创建目标Buff栏
    TargetBuffBar = new BuffBarUI
    {
        Anchor = AnchorPresets.TopCenter,
        Offsets = new Margin(0, 80, 300, 60),
        MaxDisplayCount = 8,
        Parent = this
    };
}

public override void OnUpdate()
{
    base.OnUpdate();
    
    // 更新玩家Buff显示
    var playerEffects = SkillEffectSystem.Instance.GetActiveEffects(playerEntityId);
    PlayerBuffBar.UpdateBuffs(playerEffects);
    
    // 更新目标Buff显示
    if (targetEntityId > 0)
    {
        var targetEffects = SkillEffectSystem.Instance.GetActiveEffects(targetEntityId);
        TargetBuffBar.UpdateBuffs(targetEffects);
    }
    else
    {
        TargetBuffBar.ClearBuffs();
    }
}
```

---

### 4. 战斗日志 ✅
**文件**: [CombatLogUI.cs](file:///c:/Works/GitHubProjects/HundunWorld/HundunWorld/Source/Game/UI/GameMain/CombatLogUI.cs)

**功能特性**:
- ✅ 实时记录战斗事件
- ✅ 颜色区分不同类型消息
- ✅ 时间戳显示
- ✅ 自动滚动到最新消息
- ✅ 最多保存100条记录

**集成方法**:
```csharp
// 在 HUD 中添加
public CombatLogUI CombatLog;

public override void OnStart()
{
    CombatLog = new CombatLogUI
    {
        Anchor = AnchorPresets.BottomLeft,
        Offsets = new Margin(10, -300, 400, 290),
        MaxLogEntries = 100,
        ShowTimestamp = true,
        AutoScroll = true,
        Parent = this
    };
}

// 在战斗事件中添加日志
private void OnDamageDealt(ulong attackerId, ulong targetId, float damage, bool isCritical)
{
    if (isCritical)
    {
        CombatLog.AddLog(CombatLogType.Critical, 
            $"对 {GetActorName(targetId)} 造成暴击 {damage:F0} 点伤害!");
    }
    else
    {
        CombatLog.AddLog(CombatLogType.Damage, 
            $"对 {GetActorName(targetId)} 造成 {damage:F0} 点伤害");
    }
}

private void OnBuffApplied(ulong targetId, string buffName)
{
    CombatLog.AddLog(CombatLogType.Buff, 
        $"{GetActorName(targetId)} 获得了 {buffName}");
}

private void OnDebuffApplied(ulong targetId, string debuffName)
{
    CombatLog.AddLog(CombatLogType.Debuff, 
        $"{GetActorName(targetId)} 受到 {debuffName} 效果");
}

private void OnSkillUsed(ulong casterId, string skillName)
{
    CombatLog.AddLog(CombatLogType.Skill, 
        $"{GetActorName(casterId)} 使用了 {skillName}");
}
```

---

### 5. DPS统计系统 ✅
**文件**: [DamageMeter.cs](file:///c:/Works/GitHubProjects/HundunWorld/HundunWorld/Source/Game/Combat/DamageMeter.cs)

**功能特性**:
- ✅ 实时DPS计算（10秒窗口）
- ✅ 瞬时DPS（1秒）
- ✅ 总伤害统计
- ✅ 暴击率统计
- ✅ 最高/平均伤害
- ✅ 技能伤害分布
- ✅ 技能使用次数统计

**集成方法**:
```csharp
// 已自动集成到 CombatSystemManager.cs
// 每次造成伤害时会自动调用 DamageMeter.Instance.RecordDamage()

// 获取玩家战斗统计
var stats = DamageMeter.Instance.GetStatistics(playerEntityId);
Debug.Log($"DPS: {stats.DPS:F1}, 暴击率: {stats.CriticalRate:F1}%, 最高伤害: {stats.MaxHit:F0}");

// 获取技能伤害分布
var skillBreakdown = DamageMeter.Instance.GetSkillDamageBreakdown(playerEntityId);
foreach (var kvp in skillBreakdown)
{
    Debug.Log($"{kvp.Key}: {kvp.Value:F0} 伤害");
}

// 重置统计（战斗结束时）
DamageMeter.Instance.ResetEntity(playerEntityId);
```

---

### 6. DPS显示UI ✅
**文件**: [DPSMeterUI.cs](file:///c:/Works/GitHubProjects/HundunWorld/HundunWorld/Source/Game/UI/GameMain/DPSMeterUI.cs)

**功能特性**:
- ✅ 实时显示DPS
- ✅ 瞬时DPS
- ✅ 暴击率
- ✅ 最高伤害
- ✅ 平均伤害
- ✅ 命中次数

**集成方法**:
```csharp
// 在 HUD 中添加
public DPSMeterUI DPSMeter;

public override void OnStart()
{
    DPSMeter = new DPSMeterUI
    {
        Anchor = AnchorPresets.TopLeft,
        Offsets = new Margin(10, 60, 250, 200),
        ShowDetailedStats = true,
        UpdateInterval = 0.5f,
        Parent = this
    };
    
    // 设置玩家实体ID
    DPSMeter.SetPlayerEntityId(playerEntityId);
}

// UI会自动每0.5秒更新一次显示
```

---

## 🎮 完整使用示例

### 场景1: 单体技能释放流程

```csharp
// 1. 选择目标（Tab键）
TargetSelectionSystem.Instance.SelectNextTarget();

// 2. 释放技能
private void CastSingleTargetSkill(SkillInfo skill)
{
    var target = TargetSelectionSystem.Instance.CurrentTarget;
    if (target == null)
    {
        Debug.LogWarning("请先选择目标");
        return;
    }
    
    // 创建攻击动作
    var attack = new AttackAction
    {
        AttackerId = playerEntityId,
        DefenderId = GetEntityId(target),
        Skill = skill,
        AttackPosition = target.Position
    };
    
    // 处理攻击（会自动记录DPS）
    var result = CombatSystemManager.Instance.ProcessAttack(attack);
    
    if (result.IsSuccess)
    {
        // 添加战斗日志
        if (result.DamageResult.IsCritical)
        {
            CombatLogUI.Instance.AddLog(CombatLogType.Critical, 
                $"暴击! 造成 {result.ActualDamage:F0} 点伤害!");
        }
        else
        {
            CombatLogUI.Instance.AddLog(CombatLogType.Damage, 
                $"造成 {result.ActualDamage:F0} 点伤害");
        }
    }
}
```

### 场景2: AOE技能释放流程

```csharp
// 1. 显示AOE范围指示器
private void OnAOESkillPressed(SkillInfo skill)
{
    AOEIndicatorSystem.Instance.ShowIndicator(
        AOEIndicatorSystem.IndicatorShape.Circle, 
        radius: skill.Radius, 
        maxRange: skill.Range
    );
    
    // 等待玩家点击确认
    _waitingForAOEConfirm = true;
    _currentAOESkill = skill;
}

// 2. 玩家点击鼠标左键确认
public override void OnUpdate()
{
    if (_waitingForAOEConfirm && Input.GetMouseButtonDown(MouseButton.Left))
    {
        if (AOEIndicatorSystem.Instance.IsInRange)
        {
            Vector3 targetPos = AOEIndicatorSystem.Instance.GetIndicatorPosition();
            CastAOESkill(_currentAOESkill, targetPos);
            
            // 隐藏指示器
            AOEIndicatorSystem.Instance.HideIndicator();
            _waitingForAOEConfirm = false;
        }
    }
    
    // ESC取消
    if (_waitingForAOEConfirm && Input.GetKeyDown(KeyboardKeys.Escape))
    {
        AOEIndicatorSystem.Instance.HideIndicator();
        _waitingForAOEConfirm = false;
    }
}

// 3. 释放AOE技能到指定位置
private void CastAOESkill(SkillInfo skill, Vector3 targetPosition)
{
    // 查找范围内的所有敌人
    var enemiesInRange = FindEnemiesInRadius(targetPosition, skill.Radius);
    
    foreach (var enemy in enemiesInRange)
    {
        var attack = new AttackAction
        {
            AttackerId = playerEntityId,
            DefenderId = enemy.EntityId,
            Skill = skill,
            AttackPosition = targetPosition
        };
        
        CombatSystemManager.Instance.ProcessAttack(attack);
    }
    
    // 添加战斗日志
    CombatLogUI.Instance.AddLog(CombatLogType.Skill, 
        $"使用 {skill.Name} 命中 {enemiesInRange.Count} 个目标");
}
```

---

## 🔧 关键配置项

### TargetSelectionSystem
```csharp
MaxSelectDistance = 50f;        // 最大选择距离
HighlightColor = Color.Yellow;  // 高亮颜色
ShowSelectionBox = true;        // 是否显示选择框
EnableDebugLog = false;         // 调试日志
```

### AOEIndicatorSystem
```csharp
ValidColor = Color.Green;       // 有效范围颜色
InvalidColor = Color.Red;       // 超出范围颜色
IndicatorHeightOffset = 0.1f;   // 高度偏移
CircleSegments = 32;            // 圆形分段数
```

### BuffBarUI
```csharp
IconSize = new Float2(40, 40);  // 图标大小
IconSpacing = 5f;               // 图标间距
MaxDisplayCount = 10;           // 最大显示数量
BuffBorderColor = Color.Green;  // Buff边框颜色
DebuffBorderColor = Color.Red;  // Debuff边框颜色
```

### DPSMeterUI
```csharp
UpdateInterval = 0.5f;          // 更新间隔
ShowDetailedStats = true;       // 显示详细统计
```

---

## 🎨 下一步：特效与音效资源

现在代码框架已经完成，接下来需要补充视觉和听觉资源：

### 需要的特效资源（25个Prefab）
```
Content/Game/Effects/Skills/
├── Fire/
│   ├── LieYanZhang_Burn.prefab      // 烈焰掌燃烧
│   ├── HuoQiuShu_Fireball.prefab    // 火球术飞行
│   ├── HuoQiuShu_Explosion.prefab   // 火球术爆炸
│   └── ...
├── Water/
├── Wood/
├── Metal/
└── Earth/
```

### 需要的音效资源（38个音频）
```
Content/Game/Audio/Combat/
├── SkillCasts/
│   ├── Fire_LieYanZhang.wav
│   ├── Fire_HuoQiuShu_Launch.wav
│   └── ...
├── HitSounds/
│   ├── Hit_Normal.wav
│   ├── Hit_Critical.wav
│   ├── Block.wav
│   └── Dodge.wav
└── UI/
    ├── Skill_Ready.wav
    └── Button_Click.wav
```

---

## ✅ 验收清单

- [x] 目标选择系统（Tab切换、点击选择、高亮显示）
- [x] AOE范围指示器（圆形/扇形/矩形）
- [x] Buff/Debuff UI（图标、倒计时、层数）
- [x] 战斗日志（实时记录、颜色区分）
- [x] DPS统计系统（DPS、暴击率、技能分布）
- [x] DPS显示UI（实时显示战斗数据）
- [x] 集成到CombatSystemManager（自动记录DPS）
- [ ] 特效资源（待补充）
- [ ] 音效资源（待补充）

---

**最后更新**: 2026年2月12日  
**状态**: ✅ Phase 0 代码层完成，等待资源补充
