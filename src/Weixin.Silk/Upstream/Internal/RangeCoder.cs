using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace SilkCodec.NET.Internal;

// Based on the vendored DrAbc SilkCodec.NET range coder. Local changes:
// bounded CDF spans, checked carry propagation, explicit zero padding, and
// immediate codec exceptions for malformed state instead of unsafe recovery.
internal sealed unsafe class RangeCoder
{
    const int DecoderPadding = 4;
    public int BufferLength;
    public int BufferIx;
    public uint BaseQ32;
    public uint RangeQ16;
    public int Error;
    public readonly byte[] Buffer;
    readonly byte* _buf;
#if !NET5_0_OR_GREATER
    readonly GCHandle _pin;
#endif

    public RangeCoder()
    {
        Buffer = SilkUnsafe.Pinned<byte>(SilkLimits.MaxArithmBytes + DecoderPadding);
#if NET5_0_OR_GREATER
        _buf = SilkUnsafe.Ptr(Buffer);
#else
        _pin = GCHandle.Alloc(Buffer, GCHandleType.Pinned);
        _buf = (byte*)_pin.AddrOfPinnedObject();
#endif
    }

#if !NET5_0_OR_GREATER
    ~RangeCoder()
    {
        if (_pin.IsAllocated)
            _pin.Free();
    }
#endif

    public void InitEncoder()
    {
        BufferLength = SilkLimits.MaxArithmBytes;
        RangeQ16 = 0x0000FFFF;
        BufferIx = 0;
        BaseQ32 = 0;
        Error = 0;
        Buffer.AsSpan().Clear();
    }

    public void InitDecoder(ReadOnlySpan<byte> payload)
    {
        // The SILK arithmetic decoder can consume four implicit zero bytes
        // beyond the packet. Clear the whole backing store before every copy,
        // including when the preceding packet was longer than this one.
        Buffer.AsSpan().Clear();
        BufferLength = 0;
        BufferIx = 0;
        BaseQ32 = 0;
        RangeQ16 = 0x0000FFFF;
        Error = 0;
        if ((uint)payload.Length > SilkLimits.MaxArithmBytes)
            throw Fail(SilkLimits.RangePayloadTooLong, "SILK range payload exceeds its buffer.");
        if (payload.IsEmpty)
            throw Fail(SilkLimits.RangeDecoderCheckFailed, "SILK range payload is empty.");
        int n = payload.Length;
        if (n > 0)
        {
            fixed (byte* src = payload)
                SilkUnsafe.Copy(_buf, src, n);
        }
        BufferLength = n;
        BufferIx = 0;
        BaseQ32 = n >= 4
            ? ((uint)_buf[0] << 24) | ((uint)_buf[1] << 16) | ((uint)_buf[2] << 8) | _buf[3]
            : n switch
            {
                3 => ((uint)_buf[0] << 24) | ((uint)_buf[1] << 16) | ((uint)_buf[2] << 8),
                2 => ((uint)_buf[0] << 24) | ((uint)_buf[1] << 16),
                1 => (uint)_buf[0] << 24,
                _ => 0
            };
        RangeQ16 = 0x0000FFFF;
        Error = 0;
    }

    [MethodImpl(SilkUnsafe.Hot)]
    public void Encode(int data, ReadOnlySpan<ushort> cdf)
    {
        EnsureReady();
        ValidateCdf(cdf);
        if ((uint)data >= (uint)(cdf.Length - 1) || cdf[data] >= cdf[data + 1])
            throw Fail(SilkLimits.RangeCdfOutOfRange, "SILK range symbol is outside its CDF interval.");
        fixed (ushort* p = cdf)
            EncodeCore(data, p);
    }

    [MethodImpl(SilkUnsafe.Hot)]
    public void Encode(int data, ushort* cdf, int count)
    {
        if (cdf == null || count < 2 || count > 65536)
            throw Fail(SilkLimits.RangeCdfOutOfRange, "SILK CDF pointer or length is invalid.");
        Encode(data, new ReadOnlySpan<ushort>(cdf, count));
    }

    [MethodImpl(SilkUnsafe.Hot)]
    void EncodeCore(int data, ushort* cdf)
    {
        uint low = cdf[data];
        uint high = cdf[data + 1];
        uint baseTmp = BaseQ32;
        BaseQ32 += Fx.MulU(RangeQ16, low);
        uint rangeQ32 = Fx.MulU(RangeQ16, high - low);
        if (BaseQ32 < baseTmp)
            PropagateCarry();

        if ((rangeQ32 & 0xFF000000u) != 0)
        {
            RangeQ16 = rangeQ32 >> 16;
            return;
        }

        if ((rangeQ32 & 0xFFFF0000u) != 0)
            RangeQ16 = rangeQ32 >> 8;
        else
        {
            RangeQ16 = rangeQ32;
            if (BufferIx >= BufferLength)
            {
                throw Fail(SilkLimits.RangeWriteBeyondBuffer, "SILK range encoder buffer is full.");
            }
            _buf[BufferIx++] = (byte)(BaseQ32 >> 24);
            BaseQ32 <<= 8;
        }

        if (BufferIx >= BufferLength)
        {
            throw Fail(SilkLimits.RangeWriteBeyondBuffer, "SILK range encoder buffer is full.");
        }
        _buf[BufferIx++] = (byte)(BaseQ32 >> 24);
        BaseQ32 <<= 8;
    }

    [MethodImpl(SilkUnsafe.Hot)]
    public int Decode(ReadOnlySpan<ushort> cdf, int probIx)
    {
        EnsureReady();
        ValidateCdf(cdf);
        if ((uint)probIx >= (uint)cdf.Length)
            throw Fail(SilkLimits.RangeCdfOutOfRange, "SILK range probability index is outside its CDF.");
        fixed (ushort* p = cdf)
            return DecodeCore(p, cdf.Length, probIx);
    }

    [MethodImpl(SilkUnsafe.Hot)]
    public int Decode(ushort* cdf, int count, int probIx)
    {
        if (cdf == null || count < 2 || count > 65536)
            throw Fail(SilkLimits.RangeCdfOutOfRange, "SILK CDF pointer or length is invalid.");
        return Decode(new ReadOnlySpan<ushort>(cdf, count), probIx);
    }

    [MethodImpl(SilkUnsafe.Hot)]
    int DecodeCore(ushort* cdf, int count, int probIx)
    {
        uint high = cdf[probIx];
        uint baseTmp = Fx.MulU(RangeQ16, high);
        uint low;
        if (baseTmp > BaseQ32)
        {
            while (true)
            {
                if (probIx <= 0)
                    throw Fail(SilkLimits.RangeCdfOutOfRange, "SILK range CDF search reached its lower boundary.");
                low = cdf[--probIx];
                baseTmp = Fx.MulU(RangeQ16, low);
                if (baseTmp <= BaseQ32) break;
                high = low;
                if (high == 0)
                {
                    throw Fail(SilkLimits.RangeCdfOutOfRange, "SILK range CDF search found no interval.");
                }
            }
        }
        else
        {
            while (true)
            {
                if (probIx >= count - 1)
                    throw Fail(SilkLimits.RangeCdfOutOfRange, "SILK range CDF search reached its upper boundary.");
                low = high;
                high = cdf[++probIx];
                baseTmp = Fx.MulU(RangeQ16, high);
                if (baseTmp > BaseQ32)
                {
                    probIx--;
                    break;
                }
                if (high == 0xFFFF)
                {
                    throw Fail(SilkLimits.RangeCdfOutOfRange, "SILK range CDF search found no interval.");
                }
            }
        }

        int data = probIx;
        if (high <= low)
            throw Fail(SilkLimits.RangeZeroIntervalWidth, "SILK range CDF interval has zero width.");
        BaseQ32 -= Fx.MulU(RangeQ16, low);
        uint rangeQ32 = Fx.MulU(RangeQ16, high - low);
        if ((rangeQ32 & 0xFF000000u) != 0)
            RangeQ16 = rangeQ32 >> 16;
        else
        {
            if ((rangeQ32 & 0xFFFF0000u) != 0)
            {
                RangeQ16 = rangeQ32 >> 8;
                if ((BaseQ32 >> 24) != 0)
                {
                    throw Fail(SilkLimits.RangeNormalizationFailed, "SILK range normalization failed.");
                }
            }
            else
            {
                RangeQ16 = rangeQ32;
                if ((BaseQ32 >> 16) != 0)
                {
                    throw Fail(SilkLimits.RangeNormalizationFailed, "SILK range normalization failed.");
                }
                BaseQ32 <<= 8;
                if (BufferIx < BufferLength)
                    BaseQ32 |= _buf[4 + BufferIx++];
            }
            BaseQ32 <<= 8;
            if (BufferIx < BufferLength)
                BaseQ32 |= _buf[4 + BufferIx++];
        }

        if (RangeQ16 == 0)
        {
            throw Fail(SilkLimits.RangeZeroIntervalWidth, "SILK range interval has zero width.");
        }
        return data;
    }

    [MethodImpl(SilkUnsafe.Hot)]
    public int GetLength(out int nBytes)
    {
        EnsureReady();
        int nBits = (BufferIx << 3) + Fx.Clz32(unchecked((int)(RangeQ16 - 1))) - 14;
        nBytes = (nBits + 7) >> 3;
        if (nBytes < 1 || nBytes > BufferLength)
            throw Fail(SilkLimits.RangeReadBeyondBuffer, "SILK computed range length exceeds its buffer.");
        return nBits;
    }

    public void WrapUp()
    {
        uint baseQ24 = BaseQ32 >> 8;
        int bitsInStream = GetLength(out int nBytes);
        int bitsToStore = bitsInStream - (BufferIx << 3);
        if (bitsToStore is < 1 or > 24 || nBytes < 1 || nBytes > BufferLength)
            throw Fail(SilkLimits.RangeWriteBeyondBuffer, "SILK final range length is invalid.");
        baseQ24 += 0x00800000u >> (bitsToStore - 1);
        baseQ24 &= unchecked(uint.MaxValue << (24 - bitsToStore));
        if ((baseQ24 & 0x01000000u) != 0)
            PropagateCarry();
        if (BufferIx < BufferLength)
        {
            _buf[BufferIx++] = (byte)(baseQ24 >> 16);
            if (bitsToStore > 8 && BufferIx < BufferLength)
                _buf[BufferIx++] = (byte)(baseQ24 >> 8);
        }
        if ((bitsInStream & 7) != 0)
        {
            int mask = 0xFF >> (bitsInStream & 7);
            _buf[nBytes - 1] |= (byte)mask;
        }
    }

    public void CheckAfterDecoding()
    {
        int bitsInStream = GetLength(out int nBytes);
        if (nBytes < 1 || nBytes > BufferLength)
        {
            throw Fail(SilkLimits.RangeDecoderCheckFailed, "SILK range payload is truncated.");
        }
        if ((bitsInStream & 7) != 0)
        {
            int mask = 0xFF >> (bitsInStream & 7);
            if ((_buf[nBytes - 1] & mask) != mask)
                throw Fail(SilkLimits.RangeDecoderCheckFailed, "SILK range payload padding is invalid.");
        }
    }

    void ValidateCdf(ReadOnlySpan<ushort> cdf)
    {
        if (cdf.Length < 2 || cdf[0] != 0 || cdf[^1] != ushort.MaxValue)
            throw Fail(SilkLimits.RangeCdfOutOfRange, "SILK CDF endpoints are invalid.");
        // Equal neighboring entries denote impossible symbols in valid SILK
        // shell tables. Preserve them, while rejecting a descending CDF.
        for (int i = 1; i < cdf.Length; i++)
            if (cdf[i] < cdf[i - 1])
                throw Fail(SilkLimits.RangeCdfOutOfRange, "SILK CDF is not monotonic.");
    }

    void EnsureReady()
    {
        if (Error != 0)
            throw new SilkCodecException($"SILK range coder already failed ({Error}).");
        if (BufferLength < 1 || BufferLength > SilkLimits.MaxArithmBytes ||
            BufferIx < 0 || BufferIx > BufferLength)
            throw Fail(SilkLimits.RangeReadBeyondBuffer, "SILK range buffer state is invalid.");
        if (RangeQ16 == 0 || RangeQ16 > ushort.MaxValue)
            throw Fail(SilkLimits.RangeZeroIntervalWidth, "SILK range state is invalid.");
    }

    void PropagateCarry()
    {
        int index = BufferIx;
        while (index > 0)
        {
            index--;
            _buf[index] = unchecked((byte)(_buf[index] + 1));
            if (_buf[index] != 0)
                return;
        }
        throw Fail(SilkLimits.RangeWriteBeyondBuffer, "SILK range carry precedes the buffer.");
    }

    SilkCodecException Fail(int code, string message)
    {
        Error = code;
        return new SilkCodecException(message);
    }
}
