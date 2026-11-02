using FlaxEngine.GUI;
using HundunWorld.Game.Network;

namespace HundunWorld.Game.UI.Ink.Components
{
    /// <summary>
    /// 页面顶栏玩家信息的统一绑定。
    /// <para>
    /// 各页初值仍是设计稿示例（江湖过客 / Lv.42 / 12,450），只有真实值可用时才覆盖：
    /// 昵称/等级取本地玩家的 <c>CharacterAttributesComponent</c>；
    /// 铜钱取 <see cref="ShopClientService"/> 的服务端快照，且以 <see cref="ShopClientService.Revision"/> 判定
    /// "服务端给过没有"——没开过商店时 <c>Gold</c> 是 0，直接当余额显示会骗人。
    /// </para>
    /// </summary>
    public static class InkPlayerHeaderBinder
    {
        /// <param name="name">昵称标签（可为 null）</param>
        /// <param name="level">等级标签（可为 null）</param>
        /// <param name="coin">铜钱数值标签（可为 null）</param>
        /// <param name="levelPrefix">等级前缀（如 "▲ "），由页面决定</param>
        /// <param name="coinPrefix">铜钱前缀（如 "铜钱 "），由页面决定</param>
        public static void Bind(Label name, Label level, Label coin = null,
            string levelPrefix = "", string coinPrefix = "")
        {
            try
            {
                var local = HundunWorldGame.Instance?.LocalPlayerActor;
                var attrs = local?.GetScript<global::Game.Character.Attributes.CharacterAttributesComponent>();

                if (attrs != null)
                {
                    if (name != null && !string.IsNullOrWhiteSpace(attrs.Nickname))
                        name.Text = attrs.Nickname;

                    if (level != null && attrs.Level > 0)
                        level.Text = levelPrefix + "Lv. " + attrs.Level;
                }

                if (coin != null)
                {
                    var shop = ShopClientService.Instance;
                    if (shop.Revision > 0)
                        coin.Text = coinPrefix + shop.Gold.ToString("N0");
                }
            }
            catch (System.Exception ex)
            {
                FlaxEngine.Debug.LogWarning($"[InkPlayerHeaderBinder] 顶栏绑定失败: {ex.Message}");
            }
        }

        /// <summary>
        /// 门派标签：取当前选中角色的职业（创角不再选门派，所以多半是"无门派"）；
        /// 连角色都没有时显示「—」，不摆设计稿的门派名。
        /// </summary>
        public static void BindSect(Label sect)
        {
            if (sect == null)
                return;

            try
            {
                var role = global::Game.UI.Character.CharacterManager.Instance?.SelectedCharacter;
                if (role == null)
                {
                    sect.Text = "—";
                    return;
                }

                string name = global::HundunWorld.Game.UI.Ink.Pages.CharacterCreation.CcSectNames.Get(role.Profession);
                sect.Text = string.IsNullOrEmpty(name) ? "无门派" : name;
            }
            catch (System.Exception ex)
            {
                FlaxEngine.Debug.LogWarning($"[InkPlayerHeaderBinder] 门派绑定失败: {ex.Message}");
            }
        }

        /// <summary>
        /// 等级标签只要派生自 <see cref="Label"/> 都适用（如设置页的 <c>InkItemTag</c>），
        /// 因此不需要额外重载。
        /// </summary>
        public static void BindLevel(Label level, string levelPrefix = "")
        {
            try
            {
                var local = HundunWorldGame.Instance?.LocalPlayerActor;
                var attrs = local?.GetScript<global::Game.Character.Attributes.CharacterAttributesComponent>();
                if (attrs != null && attrs.Level > 0 && level != null)
                    level.Text = levelPrefix + "Lv. " + attrs.Level;
            }
            catch (System.Exception ex)
            {
                FlaxEngine.Debug.LogWarning($"[InkPlayerHeaderBinder] 等级绑定失败: {ex.Message}");
            }
        }

        /// <summary>
        /// 当前网关的区域名与时延文本；未连接给「未连接」、时延没测过给「未测得」。
        /// 顶栏的「服务器/延迟」类标签统一问这里，别各页自己拿 <c>Latency</c> 当毫秒数显示。
        /// 这里刻意不用「—」：11f Body 字号下破折号实测没有字形，页脚会只剩「服务器：」。
        /// </summary>
        public static void GetGatewayText(out string region, out string latency)
        {
            region = "未连接";
            latency = "未测得";

            try
            {
                var gateway = HundunWorldGame.Instance?.NetworkManager?.GetCurrentGateway();
                if (gateway == null)
                    return;

                // 网关在、只是没下发区域名：说"未连接"会与同一行的实时延迟自相矛盾
                region = string.IsNullOrEmpty(gateway.Region) ? "未知区域" : gateway.Region;

                // Latency 默认 long.MaxValue 表示"没测过"，不能当毫秒数显示
                if (gateway.Latency >= 0 && gateway.Latency < long.MaxValue / 2)
                    latency = gateway.Latency + "ms";
            }
            catch (System.Exception ex)
            {
                FlaxEngine.Debug.LogWarning($"[InkPlayerHeaderBinder] 网关信息读取失败: {ex.Message}");
            }
        }
    }
}
