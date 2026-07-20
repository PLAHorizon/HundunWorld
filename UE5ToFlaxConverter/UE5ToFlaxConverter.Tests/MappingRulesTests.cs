using FluentAssertions;
using UE5ToFlaxConverter.Core.Mappers;
using UE5ToFlaxConverter.Core.Models;
using Xunit;

namespace UE5ToFlaxConverter.Tests;

public class MappingRulesTests
{
    [Fact]
    public void Load_should_resolve_known_types()
    {
        var rules = MappingRules.Load();
        rules.ResolveType("UStaticMesh").Should().Be("FlaxEngine.Model");
        rules.ResolveType("USkeletalMesh").Should().Be("FlaxEngine.SkinnedModel");
        rules.ResolveType("UGameplayAbility").Should().Be("NarrativeGameplayAbility");
        rules.ResolveType("UAttributeSet").Should().Be("NarrativeAttributeSetBase");
    }

    [Fact]
    public void Load_should_resolve_blend_modes()
    {
        var rules = MappingRules.Load();
        rules.ResolveBlendMode("BLEND_Translucent").Should().Be("Transparent");
        rules.ResolveBlendMode("BLEND_Opaque").Should().Be("Opaque");
        rules.ResolveBlendMode("UNKNOWN").Should().Be("Opaque");
    }

    [Fact]
    public void Load_should_resolve_particle_modules()
    {
        var rules = MappingRules.Load();
        rules.ResolveParticleModule("Engine.SpawnRate").Should().Be("SpawnRate");
        rules.ResolveParticleModule("UParticleModuleColor").Should().Be("UpdateColor");
    }

    [Fact]
    public void GetProfile_preview_and_apply_should_exist()
    {
        var rules = MappingRules.Load();
        rules.GetProfile("preview").Should().NotBeNull();
        rules.GetProfile("apply").Should().NotBeNull();
        rules.GetProfile("apply").BackupExisting.Should().BeTrue();
    }

    [Fact]
    public void TypeMap_should_resolve_ue_to_flax_types()
    {
        TypeMap.Resolve("UStaticMesh").Should().Be("FlaxEngine.Model");
        TypeMap.Resolve("USkeletalMeshComponent").Should().Be("FlaxEngine.AnimatedModel");
        TypeMap.Resolve("UBoxComponent").Should().Be("FlaxEngine.BoxCollider");
        TypeMap.Resolve("FGuid").Should().Be("System.Guid");
        TypeMap.Resolve("Unknown").Should().Be("object");
    }

    [Fact]
    public void TypeMap_UeToFVector_should_swap_y_and_z()
    {
        var v = new System.Numerics.Vector3(1, 2, 3);
        var result = TypeMap.UeToFVector(v);
        result.X.Should().Be(1);
        result.Y.Should().Be(3); // UE Z → Flax Y
        result.Z.Should().Be(2); // UE Y → Flax Z
    }
}

public class GameplayTagMapperTests
{
    private readonly GameplayTagMapper _mapper = new();

    [Fact]
    public void NormalizeClassName_should_strip_blueprint_suffix()
    {
        _mapper.NormalizeClassName("BP_HeroAbility_Fireball_C").Should().Be("HeroAbility_Fireball");
    }

    [Fact]
    public void NormalizeClassName_should_strip_prefix()
    {
        _mapper.NormalizeClassName("GA_Fireball").Should().Be("Fireball");
        _mapper.NormalizeClassName("GE_Damage").Should().Be("Damage");
        _mapper.NormalizeClassName("SM_Rock").Should().Be("Rock");
    }

    [Fact]
    public void NormalizeClassName_should_replace_invalid_chars()
    {
        _mapper.NormalizeClassName("Ability.Fire-Ball").Should().Be("Ability_Fire_Ball");
    }

    [Fact]
    public void Map_should_return_original_tag_by_default()
    {
        _mapper.Map("Ability.Cooldown.Fireball").Should().Be("Ability.Cooldown.Fireball");
    }

    [Fact]
    public void Map_should_apply_custom_prefix_rewrite()
    {
        var mapper = new GameplayTagMapper(new Dictionary<string, string>
        {
            ["UE5.Ability."] = "Flax.Ability."
        });
        mapper.Map("UE5.Ability.Fireball").Should().Be("Flax.Ability.Fireball");
    }

    [Theory]
    [InlineData("NarrativePro/Abilities/NarrativeCombatAbility", "NarrativeCombatAbility")]
    [InlineData("NarrativePro/Abilities/NarrativeInteractAbility", "NarrativeInteractAbility")]
    [InlineData("", "NarrativeGameplayAbility")]
    [InlineData(null, "NarrativeGameplayAbility")]
    public void ResolveAbilityBaseClass_should_return_correct_base(string? path, string expected)
    {
        _mapper.ResolveAbilityBaseClass(path).Should().Be(expected);
    }
}
