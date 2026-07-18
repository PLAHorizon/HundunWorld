using System;

namespace NarrativePro.Character
{
    /// <summary>
    /// 玩家定义。对应 UE5 UPlayerDefinition。
    /// 定义一个由玩家控制的角色。继承角色定义，增加玩家显示名。
    /// CharacterCreator 数据会覆盖 PlayerDisplayName（覆盖 ApplyAppearance 可改变此行为）。
    /// </summary>
    [Serializable]
    public class PlayerDefinition : CharacterDefinition
    {
        /// <summary>玩家显示名。CharacterCreator 数据会覆盖此值。</summary>
        public string PlayerDisplayName = "";
    }
}
