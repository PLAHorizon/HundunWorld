import { IMediaObject } from "./SendMessageToWX";
export declare class WXVideoObject implements IMediaObject {
    /**
     * 视频链接
     * @note 长度大于0且不超过10KB
     */
    videoUrl?: string;
    /**
     * 供低带宽的环境下使用的视频链接
     * @note 长度大于0且不超过10KB
     */
    videoLowBandUrl?: string;
    get type(): number;
    serializeTo(b11: object): void;
    deserializeFrom(a11: object): void;
    checkArgs(): boolean;
}
