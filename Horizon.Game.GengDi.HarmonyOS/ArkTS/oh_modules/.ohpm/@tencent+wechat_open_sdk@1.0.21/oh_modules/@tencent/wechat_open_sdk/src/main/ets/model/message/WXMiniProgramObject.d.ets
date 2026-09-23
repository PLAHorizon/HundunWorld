import { IMediaObject } from './SendMessageToWX';
/**
 * 小程序版本
 */
export declare enum WXMiniProgramType {
    RELEASE = 0,
    TEST = 1,
    PREVIEW = 2
}
/**
 * 描述一个小程序对象
 */
export declare class WXMiniProgramObject implements IMediaObject {
    /**
     * html 链接
     * @note: 长度大于0且不超过10KB<
     */
    webpageUrl?: string;
    /**
     * 小程序的原始id
     */
    userName?: string;
    /**
     * 小程序的path
     */
    path?: string;
    /**
     * 是否使用带shareTicket的分享
     */
    withShareTicket?: boolean;
    /**
     * 小程序的类型，默认正式版
     */
    miniprogramType?: WXMiniProgramType;
    /**
     * 是否禁止转发
     */
    disableForward: boolean;
    /**
     * 是否是动态消息
     */
    isUpdatableMessage: boolean;
    /**
     * 是否是私密消息
     */
    isSecretMessage: boolean;
    /**
     * 业务所需的额外信息
     */
    extraInfoMap?: Map<string, string>;
    get type(): number;
    serializeTo(k10: object): void;
    deserializeFrom(i10: object): void;
    checkArgs(): boolean;
    putExtra(g10: string, h10: string): void;
    getExtra(e10: string, f10: string): string;
}
