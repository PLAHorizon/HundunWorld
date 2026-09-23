import { MediaObjectType } from './SendMessageToWX';
export class WXImageObject {
    get type() {
        return MediaObjectType.TYPE_IMAGE;
    }
    serializeTo(c10) {
        c10['imageBase64'] = this.imageData;
        c10['uri'] = this.uri;
    }
    deserializeFrom(b10) {
        this.imageData = b10['imageBase64'];
        this.uri = b10['uri'];
    }
    checkArgs() {
        if (this.uri === undefined) {
            return false;
        }
        return true;
    }
}
