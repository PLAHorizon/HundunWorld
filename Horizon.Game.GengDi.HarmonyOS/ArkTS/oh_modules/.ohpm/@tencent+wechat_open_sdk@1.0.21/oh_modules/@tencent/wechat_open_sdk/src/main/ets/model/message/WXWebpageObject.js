import { MediaObjectType } from './SendMessageToWX';
export class WXWebpageObject {
    constructor() {
        this.isSecretMessage = false;
    }
    get type() {
        return MediaObjectType.TYPE_URL;
    }
    serializeTo(i11) {
        i11['wx_webpage_url'] = this.webpageUrl;
        i11['wx_webpage_extinfo'] = this.extInfo;
        i11['wx_webpage_canvaspagexml'] = this.canvasPageXml;
        i11['wx_webpage_issecretmsg'] = this.isSecretMessage;
        if (this.extraInfoMap != undefined) {
            const j11 = {};
            this.extraInfoMap.forEach((k11, l11) => {
                j11[l11] = k11;
            });
            i11['wx_webpage_extrainfo'] = j11;
        }
    }
    deserializeFrom(g11) {
        this.webpageUrl = g11["wx_webpage_url"];
        this.extInfo = g11['wx_webpage_extinfo'];
        this.canvasPageXml = g11['wx_webpage_canvaspagexml'];
        this.isSecretMessage = g11['wx_webpage_issecretmsg'];
        if (g11["wx_webpage_extrainfo"] != undefined) {
            const h11 = g11["wx_webpage_extrainfo"];
            this.extraInfoMap = new Map(Object.entries(h11));
        }
    }
    checkArgs() {
        if (this.webpageUrl == undefined || this.webpageUrl.length == 0 || this.webpageUrl.length > 1024 * 10) {
            return false;
        }
        return true;
    }
    putExtra(e11, f11) {
        if (this.extraInfoMap == undefined) {
            this.extraInfoMap = new Map();
        }
        this.extraInfoMap.set(e11, f11);
    }
    getExtra(c11, d11) {
        return this.extraInfoMap?.get(c11) ?? d11;
    }
}
