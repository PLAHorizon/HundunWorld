import { MediaObjectType } from "./SendMessageToWX";
import util from "@ohos.util";
import { Log } from '../../log/Log';
export var WXWeWorkSubType;
(function (s11) {
    s11[s11["MSG_RECORD"] = 1] = "MSG_RECORD";
    s11[s11["MSG_MULTI"] = 2] = "MSG_MULTI";
})(WXWeWorkSubType || (WXWeWorkSubType = {}));
export class WXWeWorkObject {
    constructor() {
        this.subType = 0;
    }
    get type() {
        return MediaObjectType.TYPE_OPENSDK_WEWORK_OBJECT;
    }
    serializeTo(p11) {
        if (this.data != undefined) {
            try {
                const r11 = new util.Base64Helper();
                p11['wx_wework_data'] = r11.encodeToStringSync(this.data);
            }
            catch (q11) {
                Log.e('WXWeWorkObject', `serializeTo encodeToStringSync failed: ${q11.message}`);
            }
        }
        p11['wx_wework_subtype'] = this.subType;
        p11['wx_wework_extinfo'] = this.extInfo;
    }
    deserializeFrom(m11) {
        if (m11['wx_wework_data'] != undefined) {
            try {
                const o11 = new util.Base64Helper();
                this.data = o11.decodeSync(m11['wx_wework_data']);
            }
            catch (n11) {
                Log.e('WXWeWorkObject', `deserializeFrom decodeSync failed: ${n11.message}`);
            }
        }
        this.subType = m11['wx_wework_subtype'];
        this.extInfo = m11['wx_wework_extinfo'];
    }
    checkArgs() {
        if (this.data === undefined) {
            return false;
        }
        if (this.subType !== WXWeWorkSubType.MSG_RECORD && this.subType !== WXWeWorkSubType.MSG_MULTI) {
            return false;
        }
        return true;
    }
}
