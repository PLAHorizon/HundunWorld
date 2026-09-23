import { BaseReq, BaseResp } from './Base';
export declare class GetA8KeyReq extends BaseReq {
    url?: string;
    checkArgs(): boolean;
    serializeTo(s8: object): void;
    deserializeFrom(r8: object): void;
    get type(): number;
}
export declare class GetA8KeyResp extends BaseResp {
    redirectUrl?: string;
    serializeTo(q8: object): void;
    deserializeFrom(p8: object): void;
    get type(): number;
}
