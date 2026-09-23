import { IMediaObject, MediaObjectType } from './SendMessageToWX';
import { WXMusicVipInfo } from './WXMusicVipInfo';
export declare class WXMusicVideoObject implements IMediaObject {
    /**
     * 音频网页的 URL 地址
     * 必填，不能为空，限制长度不超过 10KB
     */
    musicUrl?: string;
    /**
     * 音频数据的 URL 地址
     * 必填，不能为空，限制长度不超过 10KB
     */
    musicDataUrl?: string;
    /**
     * 歌手名
     * 必填，不能为空，限制长度不超过 1KB
     */
    singerName?: string;
    /**
     * 歌曲时长
     * 必填，单位：毫秒
     */
    duration?: number;
    /**
     * 歌词
     * 建议填写，限制长度不超过 32KB
     */
    songLyric?: string;
    /**
     * 高清专辑图本地文件路径
     * 选填，文件限制长度不超过 1MB
     */
    hdAlbumThumbFilePath?: string;
    /**
     * 音乐专辑名
     * 选填
     */
    albumName?: string;
    /**
     * 音乐流派
     * 选填
     */
    musicGenre?: string;
    /**
     * 发行时间
     * Unix 时间戳	选填，单位：秒
     */
    issueDate?: number;
    /**
     * 音乐标识符
     * 建议填写，用户在微信音乐播放器跳回应用时会携带该参数，可用于唯一标识一首歌，微信侧不理解
     */
    identification?: string;
    /**
     * 高清专辑封面图
     * SHA256	用于签名，详情可见 OpenSDK 分享签名开发手册
     */
    hdAlbumThumbFileHash?: string;
    /**
     * 音乐播放器内展示 URL
     */
    musicOperationUrl?: string;
    /**
     * 付费歌曲额外信息
     */
    musicVipInfo?: WXMusicVipInfo;
    get type(): MediaObjectType;
    /**
     * 高清专辑图本地文件路径，作为跨应用传递的 URI 来源。
     * 用户传入的是应用沙箱路径（如 /data/storage/.../files/xxx.png），跨应用传递时必须
     * 转为 file:// 形式的 uri，发送链路才会把它放入 Want.uri 并以
     * FLAG_AUTH_READ_URI_PERMISSION 授权微信只读访问；转换失败时降级返回原路径。
     */
    get hdAlbumThumbFileUri(): string | undefined;
    serializeTo(s10: object): void;
    deserializeFrom(q10: object): void;
    static isNullOrNil(p10: string | null | undefined): boolean;
    checkArgs(): boolean;
}
