import { MediaObjectType } from "./SendMessageToWX";
export class WXVideoFileObject {
    get type() {
        return MediaObjectType.TYPE_VIDEO_FILE;
    }
    serializeTo(z10) {
        z10['fileUri'] = this.fileUri;
    }
    deserializeFrom(y10) {
        this.fileUri = y10["fileUri"];
    }
    ;
    checkArgs() {
        if (this.fileUri == undefined) {
            return false;
        }
        return true;
    }
}
