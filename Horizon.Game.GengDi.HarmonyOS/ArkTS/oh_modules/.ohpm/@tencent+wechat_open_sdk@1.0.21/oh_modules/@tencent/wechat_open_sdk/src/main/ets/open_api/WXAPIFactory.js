import { Command, kWechatBundleName, kWechatEntryAbility } from '../model/Constants';
import bundleManager from "@ohos.bundle.bundleManager";
import wantConstant from "@ohos.app.ability.wantConstant";
import deviceInfo from '@ohos.deviceInfo';
import { SendAuthReq, SendAuthResp } from '../model/SendAuth';
import { JointPayReq, JointPayResp, PayReq, PayResp } from '../model/Pay';
import { Log } from '../log/Log';
import { SendTdiAuthReq, SendTdiAuthResp } from '../model/SendTdiAuth';
import { MediaObjectType, SendMessageToWXReq, SendMessageToWXResp } from '../model/message/SendMessageToWX';
import { LaunchMiniProgramReq, LaunchMiniProgramResp } from '../model/LaunchMiniProgram';
import { OpenBusinessViewReq, OpenBusinessViewResp } from '../model/OpenBusinessView';
import { LaunchFromWXReq } from '../model/LaunchFromWX';
import { LaunchWxaRedirectingPageReq, LaunchWxaRedirectingPageResp } from '../model/LaunchWxaRediectingPage';
import { OpenCustomerServiceChatReq, OpenCustomerServiceChatResp } from '../model/OpenCustomerServiceChat';
import { OpenBusinessWebViewReq, OpenBusinessWebViewResp } from '../model/OpenBusinessWebView';
import { OpenWebViewReq, OpenWebViewResp } from '../model/OpenWebView';
import { SubscribeMessageReq, SubscribeMessageResp } from '../model/SubscribeMessage';
import { InvoiceAuthInsertReq, InvoiceAuthInsertResp } from '../model/InvoiceAuthInsert';
import { NontaxPayReq, NontaxPayResp } from '../model/NontaxPay';
import { PayInsuranceReq, PayInsuranceResp } from '../model/PayInsurance';
import { GetA8KeyReq, GetA8KeyResp } from '../model/GetA8Key';
import url from "@ohos.url";
import fileIo from "@ohos.file.fs";
import BuildProfile from '../../../../BuildProfile';
import { PayReportHelper } from '../log/PayReportHelper';
const kWantCallerBundleNameKey = "ohos.aafwk.param.callerBundleName";
const kWantRequestKey = "wxopensdk_request";
const kWantResponseKey = "wxopensdk_response";
const kWantAppidKey = "wxopensdk_appid";
const kWantSdkVersion = "wxopensdk_sdk_version";
const kHdAlbumThumbFdKey = "wxopensdk_musicvideo_hd_album_thumb_fd";
const kWechatBundleNameCandidates = [
    "com.tencent.wechat.debug",
    "com.tencent.wechat.red",
    "com.tencent.wechat.purple",
    "com.tencent.wechat.blue",
    "com.tencent.wechat.yellow"
];
const kWechatLink = "wxopensdk://open_sdk_request_link";
const kTag = 'wxopensdk::WXApi';
export class WXAPIFactory {
    static createWXAPI(r17, s17) {
        return new WXApiImpl(r17, s17);
    }
    static createReq(o17) {
        let p17 = o17['__type__'] ?? -1;
        let q17 = (() => {
            switch (p17) {
                case Command.kCommandSendAuth: return new SendAuthReq;
                case Command.kCommandSendTdiAuth: return new SendTdiAuthReq;
                case Command.kCommandSendMessageToWX: return new SendMessageToWXReq;
                case Command.kCommandPay: return new PayReq();
                case Command.kCommandJointPay: return new JointPayReq();
                case Command.kCommandLaunchFromWX: return new LaunchFromWXReq;
                case Command.kCommandLaunchMiniProgram: return new LaunchMiniProgramReq;
                case Command.kCommandOpenWebView: return new OpenWebViewReq;
                case Command.kCommandOpenBusinessWebView: return new OpenBusinessWebViewReq;
                case Command.kCommandOpenBusinessView: return new OpenBusinessViewReq;
                case Command.kCommandWxaRedirectingPage: return new LaunchWxaRedirectingPageReq;
                case Command.kCommandOpenCustomerServiceChat: return new OpenCustomerServiceChatReq;
                case Command.kCommandSubscribeMessage: return new SubscribeMessageReq;
                case Command.kCommandInvoiceAuthInsert: return new InvoiceAuthInsertReq;
                case Command.kCommandNontaxPay: return new NontaxPayReq;
                case Command.kCommandPayInsurance: return new PayInsuranceReq;
                case Command.kCommandGetA8Key: return new GetA8KeyReq;
                default: return;
            }
        })();
        q17?.deserializeFrom(o17);
        return q17;
    }
    static createResp(l17) {
        let m17 = l17['__type__'] ?? -1;
        let n17 = (() => {
            switch (m17) {
                case Command.kCommandSendAuth: return new SendAuthResp;
                case Command.kCommandSendTdiAuth: return new SendTdiAuthResp;
                case Command.kCommandSendMessageToWX: return new SendMessageToWXResp;
                case Command.kCommandPay: return new PayResp;
                case Command.kCommandJointPay: return new JointPayResp;
                case Command.kCommandLaunchMiniProgram: return new LaunchMiniProgramResp;
                case Command.kCommandOpenWebView: return new OpenWebViewResp;
                case Command.kCommandOpenBusinessWebView: return new OpenBusinessWebViewResp;
                case Command.kCommandOpenBusinessView: return new OpenBusinessViewResp;
                case Command.kCommandWxaRedirectingPage: return new LaunchWxaRedirectingPageResp;
                case Command.kCommandOpenCustomerServiceChat: return new OpenCustomerServiceChatResp;
                case Command.kCommandSubscribeMessage: return new SubscribeMessageResp;
                case Command.kCommandInvoiceAuthInsert: return new InvoiceAuthInsertResp;
                case Command.kCommandNontaxPay: return new NontaxPayResp;
                case Command.kCommandPayInsurance: return new PayInsuranceResp;
                case Command.kCommandGetA8Key: return new GetA8KeyResp;
                default: return;
            }
        })();
        n17?.deserializeFrom(l17);
        return n17;
    }
}
function parseWxInternalRespType(z16) {
    try {
        let b17;
        try {
            b17 = url.URL.parseURL(z16).params;
        }
        catch (h17) {
            let i17;
            const j17 = z16.indexOf('?');
            if (j17 !== -1) {
                i17 = z16.substring(j17 + 1);
            }
            else {
                i17 = z16;
            }
            try {
                b17 = new url.URLParams(i17);
            }
            catch (k17) {
                Log.e(kTag, `parseWxInternalRespType fallback params error: ${k17.message}, queryStr: ${i17}`);
                return undefined;
            }
        }
        let c17 = b17.get('wx_internal_resptype') ?? '';
        if (c17 === 'subscribemessage') {
            let g17 = new SubscribeMessageResp();
            g17.errCode = Number(b17.get('ret') ?? '0') || 0;
            g17.openId = b17.get('openid') ?? '';
            g17.templateId = b17.get('template_id') ?? '';
            g17.scene = Number(b17.get('scene') ?? '0') || 0;
            g17.action = b17.get('action') ?? '';
            g17.reserved = b17.get('reserved') ?? '';
            return g17;
        }
        else if (c17 === 'invoice_auth_insert') {
            let f17 = new InvoiceAuthInsertResp();
            f17.errCode = Number(b17.get('ret') ?? '0') || 0;
            f17.wxOrderId = b17.get('wx_order_id') ?? '';
            return f17;
        }
        else if (c17 === 'nontaxpay') {
            let e17 = new NontaxPayResp();
            e17.errCode = Number(b17.get('ret') ?? '0') || 0;
            e17.wxOrderId = b17.get('wx_order_id') ?? '';
            return e17;
        }
        else if (c17 === 'payinsurance') {
            let d17 = new PayInsuranceResp();
            d17.errCode = Number(b17.get('ret') ?? '0') || 0;
            d17.wxOrderId = b17.get('wx_order_id') ?? '';
            return d17;
        }
        else {
            Log.i(kTag, `parseWxInternalRespType, unknown extInfo:${z16}`);
        }
    }
    catch (a17) {
        Log.e(kTag, `parseWxInternalRespType fail ${a17.message}, extInfo:${z16}`);
    }
    return undefined;
}
class WXApiImpl {
    constructor(x16, y16) {
        this.resolves = new Map();
        this.appId = x16;
        PayReportHelper.init(y16, x16);
    }
    static reqToWantObject(v16) {
        if (v16) {
            let w16 = {};
            v16.serializeTo(w16);
            if (w16['__type__'] !== v16.type) {
                Log.e(kTag, 'super.serializedTo must be called');
            }
            return w16;
        }
        else {
            return undefined;
        }
    }
    makeParameters(h16) {
        let i16 = {};
        let j16 = [];
        i16[kWantRequestKey] = WXApiImpl.reqToWantObject(h16) ?? "";
        i16[kWantAppidKey] = this.appId;
        i16[kWantSdkVersion] = BuildProfile.HAR_VERSION;
        i16["from"] = "_wechat_open_sdk";
        i16["linkFeature"] = "Pay";
        if (h16 && h16.type === Command.kCommandSendMessageToWX) {
            let k16 = h16;
            if (k16.message?.mediaObject?.type === MediaObjectType.TYPE_OPENSDK_WEWORK_OBJECT) {
                let l16 = k16.message.mediaObject;
                if (l16.extInfo != undefined && l16.extInfo.length > 0) {
                    let m16;
                    try {
                        let u16 = JSON.parse(l16.extInfo);
                        m16 = u16['file_info'];
                    }
                    catch (t16) {
                        Log.e(kTag, `makeParameters, parse weWork extInfo failed: ${t16.message}`);
                    }
                    if (m16 && Array.isArray(m16)) {
                        for (let n16 of m16) {
                            let o16 = n16['msgId'];
                            let p16 = n16['uri'];
                            if (o16 != undefined && p16 != undefined) {
                                try {
                                    let r16 = fileIo.openSync(p16, fileIo.OpenMode.READ_ONLY);
                                    j16.push(r16.fd);
                                    let s16 = { 'type': 'FD', 'value': r16.fd };
                                    i16[o16] = s16;
                                    i16[o16 + '_uri'] = p16;
                                }
                                catch (q16) {
                                    Log.e(kTag, `makeParameters, openSync uri failed: ${q16.message}, uri: ${p16}`);
                                }
                            }
                        }
                    }
                }
            }
        }
        if (j16.length > 0) {
            Log.i(kTag, `makeParameters, openedFds: ${j16}`);
        }
        return { parameters: i16, openedFds: j16 };
    }
    makeWant(v15, w15) {
        let x15 = undefined;
        let y15 = undefined;
        let z15 = undefined;
        if (v15 && v15.type === Command.kCommandSendMessageToWX) {
            let c16 = v15;
            if (c16.checkArgs()) {
                if (c16.message?.mediaObject?.type === MediaObjectType.TYPE_IMAGE) {
                    let g16 = c16.message?.mediaObject;
                    x15 = g16.uri;
                }
                else if (c16.message?.mediaObject?.type === MediaObjectType.TYPE_FILE) {
                    let f16 = c16.message?.mediaObject;
                    x15 = f16.fileUri;
                }
                else if (c16.message?.mediaObject?.type === MediaObjectType.TYPE_VIDEO_FILE) {
                    let e16 = c16.message?.mediaObject;
                    x15 = e16.fileUri;
                }
                else if (c16.message?.mediaObject?.type === MediaObjectType.TYPE_MUSIC_VIDEO) {
                    let d16 = c16.message?.mediaObject;
                    x15 = d16.hdAlbumThumbFileUri;
                }
            }
        }
        else if (v15 && v15.type === Command.kCommandWxaRedirectingPage) {
            x15 = v15.mediaUri?.trim();
        }
        if (x15 != undefined && x15.length > 0) {
            y15 = 'ohos.want.action.sendData';
            z15 = wantConstant.Flags.FLAG_AUTH_READ_URI_PERMISSION;
        }
        let a16 = this.makeParameters(v15);
        let b16 = {
            bundleName: w15 ?? kWechatBundleName,
            abilityName: kWechatEntryAbility,
            parameters: a16.parameters,
            action: y15,
            uri: x15,
            flags: z15
        };
        return { want: b16, openedFds: a16.openedFds };
    }
    openWeChatWithLink(b15, c15, d15) {
        const e15 = PayReportHelper.beginLaunch(d15, 'link');
        return new Promise(async (f15) => {
            const g15 = deviceInfo.sdkApiVersion >= 21;
            let h15;
            if (g15) {
                let q15 = false;
                h15 = {
                    onRequestSuccess: (t15, u15) => {
                        Log.i(kTag, `openWeChatWithLink onRequestSuccess: ${u15}`);
                        if (!q15) {
                            q15 = true;
                            PayReportHelper.reportLaunchSuccess(d15, 'link', 'launch_ok_completion', e15);
                            f15(true);
                        }
                    },
                    onRequestFailure: (r15, s15) => {
                        Log.e(kTag, `openWeChatWithLink onRequestFailure: ${s15}`);
                        if (!q15) {
                            q15 = true;
                            PayReportHelper.reportLinkCompletionFailure(d15, s15 ?? '');
                            f15(false);
                        }
                    }
                };
            }
            let i15 = this.makeParameters(c15);
            const j15 = {
                appLinkingOnly: false,
                parameters: i15.parameters,
                completionHandler: h15
            };
            try {
                PayReportHelper.flushBeforeLaunch(d15);
                await b15.openLink(kWechatLink, j15, (o15, p15) => {
                    if (o15 && o15.code != 0) {
                        Log.e(kTag, `openWeChatWithLink result err: ${o15.message}`);
                        PayReportHelper.reportLinkResultError(d15, o15.code);
                        return;
                    }
                    this.handleResult(p15);
                });
                if (!h15) {
                    PayReportHelper.reportLaunchSuccess(d15, 'link', 'launch_ok_optimistic', e15, { 'optimistic': true });
                    f15(true);
                }
            }
            catch (m15) {
                const n15 = m15?.code;
                Log.e(kTag, `openWeChatWithLink fail by err: ${m15.message}`);
                PayReportHelper.reportLinkThrow(d15, n15);
                f15(false);
            }
            finally {
                for (const k15 of i15.openedFds) {
                    try {
                        fileIo.closeSync(k15);
                    }
                    catch (l15) {
                        Log.e(kTag, `close fd failed: ${l15.message}`);
                    }
                }
            }
        });
    }
    openWechatWithWant(o14, p14, q14) {
        const r14 = PayReportHelper.beginLaunch(q14, 'want');
        return new Promise((s14) => {
            let t14 = [];
            t14.push(kWechatBundleName);
            t14.push(...kWechatBundleNameCandidates);
            (async () => {
                PayReportHelper.flushBeforeLaunch(q14);
                let u14 = false;
                for (let v14 of t14) {
                    let w14 = this.makeWant(p14, v14);
                    try {
                        await o14.startAbility(w14.want);
                        u14 = true;
                        PayReportHelper.reportLaunchSuccess(q14, 'want', 'startability_ok', r14, { 'bundle': v14 });
                        s14(true);
                        return;
                    }
                    catch (z14) {
                        const a15 = z14?.code;
                        Log.e(kTag, `openWechatWithWant fail by err: ${z14.message}`);
                        PayReportHelper.reportWantAttemptFailure(q14, v14, a15);
                    }
                    finally {
                        for (const x14 of w14.openedFds) {
                            try {
                                fileIo.closeSync(x14);
                            }
                            catch (y14) {
                                Log.e(kTag, `close fd failed: ${y14.message}`);
                            }
                        }
                    }
                }
                if (!u14) {
                    PayReportHelper.reportWantExhausted(q14, this.isWXAppInstalled());
                }
                s14(u14);
            })();
        });
    }
    handleResult(m14) {
        Log.i(kTag, `handleResult, result = ${JSON.stringify(m14)}`);
        const n14 = PayReportHelper.beginAbilityResult(m14.want);
        if (m14.resultCode != 0) {
            PayReportHelper.reportInvalidAbilityResult(n14, m14.resultCode);
            return;
        }
        PayReportHelper.completeAbilityResult(n14, m14.want);
        this.handleWantInternal(m14.want);
    }
    handleWantInternal(f14) {
        if (f14?.parameters === undefined) {
            return false;
        }
        let g14 = f14.parameters[kWantRequestKey];
        if (g14) {
            let j14 = WXAPIFactory.createReq(g14);
            if (j14 instanceof LaunchFromWXReq) {
                let k14 = j14.message?.messageExt;
                if (k14 && k14.includes('wx_internal_resptype')) {
                    let l14 = parseWxInternalRespType(k14);
                    if (l14) {
                        this.eventHandler?.onResp(l14);
                        return true;
                    }
                }
            }
            if (j14) {
                this.eventHandler?.onReq(j14);
                return true;
            }
        }
        let h14 = f14.parameters[kWantResponseKey];
        if (h14) {
            let i14 = WXAPIFactory.createResp(h14);
            if (i14) {
                this.eventHandler?.onResp(i14);
                return true;
            }
            return false;
        }
        return false;
    }
    canOpenWithLink(v13, w13) {
        try {
            if (!bundleManager.canOpenLink(kWechatLink)) {
                PayReportHelper.reportLinkUnavailable(w13);
                return false;
            }
        }
        catch (d14) {
            let e14 = d14?.code;
            Log.e(kTag, `canOpenLink fail by err: ${d14.message}`);
            PayReportHelper.reportLinkCheckError(w13, e14);
            return false;
        }
        if (v13.type === Command.kCommandSendMessageToWX) {
            const y13 = v13;
            if (y13.message?.mediaObject?.type === MediaObjectType.TYPE_IMAGE) {
                const c14 = y13.message.mediaObject;
                return c14.uri === undefined || c14.uri.trim().length === 0;
            }
            else if (y13.message?.mediaObject?.type === MediaObjectType.TYPE_FILE) {
                const b14 = y13.message.mediaObject;
                return b14.fileUri === undefined || b14.fileUri.trim().length === 0;
            }
            else if (y13.message?.mediaObject?.type === MediaObjectType.TYPE_VIDEO_FILE) {
                const a14 = y13.message.mediaObject;
                return a14.fileUri === undefined || a14.fileUri.trim().length === 0;
            }
            else if (y13.message?.mediaObject?.type === MediaObjectType.TYPE_MUSIC_VIDEO) {
                const z13 = y13.message.mediaObject;
                return z13.hdAlbumThumbFilePath === undefined ||
                    z13.hdAlbumThumbFilePath.trim().length === 0;
            }
        }
        else if (v13.type === Command.kCommandWxaRedirectingPage) {
            const x13 = v13;
            return x13.mediaUri === undefined || x13.mediaUri.trim().length === 0;
        }
        return true;
    }
    sendReq(q13, r13) {
        let s13 = undefined;
        try {
            s13 = PayReportHelper.beginRequest(q13, r13);
        }
        catch (u13) {
            Log.e(kTag, `begin pay report fail: ${u13}`);
        }
        let t13;
        if (this.canOpenWithLink(r13, s13)) {
            Log.i(kTag, 'sendReq, use link');
            t13 = this.openWeChatWithLink(q13, r13, s13);
        }
        else {
            Log.i(kTag, 'sendReq, use want');
            t13 = this.openWechatWithWant(q13, r13, s13);
        }
        return t13;
    }
    openWechat(p13) {
        return this.openWechatWithWant(p13, undefined);
    }
    handleWant(k13, l13) {
        this.eventHandler = l13;
        const m13 = k13.parameters ? k13.parameters[kWantCallerBundleNameKey] : undefined;
        const n13 = typeof m13 === 'string' &&
            (m13 === kWechatBundleName || kWechatBundleNameCandidates.includes(m13));
        try {
            PayReportHelper.reportCallbackAsync(k13, n13);
        }
        catch (o13) {
            Log.e(kTag, `schedule pay callback report fail: ${o13}`);
        }
        if (!n13) {
            return false;
        }
        return this.handleWantInternal(k13);
    }
    setPayReportEnabled(j13) {
        PayReportHelper.setEnabled(j13);
    }
    isWXAppInstalled() {
        try {
            return bundleManager.canOpenLink("weixin://");
        }
        catch (g13) {
            let h13 = g13?.code;
            let i13 = g13?.message ?? '';
            if (h13 !== undefined) {
                if (h13 === 17700056) {
                    i13 += ` Please include "weixin" inside the "querySchemes" element of module.json5 in your app module.`;
                }
                Log.e(kTag, `isWXAppInstalled get error ${i13}`);
            }
            else {
                Log.e(kTag, `isWXAppInstalled get error ${g13}`);
            }
            return false;
        }
    }
    isWXAppSupportFunc(d13) {
        try {
            let f13 = `wxopensdk://open_sdk_${d13}`;
            return bundleManager.canOpenLink(f13);
        }
        catch (e13) {
            Log.e(kTag, `isWXAppSupportFunc ${d13} canOpenLink err: ${e13.message}`);
            return false;
        }
    }
}
WXApiImpl.NextId = 0;
