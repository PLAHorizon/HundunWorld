import { MediaObjectType } from './SendMessageToWX';
import { WXMusicVipInfo } from './WXMusicVipInfo';
import { Log } from '../../log/Log';
import fileUri from "@ohos.file.fileuri";
const TAG = "wxopensdk::WXMusicObject";
const URL_LENGTH_LIMIT = 10 * 1024;
const LYRIC_LENGTH_LIMIT = 32 * 1024;
const STRING_LIMIT = 1024;
const HD_ALBUM_FILE_LENGTH = 1 * 1024 * 1024;
export class WXMusicVideoObject {
    get type() {
        return MediaObjectType.TYPE_MUSIC_VIDEO;
    }
    get hdAlbumThumbFileUri() {
        if (!this.hdAlbumThumbFilePath) {
            return undefined;
        }
        try {
            return fileUri.getUriFromPath(this.hdAlbumThumbFilePath);
        }
        catch (t10) {
            Log.e(TAG, "hdAlbumThumbFilePath to uri failed: " + t10);
            return this.hdAlbumThumbFilePath;
        }
    }
    serializeTo(s10) {
        s10["_wxmusicvideoobject_musicUrl"] = this.musicUrl;
        s10["_wxmusicvideoobject_musicDataUrl"] = this.musicDataUrl;
        s10["_wxmusicvideoobject_singerName"] = this.singerName;
        s10["_wxmusicvideoobject_duration"] = this.duration;
        s10["_wxmusicvideoobject_songLyric"] = this.songLyric;
        s10["_wxmusicvideoobject_hdAlbumThumbFilePath"] = this.hdAlbumThumbFileUri;
        s10["_wxmusicvideoobject_albumName"] = this.albumName;
        s10["_wxmusicvideoobject_musicGenre"] = this.musicGenre;
        s10["_wxmusicvideoobject_issueDate"] = this.issueDate;
        s10["_wxmusicvideoobject_identification"] = this.identification;
        s10["_wxmusicvideoobject_hdAlbumThumbFileHash"] = this.hdAlbumThumbFileHash;
        s10["_wxmusicvideoobject_musicOperationUrl"] = this.musicOperationUrl;
        if (this.musicVipInfo) {
            s10["_wxmusicvideoobject_musicVipInfo"] = "WXMusicVipInfo";
            this.musicVipInfo.serializeTo(s10);
        }
    }
    deserializeFrom(q10) {
        this.musicUrl = q10["_wxmusicvideoobject_musicUrl"];
        this.musicDataUrl = q10["_wxmusicvideoobject_musicDataUrl"];
        this.singerName = q10["_wxmusicvideoobject_singerName"];
        this.duration = q10["_wxmusicvideoobject_duration"];
        this.songLyric = q10["_wxmusicvideoobject_songLyric"];
        this.hdAlbumThumbFilePath = q10["_wxmusicvideoobject_hdAlbumThumbFilePath"];
        this.albumName = q10["_wxmusicvideoobject_albumName"];
        this.musicGenre = q10["_wxmusicvideoobject_musicGenre"];
        this.issueDate = q10["_wxmusicvideoobject_issueDate"];
        this.identification = q10["_wxmusicvideoobject_identification"];
        this.hdAlbumThumbFileHash = q10["_wxmusicvideoobject_hdAlbumThumbFileHash"];
        this.musicOperationUrl = q10["_wxmusicvideoobject_musicOperationUrl"];
        let r10 = q10["_wxmusicvideoobject_musicVipInfo"];
        if (r10) {
            this.musicVipInfo = new WXMusicVipInfo();
            this.musicVipInfo.deserializeFrom(q10);
        }
    }
    static isNullOrNil(p10) {
        return p10 === undefined || p10 === null;
    }
    checkArgs() {
        if (!this.musicUrl || this.musicUrl.length > URL_LENGTH_LIMIT) {
            Log.e(TAG, "musicUrl is null or exceeds the limit");
            return false;
        }
        if (!this.musicDataUrl || this.musicDataUrl.length > URL_LENGTH_LIMIT) {
            Log.e(TAG, "musicDataUrl is null or exceeds the limit");
            return false;
        }
        if (!this.singerName || this.singerName.length > STRING_LIMIT) {
            Log.e(TAG, "singerName is null or exceeds the limit");
            return false;
        }
        if (!this.duration || this.duration < 0) {
            Log.e(TAG, "duration is null or less than 0");
            return false;
        }
        if (this.songLyric && this.songLyric.length > LYRIC_LENGTH_LIMIT) {
            Log.e(TAG, "songLyric is exceeds the limit");
            return false;
        }
        if (this.musicGenre && this.musicGenre.length > STRING_LIMIT) {
            Log.e(TAG, "musicGenre is exceeds the limit");
            return false;
        }
        if (this.identification && this.identification.length > STRING_LIMIT) {
            Log.e(TAG, "identification is exceeds the limit");
            return false;
        }
        if (this.musicOperationUrl && this.musicOperationUrl.length > URL_LENGTH_LIMIT) {
            Log.e(TAG, "musicOperationUrl is exceeds the limit");
            return false;
        }
        if (this.hdAlbumThumbFilePath && this.hdAlbumThumbFilePath.length > STRING_LIMIT) {
            Log.e(TAG, "hdAlbumThumbFilePath is exceeds the limit");
            return false;
        }
        if (this.musicVipInfo && this.musicVipInfo.checkArgs() == false) {
            Log.e(TAG, "musicVipInfo checkArgs failed");
            return false;
        }
        return true;
    }
}
