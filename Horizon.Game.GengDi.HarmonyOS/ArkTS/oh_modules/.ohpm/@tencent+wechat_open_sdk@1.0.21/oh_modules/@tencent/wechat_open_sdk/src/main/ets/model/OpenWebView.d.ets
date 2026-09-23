import { BaseReq, BaseResp } from "./Base";
import { Command } from "./Constants";
export declare class OpenWebViewReq extends BaseReq {
    url?: string;
    checkArgs(): boolean;
    serializeTo(k12: object): void;
    deserializeFrom(j12: object): void;
    get type(): Command;
}
export declare class OpenWebViewResp extends BaseResp {
    result?: string;
    serializeTo(i12: object): void;
    deserializeFrom(h12: object): void;
    get type(): Command;
}
