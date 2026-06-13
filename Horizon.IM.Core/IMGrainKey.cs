namespace Horizon.IM.Core;

public static class IMGrainKey
{
    public static Guid ToGuid(ulong value)
    {
        var bytes = new byte[16];
        BitConverter.GetBytes(value).CopyTo(bytes, 0);
        return new Guid(bytes);
    }

    public static ulong ToUInt64(Guid value)
    {
        return BitConverter.ToUInt64(value.ToByteArray(), 0);
    }

    public static ulong NewUInt64Id()
    {
        ulong value = 0;
        while (value == 0)
        {
            value = ToUInt64(Guid.NewGuid());
        }

        return value;
    }
}