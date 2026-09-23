import { BaseResp, Log } from '../../../../../Index';
import { BaseReq } from '../../../../../Index';
import { Command } from '../Constants';
import { WXMediaObjectFactory } from './WXMediaObjectFactory';
import util from "@ohos.util";
export class SendMessageToWXReq extends BaseReq {
    constructor() {
        super(...arguments);
        this.scene = SendMessageToWXReq.WXSceneSession;
    }
    serializeTo(x9) {
        super.serializeTo(x9);
        if (this.message !== undefined) {
            let y9 = {};
            this.message.serializeTo(y9);
            x9['message'] = y9;
        }
        x9['scene'] = this.scene;
    }
    deserializeFrom(w9) {
        super.deserializeFrom(w9);
        if (typeof w9['message'] !== 'undefined') {
            this.message = new WXMediaMessage;
            this.message.deserializeFrom(w9['message']);
        }
        this.scene = w9['scene'];
    }
    checkArgs() {
        if (this.message === undefined) {
            return false;
        }
        return this.message.checkArgs();
    }
    get type() {
        return Command.kCommandSendMessageToWX;
    }
}
SendMessageToWXReq.WXSceneSession = 0;
SendMessageToWXReq.WXSceneTimeline = 1;
export class SendMessageToWXResp extends BaseResp {
    serializeTo(v9) {
        super.serializeTo(v9);
    }
    deserializeFrom(u9) {
        super.deserializeFrom(u9);
    }
    get type() {
        return Command.kCommandSendMessageToWX;
    }
}
export var MediaObjectType;
(function (t9) {
    t9[t9["TYPE_UNKNOWN"] = 0] = "TYPE_UNKNOWN";
    t9[t9["TYPE_TEXT"] = 1] = "TYPE_TEXT";
    t9[t9["TYPE_IMAGE"] = 2] = "TYPE_IMAGE";
    t9[t9["TYPE_VIDEO"] = 4] = "TYPE_VIDEO";
    t9[t9["TYPE_URL"] = 5] = "TYPE_URL";
    t9[t9["TYPE_FILE"] = 6] = "TYPE_FILE";
    t9[t9["TYPE_OPENSDK_APPBRAND"] = 36] = "TYPE_OPENSDK_APPBRAND";
    t9[t9["TYPE_VIDEO_FILE"] = 38] = "TYPE_VIDEO_FILE";
    t9[t9["TYPE_OPENSDK_APPBRAND_WEISHIVIDEO"] = 46] = "TYPE_OPENSDK_APPBRAND_WEISHIVIDEO";
    t9[t9["TYPE_OPENSDK_WEWORK_OBJECT"] = 49] = "TYPE_OPENSDK_WEWORK_OBJECT";
    t9[t9["TYPE_MUSIC_VIDEO"] = 76] = "TYPE_MUSIC_VIDEO";
    t9[t9["TYPE_NATIVE_GAME_PAGE"] = 101] = "TYPE_NATIVE_GAME_PAGE";
})(MediaObjectType || (MediaObjectType = {}));
function isAppBrandMsg(s9) {
    return s9 == MediaObjectType.TYPE_OPENSDK_APPBRAND
        || s9 == MediaObjectType.TYPE_OPENSDK_APPBRAND_WEISHIVIDEO;
}
function isNativeGameMsg(r9) {
    return r9 == MediaObjectType.TYPE_NATIVE_GAME_PAGE;
}
const TAG = "wxopensdk::WXMediaMessage";
const THUMB_LENGTH_LIMIT = 64 * 1024;
const TITLE_LENGTH_LIMIT = 512;
const DESCRIPTION_LENGTH_LIMIT = 1024;
const MEDIA_TAG_NAME_LENGTH_LIMIT = 64;
const MESSAGE_ACTION_LENGTH_LIMIT = 2 * 1024;
const MESSAGE_EXT_LENGTH_LIMIT = 2 * 1024;
export class WXMediaMessage {
    constructor() {
        this.title = '';
        this.description = "";
    }
    serializeTo(n9) {
        n9['wx_media_message_title'] = this.title;
        n9['wx_media_message_description'] = this.description;
        if (this.mediaObject !== undefined) {
            n9['wx_media_message_media_object_type'] = this.mediaObject.type;
            let q9 = {};
            this.mediaObject.serializeTo(q9);
            n9['wx_media_message_media_object'] = q9;
        }
        if (this.thumbData != undefined) {
            const o9 = new util.Base64Helper();
            const p9 = o9.encodeToStringSync(this.thumbData);
            n9['wx_media_message_thumb_data'] = p9;
        }
        n9['wx_media_message_media_tag_name'] = this.mediaTagName;
        n9['wx_media_message_message_action'] = this.messageAction;
        n9['wx_media_message_message_ext'] = this.messageExt;
    }
    deserializeFrom(j9) {
        this.title = j9['wx_media_message_title'];
        this.description = j9['wx_media_message_description'];
        if (typeof j9['wx_media_message_media_object_type'] === 'number' &&
            typeof j9['wx_media_message_media_object'] === 'object') {
            let m9 = WXMediaObjectFactory.createMediaObject(j9['wx_media_message_media_object_type']);
            if (m9 !== undefined) {
                m9.deserializeFrom(j9['wx_media_message_media_object']);
            }
            this.mediaObject = m9;
        }
        if (j9["wx_media_message_thumb_data"] != undefined) {
            const k9 = j9["wx_media_message_thumb_data"];
            const l9 = new util.Base64Helper();
            this.thumbData = l9.decodeSync(k9);
        }
        this.mediaTagName = j9['wx_media_message_media_tag_name'];
        this.messageAction = j9['wx_media_message_message_action'];
        this.messageExt = j9['wx_media_message_message_ext'];
    }
    getType() {
        if (this.mediaObject === undefined) {
            return MediaObjectType.TYPE_UNKNOWN;
        }
        return this.mediaObject.type;
    }
    checkArgs() {
        if (this.getType() === MediaObjectType.TYPE_MUSIC_VIDEO) {
            if (this.title === undefined || this.title.length <= 0) {
                Log.e(TAG, "checkArgs fail, Type = Music Video, but title == null");
                return false;
            }
        }
        let i9 = isAppBrandMsg(this.getType()) || isNativeGameMsg(this.getType());
        if (!i9 && this.thumbData && this.thumbData.byteLength > THUMB_LENGTH_LIMIT) {
            Log.e(TAG, "checkArgs fail, thumbData is invalid");
            return false;
        }
        if (this.title && this.title.length > TITLE_LENGTH_LIMIT) {
            Log.e(TAG, "checkArgs fail, title is invalid");
            return false;
        }
        if (this.description && this.description.length > DESCRIPTION_LENGTH_LIMIT) {
            Log.e(TAG, "checkArgs fail, description is invalid");
            return false;
        }
        if (this.mediaObject === undefined) {
            Log.e(TAG, "checkArgs fail, mediaObject is null");
            return false;
        }
        if (this.mediaTagName && this.mediaTagName.length > MEDIA_TAG_NAME_LENGTH_LIMIT) {
            Log.e(TAG, "checkArgs fail, mediaTagName is too long");
            return false;
        }
        if (this.messageAction && this.messageAction.length > MESSAGE_ACTION_LENGTH_LIMIT) {
            Log.e(TAG, "checkArgs fail, messageAction is too long");
            return false;
        }
        if (this.messageExt && this.messageExt.length > MESSAGE_EXT_LENGTH_LIMIT) {
            Log.e(TAG, "checkArgs fail, messageExt is too long");
            return false;
        }
        return this.mediaObject.checkArgs();
    }
}
