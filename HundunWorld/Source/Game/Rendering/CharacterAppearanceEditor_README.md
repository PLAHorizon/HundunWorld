# CharacterAppearanceEditor 使用说明

## 功能概述
`CharacterAppearanceEditor` 是一个用于统一管理角色外观（皮肤、眼睛、毛发）材质参数的脚本。

## 组件要求

要使 `CharacterAppearanceEditor` 正常工作，需要在同一个 Actor 或其子对象中包含以下三个组件：

1. `SkinMaterialController` - 用于管理皮肤材质参数
2. `EyeMaterialController` - 用于管理眼睛材质参数
3. `HairMaterialController` - 用于管理毛发材质参数

## 自动绑定

脚本会尝试自动在当前 Actor 和其所有子对象中查找上述三个控制器组件。

## 预设系统

- 默认预设路径：`Content/Presets/MetaHuman/Preset_Default.json`
- 支持的预设类型：
  - 默认预设
  - 亚洲人预设
  - 欧洲人预设

## 故障排除

如果出现控制器未分配的警告，请检查：
1. 确保角色 Actor 上或其子对象上附加了所需的控制器组件
2. 检查预设文件是否存在