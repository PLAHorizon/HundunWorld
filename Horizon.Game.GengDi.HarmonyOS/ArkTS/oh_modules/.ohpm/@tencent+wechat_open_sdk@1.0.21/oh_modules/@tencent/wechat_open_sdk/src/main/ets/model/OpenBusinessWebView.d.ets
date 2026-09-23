import { Command } from "./Constants";
import { BaseReq, BaseResp } from "./Base";
export declare class OpenBusinessWebViewReq extends BaseReq {
    queryInfo?: Record<string, string>;
    businessType: number;
    checkArgs(): boolean;
    serializeTo(e12: object): void;
    deserializeFrom(d12: object): void;
    get type(): Command;
}
export declare class OpenBusinessWebViewResp extends BaseResp {
    resultInfo?: string;
    businessType: number;
    serializeTo(c12: object): void;
    deserializeFrom(b12: object): void;
    get type(): Command;
}
