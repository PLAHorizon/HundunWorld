using Horizon.Core.Security;

namespace Horizon.Orleans.Grains
{
    /// <summary>
    /// 敏感词过滤器
    /// </summary>
    public class SensitiveWordFilter : Horizon.Core.Security.SensitiveWordFilter
    {
        public SensitiveWordFilter()
        {
        }

        public SensitiveWordFilter(IEnumerable<string> words)
            : base(words)
        {
        }
    }
}
