import http from "@ohos.net.http";
import { Log } from '../log/Log';
const kTag = 'wxopensdk::DiffDevOAuth';
export var OAuthErrCode;
(function (g1) {
    g1[g1["WechatAuth_Err_OK"] = 0] = "WechatAuth_Err_OK";
    g1[g1["WechatAuth_Err_NormalErr"] = -1] = "WechatAuth_Err_NormalErr";
    g1[g1["WechatAuth_Err_NetworkErr"] = -2] = "WechatAuth_Err_NetworkErr";
    g1[g1["WechatAuth_Err_JsonDecodeErr"] = -3] = "WechatAuth_Err_JsonDecodeErr";
    g1[g1["WechatAuth_Err_Cancel"] = -4] = "WechatAuth_Err_Cancel";
    g1[g1["WechatAuth_Err_Timeout"] = -5] = "WechatAuth_Err_Timeout";
    g1[g1["WechatAuth_Err_Auth_Stopped"] = -6] = "WechatAuth_Err_Auth_Stopped";
    g1[g1["WechatAuth_Err_Param_Err"] = -7] = "WechatAuth_Err_Param_Err";
})(OAuthErrCode || (OAuthErrCode = {}));
export class DiffDevOAuthFactory {
    constructor() {
    }
    static getDiffDevOAuth(f1 = DiffDevOAuthFactory.MAX_SUPPORTED_VERSION) {
        if (f1 > DiffDevOAuthFactory.MAX_SUPPORTED_VERSION) {
            return null;
        }
        switch (f1) {
            case DiffDevOAuthFactory.VERSION_1:
                return new DiffDevOAuthImplV1();
            default:
                return null;
        }
    }
}
DiffDevOAuthFactory.VERSION_1 = 1;
DiffDevOAuthFactory.MAX_SUPPORTED_VERSION = DiffDevOAuthFactory.VERSION_1;
var UUIDStatusCodeEnum;
(function (e1) {
    e1[e1["UUID_EXPIRED"] = 402] = "UUID_EXPIRED";
    e1[e1["UUID_CANCELED"] = 403] = "UUID_CANCELED";
    e1[e1["UUID_SCANNED"] = 404] = "UUID_SCANNED";
    e1[e1["UUID_CONFIRM"] = 405] = "UUID_CONFIRM";
    e1[e1["UUID_KEEP_CONNECT"] = 408] = "UUID_KEEP_CONNECT";
    e1[e1["UUID_SERVER_ERROR"] = 500] = "UUID_SERVER_ERROR";
})(UUIDStatusCodeEnum || (UUIDStatusCodeEnum = {}));
class DiffDevOAuthImplV1 {
    constructor() {
        this.started = false;
        this.callback = null;
        this.isCanceled = false;
        this.lastUUIDStatusCode = 0;
    }
    startOAuth(y, z, a1, b1, c1, d1) {
        this.callback = d1;
        this.startOAuthInner(y, z, a1, b1, c1);
    }
    async startOAuthInner(j, k, l, m, n) {
        if (this.started) {
            this.callback?.onAuthError(OAuthErrCode.WechatAuth_Err_Auth_Stopped, 'OAuth is already started');
            return;
        }
        this.started = true;
        if (j.length === 0 || k.length === 0) {
            this.callback?.onAuthError(OAuthErrCode.WechatAuth_Err_Param_Err, 'appId or scope is empty');
            return;
        }
        const o = `https://open.weixin.qq.com/connect/sdk/qrconnect?appid=${j}&noncestr=${l}&timestamp=${m}&scope=${k}&signature=${n}`;
        const p = http.createHttp();
        try {
            const r = await p.request(o);
            const s = r.result;
            if (s.length === 0) {
                this.callback?.onAuthError(OAuthErrCode.WechatAuth_Err_NetworkErr, 'response data is empty');
                return;
            }
            Log.i(kTag, `qrconect get data ${s}`);
            const t = JSON.parse(s);
            if (t['errcode'] !== 0) {
                this.callback?.onAuthError(OAuthErrCode.WechatAuth_Err_NormalErr, `response errcode: ${t["errcode"]} errmsg: ${t["errmsg"]}`);
                return;
            }
            const u = t['uuid'];
            if (u.length === 0) {
                this.callback?.onAuthError(OAuthErrCode.WechatAuth_Err_JsonDecodeErr, 'uuid is empty');
                return;
            }
            const v = t.qrcode;
            const w = v['qrcodebase64'];
            const x = v['qrcodelength'];
            if (w.length === 0 || x === 0) {
                this.callback?.onAuthError(OAuthErrCode.WechatAuth_Err_JsonDecodeErr, 'qrcodebase64 or qrcodelength is empty');
                return;
            }
            this.callback?.onGotQRCode(w);
            this.startLongPolling(u);
        }
        catch (q) {
            this.callback?.onAuthError(OAuthErrCode.WechatAuth_Err_NetworkErr, q.message);
        }
    }
    async startLongPolling(a) {
        if (a.length === 0) {
            this.callback?.onAuthError(OAuthErrCode.WechatAuth_Err_NormalErr, 'uuid is empty');
            return;
        }
        while (!this.isCanceled) {
            let b = `https://long.open.weixin.qq.com/connect/l/qrconnect?f=json&uuid=${a}`;
            if (this.lastUUIDStatusCode !== 0) {
                b += `&last=${this.lastUUIDStatusCode}`;
            }
            const c = http.createHttp();
            const d = await c.request(b);
            const e = d.result;
            if (e.length === 0) {
                this.callback?.onAuthError(OAuthErrCode.WechatAuth_Err_NetworkErr, 'response data is empty');
                return;
            }
            const f = JSON.parse(e);
            let g = f['wx_errcode'];
            Log.d(kTag, `lastUUIDStatusCode: ${this.lastUUIDStatusCode}`);
            let h = OAuthErrCode.WechatAuth_Err_OK;
            let i = 0;
            switch (g) {
                case UUIDStatusCodeEnum.UUID_CONFIRM:
                    i = f['wx_code'];
                    break;
                case UUIDStatusCodeEnum.UUID_SCANNED:
                    break;
                case UUIDStatusCodeEnum.UUID_KEEP_CONNECT:
                    break;
                case UUIDStatusCodeEnum.UUID_EXPIRED:
                    h = OAuthErrCode.WechatAuth_Err_Timeout;
                    break;
                case UUIDStatusCodeEnum.UUID_CANCELED:
                    h = OAuthErrCode.WechatAuth_Err_Cancel;
                    break;
                case UUIDStatusCodeEnum.UUID_SERVER_ERROR:
                    h = OAuthErrCode.WechatAuth_Err_NormalErr;
                    break;
                default:
                    h = OAuthErrCode.WechatAuth_Err_NormalErr;
                    break;
            }
            if (h === OAuthErrCode.WechatAuth_Err_OK) {
                this.lastUUIDStatusCode = g;
                if (g === UUIDStatusCodeEnum.UUID_SCANNED) {
                    this.callback?.onQRCodeScanned();
                }
                else if (g === UUIDStatusCodeEnum.UUID_CONFIRM) {
                    this.callback?.onAuthFinish(i.toString());
                    break;
                }
            }
            else {
                this.callback?.onAuthError(h, 'polling error');
                break;
            }
        }
    }
    stopOAuth() {
        this.callback = null;
        this.isCanceled = true;
    }
}
