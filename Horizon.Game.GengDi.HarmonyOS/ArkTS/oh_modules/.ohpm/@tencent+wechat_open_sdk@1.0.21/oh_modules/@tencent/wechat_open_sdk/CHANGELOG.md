#1.0.21
- Support `WXMusicVideoObject`

# 1.0.20
- Support `InvoiceAuthInsert`, `NontaxPay`, and `PayInsurance`
- Support payment reporting through `WXApi.setPayReportEnabled`, with an optional `UIAbilityContext` in `WXAPIFactory.createWXAPI`
- Support `WXWeWorkObject` for forwarding WeCom messages
- Breaking change: the unintentionally exposed internal helper `WXAPIFactory.parseWxInternalRespType` is no longer public. Pass incoming `Want` objects to `WXApi.handleWant` and receive parsed responses through `WXApiEventHandler.onResp` instead

# 1.0.19
- Fix the problem of no callback when canceling the system pop-up window with `openLink`
- Fix `WXApi.handleWant` incorrectly returning `true` when want is not actually consumed

# 1.0.18
- Support `GetA8Key`

# 1.0.17
- Support `SubscribeMessage`
- `IMediaObject.type` 改为只读

# 1.0.16
- Support `OpenBusinessWebView`

# 1.0.15
- Support `OpenCustomerServiceChat`

# 1.0.14
- Support `isSecretMessage` in `WXWebpageObject`

# 1.0.13
- Support `LaunchWxaRedirectingPageReq`

# 1.0.12
- Support `WXSceneTimeline` in `SendMessageToWXReq`
- Support `WXVideoObject`

# 1.0.11
- Fix issue because of conflicted `AppLink` scheme

# 1.0.10
- Support launching WeChat with `openLink`

# 1.0.9
- Support `WXFileObject`

# 1.0.8
- Fix issue for serialization

# 1.0.7
- Support `LaunchFromWXReq`

# 1.0.6
- Support `WXWebpageObject` & `WXMiniProgramObject` for `WXMediaMessage.mediaObject`

# 1.0.5
- Support `OpenBusinessView`

# 1.0.4
- Support `isWXAppInstalled`
- Add consumer-rules for obfuscation

# 1.0.3
- Support `LaunchMiniProgram`

# 1.0.2
- Fix issue `WXApi.sendReq` never returns (promise does not resolve) when `startAbility` succeed

# 1.0.1
- Add `callbackAbility` in `BaseReq`

# 1.0.0

init WeChat OpenSDK
