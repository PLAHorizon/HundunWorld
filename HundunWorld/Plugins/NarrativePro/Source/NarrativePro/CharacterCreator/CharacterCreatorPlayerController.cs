using System;
using System.Collections.Generic;
using FlaxEngine;
using NarrativePro.Character;
using NarrativePro.CharacterCreator.Options;
using NarrativePro.Core;
using NarrativePro.Items;

namespace NarrativePro.CharacterCreator
{
    /// <summary>
    /// 角色创建器会话控制器。对应 UE5 <c>ACharacterCreatorPlayerController</c>
    /// （CharacterCreatorPlayerController.h/.cpp —— UE5 中是留给蓝图派生用的 C++ 基类，
    /// 注释明言"cpp base，因为蓝图里更新存档文件对引用很不方便"）。
    /// <para>
    /// Flax 无 PlayerController，本类以 <see cref="Script"/> 挂载在创建器场景的宿主 Actor 上，
    /// 承担 UE5 蓝图侧的创建器会话职责：
    /// <list type="bullet">
    /// <item>会话状态：当前表单/配置、<see cref="CharacterCreatorAttributeSet"/> 与逐选项的
    /// <see cref="CreatorOptionWidget"/> 绑定。</item>
    /// <item>开/关创建器：<see cref="OpenCreator(string, GameplayTag)"/> / <see cref="CloseCreator"/>。</item>
    /// <item>随机化/重置：<see cref="Randomize"/> / <see cref="Reset"/>。</item>
    /// <item>应用：<see cref="Apply"/> 把属性集推给 <see cref="NarrativeCharacterVisual"/>。</item>
    /// <item>相机开关：<see cref="ToggleCamera"/> 翻转创建器相机并广播供场景相机接管。</item>
    /// </list>
    /// 输入绑定说明：UE5 里这些动作由蓝图 PC 的输入映射触发；Flax 的输入映射按
    /// InputSettings 资产绑定、脚本无自然注册宿主，故本类只暴露上述公开方法，
    /// 由场景中的输入处理方（如 UI 按钮/命令层）调用，不注册悬空输入动作。
    /// </para>
    /// </summary>
    public class CharacterCreatorPlayerController : Script
    {
        /// <summary>当前会话属性集（编辑目标）；未打开创建器时为 null。</summary>
        public CharacterCreatorAttributeSet CurrentAttributes { get; private set; }

        /// <summary>当前表单（创建器 UI 结构来源）。</summary>
        public CharacterCreatorForm CurrentForm { get; private set; }

        /// <summary>每个选项对应的 UI 绑定 widget（OpenCreator 时构建）。</summary>
        public List<CreatorOptionWidget> OptionWidgets { get; } = new List<CreatorOptionWidget>();

        /// <summary>创建器 UI 是否打开。</summary>
        public bool bCreatorOpen { get; private set; } = false;

        /// <summary>创建器相机是否启用（ToggleCamera 翻转）。</summary>
        public bool bCreatorCameraActive { get; private set; } = false;

        /// <summary>应用目标：属性集要写入的角色视觉（可选，经 <see cref="SetApplyTarget"/> 注册）。</summary>
        [NonSerialized]
        private NarrativeCharacterVisual _applyTargetVisual;

        [NonSerialized]
        private Actor _applyTargetCharacter;

        private string _sessionConfigPath = "";
        private GameplayTag _sessionFormTag = GameplayTag.None;
        private CharacterCreatorAttributeSet _baselineAttributes; // Reset 用的初始快照

        // ===== 会话广播（对应 UE5 蓝图 PC 上的委托） =====

        /// <summary>创建器打开（参数：属性集）。</summary>
        public event Action<CharacterCreatorAttributeSet> OnCreatorOpened;

        /// <summary>创建器关闭。</summary>
        public event Action OnCreatorClosed;

        /// <summary>属性应用（参数：属性集；宿主应刷新预览/存档）。</summary>
        public event Action<CharacterCreatorAttributeSet> OnAttributesApplied;

        /// <summary>创建器相机切换（参数：是否启用创建器相机）。</summary>
        public event Action<bool> OnCreatorCameraToggled;

        // ===== 开 / 关 =====

        /// <summary>
        /// 打开创建器会话：加载配置中的表单、构建默认属性集（走
        /// <see cref="CharacterCreatorFactory"/>，与 NarrativeDataLoader 的创建路由一致）、
        /// 为每个选项建立 <see cref="CreatorOptionWidget"/> 并绑定属性集。
        /// </summary>
        public bool OpenCreator(string configJsonPath, GameplayTag formTag)
        {
            _sessionConfigPath = configJsonPath;
            _sessionFormTag = formTag;

            CharacterCreatorForm form = null;
            try
            {
                var config = CharacterCreatorFactory.LoadConfiguration(configJsonPath);
                if (config?.CharacterCreatorForms != null)
                {
                    form = config.CharacterCreatorForms.Find(f => f != null && f.FormTag == formTag);
                }
            }
            catch (Exception ex)
            {
                NarrativeLog.LogError($"[CharacterCreatorPC] 配置加载失败 {configJsonPath}: {ex.Message}");
                return false;
            }

            if (form == null)
            {
                NarrativeLog.LogWarning($"[CharacterCreatorPC] 配置中未找到表单 {formTag}");
                return false;
            }

            return OpenCreator(form);
        }

        /// <summary>用已加载的表单打开创建器会话。</summary>
        public bool OpenCreator(CharacterCreatorForm form)
        {
            if (form == null) return false;

            CloseCreator(); // 重启会话（对应 UE5 蓝图重新初始化）

            CurrentForm = form;
            CurrentAttributes = CharacterCreatorFactory.CreateDefaultAttributeSet(form);
            if (CurrentAttributes == null)
            {
                NarrativeLog.LogError("[CharacterCreatorPC] 默认属性集构建失败");
                return false;
            }
            _baselineAttributes = CharacterCreatorFactory.CreateDefaultAttributeSet(form);

            // 逐选项建 widget 并绑定 —— 对应 UE5 CreateCreatorOptionWidget(ExposeOnSpawn=CreatorOption)
            foreach (var page in form.CharacterCreatorPages)
            {
                if (page?.PageSections == null) continue;
                foreach (var section in page.PageSections)
                {
                    if (section?.Options == null) continue;
                    foreach (var option in section.Options)
                    {
                        var widget = CreatorOptionWidget.CreateWidget(option);
                        if (widget == null) continue;
                        widget.BindAttributeSet(CurrentAttributes);
                        OptionWidgets.Add(widget);
                    }
                }
            }

            bCreatorOpen = true;
            OnCreatorOpened?.Invoke(CurrentAttributes);
            return true;
        }

        /// <summary>关闭创建器会话，释放 widget 绑定。</summary>
        public void CloseCreator()
        {
            if (!bCreatorOpen && OptionWidgets.Count == 0 && CurrentAttributes == null)
            {
                return;
            }

            OptionWidgets.Clear();
            CurrentAttributes = null;
            _baselineAttributes = null;
            CurrentForm = null;
            bCreatorOpen = false;
            OnCreatorClosed?.Invoke();
        }

        // ===== 随机化 / 重置 / 应用 =====

        /// <summary>
        /// 随机化所有选项：标量在 [Min,Max] 内随机并经 widget 推回属性集；
        /// 向量在色板内随机；网格/毛发在列表内随机。对应 UE5 蓝图 Randomize 按钮逻辑。
        /// </summary>
        public void Randomize()
        {
            if (!bCreatorOpen || CurrentAttributes == null) return;

            var rng = new Random(Environment.TickCount);
            foreach (var widget in OptionWidgets)
            {
                switch (widget.CreatorOption)
                {
                    case CharacterCreatorOption_Scalar scalar:
                        widget.PushScalarValue(scalar.MinValue + (float)rng.NextDouble() * (scalar.MaxValue - scalar.MinValue));
                        break;

                    case CharacterCreatorOption_Vector vector:
                        if (widget.VectorSwatch != null && widget.VectorSwatch.Colors.Count > 0)
                        {
                            widget.SelectVectorOption(rng.Next(widget.VectorSwatch.Colors.Count));
                        }
                        break;

                    case CharacterCreatorOption_Mesh mesh:
                        if (mesh.Meshes.Count > 0)
                        {
                            widget.SelectItemOption(mesh.Meshes[rng.Next(mesh.Meshes.Count)]?.ItemDisplayName);
                        }
                        break;

                    case CharacterCreatorOption_Groom groom:
                        if (groom.Grooms.Count > 0)
                        {
                            widget.SelectItemOption(groom.Grooms[rng.Next(groom.Grooms.Count)]?.ItemDisplayName);
                        }
                        break;
                }
            }
        }

        /// <summary>重置为表单默认值：重建默认属性集并重新绑定所有 widget。</summary>
        public void Reset()
        {
            if (!bCreatorOpen || CurrentForm == null) return;

            CurrentAttributes = _baselineAttributes != null
                ? CloneBaseline(_baselineAttributes)
                : CharacterCreatorFactory.CreateDefaultAttributeSet(CurrentForm);

            foreach (var widget in OptionWidgets)
            {
                widget.BindAttributeSet(CurrentAttributes);
            }
        }

        /// <summary>按 Tag/Slot 深拷贝一份基线属性集（避免下次 Reset 被会话编辑污染）。</summary>
        private static CharacterCreatorAttributeSet CloneBaseline(CharacterCreatorAttributeSet source)
        {
            var copy = new CharacterCreatorAttributeSet
            {
                FormTag = source.FormTag,
                CharacterVisualClassPath = source.CharacterVisualClassPath,
                BaseMesh = source.BaseMesh,
                bHideBaseMesh = source.bHideBaseMesh,
                BaseMeshAnimBPPath = source.BaseMeshAnimBPPath,
                BaseLocalMeshAnimBPPath = source.BaseLocalMeshAnimBPPath,
                UnarmedAnimLayerPath = source.UnarmedAnimLayerPath,
            };
            foreach (var e in source.MeshEntries) copy.MeshEntries.Add(new MeshAttributeEntry { Slot = e.Slot, Attribute = e.Attribute });
            foreach (var e in source.GroomEntries) copy.GroomEntries.Add(new GroomAttributeEntry { Slot = e.Slot, Attribute = e.Attribute });
            foreach (var m in source.Morphs) copy.Morphs.Add(new CharacterCreatorAttribute_Morph { MorphName = m.MorphName, MorphValue = m.MorphValue });
            foreach (var e in source.ScalarEntries) copy.ScalarEntries.Add(new ScalarValueEntry { Tag = e.Tag, Value = e.Value });
            foreach (var e in source.VectorEntries) copy.VectorEntries.Add(new VectorValueEntry { Tag = e.Tag, Value = e.Value });
            return copy;
        }

        /// <summary>注册属性应用目标（角色视觉脚本 + 所属角色 Actor）。</summary>
        public void SetApplyTarget(NarrativeCharacterVisual visual, Actor character)
        {
            _applyTargetVisual = visual;
            _applyTargetCharacter = character;
        }

        /// <summary>
        /// 应用当前属性集：推给已注册的 <see cref="NarrativeCharacterVisual"/>
        /// （对应 UE5 蓝图 Apply 按钮 → 更新预览角色并写存档），并广播 <see cref="OnAttributesApplied"/>
        /// 供存档/UI 层跟进。
        /// </summary>
        public bool Apply()
        {
            if (!bCreatorOpen || CurrentAttributes == null) return false;

            if (_applyTargetVisual != null)
            {
                _applyTargetVisual.InitializeFromCharacterAndAttributes(_applyTargetCharacter, CurrentAttributes);
            }
            OnAttributesApplied?.Invoke(CurrentAttributes);
            return true;
        }

        // ===== 相机 =====

        /// <summary>
        /// 切换创建器相机（对应 UE5 蓝图 PC 的相机 toggle 输入动作）：
        /// 翻转 <see cref="bCreatorCameraActive"/> 并广播，由场景相机/预览宿主接管实际切换。
        /// </summary>
        public void ToggleCamera()
        {
            bCreatorCameraActive = !bCreatorCameraActive;
            OnCreatorCameraToggled?.Invoke(bCreatorCameraActive);
        }
    }
}
