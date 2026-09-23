import { MediaObjectType } from "./SendMessageToWX";
export class WXVideoObject {
    get type() {
        return MediaObjectType.TYPE_VIDEO;
    }
    serializeTo(b11) {
        b11['videoUrl'] = this.videoUrl;
        b11['videoLowBandUrl'] = this.videoLowBandUrl;
    }
    deserializeFrom(a11) {
        this.videoUrl = a11["videoUrl"];
        this.videoLowBandUrl = a11["videoLowBandUrl"];
    }
    checkArgs() {
        if ((this.videoUrl == undefined || this.videoUrl.length == 0)
            && (this.videoLowBandUrl == undefined || this.videoLowBandUrl.length == 0)) {
            return false;
        }
        return true;
    }
}
