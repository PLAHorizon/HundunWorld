import { BaseReq, BaseResp } from './Base';
export declare class SubscribeMessageReq extends BaseReq {
    scene: number;
    templateId: string;
    reserved: string;
    checkArgs(): boolean;
    get type(): number;
    serializeTo(c13: object): void;
    deserializeFrom(b13: object): void;
}
export declare class SubscribeMessageResp extends BaseResp {
    templateId: string;
    scene: number;
    action: string;
    reserved: string;
    get type(): number;
    serializeTo(a13: object): void;
    deserializeFrom(z12: object): void;
}
