using System.Runtime.InteropServices;
using SilkCodec.NET.Internal;

namespace SilkCodec.NET;

/// <summary>Decodes Silk v3 bitstreams to little-endian 16-bit PCM.</summary>
public sealed unsafe class SilkDecoder
{
    // Local integration: bounds and cancellation remain active during decoding.
    internal Action? Checkpoint { get; init; }
    internal int MaximumOutputBytes { get; init; } = int.MaxValue;
    readonly DecoderEngine _engine = new();
    public SilkDecoderOptions Options { get; }

    public SilkDecoder() : this(new SilkDecoderOptions()) { }
    public SilkDecoder(SilkDecoderOptions options)
    {
        Options = options;
        _engine.Reset();
    }

    public byte[] Decode(ReadOnlySpan<byte> silk)
    {
        var packets = SilkContainer.ReadPackets(silk);
        if (packets.Count == 0)
            throw new SilkCodecException("Silk v3 stream contains no frames.");

        _engine.Reset();
        int api = Options.SampleRate == 0 ? 24000 : Options.SampleRate;
        int frameSamples = api / 50;
        int cap = packets.Count * frameSamples;
        if (cap < frameSamples) cap = frameSamples;
        if ((long)cap * 2 > MaximumOutputBytes)
            throw new SilkCodecException("Decoded output exceeds the configured limit.");
        byte[] pcm = SilkUnsafe.Uninit<byte>(cap * 2);
        int written = 0;
        short* frame = stackalloc short[SilkLimits.MaxApiFsKhz * SilkLimits.FrameLengthMs * SilkLimits.MaxFramesPerPacket];
        int frameCap = SilkLimits.MaxApiFsKhz * SilkLimits.FrameLengthMs * SilkLimits.MaxFramesPerPacket;
        Span<short> frameSpan = new(frame, frameCap);

        try
        {
        for (int p = 0; p < packets.Count; p++)
        {
            Checkpoint?.Invoke();
            var packet = packets[p];
            var payload = silk.Slice(packet.Offset, packet.Length);
            int lost = 0;
            int frameCount = 0;
            do
            {
                Checkpoint?.Invoke();
                if (++frameCount > SilkLimits.MaxFramesPerPacket)
                    throw new SilkCodecException("Too many frames in a Silk packet.");
                int ret = _engine.DecodePayload(payload, lost, api, frameSpan, out int n);
                // Offline files must not silently turn a corrupt range stream into PLC.
                if (ret < 0 || _engine.RangeErrorCount != 0 || n < 0 || n > frameCap)
                    throw new SilkCodecException("Silk v3 payload could not be decoded.");
                if (n <= 0) continue;
                if ((long)(written + n) * 2 > MaximumOutputBytes)
                    throw new SilkCodecException("Decoded output exceeds the configured limit.");
                if (written + n > pcm.Length / 2)
                {
                    int next = (int)Math.Min((long)(written + n) * 4, MaximumOutputBytes);
                    var grown = SilkUnsafe.Uninit<byte>(next);
                    pcm.AsSpan(0, written * 2).CopyTo(grown);
                    System.Security.Cryptography.CryptographicOperations.ZeroMemory(pcm);
                    pcm = grown;
                }
                if (BitConverter.IsLittleEndian)
                    new ReadOnlySpan<short>(frame, n).CopyTo(MemoryMarshal.Cast<byte, short>(pcm.AsSpan()).Slice(written));
                else
                {
                    fixed (byte* d = pcm)
                    {
                        byte* o = d + written * 2;
                        for (int i = 0; i < n; i++)
                        {
                            short v = frame[i];
                            o[0] = (byte)v;
                            o[1] = (byte)(v >> 8);
                            o += 2;
                        }
                    }
                }
                written += n;
            } while (_engine.MoreInternalFrames != 0);
        }
        Checkpoint?.Invoke();
        if (written * 2 == pcm.Length)
        {
            var result = pcm;
            pcm = [];
            return result;
        }
        return pcm.AsSpan(0, written * 2).ToArray();
        }
        finally
        {
            System.Security.Cryptography.CryptographicOperations.ZeroMemory(pcm);
            frameSpan.Clear();
        }
    }

    public byte[] Decode(Stream silk)
    {
        using var ms = new MemoryStream();
        silk.CopyTo(ms);
        return Decode(ms.GetBuffer().AsSpan(0, (int)ms.Length));
    }

    public void Decode(string silkPath, string pcmPath)
        => File.WriteAllBytes(pcmPath, Decode(File.ReadAllBytes(silkPath)));

    public Task<byte[]> DecodeAsync(ReadOnlyMemory<byte> silk, CancellationToken cancellationToken = default)
        => Task.Run(() => Decode(silk.Span), cancellationToken);

    public Task DecodeAsync(string silkPath, string pcmPath, CancellationToken cancellationToken = default)
        => Task.Run(() => Decode(silkPath, pcmPath), cancellationToken);
}

