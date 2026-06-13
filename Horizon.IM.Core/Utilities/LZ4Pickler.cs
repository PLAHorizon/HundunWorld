using K4os.Compression.LZ4;

namespace Horizon.IM.Core.Utilities;

public static class LZ4Pickler
{
    public static byte[] Pickle(byte[] input)
    {
        if (input == null || input.Length == 0)
        {
            return Array.Empty<byte>();
        }

        try
        {
            var maxLength = LZ4Codec.MaximumOutputSize(input.Length);
            var output = new byte[maxLength + 4];
            BitConverter.GetBytes(input.Length).CopyTo(output, 0);

            var compressedLength = LZ4Codec.Encode(
                input,
                0,
                input.Length,
                output,
                4,
                output.Length - 4);

            if (compressedLength >= input.Length)
            {
                var rawResult = new byte[input.Length + 4];
                BitConverter.GetBytes(input.Length).CopyTo(rawResult, 0);
                input.CopyTo(rawResult, 4);
                return rawResult;
            }

            var final = new byte[compressedLength + 4];
            Array.Copy(output, 0, final, 0, final.Length);
            return final;
        }
        catch
        {
            return input;
        }
    }

    public static byte[]? Unpickle(ReadOnlySpan<byte> input)
    {
        if (input.Length < 4)
        {
            return null;
        }

        try
        {
            var raw = input.ToArray();
            var originalLength = BitConverter.ToInt32(raw, 0);
            if (originalLength <= 0)
            {
                return null;
            }

            var output = new byte[originalLength];
            var decompressedLength = LZ4Codec.Decode(raw, 4, raw.Length - 4, output, 0, output.Length);

            if (decompressedLength != originalLength)
            {
                if (raw.Length - 4 == originalLength)
                {
                    Array.Copy(raw, 4, output, 0, originalLength);
                    return output;
                }

                return null;
            }

            return output;
        }
        catch
        {
            return null;
        }
    }
}