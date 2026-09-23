import { BaseResp } from '../../../../../Index';
import { BaseReq } from '../../../../../Index';
import { Command } from '../Constants';
export declare class SendMessageToWXReq extends BaseReq {
    /**
     * 表示发送到会话
     */
    static readonly WXSceneSession: number;
    /**
     * 表示发送到朋友圈
     */
    static readonly WXSceneTimeline: number;
    /** 发送消息的多媒体内容
     * @see WXMediaMessage
     */
    message?: WXMediaMessage;
    /** 发送的目标场景。 默认发送到会话。
     * @see WXScene
     */
    scene: number;
    serializeTo(x9: object): void;
    deserializeFrom(w9: object): void;
    checkArgs(): boolean;
    get type(): Command;
}
export declare class SendMessageToWXResp extends BaseResp {
    serializeTo(v9: object): void;
    deserializeFrom(u9: object): void;
    get type(): Command;
}
export declare enum MediaObjectType {
    TYPE_UNKNOWN = 0,
    TYPE_TEXT = 1,
    TYPE_IMAGE = 2,
    TYPE_VIDEO = 4,
    TYPE_URL = 5,
    TYPE_FILE = 6,
    TYPE_OPENSDK_APPBRAND = 36,
    TYPE_VIDEO_FILE = 38,
    TYPE_OPENSDK_APPBRAND_WEISHIVIDEO = 46,
    TYPE_OPENSDK_WEWORK_OBJECT = 49,
    TYPE_MUSIC_VIDEO = 76,
    TYPE_NATIVE_GAME_PAGE = 101
}
export interface IMediaObject {
    serializeTo: (to: object) => void;
    deserializeFrom: (from: object) => void;
    get type(): number;
    checkArgs: () => boolean;
}
export interface IWXMusicVipObject {
    serializeTo: (to: object) => void;
    deserializeFrom: (from: object) => void;
    checkArgs: () => boolean;
}
export declare class WXMediaMessage {
    /** 标题
     * @note 长度不能超过512字节
     */
    title?: string;
    /**
     * 描述
     * @note 限制长度不超过1KB
     */
    description?: string;
    mediaObject?: IMediaObject;
    /** 缩略图数据
     * @note 大小不能超过64K
     */
    thumbData?: Uint8Array;
    mediaTagName?: string;
    messageAction?: string;
    messageExt?: string;
    serializeTo(n9: object): void;
    deserializeFrom(j9: object): void;
    getType(): number;
    checkArgs(): boolean;
}
