using System.Buffers.Binary;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using SilkCodec.NET.Managed;

namespace Weixin.Silk;

/// <summary>Encodes PCM with the complete managed Jitsi/Skype legacy SILK encoder.</summary>
internal static class ManagedSilkEncoder
{
    private const int MaxPacketBytes = 1250;
    private static ReadOnlySpan<byte> Header => "\x02#!SILK_V3"u8;

    internal static byte[] Encode(byte[] pcm, int rate, int maxOutputBytes, Action checkpoint)
    {
        ArgumentNullException.ThrowIfNull(pcm);
        ArgumentNullException.ThrowIfNull(checkpoint);
        if (rate is not (8000 or 12000 or 16000 or 24000 or 32000 or 44100 or 48000))
            throw new ArgumentOutOfRangeException(nameof(rate));
        if (pcm.Length == 0 || (pcm.Length & 1) != 0)
            throw new ArgumentException("PCM must contain complete signed 16-bit little-endian samples.", nameof(pcm));
        if (maxOutputBytes < Header.Length + 3)
            throw new ArgumentOutOfRangeException(nameof(maxOutputBytes));

        checkpoint();
        int frameSamples = rate / 50;
        int frameBytes = frameSamples * sizeof(short);
        int frames = 1 + (pcm.Length - 1) / frameBytes;
        var input = new short[frameSamples];
        var packet = new byte[MaxPacketBytes];
        var packetLength = new short[1];
        var state = new SKP_Silk_encoder_state_FLP();
        var control = new SKP_SILK_SDK_EncControlStruct
        {
            API_sampleRate = rate,
            maxInternalSampleRate = Math.Min(rate, 24000),
            packetSize = frameSamples,
            bitRate = 25000,
            packetLossPercentage = 0,
            complexity = 2,
            useInBandFEC = 0,
            useDTX = 0
        };
        // One bounded buffer: growth must not leave old compressed-audio arrays uncleared.
        var output = new byte[(int)Math.Min(maxOutputBytes, Header.Length + (long)frames * (sizeof(short) + MaxPacketBytes))];
        int written = 0;
        Span<byte> lengthPrefix = stackalloc byte[sizeof(short)];
        try
        {
            ThrowOnError(EncAPI.SKP_Silk_SDK_InitEncoder(state, new SKP_SILK_SDK_EncControlStruct()));
            Header.CopyTo(output); written = Header.Length;
            for (int frame = 0; frame < frames; frame++)
            {
                checkpoint();
                int offset = frame * frameBytes;
                int bytes = Math.Min(frameBytes, pcm.Length - offset);
                Array.Clear(input);
                for (int sample = 0; sample < bytes / sizeof(short); sample++)
                    input[sample] = BinaryPrimitives.ReadInt16LittleEndian(pcm.AsSpan(offset + sample * sizeof(short), sizeof(short)));

                packetLength[0] = MaxPacketBytes;
                ThrowOnError(EncAPI.SKP_Silk_SDK_Encode(state, control, input, 0, input.Length, packet, 0, packetLength));
                checkpoint();
                int count = packetLength[0];
                if (count is < 1 or > MaxPacketBytes)
                    throw new InvalidDataException("The managed SILK encoder returned an invalid packet length.");
                if ((long)written + sizeof(short) + count > output.Length)
                    throw new InvalidDataException("Encoded SILK exceeds the configured output limit.");
                BinaryPrimitives.WriteInt16LittleEndian(lengthPrefix, (short)count);
                lengthPrefix.CopyTo(output.AsSpan(written)); written += sizeof(short);
                packet.AsSpan(0, count).CopyTo(output.AsSpan(written)); written += count;
            }
            checkpoint();
            return output.AsSpan(0, written).ToArray();
        }
        finally
        {
            CryptographicOperations.ZeroMemory(MemoryMarshal.AsBytes(input.AsSpan()));
            CryptographicOperations.ZeroMemory(packet);
            Array.Clear(packetLength);
            CryptographicOperations.ZeroMemory(lengthPrefix);
            CryptographicOperations.ZeroMemory(output);
        }
    }

    private static void ThrowOnError(int result)
    {
        if (result != 0)
            throw new InvalidDataException($"The managed SILK encoder returned error {result}.");
    }
}
