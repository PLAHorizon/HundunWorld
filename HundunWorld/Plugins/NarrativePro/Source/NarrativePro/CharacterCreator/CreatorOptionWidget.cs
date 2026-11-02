using System;
using System.Collections.Generic;
using FlaxEngine;
using FlaxEngine.GUI;
using NarrativePro.CharacterCreator.Items;
using NarrativePro.CharacterCreator.Options;
using NarrativePro.CommonUI.Widgets;
using NarrativePro.Core;
using NarrativePro.Items;

namespace NarrativePro.CharacterCreator
{
    /// <summary>
    /// 创建器选项控件绑定。对应 UE5 <c>UCreatorOptionWidget</c>
    /// （CharacterCreator/UI/CreatorOptionWidget.h：ExposeOnSpawn 的 CreatorOption 指针）。
    /// <para>
    /// UE5 中派生控件由蓝图实现；Flax 无 UMG/蓝图等价物，故本类以代码方式承担
    /// “一个 <see cref="CharacterCreatorOption"/> ↔ 一个编辑控件” 的绑定：
    /// <list type="bullet">
    /// <item>Scalar → <see cref="NarrativeSpinBox"/>（数值显示）+ 可选 <see cref="NarrativeAnalogSlider"/>（拖动）。</item>
    /// <item>Vector → <see cref="NarrativeComboBoxString"/>（颜色色板选项）。</item>
    /// <item>Mesh / Groom → <see cref="NarrativeComboBoxString"/>（项显示名列表）。</item>
    /// </list>
    /// 值变化经 <see cref="BindAttributeSet"/> 绑定到 <see cref="CharacterCreatorAttributeSet"/>
    /// （与 <see cref="CharacterCreatorFactory.CreateDefaultAttributeSet(CharacterCreatorForm)"/>
    /// 构建的条目布局一致：ScalarEntries/VectorEntries/MeshEntries/GroomEntries 按 Tag/Slot 寻址）。
    /// </para>
    /// </summary>
    public class CreatorOptionWidget
    {
        /// <summary>绑定到本 widget 的创建器选项。对应 UE5 <c>CreatorOption</c>（ExposeOnSpawn）。</summary>
        public CharacterCreatorOption CreatorOption;

        /// <summary>要读写的属性集；为空时仅广播事件不改写属性。</summary>
        public CharacterCreatorAttributeSet BoundAttributes;

        // ===== 按选项类型构建的控件（互斥，至多一个非空） =====

        /// <summary>Scalar 选项的数值框。</summary>
        public NarrativeSpinBox ScalarSpinBox { get; private set; }

        /// <summary>Scalar 选项的模拟滑条（可选；用 <see cref="EnableScalarSlider"/> 创建）。</summary>
        public NarrativeAnalogSlider ScalarSlider { get; private set; }

        /// <summary>Vector/Mesh/Groom 选项的下拉框。</summary>
        public NarrativeComboBoxString OptionComboBox { get; private set; }

        /// <summary>Vector 选项加载到的色板。</summary>
        public CreatorColorSwatch VectorSwatch { get; private set; }

        // ===== 值变化广播 =====

        /// <summary>标量值变化（已按 Min/Max 钳制）。</summary>
        public event Action<float> ScalarValueChanged;

        /// <summary>向量（颜色）值变化。</summary>
        public event Action<Color> VectorValueChanged;

        /// <summary>网格/毛发选项切换。</summary>
        public event Action<CharacterCreatorItem> ItemSelectionChanged;

        // ===== 构造：按选项类型创建对应控件 =====

        /// <summary>
        /// 工厂入口。对应 UE5 中按 CreatorOption 类型实例化派生 widget 的行为。
        /// </summary>
        public static CreatorOptionWidget CreateWidget(CharacterCreatorOption option)
        {
            if (option == null) return null;

            var widget = new CreatorOptionWidget { CreatorOption = option };
            switch (option)
            {
                case CharacterCreatorOption_Scalar scalar:
                    widget.ScalarSpinBox = new NarrativeSpinBox
                    {
                        MinValue = scalar.MinValue,
                        MaxValue = scalar.MaxValue,
                        Value = scalar.DefaultValue,
                    };
                    widget.ScalarSpinBox.RebuildWidget();
                    break;

                case CharacterCreatorOption_Vector vector:
                    widget.OptionComboBox = new NarrativeComboBoxString();
                    widget.VectorSwatch = LoadSwatch(vector.AvailableOptionsSwatchPath);
                    for (int i = 0; i < (widget.VectorSwatch?.Colors?.Count ?? 0); i++)
                    {
                        Color c = widget.VectorSwatch.Colors[i];
                        widget.OptionComboBox.DefaultOptions.Add(
                            $"{vector.OptionDisplayName}_{i}(#{(int)(c.R * 255):X2}{(int)(c.G * 255):X2}{(int)(c.B * 255):X2})");
                    }
                    widget.OptionComboBox.RebuildWidget();
                    break;

                case CharacterCreatorOption_Mesh mesh:
                    widget.OptionComboBox = new NarrativeComboBoxString();
                    foreach (var item in mesh.Meshes)
                    {
                        if (item != null) widget.OptionComboBox.DefaultOptions.Add(item.ItemDisplayName);
                    }
                    widget.OptionComboBox.RebuildWidget();
                    break;

                case CharacterCreatorOption_Groom groom:
                    widget.OptionComboBox = new NarrativeComboBoxString();
                    foreach (var item in groom.Grooms)
                    {
                        if (item != null) widget.OptionComboBox.DefaultOptions.Add(item.ItemDisplayName);
                    }
                    widget.OptionComboBox.RebuildWidget();
                    break;
            }
            return widget;
        }

        private static CreatorColorSwatch LoadSwatch(string swatchPath)
        {
            if (string.IsNullOrEmpty(swatchPath)) return null;
            try
            {
                return CharacterCreatorFactory.LoadColorSwatch(swatchPath);
            }
            catch (Exception ex)
            {
                NarrativeLog.LogWarning($"[CreatorOptionWidget] 色板加载失败 {swatchPath}: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// 为 Scalar 选项额外创建一个可拖动的模拟滑条并自动推值。
        /// 滑条是真实 Flax <see cref="Slider"/> 控件，ValueChanged 直接驱动属性集。
        /// </summary>
        public NarrativeAnalogSlider EnableScalarSlider(ContainerControl parent = null)
        {
            if (!(CreatorOption is CharacterCreatorOption_Scalar scalar) || ScalarSlider != null)
            {
                return ScalarSlider;
            }

            ScalarSlider = new NarrativeAnalogSlider
            {
                Parent = parent,
                Minimum = scalar.MinValue,
                Maximum = scalar.MaxValue,
                Value = scalar.DefaultValue,
            };
            ScalarSlider.SlidingCommitted += _ => PushScalarValue(ScalarSlider.Value);
            return ScalarSlider;
        }

        // ===== 绑定与初始值拉取 =====

        /// <summary>
        /// 绑定属性集并按其当前值拉取初始显示值（对应 UE5 蓝图 widget 的 NativeConstruct 读默认值）。
        /// </summary>
        public void BindAttributeSet(CharacterCreatorAttributeSet attributes)
        {
            BoundAttributes = attributes;
            if (attributes == null) return;

            switch (CreatorOption)
            {
                case CharacterCreatorOption_Scalar s when s.ScalarTagID.IsValid():
                    float value = attributes.GetScalarValue(s.ScalarTagID);
                    // 属性集中尚无该 Tag 时保留选项默认值（与工厂登记 DefaultValue 的行为一致）。
                    if (attributes.ScalarEntries.Exists(e => e.Tag == s.ScalarTagID) && ScalarSpinBox != null)
                    {
                        ScalarSpinBox.SetValue(value);
                    }
                    if (ScalarSlider != null)
                    {
                        ScalarSlider.Value = Mathf.Clamp(value, s.MinValue, s.MaxValue);
                    }
                    break;

                case CharacterCreatorOption_Vector v when v.VectorTagID.IsValid() && OptionComboBox != null:
                    if (VectorSwatch != null && VectorSwatch.Colors.Count > 0)
                    {
                        Color current = attributes.GetVectorValue(v.VectorTagID);
                        int idx = VectorSwatch.Colors.FindIndex(c => ColorApproxEqual(c, current));
                        if (idx < 0) idx = 0; // 工厂初始写入色板首色
                        OptionComboBox.SelectedOption = idx < OptionComboBox.DefaultOptions.Count
                            ? OptionComboBox.DefaultOptions[idx] : string.Empty;
                    }
                    break;

                case CharacterCreatorOption_Mesh m when OptionComboBox != null:
                    // 初始选中：属性集里该 slot 已有条目则保持第一项（工厂已按表单默认网格写入条目），
                    // Flax 无法反查条目对应哪个 item，按列表首项呈现。
                    OptionComboBox.SelectedOption = OptionComboBox.DefaultOptions.Count > 0
                        ? OptionComboBox.DefaultOptions[0] : string.Empty;
                    break;

                case CharacterCreatorOption_Groom g when OptionComboBox != null:
                    OptionComboBox.SelectedOption = OptionComboBox.DefaultOptions.Count > 0
                        ? OptionComboBox.DefaultOptions[0] : string.Empty;
                    break;
            }
        }

        private static bool ColorApproxEqual(Color a, Color b)
        {
            const float eps = 0.0039f; // 1/255
            return Math.Abs(a.R - b.R) < eps && Math.Abs(a.G - b.G) < eps && Math.Abs(a.B - b.B) < eps;
        }

        // ===== 值推送（UI → 选项 / 属性集） =====

        /// <summary>推送标量值：钳制到 [Min,Max]，写 spinbox 显示并更新属性集。</summary>
        public void PushScalarValue(float newValue)
        {
            if (!(CreatorOption is CharacterCreatorOption_Scalar scalar)) return;

            newValue = Mathf.Clamp(newValue, scalar.MinValue, scalar.MaxValue);
            ScalarSpinBox?.SetValue(newValue);

            if (BoundAttributes != null && scalar.ScalarTagID.IsValid())
            {
                var entry = BoundAttributes.ScalarEntries.Find(e => e.Tag == scalar.ScalarTagID);
                if (entry == null)
                {
                    entry = new ScalarValueEntry { Tag = scalar.ScalarTagID };
                    BoundAttributes.ScalarEntries.Add(entry);
                }
                entry.Value = newValue;
            }
            ScalarValueChanged?.Invoke(newValue);
        }

        /// <summary>
        /// 选择向量（颜色）选项：按下拉项索引取色板颜色写入 VectorEntries。
        /// 返回 false 表示选项非 Vector 或索引无效。
        /// </summary>
        public bool SelectVectorOption(int colorIndex)
        {
            if (!(CreatorOption is CharacterCreatorOption_Vector vector) || VectorSwatch == null) return false;
            if (colorIndex < 0 || colorIndex >= VectorSwatch.Colors.Count) return false;

            Color picked = VectorSwatch.Colors[colorIndex];
            if (OptionComboBox != null && colorIndex < OptionComboBox.DefaultOptions.Count)
            {
                OptionComboBox.SelectedOption = OptionComboBox.DefaultOptions[colorIndex];
            }

            if (BoundAttributes != null && vector.VectorTagID.IsValid())
            {
                var entry = BoundAttributes.VectorEntries.Find(e => e.Tag == vector.VectorTagID);
                if (entry == null)
                {
                    entry = new VectorValueEntry { Tag = vector.VectorTagID };
                    BoundAttributes.VectorEntries.Add(entry);
                }
                entry.Value = picked;
            }
            VectorValueChanged?.Invoke(picked);
            return true;
        }

        /// <summary>
        /// 选择网格/毛发项（按显示名）。Mesh 项经 <see cref="CharacterCreatorItem_Mesh.ToMeshAttribute"/>
        /// 构建属性条目并覆盖 MeshEntries/对应 Slot；Groom 项直接构建 GroomEntries。
        /// </summary>
        public bool SelectItemOption(string displayName)
        {
            CharacterCreatorItem item = null;
            switch (CreatorOption)
            {
                case CharacterCreatorOption_Mesh m:
                    item = m.Meshes.Find(i => i != null && i.ItemDisplayName == displayName);
                    break;
                case CharacterCreatorOption_Groom g:
                    item = g.Grooms.Find(i => i != null && i.ItemDisplayName == displayName);
                    break;
            }
            if (item == null || BoundAttributes == null) return false;

            if (item is CharacterCreatorItem_Mesh meshItem)
            {
                CharacterCreatorAttribute_Mesh attr = meshItem.ToMeshAttribute();
                int idx = BoundAttributes.MeshEntries.FindIndex(e => e.Slot == meshItem.Slot);
                var entry = new MeshAttributeEntry { Slot = meshItem.Slot, Attribute = attr };
                if (idx >= 0) BoundAttributes.MeshEntries[idx] = entry;
                else BoundAttributes.MeshEntries.Add(entry);
            }
            else if (item is CharacterCreatorItem_Groom groomItem)
            {
                var attr = new CharacterCreatorAttribute_Groom
                {
                    GroomAssetPath = groomItem.GroomAssetPath,
                    GroomBindingAssetPath = groomItem.GroomBindingAssetPath,
                };
                if (groomItem.GroomMaterials != null)
                {
                    foreach (var option in groomItem.GroomMaterials)
                    {
                        if (option != null) attr.GroomMaterials.Add(option.GetDefaultMaterial());
                    }
                }
                int idx = BoundAttributes.GroomEntries.FindIndex(e => e.Slot == groomItem.Slot);
                var entry = new GroomAttributeEntry { Slot = groomItem.Slot, Attribute = attr };
                if (idx >= 0) BoundAttributes.GroomEntries[idx] = entry;
                else BoundAttributes.GroomEntries.Add(entry);
            }

            if (OptionComboBox != null)
            {
                OptionComboBox.SelectedOption = displayName;
            }
            ItemSelectionChanged?.Invoke(item);
            return true;
        }
    }
}
