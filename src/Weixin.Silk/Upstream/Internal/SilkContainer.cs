using System.Buffers.Binary;

namespace SilkCodec.NET.Internal;

internal readonly record struct SilkPacket(int Offset, int Length);

internal static unsafe class SilkContainer
{
    public static ReadOnlySpan<byte> StandardHeader => "#!SILK_V3"u8;
    public const byte TencentMarker = 0x02;

    public static byte[] EncodePackets(IReadOnlyList<byte[]> packets, bool tencent)
    {
        int extra = tencent ? 1 : 0;
        int terminator = tencent ? 0 : 2;
        int size = extra + StandardHeader.Length + terminator;
        foreach (var p in packets) size += 2 + p.Length;
        byte[] output = SilkUnsafe.Uninit<byte>(size);
        fixed (byte* dst = output)
        {
            int o = 0;
            if (tencent) dst[o++] = TencentMarker;
            ReadOnlySpan<byte> header = StandardHeader;
            header.CopyTo(new Span<byte>(dst + o, header.Length));
            o += header.Length;
            foreach (var packet in packets)
            {
                BinaryPrimitives.WriteInt16LittleEndian(new Span<byte>(dst + o, 2), (short)packet.Length);
                o += 2;
                if (packet.Length > 0)
                {
                    fixed (byte* src = packet)
                        SilkUnsafe.Copy(dst + o, src, packet.Length);
                    o += packet.Length;
                }
            }
            if (!tencent)
                BinaryPrimitives.WriteInt16LittleEndian(new Span<byte>(dst + o, 2), -1);
        }
        return output;
    }

    public static bool TryReadHeader(ReadOnlySpan<byte> data, out int offset, out bool tencent)
    {
        offset = 0;
        tencent = false;
        if (data.IsEmpty) return false;
        if (data[0] == TencentMarker)
        {
            if (data.Length < 1 + StandardHeader.Length) return false;
            if (!data.Slice(1, StandardHeader.Length).SequenceEqual(StandardHeader)) return false;
            tencent = true;
            offset = 1 + StandardHeader.Length;
            return true;
        }

        if (data.Length >= StandardHeader.Length && data.Slice(0, StandardHeader.Length).SequenceEqual(StandardHeader))
        {
            offset = StandardHeader.Length;
            return true;
        }
        ReadOnlySpan<byte> alt = "!SILK_V3"u8;
        if (data.Length >= 1 + alt.Length && data[0] != TencentMarker && data.Slice(1, alt.Length).SequenceEqual(alt))
        {
            offset = 1 + alt.Length;
            return true;
        }
        return false;
    }

    public static List<SilkPacket> ReadPackets(ReadOnlySpan<byte> data)
    {
        if (!TryReadHeader(data, out int offset, out _))
            throw new SilkCodecException("Input is not a valid Silk v3 bitstream.");

        var packets = new List<SilkPacket>();
        while (offset < data.Length)
        {
            if (data.Length - offset < 2)
                throw new SilkCodecException("Truncated Silk packet length.");
            short nBytes = BinaryPrimitives.ReadInt16LittleEndian(data.Slice(offset));
            offset += 2;
            if (nBytes == -1 && offset == data.Length) break;
            if (nBytes <= 0 || nBytes > 1250 || offset + nBytes > data.Length)
                throw new SilkCodecException("Invalid or truncated Silk packet.");
            packets.Add(new SilkPacket(offset, nBytes));
            offset += nBytes;
        }
        return packets;
    }
}
