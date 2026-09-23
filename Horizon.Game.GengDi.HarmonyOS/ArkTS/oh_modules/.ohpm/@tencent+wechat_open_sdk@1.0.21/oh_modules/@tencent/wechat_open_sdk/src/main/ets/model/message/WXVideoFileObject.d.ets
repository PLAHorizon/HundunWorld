import { IMediaObject } from "./SendMessageToWX";
/**
 * 目前只支持分享到朋友圈
 */
export declare class WXVideoFileObject implements IMediaObject {
    /**
     * 文件路径uri
     */
    fileUri?: string;
    get type(): number;
    serializeTo(z10: object): void;
    deserializeFrom(y10: object): void;
    checkArgs(): boolean;
}
