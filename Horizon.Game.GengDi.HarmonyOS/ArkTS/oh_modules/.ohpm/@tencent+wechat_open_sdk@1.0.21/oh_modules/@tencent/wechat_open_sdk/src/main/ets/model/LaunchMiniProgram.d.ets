import { BaseReq, BaseResp } from './Base';
import { Command } from './Constants';
export declare class LaunchMiniProgramReq extends BaseReq {
    private kTag;
    /**
     * 小程序的原始id，必填
     */
    userName?: string;
    /**
     * 小程序的启动路径，可不填
     */
    path?: string;
    /**
     * 小程序的类型，0-正式版 1-开发版 2-体验版，
     * 可不填，不填则默认为0
     */
    miniprogramType?: number;
    extraData?: string;
    checkArgs(): boolean;
    serializeTo(d9: object): void;
    deserializeFrom(c9: object): void;
    get type(): Command;
}
export declare class LaunchMiniProgramResp extends BaseResp {
    extMsg?: string;
    serializeTo(b9: object): void;
    deserializeFrom(a9: object): void;
    get type(): Command;
}
