import hilog from "@ohos.hilog";
import common from "@ohos.app.ability.common";
import { wantConstant, Want } from "@kit.AbilityKit";
import { ApiClient, PaymentStateDto } from "./ApiClient";
import { WXAPIFactory, PayReq, WXApi } from "@tencent/wechat_open_sdk";

const TAG = 'GengDi.PaymentService';

/**
 * 支付渠道（与服务端 PaymentChannel 枚举对齐）
 */
export const PAY_CHANNEL_WECHAT = 0;
export const PAY_CHANNEL_ALIPAY = 1;

/**
 * 模拟支付开关。
 * 当前尚未配置真实微信/支付宝商户，置 true 时跳过真实收银台，直接标记支付成功，
 * 以保证无商户也能完整跑通「下单→支付→订单流转」流程。真实商户配置到位后改为 false。
 */
export const MOCK_PAYMENT_ENABLED = true;

/** 支付拉起结果 */
export class LaunchResult {
    launched: boolean = false;
    message: string = '';
}

/** 支付最终结果 */
export class PayResult {
    success: boolean = false;
    message: string = '';
}

/**
 * 正式支付服务：服务端预下单 → 拉起微信/支付宝 App → 轮询支付结果。
 *
 * 支付宝：服务端返回 RSA2 签名 orderString，通过 alipays:// scheme 拉起支付宝收银台（无需客户端 SDK）。
 * 微信：需集成官方鸿蒙 SDK（@tencent/wechat_open_sdk，ohpm install 后在 launchWechatPay 中
 *       注册 AppId 并发送 PayReq）。服务端已返回 appId/partnerId/prepayId/nonceStr/timeStamp/sign。
 */
export class PaymentService {
    private static instance: PaymentService | null = null;
    static getInstance(): PaymentService {
        if (PaymentService.instance === null) {
            PaymentService.instance = new PaymentService();
        }
        return PaymentService.instance;
    }

    /**
     * 完整支付流程：预下单 → 拉起支付 App → 轮询确认。
     * @param orderId 服务端订单ID
     * @param channel PAY_CHANNEL_WECHAT / PAY_CHANNEL_ALIPAY
     * @param amount 支付金额（服务端以订单实价为准）
     * @param onProgress 进度回调（如「正在拉起支付宝...」「等待支付确认...」）
     */
    async payAsync(orderId: number, channel: number, amount: number,
        onProgress?: (msg: string) => void): Promise<PayResult> {
        const result = new PayResult();
        try {
            const notify = (msg: string): void => {
                if (onProgress) {
                    onProgress(msg);
                }
            };

            // 模拟支付：未配置真实商户时直接标记成功，跑通完整订单流程
            if (MOCK_PAYMENT_ENABLED) {
                notify('模拟支付成功');
                hilog.info(0x0001, TAG, '模拟支付成功: orderId=%{public}d', orderId);
                result.success = true;
                result.message = '支付成功（模拟）';
                return result;
            }

            // 1. 服务端预下单（含渠道配置校验；返回拉起参数）
            notify('正在创建支付订单...');
            const payment = await ApiClient.getInstance().initiatePayment(orderId, channel, amount);
            if (payment.transactionId <= 0) {
                result.message = '创建支付交易失败';
                return result;
            }

            // 2. 拉起支付 App
            const launch = await this.launchPaymentApp(channel, payment.payParams);
            if (!launch.launched) {
                result.message = launch.message;
                return result;
            }

            // 3. 轮询支付结果（3 秒/次，最多 40 次 = 2 分钟）
            notify('等待支付确认...');
            return await this.pollPaymentResult(payment.transactionId);
        }
        catch (e) {
            const err = e as Error;
            hilog.warn(0x0001, TAG, '支付流程异常: %{public}s', err.message);
            result.message = err.message.length > 0 ? err.message : '支付失败，请重试';
            return result;
        }
    }

    /** 仅拉起支付 App（支付宝 scheme / 微信 SDK） */
    async launchPaymentApp(channel: number, payParamsJson: string): Promise<LaunchResult> {
        const result = new LaunchResult();
        try {
            if (payParamsJson.length === 0) {
                result.message = '服务端未返回支付参数';
                return result;
            }
            const params = JSON.parse(payParamsJson) as Record<string, Object>;

            if (channel === PAY_CHANNEL_ALIPAY) {
                return this.launchAlipay(String(params['orderInfo'] ?? ''));
            }
            if (channel === PAY_CHANNEL_WECHAT) {
                return this.launchWechatPay(params);
            }
            result.message = '不支持的支付渠道';
            return result;
        }
        catch (e) {
            result.message = '支付参数解析失败';
            return result;
        }
    }

    /**
     * 拉起支付宝收银台：alipays://platformapi/startapp?appId=20000067&orderSuffix=<orderString>。
     * 通过 startAbility(want) 打开 scheme；设备未安装支付宝时系统返回错误。
     */
    private async launchAlipay(orderInfo: string): Promise<LaunchResult> {
        const result = new LaunchResult();
        if (orderInfo.length === 0) {
            result.message = '支付宝支付参数缺失';
            return result;
        }
        try {
            const suffix = encodeURIComponent(orderInfo);
            const uri = `alipays://platformapi/startapp?appId=20000067&orderSuffix=${suffix}`;
            await this.openUri(uri);
            result.launched = true;
            result.message = '已拉起支付宝';
            return result;
        }
        catch (e) {
            result.message = '拉起支付宝失败（请确认已安装支付宝App）';
            return result;
        }
    }

    /**
     * 拉起微信支付（@tencent/wechat_open_sdk）。
     * 服务端已生成拉起参数（appId/partnerId/prepayId/nonceStr/timeStamp/package/sign/signType），
     * 此处构造 PayReq 并 sendReq 拉起微信收银台。
     */
    private async launchWechatPay(params: Record<string, Object>): Promise<LaunchResult> {
        const result = new LaunchResult();
        try {
            const appId = String(params['appId'] ?? '');
            const prepayId = String(params['prepayId'] ?? '');
            const partnerId = String(params['partnerId'] ?? '');
            const nonceStr = String(params['nonceStr'] ?? '');
            const timeStamp = String(params['timeStamp'] ?? '');
            const packageValue = String(params['package'] ?? 'Sign=WXPay');
            const sign = String(params['sign'] ?? '');
            const signType = String(params['signType'] ?? 'RSA');

            if (appId.length === 0 || prepayId.length === 0 || partnerId.length === 0 || sign.length === 0) {
                result.message = '微信支付参数缺失（appId/prepayId/partnerId/sign）';
                return result;
            }

            // 创建 WXApi（appId 取自服务端支付参数），发送支付请求
            const wxApi = WXAPIFactory.createWXAPI(appId);
            PaymentService.wxApi = wxApi;
            PaymentService.currentWxAppId = appId;

            const req = new PayReq();
            req.appId = appId;
            req.partnerId = partnerId;
            req.prepayId = prepayId;
            req.nonceStr = nonceStr;
            req.timeStamp = timeStamp;
            req.packageValue = packageValue;
            req.sign = sign;
            req.signType = signType;
            req.callbackAbility = 'EntryAbility';

            const context = this.getUIAbilityContext();
            const sendResult = wxApi.sendReq(context, req);
            const ok = (typeof sendResult === 'boolean') ? sendResult : await sendResult;
            if (ok) {
                result.launched = true;
                result.message = '已拉起微信';
            }
            else {
                result.message = '拉起微信失败（请确认已安装微信APP）';
            }
            return result;
        }
        catch (e) {
            hilog.warn(0x0001, TAG, '微信支付拉起异常: %{public}s', (e as Error).message);
            result.message = '拉起微信支付异常，请重试';
            return result;
        }
    }

    /** 微信支付 SDK 实例（供 EntryAbility 回调处理） */
    private static wxApi: WXApi | null = null;
    /** 最近一次拉起微信使用的 AppId */
    private static currentWxAppId: string = '';

    /** 获取已创建的 WXApi（无则用 appId 懒创建） */
    private static getWxApi(appId: string): WXApi | null {
        if (PaymentService.wxApi === null) {
            PaymentService.wxApi = WXAPIFactory.createWXAPI(appId);
        }
        return PaymentService.wxApi;
    }

    /**
     * 处理微信跳回宿主 App 的 WANT（在 EntryAbility.onNewWant 中调用）。
     * 该方法将微信支付的响应结果分发到注册的回调。
     */
    handleWxWant(want: Want, appId?: string): void {
        try {
            const wxAppId = (appId && appId.length > 0) ? appId : PaymentService.currentWxAppId;
            const wxApi = PaymentService.getWxApi(wxAppId);
            if (wxApi === null) {
                hilog.warn(0x0001, TAG, 'handleWxWant: wxApi 未创建');
                return;
            }
            const handled = wxApi.handleWant(want, {
                onReq: (req: Object): void => {
                    hilog.info(0x0001, TAG, '微信 onReq 收到请求');
                },
                onResp: (resp: Object): void => {
                    const anyResp = resp as Record<string, Object>;
                    hilog.info(0x0001, TAG, '微信 onResp 收到支付结果: errCode=%{public}s',
                        String(anyResp['errCode'] ?? ''));
                    PaymentService.lastWxRespCode = Number(anyResp['errCode'] ?? -1);
                }
            });
            hilog.info(0x0001, TAG, 'handleWant 处理结果: %{public}s', String(handled));
        }
        catch (e) {
            hilog.warn(0x0001, TAG, 'handleWxWant 异常: %{public}s', (e as Error).message);
        }
    }

    /** 最近一次微信回调的 errCode（0 表示支付成功确认 / -2 用户取消），供 UI 查询 */
    private static lastWxRespCode: number = -99;

    /** 轮询支付状态：3 秒/次最多 40 次；status==1 视为支付成功 */
    private async pollPaymentResult(transactionId: number): Promise<PayResult> {
        const result = new PayResult();
        const maxAttempts = 40;
        for (let i = 0; i < maxAttempts; i++) {
            await this.sleep(3000);
            try {
                const state = await ApiClient.getInstance().queryPaymentStatus(transactionId);
                if (state.status === 1) {
                    result.success = true;
                    result.message = '支付成功';
                    return result;
                }
                if (state.status === 2) {
                    result.message = '交易已关闭';
                    return result;
                }
            }
            catch (e) {
                // 单次查询失败继续轮询（网络抖动容错）
                hilog.warn(0x0001, TAG, '支付状态查询失败(第%{public}d次): %{public}s', i, (e as Error).message);
            }
        }
        result.message = '支付结果确认超时，请稍后在订单列表查看';
        return result;
    }

    /** 通过隐式 Want 打开外部 scheme（支付宝等 App 拉起） */
    private async openUri(uri: string): Promise<void> {
        const context = this.getUIAbilityContext();
        // ArkTS 严格模式：对象字面量需显式类型
        const want: Record<string, Object> = {
            'action': 'ohos.want.action.viewData',
            'uri': uri,
            'flags': wantConstant.Flags.FLAG_AUTH_READ_URI_PERMISSION
        };
        await context.startAbility(want);
    }

    /** 获取 UIAbility 上下文（App 内单 Ability） */
    private getUIAbilityContext(): common.UIAbilityContext {
        const context = getContext(this) as common.UIAbilityContext;
        return context;
    }

    private sleep(ms: number): Promise<void> {
        return new Promise<void>((resolve: (value: void) => void) => {
            setTimeout(() => resolve(), ms);
        });
    }
}
