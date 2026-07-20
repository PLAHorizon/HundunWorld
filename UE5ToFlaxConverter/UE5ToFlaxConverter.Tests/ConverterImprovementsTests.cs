using System.IO;
using FluentAssertions;
using UE5ToFlaxConverter.Core.Mappers;
using UE5ToFlaxConverter.Core.Models;
using UE5ToFlaxConverter.Core.Pipeline;
using UE5ToFlaxConverter.Core.Writers;
using Xunit;

namespace UE5ToFlaxConverter.Tests;

/// <summary>
/// 针对本轮优化引入的修复与新增功能补充的回归测试。
/// 覆盖：GameplayTagMapper 边界条件、GuessAssetType 精确匹配、
/// JsonHelper 路径规范化、Writer 异步 API、AnimSegment 默认值等。
/// </summary>
public class ConverterImprovementsTests
{
    // ============ GameplayTagMapper 边界条件 ============

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void NormalizeClassName_should_return_Unknown_for_empty_input(string? input)
    {
        var mapper = new GameplayTagMapper();
        mapper.NormalizeClassName(input!).Should().Be("Unknown");
    }

    [Fact]
    public void NormalizeClassName_should_handle_blueprint_only_suffix()
    {
        // 原始 bug：仅输入 "_C" 时替换后为空字符串，越界。
        var mapper = new GameplayTagMapper();
        mapper.NormalizeClassName("_C").Should().Be("Unnamed");
    }

    [Fact]
    public void NormalizeClassName_should_prefix_underscore_when_starting_with_digit()
    {
        var mapper = new GameplayTagMapper();
        mapper.NormalizeClassName("123Ability").Should().Be("_123Ability");
    }

    [Fact]
    public void Map_should_return_empty_for_null_or_empty_tag()
    {
        var mapper = new GameplayTagMapper();
        mapper.Map(null!).Should().BeEmpty();
        mapper.Map("").Should().BeEmpty();
        mapper.Map("   ").Should().BeEmpty();
    }

    [Fact]
    public void Map_should_prefer_longest_prefix_when_overlapping()
    {
        // 当 Ability. 与 Ability.Cooldown. 同时存在时，后者应优先匹配。
        var mapper = new GameplayTagMapper(new Dictionary<string, string>
        {
            ["Ability."] = "Gameplay.Ability.",
            ["Ability.Cooldown."] = "Gameplay.Ability.CD."
        });
        mapper.Map("Ability.Cooldown.Fireball").Should().Be("Gameplay.Ability.CD.Fireball");
        mapper.Map("Ability.Dash").Should().Be("Gameplay.Ability.Dash");
    }

    // ============ UassetProvider.GuessAssetType 精确匹配 ============

    [Theory]
    [InlineData("/Game/Characters/Hero/Meshes/SK_Body.uasset", AssetType.SkeletalMesh)]
    [InlineData("/Game/Props/Weapons/SM_Sword.uasset", AssetType.StaticMesh)]
    [InlineData("/Game/Animations/AM_Attack_Montage.uasset", AssetType.AnimationMontage)]
    [InlineData("/Game/Abilities/GA_Fireball.uasset", AssetType.GameplayAbility)]
    [InlineData("/Game/Effects/GE_Damage.uasset", AssetType.GameplayEffect)]
    [InlineData("/Game/Attributes/AS_Hero.uasset", AssetType.AttributeSet)]
    [InlineData("/Game/Effects/NS_Flame.uasset", AssetType.NiagaraSystem)]
    [InlineData("/Game/FX/PS_Smoke.uasset", AssetType.CascadeParticleSystem)]
    [InlineData("/Game/Materials/M_Iron.uasset", AssetType.Material)]
    [InlineData("/Game/Textures/T_Diffuse.uasset", AssetType.Texture2D)]
    public void GuessAssetType_should_classify_common_prefixes(string path, AssetType expected)
    {
        UE5ToFlaxConverter.Core.Readers.UassetProvider.GuessAssetType(path)
            .Should().Be(expected);
    }

    [Theory]
    // 关键回归：原 bug 中 AS_ 会误匹配 "Glass_" 等含 "AS_" 子串的路径。
    // Glass_Shelf 不含 M_ 前缀，应为 Unknown；Brass_Knob/AssetRef 同理。
    [InlineData("/Game/Props/Glass_Shelf.uasset", AssetType.Unknown)]
    [InlineData("/Game/Items/Brass_Knob.uasset", AssetType.Unknown)]
    [InlineData("/Game/Classes/AssetRef.uasset", AssetType.Unknown)]
    public void GuessAssetType_should_not_mismatch_substring_inside_words(string path, AssetType expected)
    {
        UE5ToFlaxConverter.Core.Readers.UassetProvider.GuessAssetType(path)
            .Should().Be(expected);
    }

    [Fact]
    public void GuessAssetType_should_return_unknown_for_empty_path()
    {
        UE5ToFlaxConverter.Core.Readers.UassetProvider.GuessAssetType("")
            .Should().Be(AssetType.Unknown);
    }

    // ============ JsonHelper 路径规范化 ============

    [Theory]
    [InlineData("Models/Hero.flax", "Content/Models/Hero.flax")]
    [InlineData("Content/Models/Hero.flax", "Content/Models/Hero.flax")]
    [InlineData("/Models/Hero.flax", "Content/Models/Hero.flax")]
    [InlineData("\\Models\\Hero.flax", "Content/Models/Hero.flax")]
    [InlineData("", "Content/")]
    public void ToFlaxContentPath_should_normalize(string input, string expected)
    {
        JsonHelper.ToFlaxContentPath(input).Should().Be(expected);
    }

    // ============ AnimSegment 默认值 ============

    [Fact]
    public void AnimSegment_End_defaults_to_negative_one()
    {
        // 验证默认值未初始化时为 -1（Writer 应在序列化前替换为动画总时长）
        var seg = new AnimSegment();
        seg.End.Should().Be(-1f);
    }

    // ============ MappingRules null/empty 容错 ============

    [Fact]
    public void MappingRules_should_resolve_unknown_to_default_without_throwing()
    {
        var rules = MappingRules.Load();
        rules.ResolveType(null).Should().Be("object");
        rules.ResolveType("").Should().Be("object");
        rules.ResolveType("UnknownType").Should().Be("object");
        rules.ResolveBlendMode(null).Should().Be("Opaque");
        rules.ResolveBlendMode("").Should().Be("Opaque");
        rules.ResolveShadingModel(null).Should().Be("DefaultLit");
    }

    [Fact]
    public void MappingRules_TryGetProfile_should_return_null_for_missing_profile()
    {
        var rules = MappingRules.Load();
        rules.TryGetProfile("non_existent").Should().BeNull();
        rules.TryGetProfile("preview").Should().NotBeNull();
    }

    [Fact]
    public void MappingRules_GetProfile_throws_KeyNotFound_for_missing_profile()
    {
        var rules = MappingRules.Load();
        var act = () => rules.GetProfile("non_existent");
        act.Should().Throw<KeyNotFoundException>();
    }

    // ============ Writer 异步 API ============

    [Fact]
    public async Task ModelWriter_should_reject_empty_output_root()
    {
        var act = () => new ModelWriter("");
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public async Task ModelWriter_should_reject_null_mesh()
    {
        var tempDir = GetTempDir();
        try
        {
            var writer = new ModelWriter(tempDir);
            var act = async () => await writer.WriteStaticMeshAsync(null!);
            await act.Should().ThrowAsync<ArgumentNullException>();
        }
        finally
        {
            CleanupDir(tempDir);
        }
    }

    [Fact]
    public async Task ModelWriter_should_reject_mesh_without_name()
    {
        var tempDir = GetTempDir();
        try
        {
            var writer = new ModelWriter(tempDir);
            var mesh = new IntermediateMesh { AssetName = "" };
            var act = async () => await writer.WriteStaticMeshAsync(mesh);
            await act.Should().ThrowAsync<ArgumentException>();
        }
        finally
        {
            CleanupDir(tempDir);
        }
    }

    [Fact]
    public async Task ModelWriter_should_produce_expected_files_for_static_mesh()
    {
        var tempDir = GetTempDir();
        try
        {
            var writer = new ModelWriter(tempDir);
            var mesh = new IntermediateMesh
            {
                AssetName = "TestMesh",
                Kind = MeshKind.Static,
                LODs = { new MeshLOD { LODIndex = 0, ScreenSize = 1.0f } },
                Materials = { new MeshMaterial { SlotIndex = 0, MaterialName = "M_Test" } }
            };

            var output = await writer.WriteStaticMeshAsync(mesh);

            output.TargetDirectory.Should().NotBeEmpty();
            output.Files.Should().Contain(f => f.Kind == "Model");
            output.Files.Should().Contain(f => f.Kind == "Prefab");
            output.Files.Should().Contain(f => f.Kind == "ImportScript");
            output.PendingManualSteps.Should().NotBeEmpty();

            File.Exists(Path.Combine(output.TargetDirectory, "TestMesh.prefab.json")).Should().BeTrue();
            File.Exists(Path.Combine(output.TargetDirectory, "import-manifest.json")).Should().BeTrue();
            File.Exists(Path.Combine(output.TargetDirectory, "materials-map.json")).Should().BeTrue();
        }
        finally
        {
            CleanupDir(tempDir);
        }
    }

    [Fact]
    public async Task AnimationWriter_should_normalize_montage_segment_negative_end_to_duration()
    {
        var tempDir = GetTempDir();
        try
        {
            var writer = new AnimationWriter(tempDir);
            var anim = new IntermediateAnimation
            {
                AssetName = "TestMontage",
                Kind = AnimationKind.Montage,
                DurationSeconds = 1.5f,
                MontageSegments = { new AnimSegment { SectionName = "Default", Start = 0, End = -1f } }
            };

            var output = await writer.WriteAsync(anim);
            var sectionsJson = await File.ReadAllTextAsync(
                Path.Combine(output.TargetDirectory, "montage-sections.json"));

            // -1 应被替换为 1.5（动画总时长）。JSON 序列化有 "end": 1.5 形式（带空格）
            sectionsJson.Should().Contain("1.5");
            sectionsJson.Should().Contain("\"end\"");
        }
        finally
        {
            CleanupDir(tempDir);
        }
    }

    [Fact]
    public async Task GasWriter_should_escape_special_characters_in_tag_strings()
    {
        var tempDir = GetTempDir();
        try
        {
            var writer = new GasWriter(tempDir);
            var ability = new GameplayAbility
            {
                ClassName = "TestAbility",
                AbilityTags = { "Ability.\"Quote\"\\Backslash" }
            };

            var output = await writer.WriteAbilityAsync(ability);
            var csContent = await File.ReadAllTextAsync(
                Path.Combine(output.TargetDirectory, "TestAbility.cs"));

            // 应输出转义后的字符串字面量，确保 C# 代码可编译
            csContent.Should().Contain("new GameplayTag(\"Ability.\\\"Quote\\\"\\\\Backslash\")");
        }
        finally
        {
            CleanupDir(tempDir);
        }
    }

    [Fact]
    public async Task ParticleWriter_should_emit_emitter_json_per_emitter()
    {
        var tempDir = GetTempDir();
        try
        {
            var writer = new ParticleWriter(tempDir);
            var ps = new IntermediateParticleSystem
            {
                AssetName = "TestPS",
                Kind = ParticleSystemKind.Niagara,
                Emitters =
                {
                    new ParticleEmitter { Name = "EmA", Capacity = 100 },
                    new ParticleEmitter { Name = "EmB", Capacity = 200 }
                }
            };

            var output = await writer.WriteAsync(ps);

            File.Exists(Path.Combine(output.TargetDirectory, "Emitter_EmA.json")).Should().BeTrue();
            File.Exists(Path.Combine(output.TargetDirectory, "Emitter_EmB.json")).Should().BeTrue();
            File.Exists(Path.Combine(output.TargetDirectory, "TestPS.ParticleSystem.json")).Should().BeTrue();
            File.Exists(Path.Combine(output.TargetDirectory, "TestPS.prefab.json")).Should().BeTrue();
        }
        finally
        {
            CleanupDir(tempDir);
        }
    }

    [Fact]
    public async Task ModelWriter_should_sanitize_path_unsafe_asset_names()
    {
        var tempDir = GetTempDir();
        try
        {
            var writer = new ModelWriter(tempDir);
            // 资源名含路径分隔符（不应进入子目录）
            var mesh = new IntermediateMesh
            {
                AssetName = "..\\..\\Evil",
                Kind = MeshKind.Static
            };

            var output = await writer.WriteStaticMeshAsync(mesh);

            // 输出目录不应跳出 tempDir
            var fullOutput = Path.GetFullPath(output.TargetDirectory);
            var fullTemp = Path.GetFullPath(tempDir);
            fullOutput.StartsWith(fullTemp, StringComparison.OrdinalIgnoreCase).Should().BeTrue();
        }
        finally
        {
            CleanupDir(tempDir);
        }
    }

    // ============ ConversionContext ============

    [Fact]
    public void ConversionContext_CreateDefault_should_load_default_rules()
    {
        // 验证 CreateDefault 工厂方法能正确加载默认映射规则
        var ctx = ConversionContext.CreateDefault("C:/UE5/Content", "./output");
        ctx.Rules.Should().NotBeNull();
        ctx.Rules.ResolveType("UStaticMesh").Should().Be("FlaxEngine.Model");
    }

    // ============ 端到端验证：FlaxAssetFile 结构一致性 ============

    [Fact]
    public async Task ModelWriter_prefab_json_should_use_FlaxAssetFile_structure()
    {
        var tempDir = GetTempDir();
        try
        {
            var writer = new ModelWriter(tempDir);
            var mesh = new IntermediateMesh
            {
                AssetName = "HeroMesh",
                Kind = MeshKind.Static,
                LODs = { new MeshLOD { LODIndex = 0, ScreenSize = 1.0f } },
                Materials = { new MeshMaterial { SlotIndex = 0, MaterialName = "M_Hero" } }
            };

            var output = await writer.WriteStaticMeshAsync(mesh);
            var prefabPath = Path.Combine(output.TargetDirectory, "HeroMesh.prefab.json");
            File.Exists(prefabPath).Should().BeTrue();

            var json = await File.ReadAllTextAsync(prefabPath);
            // 必须包含 FlaxAssetFile 的 4 个关键字段
            json.Should().Contain("\"ID\"");
            json.Should().Contain("\"TypeName\"");
            json.Should().Contain("\"EngineBuild\"");
            json.Should().Contain("\"Data\"");
            // TypeName 必须为 FlaxEngine.Prefab（不是旧的自定义格式 typeName/name/actors）
            json.Should().Contain("\"FlaxEngine.Prefab\"");
            // 不应包含旧格式的字段名
            json.Should().NotContain("\"typeName\"");
            json.Should().NotContain("\"actors\"");
        }
        finally
        {
            CleanupDir(tempDir);
        }
    }

    [Fact]
    public async Task SkinnedModelWriter_should_emit_skeleton_hierarchy_with_bones()
    {
        var tempDir = GetTempDir();
        try
        {
            var writer = new ModelWriter(tempDir);
            var mesh = new IntermediateMesh
            {
                AssetName = "HeroSkeleton",
                Kind = MeshKind.Skeletal,
                Bones =
                {
                    new MeshBone { Name = "root", ParentIndex = -1 },
                    new MeshBone { Name = "spine_01", ParentIndex = 0 },
                    new MeshBone { Name = "head", ParentIndex = 1 }
                }
            };

            var output = await writer.WriteSkeletalMeshAsync(mesh);
            var skeletonPath = Path.Combine(output.TargetDirectory, "skeleton-hierarchy.json");
            File.Exists(skeletonPath).Should().BeTrue();

            var json = await File.ReadAllTextAsync(skeletonPath);
            // 骨骼数必须大于 0（原 bug：boneCount=0）
            json.Should().Contain("\"boneCount\": 3");
            json.Should().Contain("\"root\"");
            json.Should().Contain("\"spine_01\"");
            json.Should().Contain("\"head\"");
        }
        finally
        {
            CleanupDir(tempDir);
        }
    }

    [Fact]
    public async Task SkinnedModelWriter_should_extract_correct_translation_from_bindpose_matrix()
    {
        // 验证从 Matrix4x4 BindPose 提取平移分量并应用 UE5→Flax 坐标系映射
        // 原 bug：错误提取 M11/M12/M13/M14（旋转矩阵第一行），且未做坐标系映射
        var tempDir = GetTempDir();
        try
        {
            var writer = new ModelWriter(tempDir);
            // UE5 坐标系：(X 右, Y 前, Z 上)
            // 测试骨骼在 UE5 中位置：(0, 0, 0) / (0, 10, 0) / (0, 20, 50)
            var mesh = new IntermediateMesh
            {
                AssetName = "BindPoseTest",
                Kind = MeshKind.Skeletal,
                Bones =
                {
                    new MeshBone { Name = "root", ParentIndex = -1, BindPose = System.Numerics.Matrix4x4.CreateTranslation(0, 0, 0) },
                    new MeshBone { Name = "spine_01", ParentIndex = 0, BindPose = System.Numerics.Matrix4x4.CreateTranslation(0, 10, 0) },
                    new MeshBone { Name = "head", ParentIndex = 1, BindPose = System.Numerics.Matrix4x4.CreateTranslation(0, 20, 50) }
                }
            };

            var output = await writer.WriteSkeletalMeshAsync(mesh);
            var skeletonPath = Path.Combine(output.TargetDirectory, "skeleton-hierarchy.json");
            File.Exists(skeletonPath).Should().BeTrue();

            var json = await File.ReadAllTextAsync(skeletonPath);
            var hierarchy = System.Text.Json.JsonSerializer.Deserialize<System.Text.Json.JsonElement>(json);
            var bones = hierarchy.GetProperty("bones");

            // Flax 坐标系：(X 右, Y 上, Z 前) —— UE5 的 Z 映射为 Flax Y，UE5 的 Y 映射为 Flax Z
            // root: UE5 (0,0,0) → Flax (0,0,0)
            var rootT = bones[0].GetProperty("translation");
            rootT[0].GetSingle().Should().Be(0f);
            rootT[1].GetSingle().Should().Be(0f);
            rootT[2].GetSingle().Should().Be(0f);

            // spine_01: UE5 (0,10,0) → Flax (X=0, Y=Z=0, Z=Y=10) = (0,0,10)
            var spineT = bones[1].GetProperty("translation");
            spineT[0].GetSingle().Should().Be(0f);
            spineT[1].GetSingle().Should().Be(0f);  // UE5 Z=0 → Flax Y=0
            spineT[2].GetSingle().Should().Be(10f); // UE5 Y=10 → Flax Z=10

            // head: UE5 (0,20,50) → Flax (0,50,20) —— UE5 Z=50 → Flax Y=50, UE5 Y=20 → Flax Z=20
            var headT = bones[2].GetProperty("translation");
            headT[0].GetSingle().Should().Be(0f);
            headT[1].GetSingle().Should().Be(50f); // UE5 Z=50 → Flax Y=50
            headT[2].GetSingle().Should().Be(20f); // UE5 Y=20 → Flax Z=20

            // translation 必须是 3 元素数组（原 bug：4 元素 M11/M12/M13/M14）
            rootT.GetArrayLength().Should().Be(3, "translation 必须是 3 元素 [x,y,z]（原 bug 输出 4 元素矩阵行）");
        }
        finally
        {
            CleanupDir(tempDir);
        }
    }

    [Fact]
    public async Task ParticleWriter_prefab_should_reference_system_guid_consistently()
    {
        // 端到端：ParticleEffect.System 的 GUID 必须与 ParticleSystem.json 的 ID 一致
        var tempDir = GetTempDir();
        try
        {
            var writer = new ParticleWriter(tempDir);
            var ps = new IntermediateParticleSystem
            {
                AssetName = "FlamePS",
                Kind = ParticleSystemKind.Niagara,
                Emitters =
                {
                    new ParticleEmitter { Name = "FlameEmitter", Capacity = 500 }
                }
            };

            var output = await writer.WriteAsync(ps);

            // 1. 读取 ParticleSystem.json 的 ID
            var systemJson = await File.ReadAllTextAsync(
                Path.Combine(output.TargetDirectory, "FlamePS.ParticleSystem.json"));
            var systemAsset = System.Text.Json.JsonSerializer.Deserialize<System.Text.Json.JsonElement>(systemJson);
            var systemGuid = systemAsset.GetProperty("ID").GetString();
            systemGuid.Should().NotBeNullOrEmpty();
            systemGuid!.Length.Should().Be(32, "GUID 必须是 32 位十六进制字符串（无连字符）");
            // 必须全部为小写十六进制
            systemGuid.Should().MatchRegex("^[0-9a-f]{32}$", "GUID 必须为小写十六进制");

            // 2. 读取 Prefab.json，找到 ParticleEffect 节点的 System 字段
            var prefabJson = await File.ReadAllTextAsync(
                Path.Combine(output.TargetDirectory, "FlamePS.prefab.json"));
            var prefabAsset = System.Text.Json.JsonSerializer.Deserialize<System.Text.Json.JsonElement>(prefabJson);
            prefabAsset.GetProperty("TypeName").GetString().Should().Be("FlaxEngine.Prefab");

            // Data 是节点数组，找到 ParticleEffect 节点
            // 注意：FlaxNode.ExtraFields 标记为 [JsonExtensionData]，
            // 所以 ParticleSystem/IsPlaying 等字段直接平铺在节点对象顶层，而非嵌套在 ExtraFields 子对象中。
            var data = prefabAsset.GetProperty("Data");
            data.ValueKind.Should().Be(System.Text.Json.JsonValueKind.Array);
            var found = false;
            foreach (var node in data.EnumerateArray())
            {
                var typeName = node.GetProperty("TypeName").GetString();
                if (typeName == "FlaxEngine.ParticleEffect")
                {
                    // ParticleSystem 字段直接在节点上（通过 JsonExtensionData 平铺）
                    var systemRef = node.GetProperty("ParticleSystem").GetString();
                    systemRef.Should().Be(systemGuid, "Prefab 中 ParticleEffect.System 的 GUID 必须与 ParticleSystem.json 的 ID 一致");
                    found = true;
                    break;
                }
            }
            found.Should().BeTrue("Prefab 中必须存在 ParticleEffect 节点");
        }
        finally
        {
            CleanupDir(tempDir);
        }
    }

    [Fact]
    public async Task AnimationWriter_blend_space_samples_should_be_emitted_for_blendspace()
    {
        var tempDir = GetTempDir();
        try
        {
            var writer = new AnimationWriter(tempDir);
            var anim = new IntermediateAnimation
            {
                AssetName = "BS_WalkJog",
                Kind = AnimationKind.BlendSpace,
                DurationSeconds = 2.0f,
                FrameRate = 30f,
                BlendSamples =
                {
                    new BlendSpaceSample { AnimName = "Walk", Position = new System.Numerics.Vector2(0, 0) },
                    new BlendSpaceSample { AnimName = "Jog", Position = new System.Numerics.Vector2(1, 0) }
                }
            };

            var output = await writer.WriteAsync(anim);
            var samplesPath = Path.Combine(output.TargetDirectory, "blend-space-samples.json");
            File.Exists(samplesPath).Should().BeTrue();

            var json = await File.ReadAllTextAsync(samplesPath);
            json.Should().Contain("\"Walk\"");
            json.Should().Contain("\"Jog\"");
            json.Should().Contain("\"samples\"");
        }
        finally
        {
            CleanupDir(tempDir);
        }
    }

    [Fact]
    public async Task GasWriter_ability_should_emit_cs_file_with_proper_namespace()
    {
        var tempDir = GetTempDir();
        try
        {
            var writer = new GasWriter(tempDir);
            var ability = new GameplayAbility
            {
                ClassName = "GA_TestAbility",
                AbilityTags = { "Ability.Test" },
                InstancingPolicy = "InstancedPerActor",
                NetExecutionPolicy = "LocalPredicted"
            };

            var output = await writer.WriteAbilityAsync(ability);
            var csPath = Path.Combine(output.TargetDirectory, "GA_TestAbility.cs");
            File.Exists(csPath).Should().BeTrue();

            var cs = await File.ReadAllTextAsync(csPath);
            // 必须是有效的 C# 代码：包含 namespace、class、using
            cs.Should().Contain("namespace");
            cs.Should().Contain("class");
            cs.Should().Contain("using");
            // 类名应被规范化（去掉 GA_ 前缀）
            cs.Should().Contain("TestAbility");
        }
        finally
        {
            CleanupDir(tempDir);
        }
    }

    [Fact]
    public async Task ModelWriter_guid_should_be_deterministic_by_path()
    {
        // 同一个 SourcePath 多次调用应得到相同 GUID（基于 SHA256 哈希）
        var tempDir1 = GetTempDir();
        var tempDir2 = GetTempDir();
        try
        {
            var mesh = new IntermediateMesh
            {
                AssetName = "DeterministicTest",
                SourcePath = "Game/Models/TestMesh.uasset",
                Kind = MeshKind.Static
            };

            var w1 = new ModelWriter(tempDir1);
            var w2 = new ModelWriter(tempDir2);
            await w1.WriteStaticMeshAsync(mesh);
            await w2.WriteStaticMeshAsync(mesh);

            var json1 = await File.ReadAllTextAsync(Path.Combine(tempDir1, "Models", "DeterministicTest", "DeterministicTest.prefab.json"));
            var json2 = await File.ReadAllTextAsync(Path.Combine(tempDir2, "Models", "DeterministicTest", "DeterministicTest.prefab.json"));

            var id1 = System.Text.Json.JsonSerializer.Deserialize<System.Text.Json.JsonElement>(json1).GetProperty("ID").GetString();
            var id2 = System.Text.Json.JsonSerializer.Deserialize<System.Text.Json.JsonElement>(json2).GetProperty("ID").GetString();

            id1.Should().Be(id2, "同一 SourcePath 的 GUID 必须可重现（基于 SHA256 哈希）");
            id1.Should().MatchRegex("^[0-9a-f]{32}$");
        }
        finally
        {
            CleanupDir(tempDir1);
            CleanupDir(tempDir2);
        }
    }

    // ============ 辅助方法 ============

    private static string GetTempDir()
    {
        var dir = Path.Combine(Path.GetTempPath(), "UE5ToFlaxConverterTests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return dir;
    }

    private static void CleanupDir(string path)
    {
        try { Directory.Delete(path, recursive: true); }
        catch { /* 测试清理失败不应让测试失败 */ }
    }
}