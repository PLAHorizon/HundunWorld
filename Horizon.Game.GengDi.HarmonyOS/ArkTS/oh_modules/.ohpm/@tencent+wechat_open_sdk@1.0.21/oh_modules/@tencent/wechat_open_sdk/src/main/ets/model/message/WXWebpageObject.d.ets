import { IMediaObject } from './SendMessageToWX';
export declare class WXWebpageObject implements IMediaObject {
    /**
     * html 链接
     * @note 长度大于0且不超过10KB
     */
    webpageUrl?: string;
    /**
     * 拓展信息
     */
    extInfo?: string;
    /**
     * canvas链接
     */
    canvasPageXml?: string;
    /**
     * 是否是私密消息
     */
    isSecretMessage?: boolean;
    /**
     * 业务所需的额外信息
     */
    private extraInfoMap?;
    get type(): number;
    serializeTo(i11: object): void;
    deserializeFrom(g11: object): void;
    checkArgs(): boolean;
    putExtra(e11: string, f11: string): void;
    getExtra(c11: string, d11: string): string;
}
