using System;
using System.Collections.Generic;

namespace HundunWorld.Game.UI.Ink
{
    /// <summary>侧边栏条目：显示名 + 目标页 dom-id（可选右侧角标，如"热"）。</summary>
    public readonly struct InkNavItem
    {
        public readonly string Label;
        public readonly string DomId;

        /// <summary>可选角标文案（空表示无角标）。对应设计稿条目右侧的小标签。</summary>
        public readonly string Badge;

        public InkNavItem(string label, string domId, string badge = null)
        {
            Label = label;
            DomId = domId;
            Badge = badge;
        }
    }

    /// <summary>侧边栏分组：标题 + 条目。</summary>
    public readonly struct InkNavSection
    {
        public readonly string Title;
        public readonly InkNavItem[] Items;

        public InkNavSection(string title, InkNavItem[] items)
        {
            Title = title;
            Items = items;
        }
    }

    /// <summary>
    /// 侧边栏导航清单的唯一来源：**除弹框与事件 UI 之外的功能页**都在这里列一遍。
    /// <para>
    /// 口径（2026-10-07 用户确认）：只收功能页——创角/选角流程、加载与章节过场、死亡/QTE/引导等事件页、
    /// 弹层族（Modal）、UI 画廊与水墨动画参考页（开发用）、灵台/元素视野（游戏内叠加模式）都不进侧边栏；
    /// 同一页的多个别名只保留一个（如 `nav-mail` 与 `nav-social-mail` 同页，只留前者）。
    /// </para>
    /// <para>
    /// 各页面共用本清单渲染侧边栏，闸门规则 P 会核对"功能页是否都在清单里"，避免再出现"某个页面没有入口"。
    /// </para>
    /// </summary>
    public static class InkSidebarNav
    {
        /// <summary>清单分组（顺序即渲染顺序）。</summary>
        public static readonly InkNavSection[] Sections =
        {
            new InkNavSection("世界", new[]
            {
                new InkNavItem("任务", InkPageDomIds.NavQuests),
                new InkNavItem("世界地图", InkPageDomIds.NavWorldMap),
                new InkNavItem("罗盘", InkPageDomIds.NavCompass),
                new InkNavItem("秘境", InkPageDomIds.NavDungeonEntry),
                new InkNavItem("博物志", InkPageDomIds.NavBestiary),
                new InkNavItem("武林录", InkPageDomIds.NavMartialRecord),
                new InkNavItem("营生", InkPageDomIds.NavLivelihood),
                new InkNavItem("时辰", InkPageDomIds.NavTime),
                new InkNavItem("多人", InkPageDomIds.NavMultiplayer),
                new InkNavItem("战前备战", InkPageDomIds.NavBattlePrep),
            }),

            new InkNavSection("成长", new[]
            {
                new InkNavItem("角色", InkPageDomIds.NavCharacterPanel),
                new InkNavItem("装备", InkPageDomIds.NavEquipment),
                // 旧「技能」页是设计稿假数据（8 个写死招式）；技能树才是服务端目录驱动的真实页（2026-10-29）
                new InkNavItem("技能树", InkPageDomIds.NavSkillTree),
                new InkNavItem("装备强化", InkPageDomIds.NavEquipmentEnhance),
                new InkNavItem("锻造", InkPageDomIds.NavCrafting),
                new InkNavItem("坐骑宠物", InkPageDomIds.NavMountPet),
                new InkNavItem("外观", InkPageDomIds.NavAppearance),
            }),

            new InkNavSection("社交", new[]
            {
                new InkNavItem("组队", InkPageDomIds.NavTeam),
                new InkNavItem("好友", InkPageDomIds.NavFriends),
                new InkNavItem("门派", InkPageDomIds.NavSect),
                new InkNavItem("结伴江湖", InkPageDomIds.NavSocialGuild),
                new InkNavItem("师徒", InkPageDomIds.NavMentor),
                new InkNavItem("排行榜", InkPageDomIds.NavLeaderboard),
                new InkNavItem("成就", InkPageDomIds.NavAchievement),
                new InkNavItem("邮箱", InkPageDomIds.NavMail),
            }),

            new InkNavSection("系统", new[]
            {
                new InkNavItem("商店", InkPageDomIds.NavShop),
                new InkNavItem("珍品阁", InkPageDomIds.NavShopRare),
                new InkNavItem("活动", InkPageDomIds.NavActivities),
                new InkNavItem("战令", InkPageDomIds.NavBattlePass, "热"),
                new InkNavItem("祈愿", InkPageDomIds.NavGacha),
                new InkNavItem("休闲模式", InkPageDomIds.NavCasualMode),
                new InkNavItem("个人信息", InkPageDomIds.NavPersonalInfo),
                new InkNavItem("设置", InkPageDomIds.NavSettings),
                new InkNavItem("音频设置", InkPageDomIds.NavSettingsAudio),
            }),
        };

        /// <summary>清单里所有目标 dom-id。</summary>
        public static IEnumerable<string> AllDomIds()
        {
            foreach (var section in Sections)
            {
                foreach (var item in section.Items)
                {
                    yield return item.DomId;
                }
            }
        }

        /// <summary>某页是否在清单里（用于判定激活态；别名页会命中它指向的正名）。</summary>
        public static bool Contains(string domId)
        {
            if (string.IsNullOrEmpty(domId)) return false;
            foreach (var id in AllDomIds())
            {
                if (id == domId) return true;
            }
            return false;
        }
    }
}
