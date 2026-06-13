using System.Buffers;

using Horizon.IM.Message;
using Horizon.IM.Message.Enums;

using MemoryPack;

namespace Horizon.IM.Gateway.Tests;

public sealed class IMContactEntryMemoryPackCompatibilityTests
{
    [Fact]
    public void Deserialize_LegacySevenFieldPayload_PreservesOnlineStatus()
    {
        var payload = SerializeLegacySevenFieldPayload(
            userId: 1001,
            nickname: "LegacyUser",
            avatar: "avatar.png",
            remark: "Old friend",
            relation: IMContactRelation.Friend,
            onlineStatus: IMOnlineStatus.Busy,
            addTime: 1712345678901);

        var contact = MemoryPackSerializer.Deserialize<IMContactEntry>(payload);

        Assert.NotNull(contact);
        Assert.Equal((ulong)1001, contact!.UserId);
        Assert.Equal("LegacyUser", contact.Nickname);
        Assert.Equal("avatar.png", contact.Avatar);
        Assert.Equal("Old friend", contact.Remark);
        Assert.Equal(IMContactRelation.Friend, contact.Relation);
        Assert.Equal(IMOnlineStatus.Busy, contact.OnlineStatus);
        Assert.Equal(1712345678901, contact.AddTime);
    }

    [Fact]
    public void Deserialize_TransitionalSixFieldPayload_MapsSixthMemberToAddTime()
    {
        var payload = SerializeTransitionalSixFieldPayload(
            userId: 2002,
            nickname: "CurrentUser",
            avatar: "avatar2.png",
            remark: "Migrated friend",
            relation: IMContactRelation.Friend,
            addTime: 1712345678999);

        var contact = MemoryPackSerializer.Deserialize<IMContactEntry>(payload);

        Assert.NotNull(contact);
        Assert.Equal((ulong)2002, contact!.UserId);
        Assert.Equal("CurrentUser", contact.Nickname);
        Assert.Equal("avatar2.png", contact.Avatar);
        Assert.Equal("Migrated friend", contact.Remark);
        Assert.Equal(IMContactRelation.Friend, contact.Relation);
        Assert.Equal(IMOnlineStatus.Offline, contact.OnlineStatus);
        Assert.Equal(1712345678999, contact.AddTime);
    }

    private static byte[] SerializeLegacySevenFieldPayload(
        ulong userId,
        string nickname,
        string avatar,
        string remark,
        IMContactRelation relation,
        IMOnlineStatus onlineStatus,
        long addTime)
    {
        var buffer = new ArrayBufferWriter<byte>();
        using var state = MemoryPackWriterOptionalStatePool.Rent(MemoryPackSerializerOptions.Default);
        var writer = new MemoryPackWriter<ArrayBufferWriter<byte>>(ref buffer, state);

        writer.WriteObjectHeader(7);
        writer.WriteUnmanaged(userId);
        writer.WriteString(nickname);
        writer.WriteString(avatar);
        writer.WriteString(remark);
        writer.WriteUnmanaged(relation);
        writer.WriteUnmanaged(onlineStatus, addTime);
        writer.Flush();

        return buffer.WrittenSpan.ToArray();
    }

    private static byte[] SerializeTransitionalSixFieldPayload(
        ulong userId,
        string nickname,
        string avatar,
        string remark,
        IMContactRelation relation,
        long addTime)
    {
        var buffer = new ArrayBufferWriter<byte>();
        using var state = MemoryPackWriterOptionalStatePool.Rent(MemoryPackSerializerOptions.Default);
        var writer = new MemoryPackWriter<ArrayBufferWriter<byte>>(ref buffer, state);

        writer.WriteObjectHeader(6);
        writer.WriteUnmanaged(userId);
        writer.WriteString(nickname);
        writer.WriteString(avatar);
        writer.WriteString(remark);
        writer.WriteUnmanaged(relation);
        writer.WriteUnmanaged(addTime);
        writer.Flush();

        return buffer.WrittenSpan.ToArray();
    }
}