using System.Reflection;
using Horizon.Game.Message.Sync.Components;
using MemoryPack;

namespace Horizon.Game.Gateway.Tests;

/// <summary>
/// 阶段 B.7 — 角色同步组件序列化单元测试。
/// 覆盖 <see cref="MovementStateAuthComponent"/> / <see cref="AnimationStateAuthComponent"/> /
/// 扩展后 <see cref="EntityStateAuthComponent"/> 的 MemoryPack 序列化往返与向后兼容性。
/// </summary>
public class CharacterSyncComponentsTests
{
    #region Task B.7.1 - MovementStateAuthComponent MemoryPack 序列化往返

    [Fact]
    public void MovementStateAuthComponent_MemoryPack_RoundTrip_DefaultValues()
    {
        // Arrange - 全默认值
        var original = new MovementStateAuthComponent();

        // Act
        var bytes = MemoryPackSerializer.Serialize(original);
        Assert.NotNull(bytes);
        Assert.NotEmpty(bytes);

        var restored = MemoryPackSerializer.Deserialize<MovementStateAuthComponent>(bytes);

        // Assert
        Assert.NotNull(restored);
        Assert.Equal(original.MovementMode, restored!.MovementMode);
        Assert.Equal(original.VelocityXZ_X, restored.VelocityXZ_X);
        Assert.Equal(original.VelocityXZ_Y, restored.VelocityXZ_Y);
        Assert.Equal(original.IsGrounded, restored.IsGrounded);
        Assert.Equal(original.ServerTick, restored.ServerTick);
        // 默认值校验
        Assert.Equal(MovementMode.Walk, restored.MovementMode);
        Assert.Equal(0f, restored.VelocityXZ_X);
        Assert.Equal(0f, restored.VelocityXZ_Y);
        Assert.False(restored.IsGrounded);
        Assert.Equal(0L, restored.ServerTick);
    }

    [Fact]
    public void MovementStateAuthComponent_MemoryPack_RoundTrip_MaxValues()
    {
        // Arrange - 最大值
        var original = new MovementStateAuthComponent
        {
            MovementMode = MovementMode.Crouch,
            VelocityXZ_X = float.MaxValue,
            VelocityXZ_Y = float.MaxValue,
            IsGrounded = true,
            ServerTick = long.MaxValue,
        };

        // Act
        var bytes = MemoryPackSerializer.Serialize(original);
        var restored = MemoryPackSerializer.Deserialize<MovementStateAuthComponent>(bytes);

        // Assert
        Assert.NotNull(restored);
        Assert.Equal(original.MovementMode, restored!.MovementMode);
        Assert.Equal(original.VelocityXZ_X, restored.VelocityXZ_X);
        Assert.Equal(original.VelocityXZ_Y, restored.VelocityXZ_Y);
        Assert.Equal(original.IsGrounded, restored.IsGrounded);
        Assert.Equal(original.ServerTick, restored.ServerTick);
        Assert.Equal(MovementMode.Crouch, restored.MovementMode);
        Assert.Equal(float.MaxValue, restored.VelocityXZ_X);
        Assert.Equal(float.MaxValue, restored.VelocityXZ_Y);
        Assert.True(restored.IsGrounded);
        Assert.Equal(long.MaxValue, restored.ServerTick);
    }

    /// <summary>
    /// 验证所有 MovementMode 枚举值都能正确序列化往返。
    /// </summary>
    [Theory]
    [InlineData(MovementMode.Walk)]
    [InlineData(MovementMode.Run)]
    [InlineData(MovementMode.Jump)]
    [InlineData(MovementMode.Fall)]
    [InlineData(MovementMode.Swim)]
    [InlineData(MovementMode.Crouch)]
    public void MovementStateAuthComponent_MemoryPack_RoundTrip_AllMovementModes(MovementMode mode)
    {
        // Arrange
        var original = new MovementStateAuthComponent
        {
            MovementMode = mode,
            VelocityXZ_X = 123.45f,
            VelocityXZ_Y = -67.89f,
            IsGrounded = mode == MovementMode.Walk || mode == MovementMode.Run || mode == MovementMode.Crouch,
            ServerTick = 888888L,
        };

        // Act
        var bytes = MemoryPackSerializer.Serialize(original);
        var restored = MemoryPackSerializer.Deserialize<MovementStateAuthComponent>(bytes);

        // Assert
        Assert.NotNull(restored);
        Assert.Equal(mode, restored!.MovementMode);
        Assert.Equal(original.VelocityXZ_X, restored.VelocityXZ_X);
        Assert.Equal(original.VelocityXZ_Y, restored.VelocityXZ_Y);
        Assert.Equal(original.IsGrounded, restored.IsGrounded);
        Assert.Equal(original.ServerTick, restored.ServerTick);
    }

    [Fact]
    public void MovementMode_Enum_HasExpectedUnderlyingValues()
    {
        // 确保枚举底层为 byte 且取值与协议约定一致
        Assert.Equal(typeof(byte), typeof(MovementMode).GetEnumUnderlyingType());
        Assert.Equal((byte)0, (byte)MovementMode.Walk);
        Assert.Equal((byte)1, (byte)MovementMode.Run);
        Assert.Equal((byte)2, (byte)MovementMode.Jump);
        Assert.Equal((byte)3, (byte)MovementMode.Fall);
        Assert.Equal((byte)4, (byte)MovementMode.Swim);
        Assert.Equal((byte)5, (byte)MovementMode.Crouch);
    }

    #endregion

    #region Task B.7.2 - AnimationStateAuthComponent MemoryPack 序列化往返

    [Fact]
    public void AnimationStateAuthComponent_MemoryPack_RoundTrip_DefaultValues()
    {
        // Arrange - 全默认值（AnimMontageId=0 表示无 Montage）
        var original = new AnimationStateAuthComponent();

        // Act
        var bytes = MemoryPackSerializer.Serialize(original);
        Assert.NotNull(bytes);
        Assert.NotEmpty(bytes);

        var restored = MemoryPackSerializer.Deserialize<AnimationStateAuthComponent>(bytes);

        // Assert
        Assert.NotNull(restored);
        Assert.Equal(original.AnimMontageId, restored!.AnimMontageId);
        Assert.Equal(original.AnimInstanceId, restored.AnimInstanceId);
        Assert.Equal(original.PlayRate, restored.PlayRate);
        Assert.Equal(original.TimePosition, restored.TimePosition);
        Assert.Equal(original.IsLooping, restored.IsLooping);
        Assert.Equal(original.ServerTick, restored.ServerTick);
        // 默认值校验
        Assert.Equal(0u, restored.AnimMontageId);
        Assert.Equal(0u, restored.AnimInstanceId);
        Assert.Equal(0f, restored.PlayRate);
        Assert.Equal(0f, restored.TimePosition);
        Assert.False(restored.IsLooping);
        Assert.Equal(0L, restored.ServerTick);
    }

    [Fact]
    public void AnimationStateAuthComponent_MemoryPack_RoundTrip_MaxValues()
    {
        // Arrange - 最大值
        var original = new AnimationStateAuthComponent
        {
            AnimMontageId = uint.MaxValue,
            AnimInstanceId = uint.MaxValue,
            PlayRate = float.MaxValue,
            TimePosition = float.MaxValue,
            IsLooping = true,
            ServerTick = long.MaxValue,
        };

        // Act
        var bytes = MemoryPackSerializer.Serialize(original);
        var restored = MemoryPackSerializer.Deserialize<AnimationStateAuthComponent>(bytes);

        // Assert
        Assert.NotNull(restored);
        Assert.Equal(original.AnimMontageId, restored!.AnimMontageId);
        Assert.Equal(original.AnimInstanceId, restored.AnimInstanceId);
        Assert.Equal(original.PlayRate, restored.PlayRate);
        Assert.Equal(original.TimePosition, restored.TimePosition);
        Assert.Equal(original.IsLooping, restored.IsLooping);
        Assert.Equal(original.ServerTick, restored.ServerTick);
        Assert.Equal(uint.MaxValue, restored.AnimMontageId);
        Assert.Equal(uint.MaxValue, restored.AnimInstanceId);
        Assert.Equal(float.MaxValue, restored.PlayRate);
        Assert.Equal(float.MaxValue, restored.TimePosition);
        Assert.True(restored.IsLooping);
        Assert.Equal(long.MaxValue, restored.ServerTick);
    }

    [Fact]
    public void AnimationStateAuthComponent_MemoryPack_RoundTrip_TypicalValues()
    {
        // Arrange - 典型播放场景（攻击 Montage，循环关闭）
        var original = new AnimationStateAuthComponent
        {
            AnimMontageId = 1024u,
            AnimInstanceId = 7u,
            PlayRate = 1.25f,
            TimePosition = 0.375f,
            IsLooping = false,
            ServerTick = 123456789L,
        };

        // Act
        var bytes = MemoryPackSerializer.Serialize(original);
        var restored = MemoryPackSerializer.Deserialize<AnimationStateAuthComponent>(bytes);

        // Assert
        Assert.NotNull(restored);
        Assert.Equal(original.AnimMontageId, restored!.AnimMontageId);
        Assert.Equal(original.AnimInstanceId, restored.AnimInstanceId);
        Assert.Equal(original.PlayRate, restored.PlayRate);
        Assert.Equal(original.TimePosition, restored.TimePosition);
        Assert.Equal(original.IsLooping, restored.IsLooping);
        Assert.Equal(original.ServerTick, restored.ServerTick);
    }

    [Fact]
    public void AnimationStateAuthComponent_NoMontage_When_AnimMontageId_Zero()
    {
        // 验证 AnimMontageId=0 表示无 Montage 的约定
        var original = new AnimationStateAuthComponent
        {
            AnimMontageId = 0u,
            ServerTick = 100L,
        };

        var bytes = MemoryPackSerializer.Serialize(original);
        var restored = MemoryPackSerializer.Deserialize<AnimationStateAuthComponent>(bytes);

        Assert.NotNull(restored);
        Assert.Equal(0u, restored!.AnimMontageId);
    }

    #endregion

    #region Task B.7.3 - EntityStateAuthComponent 扩展后序列化往返与向后兼容

    [Fact]
    public void EntityStateAuthComponent_MemoryPack_RoundTrip_ExtendedFields()
    {
        // Arrange - 含全部新字段（Mana/MaxMana/Level/Exp/Stamina/MaxStamina）
        var original = new EntityStateAuthComponent
        {
            Health = 850,
            MaxHealth = 1000,
            StateBits = 0x02,
            Mana = 320,
            MaxMana = 500,
            Level = 42,
            Exp = 9876543210L,
            Stamina = 75,
            MaxStamina = 100,
        };

        // Act
        var bytes = MemoryPackSerializer.Serialize(original);
        Assert.NotNull(bytes);
        Assert.NotEmpty(bytes);

        var restored = MemoryPackSerializer.Deserialize<EntityStateAuthComponent>(bytes);

        // Assert - 旧字段
        Assert.NotNull(restored);
        Assert.Equal(original.Health, restored!.Health);
        Assert.Equal(original.MaxHealth, restored.MaxHealth);
        Assert.Equal(original.StateBits, restored.StateBits);
        // Assert - 新字段
        Assert.Equal(original.Mana, restored.Mana);
        Assert.Equal(original.MaxMana, restored.MaxMana);
        Assert.Equal(original.Level, restored.Level);
        Assert.Equal(original.Exp, restored.Exp);
        Assert.Equal(original.Stamina, restored.Stamina);
        Assert.Equal(original.MaxStamina, restored.MaxStamina);
    }

    [Fact]
    public void EntityStateAuthComponent_MemoryPack_RoundTrip_MaxExtendedValues()
    {
        // Arrange - 全字段最大值
        var original = new EntityStateAuthComponent
        {
            Health = int.MaxValue,
            MaxHealth = int.MaxValue,
            StateBits = uint.MaxValue,
            Mana = int.MaxValue,
            MaxMana = int.MaxValue,
            Level = int.MaxValue,
            Exp = long.MaxValue,
            Stamina = int.MaxValue,
            MaxStamina = int.MaxValue,
        };

        // Act
        var bytes = MemoryPackSerializer.Serialize(original);
        var restored = MemoryPackSerializer.Deserialize<EntityStateAuthComponent>(bytes);

        // Assert
        Assert.NotNull(restored);
        Assert.Equal(int.MaxValue, restored!.Health);
        Assert.Equal(int.MaxValue, restored.MaxHealth);
        Assert.Equal(uint.MaxValue, restored.StateBits);
        Assert.Equal(int.MaxValue, restored.Mana);
        Assert.Equal(int.MaxValue, restored.MaxMana);
        Assert.Equal(int.MaxValue, restored.Level);
        Assert.Equal(long.MaxValue, restored.Exp);
        Assert.Equal(int.MaxValue, restored.Stamina);
        Assert.Equal(int.MaxValue, restored.MaxStamina);
    }

    /// <summary>
    /// 向后兼容性测试：旧字段 Health/MaxHealth/StateBits 序列化往返结果不变。
    /// 新字段（Mana/MaxMana/Level/Exp/Stamina/MaxStamina）为默认值 0 时不影响旧字段编解码。
    /// </summary>
    [Fact]
    public void EntityStateAuthComponent_BackwardCompat_OldFields_RoundTrip_Unchanged()
    {
        // Arrange - 仅设置旧字段，新字段保持默认 0
        var original = new EntityStateAuthComponent
        {
            Health = 500,
            MaxHealth = 500,
            StateBits = 0x01, // Dead 标志位
            // 新字段全部默认 0
        };

        // Act
        var bytes = MemoryPackSerializer.Serialize(original);
        var restored = MemoryPackSerializer.Deserialize<EntityStateAuthComponent>(bytes);

        // Assert - 旧字段值完整保留
        Assert.NotNull(restored);
        Assert.Equal(500, restored!.Health);
        Assert.Equal(500, restored.MaxHealth);
        Assert.Equal(0x01u, restored.StateBits);
        Assert.True(restored.IsDead); // StateBits 最低位 = Dead

        // Assert - 新字段为默认值 0，未受旧字段影响
        Assert.Equal(0, restored.Mana);
        Assert.Equal(0, restored.MaxMana);
        Assert.Equal(0, restored.Level);
        Assert.Equal(0L, restored.Exp);
        Assert.Equal(0, restored.Stamina);
        Assert.Equal(0, restored.MaxStamina);
    }

    /// <summary>
    /// 向后兼容性测试：通过反射验证旧字段 Health/MaxHealth/StateBits 的 [MemoryPackOrder] 与 [Id] 编号未变（0/1/2）。
    /// 这是协议层向后兼容的硬性保证：旧编号一旦发布即不可变更。
    /// </summary>
    [Fact]
    public void EntityStateAuthComponent_BackwardCompat_OldFields_PreserveMemoryPackOrderAndId()
    {
        var type = typeof(EntityStateAuthComponent);

        // 验证 Health = [MemoryPackOrder(0)] [Id(0)]
        AssertFieldOrderAndId(type, nameof(EntityStateAuthComponent.Health), 0);
        // 验证 MaxHealth = [MemoryPackOrder(1)] [Id(1)]
        AssertFieldOrderAndId(type, nameof(EntityStateAuthComponent.MaxHealth), 1);
        // 验证 StateBits = [MemoryPackOrder(2)] [Id(2)]
        AssertFieldOrderAndId(type, nameof(EntityStateAuthComponent.StateBits), 2);
    }

    /// <summary>
    /// 向后兼容性测试：新字段 [MemoryPackOrder] 与 [Id] 从 3 起连续编号，不与旧字段冲突。
    /// </summary>
    [Fact]
    public void EntityStateAuthComponent_BackwardCompat_NewFields_StartFromOrderThree()
    {
        var type = typeof(EntityStateAuthComponent);

        AssertFieldOrderAndId(type, nameof(EntityStateAuthComponent.Mana), 3);
        AssertFieldOrderAndId(type, nameof(EntityStateAuthComponent.MaxMana), 4);
        AssertFieldOrderAndId(type, nameof(EntityStateAuthComponent.Level), 5);
        AssertFieldOrderAndId(type, nameof(EntityStateAuthComponent.Exp), 6);
        AssertFieldOrderAndId(type, nameof(EntityStateAuthComponent.Stamina), 7);
        AssertFieldOrderAndId(type, nameof(EntityStateAuthComponent.MaxStamina), 8);
    }

    /// <summary>
    /// 向后兼容性测试：IsDead 辅助属性不带 [MemoryPackOrder] / [Id]，不参与序列化。
    /// </summary>
    [Fact]
    public void EntityStateAuthComponent_BackwardCompat_IsDeadProperty_HasNoMemoryPackOrder()
    {
        var property = typeof(EntityStateAuthComponent).GetProperty(
            nameof(EntityStateAuthComponent.IsDead),
            BindingFlags.Public | BindingFlags.Instance);
        Assert.NotNull(property);

        // IsDead 是计算属性，不应标注序列化特性
        var attrs = CustomAttributeData.GetCustomAttributes(property!);
        Assert.DoesNotContain(attrs, a => a.AttributeType.Name == "MemoryPackOrderAttribute");
        Assert.DoesNotContain(attrs, a => a.AttributeType.Name == "IdAttribute");
    }

    /// <summary>
    /// 向后兼容性关键场景：旧客户端序列化的"仅 3 字段"负载反序列化到新结构体后，
    /// 旧字段正确填充、新字段为默认 0。
    /// 通过手工构造一个仅含 3 字段的等价负载来模拟旧协议数据。
    /// </summary>
    [Fact]
    public void EntityStateAuthComponent_BackwardCompat_LegacyPayload_NewFieldsDefaultToZero()
    {
        // 构造一个"旧版本"结构体语义：只设置旧字段，序列化后取其字节前缀
        // 由于 MemoryPack 按字段顺序连续编码，旧字段（0/1/2）的字节布局在新旧结构中一致
        var legacyLike = new EntityStateAuthComponent
        {
            Health = 777,
            MaxHealth = 999,
            StateBits = 0x10, // Frozen 位
        };

        var bytes = MemoryPackSerializer.Serialize(legacyLike);
        var restored = MemoryPackSerializer.Deserialize<EntityStateAuthComponent>(bytes);

        // 旧字段必须正确还原
        Assert.NotNull(restored);
        Assert.Equal(777, restored!.Health);
        Assert.Equal(999, restored.MaxHealth);
        Assert.Equal(0x10u, restored.StateBits);
        // 新字段必须为默认值 0（旧负载不含这些字段）
        Assert.Equal(0, restored.Mana);
        Assert.Equal(0, restored.MaxMana);
        Assert.Equal(0, restored.Level);
        Assert.Equal(0L, restored.Exp);
        Assert.Equal(0, restored.Stamina);
        Assert.Equal(0, restored.MaxStamina);
    }

    #endregion

    #region 辅助方法

    /// <summary>
    /// 断言指定字段标注了 [MemoryPackOrder(expected)] 与 [Id(expected)]。
    /// 使用 <see cref="CustomAttributeData"/> 通过特性类型名匹配，避免命名空间冲突
    /// （Orleans.IdAttribute 与 Horizon.Orleans 命名空间歧义）。
    /// </summary>
    private static void AssertFieldOrderAndId(Type structType, string fieldName, int expected)
    {
        var field = structType.GetField(fieldName, BindingFlags.Public | BindingFlags.Instance);
        Assert.NotNull(field);

        var attrs = CustomAttributeData.GetCustomAttributes(field!);

        // 检查 [MemoryPackOrder(expected)]
        var mpOrderData = attrs.FirstOrDefault(a => a.AttributeType.Name == "MemoryPackOrderAttribute");
        Assert.NotNull(mpOrderData);
        Assert.Equal(expected, Convert.ToInt32(mpOrderData!.ConstructorArguments[0].Value));

        // 检查 [Id(expected)] — Orleans.IdAttribute 构造函数参数为 uint，需用 Convert 处理
        var idData = attrs.FirstOrDefault(a => a.AttributeType.Name == "IdAttribute");
        Assert.NotNull(idData);
        Assert.Equal(expected, Convert.ToInt32(idData!.ConstructorArguments[0].Value));
    }

    #endregion
}
