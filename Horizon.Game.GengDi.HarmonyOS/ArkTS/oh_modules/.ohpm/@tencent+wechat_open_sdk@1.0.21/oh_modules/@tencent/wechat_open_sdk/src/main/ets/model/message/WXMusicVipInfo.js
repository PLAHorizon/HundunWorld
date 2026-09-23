import { MediaObjectType } from './SendMessageToWX';
import { Log } from '../../log/Log';
const TAG = "wxopensdk::WXMusicVipObject";
const LENGTH_LIMIT = 10 * 1024;
export class WXMusicVipInfo {
    get type() {
        return MediaObjectType.TYPE_MUSIC_VIDEO;
    }
    serializeTo(v10) {
        v10["wx_music_vip_id"] = this.musicId;
    }
    deserializeFrom(u10) {
        this.musicId = u10["wx_music_vip_id"];
    }
    checkArgs() {
        if (this.musicId === undefined || this.musicId === '') {
            Log.e(TAG, "musicId is required");
            return false;
        }
        if (this.musicId.length > LENGTH_LIMIT) {
            Log.e(TAG, "musicId length is larger than 10240");
            return false;
        }
        return true;
    }
}
