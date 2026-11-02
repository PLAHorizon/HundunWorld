-- 008_seed_equipment_affinity.sql
-- 把客户端装备表里的"五行属性值（亲和）"补写进服务端物品模板，让两端的兜底地板读到同一份数字。
--
-- 新口径（2026-09-29 确认）：三行有一个五行属性值兜底——
--   兜底行 = 亲和 ÷10 个等效点 × 加点每点换算 × 50%，再与已聚合出来的那一行取较高。
--   客户端的亲和一直存在（EquipmentData.WuxingBonus + 宝石），服务端模板里却没有，
--   于是同一件装备两端算出的三行不同：面板显示一套、服务端结算另一套。
--
-- 下面的 base_attributes 全部由 EquipmentDatabase.cs 生成（通用攻防/血蓝 + 五行三行 + 五行亲和），
-- 不手写，避免抄错；数值与 006 完全一致，只是多了裸元素名那几个亲和键。
-- 可重复执行。006 不动（已应用于现库）：新库按 006 → 008 顺序执行即可。
-- 回退：重新执行 006（它把同一批行的 base_attributes 刷回不含亲和键的那一份）。

SET NOCOUNT ON;
-- 默认衣服
UPDATE dbo.Game_HunduShijie_ItemTemplate SET base_attributes = N'{"Defense": 10, "Earth": 20, "EarthDefense": 20, "HP": 50}', update_time = GETUTCDATE() WHERE item_id = 10001;
-- 默认长剑
UPDATE dbo.Game_HunduShijie_ItemTemplate SET base_attributes = N'{"Attack": 15, "Metal": 30, "MetalAttack": 30}', update_time = GETUTCDATE() WHERE item_id = 20001;
-- 默认头巾
UPDATE dbo.Game_HunduShijie_ItemTemplate SET base_attributes = N'{"Defense": 5, "MP": 30, "Wood": 15, "WoodPenetration": 15}', update_time = GETUTCDATE() WHERE item_id = 30001;
-- 新手项链
UPDATE dbo.Game_HunduShijie_ItemTemplate SET base_attributes = N'{"MP": 20, "Wood": 10, "WoodPenetration": 10}', update_time = GETUTCDATE() WHERE item_id = 10002;
-- 新手护肩
UPDATE dbo.Game_HunduShijie_ItemTemplate SET base_attributes = N'{"Defense": 5, "Earth": 10, "EarthPenetration": 10, "HP": 20}', update_time = GETUTCDATE() WHERE item_id = 10003;
-- 新手披风
UPDATE dbo.Game_HunduShijie_ItemTemplate SET base_attributes = N'{"Defense": 3, "Water": 10, "WaterPenetration": 10}', update_time = GETUTCDATE() WHERE item_id = 10004;
-- 新手腰带
UPDATE dbo.Game_HunduShijie_ItemTemplate SET base_attributes = N'{"Earth": 10, "EarthPenetration": 10, "HP": 30}', update_time = GETUTCDATE() WHERE item_id = 10005;
-- 新手长裤
UPDATE dbo.Game_HunduShijie_ItemTemplate SET base_attributes = N'{"Defense": 6, "Earth": 15, "EarthDefense": 15, "HP": 20}', update_time = GETUTCDATE() WHERE item_id = 10006;
-- 新手布鞋
UPDATE dbo.Game_HunduShijie_ItemTemplate SET base_attributes = N'{"Defense": 4, "Water": 10, "WaterDefense": 10}', update_time = GETUTCDATE() WHERE item_id = 10007;
-- 新手短剑
UPDATE dbo.Game_HunduShijie_ItemTemplate SET base_attributes = N'{"Attack": 10, "Metal": 20, "MetalAttack": 20}', update_time = GETUTCDATE() WHERE item_id = 10008;
-- 新手右戒指
UPDATE dbo.Game_HunduShijie_ItemTemplate SET base_attributes = N'{"Attack": 3, "Metal": 10, "MetalPenetration": 10}', update_time = GETUTCDATE() WHERE item_id = 10009;
-- 新手左戒指
UPDATE dbo.Game_HunduShijie_ItemTemplate SET base_attributes = N'{"MP": 15, "Wood": 10, "WoodPenetration": 10}', update_time = GETUTCDATE() WHERE item_id = 10010;
-- 新手右手腕
UPDATE dbo.Game_HunduShijie_ItemTemplate SET base_attributes = N'{"Defense": 4, "Earth": 10, "EarthPenetration": 10}', update_time = GETUTCDATE() WHERE item_id = 10011;
-- 新手左手腕
UPDATE dbo.Game_HunduShijie_ItemTemplate SET base_attributes = N'{"MP": 10, "Water": 10, "WaterPenetration": 10}', update_time = GETUTCDATE() WHERE item_id = 10012;
-- 新手面具
UPDATE dbo.Game_HunduShijie_ItemTemplate SET base_attributes = N'{"Attack": 2, "Defense": 2, "Fire": 10, "FirePenetration": 10}', update_time = GETUTCDATE() WHERE item_id = 10013;
-- 粗布小包
UPDATE dbo.Game_HunduShijie_ItemTemplate SET base_attributes = N'{"Earth": 5, "HP": 10}', update_time = GETUTCDATE() WHERE item_id = 2001;
-- 皮革中包
UPDATE dbo.Game_HunduShijie_ItemTemplate SET base_attributes = N'{"Defense": 3, "Earth": 10, "HP": 20}', update_time = GETUTCDATE() WHERE item_id = 2002;
-- 丝绸大包
UPDATE dbo.Game_HunduShijie_ItemTemplate SET base_attributes = N'{"Defense": 6, "HP": 30, "Wood": 15}', update_time = GETUTCDATE() WHERE item_id = 2003;
-- 龙皮巨包
UPDATE dbo.Game_HunduShijie_ItemTemplate SET base_attributes = N'{"Defense": 10, "Fire": 20, "HP": 50}', update_time = GETUTCDATE() WHERE item_id = 2004;
GO
