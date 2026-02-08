namespace Horizon.Orleans.Grains
{
    /// <summary>
    /// 敏感词过滤器
    /// </summary>
    public class SensitiveWordFilter
    {
        private readonly HashSet<string> _sensitiveWords = new(StringComparer.OrdinalIgnoreCase);

        public SensitiveWordFilter()
        {
            // Default sensitive words
            var defaults = new[]
            {
                "spam", "abuse", "cheat", "hack", "exploit",
                "scam", "phishing", "botting", "trash", "idiot"
            };
            foreach (var word in defaults)
                _sensitiveWords.Add(word);
        }

        public SensitiveWordFilter(IEnumerable<string> words)
        {
            foreach (var word in words)
                _sensitiveWords.Add(word);
        }

        public bool ContainsSensitiveWord(string text)
        {
            if (string.IsNullOrEmpty(text))
                return false;
            foreach (var word in _sensitiveWords)
            {
                if (text.Contains(word, StringComparison.OrdinalIgnoreCase))
                    return true;
            }
            return false;
        }

        public string FilterText(string text, char replacement = '*')
        {
            if (string.IsNullOrEmpty(text))
                return text;
            var sb = new System.Text.StringBuilder(text);
            foreach (var word in _sensitiveWords)
            {
                int index;
                int searchFrom = 0;
                while (searchFrom < sb.Length && (index = sb.ToString().IndexOf(word, searchFrom, StringComparison.OrdinalIgnoreCase)) >= 0)
                {
                    for (int i = 0; i < word.Length; i++)
                        sb[index + i] = replacement;
                    searchFrom = index + word.Length;
                }
            }
            return sb.ToString();
        }

        public List<string> GetMatchedWords(string text)
        {
            var matched = new List<string>();
            if (string.IsNullOrEmpty(text))
                return matched;
            foreach (var word in _sensitiveWords)
            {
                if (text.Contains(word, StringComparison.OrdinalIgnoreCase))
                    matched.Add(word);
            }
            return matched;
        }

        public void AddWord(string word)
        {
            if (!string.IsNullOrEmpty(word))
                _sensitiveWords.Add(word);
        }

        public void RemoveWord(string word)
        {
            _sensitiveWords.Remove(word);
        }

        public int WordCount => _sensitiveWords.Count;
    }
}
