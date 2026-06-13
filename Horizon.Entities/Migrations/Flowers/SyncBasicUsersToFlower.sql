-- ============================================================
-- 花卉用户数据同步脚本 - 从 Basic 数据库同步用户到 Flower_User
-- 用途：初始化 Flower_User 表，将 Basic 中的通行证用户导入
-- 执行前请确认：
--   1. 已连接到 Flower 数据库
--   2. Basic 数据库存在且包含 Basic_Sys_User 表
-- ============================================================

USE Flower;
GO

-- 从 Basic 数据库同步所有有效用户到 Flower_User 表
-- 仅同步不存在于 Flower_User 中的用户
INSERT INTO Flower_User (
    Passport,           -- 通行证ID (varchar(32))
    UserId,             -- 用户GUID (uniqueidentifier)
    UserType,           -- 用户类型: 0=Normal, 1=Merchant, 2=Admin
    DisplayName,        -- 显示名称 (varchar(64))
    Phone,              -- 手机号 (varchar(20))
    Region,             -- 地区 (varchar(64))
    SubscriptionLevel,  -- 订阅等级: 0=Free, 1=Basic, 2=Premium, 3=VIP
    MerchantId,         -- 商户ID (bigint, nullable)
    IsValid,            -- 是否有效
    IsDeleted,          -- 是否已删除
    CreateTime          -- 创建时间
)
SELECT
    bu.PassportId,                              -- Passport = Basic 的 PassportId
    bu.Id,                                      -- UserId = Basic 的 Id (Guid)
    0 AS UserType,                              -- 默认普通用户
    COALESCE(bu.NickName, bu.Name, bu.PassportId, '用户') AS DisplayName,
    ISNULL(bu.Phone, '') AS Phone,
    '默认' AS Region,
    0 AS SubscriptionLevel,                     -- 默认免费订阅
    NULL AS MerchantId,                         -- 初始无商户关联
    1 AS IsValid,                               -- 有效用户
    0 AS IsDeleted,                             -- 未删除
    COALESCE(bu.CreateDate, GETUTCDATE()) AS CreateTime
FROM [Basic].[dbo].[Basic_Sys_User] bu
WHERE bu.IsValid = 1                            -- 仅同步有效用户
  AND NOT EXISTS (                              -- 仅同步不存在的用户
      SELECT 1 FROM Flower_User fu
      WHERE fu.Passport = bu.PassportId
         OR fu.UserId = bu.Id
  );

-- 显示同步结果
DECLARE @SyncCount INT = @@ROWCOUNT;
PRINT '✅ 用户数据同步完成，共同步 ' + CAST(@SyncCount AS VARCHAR(10)) + ' 个用户';

-- 验证同步结果
SELECT 
    COUNT(*) AS TotalUsers,
    COUNT(CASE WHEN IsValid = 1 THEN 1 END) AS ValidUsers,
    COUNT(CASE WHEN UserType = 0 THEN 1 END) AS NormalUsers,
    COUNT(CASE WHEN MerchantId IS NOT NULL THEN 1 END) AS MerchantUsers
FROM Flower_User;
