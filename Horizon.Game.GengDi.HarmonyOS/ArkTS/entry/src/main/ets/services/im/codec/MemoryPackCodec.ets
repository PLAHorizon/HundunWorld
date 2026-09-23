/**
 * MemoryPack v1.21 字节级读写器（与 C# MemoryPackSerializer 对拍验证过）。
 *
 * 实证格式规则（见 tools/ImMemoryPackFixtures）：
 * - 对象：[成员数 u8][按 MemoryPackOrder 数值序排列的字段]
 * - union（以基类类型序列化）：[tag u8][对象头]
 * - string：null → i32(-1)；空串 → i32(0)；非空 → i32(~utf8字节长度) + i32(字符数) + UTF8 字节
 * - List/数组：null → i32(-1)；否则 [count i32][元素]
 * - Dictionary：[count i32][k v 成对]
 * - bool=1B；byte=1B；ushort 枚举=2B LE；int 枚举=4B LE；long/ulong=8B LE
 *
 * 本文件不依赖任何 @ohos 模块，可用 Node 直接对拍单测。
 */
export class MemoryPackWriter {
    private buf: Uint8Array;
    private length: number;
    private encoder: textEncoderShim = new textEncoderShim();
    constructor(capacity: number = 256) {
        this.buf = new Uint8Array(capacity);
        this.length = 0;
    }
    private ensure(extra: number): void {
        if (this.length + extra <= this.buf.length) {
            return;
        }
        let next = this.buf.length * 2;
        while (next < this.length + extra) {
            next *= 2;
        }
        const grown = new Uint8Array(next);
        grown.set(this.buf.subarray(0, this.length));
        this.buf = grown;
    }
    writeU8(v: number): void {
        this.ensure(1);
        this.buf[this.length++] = v & 0xFF;
    }
    writeBool(v: boolean): void {
        this.writeU8(v ? 1 : 0);
    }
    writeU16(v: number): void {
        this.ensure(2);
        this.buf[this.length++] = v & 0xFF;
        this.buf[this.length++] = (v >>> 8) & 0xFF;
    }
    writeI32(v: number): void {
        this.ensure(4);
        this.buf[this.length++] = v & 0xFF;
        this.buf[this.length++] = (v >>> 8) & 0xFF;
        this.buf[this.length++] = (v >>> 16) & 0xFF;
        this.buf[this.length++] = (v >>> 24) & 0xFF;
    }
    writeU32(v: number): void {
        this.writeI32(v);
    }
    /** long（时间戳毫秒，安全整数范围内用 number 承载） */
    writeI64(v: number): void {
        this.writeBigInt64(BigInt(Math.trunc(v)));
    }
    /** ulong（用户ID等 64 位值） */
    writeUInt64(v: bigint): void {
        this.writeBigInt64(v);
    }
    writeBigInt64(v: bigint): void {
        this.ensure(8);
        let x = v & 0xffffffffffffffffn;
        for (let i = 0; i < 8; i++) {
            this.buf[this.length++] = Number(x & 0xffn);
            x >>= 8n;
        }
    }
    writeString(v: string | null): void {
        if (v === null) {
            this.writeI32(-1);
            return;
        }
        const bytes = this.encoder.encode(v);
        if (bytes.length === 0) {
            this.writeI32(0);
            return;
        }
        this.writeI32(~bytes.length);
        this.writeI32(v.length);
        this.ensure(bytes.length);
        this.buf.set(bytes, this.length);
        this.length += bytes.length;
    }
    /** 对象头：成员数字节 */
    writeObjectHeader(memberCount: number): void {
        this.writeU8(memberCount);
    }
    /** 集合长度头 */
    writeCollectionHeader(count: number): void {
        this.writeI32(count);
    }
    /** union 标签字节 */
    writeUnionTag(tag: number): void {
        this.writeU8(tag);
    }
    toUint8Array(): Uint8Array {
        return this.buf.slice(0, this.length);
    }
}
export class MemoryPackReader {
    private buf: Uint8Array;
    offset: number;
    private decoder: textDecoderShim = new textDecoderShim();
    constructor(buf: Uint8Array) {
        this.buf = buf;
        this.offset = 0;
    }
    get remaining(): number {
        return this.buf.length - this.offset;
    }
    readU8(): number {
        return this.buf[this.offset++];
    }
    readBool(): boolean {
        return this.readU8() !== 0;
    }
    readU16(): number {
        const lo = this.buf[this.offset++];
        const hi = this.buf[this.offset++];
        return lo | (hi << 8);
    }
    readI32(): number {
        const b0 = this.buf[this.offset++];
        const b1 = this.buf[this.offset++];
        const b2 = this.buf[this.offset++];
        const b3 = this.buf[this.offset++];
        return (b0 | (b1 << 8) | (b2 << 16) | (b3 << 24));
    }
    readU32(): number {
        return this.readI32() >>> 0;
    }
    readI64(): number {
        return Number(this.readBigInt64());
    }
    readUInt64(): bigint {
        return this.readBigInt64() & 0xffffffffffffffffn;
    }
    readBigInt64(): bigint {
        let v = 0n;
        for (let i = 7; i >= 0; i--) {
            v = (v << 8n) | BigInt(this.buf[this.offset + i]);
        }
        this.offset += 8;
        return v;
    }
    readString(): string | null {
        const header = this.readI32();
        if (header === -1) {
            return null;
        }
        if (header === 0) {
            return '';
        }
        const byteLen = ~header;
        this.offset += 4; // 跳过字符数（解码不需要）
        const s = this.decoder.decode(this.buf.subarray(this.offset, this.offset + byteLen));
        this.offset += byteLen;
        return s;
    }
    /** 集合长度头。返回 -1 表示 null。 */
    readCollectionHeader(): number {
        return this.readI32();
    }
    readObjectHeader(): number {
        return this.readU8();
    }
    readUnionTag(): number {
        return this.readU8();
    }
}
/** UTF8 编码：纯 TS 实现（ArkTS 无全局 TextEncoder，且保持 Node 对拍同构）。 */
class textEncoderShim {
    encode(s: string): Uint8Array {
        const out: number[] = [];
        for (let i = 0; i < s.length; i++) {
            let code = s.charCodeAt(i);
            if (code >= 0xD800 && code <= 0xDBFF && i + 1 < s.length) {
                // 代理对 → 码点
                const lo = s.charCodeAt(i + 1);
                if (lo >= 0xDC00 && lo <= 0xDFFF) {
                    code = 0x10000 + ((code - 0xD800) << 10) + (lo - 0xDC00);
                    i++;
                }
            }
            if (code < 0x80) {
                out.push(code);
            }
            else if (code < 0x800) {
                out.push(0xC0 | (code >> 6), 0x80 | (code & 0x3F));
            }
            else if (code < 0x10000) {
                out.push(0xE0 | (code >> 12), 0x80 | ((code >> 6) & 0x3F), 0x80 | (code & 0x3F));
            }
            else {
                out.push(0xF0 | (code >> 18), 0x80 | ((code >> 12) & 0x3F), 0x80 | ((code >> 6) & 0x3F), 0x80 | (code & 0x3F));
            }
        }
        return new Uint8Array(out);
    }
}
/** UTF8 解码：纯 TS 实现。 */
class textDecoderShim {
    decode(b: Uint8Array): string {
        let out = '';
        let i = 0;
        while (i < b.length) {
            const byte1 = b[i++];
            if (byte1 < 0x80) {
                out += String.fromCharCode(byte1);
            }
            else if (byte1 < 0xE0) {
                const byte2 = b[i++] & 0x3F;
                out += String.fromCharCode(((byte1 & 0x1F) << 6) | byte2);
            }
            else if (byte1 < 0xF0) {
                const byte2 = b[i++] & 0x3F;
                const byte3 = b[i++] & 0x3F;
                out += String.fromCharCode(((byte1 & 0x0F) << 12) | (byte2 << 6) | byte3);
            }
            else {
                const byte2 = b[i++] & 0x3F;
                const byte3 = b[i++] & 0x3F;
                const byte4 = b[i++] & 0x3F;
                let code = ((byte1 & 0x07) << 18) | (byte2 << 12) | (byte3 << 6) | byte4;
                code -= 0x10000;
                out += String.fromCharCode(0xD800 + (code >> 10), 0xDC00 + (code & 0x3FF));
            }
        }
        return out;
    }
}
