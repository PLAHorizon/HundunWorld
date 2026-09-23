# 微信「App 支付」商户号申办图文步骤

> 适用对象：耕地鸿蒙客户端，目标实现真机微信支付收银台（`PaymentScene.App`）。
> 前置：需要一台已实名认证的手机微信 + 一个可用的手机号，注册主体为「个人」「个体工商户」或「企业」均可，但 **App 支付要求主体已完成企业/个体认证**（个人主体无法开通 App 支付，请注意）。

---

## 0. 整体流程图

```mermaid
flowchart TD
    A[1 注册微信开放平台账号] --> B[2 创建“移动应用”并获取 AppID]
    B --> C[3 注册微信支付商户号<br/>（选择 App 支付）]
    C --> D[4 商户平台开通 API v3<br/>获取证书/密钥]
    D --> E[5 开放平台绑定商户号<br/>并关联 AppID]
    E --> F[6 配置到本项目<br/>appsettings.json / 环境变量]
    F --> G[7 真机拉收银台验证]
```

---

## 1. 注册微信开放平台账号（若已有可跳过）

- 访问 [open.weixin.qq.com](https://open.weixin.qq.com)，点击右上角「登录」。
- 用**企业/个体主体**的微信扫码登录，按提示完成**开发者资质认证**（认证费 300 元/年，企业主体）。
- ⚠️ 个人主体只能注册「个人开发者」，不开放 App 支付；如需正式支付必须企业/个体。

> 如果尚未认证，这一步是后续所有步骤的前置门槛，请优先完成。

---

## 2. 创建「移动应用」并获取 AppID

1. 登录开放平台 → 顶部菜单 **管理中心 → 移动应用 → 创建移动应用**。
2. 如实填写应用名称、简介、图标等；平台类型选择 **HarmonyOS / OpenHarmony**（如无该选项，选「Android」并在备注注明鸿蒙）。
3. 提交后进入审核。审核通过后，在应用的 **应用信息** 页拿到你的 **AppID**（形如 `wx8c0e...`）。
4. 记录该 AppID —— 后续服务端 `WECHAT_APP_ID` 与客户端 scheme 都使用它。

> 客户端的 `module.json5` 中，微信回调 scheme 需填 `wx` + 该 AppID（见第 6 步）。

---

## 3. 注册微信支付商户号（开通 App 支付产品）

1. 访问 [pay.weixin.qq.com](https://pay.weixin.qq.com)（微信支付商户平台）。
2. 点击「接入微信支付」→ 选择 **App 支付**，使用已认证的开放平台账号关联登录。
3. 按提示提交**营业执照、法人/经营者身份、银行结算账户**等主体资料，等待微信审核（1~3 个工作日）。
4. 审核通过后，在商户平台「账户中心 → 商户信息」中拿到你的 **商户号（mchid）**，形如 `1900001xxx`。
5. 同时完成 **商户平台的超级管理员/操作员角色绑定**，方便后续获取密钥。

> 申请时务必勾选产品类型 **App 支付**；若漏选，需后续在「产品中心 → App支付」手动开通。

---

## 4. 商户平台开通 API v3，获取证书与密钥

在 [pay.weixin.qq.com](https://pay.weixin.qq.com) 按顺序执行，全程需要商户超级管理员配合扫码授权：

### 4.1 获取 API v3 密钥
- **账户中心 → API安全 → 设置APIv3密钥**，设置一个 32 位密钥并妥善保存。
- → 对应配置项 `WECHAT_V3_SECRET`。

### 4.2 安装 API 证书（双向 TLS）
- **账户中心 → API安全 → 申请API证书**，按向导下载「微信支付证书工具」，导出**商户 API 证书**。
- 你会得到 **商户证书序列号** 和 **商户私钥（apiclient_key.pem）**。
- → 证书序列号对应 `WECHAT_CERT_SERIAL`，私钥对应 `WECHAT_PRIVATE_KEY`。

> 未申请 API 证书时，服务端将无法完成 v3 签名，下单会报「商户配置缺失/签名失败」。请务必完成。

---

## 5. 开放平台绑定商户号并关联应用

1. 回到微信开放平台 → 你创建的移动应用 → **支付能力 / 关联 AppID 与商户号**。
2. 将第 3 步的**商户号**和第 2 步的**AppID**进行绑定。
3. 确认绑定成功后，App v3 下单才能通过校验。

---

## 6. 把凭据配置到本项目

以下五种值，二选一填入即可（推荐环境变量，避免明文进仓库）：

| 配置项名 | 来源 | 说明 |
|---|---|---|
| `WECHAT_MERCHANT_ID` | 第 3 步 | 商户号 mchid |
| `WECHAT_APP_ID` | 第 2 步 | 开放平台移动应用 AppID |
| `WECHAT_V3_SECRET` | 第 4.1 步 | APIv3 32 位密钥 |
| `WECHAT_CERT_SERIAL` | 第 4.2 步 | 商户证书序列号 |
| `WECHAT_PRIVATE_KEY` | 第 4.2 步 | 商户私钥（PEM，含头尾） |
| `WECHAT_NOTIFY_URL` | 自行提供 | 公网 HTTPS 回调地址 |

### 方式 A：环境变量（推荐）
启动 Silo 前设置：
```powershell
$env:WECHAT_MERCHANT_ID="1900001xxx"
$env:WECHAT_APP_ID="wx8c0e..."
$env:WECHAT_V3_SECRET="32位密钥"
$env:WECHAT_CERT_SERIAL="证书序列号"
$env:WECHAT_PRIVATE_KEY="-----BEGIN PRIVATE KEY-----\n...\n-----END PRIVATE KEY-----"
$env:WECHAT_NOTIFY_URL="https://你的域名/FlowerPayment/callback/wechat"
```

### 方式 B：`appsettings.json`
编辑 `Horizon.Orleans.Silo/appsettings.json` 下的：
```json
"WechatPaySettings": {
  "MerchantId": "1900001xxx",
  "AppId": "wx8c0e...",
  "MerchantV3Secret": "32位密钥",
  "CertSerialNumber": "证书序列号",
  "PrivateKey": "-----BEGIN PRIVATE KEY-----...",
  "NotifyUrl": "https://你的域名/FlowerPayment/callback/wechat",
  "IsSandbox": false
}
```
> `IsSandbox` 必须为 `false`：微信沙箱不支持 App 收银台拉起。

### 客户端同步修改
1. `PaymentService.ets` 内 `MOCK_PAYMENT_ENABLED` 改为 `false`（关闭模拟，启用真实）。
2. `module.json5` 中微信回调 scheme `wxAPPID` 改为 `wx` + 你的 AppID（小写）。

---

## 7. 真机验证

1. 重启 Silo，确认日志无「商户配置缺失」报错。
2. 打包鸿蒙客户端并安装到真机。
3. 下单 → 选择微信支付 → 应能拉起微信收银台并完成支付。
4. 支付后服务端收到回调并更新订单为「已支付」。

---

## 常见问题

| 现象 | 原因 | 处理 |
|---|---|---|
| 无法创建「移动应用」 | 未完成开发者资质认证 | 完成企业/个体认证 |
| 申请 App 支付被拒 | 主体为个人 | 升级企业/个体主体 |
| 下单报「签名失败」 | APIv3 密钥或证书错 | 核对第 4 步密钥/证书 |
| `IsSandbox:true` 拉起不了收银台 | 沙箱不支持 App | 改为 `false` |
| 微信回调不到 | scheme 未配置或 NotifyUrl 无公网 | 核对第 6 步 |