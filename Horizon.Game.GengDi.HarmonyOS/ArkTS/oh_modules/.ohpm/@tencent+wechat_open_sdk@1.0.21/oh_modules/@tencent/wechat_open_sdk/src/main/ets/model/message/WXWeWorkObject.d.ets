import { IMediaObject } from "./SendMessageToWX";
export declare enum WXWeWorkSubType {
    /** 合并转发 */
    MSG_RECORD = 1,
    /** 逐条转发 */
    MSG_MULTI = 2
}
export declare class WXWeWorkObject implements IMediaObject {
    /**
     * 加密数据
     */
    data?: Uint8Array;
    /**
     * @see WXWeWorkSubType
     */
    subType: WXWeWorkSubType;
    /**
     * 扩展信息，JSON 格式，包含文件 uri 信息
     * 格式：{"file_info": [{"msgId": "xxx", "uri": "xxx"}]}
     */
    extInfo?: string;
    get type(): number;
    serializeTo(p11: object): void;
    deserializeFrom(m11: object): void;
    checkArgs(): boolean;
}
