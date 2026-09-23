import sys

# Flax 日志存在混合编码：多数行以 UTF-16LE 写入，但部分行由「增强日志桥」以 UTF-8 字节
# 直接写进了 UTF-16 流。结果按 UTF-16LE 读出来时，这些行呈现为「UTF-8 字节被当作 UTF-16 码元」
# 的乱码。还原方法：把该行按 UTF-16LE 编码回字节，再按 UTF-8 解码。
#
# 用法: python dec.py <日志文件>   →  还原后的文本输出到 stdout


def fix(line: str) -> str:
    try:
        return line.encode("utf-16-le").decode("utf-8")
    except Exception:
        return line


def main() -> int:
    path = sys.argv[1]
    raw = open(path, "rb").read()
    text = raw.decode("utf-16-le", errors="ignore")
    out = [fix(line) for line in text.splitlines()]
    sys.stdout.reconfigure(encoding="utf-8")
    print("\n".join(out))
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
