import { Command } from "./Constants";
import { BaseReq, BaseResp } from "./Base";
export declare class LaunchWxaRedirectingPageReq extends BaseReq {
    invokeTicket?: string;
    mediaUri?: string;
    checkArgs(): boolean;
    serializeTo(h9: object): void;
    deserializeFrom(g9: object): void;
    get type(): Command;
}
export declare class LaunchWxaRedirectingPageResp extends BaseResp {
    serializeTo(f9: object): void;
    deserializeFrom(e9: object): void;
    get type(): Command;
}
