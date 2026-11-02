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
        private int _currentAppearanceId;
        private int _gender;
        private bool _genderResolved;
        private Dictionary<EquipmentSlot, int> _currentWeapons = new Dictionary<EquipmentSlot, int>();
        private Dictionary<EquipmentSlot, int> _currentAccessories = new Dictionary<EquipmentSlot, int>();
        private List<CharacterAttachmentSlot> _attachmentSlots = new List<CharacterAttachmentSlot>();

        /// <summary>整体外形（外观装备）变化后触发，供 3D 预览等 UI 重新读取角色模型。</summary>
        public static event Action AppearanceChanged;

        /// <summary>当前穿戴的外观装备 ID（0 = 未穿外观）。</summary>
        public int CurrentAppearanceId => _currentAppearanceId;

        /// <summary>当前角色性别（0 = 男，1 = 女），由进场时的身体模型路径推断。</summary>
        public int CurrentGender => _gender;

        /// <summary>
        /// 初始化角色装备，会先清空当前所有装备再重新挂载。
        /// </summary>
        public void Initialize(int bodyEquipmentId, List<int> accessoryIds, List<int> weaponIds,
            int appearanceEquipmentId = 0)
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

            var animatedModel = FindAnimatedModel();
            // 只在第一具真实身体模型上判定性别：后续 Initialize 时模型可能已经被外观换成
            // 中性傀儡资产，再按路径推断会把女性角色误判成男性（卸下外观后换回男体）。
            if (!_genderResolved && animatedModel != null && animatedModel.SkinnedModel != null)
            {
                _gender = DetectGender(animatedModel);
                _genderResolved = true;
            }

            // 服务端清单里没有身体槽 = 没穿战斗身体装备，视觉上等价于默认身体；
            // 不能把 0 直接送进 EquipBody（那会报"找不到装备 ID 0"并让整次同步停在旧外观）。
            if (bodyEquipmentId <= 0)
                bodyEquipmentId = EquipmentDatabase.DefaultBodyId;

            // 预加载全部外观模型：换装发生在 UI 点击回调里，不应该在那里阻塞等资产。
            foreach (var appearance in EquipmentDatabase.GetAppearanceEquipments())
            {
                if (!string.IsNullOrEmpty(appearance.BodyModelPath))
                    EquipmentDatabase.RequestBodyModelByPath(appearance.BodyModelPath);
            }

            // 装备身体模型。如果是默认身体装备（ID == DefaultBodyId），
            // 原则上保留 CreateLocalPlayerActor 中已经设置好的 SkinnedModel；
            // 但如果当前 SkinnedModel 未加载（兜底角色或加载失败），仍需要主动加载默认身体。
            if (bodyEquipmentId == EquipmentDatabase.DefaultBodyId)
            {
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

            // 外观最后应用：它以"覆盖层"的身份决定最终整套外形。
            if (appearanceEquipmentId > 0)
            {
                EquipAppearance(appearanceEquipmentId);
            }
            else if (_currentAppearanceId != 0)
            {
                // 服务端确认"没有外观"而本地还挂着上一件时，必须真的把模型换回来；
                // 只清 ID 会让角色继续显示旧外观（例如跨设备卸下后重登）。
                _currentAppearanceId = 0;
                ApplyVisualBody();
            }
        }

        /// <summary>从当前身体模型路径推断性别（Character_2 = 女，其余按男处理）。</summary>
        private static int DetectGender(AnimatedModel animatedModel)
        {
            var path = animatedModel?.SkinnedModel?.Path;
            return !string.IsNullOrEmpty(path) &&
                   path.IndexOf("Character_2", StringComparison.OrdinalIgnoreCase) >= 0
                ? 1
                : 0;
        }

        /// <summary>
        /// 装备外观：把角色整套 SkinnedModel 换成外观配置的模型。外观独立于战斗装备槽位，
        /// 不占身体/配饰/武器槽，也不提供战斗属性。
        /// </summary>
        public void EquipAppearance(int equipmentId)
        {
            var data = EquipmentDatabase.GetEquipment(equipmentId);
            if (data == null)
            {
                Debug.LogError($"[CharacterEquipmentManager] EquipAppearance 找不到装备 ID: {equipmentId}");
                return;
            }

            if (data.Type != EquipmentType.Appearance)
            {
                Debug.LogError($"[CharacterEquipmentManager] EquipAppearance 装备类型错误，期望 Appearance，实际 {data.Type}，ID: {equipmentId}");
                return;
            }

            _currentAppearanceId = equipmentId;
            ApplyVisualBody();
            Debug.Log($"[CharacterEquipmentManager] 装备外观: {data.Name} (ID: {equipmentId})");
        }

        /// <summary>卸下外观，恢复当前身体装备（无身体装备则恢复性别默认身体模型）。</summary>
        public void UnequipAppearance()
        {
            if (_currentAppearanceId == 0)
                return;

            int previous = _currentAppearanceId;
            _currentAppearanceId = 0;
            ApplyVisualBody();
            Debug.Log($"[CharacterEquipmentManager] 卸下外观: ID={previous}，恢复默认身体");
        }

        /// <summary>
        /// 按"外观 &gt; 身体装备 &gt; 性别默认身体"的优先级重新计算并应用整套外形。
        /// </summary>
        private void ApplyVisualBody()
        {
            var animatedModel = FindAnimatedModel();
            if (animatedModel == null)
            {
                Debug.LogError("[CharacterEquipmentManager] ApplyVisualBody 找不到角色 AnimatedModel");
                return;
            }

            // MetaHuman 承载身体（Ada）不能被"外观/身体装备"的整模替换换回占位人形：
            // 占位壳（Character_1）自己的 retarget 条目指向已失效骨架，Anim Graph 求值不出姿态（T-pose），
            // 画面上就会变成一个白橙人偶（2026-10-25 实测）。身形/五官仍由模板参数驱动，不需要换模型。
            if (animatedModel.SkinnedModel != null
                && animatedModel.SkinnedModel.Path.IndexOf("Ada_BodyPreview", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                Debug.Log("[CharacterEquipmentManager] 当前是 MetaHuman 身体（Ada），跳过整模替换");
                return;
            }

            SkinnedModel targetModel = null;
            MaterialBase overrideMaterial = null;
            Vector3 bodyScale = Vector3.One;

            var appearance = _currentAppearanceId > 0 ? EquipmentDatabase.GetEquipment(_currentAppearanceId) : null;
            bool hasAppearance = appearance != null;
            if (hasAppearance)
            {
                targetModel = ResolveBodyModel(appearance);
                overrideMaterial = appearance.OverrideMaterial;
                bodyScale = appearance.BodyScale;
            }

            if (targetModel == null)
            {
                var body = _currentBodyEquipmentId > 0
                    ? EquipmentDatabase.GetEquipment(_currentBodyEquipmentId)
                    : null;
                if (body != null)
                {
                    targetModel = ResolveBodyModel(body);
                    // 外观层没有独立网格时会回退到身体装备/默认身体模型，但**外形参数仍归外观所有**；
                    // 这里若无条件写回身体装备的 One/材质，会把外观比例覆盖掉（引擎自检实测 afterScale=1.0）。
                    if (!hasAppearance)
                    {
                        overrideMaterial = body.OverrideMaterial;
                        bodyScale = body.BodyScale;
                    }
                }
            }

            if (targetModel == null)
                targetModel = EquipmentDatabase.GetBodyModel(_gender);

            if (targetModel == null)
            {
                Debug.LogError($"[CharacterEquipmentManager] 无法解析身体模型（外观={_currentAppearanceId}, 身体={_currentBodyEquipmentId}, 性别={_gender}）");
                return;
            }

            if (!targetModel.IsLoaded)
            {
                Debug.Log($"[CharacterEquipmentManager] 身体模型尚未加载，等待加载: {targetModel.Path}");
                if (!targetModel.WaitForLoaded(5000.0) || !targetModel.IsLoaded)
                {
                    Debug.LogError($"[CharacterEquipmentManager][PACKAGE] 身体模型加载失败或超时: {targetModel.Path}");
                    return;
                }
            }

            SwitchBodyModel(animatedModel, targetModel, overrideMaterial, bodyScale);
        }

        /// <summary>解析装备配置的身体模型：优先资产引用，其次延迟加载路径。</summary>
        private static SkinnedModel ResolveBodyModel(EquipmentData data)
        {
            if (data == null) return null;
            if (data.BodyModel != null) return data.BodyModel;
            if (!string.IsNullOrEmpty(data.BodyModelPath))
                return EquipmentDatabase.RequestBodyModelByPath(data.BodyModelPath);
            return null;
        }

        /// <summary>
        /// 竞态安全地切换角色 SkinnedModel（完整保留动画图与激活状态）。
        /// </summary>
        private void SwitchBodyModel(AnimatedModel animatedModel, SkinnedModel bodyModel,
            MaterialBase overrideMaterial, Vector3 bodyScale)
        {
            if (animatedModel == null || bodyModel == null || !bodyModel.IsLoaded)
                return;

            if (ReferenceEquals(animatedModel.SkinnedModel, bodyModel))
            {
                // 模型没变（例如两件外观共用同一具身体资产）：只更新材质覆盖与整体比例。
                if (overrideMaterial != null)
                    SetMaterial(animatedModel, overrideMaterial);
                animatedModel.LocalScale = bodyScale;
                AppearanceChanged?.Invoke();
                return;
            }

            // 先设置 UpdateMode = Never 和 IsActive = false，再清空 AnimationGraph，
            // 使 CanUpdateModel 返回 false，阻止 Job 调用 SetupSkinningData。
            var originalUpdateMode = animatedModel.UpdateMode;
            bool wasActive = animatedModel.IsActive;
            var originalAnimGraph = animatedModel.AnimationGraph;

            animatedModel.UpdateMode = AnimatedModel.AnimationUpdateMode.Never;
            animatedModel.IsActive = false;
            animatedModel.AnimationGraph = null;

            animatedModel.SkinnedModel = bodyModel;
            animatedModel.LocalScale = bodyScale;

            if (overrideMaterial != null)
                SetMaterial(animatedModel, overrideMaterial);

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
                    Debug.LogWarning($"[CharacterEquipmentManager] 刷新身体模型失败: {ex.Message}");
                }
            }

            animatedModel.IsActive = wasActive;
            animatedModel.UpdateMode = originalUpdateMode;

            AppearanceChanged?.Invoke();
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

            _currentBodyEquipmentId = equipmentId;
            ApplyVisualBody();
            Debug.Log($"[CharacterEquipmentManager] 装备身体: {data.Name} (ID: {equipmentId})");
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
            _currentBodyEquipmentId = 0;
            ApplyVisualBody();
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
                // 不是运行期故障：ItemPrefab / StaticMesh 在装备表里从来没被赋值
                // （EquipmentDatabase 里 20001/30001 的 StaticMesh = null），属于待补的美术内容缺口。
                // 报 Warning 会让每次登录都刷一批假故障，降到 Info 并说清"这里什么都不挂"。
                Debug.Log($"[CharacterEquipmentManager] 装备 {data.Name} (ID: {data.Id}) 未配置 ItemPrefab/StaticMesh，跳过外观挂载（需补美术资产）");
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
