using System;
using System.Collections.Generic;
using FlaxEngine;
using HundunWorld.Game;
using HundunWorld.Game.Equipment;

namespace HundunWorld.Game.Character
{
    /// <summary>
    /// 角色装备管理器，负责身体装备、配饰与武器的挂载与卸载。
    /// </summary>
    public class CharacterEquipmentManager : Script
    {
        private AnimatedModel _animatedModel;
        private int _currentBodyEquipmentId;
        private Dictionary<EquipmentSlot, int> _currentWeapons = new Dictionary<EquipmentSlot, int>();
        private Dictionary<EquipmentSlot, int> _currentAccessories = new Dictionary<EquipmentSlot, int>();
        private List<CharacterAttachmentSlot> _attachmentSlots = new List<CharacterAttachmentSlot>();

        /// <summary>
        /// 初始化角色装备，会先清空当前所有装备再重新挂载。
        /// </summary>
        public void Initialize(int bodyEquipmentId, List<int> accessoryIds, List<int> weaponIds)
        {
            // 先清空配饰和武器
            var accessorySlots = new List<EquipmentSlot>(_currentAccessories.Keys);
            foreach (var slot in accessorySlots)
            {
                UnequipAccessory(slot);
            }

            var weaponSlots = new List<EquipmentSlot>(_currentWeapons.Keys);
            foreach (var slot in weaponSlots)
            {
                UnequipWeapon(slot);
            }

            // 装备身体模型。如果是默认身体装备（ID == DefaultBodyId），
            // 原则上保留 CreateLocalPlayerActor 中已经设置好的 SkinnedModel；
            // 但如果当前 SkinnedModel 未加载（兜底角色或加载失败），仍需要主动加载默认身体。
            if (bodyEquipmentId == EquipmentDatabase.DefaultBodyId)
            {
                var animatedModel = FindAnimatedModel();
                if (animatedModel != null && animatedModel.SkinnedModel != null && animatedModel.SkinnedModel.IsLoaded)
                {
                    _currentBodyEquipmentId = bodyEquipmentId;
                    Debug.Log($"[CharacterEquipmentManager] 使用默认身体装备，保留当前已加载 SkinnedModel: ID={bodyEquipmentId}");
                }
                else
                {
                    Debug.LogWarning($"[CharacterEquipmentManager] 默认身体装备但当前 SkinnedModel 未就绪，主动加载默认身体: ID={bodyEquipmentId}");
                    EquipBody(bodyEquipmentId);
                }
            }
            else
            {
                EquipBody(bodyEquipmentId);
            }

            if (accessoryIds != null)
            {
                foreach (var id in accessoryIds)
                {
                    var data = EquipmentDatabase.GetEquipment(id);
                    if (data != null)
                    {
                        EquipAccessory(id, data.Slot);
                    }
                    else
                    {
                        Debug.LogWarning($"[CharacterEquipmentManager] Initialize 中找不到配饰 ID: {id}");
                    }
                }
            }

            if (weaponIds != null)
            {
                foreach (var id in weaponIds)
                {
                    var data = EquipmentDatabase.GetEquipment(id);
                    if (data != null)
                    {
                        EquipWeapon(id, data.Slot);
                    }
                    else
                    {
                        Debug.LogWarning($"[CharacterEquipmentManager] Initialize 中找不到武器 ID: {id}");
                    }
                }
            }
        }

        /// <summary>
        /// 装备身体模型。
        /// </summary>
        public void EquipBody(int equipmentId)
        {
            var data = EquipmentDatabase.GetEquipment(equipmentId);
            if (data == null)
            {
                Debug.LogError($"[CharacterEquipmentManager] EquipBody 找不到装备 ID: {equipmentId}");
                return;
            }

            if (data.Type != EquipmentType.Body)
            {
                Debug.LogError($"[CharacterEquipmentManager] EquipBody 装备类型错误，期望 Body，实际 {data.Type}，ID: {equipmentId}");
                return;
            }

            var animatedModel = FindAnimatedModel();
            if (animatedModel == null)
            {
                Debug.LogError("[CharacterEquipmentManager] EquipBody 找不到角色 AnimatedModel");
                return;
            }

            // 获取要装备的身体模型
            SkinnedModel bodyModel = data.BodyModel;
            if (bodyModel == null && data.Id == EquipmentDatabase.DefaultBodyId)
            {
                bodyModel = EquipmentDatabase.GetDefaultBodyModel();
            }

            // 严格等待模型加载完成
            if (bodyModel != null && !bodyModel.IsLoaded)
            {
                Debug.Log($"[CharacterEquipmentManager] 身体模型尚未加载，等待加载: {bodyModel.Path}");
                if (!bodyModel.WaitForLoaded(30000.0) || !bodyModel.IsLoaded)
                {
                    Debug.LogError($"[CharacterEquipmentManager][PACKAGE] 身体模型加载失败或超时: {bodyModel.Path}");
                    return;
                }
            }

            // 只有成功加载后才赋值
            if (bodyModel != null && bodyModel.IsLoaded)
            {
                // ── 竞态安全切换 ──
                // 先设置 UpdateMode = Never 和 IsActive = false，再清空 AnimationGraph，
                // 使 CanUpdateModel 返回 false，阻止 Job 调用 SetupSkinningData。
                var originalUpdateMode = animatedModel.UpdateMode;
                bool wasActive = animatedModel.IsActive;
                var originalAnimGraph = animatedModel.AnimationGraph;

                animatedModel.UpdateMode = AnimatedModel.AnimationUpdateMode.Never;
                animatedModel.IsActive = false;
                animatedModel.AnimationGraph = null;

                animatedModel.SkinnedModel = bodyModel;

                if (data.OverrideMaterial != null)
                {
                    SetMaterial(animatedModel, data.OverrideMaterial);
                }

                // 恢复 AnimationGraph（保留原始引用）
                animatedModel.AnimationGraph = originalAnimGraph;

                // 手动刷新（SkinnedModel 已加载，安全）
                if (animatedModel.SkinnedModel != null && animatedModel.SkinnedModel.IsLoaded)
                {
                    try
                    {
                        animatedModel.SetupSkinningData();
                        animatedModel.ResetAnimation();
                        animatedModel.UpdateAnimation();
                    }
                    catch (Exception ex)
                    {
                        Debug.LogWarning($"[CharacterEquipmentManager] 刷新身体模型失败: {ex.Message}");
                    }
                }

                // 恢复激活状态和更新模式
                animatedModel.IsActive = wasActive;
                animatedModel.UpdateMode = originalUpdateMode;

                _currentBodyEquipmentId = equipmentId;
                Debug.Log($"[CharacterEquipmentManager] 装备身体: {data.Name} (ID: {equipmentId})");
            }
            else
            {
                Debug.LogError($"[CharacterEquipmentManager][PACKAGE] 无法获取已加载的身体模型，跳过换装: ID={equipmentId}");
            }
        }

        /// <summary>
        /// 装备配饰到指定槽位。
        /// </summary>
        public void EquipAccessory(int equipmentId, EquipmentSlot slot)
        {
            var data = EquipmentDatabase.GetEquipment(equipmentId);
            if (data == null)
            {
                Debug.LogError($"[CharacterEquipmentManager] EquipAccessory 找不到装备 ID: {equipmentId}");
                return;
            }

            if (data.Type != EquipmentType.Accessory)
            {
                Debug.LogError($"[CharacterEquipmentManager] EquipAccessory 装备类型错误，期望 Accessory，实际 {data.Type}，ID: {equipmentId}");
                return;
            }

            UnequipAccessory(slot);
            AttachItem(data, slot);
            _currentAccessories[slot] = equipmentId;
            Debug.Log($"[CharacterEquipmentManager] 装备配饰: {data.Name} (ID: {equipmentId}, Slot: {slot})");
        }

        /// <summary>
        /// 装备武器到指定槽位。
        /// </summary>
        public void EquipWeapon(int equipmentId, EquipmentSlot slot)
        {
            var data = EquipmentDatabase.GetEquipment(equipmentId);
            if (data == null)
            {
                Debug.LogError($"[CharacterEquipmentManager] EquipWeapon 找不到装备 ID: {equipmentId}");
                return;
            }

            if (data.Type != EquipmentType.Weapon)
            {
                Debug.LogError($"[CharacterEquipmentManager] EquipWeapon 装备类型错误，期望 Weapon，实际 {data.Type}，ID: {equipmentId}");
                return;
            }

            UnequipWeapon(slot);
            AttachItem(data, slot);
            _currentWeapons[slot] = equipmentId;
            Debug.Log($"[CharacterEquipmentManager] 装备武器: {data.Name} (ID: {equipmentId}, Slot: {slot})");
        }

        /// <summary>
        /// 卸下身体装备并恢复默认身体模型。
        /// </summary>
        public void UnequipBody()
        {
            var animatedModel = FindAnimatedModel();
            if (animatedModel != null)
            {
                var defaultModel = EquipmentDatabase.GetDefaultBodyModel();
                if (defaultModel != null && defaultModel.IsLoaded)
                {
                    // ── 竞态安全切换 ──
                    var originalUpdateMode = animatedModel.UpdateMode;
                    bool wasActive = animatedModel.IsActive;
                    var originalAnimGraph = animatedModel.AnimationGraph;

                    animatedModel.UpdateMode = AnimatedModel.AnimationUpdateMode.Never;
                    animatedModel.IsActive = false;
                    animatedModel.AnimationGraph = null;

                    animatedModel.SkinnedModel = defaultModel;

                    animatedModel.AnimationGraph = originalAnimGraph;

                    if (animatedModel.SkinnedModel != null && animatedModel.SkinnedModel.IsLoaded)
                    {
                        try
                        {
                            animatedModel.SetupSkinningData();
                            animatedModel.ResetAnimation();
                            animatedModel.UpdateAnimation();
                        }
                        catch (Exception ex)
                        {
                            Debug.LogWarning($"[CharacterEquipmentManager] 恢复默认身体模型失败: {ex.Message}");
                        }
                    }

                    animatedModel.IsActive = wasActive;
                    animatedModel.UpdateMode = originalUpdateMode;
                }
                else
                {
                    Debug.LogError("[CharacterEquipmentManager] 默认身体模型未成功加载，无法恢复默认身体");
                }
            }

            _currentBodyEquipmentId = 0;
            Debug.Log("[CharacterEquipmentManager] 卸下身体装备");
        }

        /// <summary>
        /// 卸下指定槽位的配饰。
        /// </summary>
        public void UnequipAccessory(EquipmentSlot slot)
        {
            for (int i = _attachmentSlots.Count - 1; i >= 0; i--)
            {
                var info = _attachmentSlots[i];
                if (info.Slot == slot && info.Type == EquipmentType.Accessory)
                {
                    if (info.AttachedActor != null)
                    {
                        info.AttachedActor.Parent = null;
                        Destroy(info.AttachedActor);
                    }
                    _attachmentSlots.RemoveAt(i);
                }
            }

            _currentAccessories.Remove(slot);
            Debug.Log($"[CharacterEquipmentManager] 卸下配饰 Slot: {slot}");
        }

        /// <summary>
        /// 卸下指定槽位的武器。
        /// </summary>
        public void UnequipWeapon(EquipmentSlot slot)
        {
            for (int i = _attachmentSlots.Count - 1; i >= 0; i--)
            {
                var info = _attachmentSlots[i];
                if (info.Slot == slot && info.Type == EquipmentType.Weapon)
                {
                    if (info.AttachedActor != null)
                    {
                        info.AttachedActor.Parent = null;
                        Destroy(info.AttachedActor);
                    }
                    _attachmentSlots.RemoveAt(i);
                }
            }

            _currentWeapons.Remove(slot);
            Debug.Log($"[CharacterEquipmentManager] 卸下武器 Slot: {slot}");
        }

        /// <inheritdoc />
        public override void OnUpdate()
        {
            if (_animatedModel == null)
                FindAnimatedModel();

            foreach (var slot in _attachmentSlots)
            {
                slot.SyncToBone(_animatedModel);
            }
        }

        /// <summary>
        /// 在 Actor 上查找或缓存 AnimatedModel。
        /// </summary>
        private AnimatedModel FindAnimatedModel()
        {
            if (_animatedModel != null)
                return _animatedModel;

            _animatedModel = Actor.FindActor<AnimatedModel>();
            return _animatedModel;
        }

        /// <summary>
        /// 挂载装备到角色上，创建子 Actor 并记录附件信息。
        /// </summary>
        private void AttachItem(EquipmentData data, EquipmentSlot slot)
        {
            Actor spawnedActor = null;

            if (data.ItemPrefab != null)
            {
                if (!data.ItemPrefab.IsLoaded)
                {
                    Debug.Log($"[CharacterEquipmentManager] ItemPrefab 尚未加载，等待加载: {data.ItemPrefab.Path}");
                    data.ItemPrefab.WaitForLoaded(30000.0);
                }

                if (data.ItemPrefab.IsLoaded)
                {
                    spawnedActor = PrefabManager.SpawnPrefab(data.ItemPrefab);
                }
                else
                {
                    Debug.LogError($"[CharacterEquipmentManager] ItemPrefab 加载失败，跳过挂载: {data.ItemPrefab.Path}");
                }
            }
            else if (data.StaticMesh != null)
            {
                if (!data.StaticMesh.IsLoaded)
                {
                    Debug.Log($"[CharacterEquipmentManager] 静态网格模型尚未加载，等待加载: {data.StaticMesh.Path}");
                    data.StaticMesh.WaitForLoaded(10000.0);
                }

                var staticModel = new StaticModel
                {
                    Name = $"{data.Name}_{slot}"
                };

                staticModel.Model = data.StaticMesh;

                Level.SpawnActor(staticModel, Actor);
                spawnedActor = staticModel;
            }
            else
            {
                Debug.LogWarning($"[CharacterEquipmentManager] 装备 {data.Name} (ID: {data.Id}) 没有 ItemPrefab 也没有 StaticMesh，创建占位");
            }

            if (spawnedActor != null)
            {
                spawnedActor.Parent = Actor;
                spawnedActor.LocalPosition = data.AttachmentOffset;
                spawnedActor.LocalOrientation = data.AttachmentRotation;
                spawnedActor.LocalScale = data.AttachmentScale;
            }

            var attachmentSlot = new CharacterAttachmentSlot(slot, spawnedActor, data.AttachmentBoneName, data.AttachmentOffset, data.AttachmentRotation, data.AttachmentScale)
            {
                Type = data.Type
            };
            _attachmentSlots.Add(attachmentSlot);

            if (_animatedModel != null)
                attachmentSlot.SyncToBone(_animatedModel);
        }

        /// <summary>
        /// 设置 AnimatedModel 的材质。
        /// </summary>
        private void SetMaterial(AnimatedModel model, MaterialBase material)
        {
            if (model != null && material != null)
            {
                model.SetMaterial(0, material);
            }
        }

    }
}
