import { MediaObjectType } from './SendMessageToWX';
export class WXTextObject {
    get type() {
        return MediaObjectType.TYPE_TEXT;
    }
    serializeTo(x10) {
        x10['wx_text_object_text'] = this.text;
    }
    deserializeFrom(w10) {
        this.text = w10['wx_text_object_text'];
    }
    checkArgs() {
        if (this.text === undefined || this.text.length === 0) {
            return false;
        }
        return true;
    }
}
