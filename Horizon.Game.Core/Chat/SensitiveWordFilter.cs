using System;
using System.Collections.Generic;
using System.IO;

namespace Horizon.Game.Core.Chat
{
    /// <summary>
    /// 简易敏感词过滤器（子串匹配，忽略大小写）。
    /// <para>
    /// 词库加载优先级：运行目录下 <c>sensitive-words.txt</c>（每行一个词，<c>#</c> 起头为注释）
    /// → 内置最小词表。生产环境应提供词库文件；<see cref="SetWords"/> 可运行时注入（热更新/测试）。
    /// </para>
    /// <para>线程安全：首次访问或 <see cref="Reload"/> 后按需加载。</para>
    /// </summary>
    public static class SensitiveWordFilter
    {
        private static readonly object Gate = new object();
        private static HashSet<string> _words;
        private static bool _loaded;

        /// <summary>内置最小词表（仅占位/兜底；生产用 sensitive-words.txt 覆盖）。</summary>
        private static readonly string[] DefaultWords =
        {
            "傻逼", "加微信", "代练", "私服", "fuck", "shit",
        };

        /// <summary>词库文件（运行目录）。</summary>
        private static string WordFile => Path.Combine(AppContext.BaseDirectory, "sensitive-words.txt");

        /// <summary>运行时注入词库（覆盖文件/内置）。</summary>
        public static void SetWords(IEnumerable<string> words)
        {
            var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (words != null)
            {
                foreach (var w in words)
                {
                    var t = w?.Trim();
                    if (!string.IsNullOrEmpty(t)) set.Add(t);
                }
            }
            lock (Gate)
            {
                _words = set;
                _loaded = true;
            }
        }

        /// <summary>强制下次访问重新加载词库。</summary>
        public static void Reload()
        {
            lock (Gate) _loaded = false;
        }

        private static void Ensure()
        {
            if (_loaded) return;
            lock (Gate)
            {
                if (_loaded) return;

                var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                try
                {
                    if (File.Exists(WordFile))
                    {
                        foreach (var line in File.ReadAllLines(WordFile))
                        {
                            var w = line.Trim();
                            if (w.Length > 0 && !w.StartsWith("#")) set.Add(w);
                        }
                    }
                }
                catch
                {
                    // 词库读取失败则退回内置词表，避免影响聊天主流程
                }

                if (set.Count == 0)
                    foreach (var w in DefaultWords) set.Add(w);

                _words = set;
                _loaded = true;
            }
        }

        /// <summary>是否包含敏感词。</summary>
        public static bool Contains(string text)
        {
            if (string.IsNullOrEmpty(text)) return false;
            Ensure();
            foreach (var w in _words)
            {
                if (w.Length > 0 && text.Contains(w, StringComparison.OrdinalIgnoreCase))
                    return true;
            }
            return false;
        }

        /// <summary>把命中的敏感词替换为等长 <c>*</c>（用于留档的过滤后内容）。</summary>
        public static string Mask(string text)
        {
            if (string.IsNullOrEmpty(text)) return text;
            Ensure();

            var result = text;
            foreach (var w in _words)
            {
                if (w.Length == 0) continue;
                result = ReplaceIgnoreCase(result, w, new string('*', w.Length));
            }
            return result;
        }

        private static string ReplaceIgnoreCase(string source, string word, string replacement)
        {
            int idx = source.IndexOf(word, StringComparison.OrdinalIgnoreCase);
            if (idx < 0) return source;

            var sb = new System.Text.StringBuilder(source.Length);
            int start = 0;
            while (idx >= 0)
            {
                sb.Append(source, start, idx - start);
                sb.Append(replacement);
                start = idx + word.Length;
                idx = source.IndexOf(word, start, StringComparison.OrdinalIgnoreCase);
            }
            sb.Append(source, start, source.Length - start);
            return sb.ToString();
        }
    }
}
