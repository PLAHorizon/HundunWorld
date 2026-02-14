# MetaHuman 角色外观编辑系统 - 使用手册

**版本**: 1.0  
**更新日期**: 2026-02-13  
**适用平台**: Flax Engine 游戏引擎  

---

## 📑 目录

1. [系统概述](#系统概述)
2. [快速开始](#快速开始)
3. [编辑器界面详解](#编辑器界面详解)
4. [皮肤编辑](#皮肤编辑)
5. [眼睛编辑](#眼睛编辑)
6. [毛发编辑](#毛发编辑)
7. [预设管理](#预设管理)
8. [预览控制](#预览控制)
9. [高级功能](#高级功能)
10. [API参考](#api参考)
11. [常见问题](#常见问题)
12. [最佳实践](#最佳实践)

---

## 系统概述

### 系统简介

MetaHuman 角色外观编辑系统是一个专业级的角色外观定制工具，提供：

- 🎨 **精细化参数控制**: 85+ 可调参数
- 👤 **三大编辑模块**: 皮肤、眼睛、毛发
- 📦 **预设管理系统**: 保存/加载/快速应用
- 👁️ **实时3D预览**: 所见即所得
- ⚡ **性能优化**: LOD和自适应质量

### 系统架构

```
MetaHuman 编辑系统
│
├─ 核心渲染层
│  ├─ MetaHumanCharacterRenderer      (渲染管理)
│  ├─ SkinMaterialController          (皮肤材质)
│  ├─ EyeMaterialController           (眼睛材质)
│  └─ HairMaterialController          (毛发材质)
│
├─ 编辑器层
│  ├─ CharacterAppearanceEditor       (统一管理)
│  └─ CharacterAppearancePreviewController (预览控制)
│
└─ UI层
   ├─ MetaHumanEditorUI               (主界面)
   ├─ SkinEditorPanel                 (皮肤面板)
   ├─ EyeEditorPanel                  (眼睛面板)
   ├─ HairEditorPanel                 (毛发面板)
   └─ PresetManagerPanel              (预设管理)
```

---

## 快速开始

### 步骤1: 场景设置

#### 1.1 创建角色Actor

```
1. 在Flax编辑器中创建新的Actor
2. 命名为 "MetaHumanCharacter"
3. 添加AnimatedModel组件（可选）
```

#### 1.2 添加材质控制器脚本

**必需的脚本组件**:

```csharp
// 添加到角色Actor或其子对象
- SkinMaterialController    // 皮肤材质控制
- EyeMaterialController     // 眼睛材质控制
- HairMaterialController    // 毛发材质控制
```

**添加方法**:
1. 选中Actor
2. 点击 "Add Script"
3. 搜索并添加上述三个控制器

#### 1.3 添加编辑器脚本

```csharp
// 添加到角色Actor
- CharacterAppearanceEditor              // 外观编辑器
- CharacterAppearancePreviewController   // 预览控制器（可选）
- MetaHumanCharacterRenderer            // 渲染管理器（可选）
```

---

### 步骤2: 创建UI

#### 2.1 创建UI Canvas

```
1. 创建新的 UICanvas Actor
2. 设置为全屏模式
3. 设置渲染模式为 ScreenSpace
```

#### 2.2 添加编辑器UI脚本

```csharp
// 添加到UICanvas
- MetaHumanEditorUI
```

#### 2.3 连接引用

在 `MetaHumanEditorUI` 的属性面板中：

```
AppearanceEditor    → 拖入 CharacterAppearanceEditor 组件
PreviewController   → 拖入 CharacterAppearancePreviewController 组件
```

---

### 步骤3: 初始化编辑器

#### 代码初始化

```csharp
// 在游戏代码中初始化
public override void OnStart()
{
    // 获取编辑器UI
    var editorUI = UICanvas.GetScript<MetaHumanEditorUI>();
    
    // 初始化UI
    editorUI.Initialize();
    
    // 设置目标角色
    editorUI.SetTargetCharacter(targetCharacterActor);
}
```

#### 手动初始化

```
1. 运行游戏
2. 编辑器UI自动初始化
3. 开始编辑角色外观
```

---

## 编辑器界面详解

### 主界面布局

```
┌─────────────────────────────────────────────────────┐
│  [预设管理栏]                                       │
├─────────────────┬───────────────────────────────────┤
│  [皮肤][眼睛][毛发] │                                 │
├─────────────────┤         3D 预览区                 │
│                 │                                     │
│                 │                                     │
│   参数编辑区    │      (实时显示角色)                │
│                 │                                     │
│   - 滑块        │                                     │
│   - 颜色选择器  │                                     │
│   - 快速预设    │                                     │
│                 ├───────────────────────────────────┤
│                 │ [面部特写][上半身][全身] [自动旋转] │
└─────────────────┴───────────────────────────────────┘
```

### UI元素说明

| 元素 | 位置 | 功能 |
|------|------|------|
| 预设管理栏 | 顶部 | 加载/保存预设 |
| 标签栏 | 左上 | 切换编辑面板 |
| 参数编辑区 | 左侧 | 调整各项参数 |
| 3D预览区 | 右侧 | 实时预览效果 |
| 预览控制栏 | 右下 | 切换视角/截图 |

---

## 皮肤编辑

### 参数分类

#### 1. 基础参数

**基肤色** (BaseColor)
- **作用**: 设置皮肤的主要颜色
- **范围**: RGB颜色
- **推荐值**: 
  - 亚洲: (0.92, 0.78, 0.65)
  - 欧洲: (1.0, 0.87, 0.78)
  - 非洲: (0.45, 0.35, 0.28)

**粗糙度** (Roughness)
- **作用**: 控制皮肤表面的粗糙程度
- **范围**: 0.0 - 1.0
- **推荐值**:
  - 年轻: 0.28 - 0.32
  - 成熟: 0.38 - 0.45
  - 油性: 0.15 - 0.25

**金属度** (Metallic)
- **作用**: 控制金属感（通常保持为0）
- **范围**: 0.0 - 1.0
- **推荐值**: 0.0（皮肤不应有金属感）

---

#### 2. SSS次表面散射参数

> **什么是SSS？**  
> SSS（Subsurface Scattering）模拟光线穿透皮肤表层，在皮下散射后透出的效果，是实现真实皮肤外观的关键。

**SSS强度** (SSSIntensity)
- **作用**: 控制次表面散射的整体强度
- **范围**: 0.0 - 2.0
- **推荐值**:
  - 年轻: 1.0 - 1.2
  - 成熟: 0.8 - 1.0
  - 厚皮肤: 0.6 - 0.8

**三层皮肤结构**

```
光线
  ↓
┌─────────────┐
│  表皮层     │ ← Epidermis Color (浅色，偏黄)
├─────────────┤
│  真皮层     │ ← Dermis Color (中等，偏红)
├─────────────┤
│  皮下组织   │ ← Subcutis Color (深色，偏红)
└─────────────┘
```

**表皮层颜色** (EpidermisColor)
- **推荐值**: (1.0, 0.9, 0.85) - 浅黄色
- **厚度**: 0.5 - 1.0

**真皮层颜色** (DermisColor)
- **推荐值**: (0.9, 0.5, 0.4) - 偏红色
- **厚度**: 1.0 - 2.0

**皮下组织颜色** (SubcutisColor)
- **推荐值**: (0.8, 0.3, 0.25) - 深红色
- **厚度**: 2.0 - 4.0

**散射半径** (ScatterRadius)
- **作用**: 控制光线在皮下的散射距离
- **范围**: 0.0 - 5.0
- **推荐值**: 1.0 - 1.5

---

#### 3. 皮肤细节参数

**毛孔强度** (PoreIntensity)
- **作用**: 控制毛孔的可见程度
- **范围**: 0.0 - 2.0
- **推荐值**:
  - 年轻细腻: 0.3 - 0.5
  - 正常: 0.5 - 0.7
  - 粗糙: 0.7 - 1.0

**毛孔缩放** (PoreScale)
- **作用**: 调整毛孔的大小
- **范围**: 0.1 - 5.0
- **推荐值**: 0.8 - 1.5

**皱纹强度** (WrinkleIntensity)
- **作用**: 控制皱纹的深度
- **范围**: 0.0 - 2.0
- **推荐值**:
  - 年轻: 0.0 - 0.2
  - 中年: 0.3 - 0.5
  - 老年: 0.6 - 1.0

**细节法线强度** (DetailNormalStrength)
- **作用**: 控制细节法线贴图的强度
- **范围**: 0.0 - 2.0
- **推荐值**: 0.4 - 0.7

---

#### 4. 皮肤特征参数

**油光强度** (OilIntensity)
- **作用**: 模拟皮肤油脂的反光
- **范围**: 0.0 - 2.0
- **推荐值**:
  - 干性: 0.0 - 0.2
  - 正常: 0.2 - 0.4
  - 油性: 0.4 - 0.8

**雀斑强度** (FreckleIntensity)
- **作用**: 控制雀斑的可见度
- **范围**: 0.0 - 1.0
- **推荐值**: 0.0（无）- 0.5（明显）

**雀斑颜色** (FreckleColor)
- **推荐值**: (0.6, 0.4, 0.3) - 浅棕色

**血管强度** (VeinIntensity)
- **作用**: 控制皮下血管的可见度
- **范围**: 0.0 - 1.0
- **推荐值**: 0.1 - 0.3（薄皮肤区域）

---

### 快速预设使用

#### 种族预设

**亚洲皮肤**
```
基肤色: (0.92, 0.78, 0.65)
粗糙度: 0.38
SSS强度: 1.0
毛孔强度: 0.5
油光强度: 0.35
```

**欧洲皮肤**
```
基肤色: (1.0, 0.87, 0.78)
粗糙度: 0.32
SSS强度: 1.1
毛孔强度: 0.65
油光强度: 0.25
```

#### 年龄预设

**年轻皮肤**
```
基肤色: (1.0, 0.9, 0.82)
粗糙度: 0.28
SSS强度: 1.15
毛孔强度: 0.4
皱纹强度: 0.1
```

**成熟皮肤**
```
基肤色: (0.95, 0.82, 0.72)
粗糙度: 0.42
SSS强度: 0.9
毛孔强度: 0.75
皱纹强度: 0.6
```

---

## 眼睛编辑

### 眼睛解剖结构

```
           角膜（透明）
              ↓
        ┌──────────┐
        │          │ ← 巩膜（白色部分）
    ┌───┤  ┌────┐  │
    │   │  │瞳孔│  │
    │虹膜 │  └────┘  │
    │   │          │
    └───┤          │
        └──────────┘
```

### 参数分类

#### 1. 虹膜参数

**虹膜主色** (IrisColor)
- **作用**: 虹膜的主要颜色
- **常见颜色**:
  - 棕色: (0.45, 0.28, 0.15)
  - 蓝色: (0.25, 0.45, 0.75)
  - 绿色: (0.28, 0.52, 0.32)
  - 灰色: (0.48, 0.5, 0.53)

**虹膜次色** (IrisSecondaryColor)
- **作用**: 虹膜的次要颜色（用于纹理变化）
- **推荐**: 比主色稍深的同色系

**虹膜细节** (IrisDetail)
- **作用**: 控制虹膜纹理的细节程度
- **范围**: 0.0 - 2.0
- **推荐值**: 0.8 - 1.2

**纹理强度** (IrisPatternIntensity)
- **作用**: 控制虹膜纹理的对比度
- **范围**: 0.0 - 2.0
- **推荐值**: 0.8 - 1.5

**虹膜半径** (IrisRadius)
- **作用**: 控制虹膜的大小
- **范围**: 0.3 - 0.6
- **推荐值**: 0.4 - 0.5

---

#### 2. 角膜缘参数

> **什么是角膜缘？**  
> 角膜缘（Limbus）是虹膜外圈的深色边缘，增强眼睛的立体感和真实感。

**角膜缘强度** (LimbusIntensity)
- **范围**: 0.0 - 2.0
- **推荐值**:
  - 年轻: 1.0 - 1.5
  - 成熟: 0.5 - 1.0

**角膜缘宽度** (LimbusWidth)
- **范围**: 0.0 - 0.1
- **推荐值**: 0.015 - 0.025

**角膜缘颜色** (LimbusColor)
- **推荐值**: (0.1, 0.1, 0.12) - 深灰色

---

#### 3. 瞳孔参数

**瞳孔大小** (PupilSize)
- **作用**: 控制瞳孔的直径
- **范围**: 0.1 - 0.8
- **推荐值**:
  - 明亮环境: 0.2 - 0.35
  - 正常环境: 0.35 - 0.5
  - 黑暗环境: 0.5 - 0.7

**瞳孔反应性** (PupilReactivity)
- **作用**: 控制瞳孔对光线的反应速度
- **范围**: 0.0 - 1.0
- **推荐值**: 0.4 - 0.6

**瞳孔颜色** (PupilColor)
- **推荐值**: (0.02, 0.02, 0.02) - 接近黑色

---

#### 4. 巩膜参数

**巩膜颜色** (ScleraColor)
- **作用**: 眼白的颜色
- **推荐值**:
  - 健康: (1.0, 0.98, 0.95)
  - 疲劳: (0.95, 0.93, 0.88)
  - 充血: (0.98, 0.92, 0.88)

**血丝强度** (ScleraVeinIntensity)
- **范围**: 0.0 - 2.0
- **推荐值**:
  - 清澈: 0.1 - 0.3
  - 正常: 0.3 - 0.5
  - 充血: 0.5 - 1.0

**血丝颜色** (ScleraVeinColor)
- **推荐值**: (0.8, 0.2, 0.15) - 红色

**边缘暗化** (ScleraDarkening)
- **作用**: 眼球边缘的阴影效果
- **范围**: 0.0 - 1.0
- **推荐值**: 0.15 - 0.25

---

#### 5. 角膜参数

**折射率** (CorneaIOR)
- **作用**: 角膜的折射指数
- **范围**: 1.0 - 1.5
- **推荐值**: 1.376（真实人眼）

**角膜粗糙度** (CorneaRoughness)
- **范围**: 0.0 - 0.3
- **推荐值**: 0.01 - 0.03（非常光滑）

**表面凹凸** (CorneaBump)
- **范围**: 0.0 - 1.0
- **推荐值**: 0.05 - 0.15

**角膜厚度** (CorneaThickness)
- **范围**: 0.0 - 0.1
- **推荐值**: 0.04 - 0.06

---

#### 6. 眼球效果

**湿润度** (Wetness)
- **作用**: 眼球表面的湿润光泽
- **范围**: 0.0 - 1.0
- **推荐值**: 0.7 - 0.9

**遮蔽强度** (OcclusionStrength)
- **作用**: 环境光遮蔽效果
- **范围**: 0.0 - 1.0
- **推荐值**: 0.4 - 0.6

**视差深度** (ParallaxDepth)
- **作用**: 虹膜的深度视差效果
- **范围**: 0.0 - 0.5
- **推荐值**: 0.1 - 0.2

**焦散强度** (CausticsIntensity)
- **作用**: 光线聚焦产生的亮点
- **范围**: 0.0 - 1.0
- **推荐值**: 0.2 - 0.4

---

### 眼睛颜色快速预设

#### 蓝色眼睛
```
虹膜主色: (0.25, 0.45, 0.75)
虹膜次色: (0.15, 0.3, 0.55)
虹膜细节: 1.0
角膜缘强度: 1.2
```

#### 棕色眼睛
```
虹膜主色: (0.45, 0.28, 0.15)
虹膜次色: (0.3, 0.18, 0.08)
虹膜细节: 0.8
角膜缘强度: 0.8
```

#### 绿色眼睛
```
虹膜主色: (0.28, 0.52, 0.32)
虹膜次色: (0.35, 0.42, 0.25)
虹膜细节: 1.1
角膜缘强度: 1.0
```

#### 灰色眼睛
```
虹膜主色: (0.48, 0.5, 0.53)
虹膜次色: (0.38, 0.4, 0.45)
虹膜细节: 0.7
角膜缘强度: 0.9
```

---

## 毛发编辑

### 毛发渲染原理

```
光线
  ↓
┌─────────┐
│ 发尖    │ ← Tip Color（通常较浅）
├─────────┤
│         │
│ 发干    │ ← Base Color
│         │
├─────────┤
│ 发根    │ ← Root Color（通常较深）
└─────────┘
  ↑
黑色素 + 红色素
```

### 参数分类

#### 1. 发色参数

**基础发色** (BaseColor)
- **作用**: 头发的主要颜色
- **常见颜色**:
  - 黑色: (0.03, 0.03, 0.03)
  - 棕色: (0.35, 0.2, 0.12)
  - 金色: (0.85, 0.7, 0.45)
  - 红色: (0.55, 0.18, 0.08)

**发梢颜色** (TipColor)
- **作用**: 发尖的颜色（通常比基色浅）
- **推荐**: 比基色亮10-20%

**发根颜色** (RootColor)
- **作用**: 发根的颜色（通常比基色深）
- **推荐**: 比基色暗10-20%

**颜色变化** (ColorVariation)
- **作用**: 头发颜色的随机变化
- **范围**: 0.0 - 1.0
- **推荐值**: 0.05 - 0.15

---

#### 2. 黑色素参数

> **黑色素模型**  
> 使用真实的黑色素和红色素比例来模拟自然发色。

**黑色素** (Melanin)
- **作用**: 控制头发的深浅程度
- **范围**: 0.0 - 1.0
- **推荐值**:
  - 金色: 0.1 - 0.3
  - 棕色: 0.4 - 0.7
  - 黑色: 0.8 - 1.0

**红色素** (Pheomelanin)
- **作用**: 控制头发的红色调
- **范围**: 0.0 - 1.0
- **推荐值**:
  - 黑色/棕色: 0.1 - 0.3
  - 红色: 0.7 - 0.9
  - 金色: 0.3 - 0.5

---

#### 3. 高光参数

**各向异性** (Anisotropy)
- **作用**: 控制沿发丝方向的高光延伸
- **范围**: 0.0 - 1.0
- **推荐值**: 0.8 - 0.95（头发特有）

**粗糙度** (Roughness)
- **作用**: 控制高光的锐利程度
- **范围**: 0.0 - 1.0
- **推荐值**:
  - 光滑（湿发）: 0.3 - 0.4
  - 正常: 0.4 - 0.5
  - 粗糙（干发）: 0.5 - 0.7

**主高光强度** (PrimarySpecularIntensity)
- **作用**: 第一层高光的强度
- **范围**: 0.0 - 2.0
- **推荐值**: 0.8 - 1.2

**主高光颜色** (PrimarySpecularColor)
- **推荐值**: (1.0, 0.95, 0.9) - 暖白色

**次高光强度** (SecondarySpecularIntensity)
- **作用**: 第二层高光的强度
- **范围**: 0.0 - 2.0
- **推荐值**: 0.3 - 0.7

**次高光颜色** (SecondarySpecularColor)
- **推荐值**: (0.9, 0.85, 0.75) - 偏暖

**高光偏移** (SpecularShift)
- **作用**: 调整高光位置
- **范围**: -1.0 - 1.0
- **推荐值**: -0.1 - 0.1

---

#### 4. 散射参数

**散射强度** (ScatterIntensity)
- **作用**: 光线穿透头发的散射效果
- **范围**: 0.0 - 2.0
- **推荐值**:
  - 深色头发: 0.5 - 0.8
  - 浅色头发: 0.8 - 1.5

**散射颜色** (ScatterColor)
- **推荐值**: 接近发色但偏暖

**透射** (Transmission)
- **作用**: 背光透射效果
- **范围**: 0.0 - 1.0
- **推荐值**: 0.2 - 0.4

**背散射** (Backscatter)
- **作用**: 背向散射强度
- **范围**: 0.0 - 1.0
- **推荐值**: 0.1 - 0.3

---

#### 5. 细节参数

**发丝粗细** (StrandThickness)
- **范围**: 0.01 - 0.2
- **推荐值**:
  - 细发: 0.03 - 0.05
  - 正常: 0.05 - 0.08
  - 粗发: 0.08 - 0.12

**发丝粗糙度** (StrandRoughness)
- **范围**: 0.0 - 1.0
- **推荐值**: 0.2 - 0.4

**AO强度** (AOIntensity)
- **作用**: 环境光遮蔽
- **范围**: 0.0 - 2.0
- **推荐值**: 0.8 - 1.2

**阴影强度** (ShadowIntensity)
- **范围**: 0.0 - 2.0
- **推荐值**: 0.8 - 1.2

---

#### 6. 动态效果参数

**风力响应** (WindResponse)
- **作用**: 头发对风力的响应程度
- **范围**: 0.0 - 2.0
- **推荐值**: 0.8 - 1.2

**重力影响** (Gravity)
- **范围**: 0.0 - 2.0
- **推荐值**: 0.8 - 1.2

**刚度** (Stiffness)
- **作用**: 头发的硬度
- **范围**: 0.0 - 1.0
- **推荐值**:
  - 柔软: 0.2 - 0.4
  - 正常: 0.4 - 0.6
  - 坚硬: 0.6 - 0.8

---

### 发色快速预设

#### 黑色头发
```
基础色: (0.03, 0.03, 0.03)
发梢色: (0.05, 0.05, 0.05)
发根色: (0.02, 0.02, 0.02)
黑色素: 0.95
红色素: 0.1
粗糙度: 0.5
各向异性: 0.8
散射强度: 0.6
```

#### 金色头发
```
基础色: (0.85, 0.7, 0.45)
发梢色: (0.9, 0.78, 0.55)
发根色: (0.7, 0.55, 0.35)
黑色素: 0.2
红色素: 0.4
粗糙度: 0.4
各向异性: 0.9
散射强度: 1.2
```

#### 棕色头发
```
基础色: (0.35, 0.2, 0.12)
发梢色: (0.4, 0.25, 0.15)
发根色: (0.25, 0.15, 0.08)
黑色素: 0.7
红色素: 0.3
粗糙度: 0.45
各向异性: 0.85
散射强度: 0.8
```

#### 红色头发
```
基础色: (0.55, 0.18, 0.08)
发梢色: (0.65, 0.25, 0.12)
发根色: (0.4, 0.12, 0.05)
黑色素: 0.4
红色素: 0.85
粗糙度: 0.42
各向异性: 0.88
散射强度: 1.0
```

---

## 预设管理

### 预设文件结构

#### JSON格式

```json
{
  "PresetName": "MyCharacter",
  "Description": "自定义角色外观",
  "CreatedDate": "2026-02-13 14:30:00",
  "Skin": {
    "BaseColor": { "R": 1.0, "G": 0.85, "B": 0.75 },
    "Roughness": 0.35,
    "SSSIntensity": 1.0,
    // ... 更多参数
  },
  "Eye": {
    "IrisColor": { "R": 0.3, "G": 0.5, "B": 0.7 },
    "PupilSize": 0.35,
    // ... 更多参数
  },
  "Hair": {
    "BaseColor": { "R": 0.15, "G": 0.1, "B": 0.08 },
    "Roughness": 0.45,
    // ... 更多参数
  }
}
```

---

### 保存预设

#### UI方式

```
1. 在预设管理栏中输入预设名称
2. 点击"保存"按钮
3. 预设自动保存到 Content/Presets/Characters/
```

#### 代码方式

```csharp
// 保存当前外观为预设
var editor = Actor.GetScript<CharacterAppearanceEditor>();
string filePath = "Content/Presets/Characters/MyPreset.json";
editor.SavePreset(filePath, "MyPreset");
```

---

### 加载预设

#### UI方式

```
1. 在预设下拉框中选择预设
2. 点击"加载"按钮
3. 预设自动应用到角色
```

#### 代码方式

```csharp
// 加载预设文件
var editor = Actor.GetScript<CharacterAppearanceEditor>();
string filePath = "Content/Presets/Characters/MyPreset.json";
editor.LoadPreset(filePath);
```

---

### 快速预设

#### 使用内置预设

```csharp
// 皮肤预设
editor.ApplyYoungSkinPreset();
editor.ApplyMatureSkinPreset();
editor.ApplyOilySkinPreset();
editor.ApplyDrySkinPreset();

// 眼睛预设
editor.ApplyBrownEyePreset();
editor.ApplyBlueEyePreset();
editor.ApplyGreenEyePreset();

// 毛发预设
editor.ApplyBlackHairPreset();
editor.ApplyBrownHairPreset();
editor.ApplyBlondeHairPreset();
editor.ApplyRedHairPreset();
editor.ApplyWhiteHairPreset();
```

---

### 预设目录结构

```
Content/
└── Presets/
    └── Characters/
        ├── Default.json           (默认预设)
        ├── Asian_Young.json       (亚洲年轻)
        ├── European_Mature.json   (欧洲成熟)
        ├── Custom/                (用户自定义)
        │   ├── Hero1.json
        │   └── NPC_Guard.json
        └── Templates/             (模板)
            ├── Male_Template.json
            └── Female_Template.json
```

---

## 预览控制

### 预览模式

#### 面部特写模式
- **用途**: 查看面部细节（皮肤、眼睛）
- **相机距离**: 0.8米
- **高度偏移**: 1.6米（头部高度）
- **俯仰角**: -5°
- **景深**: 启用
- **自动旋转**: 禁用

#### 上半身模式
- **用途**: 查看面部和上身（默认模式）
- **相机距离**: 1.5米
- **高度偏移**: 1.2米
- **俯仰角**: -10°
- **景深**: 启用
- **自动旋转**: 启用

#### 全身模式
- **用途**: 查看整体外观
- **相机距离**: 3.0米
- **高度偏移**: 0.8米
- **俯仰角**: -15°
- **景深**: 禁用
- **自动旋转**: 启用

---

### 相机控制

#### 手动控制

```
鼠标左键拖动: 旋转视角
鼠标滚轮: 缩放距离
```

#### 代码控制

```csharp
var previewController = Actor.GetScript<CharacterAppearancePreviewController>();

// 设置相机距离
previewController.CameraDistance = 2.0f;

// 设置相机高度
previewController.CameraHeightOffset = 1.0f;

// 设置俯仰角
previewController.CameraPitch = -10f;

// 重置视角
previewController.ResetCameraView();
```

---

### 光照控制

#### 三点光照系统

```
       轮廓光 (Rim Light)
           ↓
          角色
        ↙     ↘
   主光源      补光
(Key Light)  (Fill Light)
```

**主光源** (Key Light)
- 亮度: 3.0
- 角度: 45° 侧向，45° 俯角
- 色温: 5500K（日光）

**补光** (Fill Light)
- 亮度: 1.5
- 位置: 主光对侧
- 色温: 6000K

**轮廓光** (Rim Light)
- 亮度: 2.0
- 位置: 背后上方
- 色温: 7000K（略冷）

---

### 自动旋转

#### UI控制

```
勾选"自动旋转"复选框
```

#### 代码控制

```csharp
var previewController = Actor.GetScript<CharacterAppearancePreviewController>();

// 启用自动旋转
previewController.EnableAutoRotation = true;

// 设置旋转速度（度/秒）
previewController.AutoRotationSpeed = 15f;

// 设置旋转角度
previewController.SetCharacterRotation(45f);
```

---

### 截图功能

#### UI方式

```
点击"截图"按钮
图片自动保存到 Screenshots/ 目录
文件名: MetaHuman_Capture_YYYYMMDD_HHmmss.png
```

#### 代码方式

```csharp
var previewController = Actor.GetScript<CharacterAppearancePreviewController>();

// 截取预览图
string filePath = "Screenshots/MyCharacter.png";
previewController.CapturePreviewImage(filePath, 1024, 1024);
```

---

## 高级功能

### 性能优化

#### LOD系统配置

```csharp
var renderer = Actor.GetScript<MetaHumanCharacterRenderer>();

// 启用LOD
renderer.EnableLOD = true;

// 设置LOD距离
renderer.HighLODDistance = 5f;    // 高质量距离
renderer.MediumLODDistance = 15f; // 中等质量距离
renderer.LowLODDistance = 30f;    // 低质量距离

// LOD偏移
renderer.LODBias = 0f; // -2 到 +2
```

---

#### 自适应质量

```csharp
// 启用自适应质量
renderer.AdaptiveQuality = true;

// 设置目标帧率
renderer.TargetFrameRate = 60; // 30-144

// 当前质量等级
var quality = renderer.Quality;
// Low, Medium, High, Ultra, Custom
```

---

### 实时更新控制

```csharp
var editor = Actor.GetScript<CharacterAppearanceEditor>();

// 启用实时更新
editor.EnableRealtimeUpdate = true;

// 设置更新间隔（秒）
editor.UpdateInterval = 0.05f; // 0.016-0.5

// 手动刷新所有材质
editor.RefreshAllMaterials();
```

---

### 事件订阅

```csharp
var editor = Actor.GetScript<CharacterAppearanceEditor>();

// 订阅外观变化事件
editor.OnAppearanceChanged += (preset) =>
{
    Debug.Log($"外观已更新: {preset.PresetName}");
};

// 订阅预设加载事件
editor.OnPresetLoaded += (path) =>
{
    Debug.Log($"预设已加载: {path}");
};

// 订阅皮肤变化事件
editor.OnSkinChanged += () =>
{
    Debug.Log("皮肤参数已更新");
};

// 订阅眼睛变化事件
editor.OnEyeChanged += () =>
{
    Debug.Log("眼睛参数已更新");
};

// 订阅毛发变化事件
editor.OnHairChanged += () =>
{
    Debug.Log("毛发参数已更新");
};
```

---

## API参考

### CharacterAppearanceEditor

#### 核心方法

```csharp
// 预设管理
public bool LoadPreset(string filePath);
public bool SavePreset(string filePath, string presetName);
public void ApplyPreset(CharacterAppearancePreset preset);
public CharacterAppearancePreset CaptureCurrentAppearance();

// 控制器绑定
public void AutoBindControllers();
public void RefreshAllMaterials();

// 皮肤参数设置
public void SetSkinBaseColor(Color color);
public void SetSkinRoughness(float roughness);
public void SetSkinSSSIntensity(float intensity);
public void SetSkinSSSRadius(float radius);
public void SetSkinEpidermisColor(Color color);
public void SetSkinDermisColor(Color color);
public void SetSkinSubcutisColor(Color color);
public void SetSkinLipColor(Color color);
public void SetSkinBlushIntensity(float intensity);

// 眼睛参数设置
public void SetEyeIrisColor(Color color);
public void SetEyePupilSize(float size);
public void SetEyeWetness(float wetness);
public void SetEyeScleraVeinsIntensity(float intensity);

// 毛发参数设置
public void SetHairRootColor(Color color);
public void SetHairTipColor(Color color);
public void SetHairMelanin(float melanin);
public void SetHairRoughness(float roughness);
public void SetHairAnisotropyIntensity(float intensity);

// 快速预设
public void ApplyYoungSkinPreset();
public void ApplyMatureSkinPreset();
public void ApplyOilySkinPreset();
public void ApplyDrySkinPreset();
public void ApplyBrownEyePreset();
public void ApplyBlueEyePreset();
public void ApplyGreenEyePreset();
public void ApplyBlackHairPreset();
public void ApplyBrownHairPreset();
public void ApplyBlondeHairPreset();
public void ApplyRedHairPreset();
public void ApplyWhiteHairPreset();
```

---

### MetaHumanEditorUI

#### 核心方法

```csharp
// 初始化与控制
public void Initialize();
public void SwitchToTab(EditorTab tab);
public void SetTargetCharacter(Actor character);
public void RefreshAllPanels();
public void ResetToDefault();
public void Close();
```

#### 枚举

```csharp
public enum EditorTab
{
    Skin = 0,
    Eyes = 1,
    Hair = 2
}
```

---

### CharacterAppearancePreviewController

#### 核心方法

```csharp
// 预览模式
public void ApplyPreviewMode(PreviewMode mode);

// 相机控制
public void SetAutoRotation(bool enabled);
public void SetCharacterRotation(float rotation);
public void ResetCameraView();

// 动画控制
public void LoadPreviewCharacter(string modelPath);
public void PlayPreviewAnimation(string animationName);
public void StopPreviewAnimation();

// 截图
public void CapturePreviewImage(string filePath, int width = 512, int height = 512);

// 刷新
public void RefreshPreview();
```

#### 枚举

```csharp
public enum PreviewMode
{
    FaceCloseUp,  // 面部特写
    UpperBody,    // 上半身
    FullBody      // 全身
}
```

---

## 常见问题

### Q1: 材质控制器未自动绑定？

**问题**: 编辑器启动时提示控制器未分配

**解决方案**:
```csharp
// 手动调用自动绑定
var editor = Actor.GetScript<CharacterAppearanceEditor>();
editor.AutoBindControllers();

// 或手动分配
editor.SkinController = targetActor.GetScript<SkinMaterialController>();
editor.EyeController = targetActor.GetScript<EyeMaterialController>();
editor.HairController = targetActor.GetScript<HairMaterialController>();
```

---

### Q2: 参数修改后不生效？

**问题**: 拖动滑块后角色外观没有变化

**可能原因**:
1. 材质控制器未正确引用
2. 实时更新被禁用
3. 材质未正确分配到模型

**解决方案**:
```csharp
// 1. 检查控制器引用
if (editor.SkinController == null)
{
    Debug.LogWarning("SkinController未分配");
}

// 2. 启用实时更新
editor.EnableRealtimeUpdate = true;

// 3. 手动刷新
editor.RefreshAllMaterials();
```

---

### Q3: 预设保存失败？

**问题**: 点击保存按钮后没有生成文件

**可能原因**:
1. 预设目录不存在
2. 文件名包含非法字符
3. 没有写入权限

**解决方案**:
```csharp
// 确保目录存在
string directory = "Content/Presets/Characters";
if (!Directory.Exists(directory))
{
    Directory.CreateDirectory(directory);
}

// 验证文件名
string presetName = "MyPreset"; // 不要包含 / \ : * ? " < > |
string filePath = Path.Combine(directory, presetName + ".json");
```

---

### Q4: UI界面显示不正常？

**问题**: UI元素重叠或位置错误

**解决方案**:
```csharp
// 1. 确保UICanvas正确设置
var canvas = Actor as UICanvas;
canvas.RenderMode = CanvasRenderMode.ScreenSpace;

// 2. 手动刷新UI布局
var editorUI = canvas.GetScript<MetaHumanEditorUI>();
editorUI.Initialize();
```

---

### Q5: 性能问题？

**问题**: 编辑时帧率下降

**优化建议**:
```csharp
var renderer = Actor.GetScript<MetaHumanCharacterRenderer>();

// 1. 降低质量等级
renderer.Quality = RenderQuality.Medium;

// 2. 启用LOD
renderer.EnableLOD = true;

// 3. 启用自适应质量
renderer.AdaptiveQuality = true;
renderer.TargetFrameRate = 30;

// 4. 减少更新频率
editor.UpdateInterval = 0.1f; // 从0.05增加到0.1
```

---

## 最佳实践

### 工作流程建议

#### 1. 创建角色流程

```
Step 1: 选择种族预设
  └─ 应用亚洲/欧洲/非洲皮肤预设

Step 2: 调整年龄
  └─ 应用年轻/成熟皮肤预设
  └─ 微调皱纹、毛孔强度

Step 3: 设置眼睛
  └─ 选择眼睛颜色预设
  └─ 调整瞳孔大小
  └─ 调整湿润度

Step 4: 设置头发
  └─ 选择发色预设
  └─ 调整黑色素/红色素
  └─ 调整高光

Step 5: 细节调整
  └─ 雀斑、血管等特征
  └─ 嘴唇颜色、腮红

Step 6: 保存预设
  └─ 输入有意义的名称
  └─ 添加描述信息
```

---

#### 2. 参数调整技巧

**从粗到细**
```
1. 先调整大的参数（颜色、粗糙度）
2. 再调整中等参数（SSS强度、细节）
3. 最后调整精细参数（毛孔、雀斑）
```

**对比参考**
```
1. 准备参考图片
2. 使用分屏对比
3. 逐个参数匹配
```

**预设起点**
```
1. 从最接近的预设开始
2. 记录修改的参数
3. 保存为新预设
```

---

#### 3. 性能优化建议

**编辑时**
```csharp
// 使用中等质量
renderer.Quality = RenderQuality.Medium;

// 禁用自动旋转（减少渲染负担）
previewController.EnableAutoRotation = false;

// 增大更新间隔
editor.UpdateInterval = 0.1f;
```

**预览时**
```csharp
// 使用高质量
renderer.Quality = RenderQuality.High;

// 启用景深等效果
previewController.EnableDOF = true;
previewController.EnableBloom = true;
```

**发布时**
```csharp
// 启用所有优化
renderer.EnableLOD = true;
renderer.AdaptiveQuality = true;

// 根据平台设置目标帧率
renderer.TargetFrameRate = 60; // PC
// renderer.TargetFrameRate = 30; // 移动端
```

---

#### 4. 预设组织建议

**目录结构**
```
Presets/Characters/
├── Base/              # 基础预设
│   ├── Asian.json
│   ├── European.json
│   └── African.json
├── Age/               # 年龄变体
│   ├── Young.json
│   └── Mature.json
├── Heroes/            # 主角
│   ├── Hero_Male.json
│   └── Hero_Female.json
├── NPCs/              # NPC
│   ├── Guard.json
│   └── Merchant.json
└── Custom/            # 自定义
    └── ...
```

**命名规范**
```
格式: Category_Type_Variant.json
示例:
  - Asian_Young_Female.json
  - European_Mature_Male.json
  - Hero_Protagonist_Final.json
```

---

#### 5. 团队协作建议

**版本控制**
```
1. 将预设文件加入Git
2. 使用.gitattributes标记为文本
3. 记录重要版本的Tag
```

**预设共享**
```
1. 创建预设库文档
2. 截图展示效果
3. 标注用途和特点
```

**参数文档化**
```
在预设Description字段中记录:
  - 创建时间
  - 创建者
  - 用途说明
  - 特殊参数说明
```

---

## 附录

### A. 参数速查表

#### 皮肤参数（35+）

| 参数 | 范围 | 推荐值 | 说明 |
|------|------|--------|------|
| BaseColor | RGB | 种族相关 | 基肤色 |
| Roughness | 0-1 | 0.3-0.4 | 粗糙度 |
| SSSIntensity | 0-2 | 0.8-1.2 | SSS强度 |
| SSSRadius | 0-5 | 1.0-1.5 | SSS半径 |
| EpidermisColor | RGB | 浅黄 | 表皮色 |
| DermisColor | RGB | 偏红 | 真皮色 |
| SubcutisColor | RGB | 深红 | 皮下色 |
| PoreIntensity | 0-2 | 0.5-0.7 | 毛孔 |
| WrinkleIntensity | 0-2 | 年龄相关 | 皱纹 |
| OilIntensity | 0-2 | 0.2-0.4 | 油光 |
| FreckleIntensity | 0-1 | 0-0.5 | 雀斑 |

---

#### 眼睛参数（24+）

| 参数 | 范围 | 推荐值 | 说明 |
|------|------|--------|------|
| IrisColor | RGB | 颜色相关 | 虹膜色 |
| PupilSize | 0.1-0.8 | 0.3-0.5 | 瞳孔大小 |
| CorneaIOR | 1-1.5 | 1.376 | 折射率 |
| Wetness | 0-1 | 0.7-0.9 | 湿润度 |
| ScleraVeinsIntensity | 0-2 | 0.2-0.5 | 血丝 |
| LimbusIntensity | 0-2 | 0.8-1.2 | 角膜缘 |
| CausticsIntensity | 0-1 | 0.2-0.4 | 焦散 |

---

#### 毛发参数（26+）

| 参数 | 范围 | 推荐值 | 说明 |
|------|------|--------|------|
| BaseColor | RGB | 发色相关 | 基础发色 |
| Melanin | 0-1 | 深浅相关 | 黑色素 |
| Pheomelanin | 0-1 | 红调相关 | 红色素 |
| Roughness | 0-1 | 0.4-0.5 | 粗糙度 |
| Anisotropy | 0-1 | 0.8-0.95 | 各向异性 |
| ScatterIntensity | 0-2 | 0.6-1.2 | 散射 |
| PrimarySpecular | 0-2 | 0.8-1.2 | 主高光 |
| SecondarySpecular | 0-2 | 0.3-0.7 | 次高光 |

---

### B. 快捷键参考

| 操作 | 快捷键 | 说明 |
|------|--------|------|
| 切换标签 | Tab | 循环切换 |
| 保存预设 | Ctrl+S | 快速保存 |
| 加载预设 | Ctrl+O | 打开预设 |
| 重置参数 | Ctrl+R | 恢复默认 |
| 截图 | F12 | 保存截图 |

---

### C. 故障排除清单

**编辑器无法启动**
- [ ] 检查UICanvas配置
- [ ] 检查脚本引用
- [ ] 查看控制台错误

**参数不生效**
- [ ] 验证控制器绑定
- [ ] 检查实时更新开关
- [ ] 手动刷新材质

**性能问题**
- [ ] 降低质量等级
- [ ] 启用LOD
- [ ] 增加更新间隔
- [ ] 禁用自动旋转

**预设问题**
- [ ] 检查文件路径
- [ ] 验证文件名
- [ ] 确认目录权限

---

### D. 资源链接

**文档**
- API文档: `Game/Combat/Skills/API_Reference.md`
- 完成度报告: `Game/Combat/Skills/MetaHuman功能开发完成度报告.md`

**示例预设**
- 位置: `Content/Presets/Characters/`
- 格式: JSON

**材质控制器**
- SkinMaterialController: `Game/Rendering/Materials/`
- EyeMaterialController: `Game/Rendering/Materials/`
- HairMaterialController: `Game/Rendering/Materials/`

---

## 版本历史

| 版本 | 日期 | 更新内容 |
|------|------|---------|
| 1.0 | 2026-02-13 | 初始版本发布 |

---

**文档维护者**: AI助手  
**最后更新**: 2026-02-13  
**文档状态**: ✅ 完整
