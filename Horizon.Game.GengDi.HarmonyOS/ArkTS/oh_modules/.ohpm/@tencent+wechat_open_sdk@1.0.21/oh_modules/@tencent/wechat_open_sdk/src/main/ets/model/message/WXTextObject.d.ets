import { IMediaObject, MediaObjectType } from './SendMessageToWX';
export declare class WXTextObject implements IMediaObject {
    text?: string;
    get type(): MediaObjectType;
    serializeTo(x10: object): void;
    deserializeFrom(w10: object): void;
    checkArgs(): boolean;
}
