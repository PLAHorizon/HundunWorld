import { IWXMusicVipObject, MediaObjectType } from './SendMessageToWX';
export declare class WXMusicVipInfo implements IWXMusicVipObject {
    /**
     * 付费音乐id
     * 必填，不能为空，限制长度不超过 10KB
     */
    musicId?: string;
    get type(): MediaObjectType;
    serializeTo(v10: object): void;
    deserializeFrom(u10: object): void;
    checkArgs(): boolean;
}
