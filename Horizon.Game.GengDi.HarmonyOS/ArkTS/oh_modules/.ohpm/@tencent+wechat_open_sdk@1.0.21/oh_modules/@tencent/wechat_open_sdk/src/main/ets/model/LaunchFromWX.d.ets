import { BaseReq } from "./Base";
import { Command } from "./Constants";
import { WXMediaMessage } from "./message/SendMessageToWX";
export declare class LaunchFromWXReq extends BaseReq {
    /** 发送消息的多媒体内容
     * @see WXMediaMessage
     */
    message?: WXMediaMessage;
    lang?: string;
    country?: string;
    serializeTo(y8: object): void;
    deserializeFrom(x8: object): void;
    checkArgs(): boolean;
    get type(): Command;
}
