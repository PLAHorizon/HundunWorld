using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using FlaxEngine;
using NarrativePro.CharacterCreator.Items;
using NarrativePro.CharacterCreator.Options;
using NarrativePro.Core;

namespace NarrativePro.CharacterCreator
{
    /// <summary>
    /// 角色创建器配置工厂。对应 UE5 中以 UDataAsset 形式存在的 CharacterCreatorConfiguration 及其子资产；
    /// Flax 中按本仓库 JSON 工厂惯例（参见 Tales/Quest/QuestFactory、Items/ItemFactory）从 JSON 资产加载。
    /// 提供配置加载、色板加载（带缓存）与“按表单默认值创建角色属性集”的创建路由。
    /// </summary>
    public static class CharacterCreatorFactory
    {
        private static readonly JsonSerializerOptions _jsonOptions = new JsonSerializerOptions
        {
            IncludeFields = true,
            PropertyNameCaseInsensitive = true,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            Converters = { new JsonStringEnumConverter() }
        };

        private static readonly Dictionary<string, CreatorColorSwatch> _swatchCache =
            new Dictionary<string, CreatorColorSwatch>(StringComparer.Ordinal);

        // ===== JSON 资产加载 =====

        /// <summary>从 JSON 文件加载角色创建器配置。对应 UE5 UCharacterCreatorConfiguration 资产引用。</summary>
        public static CharacterCreatorConfiguration LoadConfiguration(string jsonFilePath)
        {
            try
            {
                if (string.IsNullOrEmpty(jsonFilePath) || !File.Exists(jsonFilePath))
                {
                    NarrativeLog.LogError($"角色创建器配置文件不存在: {jsonFilePath}");
                    return null;
                }
                string json = File.ReadAllText(jsonFilePath);
                return LoadConfigurationFromJson(json);
            }
            catch (Exception ex)
            {
                NarrativeLog.LogError($"加载角色创建器配置失败 '{jsonFilePath}': {ex.Message}");
                return null;
            }
        }

        /// <summary>从 JSON 字符串反序列化角色创建器配置。</summary>
        public static CharacterCreatorConfiguration LoadConfigurationFromJson(string json)
        {
            try
            {
                var config = JsonSerializer.Deserialize<CharacterCreatorConfiguration>(json, _jsonOptions);
                if (config != null)
                {
                    NarrativeLog.Log($"[CharacterCreator] 配置加载完成：{config.CharacterCreatorForms?.Count ?? 0} 个表单。");
                }
                return config;
            }
            catch (Exception ex)
            {
                NarrativeLog.LogError($"反序列化角色创建器配置失败: {ex.Message}");
                return null;
            }
        }

        /// <summary>加载目录下所有创建器配置 JSON（文件名 → 配置）。</summary>
        public static Dictionary<string, CharacterCreatorConfiguration> LoadAllConfigurations(string directoryPath)
        {
            var result = new Dictionary<string, CharacterCreatorConfiguration>(StringComparer.Ordinal);
            if (string.IsNullOrEmpty(directoryPath) || !Directory.Exists(directoryPath)) return result;

            foreach (var file in Directory.GetFiles(directoryPath, "*.json"))
            {
                var config = LoadConfiguration(file);
                if (config != null)
                {
                    result[Path.GetFileNameWithoutExtension(file)] = config;
                }
            }
            return result;
        }

        /// <summary>从 JSON 文件加载色板。对应 UE5 UCharacterCreatorColorSwatch 资产。</summary>
        public static CreatorColorSwatch LoadColorSwatch(string swatchPath)
        {
            if (string.IsNullOrEmpty(swatchPath)) return null;

            if (_swatchCache.TryGetValue(swatchPath, out var cached)) return cached;

            try
            {
                CreatorColorSwatch swatch = null;
                if (File.Exists(swatchPath))
                {
                    string json = File.ReadAllText(swatchPath);
                    swatch = JsonSerializer.Deserialize<CreatorColorSwatch>(json, _jsonOptions);
                }
                else
                {
                    // 非文件路径：按 Flax 资产路径处理（JSON 资产由 JsonAsset 体系承载时此处返回 null 并告警）
                    NarrativeLog.LogWarning($"[CharacterCreator] 色板文件不存在: {swatchPath}");
                }

                if (swatch != null) _swatchCache[swatchPath] = swatch;
                return swatch;
            }
            catch (Exception ex)
            {
                NarrativeLog.LogError($"加载色板失败 '{swatchPath}': {ex.Message}");
                return null;
            }
        }

        /// <summary>清空色板缓存（热重载/测试用）。</summary>
        public static void ClearSwatchCache()
        {
            _swatchCache.Clear();
        }

        // ===== 创建路由 =====

        /// <summary>按 FormTag 在配置中查找表单。</summary>
        public static CharacterCreatorForm FindForm(CharacterCreatorConfiguration configuration, NarrativePro.Items.GameplayTag formTag)
        {
            if (configuration?.CharacterCreatorForms == null) return null;
            foreach (var form in configuration.CharacterCreatorForms)
            {
                if (form != null && form.FormTag == formTag) return form;
            }
            return null;
        }

        /// <summary>
        /// 由表单默认配置构建角色属性集（创建路由）：对应 UE5 角色创建器切换表单时
        /// 应用 UCharacterCreatorForm 默认网格/毛发/基础网格的行为。
        /// 产出的属性集可交给 NarrativeCharacterVisual.InitializeFromCharacterAndAttributes 应用。
        /// </summary>
        public static CharacterCreatorAttributeSet CreateDefaultAttributeSet(CharacterCreatorForm form)
        {
            if (form == null) return null;

            var attributes = new CharacterCreatorAttributeSet
            {
                FormTag = form.FormTag,
                CharacterVisualClassPath = form.CharacterVisualClassPath,
                bHideBaseMesh = form.bHideBaseMesh,
                BaseMeshAnimBPPath = form.DefaultCharacterUnarmedAnimPath,
                UnarmedAnimLayerPath = form.DefaultCharacterUnarmedAnimPath
            };

            // 基础网格：按路径加载 SkinnedModel
            if (!string.IsNullOrEmpty(form.BaseMeshPath))
            {
                attributes.BaseMesh = Content.Load<SkinnedModel>(form.BaseMeshPath);
                if (attributes.BaseMesh == null)
                {
                    NarrativeLog.LogWarning($"[CharacterCreator] 基础网格加载失败: {form.BaseMeshPath}");
                }
            }

            // 默认网格项：逐项写入 slot 条目（经 NarrativeSaveWithCreatorData 的同一构建逻辑）
            var saveCarrier = new NarrativeSaveWithCreatorData();
            foreach (var entry in form.DefaultCharacterCreatorMeshes)
            {
                if (entry?.Mesh == null) continue;
                saveCarrier.SetCreatorDataMesh(entry.Mesh);

                // 将构建好的条目转移到目标属性集（slot 覆盖）
                var built = saveCarrier.CharacterCreatorAttributes.MeshEntries.Find(e => e.Slot == entry.Slot);
                if (built != null)
                {
                    int idx = attributes.MeshEntries.FindIndex(e => e.Slot == entry.Slot);
                    if (idx >= 0) attributes.MeshEntries[idx] = built;
                    else attributes.MeshEntries.Add(built);
                }

                TransferGlobalValues(saveCarrier.CharacterCreatorAttributes, attributes);
            }

            foreach (var entry in form.DefaultCharacterCreatorGrooms)
            {
                if (entry?.Groom == null) continue;
                saveCarrier.SetCreatorDataGroom(entry.Groom);

                var built = saveCarrier.CharacterCreatorAttributes.GroomEntries.Find(e => e.Slot == entry.Slot);
                if (built != null)
                {
                    int idx = attributes.GroomEntries.FindIndex(e => e.Slot == entry.Slot);
                    if (idx >= 0) attributes.GroomEntries[idx] = built;
                    else attributes.GroomEntries.Add(built);
                }

                // 转移该毛发/网格携带的全局标量/向量默认值
                TransferGlobalValues(saveCarrier.CharacterCreatorAttributes, attributes);
            }

            // 遍历各页面/分区，登记选项默认值（标量取 DefaultValue；颜色由 UI 选择后写入）
            foreach (var page in form.CharacterCreatorPages)
            {
                if (page?.PageSections == null) continue;
                foreach (var section in page.PageSections)
                {
                    if (section?.Options == null) continue;
                    foreach (var option in section.Options)
                    {
                        switch (option)
                        {
                            case CharacterCreatorOption_Scalar scalarOption when scalarOption.ScalarTagID.IsValid():
                                if (!attributes.ScalarEntries.Exists(e => e.Tag == scalarOption.ScalarTagID))
                                {
                                    attributes.ScalarEntries.Add(new ScalarValueEntry
                                    {
                                        Tag = scalarOption.ScalarTagID,
                                        Value = scalarOption.DefaultValue
                                    });
                                }
                                break;

                            case CharacterCreatorOption_Vector vectorOption when vectorOption.VectorTagID.IsValid():
                                if (!attributes.VectorEntries.Exists(e => e.Tag == vectorOption.VectorTagID))
                                {
                                    var swatch = LoadColorSwatch(vectorOption.AvailableOptionsSwatchPath);
                                    attributes.VectorEntries.Add(new VectorValueEntry
                                    {
                                        Tag = vectorOption.VectorTagID,
                                        Value = swatch != null && swatch.Colors.Count > 0 ? swatch.Colors[0] : Color.White
                                    });
                                }
                                break;
                        }
                    }
                }
            }

            return attributes;
        }

        /// <summary>把来源属性集中的全局标量/向量值合并进目标属性集（按 Tag 覆盖或追加）。</summary>
        private static void TransferGlobalValues(CharacterCreatorAttributeSet source, CharacterCreatorAttributeSet target)
        {
            if (source == null || target == null) return;

            foreach (var scalar in source.ScalarEntries)
            {
                if (scalar == null) continue;
                var existing = target.ScalarEntries.Find(e => e.Tag == scalar.Tag);
                if (existing != null) existing.Value = scalar.Value;
                else target.ScalarEntries.Add(scalar);
            }
            foreach (var vector in source.VectorEntries)
            {
                if (vector == null) continue;
                var existing = target.VectorEntries.Find(e => e.Tag == vector.Tag);
                if (existing != null) existing.Value = vector.Value;
                else target.VectorEntries.Add(vector);
            }
        }

        /// <summary>便捷入口：从配置文件按表单标签创建默认属性集。</summary>
        public static CharacterCreatorAttributeSet CreateDefaultAttributeSet(string configJsonPath, NarrativePro.Items.GameplayTag formTag)
        {
            var config = LoadConfiguration(configJsonPath);
            var form = FindForm(config, formTag);
            if (form == null)
            {
                NarrativeLog.LogWarning($"[CharacterCreator] 配置中未找到表单: {formTag}（{configJsonPath}）");
                return null;
            }
            return CreateDefaultAttributeSet(form);
        }
    }
}
