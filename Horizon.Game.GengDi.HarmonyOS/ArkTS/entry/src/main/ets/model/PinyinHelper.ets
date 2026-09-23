/**
 * 汉字拼音首字母工具（联系人 A-Z 分组用，对标微信通讯录索引）。
 *
 * 实现：基于系统 ICU 拼音排序（Intl.Collator，locale 扩展 -u-co-pinyin），
 * 以每个字母"最小拼音音节"的基准字做区间判定（拼音 collation 下，
 * 任意汉字 c 的首字母 = 满足 compare(c, base[L]) >= 0 的最大 L）。
 * 多音字取 collation 主序，符合通讯录习惯。
 * 运行时自检：基准字对顺序异常（collation 不可用回落 root 排序）时，
 * 全部归入 '#' 组，联系人页自动退化为不分组列表，避免误分组。
 */

const LETTERS: string[] = ['A', 'B', 'C', 'D', 'E', 'F', 'G', 'H', 'J', 'K',
  'L', 'M', 'N', 'O', 'P', 'Q', 'R', 'S', 'T', 'W', 'X', 'Y', 'Z'];
/** 各字母最小音节的基准字（a ba ca da e fa ga ha ji ka la ma na o pa qi ran sa ta wa xi ya za） */
const BASE_CHARS: string[] = ['阿', '八', '擦', '搭', '屙', '发', '旮', '哈',
  '机', '咔', '垃', '妈', '拿', '噢', '啪', '七', '然', '撒', '他', '挖', '西', '丫', '匝'];

export class PinyinHelper {
  private static collator: Intl.Collator | null = null;
  private static checked: boolean = false;
  /** 自检是否通过（不通过时所有名称归 '#' 组） */
  private static available: boolean = false;

  private static ensureReady(): void {
    if (PinyinHelper.checked) {
      return;
    }
    PinyinHelper.checked = true;
    try {
      const coll: Intl.Collator = new Intl.Collator('zh-CN-u-co-pinyin', { sensitivity: 'base' });
      // 自检：拼音首字母顺序必须递增（阿a < 妈ma < 挖wa < 匝za；哦o < 啪pa）
      const pairs: string[][] = [['阿', '妈'], ['妈', '挖'], ['哦', '啪'], ['挖', '匝']];
      let ok = true;
      for (const pair of pairs) {
        if (coll.compare(pair[0], pair[1]) >= 0) {
          ok = false;
          break;
        }
      }
      PinyinHelper.collator = ok ? coll : null;
      PinyinHelper.available = ok;
    } catch (_e) {
      PinyinHelper.collator = null;
      PinyinHelper.available = false;
    }
  }

  /** 取单个字符的拼音首字母（大写）；非汉字/无法判定返回 '' */
  private static charLetter(ch: string): string {
    const code = ch.codePointAt(0) ?? 0;
    // 常用汉字区间（CJK 统一表意文字）
    if (code < 0x4E00 || code > 0x9FFF) {
      return '';
    }
    const coll = PinyinHelper.collator;
    if (coll === null) {
      return '';
    }
    let result = '';
    for (let i = 0; i < BASE_CHARS.length; i++) {
      if (coll.compare(ch, BASE_CHARS[i]) >= 0) {
        result = LETTERS[i];
      } else {
        break;
      }
    }
    return result;
  }

  /**
   * 取名称的分组字母：
   * - 跳过表情/符号/空白，取首个可定位字符
   * - 英文字母直接返回大写
   * - 数字与其他不可定位字符归 '#'
   * - collation 不可用时统一 '#'（联系人页退化为不分组）
   */
  static groupLetter(name: string): string {
    PinyinHelper.ensureReady();
    const trimmed = name.trim();
    if (trimmed.length === 0) {
      return '#';
    }
    if (!PinyinHelper.available) {
      return '#';
    }
    for (let i = 0; i < trimmed.length; i++) {
      const ch = trimmed.charAt(i);
      // 英文字母直接作为分组
      if ((ch >= 'a' && ch <= 'z') || (ch >= 'A' && ch <= 'Z')) {
        return ch.toUpperCase();
      }
      const letter = PinyinHelper.charLetter(ch);
      if (letter.length > 0) {
        return letter;
      }
    }
    return '#';
  }

  /** collation 是否可用（联系人页用于决定是否渲染 A-Z 索引条） */
  static isAvailable(): boolean {
    PinyinHelper.ensureReady();
    return PinyinHelper.available;
  }
}
