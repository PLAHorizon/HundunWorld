import { MediaObjectType } from './SendMessageToWX';
import { Log } from '../../log/Log';
const TAG = "wxopensdk::WXMiniProgramObject";
export var WXMiniProgramType;
(function (o10) {
    o10[o10["RELEASE"] = 0] = "RELEASE";
    o10[o10["TEST"] = 1] = "TEST";
    o10[o10["PREVIEW"] = 2] = "PREVIEW";
})(WXMiniProgramType || (WXMiniProgramType = {}));
export class WXMiniProgramObject {
    constructor() {
        this.disableForward = false;
        this.isUpdatableMessage = false;
        this.isSecretMessage = false;
    }
    get type() {
        return MediaObjectType.TYPE_OPENSDK_APPBRAND;
    }
    serializeTo(k10) {
        k10["wx_miniprogram_wepageurl"] = this.webpageUrl;
        k10["wx_miniprogram_username"] = this.userName;
        k10["wx_miniprogram_path"] = this.path;
        k10["wx_miniprogram_withshareticket"] = this.withShareTicket;
        k10["wx_miniprogram_type"] = this.miniprogramType;
        k10["wx_miniprogram_disableforward"] = this.disableForward;
        k10["wx_miniprogram_isupdatablemsg"] = this.isUpdatableMessage;
        k10["wx_miniprogram_issecretmsg"] = this.isSecretMessage;
        if (this.extraInfoMap != undefined) {
            const l10 = {};
            this.extraInfoMap.forEach((m10, n10) => {
                l10[n10] = m10;
            });
            k10["wx_miniprogram_extrainfo"] = l10;
        }
    }
    deserializeFrom(i10) {
        this.webpageUrl = i10["wx_miniprogram_wepageurl"];
        this.userName = i10["wx_miniprogram_username"];
        this.path = i10["wx_miniprogram_path"];
        this.withShareTicket = i10["wx_miniprogram_withshareticket"];
        this.miniprogramType = i10["wx_miniprogram_type"];
        this.disableForward = i10["wx_miniprogram_disableforward"];
        this.isUpdatableMessage = i10["wx_miniprogram_isupdatablemsg"];
        this.isSecretMessage = i10["wx_miniprogram_issecretmsg"];
        if (i10["wx_miniprogram_extrainfo"] != undefined) {
            const j10 = i10["wx_miniprogram_extrainfo"];
            this.extraInfoMap = new Map(Object.entries(j10));
        }
    }
    checkArgs() {
        if (this.userName == undefined || this.userName.length == 0) {
            Log.i(TAG, `checkArgs, userName is null`);
            return false;
        }
        if (this.miniprogramType == undefined || this.miniprogramType < WXMiniProgramType.RELEASE || this.miniprogramType > WXMiniProgramType.PREVIEW) {
            Log.i(TAG, `checkArgs, miniprogramType is invalid`);
            return false;
        }
        return true;
    }
    putExtra(g10, h10) {
        if (this.extraInfoMap == undefined) {
            this.extraInfoMap = new Map();
        }
        this.extraInfoMap.set(g10, h10);
    }
    getExtra(e10, f10) {
        return this.extraInfoMap?.get(e10) ?? f10;
    }
}
