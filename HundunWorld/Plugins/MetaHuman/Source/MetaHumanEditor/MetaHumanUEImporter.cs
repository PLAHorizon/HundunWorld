using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.Json;
using FlaxEditor;
using FlaxEngine;
using FlaxEngine.Tools;
using MetaHuman;
using MetaHuman.Core;
using MetaHuman.Runtime;

namespace MetaHumanEditor
{
    /// <summary>UE5 MetaHuman 导出目录的扫描结果。</summary>
    public class MetaHumanUEImportPlan
    {
        public string SourceDirectory { get; set; }
        public string DestinationFolder { get; set; }
        public List<string> ModelFiles { get; set; }
        public Dictionary<string, string> TexturesBySlot { get; set; }
        public List<string> DescriptionFiles { get; set; }
        public List<string> Warnings { get; set; }

        public MetaHumanUEImportPlan()
        {
            ModelFiles = new List<string>();
            TexturesBySlot = new Dictionary<string, string>();
            DescriptionFiles = new List<string>();
            Warnings = new List<string>();
        }

        public bool IsEmpty
        {
            get { return ModelFiles.Count == 0; }
        }
    }

    /// <summary>
    /// UE5 MetaHuman 资源适配器：读导出目录（FBX/OBJ + EXR/PNG 贴图组 + 描述 JSON），
    /// 用引擎的脚本化导入（Editor.Import + ModelTool.Options）把它们真正导成 Flax 资产，
    /// 关键是强制开启 ImportBlendShapes —— 引擎默认关，而关掉就等于所有形状参数静默失效。
    /// </summary>
    public static class MetaHumanUEImporter
    {
        private static readonly string[] ModelExtensions = { ".fbx", ".obj", ".gltf", ".glb", ".za" };

        // 贴图槽位识别，取自 MetaHumanSDK 的 EFaceTextureType / EBodyTextureType 名录
        private static readonly string[] TextureSlotTokens =
        {
            "basecolor_animated_cm1", "basecolor_animated_cm2", "basecolor_animated_cm3", "basecolor",
            "normal_animated_wm1", "normal_animated_wm2", "normal_animated_wm3", "normal",
            "cavity", "specular", "roughness", "opacity",
            "body_basecolor", "body_normal", "body_cavity", "chest_basecolor", "chest_normal",
        };

        public static MetaHumanUEImportPlan Scan(string sourceDirectory, string destinationFolder)
        {
            MetaHumanUEImportPlan plan = new MetaHumanUEImportPlan();
            plan.SourceDirectory = sourceDirectory;
            plan.DestinationFolder = destinationFolder;

            if (!Directory.Exists(sourceDirectory))
            {
                plan.Warnings.Add("目录不存在：" + sourceDirectory);
                return plan;
            }

            foreach (string file in Directory.GetFiles(sourceDirectory, "*.*", SearchOption.AllDirectories))
            {
                string extension = Path.GetExtension(file).ToLowerInvariant();
                string name = Path.GetFileName(file).ToLowerInvariant();

                if (Array.IndexOf(ModelExtensions, extension) >= 0)
                {
                    plan.ModelFiles.Add(file);
                }
                else if (extension == ".json")
                {
                    plan.DescriptionFiles.Add(file);
                }
                else if (extension == ".exr" || extension == ".png" || extension == ".tga" || extension == ".jpg")
                {
                    string slot = SlotOf(name);
                    if (slot != null && !plan.TexturesBySlot.ContainsKey(slot))
                    {
                        plan.TexturesBySlot[slot] = file;
                    }
                }
                else if (extension == ".dna")
                {
                    plan.Warnings.Add("DNA 容器不在 Flax 侧解析（PCA identity 模型不移植）：" + Path.GetFileName(file)
                        + "；形状改由资产自带的 blendshape 与面部关节承担");
                }
            }

            if (plan.ModelFiles.Count == 0)
            {
                plan.Warnings.Add("目录里没有任何模型文件（试过 " + string.Join(" ", ModelExtensions) + "）");
            }

            if (!plan.TexturesBySlot.ContainsKey("basecolor"))
            {
                plan.Warnings.Add("没找到 basecolor 贴图：导入后皮肤材质会是默认外观");
            }

            return plan;
        }

        private static string SlotOf(string lowerFileName)
        {
            // 长 token 先匹配，避免 "basecolor" 抢走 "basecolor_animated_cm1"
            foreach (string token in TextureSlotTokens)
            {
                if (lowerFileName.Contains(token))
                {
                    return token;
                }
            }

            return null;
        }

        /// <summary>导入计划里的模型，返回成功的资产路径。</summary>
        public static List<string> ImportModels(MetaHumanUEImportPlan plan, out int failed)
        {
            List<string> imported = new List<string>();
            failed = 0;

            foreach (string source in plan.ModelFiles)
            {
                string stem = Path.GetFileNameWithoutExtension(source);
                string destination = CombineAssetPath(plan.DestinationFolder, stem);
                string resultFile = ProjectFileOf(destination);
                string directory = Path.GetDirectoryName(resultFile);
                if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                // 必须拿引擎自己的默认设置当底座：`new ModelTool.Options()` 是结构体零值，
                // 会把 Scale 等四十多格一起压成 0（asset_import 那边踩过同一坑）。
                // 更要命的是 Type 的零值是 Model —— 引擎拿到 Model 就压根不解析骨骼，
                // 导出来的资产 kind=Model，后面 Content.Load<SkinnedModel> 读空，
                // 于是「导入成功」与「模板 0 个 morph」同时成立，且全程不报错。
                ModelTool.Options options = ModelTool.Options.Default;
                options.Type = ModelTool.ModelType.SkinnedModel;
                options.ImportBlendShapes = true;
                options.ImportLODs = true;
                options.CalculateBoneOffsetMatrices = true;
                options.ImportMaterials = true;

                bool importFailed;
                try
                {
                    importFailed = Editor.Import(source, resultFile, options);
                }
                catch (Exception exception)
                {
                    plan.Warnings.Add("导入抛错 " + stem + "：" + exception.Message);
                    failed++;
                    continue;
                }

                // 判据只认盘上有没有这份资产：Editor.Import 的返回值是 true=失败
                // （与 SkinnedModel.Save 同一套反直觉约定），而且它要的是**带扩展名的结果文件路径**，
                // 传资产路径（不带 .flax）会静默什么都不产出。
                if (File.Exists(resultFile))
                {
                    imported.Add(destination);
                }
                else
                {
                    failed++;
                    plan.Warnings.Add("导入未产出资产文件：" + stem + "（Editor.Import 返回 "
                        + (importFailed ? "true=失败" : "false") + "，盘上没有 " + resultFile + "）");
                }
            }

            if (imported.Count > 0)
            {
                // 刚落盘的 .flax 还没进内容库，紧接着的 Content.Load 会读空——
                // 于是「合成模板」拿到 0 份可用资产，产出一份只照抄基础模板的文件却挂着新 Id。
                // 所以导入批次结束先刷库，让下游读到真的东西。
                try
                {
                    FlaxEditor.Editor.Instance.ContentDatabase.Rebuild(true);
                }
                catch (Exception exception)
                {
                    plan.Warnings.Add("导入后刷新内容库失败：" + exception.Message);
                }

                // 刷完库再按「能不能真的当 SkinnedModel 读出来」复核一遍：
                // 文件在盘上不等于类型对，而类型不对时下游全部静默空转。
                for (int i = imported.Count - 1; i >= 0; i--)
                {
                    if (MetaHumanAssetUtil.LoadSkinnedModel(imported[i]) != null)
                    {
                        continue;
                    }

                    plan.Warnings.Add("导入产物按 SkinnedModel 读不出来，已从成功清单剔除：" + imported[i]);
                    imported.RemoveAt(i);
                    failed++;
                }
            }

            return imported;
        }

        /// <summary>资产路径（不带扩展名）→ 它在工程里的实际文件路径。</summary>
        private static string ProjectFileOf(string assetPath)
        {
            string withExtension = assetPath.EndsWith(".flax", StringComparison.OrdinalIgnoreCase)
                ? assetPath
                : assetPath + ".flax";

            if (Path.IsPathRooted(withExtension))
            {
                return withExtension;
            }

            string project = FlaxEngine.Globals.ProjectFolder;
            return string.IsNullOrEmpty(project)
                ? withExtension
                : Path.Combine(project, withExtension.Replace('/', Path.DirectorySeparatorChar));
        }

        private static string CombineAssetPath(string folder, string name)
        {
            string safe = name.Replace(' ', '_');
            if (string.IsNullOrEmpty(folder))
            {
                return "Content/MetaHuman/" + safe;
            }

            return folder.TrimEnd('/', '\\') + "/" + safe;
        }

        /// <summary>
        /// 从描述 JSON 里搬运可信字段：UE 侧 ActorCreationAPI 读的 template_description.json
        /// 带 head/teeth/eye_left/eye_right/mouth_socket 等资产指向。解析不了的键一律进 Warnings，
        /// 不做「看起来成功」的静默降级。
        /// </summary>
        public static Dictionary<string, string> ParseDescription(MetaHumanUEImportPlan plan)
        {
            Dictionary<string, string> slots = new Dictionary<string, string>();
            string[] interesting = { "head", "teeth", "eye_left", "eye_right", "mouth_socket", "smile_stab", "lip_collision_upper", "lip_collision_lower" };

            foreach (string file in plan.DescriptionFiles)
            {
                string name = Path.GetFileName(file).ToLowerInvariant();
                if (!name.Contains("description") && !name.Contains("identity_model") && !name.Contains("template"))
                {
                    continue;
                }

                try
                {
                    using (JsonDocument doc = JsonDocument.Parse(File.ReadAllText(file)))
                    {
                        JsonElement root = doc.RootElement;
                        foreach (string key in interesting)
                        {
                            if (root.TryGetProperty(key, out JsonElement value) && value.ValueKind == JsonValueKind.String)
                            {
                                slots[key] = value.GetString();
                            }
                        }
                    }
                }
                catch (Exception exception)
                {
                    plan.Warnings.Add("描述 JSON 解析失败 " + Path.GetFileName(file) + "：" + exception.Message);
                }
            }

            if (slots.Count == 0)
            {
                plan.Warnings.Add("没从任何描述 JSON 里读到部件指向（该目录可能不含 template_description.json）");
            }

            return slots;
        }

        /// <summary>
        /// 导入后装配模板：以生成好的身体/脸模板为底，把新资产的真实 blendshape 名录回填进去，
        /// 产出一份描述「这份资产」的模板文件。名字沿用资产名，路径落在插件的 Templates 目录。
        /// </summary>
        public static string ComposeTemplateFromAssets(string templateId, string displayName, IList<string> assetPaths, out MetaHumanBindingReport report)
        {
            // 一份资产都没导进来时不能"照样产出一份模板"：那会得到一份照抄基础模板、
            // 却挂着新 Id 的文件，看起来是导入成果，实际什么都没描述。
            if (assetPaths == null || assetPaths.Count == 0)
            {
                report = new MetaHumanBindingReport();
                report.Notes.Add("没有可用资产，未生成模板");
                return null;
            }

            List<SkinnedModel> models = new List<SkinnedModel>();
            foreach (string assetPath in assetPaths)
            {
                SkinnedModel model = MetaHumanAssetUtil.LoadSkinnedModel(assetPath);
                if (model == null)
                {
                    Debug.LogWarning("MetaHuman 导入：资产装载失败（内容库里读不到这份 SkinnedModel） " + assetPath);
                    continue;
                }

                models.Add(model);
            }

            MetaHumanTemplate template = MetaHumanTemplateStore.Get("metahuman.face_archetype");
            if (template == null)
            {
                template = MetaHumanTemplateStore.Get("metahuman.body_base");
            }

            if (template == null)
            {
                report = new MetaHumanBindingReport();
                report.Notes.Add("基础模板装不到，无法派生");
                return null;
            }

            // 复制一份再改，避免污染共享缓存里的基础模板
            MetaHumanTemplate derived = MetaHumanTemplate.FromJson(template.ToJson());
            derived.TemplateId = templateId;
            derived.DisplayName = displayName;

            SkinnedModel[] array = models.ToArray();
            int added = MetaHumanRigBinder.ImportMissingMorphs(derived, array);
            if (array.Length > 0 && !string.IsNullOrEmpty(array[0].Path))
            {
                derived.HeadAsset = assetPaths.Count > 0 ? assetPaths[0] : derived.HeadAsset;
            }

            StringBuilder note = new StringBuilder();
            note.Append("派生自导入资产：").Append(string.Join(" , ", assetPaths))
                .Append("；新增 morph 形状参数 ").Append(added).Append(" 根");
            derived.RequiresBlendShapeImport = true;

            string root = MetaHumanTemplateStore.RootDirectory;
            if (string.IsNullOrEmpty(root))
            {
                report = MetaHumanRigBinder.Bind(derived, array);
                report.Notes.Add("模板根目录未解析，模板未落盘");
                report.Notes.Add(note.ToString());
                return null;
            }

            string templatePath = Path.Combine(root, "Template_" + templateId + ".json");
            derived.SaveToFile(templatePath);
            report = MetaHumanRigBinder.Bind(derived, array);
            // 来历要进回执：这份模板的底是脸模板（哪怕导的是身体），且 HeadAsset 记的是第一份导入资产
            report.Notes.Add(note.ToString());
            report.Notes.Add("底模板 " + template.TemplateId + "；HeadAsset 记为第一份导入资产 " + derived.HeadAsset);
            return templatePath;
        }
    }
}
