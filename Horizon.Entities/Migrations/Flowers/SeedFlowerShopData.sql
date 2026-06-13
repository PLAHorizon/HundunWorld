-- ============================================================
-- 花卉店铺管理数据种子脚本
-- 用途：初始化默认店铺等级和商品分类
-- 执行前请确认已连接到 Flower 数据库
-- ============================================================

USE Flower;
GO

-- ============================================================
-- 1. 默认店铺等级
-- ============================================================
IF NOT EXISTS (SELECT 1 FROM Flower_ShopGrade)
BEGIN
    INSERT INTO Flower_ShopGrade (Name, ProductLimit, ImageLimit, TemplateLimit, ChargeStandard, Remark, IsValid, IsDeleted, CreateTime, Passport)
    VALUES 
        (N'免费版', 50, 100, 3, 0, N'免费体验套餐，适合个人花店', 1, 0, GETUTCDATE(), 'SYSTEM'),
        (N'基础版', 200, 500, 10, 99, N'基础套餐，适合小型花店', 1, 0, GETUTCDATE(), 'SYSTEM'),
        (N'专业版', 1000, 2000, 50, 299, N'专业套餐，适合中型花店', 1, 0, GETUTCDATE(), 'SYSTEM'),
        (N'旗舰版', 10000, 10000, 200, 599, N'旗舰套餐，适合大型花店和连锁', 1, 0, GETUTCDATE(), 'SYSTEM');

    PRINT '✅ 店铺等级初始化完成';
END
ELSE
BEGIN
    PRINT '⏭ 店铺等级已存在，跳过初始化';
END

-- ============================================================
-- 2. 默认商品分类（三级分类树）
-- ============================================================
IF NOT EXISTS (SELECT 1 FROM Flower_ProductCategory)
BEGIN
    -- 一级分类
    INSERT INTO Flower_ProductCategory (Name, Depth, Path, ParentCategoryId, DisplaySequence, IsValid, IsDeleted, CreateTime, Passport)
    VALUES 
        (N'鲜切花', 1, '1', 0, 1, 1, 0, GETUTCDATE(), 'SYSTEM'),
        (N'盆栽绿植', 1, '2', 0, 2, 1, 0, GETUTCDATE(), 'SYSTEM'),
        (N'花束花篮', 1, '3', 0, 3, 1, 0, GETUTCDATE(), 'SYSTEM'),
        (N'婚庆用花', 1, '4', 0, 4, 1, 0, GETUTCDATE(), 'SYSTEM'),
        (N'园艺资材', 1, '5', 0, 5, 1, 0, GETUTCDATE(), 'SYSTEM');

    DECLARE @Cat1Id BIGINT, @Cat2Id BIGINT, @Cat3Id BIGINT, @Cat4Id BIGINT, @Cat5Id BIGINT;
    SELECT @Cat1Id = Id FROM Flower_ProductCategory WHERE Name = N'鲜切花' AND Depth = 1;
    SELECT @Cat2Id = Id FROM Flower_ProductCategory WHERE Name = N'盆栽绿植' AND Depth = 1;
    SELECT @Cat3Id = Id FROM Flower_ProductCategory WHERE Name = N'花束花篮' AND Depth = 1;
    SELECT @Cat4Id = Id FROM Flower_ProductCategory WHERE Name = N'婚庆用花' AND Depth = 1;
    SELECT @Cat5Id = Id FROM Flower_ProductCategory WHERE Name = N'园艺资材' AND Depth = 1;

    -- 二级分类 - 鲜切花
    INSERT INTO Flower_ProductCategory (Name, Depth, Path, ParentCategoryId, DisplaySequence, IsValid, IsDeleted, CreateTime, Passport)
    VALUES 
        (N'玫瑰', 2, CONCAT(@Cat1Id, '|', @Cat1Id+1), @Cat1Id, 1, 1, 0, GETUTCDATE(), 'SYSTEM'),
        (N'百合', 2, CONCAT(@Cat1Id, '|', @Cat1Id+2), @Cat1Id, 2, 1, 0, GETUTCDATE(), 'SYSTEM'),
        (N'康乃馨', 2, CONCAT(@Cat1Id, '|', @Cat1Id+3), @Cat1Id, 3, 1, 0, GETUTCDATE(), 'SYSTEM'),
        (N'菊花', 2, CONCAT(@Cat1Id, '|', @Cat1Id+4), @Cat1Id, 4, 1, 0, GETUTCDATE(), 'SYSTEM'),
        (N'郁金香', 2, CONCAT(@Cat1Id, '|', @Cat1Id+5), @Cat1Id, 5, 1, 0, GETUTCDATE(), 'SYSTEM');

    -- 二级分类 - 盆栽绿植
    INSERT INTO Flower_ProductCategory (Name, Depth, Path, ParentCategoryId, DisplaySequence, IsValid, IsDeleted, CreateTime, Passport)
    VALUES 
        (N'多肉植物', 2, CONCAT(@Cat2Id, '|', @Cat2Id+1), @Cat2Id, 1, 1, 0, GETUTCDATE(), 'SYSTEM'),
        (N'绿萝', 2, CONCAT(@Cat2Id, '|', @Cat2Id+2), @Cat2Id, 2, 1, 0, GETUTCDATE(), 'SYSTEM'),
        (N'兰花', 2, CONCAT(@Cat2Id, '|', @Cat2Id+3), @Cat2Id, 3, 1, 0, GETUTCDATE(), 'SYSTEM'),
        (N'发财树', 2, CONCAT(@Cat2Id, '|', @Cat2Id+4), @Cat2Id, 4, 1, 0, GETUTCDATE(), 'SYSTEM');

    -- 二级分类 - 花束花篮
    INSERT INTO Flower_ProductCategory (Name, Depth, Path, ParentCategoryId, DisplaySequence, IsValid, IsDeleted, CreateTime, Passport)
    VALUES 
        (N'生日花束', 2, CONCAT(@Cat3Id, '|', @Cat3Id+1), @Cat3Id, 1, 1, 0, GETUTCDATE(), 'SYSTEM'),
        (N'爱情花束', 2, CONCAT(@Cat3Id, '|', @Cat3Id+2), @Cat3Id, 2, 1, 0, GETUTCDATE(), 'SYSTEM'),
        (N'开业花篮', 2, CONCAT(@Cat3Id, '|', @Cat3Id+3), @Cat3Id, 3, 1, 0, GETUTCDATE(), 'SYSTEM'),
        (N'慰问花篮', 2, CONCAT(@Cat3Id, '|', @Cat3Id+4), @Cat3Id, 4, 1, 0, GETUTCDATE(), 'SYSTEM');

    -- 二级分类 - 婚庆用花
    INSERT INTO Flower_ProductCategory (Name, Depth, Path, ParentCategoryId, DisplaySequence, IsValid, IsDeleted, CreateTime, Passport)
    VALUES 
        (N'手捧花', 2, CONCAT(@Cat4Id, '|', @Cat4Id+1), @Cat4Id, 1, 1, 0, GETUTCDATE(), 'SYSTEM'),
        (N'婚车装饰', 2, CONCAT(@Cat4Id, '|', @Cat4Id+2), @Cat4Id, 2, 1, 0, GETUTCDATE(), 'SYSTEM'),
        (N'婚礼现场', 2, CONCAT(@Cat4Id, '|', @Cat4Id+3), @Cat4Id, 3, 1, 0, GETUTCDATE(), 'SYSTEM');

    -- 二级分类 - 园艺资材
    INSERT INTO Flower_ProductCategory (Name, Depth, Path, ParentCategoryId, DisplaySequence, IsValid, IsDeleted, CreateTime, Passport)
    VALUES 
        (N'花盆花器', 2, CONCAT(@Cat5Id, '|', @Cat5Id+1), @Cat5Id, 1, 1, 0, GETUTCDATE(), 'SYSTEM'),
        (N'营养土肥', 2, CONCAT(@Cat5Id, '|', @Cat5Id+2), @Cat5Id, 2, 1, 0, GETUTCDATE(), 'SYSTEM'),
        (N'园艺工具', 2, CONCAT(@Cat5Id, '|', @Cat5Id+3), @Cat5Id, 3, 1, 0, GETUTCDATE(), 'SYSTEM');

    PRINT '✅ 商品分类初始化完成';
END
ELSE
BEGIN
    PRINT '⏭ 商品分类已存在，跳过初始化';
END

-- 验证
SELECT N'店铺等级' AS Category, COUNT(*) AS Count FROM Flower_ShopGrade WHERE IsDeleted = 0
UNION ALL
SELECT N'商品分类', COUNT(*) FROM Flower_ProductCategory WHERE IsDeleted = 0;
