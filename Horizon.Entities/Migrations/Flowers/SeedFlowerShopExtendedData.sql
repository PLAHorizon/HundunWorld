-- 入驻配置默认数据
IF NOT EXISTS (SELECT 1 FROM Flower_SettledConfig)
BEGIN
    INSERT INTO Flower_SettledConfig (BusinessType, SettlementAccountType, TrialDays, IsCity, IsPeopleNumber, IsAddress, IsBusinessLicenseCode, IsBusinessScope, IsBusinessLicense, IsValid, IsDeleted, CreateTime, Passport)
    VALUES (2, 2, 30, 1, 0, 1, 1, 0, 1, 1, 0, GETUTCDATE(), 'SYSTEM');
END

-- 默认品牌
IF NOT EXISTS (SELECT 1 FROM Flower_Brand)
BEGIN
    INSERT INTO Flower_Brand (Name, Logo, [Description], DisplaySequence, IsRecommend, AuditStatus, IsDeleted, IsValid, CreateTime, Passport)
    VALUES 
        (N'花之语', '', N'花之语品牌鲜花', 1, 1, 1, 0, 1, GETUTCDATE(), 'SYSTEM'),
        (N'春之韵', '', N'春之韵花卉品牌', 2, 1, 1, 0, 1, GETUTCDATE(), 'SYSTEM'),
        (N'绿野仙踪', '', N'绿野仙踪绿植品牌', 3, 0, 1, 0, 1, GETUTCDATE(), 'SYSTEM'),
        (N'花好月圆', '', N'花好月圆婚庆花卉', 4, 1, 1, 0, 1, GETUTCDATE(), 'SYSTEM'),
        (N'田园牧歌', '', N'田园牧歌园艺品牌', 5, 0, 1, 0, 1, GETUTCDATE(), 'SYSTEM');
END

-- 默认满额减规则（示例）
IF NOT EXISTS (SELECT 1 FROM Flower_FullDiscountRule)
BEGIN
    INSERT INTO Flower_FullDiscountRule (ShopId, RuleName, StartDate, EndDate, LimitValue, DiscountValue, IsActive, IsDeleted, IsValid, CreateTime, Passport)
    VALUES 
        (0, N'新店开业满减', DATEADD(DAY, -30, GETUTCDATE()), DATEADD(DAY, 365, GETUTCDATE()), 100, 10, 1, 0, 1, GETUTCDATE(), 'SYSTEM'),
        (0, N'节日特惠满减', DATEADD(DAY, -30, GETUTCDATE()), DATEADD(DAY, 365, GETUTCDATE()), 200, 30, 1, 0, 1, GETUTCDATE(), 'SYSTEM');
END

-- 默认优惠券（示例）
IF NOT EXISTS (SELECT 1 FROM Flower_Coupon)
BEGIN
    INSERT INTO Flower_Coupon (ShopId, CouponName, CouponType, Denomination, UseCondition, StartDate, EndDate, TotalCount, ReceivedCount, UsedCount, IsActive, IsDeleted, IsValid, CreateTime, Passport)
    VALUES 
        (0, N'新人专享券', 0, 20, 100, DATEADD(DAY, -30, GETUTCDATE()), DATEADD(DAY, 365, GETUTCDATE()), 1000, 0, 0, 1, 0, 1, GETUTCDATE(), 'SYSTEM'),
        (0, N'满200减30', 0, 30, 200, DATEADD(DAY, -30, GETUTCDATE()), DATEADD(DAY, 365, GETUTCDATE()), 500, 0, 0, 1, 0, 1, GETUTCDATE(), 'SYSTEM'),
        (0, N'9折优惠券', 1, 90, 50, DATEADD(DAY, -30, GETUTCDATE()), DATEADD(DAY, 365, GETUTCDATE()), 800, 0, 0, 1, 0, 1, GETUTCDATE(), 'SYSTEM');
END
