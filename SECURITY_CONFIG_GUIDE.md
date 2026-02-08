# 混沌世界(HundunWorld) - 配置安全说明

## ⚠️ 重要安全提醒

**此项目的敏感配置文件已从版本控制中移除。** 为了保护系统安全，所有包含凭证的 `appsettings.json` 文件都已被 `.gitignore` 忽略。

## 📋 环境配置设置

### 1. 创建本地配置文件

每个服务都需要从模板文件创建自己的 `appsettings.json` 文件：

```bash
# Horizon.Orleans.Silo
cp Horizon.Orleans.Silo/appsettings.template.json Horizon.Orleans.Silo/appsettings.json

# Horizon.Game.Gateway  
cp Horizon.Game.Gateway/appsettings.template.json Horizon.Game.Gateway/appsettings.json

# Horizon.WebApi
cp Horizon.WebApi/appsettings.template.json Horizon.WebApi/appsettings.json
```

### 2. 配置环境变量

创建一个 `.env` 文件（也已被 gitignore）或使用系统环境变量设置以下值：

#### Redis 配置
```bash
REDIS_PASSWORD=your_redis_password_here
```

#### 数据库连接字符串
```bash
# Orleans 集群数据库
ORLEANS_SQLSERVER_CONNECTION_STRING="Data Source=.;Initial Catalog=Orleans;User Id=sa;Password=YOUR_PASSWORD;Integrated Security=True;Pooling=True;Max Pool Size=200;MultipleActiveResultSets=True;TrustServerCertificate=True;"
ORLEANS_NPGSQL_CONNECTION_STRING="User Id=postgres;Password=YOUR_PASSWORD;Host=localhost;Port=5432;Database=Orleans;Pooling=True;Encoding=UTF8;"
ORLEANS_MYSQL_CONNECTION_STRING="Server=localhost;Database=Orleans;Uid=root;Pwd=YOUR_PASSWORD;"
ORLEANS_ORACLE_CONNECTION_STRING="Data Source=localhost:1521/ORCL;User Id=system;Password=YOUR_PASSWORD;"

# 业务数据库
DB_BASIC_CONNECTION_STRING="Data Source=.;Initial Catalog=Basic;User Id=sa;Password=YOUR_PASSWORD;Integrated Security=True;Pooling=True;Max Pool Size=200;MultipleActiveResultSets=True;Encrypt=True;TrustServerCertificate=True"
DB_GAME_CONNECTION_STRING="Data Source=.;Initial Catalog=Game;User Id=sa;Password=YOUR_PASSWORD;Integrated Security=True;Pooling=True;Max Pool Size=200;MultipleActiveResultSets=True;Encrypt=True;TrustServerCertificate=True"
DB_ARTICLE_CONNECTION_STRING="Data Source=.;Initial Catalog=Article;User Id=sa;Password=YOUR_PASSWORD;Integrated Security=True;Pooling=True;Max Pool Size=200;MultipleActiveResultSets=True;Encrypt=True;TrustServerCertificate=True"
DB_SUPPORT_CONNECTION_STRING="Data Source=.;Initial Catalog=Support;User Id=sa;Password=YOUR_PASSWORD;Integrated Security=True;Pooling=True;Max Pool Size=200;MultipleActiveResultSets=True;Encrypt=True;TrustServerCertificate=True"
DB_XINGGUANG_CONNECTION_STRING="Data Source=.;Initial Catalog=Xingguang;User Id=sa;Password=YOUR_PASSWORD;Integrated Security=True;Pooling=True;Max Pool Size=200;MultipleActiveResultSets=True;Encrypt=True;TrustServerCertificate=True"

# Orleans AdoNet (WebApi)
ORLEANS_ADONET_CONNECTION_STRING="Data Source=.;Initial Catalog=Orleans;User Id=sa;Password=YOUR_PASSWORD;Integrated Security=True;Pooling=False;Max Pool Size=200;MultipleActiveResultSets=True;"
```

#### Orleans Dashboard
```bash
ORLEANS_DASHBOARD_USERNAME=your_dashboard_username
ORLEANS_DASHBOARD_PASSWORD=your_dashboard_password
```

#### 云服务 API 密钥
```bash
# 阿里云 OSS
ALI_OSS_ACCESS_KEY_ID=your_ali_access_key_id
ALI_OSS_ACCESS_KEY_SECRET=your_ali_access_key_secret

# 百度 AI
BAIDU_API_KEY=your_baidu_api_key
BAIDU_SECRET_KEY=your_baidu_secret_key

# 讯飞语音
XUNFEI_APP_ID=your_xunfei_app_id
XUNFEI_API_SECRET=your_xunfei_api_secret
XUNFEI_API_KEY=your_xunfei_api_key
```

#### 其他安全配置
```bash
PASSPORT_SECURITY_KEY=your_passport_security_key_16chars
```

### 3. 使用配置文件

编辑复制的 `appsettings.json` 文件，将所有 `${VARIABLE_NAME}` 占位符替换为实际的值，或者使用支持环境变量替换的配置系统。

### 4. 生产环境建议

对于生产环境，强烈建议使用以下方法之一管理敏感配置：

#### 选项 A: Azure Key Vault
```csharp
// 在 Program.cs 中添加
builder.Configuration.AddAzureKeyVault(
    new Uri($"https://{keyVaultName}.vault.azure.net/"),
    new DefaultAzureCredential());
```

#### 选项 B: AWS Secrets Manager
```csharp
// 使用 AWS Systems Manager Parameter Store
builder.Configuration.AddSystemsManager("/my-app/");
```

#### 选项 C: HashiCorp Vault
```csharp
// 集成 HashiCorp Vault
builder.Configuration.AddVaultConfiguration(options => {
    options.ConfigureVault(new VaultClientSettings(
        vaultUri, 
        new TokenAuthMethodInfo(vaultToken)));
});
```

#### 选项 D: Kubernetes Secrets
```yaml
apiVersion: v1
kind: Secret
metadata:
  name: hundunworld-secrets
type: Opaque
data:
  redis-password: <base64-encoded-password>
  db-password: <base64-encoded-password>
```

## 🔒 安全最佳实践

1. **永远不要提交包含真实凭证的配置文件**
2. **定期轮换所有密钥和密码**
3. **使用强密码（至少16个字符，包含大小写字母、数字和特殊字符）**
4. **为不同环境使用不同的凭证**
5. **限制对生产环境凭证的访问权限**
6. **启用审计日志记录所有敏感操作**
7. **定期审查和更新安全配置**

## 🚨 如果凭证泄露

如果您不小心提交了包含凭证的文件：

1. **立即更改所有泄露的密码和密钥**
2. **轮换所有 API 密钥**
3. **检查访问日志是否有异常活动**
4. **使用 `git filter-branch` 或 `BFG Repo-Cleaner` 从 Git 历史中删除敏感信息**
5. **强制推送清理后的历史记录**
6. **通知团队成员重新克隆仓库**

```bash
# 使用 BFG 清理敏感文件
bfg --delete-files appsettings.json
git reflog expire --expire=now --all
git gc --prune=now --aggressive
```

## 📖 更多信息

- [ASP.NET Core Configuration](https://docs.microsoft.com/en-us/aspnet/core/fundamentals/configuration/)
- [Azure Key Vault](https://azure.microsoft.com/en-us/services/key-vault/)
- [OWASP Configuration Management Cheat Sheet](https://cheatsheetseries.owasp.org/cheatsheets/Configuration_Management_Cheat_Sheet.html)

## 📧 联系方式

如有关于安全配置的问题，请联系项目安全负责人。

---

**最后更新**: 2026-02-07  
**维护者**: GitHub Copilot AI Agent
