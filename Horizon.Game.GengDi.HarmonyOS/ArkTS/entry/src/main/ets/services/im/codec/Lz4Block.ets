/**
 * LZ4 块格式解压（解码器）。
 *
 * 服务端 payload > 256B 时使用 [int32LE 原始长度][LZ4 块数据] 压缩
 * （K4os.LZ4Codec.Encode 即标准 LZ4 block format）。
 * 客户端发送端一律不压缩，故只需解压。
 * 本文件不依赖 @ohos 模块。
 */
export class Lz4Block {
    /** 解压 LZ4 块。compressed 不含长度前缀，decompressedLen 为原始长度。 */
    static decompress(compressed: Uint8Array, decompressedLen: number): Uint8Array | null {
        const out = new Uint8Array(decompressedLen);
        let src = 0;
        let dst = 0;
        while (src < compressed.length) {
            // 跳到非法位置的防护
            if (dst > decompressedLen) {
                return null;
            }
            const token = compressed[src++];
            // 字面量长度
            let literalLen = token >> 4;
            if (literalLen === 15) {
                let extra: number;
                do {
                    if (src >= compressed.length) {
                        return null;
                    }
                    extra = compressed[src++];
                    literalLen += extra;
                } while (extra === 255);
            }
            if (src + literalLen > compressed.length || dst + literalLen > decompressedLen) {
                return null;
            }
            for (let i = 0; i < literalLen; i++) {
                out[dst++] = compressed[src++];
            }
            if (src >= compressed.length) {
                break; // 最后一个序列仅字面量
            }
            // 匹配偏移与长度
            const offset = compressed[src] | (compressed[src + 1] << 8);
            src += 2;
            let matchLen = token & 0x0F;
            if (matchLen === 15) {
                let extra: number;
                do {
                    if (src >= compressed.length) {
                        return null;
                    }
                    extra = compressed[src++];
                    matchLen += extra;
                } while (extra === 255);
            }
            matchLen += 4; // 最小匹配长度 4
            if (offset <= 0 || dst + matchLen > decompressedLen) {
                return null;
            }
            let matchPos = dst - offset;
            for (let i = 0; i < matchLen; i++) {
                out[dst++] = out[matchPos++];
            }
        }
        return out;
    }
}
