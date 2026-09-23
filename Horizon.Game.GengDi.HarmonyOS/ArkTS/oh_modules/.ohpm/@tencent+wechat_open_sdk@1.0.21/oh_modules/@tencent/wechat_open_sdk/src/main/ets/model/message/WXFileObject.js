import { MediaObjectType } from "./SendMessageToWX";
export class WXFileObject {
    get type() {
        return MediaObjectType.TYPE_FILE;
    }
    serializeTo(a10) {
        a10['fileUri'] = this.fileUri;
    }
    deserializeFrom(z9) {
        this.fileUri = z9["fileUri"];
    }
    ;
    checkArgs() {
        if (this.fileUri == undefined) {
            return false;
        }
        return true;
    }
}
