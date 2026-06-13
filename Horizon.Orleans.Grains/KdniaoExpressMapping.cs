using System.Collections.Generic;

namespace Horizon.Orleans.Grains
{
    public static class KdniaoExpressMapping
    {
        private static readonly Dictionary<string, string> _nameToCode = new()
        {
            { "顺丰速运", "SF" },
            { "中通快递", "ZTO" },
            { "圆通速递", "YTO" },
            { "韵达快递", "YD" },
            { "申通快递", "STO" },
            { "极兔速递", "JTSD" },
            { "邮政EMS", "EMS" },
            { "京东物流", "JD" },
            { "德邦快递", "DBL" },
            { "百世快递", "HTKY" }
        };

        public static string GetCode(string expressCompanyName)
        {
            if (string.IsNullOrEmpty(expressCompanyName)) return "";
            return _nameToCode.TryGetValue(expressCompanyName, out var code) ? code : "";
        }
    }
}
