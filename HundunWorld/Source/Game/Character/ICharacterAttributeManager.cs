using System;
using System.Collections.Generic;
using FlaxEngine;
using HundunWorld.Game.ECS.Components;

namespace HundunWorld.Game.Character
{
    /// <summary>
    /// 角色属性管理器接口
    /// 负责管理角色的各种属性值
    /// </summary>
    public interface ICharacterAttributeManager
    {
        /// <summary>
        /// 获取角色的基础属性
        /// </summary>
        CharacterStats GetBaseStats(ulong characterId);
        
        /// <summary>
        /// 获取角色的当前属性（包含所有增益减益效果）
        /// </summary>
        CharacterStats GetCurrentStats(ulong characterId);
        
        /// <summary>
        /// 修改角色属性
        /// </summary>
        void ModifyAttribute(ulong characterId, string attributeName, float value, bool isPercent = false);
        
        /// <summary>
        /// 获取角色当前生命值
        /// </summary>
        float GetCurrentHealth(ulong characterId);
        
        /// <summary>
        /// 设置角色当前生命值
        /// </summary>
        void SetCurrentHealth(ulong characterId, float health);
        
        /// <summary>
        /// 获取角色最大生命值
        /// </summary>
        float GetMaxHealth(ulong characterId);
        
        /// <summary>
        /// 检查角色是否存活
        /// </summary>
        bool IsAlive(ulong characterId);
        
        /// <summary>
        /// 对角色造成伤害
        /// </summary>
        float DealDamage(ulong characterId, float damage, ulong attackerId = 0);
        
        /// <summary>
        /// 对角色进行治疗
        /// </summary>
        float Heal(ulong characterId, float amount, ulong healerId = 0);
        
        /// <summary>
        /// 获取角色位置
        /// </summary>
        Vector3 GetPosition(ulong characterId);
        
        /// <summary>
        /// 设置角色位置
        /// </summary>
        void SetPosition(ulong characterId, Vector3 position);
        
        /// <summary>
        /// 检查两个角色是否在范围内
        /// </summary>
        bool IsInRange(ulong characterId1, ulong characterId2, float range);
        
        /// <summary>
        /// 订阅属性变化事件
        /// </summary>
        void SubscribeAttributeChanged(ulong characterId, Action<string, float, float> callback);
        
        /// <summary>
        /// 取消订阅属性变化事件
        /// </summary>
        void UnsubscribeAttributeChanged(ulong characterId, Action<string, float, float> callback);
    }
}