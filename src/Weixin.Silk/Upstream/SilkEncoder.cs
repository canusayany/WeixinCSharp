using System.Runtime.InteropServices;
using SilkCodec.NET.Internal;
using SilkCodec.NET.Internal.Encoder;

namespace SilkCodec.NET;

/// <summary>Encodes little-endian 16-bit PCM to a Silk v3 bitstream.</summary>
public sealed unsafe class SilkEncoder(SilkEncoderOptions options)
{
    // Local integration: cooperate with cancellation and resource budgets per frame.
    internal Action? Checkpoint { get; init; }
    internal int MaximumOutputBytes { get; init; } = int.MaxValue;
    readonly EncoderEngine _engine = new();
    public SilkEncoderOptions Options { get; } = options;

    public SilkEncoder() : this(new SilkEncoderOptions()) { }

    public byte[] Encode(ReadOnlySpan<byte> pcmS16Le)
    {
        int nSamples = pcmS16Le.Length / 2;
        if (BitConverter.IsLittleEndian)
            return Encode(MemoryMarshal.Cast<byte, short>(pcmS16Le));

        short[] samples = nSamples <= 4096 ? new short[nSamples] : SilkUnsafe.Uninit<short>(nSamples);
        if (nSamples <= 4096)
        {
            Span<short> stack = stackalloc short[nSamples];
            CopySwap(pcmS16Le, stack);
            return Encode(stack);
        }
        CopySwap(pcmS16Le, samples);
        return Encode(samples);
    }

    static void CopySwap(ReadOnlySpan<byte> pcmS16Le, Span<short> samples)
    {
        fixed (byte* src = pcmS16Le)
        fixed (short* dst = samples)
        {
            int n = samples.Length;
            for (int i = 0; i < n; i++)
                dst[i] = (short)(src[i * 2] | (src[i * 2 + 1] << 8));
        }
    }

    public byte[] Encode(ReadOnlySpan<short> samples)
    {
        int api = Options.SampleRate;
        int frame = api / 50;
        if (frame <= 0)
            throw new SilkCodecException("Sample rate is out of range.");
        _engine.Reset(api, Options.MaxInternalSampleRate == 0 ? 24000 : Options.MaxInternalSampleRate, Options.PacketDurationMs);
        var packets = new List<byte[]>();
        int offset = 0;
        long outputBytes = 10;
        try
        {
        while (offset + frame <= samples.Length)
        {
            Checkpoint?.Invoke();
            var packet = _engine.Encode20Ms(samples.Slice(offset, frame), Options.BitRate);
            if (packet is { Length: > 0 })
            {
                outputBytes += packet.Length + 2;
                if (outputBytes > MaximumOutputBytes)
                {
                    System.Security.Cryptography.CryptographicOperations.ZeroMemory(packet);
                    throw new SilkCodecException("Encoded output exceeds the configured limit.");
                }
                packets.Add(packet);
            }
            offset += frame;
        }
        Checkpoint?.Invoke();
        return SilkContainer.EncodePackets(packets, Options.Tencent);
        }
        finally
        {
            foreach (var packet in packets)
                System.Security.Cryptography.CryptographicOperations.ZeroMemory(packet);
        }
    }

    public byte[] Encode(Stream pcm)
    {
        using var ms = new MemoryStream();
        pcm.CopyTo(ms);
        return Encode(ms.GetBuffer().AsSpan(0, (int)ms.Length));
    }

    public void Encode(string pcmPath, string silkPath)
        => File.WriteAllBytes(silkPath, Encode(File.ReadAllBytes(pcmPath)));

    public Task<byte[]> EncodeAsync(ReadOnlyMemory<byte> pcm, CancellationToken cancellationToken = default)
        => Task.Run(() => Encode(pcm.Span), cancellationToken);

    public Task EncodeAsync(string pcmPath, string silkPath, CancellationToken cancellationToken = default)
        => Task.Run(() => Encode(pcmPath, silkPath), cancellationToken);
}
