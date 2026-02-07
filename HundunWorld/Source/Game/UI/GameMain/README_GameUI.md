# 游戏内UI系统使用指南

## 概述

本文档介绍如何使用HundunWorld游戏的四个核心UI组件：技能栏、属性条、背包和材料合成界面。

---

## 快速开始

### 1. 场景设置

在你的游戏场景中添加以下组件：

```csharp
// 在空GameObject上添加UICanvas组件（如果还没有）
var uiObject = new EmptyActor();
uiObject.Name = "GameUI";
var canvas = uiObject.AddScript<UICanvas>();

// 添加四个UI组件
uiObject.AddScript<SkillBarUI>();
uiObject.AddScript<AttributeBarsUI>();
uiObject.AddScript<InventoryUI>();
uiObject.AddScript<CraftingUI>();
```

### 2. 玩家角色设置

确保玩家角色Actor具有以下组件：

```csharp
// 玩家角色必须命名为"Player"或手动设置引用
var player = Scene.FindActor("Player");

// 添加角色属性组件
var attributes = player.AddScript<CharacterAttributesComponent>();
attributes.Level = 1;
attributes.MaxHealth = 100f;
attributes.MaxEnergy = 100f;
attributes.MaxStamina = 100f;
```

---

## 技能栏UI (SkillBarUI)

### 功能特性

- ✅ 8-10个可配置技能槽位
- ✅ 技能冷却显示（进度条+倒计时）
- ✅ 快捷键提示（1-9, 0）
- ✅ 能量消耗显示
- ✅ 五行元素配色

### 使用方法

#### 绑定技能到槽位

```csharp
// 获取技能栏UI
var skillBarUI = uiObject.GetScript<SkillBarUI>();

// 创建或获取技能
var skill = player.GetScript<JinGangZhang>();

// 绑定到槽位0（对应数字键1）
skillBarUI.BindSkillToSlot(0, skill);
```

#### 使用技能

```csharp
// 方法1：通过快捷键（自动）
// 玩家按下数字键1会自动触发槽位0的技能

// 方法2：手动调用
if (Input.GetKeyDown(KeyboardKeys.D1))
{
    skillBarUI.TryUseSkill(0);
}
```

#### 配置参数

在Inspector中可配置：
- **SkillSlotCount**: 槽位数量（默认8）
- **StartX**: 起始X位置（默认100）
- **BottomOffset**: 距离底部偏移（默认80）
- **SlotSize**: 槽位大小（默认60）
- **SlotSpacing**: 槽位间距（默认10）
- **ShowHotkeys**: 是否显示快捷键（默认true）

---

## 属性条UI (AttributeBarsUI)

### 功能特性

- ✅ 生命条（绿/黄/红分级）
- ✅ 能量条（内力/灵力/元力自适应）
- ✅ 体力条（低于30%警告）
- ✅ 平滑过渡动画
- ✅ 伤害延迟显示
- ✅ 闪烁警告效果

### 使用方法

#### 自动初始化

属性条会自动查找名为"Player"的Actor并获取CharacterAttributesComponent。

#### 手动设置

```csharp
var attributeBarsUI = uiObject.GetScript<AttributeBarsUI>();
var playerAttributes = player.GetScript<CharacterAttributesComponent>();

// 手动设置引用
attributeBarsUI.SetCharacterAttributes(playerAttributes);
```

#### 配置参数

在Inspector中可配置：
- **StartX**: 起始X位置（默认20）
- **StartY**: 起始Y位置（默认20）
- **BarWidth**: 属性条宽度（默认300）
- **BarHeight**: 属性条高度（默认24）
- **BarSpacing**: 属性条间距（默认8）
- **ShowValueText**: 显示数值文本（默认true）
- **SmoothSpeed**: 平滑过渡速度（默认5）

### 视觉效果

- **生命条颜色**:
  - > 50%：绿色
  - 25%-50%：黄色
  - < 25%：红色
- **能量名称**:
  - 武侠阶段：内力
  - 仙侠阶段：灵力
  - 玄幻阶段：元力
- **体力条**:
  - < 30%：橙色警告

---

## 背包UI (InventoryUI)

### 功能特性

- ✅ 48个槽位（8×6）
- ✅ 材料堆叠系统
- ✅ 品质边框（白/绿/蓝/紫/红）
- ✅ 五行元素配色
- ✅ 过滤功能
- ✅ 金币显示
- ✅ 快捷键切换（B键或I键）

### 使用方法

#### 添加材料

```csharp
var inventoryUI = uiObject.GetScript<InventoryUI>();

// 添加铁矿石 × 10
var ironOre = MaterialDatabase.GetMaterial(10001);
inventoryUI.AddMaterial(ironOre, 10);
```

#### 移除材料

```csharp
// 移除铁矿石 × 5
inventoryUI.RemoveMaterial(10001, 5);
```

#### 查询材料数量

```csharp
int ironCount = inventoryUI.GetMaterialCount(10001);
Debug.Log($"背包中有 {ironCount} 个铁矿石");
```

#### 打开/关闭背包

```csharp
// 切换显示
inventoryUI.ToggleInventory();

// 直接打开
inventoryUI.ShowInventory();

// 直接关闭
inventoryUI.HideInventory();

// 检查是否可见
if (inventoryUI.IsVisible)
{
    Debug.Log("背包已打开");
}
```

#### 配置参数

在Inspector中可配置：
- **ColumnCount**: 列数（默认8）
- **RowCount**: 行数（默认6）
- **SlotSize**: 槽位大小（默认50）
- **SlotSpacing**: 槽位间距（默认4）
- **WindowWidth**: 窗口宽度（默认500）
- **WindowHeight**: 窗口高度（默认450）

### 测试材料

背包会自动加载以下测试材料：
- 铁矿石 × 25
- 青竹 × 18
- 寒泉水 × 10

---

## 材料合成UI (CraftingUI)

### 功能特性

- ✅ 配方列表显示
- ✅ 材料需求实时检测
- ✅ 产出预览
- ✅ 单次合成/一键合成
- ✅ 成功率显示
- ✅ 货币消耗计算
- ✅ 快捷键切换（C键）

### 使用方法

#### 打开合成界面

```csharp
var craftingUI = uiObject.GetScript<CraftingUI>();

// 按C键或手动调用
craftingUI.ShowCrafting();
```

#### 合成流程

1. **打开合成界面**：按C键
2. **选择配方**：点击左侧配方列表
3. **检查材料**：查看右侧材料需求（绿色✓表示足够，红色✗表示不足）
4. **查看产出**：查看产出预览面板
5. **设置数量**：输入合成数量或点击"一键合成"
6. **点击合成**：点击"合成"按钮

#### 配置参数

在Inspector中可配置：
- **WindowWidth**: 窗口宽度（默认700）
- **WindowHeight**: 窗口高度（默认500）
- **RecipeListWidth**: 配方列表宽度（默认250）

### 合成规则

- **5:1合成**：5个初级材料 → 1个中级材料
- **3:1合成**：3个高级材料 → 1个仙级材料
- **成功率**：大部分配方100%成功
- **货币消耗**：每次合成消耗一定金币

---

## 完整示例

### 场景初始化脚本

```csharp
using FlaxEngine;
using HundunWorld.Game.UI.GameMain;
using Game.Character.Attributes;
using Game.Combat.Skills;
using Game.Equipment.Material;

public class GameUISetup : Script
{
    public override void OnStart()
    {
        // 创建UI容器
        var uiObject = new EmptyActor();
        uiObject.Name = "GameUI";
        var canvas = uiObject.AddScript<UICanvas>();

        // 添加UI组件
        var skillBarUI = uiObject.AddScript<SkillBarUI>();
        var attributeBarsUI = uiObject.AddScript<AttributeBarsUI>();
        var inventoryUI = uiObject.AddScript<InventoryUI>();
        var craftingUI = uiObject.AddScript<CraftingUI>();

        // 查找玩家
        var player = Scene.FindActor("Player");
        if (player == null)
        {
            Debug.LogWarning("未找到玩家Actor");
            return;
        }

        // 设置角色属性
        var attributes = player.GetOrAddScript<CharacterAttributesComponent>();
        attributes.Level = 1;
        attributes.MaxHealth = 100f;
        attributes.CurrentHealth = 100f;
        attributes.MaxEnergy = 100f;
        attributes.CurrentEnergy = 100f;

        // 绑定技能
        var skill1 = player.GetOrAddScript<JinGangZhang>();
        skillBarUI.BindSkillToSlot(0, skill1);

        // 添加测试材料
        var ironOre = MaterialDatabase.GetMaterial(10001);
        inventoryUI.AddMaterial(ironOre, 25);

        Debug.Log("游戏UI初始化完成");
    }
}
```

---

## 快捷键总览

| 快捷键 | 功能 | 组件 |
|-------|------|------|
| 1-9, 0 | 使用技能槽位 | SkillBarUI |
| B 或 I | 打开/关闭背包 | InventoryUI |
| C | 打开/关闭合成界面 | CraftingUI |

---

## 常见问题

### Q1: 技能栏不显示技能？

**解决方法**：
1. 确保技能已绑定到槽位：`skillBarUI.BindSkillToSlot(index, skill)`
2. 检查技能的SkillData是否正确配置
3. 确保CharacterAttributesComponent已添加到玩家

### Q2: 属性条不更新？

**解决方法**：
1. 确保玩家Actor命名为"Player"或手动设置引用
2. 检查CharacterAttributesComponent是否正确初始化
3. 使用`SetCharacterAttributes()`手动设置引用

### Q3: 背包无法添加材料？

**解决方法**：
1. 检查材料是否存在：`MaterialDatabase.GetMaterial(materialId)`
2. 确保背包未满（48个槽位）
3. 检查材料堆叠上限

### Q4: 合成界面没有配方？

**解决方法**：
1. 确保MaterialCraftingSystem已添加到场景
2. 检查合成系统是否正确初始化
3. 验证配方是否已加载：`craftingSystem.GetAllRecipes()`

### Q5: 快捷键不起作用？

**解决方法**：
1. 确保UI组件的OnUpdate()正在执行
2. 检查是否有其他输入系统拦截了按键
3. 验证UICanvas的GUI是否可见

---

## 性能优化建议

### 技能栏
- 仅在有技能绑定时更新槽位
- 冷却检测通过SkillBase直接获取，避免重复计算

### 属性条
- 使用Lerp平滑过渡，避免突变
- 闪烁效果通过定时器控制，不消耗多余资源

### 背包
- 递归堆叠算法优化材料添加
- 仅在显示时更新所有槽位

### 合成界面
- 配方列表一次性加载
- 材料需求仅在显示且选中配方时实时更新

---

## 扩展开发

### 添加新的技能槽位

```csharp
// 在Inspector中修改SkillSlotCount
skillBarUI.SkillSlotCount = 10;  // 增加到10个槽位
```

### 自定义材料图标

```csharp
// 在MaterialData中添加图标纹理引用
public class MaterialData
{
    public Texture IconTexture;  // 添加此字段
    // ... 其他字段
}

// 在InventoryUI中加载图标
slot.IconImage.Brush = new TextureBrush(material.IconTexture);
```

### 添加新的合成配方

```csharp
// 在MaterialCraftingSystem中添加配方
craftingSystem.AddTierUpgradeRecipe(
    recipeId: 1003,
    recipeName: "提炼寒泉水",
    inputMaterialId: 10003,
    inputCount: 5,
    outputMaterialId: 20003,
    currencyCost: 50
);
```

---

## 调试工具

### 技能栏调试

```csharp
// 获取槽位绑定的技能
var skill = skillBarUI.GetBoundSkill(0);
if (skill != null)
{
    Debug.Log($"槽位0绑定了技能: {skill.Data.SkillName}");
    Debug.Log($"冷却进度: {skill.GetCooldownProgress() * 100}%");
    Debug.Log($"是否就绪: {skill.IsReady()}");
}
```

### 背包调试

```csharp
// 获取所有材料数量
for (int id = 10001; id <= 10005; id++)
{
    int count = inventoryUI.GetMaterialCount(id);
    var material = MaterialDatabase.GetMaterial(id);
    Debug.Log($"{material?.MaterialName}: {count}");
}
```

### 合成系统调试

```csharp
// 列出所有配方
var recipes = craftingSystem.GetAllRecipes();
foreach (var recipe in recipes)
{
    Debug.Log($"配方: {recipe.RecipeName}, 成功率: {recipe.SuccessRate}%");
}
```

---

## 总结

本UI系统提供了完整的游戏内界面解决方案，包括：
- 直观的技能管理和释放
- 实时的角色属性显示
- 完善的背包和物品管理
- 友好的材料合成界面

所有组件都经过性能优化，支持自定义配置，易于扩展。按照本指南即可快速集成到你的游戏项目中。
