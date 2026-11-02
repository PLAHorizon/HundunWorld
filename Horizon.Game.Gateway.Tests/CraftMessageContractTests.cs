using Horizon.Game.Message;
using Horizon.Game.Message.Enums;
using Horizon.Game.Message.Network;
using MemoryPack;
using Xunit;

namespace Horizon.Game.Gateway.Tests;

/// <summary>
/// 制作请求追加字段（<c>EntityId</c> / <c>Action</c>）的契约断言。
/// <para>
/// 这两个字段决定"跟哪个 NPC 结算"和"这次是学配方还是做东西"；错位不会编译失败，
/// 只会让玩家点"学习"却触发制作。走 <see cref="MessageUnion"/> 的真实线上路径往返一次，
/// 才能证明新字段确实随报文到达。
/// </para>
/// </summary>
public class CraftMessageContractTests
{
    [Fact]
    public void CraftingRequest_RoundTripsNewFieldsAsUnion()
    {
        MessageUnion original = new CraftingRequest
        {
            CharacterId = 42,
            RecipeId = 900001,
            Count = 1,
            EntityId = 0x0000_0001_0000_0002,
            Action = 1,
        };

        var back = MemoryPackSerializer.Deserialize<MessageUnion>(MemoryPackSerializer.Serialize(original));

        var request = Assert.IsType<CraftingRequest>(back);
        Assert.Equal(42UL, request.CharacterId);
        Assert.Equal(900001, request.RecipeId);
        Assert.Equal(0x0000_0001_0000_0002UL, request.EntityId);
        Assert.Equal(1, request.Action);
        Assert.Equal(MessageType.Crafting, request.Type);
    }

    /// <summary>清单回执必须能把配方数组带过线（客户端画卡片只认这一份）。</summary>
    [Fact]
    public void CraftingResponse_RoundTripsRecipeList()
    {
        var original = new CraftingResponse
        {
            Success = true,
            Message = "可用配方 1 个",
            Recipes = new System.Collections.Generic.List<CraftingRecipe>
            {
                new CraftingRecipe
                {
                    RecipeId = 900001,
                    Name = "锻造·玄铁重铠",
                    RequiredGold = 300,
                    RequiredMaterials = { { 750001, 2 } },
                    OutputItems = { { 91002, 1 } },
                    SuccessRate = 1.0f,
                },
            },
        };

        var back = MemoryPackSerializer.Deserialize<MessageUnion>(MemoryPackSerializer.Serialize<MessageUnion>(original));
        var response = Assert.IsType<CraftingResponse>(back);

        Assert.True(response.Success);
        Assert.Single(response.Recipes);
        Assert.Equal(900001, response.Recipes[0].RecipeId);
        Assert.Equal("锻造·玄铁重铠", response.Recipes[0].Name);
        Assert.Equal(2, response.Recipes[0].RequiredMaterials[750001]);
        Assert.Equal(1, response.Recipes[0].OutputItems[91002]);
    }

    [Fact]
    public void CraftingRequest_DefaultsToCraftActionWithoutNpc()
    {
        var request = new CraftingRequest();

        // 旧调用方不带这两个字段时必须退化成"没有 NPC 语境的制作"，而不是"学配方"。
        Assert.Equal(0UL, request.EntityId);
        Assert.Equal(0, request.Action);
    }
}
