import { BaseReq, BaseResp } from './Base';
export declare class SendAuthReq extends BaseReq {
    scope?: string;
    state?: string;
    extData?: string;
    isOption1: boolean;
    nonAutomatic: boolean;
    checkArgs(): boolean;
    serializeTo(w12: object): void;
    deserializeFrom(v12: object): void;
    get type(): number;
}
export declare class SendAuthResp extends BaseResp {
    code?: string;
    state?: string;
    authResult: boolean;
    url?: string;
    lang?: string;
    country?: string;
    serializeTo(u12: object): void;
    deserializeFrom(t12: object): void;
    get type(): number;
}
