import { IMediaObject } from "./SendMessageToWX";
export declare class WXFileObject implements IMediaObject {
    /**
     * 文件路径uri
     */
    fileUri?: string;
    get type(): number;
    serializeTo(a10: object): void;
    deserializeFrom(z9: object): void;
    checkArgs(): boolean;
}
