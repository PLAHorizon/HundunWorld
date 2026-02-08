# 密码安全迁移指南

## 概述

本指南说明如何从旧的明文/简单加密密码系统迁移到新的 PBKDF2 安全哈希系统。

## ⚠️ 重要提示

**在执行此迁移之前，请务必备份数据库！**

## 迁移策略

由于旧系统中的密码无法直接转换为新的哈希值（这是密码哈希的安全特性），我们提供了以下几种迁移策略：

### 策略 A: 逐步迁移（推荐）

这是最安全的方法，用户在下次登录时自动升级到新的密码系统。

#### 步骤：

1. **部署新代码**
   - 部署包含 `SecurePasswordHasher` 的新版本代码
   - 部署数据库迁移，添加 `PasswordSalt` 列

2. **添加兼容性登录逻辑**
   
   在 `PassportGrain.AuthenticationAsync` 中添加向后兼容代码：

   ```csharp
   // 3. 验证密码
   string decodedPassword;
   try
   {
       decodedPassword = Encoding.UTF8.GetString(Convert.FromBase64String(loginDto.Password));
   }
   catch
   {
       decodedPassword = loginDto.Password;
   }

   bool isPasswordValid = false;
   bool needsUpgrade = false;

   // 首先尝试新的安全验证
   if (!string.IsNullOrEmpty(passport.PasswordSalt))
   {
       isPasswordValid = SecurePasswordHasher.VerifyPassword(
           decodedPassword,
           passport.Password,
           passport.PasswordSalt);
   }
   else
   {
       // 后备：使用旧的验证方法
       string oldEncryptedPassword = PassportHelper.SetPasportPassword(passport.Id, decodedPassword);
       isPasswordValid = (passport.Password == oldEncryptedPassword);
       needsUpgrade = isPasswordValid; // 标记需要升级
   }

   if (!isPasswordValid)
   {
       _logger.LogWarning("密码验证失败: {PassportId}", loginDto.PassportId);
       await RecordFailedLoginAttempt(loginDto.PassportId);
       return null;
   }

   // 如果使用旧密码登录成功，立即升级到新系统
   if (needsUpgrade)
   {
       _logger.LogInformation("升级密码到安全哈希: {PassportId}", loginDto.PassportId);
       var (newHash, newSalt) = SecurePasswordHasher.HashPassword(decodedPassword);
       passport.Password = newHash;
       passport.PasswordSalt = newSalt;
       passport.UpdateTime = DateTime.UtcNow;
       await _dataContext.UpdateAsync(passport, passport.Id);
   }
   ```

3. **监控迁移进度**
   
   创建 SQL 查询监控迁移进度：

   ```sql
   -- 查看迁移进度
   SELECT 
       COUNT(*) as TotalPassports,
       SUM(CASE WHEN PasswordSalt IS NOT NULL AND PasswordSalt != '' THEN 1 ELSE 0 END) as MigratedPassports,
       SUM(CASE WHEN PasswordSalt IS NULL OR PasswordSalt = '' THEN 1 ELSE 0 END) as PendingPassports,
       (SUM(CASE WHEN PasswordSalt IS NOT NULL AND PasswordSalt != '' THEN 1 ELSE 0 END) * 100.0 / COUNT(*)) as MigrationProgress
   FROM Basic_Sys_Passport
   WHERE IsValid = 1;
   ```

4. **完成迁移**
   
   当大部分用户已迁移（如 >95%）后：
   - 通知未迁移用户重置密码
   - 或者强制要求所有用户在下次登录时重置密码

### 策略 B: 强制密码重置

如果用户基数小或安全要求高，可以要求所有用户重置密码。

#### 步骤：

1. **部署新代码和数据库迁移**

2. **清空所有旧密码**

   ```sql
   -- 备份旧密码表（以防万一）
   SELECT * INTO Basic_Sys_Passport_Backup_20260207
   FROM Basic_Sys_Passport;

   -- 清空密码字段，强制用户重置
   UPDATE Basic_Sys_Passport
   SET Password = NULL,
       PasswordSalt = NULL,
       UpdateTime = GETUTCDATE();
   ```

3. **发送密码重置邮件**
   
   为所有用户发送密码重置链接。

4. **用户重置密码**
   
   用户通过密码重置流程创建新密码，新密码将使用安全哈希存储。

### 策略 C: 预计算迁移（不推荐）

⚠️ 此策略仅在有访问旧密码的特殊情况下使用，不推荐。

## 数据库迁移步骤

### 1. 应用 EF Core 迁移

```bash
# 在 Horizon.Entities 项目目录下
dotnet ef migrations add AddPasswordSaltToPassport --context BasicEntityContext

# 应用迁移到数据库
dotnet ef database update --context BasicEntityContext
```

### 2. 或者手动执行 SQL

```sql
-- SQL Server
ALTER TABLE Basic_Sys_Passport
ADD PasswordSalt NVARCHAR(MAX) NULL;

-- 添加注释
EXEC sys.sp_addextendedproperty 
    @name = N'MS_Description', 
    @value = N'密码盐值', 
    @level0type = N'SCHEMA', @level0name = 'dbo',
    @level1type = N'TABLE',  @level1name = 'Basic_Sys_Passport',
    @level2type = N'COLUMN', @level2name = 'PasswordSalt';
```

```sql
-- PostgreSQL
ALTER TABLE "Basic_Sys_Passport"
ADD COLUMN "PasswordSalt" TEXT NULL;

COMMENT ON COLUMN "Basic_Sys_Passport"."PasswordSalt" IS '密码盐值';
```

```sql
-- MySQL
ALTER TABLE `Basic_Sys_Passport`
ADD COLUMN `PasswordSalt` TEXT NULL COMMENT '密码盐值';
```

## 验证迁移

### 测试步骤

1. **创建测试用户**
   
   使用新注册流程创建一个测试用户，验证密码安全存储。

   ```sql
   SELECT Id, Password, PasswordSalt, CreateTime
   FROM Basic_Sys_Passport
   WHERE Id = 'test_user_id';
   ```

   预期结果：
   - `Password` 字段应该是长的 Base64 编码字符串（约 88 个字符）
   - `PasswordSalt` 字段应该是 Base64 编码字符串（约 44 个字符）
   - 两者都不应包含可读的明文信息

2. **测试登录**
   
   使用测试用户登录，验证密码验证正常工作。

3. **测试密码修改**
   
   修改密码，验证新密码使用安全哈希存储。

4. **测试旧用户登录（如使用策略 A）**
   
   使用旧系统中的用户登录，验证：
   - 登录成功
   - 密码自动升级到新系统
   - 再次登录仍然成功

## 回滚计划

如果迁移出现问题：

1. **回滚代码**
   
   部署之前的代码版本。

2. **回滚数据库（如需要）**

   ```sql
   -- 恢复备份（如果有）
   -- 或者简单地删除 PasswordSalt 列
   ALTER TABLE Basic_Sys_Passport
   DROP COLUMN PasswordSalt;
   ```

3. **验证系统恢复正常**

## 安全最佳实践

### 密码策略

确保实施以下密码策略：

```csharp
// 在 SecurePasswordHasher.IsPasswordStrong 中实施
- 最小长度：8 个字符
- 必须包含：大写字母、小写字母、数字、特殊字符中的至少 3 种
- 不允许常见密码（如 "password123"）
- 不允许与用户名相同
```

### 登录安全

```csharp
// 已在 PassportGrain 中实施
- 登录尝试限制：5 次 / 15 分钟
- 失败登录日志记录
- 会话管理和超时
```

### 监控和审计

```csharp
// 建议添加
- 记录所有密码修改操作
- 记录所有密码验证失败
- 定期审查异常登录模式
- 设置告警机制
```

## 常见问题

### Q: 为什么不能直接转换旧密码？

A: 密码哈希是单向函数，无法从哈希值还原原始密码。这是设计特性，确保即使数据库泄露，攻击者也无法获取原始密码。

### Q: 用户会受到影响吗？

A: 使用策略 A（逐步迁移），用户无感知。使用策略 B（强制重置），用户需要重置密码。

### Q: 迁移需要多长时间？

A: 策略 A 的迁移时间取决于用户登录频率，通常 1-3 个月可完成大部分用户迁移。

### Q: 如果用户很久不登录怎么办？

A: 可以在迁移完成后（如 6 个月），将剩余未迁移账户标记为需要重置密码。

### Q: 新旧密码系统兼容多久？

A: 建议保持向后兼容至少 6 个月，确保所有活跃用户完成迁移。

## 支持

如有问题，请联系技术团队或查看：
- [OWASP Password Storage Cheat Sheet](https://cheatsheetseries.owasp.org/cheatsheets/Password_Storage_Cheat_Sheet.html)
- [NIST Digital Identity Guidelines](https://pages.nist.gov/800-63-3/)

---

**文档版本**: 1.0  
**最后更新**: 2026-02-07  
**维护者**: 技术安全团队
