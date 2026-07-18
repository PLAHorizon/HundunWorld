# Checklist

## 枚举与数据
- [ ] `EquipmentSlot` 扩展为 15 个槽位（Head/Neck/Shoulder/Back/Body/Waist/Legs/Feet/RightHand/LeftHand/RightRing/LeftRing/RightWrist/LeftWrist/Face）
- [ ] `EquipmentDatabase` 包含覆盖 15 槽的 mock 装备数据
- [ ] `GetEquipment`、`GetAllEquipments`、`GetEquipmentsBySlot` 方法正确返回新增装备

## 组件视觉
- [ ] `InkEquipmentSlot` 尺寸调整为 56×56（或 64×64），空槽显示槽位类型文字/图标
- [ ] `InkEquipmentSlot` 根据装备 `Quality` 绘制对应品质色发光边框
- [ ] 新增春色/墨青色调 Token 不破坏现有 `InkWashTheme` 使用

## 三栏布局
- [ ] `MenuCharAttributesV2Page` 采用左(30%) / 中(40%) / 右(30%) 三栏
- [ ] 左侧包含：基础属性 6 项、进阶属性 4 项、六边形雷达图、武学摘要
- [ ] 中间包含：战力大字、3D 预览、角色名/等级/门派/称号/阶段
- [ ] 右侧包含：15 装备槽人体拓扑布局、背包、武学摘要/细节切换
- [ ] 三个主面板有墨青半透明背景 + 细金色边框
- [ ] 分区标题有金色装饰竖线
- [ ] 战力数字放大居中，带鎏金辉光
- [ ] 属性条变细，颜色按五行/语义区分

## 装备逻辑
- [ ] `DisplayedSlots` 更新为 15 槽顺序
- [ ] `InitializeMockEquipment` 正确初始化 15 槽装备状态
- [ ] 双击装备槽卸下装备到背包逻辑正确
- [ ] 双击背包格子装备物品到对应槽位逻辑正确
- [ ] `RecalculateAttributes` 遍历所有 15 个已装备槽位并正确累加属性
- [ ] 装备切换后属性条、雷达图、战力数值实时刷新

## 编译验证
- [ ] Flax.Build 编译通过，0 错误
- [ ] 代码审查确认无硬编码旧版 6 槽逻辑残留
