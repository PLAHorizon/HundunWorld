import { BaseReq, BaseResp } from './Base';
/**
 * 电子发票授权开票
 */
export declare class InvoiceAuthInsertReq extends BaseReq {
    url: string;
    checkArgs(): boolean;
    get type(): number;
    serializeTo(w8: object): void;
    deserializeFrom(v8: object): void;
}
export declare class InvoiceAuthInsertResp extends BaseResp {
    wxOrderId: string;
    get type(): number;
    serializeTo(u8: object): void;
    deserializeFrom(t8: object): void;
}
