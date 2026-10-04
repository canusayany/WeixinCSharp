using System.Runtime.CompilerServices;
using SilkCodec.NET.Internal.Tables;

namespace SilkCodec.NET.Internal;

// Based on the vendored DrAbc SilkCodec.NET pulse codec. Local changes validate
// frame/table indices, bound escape shifts, and use exact shell/sign CDF sizes.
internal static unsafe class PulseCodec
{
    public static void Decode(RangeCoder rc, int sigType, int quantOffsetType, int frameLength, Span<int> pulses, out int rateLevelIndex)
    {
        ValidateFrame(sigType, quantOffsetType, frameLength, pulses.Length);
        rateLevelIndex = rc.Decode(SilkSpec.RateLevelsCDF[sigType], SilkSpec.RateLevelsCDFOffset);
        if ((uint)rateLevelIndex >= SilkLimits.NRateLevels - 1)
            throw new SilkCodecException("SILK pulse rate level is outside its codebook.");
        int iter = frameLength / SilkLimits.ShellCodecFrameLength;
        int* sumPulses = stackalloc int[SilkLimits.MaxNbShellBlocks];
        int* nLshifts = stackalloc int[SilkLimits.MaxNbShellBlocks];
        var cdf = SilkSpec.PulsesPerBlockCDF[rateLevelIndex];
        for (int i = 0; i < iter; i++)
        {
            nLshifts[i] = 0;
            sumPulses[i] = rc.Decode(cdf, SilkSpec.PulsesPerBlockCDFOffset);
            while (sumPulses[i] == SilkLimits.MaxPulses + 1)
            {
                if (nLshifts[i] >= 31)
                    throw new SilkCodecException("SILK pulse escape shifts exceed the integer domain.");
                nLshifts[i]++;
                sumPulses[i] = rc.Decode(SilkSpec.PulsesPerBlockCDF[SilkLimits.NRateLevels - 1], SilkSpec.PulsesPerBlockCDFOffset);
            }
            if ((uint)sumPulses[i] > SilkLimits.MaxPulses)
                throw new SilkCodecException("SILK pulse sum is outside its shell codebook.");
        }
        fixed (int* pPulses = pulses)
        {
            for (int i = 0; i < iter; i++)
            {
                int* dest = pPulses + i * SilkLimits.ShellCodecFrameLength;
                if (sumPulses[i] > 0)
                    DecodeShell(rc, dest, sumPulses[i]);
                else
                    SilkUnsafe.Clear(dest, SilkLimits.ShellCodecFrameLength);
            }
            for (int i = 0; i < iter; i++)
            {
                if (nLshifts[i] <= 0) continue;
                int* dest = pPulses + i * SilkLimits.ShellCodecFrameLength;
                int shifts = nLshifts[i];
                for (int k = 0; k < SilkLimits.ShellCodecFrameLength; k++)
                {
                    int absQ = dest[k];
                    for (int j = 0; j < shifts; j++)
                    {
                        if (absQ < 0 || absQ > int.MaxValue >> 1)
                            throw new SilkCodecException("SILK pulse magnitude exceeds the integer domain.");
                        absQ <<= 1;
                        absQ += rc.Decode(SilkSpec.LsbCDF, 1);
                    }
                    dest[k] = absQ;
                }
            }
            DecodeSigns(rc, pPulses, frameLength, sigType, quantOffsetType, rateLevelIndex);
        }
    }

    public static int Encode(RangeCoder rc, ReadOnlySpan<int> pulses, int frameLength, int sigType, int quantOffsetType)
    {
        ValidateFrame(sigType, quantOffsetType, frameLength, pulses.Length);
        int iter = frameLength / SilkLimits.ShellCodecFrameLength;
        int* absPulses = stackalloc int[SilkLimits.MaxFrameLength];
        int* sumPulses = stackalloc int[SilkLimits.MaxNbShellBlocks];
        int* nRshifts = stackalloc int[SilkLimits.MaxNbShellBlocks];
        int* comb = stackalloc int[8];
        fixed (int* maxTab = SilkSpec.MaxPulsesTable)
        fixed (int* pPulses = pulses)
        {
            for (int i = 0; i < frameLength; i++)
            {
                int v = pPulses[i];
                // The shell combines 16 nonnegative magnitudes in int32.
                // Generated SILK pulses are much smaller; reject impossible
                // external values before negation or the first sum can wrap.
                if (v < -(int.MaxValue / SilkLimits.ShellCodecFrameLength) ||
                    v > int.MaxValue / SilkLimits.ShellCodecFrameLength)
                    throw new SilkCodecException("SILK input pulse magnitude is too large.");
                absPulses[i] = v < 0 ? -v : v;
            }

            for (int i = 0; i < iter; i++)
            {
                int off = i * SilkLimits.ShellCodecFrameLength;
                nRshifts[i] = 0;
                while (true)
                {
                    int scaleDown = 0;
                    for (int k = 0; k < 8; k++)
                    {
                        int sum = absPulses[off + 2 * k] + absPulses[off + 2 * k + 1];
                        if (sum > maxTab[0]) scaleDown = 1;
                        comb[k] = sum;
                    }
                    for (int k = 0; k < 4; k++)
                    {
                        int sum = comb[2 * k] + comb[2 * k + 1];
                        if (sum > maxTab[1]) scaleDown = 1;
                        comb[k] = sum;
                    }
                    for (int k = 0; k < 2; k++)
                    {
                        int sum = comb[2 * k] + comb[2 * k + 1];
                        if (sum > maxTab[2]) scaleDown = 1;
                        comb[k] = sum;
                    }
                    int total = comb[0] + comb[1];
                    if (total > maxTab[3]) scaleDown = 1;

                    if (scaleDown != 0)
                    {
                        nRshifts[i]++;
                        for (int k = 0; k < SilkLimits.ShellCodecFrameLength; k++)
                            absPulses[off + k] >>= 1;
                    }
                    else
                    {
                        sumPulses[i] = total;
                        break;
                    }
                }
            }

            int rateLevel = 0;
            int minBits = int.MaxValue;
            for (int k = 0; k < SilkLimits.NRateLevels - 1; k++)
            {
                int bits = SilkSpec.RateLevelsBitsQ6[sigType][k];
                var nBits = SilkSpec.PulsesPerBlockBitsQ6[k];
                for (int i = 0; i < iter; i++)
                    bits += nRshifts[i] > 0 ? nBits[SilkLimits.MaxPulses + 1] : nBits[sumPulses[i]];
                if (bits < minBits)
                {
                    minBits = bits;
                    rateLevel = k;
                }
            }

            rc.Encode(rateLevel, SilkSpec.RateLevelsCDF[sigType]);
            var cdf = SilkSpec.PulsesPerBlockCDF[rateLevel];
            var extraCdf = SilkSpec.PulsesPerBlockCDF[SilkLimits.NRateLevels - 1];
            for (int i = 0; i < iter; i++)
            {
                if (nRshifts[i] == 0)
                    rc.Encode(sumPulses[i], cdf);
                else
                {
                    rc.Encode(SilkLimits.MaxPulses + 1, cdf);
                    for (int k = 0; k < nRshifts[i] - 1; k++)
                        rc.Encode(SilkLimits.MaxPulses + 1, extraCdf);
                    rc.Encode(sumPulses[i], extraCdf);
                }
            }

            for (int i = 0; i < iter; i++)
            {
                if (sumPulses[i] <= 0) continue;
                EncodeShell(rc, absPulses + i * SilkLimits.ShellCodecFrameLength);
            }

            for (int i = 0; i < iter; i++)
            {
                if (nRshifts[i] <= 0) continue;
                int off = i * SilkLimits.ShellCodecFrameLength;
                int nLs = nRshifts[i];
                for (int k = 0; k < SilkLimits.ShellCodecFrameLength; k++)
                {
                    int v = pPulses[off + k];
                    int absQ = v < 0 ? -v : v;
                    for (int j = nLs - 1; j >= 0; j--)
                        rc.Encode((absQ >> j) & 1, SilkSpec.LsbCDF);
                }
            }

            EncodeSigns(rc, pPulses, frameLength, sigType, quantOffsetType, rateLevel);
            return rateLevel;
        }
    }

    static void DecodeShell(RangeCoder rc, int* pulses0, int pulses4)
    {
        int* pulses3 = stackalloc int[2];
        int* pulses2 = stackalloc int[4];
        int* pulses1 = stackalloc int[8];
        Split(pulses3, pulses3 + 1, rc, pulses4, SilkSpec.ShellCodeTable3);
        Split(pulses2, pulses2 + 1, rc, pulses3[0], SilkSpec.ShellCodeTable2);
        Split(pulses1, pulses1 + 1, rc, pulses2[0], SilkSpec.ShellCodeTable1);
        Split(pulses0, pulses0 + 1, rc, pulses1[0], SilkSpec.ShellCodeTable0);
        Split(pulses0 + 2, pulses0 + 3, rc, pulses1[1], SilkSpec.ShellCodeTable0);
        Split(pulses1 + 2, pulses1 + 3, rc, pulses2[1], SilkSpec.ShellCodeTable1);
        Split(pulses0 + 4, pulses0 + 5, rc, pulses1[2], SilkSpec.ShellCodeTable0);
        Split(pulses0 + 6, pulses0 + 7, rc, pulses1[3], SilkSpec.ShellCodeTable0);
        Split(pulses2 + 2, pulses2 + 3, rc, pulses3[1], SilkSpec.ShellCodeTable2);
        Split(pulses1 + 4, pulses1 + 5, rc, pulses2[2], SilkSpec.ShellCodeTable1);
        Split(pulses0 + 8, pulses0 + 9, rc, pulses1[4], SilkSpec.ShellCodeTable0);
        Split(pulses0 + 10, pulses0 + 11, rc, pulses1[5], SilkSpec.ShellCodeTable0);
        Split(pulses1 + 6, pulses1 + 7, rc, pulses2[3], SilkSpec.ShellCodeTable1);
        Split(pulses0 + 12, pulses0 + 13, rc, pulses1[6], SilkSpec.ShellCodeTable0);
        Split(pulses0 + 14, pulses0 + 15, rc, pulses1[7], SilkSpec.ShellCodeTable0);
    }

    static void EncodeShell(RangeCoder rc, int* pulses0)
    {
        int* pulses1 = stackalloc int[8];
        int* pulses2 = stackalloc int[4];
        int* pulses3 = stackalloc int[2];
        int pulses4;
        Combine(pulses1, pulses0, 8);
        Combine(pulses2, pulses1, 4);
        Combine(pulses3, pulses2, 2);
        pulses4 = pulses3[0] + pulses3[1];
        EncodeSplit(rc, pulses3[0], pulses4, SilkSpec.ShellCodeTable3);
        EncodeSplit(rc, pulses2[0], pulses3[0], SilkSpec.ShellCodeTable2);
        EncodeSplit(rc, pulses1[0], pulses2[0], SilkSpec.ShellCodeTable1);
        EncodeSplit(rc, pulses0[0], pulses1[0], SilkSpec.ShellCodeTable0);
        EncodeSplit(rc, pulses0[2], pulses1[1], SilkSpec.ShellCodeTable0);
        EncodeSplit(rc, pulses1[2], pulses2[1], SilkSpec.ShellCodeTable1);
        EncodeSplit(rc, pulses0[4], pulses1[2], SilkSpec.ShellCodeTable0);
        EncodeSplit(rc, pulses0[6], pulses1[3], SilkSpec.ShellCodeTable0);
        EncodeSplit(rc, pulses2[2], pulses3[1], SilkSpec.ShellCodeTable2);
        EncodeSplit(rc, pulses1[4], pulses2[2], SilkSpec.ShellCodeTable1);
        EncodeSplit(rc, pulses0[8], pulses1[4], SilkSpec.ShellCodeTable0);
        EncodeSplit(rc, pulses0[10], pulses1[5], SilkSpec.ShellCodeTable0);
        EncodeSplit(rc, pulses1[6], pulses2[3], SilkSpec.ShellCodeTable1);
        EncodeSplit(rc, pulses0[12], pulses1[6], SilkSpec.ShellCodeTable0);
        EncodeSplit(rc, pulses0[14], pulses1[7], SilkSpec.ShellCodeTable0);
    }

    [MethodImpl(SilkUnsafe.Hot)]
    static void Combine(int* output, int* input, int len)
    {
        for (int k = 0; k < len; k++)
            output[k] = input[2 * k] + input[2 * k + 1];
    }

    static void Split(int* child1, int* child2, RangeCoder rc, int p, ushort[] table)
    {
        if (p < 0)
            throw new SilkCodecException("SILK shell pulse sum is negative.");
        if (p > 0)
        {
            int middle = p >> 1;
            ReadOnlySpan<ushort> cdf = ShellCdf(table, p);
            *child1 = rc.Decode(cdf, middle);
            if ((uint)*child1 > (uint)p)
                throw new SilkCodecException("SILK shell child is outside its pulse sum.");
            *child2 = p - *child1;
        }
        else
        {
            *child1 = 0;
            *child2 = 0;
        }
    }

    static void EncodeSplit(RangeCoder rc, int child1, int p, ushort[] table)
    {
        if (p < 0 || (uint)child1 > (uint)p)
            throw new SilkCodecException("SILK shell child or pulse sum is invalid.");
        if (p == 0) return;
        rc.Encode(child1, ShellCdf(table, p));
    }

    static void DecodeSigns(RangeCoder rc, int* q, int length, int sigType, int quantOffsetType, int rateLevel)
    {
        int i = SignCdfIndex(sigType, quantOffsetType, rateLevel);
        ushort* cdf = stackalloc ushort[3];
        cdf[0] = 0;
        cdf[1] = SilkSpec.SignCDF[i];
        cdf[2] = 65535;
        for (int n = 0; n < length; n++)
        {
            if (q[n] > 0)
            {
                int data = rc.Decode(cdf, 3, 1);
                q[n] *= (data << 1) - 1;
            }
        }
    }

    static void EncodeSigns(RangeCoder rc, int* q, int length, int sigType, int quantOffsetType, int rateLevel)
    {
        int i = SignCdfIndex(sigType, quantOffsetType, rateLevel);
        ushort* cdf = stackalloc ushort[3];
        cdf[0] = 0;
        cdf[1] = SilkSpec.SignCDF[i];
        cdf[2] = 65535;
        for (int n = 0; n < length; n++)
        {
            if (q[n] != 0)
                rc.Encode(q[n] > 0 ? 1 : 0, cdf, 3);
        }
    }

    static void ValidateFrame(int sigType, int quantOffsetType, int frameLength, int spanLength)
    {
        if ((uint)sigType > 1 || (uint)quantOffsetType > 1 ||
            frameLength < SilkLimits.ShellCodecFrameLength || frameLength > SilkLimits.MaxFrameLength ||
            frameLength % SilkLimits.ShellCodecFrameLength != 0 || spanLength < frameLength ||
            frameLength / SilkLimits.ShellCodecFrameLength > SilkLimits.MaxNbShellBlocks)
            throw new SilkCodecException("SILK pulse frame or signal indices are invalid.");
    }

    static ReadOnlySpan<ushort> ShellCdf(ushort[] table, int pulseSum)
    {
        if (pulseSum < 1 || (uint)pulseSum >= (uint)SilkSpec.ShellCodeTableOffsets.Length)
            throw new SilkCodecException("SILK shell pulse sum is outside its offset table.");
        int start = SilkSpec.ShellCodeTableOffsets[pulseSum];
        // A sum p encodes child values 0..p: p+1 symbols, p+2 CDF endpoints.
        int count = pulseSum + 2;
        if (start < 0 || count > table.Length || start > table.Length - count)
            throw new SilkCodecException("SILK shell CDF segment is outside its table.");
        return table.AsSpan(start, count);
    }

    static int SignCdfIndex(int sigType, int quantOffsetType, int rateLevel)
    {
        if ((uint)sigType > 1 || (uint)quantOffsetType > 1 ||
            (uint)rateLevel >= SilkLimits.NRateLevels - 1)
            throw new SilkCodecException("SILK sign codebook indices are invalid.");
        int index = Fx.SMulBb(SilkLimits.NRateLevels - 1, (sigType << 1) + quantOffsetType) + rateLevel;
        if ((uint)index >= (uint)SilkSpec.SignCDF.Length)
            throw new SilkCodecException("SILK sign codebook index is outside its table.");
        return index;
    }
}

