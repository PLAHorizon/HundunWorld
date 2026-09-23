import { BaseReq, BaseResp } from "./Base";
import { Command } from "./Constants";
export declare class OpenBusinessViewReq extends BaseReq {
    private kTag;
    businessType?: string;
    query?: string;
    extInfo?: string;
    checkArgs(): boolean;
    serializeTo(a12: object): void;
    deserializeFrom(z11: object): void;
    get type(): Command;
}
export declare class OpenBusinessViewResp extends BaseResp {
    extMsg?: string;
    businessType?: string;
    serializeTo(y11: object): void;
    deserializeFrom(x11: object): void;
    get type(): Command;
}
