namespace NarrativePro.Items
{
    /// <summary>
    /// 武器持握规则。
    /// </summary>
    public enum EWeaponHandRule
    {
        /// <summary>双手持握，例如步枪或双手剑</summary>
        Both,
        /// <summary>仅主手，例如单手剑</summary>
        Mainhand,
        /// <summary>仅副手，例如盾牌</summary>
        Offhand,
        /// <summary>可双手持握，例如匕首或手枪</summary>
        Either
    }

    /// <summary>
    /// 物品使用动作类型。
    /// </summary>
    public enum EItemUseActionType
    {
        Default,
        Equip,
        Consume,
        Activate,
        Custom
    }
}
