/**
 * IM 网关 TCP 帧编解码 + 粘包处理。
 *
 * 帧格式（与 ImGatewayMessageAdapter.PackPacket 对齐）：
 * - 8 字节定长头：[int32LE payloadLength][u8 messageType][u8 compressedFlag][int16LE checksum]
 * - checksum = payload 全字节累加和取低 16 位
 * - payload：未压缩 = MemoryPack<IMMessagePacket>；
 *   压缩 = [int32LE 原始长度][LZ4 块压缩数据]
 *
 * 本文件不依赖 @ohos 模块。
 */
export class ImFrame {
    messageType: number = 0;
    compressed: boolean = false;
    checksum: number = 0;
    payload: Uint8Array = new Uint8Array(0);
}
export class FrameCodec {
    static readonly HEADER_LENGTH = 8;
    static readonly MAX_PAYLOAD = 1024 * 1024;
    static checksum(data: Uint8Array): number {
        let sum = 0;
        for (let i = 0; i < data.length; i++) {
            sum = (sum + data[i]) & 0xFFFF;
        }
        return sum;
    }
    /** 组帧（发送端不压缩，compressedFlag=0，服务端可直接解析） */
    static pack(payload: Uint8Array, messageType: number): Uint8Array {
        const frame = new Uint8Array(FrameCodec.HEADER_LENGTH + payload.length);
        FrameCodec.writeI32(frame, 0, payload.length);
        frame[4] = messageType & 0xFF;
        frame[5] = 0;
        FrameCodec.writeI16(frame, 6, FrameCodec.checksum(payload));
        frame.set(payload, FrameCodec.HEADER_LENGTH);
        return frame;
    }
    /**
     * 从缓冲区解析完整帧（处理粘包）。
     * 返回解析出的帧数组与剩余未消费字节数；数据不足时返回空数组。
     */
    static unpackAll(buffer: Uint8Array): UnpackResult {
        const frames: Array<ImFrame> = [];
        let offset = 0;
        while (buffer.length - offset >= FrameCodec.HEADER_LENGTH) {
            const payloadLength = FrameCodec.readI32(buffer, offset);
            if (payloadLength <= 0 || payloadLength > FrameCodec.MAX_PAYLOAD) {
                // 帧头损坏，丢弃整个缓冲
                return { frames: frames, consumed: buffer.length };
            }
            if (buffer.length - offset < FrameCodec.HEADER_LENGTH + payloadLength) {
                break;
            }
            const f = new ImFrame();
            f.messageType = buffer[offset + 4];
            f.compressed = buffer[offset + 5] !== 0;
            f.checksum = FrameCodec.readI16(buffer, offset + 6);
            f.payload = buffer.subarray(offset + FrameCodec.HEADER_LENGTH, offset + FrameCodec.HEADER_LENGTH + payloadLength);
            frames.push(f);
            offset += FrameCodec.HEADER_LENGTH + payloadLength;
        }
        return { frames: frames, consumed: offset };
    }
    private static writeI32(buf: Uint8Array, pos: number, v: number): void {
        buf[pos] = v & 0xFF;
        buf[pos + 1] = (v >>> 8) & 0xFF;
        buf[pos + 2] = (v >>> 16) & 0xFF;
        buf[pos + 3] = (v >>> 24) & 0xFF;
    }
    private static readI32(buf: Uint8Array, pos: number): number {
        return buf[pos] | (buf[pos + 1] << 8) | (buf[pos + 2] << 16) | (buf[pos + 3] << 24);
    }
    private static writeI16(buf: Uint8Array, pos: number, v: number): void {
        buf[pos] = v & 0xFF;
        buf[pos + 1] = (v >>> 8) & 0xFF;
    }
    private static readI16(buf: Uint8Array, pos: number): number {
        return buf[pos] | (buf[pos + 1] << 8);
    }
}
export class UnpackResult {
    frames: Array<ImFrame> = [];
    consumed: number = 0;
}
