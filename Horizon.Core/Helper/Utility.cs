using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Horizon.Core.Helper
{
    /// <summary>
    /// Utility 类，提供字符串转换和日期格式化的工具方法。
    /// </summary>
    public static class Utility
    {
        /// <summary>
        /// 将字符串转换为 int 类型。
        /// </summary>
        /// <param name="str">输入字符串</param>
        /// <returns>转换后的 int 值</returns>
        public static int ToInt(this string str)
        {
            int.TryParse(str, out int result);
            return result;
        }

        /// <summary>
        /// 将字符串转换为 long 类型。
        /// </summary>
        /// <param name="str">输入字符串</param>
        /// <returns>转换后的 long 值</returns>
        public static long ToLong(this string str)
        {
            long.TryParse(str, out long result);
            return result;
        }

        /// <summary>
        /// 获取中文格式的当前日期。
        /// </summary>
        /// <param name="time">输入的 DateTime 对象</param>
        /// <returns>格式化后的日期字符串</returns>
        public static string GetChineseDate(this DateTime time)
        {
            return time.ToString("yyyy年MM月dd日");
        }

        /// <summary>
        /// 获取标准格式的当前日期。
        /// </summary>
        /// <param name="time">输入的 DateTime 对象</param>
        /// <returns>格式化后的日期字符串</returns>
        public static string GetDate(this DateTime time)
        {
            return time.ToString("yyyy-MM-dd");
        }

        /// <summary>
        /// 获取标准格式的当前日期和时间。
        /// </summary>
        /// <param name="time">输入的 DateTime 对象</param>
        /// <returns>格式化后的日期时间字符串</returns>
        public static string GetDateTime(this DateTime time)
        {
            return time.ToString("yyyy-MM-dd HH:mm:ss");
        }

        /// <summary>
        /// 获取带毫秒的当前日期和时间。
        /// </summary>
        /// <param name="time">输入的 DateTime 对象</param>
        /// <returns>格式化后的日期时间字符串（带毫秒）</returns>
        public static string GetDateTimeMS(this DateTime time)
        {
            return time.ToString("yyyy-MM-dd HH:mm:ss:fffffff");
        }

        /// <summary>
        /// 获取 UTC 格式的当前日期和时间。
        /// </summary>
        /// <param name="time">输入的 DateTime 对象</param>
        /// <returns>格式化后的 UTC 日期时间字符串</returns>
        public static string GetDateTimeU(this DateTime time)
        {
            return string.Format("{0:U}", time);
        }

        /// <summary>
        /// 获取当前时间（不含日期部分）。
        /// </summary>
        /// <returns>格式化后的时间字符串</returns>
        public static string GetTime()
        {
            return DateTime.Now.ToString("HH:mm:ss");
        }

        /// <summary>
        /// 过滤敏感词汇，将它们替换为 '*'。
        /// 此实现使用 Trie (字典树) 数据结构来高效地查找和替换敏感词。
        /// </summary>
        /// <param name="source">要过滤的源字符串。</param>
        /// <param name="sensitiveWords">敏感词汇的集合。此集合应根据中国现行法律法规及相关政策进行定义。</param>
        /// <param name="replacement">用于替换的字符，默认为 '*'。</param>
        /// <returns>过滤后的字符串。</returns>
        public static string FilterSensitiveWords(this string source, IEnumerable<string> sensitiveWords, char replacement = '*')
        {
            if (string.IsNullOrEmpty(source) || sensitiveWords == null || !sensitiveWords.Any())
            {
                return source;
            }

            var trie = new TrieNode();
            foreach (var word in sensitiveWords.Where(w => !string.IsNullOrEmpty(w)))
            {
                trie.AddWord(word);
            }

            var result = new StringBuilder(source.Length);
            for (int i = 0; i < source.Length; i++)
            {
                int matchLength = trie.FindLongestMatch(source, i);
                if (matchLength > 0)
                {
                    result.Append(new string(replacement, matchLength));
                    i += matchLength - 1;
                }
                else
                {
                    result.Append(source[i]);
                }
            }

            return result.ToString();
        }

        /// <summary>
        /// 表示 Trie (字典树) 中的一个节点。
        /// </summary>
        private class TrieNode
        {
            private readonly Dictionary<char, TrieNode> _children = new Dictionary<char, TrieNode>();
            private bool _isEndOfWord = false;

            /// <summary>
            /// 向 Trie 中添加一个词。
            /// </summary>
            /// <param name="word">要添加的词。</param>
            public void AddWord(string word)
            {
                var currentNode = this;
                foreach (char c in word)
                {
                    if (!currentNode._children.TryGetValue(c, out var nextNode))
                    {
                        nextNode = new TrieNode();
                        currentNode._children[c] = nextNode;
                    }
                    currentNode = nextNode;
                }
                currentNode._isEndOfWord = true;
            }

            /// <summary>
            /// 在输入字符串的指定起始位置查找最长的匹配词长度。
            /// </summary>
            /// <param name="text">要搜索的文本。</param>
            /// <param name="startIndex">开始搜索的索引。</param>
            /// <returns>匹配到的最长词的长度，如果没有匹配则返回 0。</returns>
            public int FindLongestMatch(string text, int startIndex)
            {
                var currentNode = this;
                int longestMatchLength = 0;
                for (int i = startIndex; i < text.Length; i++)
                {
                    char c = text[i];
                    if (!currentNode._children.TryGetValue(c, out var nextNode))
                    {
                        break;
                    }
                    currentNode = nextNode;
                    if (currentNode._isEndOfWord)
                    {
                        longestMatchLength = i - startIndex + 1;
                    }
                }
                return longestMatchLength;
            }
        }
    }
}