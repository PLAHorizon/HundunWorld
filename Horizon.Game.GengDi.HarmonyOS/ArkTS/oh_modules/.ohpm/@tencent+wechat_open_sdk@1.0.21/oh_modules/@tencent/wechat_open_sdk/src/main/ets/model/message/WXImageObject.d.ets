import { IMediaObject, MediaObjectType } from './SendMessageToWX';
export declare class WXImageObject implements IMediaObject {
    /** 图片的uri
     * @note 支持jpeg/png类型的图片
     */
    uri?: string;
    /** 图片的数据内容
     * @note 大小不能超过100KB，uri和imageData同时存在时忽略该字段
     */
    imageData?: string;
    get type(): MediaObjectType;
    serializeTo(c10: object): void;
    deserializeFrom(b10: object): void;
    checkArgs(): boolean;
}
