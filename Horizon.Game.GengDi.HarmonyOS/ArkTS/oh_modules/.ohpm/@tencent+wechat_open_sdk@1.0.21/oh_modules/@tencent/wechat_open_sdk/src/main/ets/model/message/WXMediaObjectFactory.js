import { MediaObjectType } from './SendMessageToWX';
import { WXFileObject } from './WXFileObject';
import { WXImageObject } from './WXImageObject';
import { WXMiniProgramObject } from './WXMiniProgramObject';
import { WXTextObject } from './WXTextObject';
import { WXWebpageObject } from './WXWebpageObject';
import { WXVideoFileObject } from './WXVideoFileObject';
import { WXVideoObject } from './WXVideoObject';
import { WXWeWorkObject } from './WXWeWorkObject';
import { WXMusicVideoObject } from './WXMusicVideoObject';
export class WXMediaObjectFactory {
    static createMediaObject(d10) {
        if (d10 === MediaObjectType.TYPE_TEXT) {
            return new WXTextObject;
        }
        else if (d10 === MediaObjectType.TYPE_IMAGE) {
            return new WXImageObject;
        }
        else if (d10 === MediaObjectType.TYPE_URL) {
            return new WXWebpageObject();
        }
        else if (d10 === MediaObjectType.TYPE_OPENSDK_APPBRAND) {
            return new WXMiniProgramObject();
        }
        else if (d10 === MediaObjectType.TYPE_FILE) {
            return new WXFileObject();
        }
        else if (d10 === MediaObjectType.TYPE_VIDEO_FILE) {
            return new WXVideoFileObject();
        }
        else if (d10 === MediaObjectType.TYPE_VIDEO) {
            return new WXVideoObject();
        }
        else if (d10 === MediaObjectType.TYPE_OPENSDK_WEWORK_OBJECT) {
            return new WXWeWorkObject();
        }
        else if (d10 === MediaObjectType.TYPE_MUSIC_VIDEO) {
            return new WXMusicVideoObject();
        }
        return undefined;
    }
}
