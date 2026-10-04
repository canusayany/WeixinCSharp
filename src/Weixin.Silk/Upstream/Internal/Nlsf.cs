using SilkCodec.NET.Internal.Tables;

namespace SilkCodec.NET.Internal;

// Based on the vendored DrAbc SilkCodec.NET NLSF implementation. Local changes
// validate codebook layouts and decoded indices before any vector access, and
// pass each stage's complete, bounded CDF to the range decoder.
internal sealed unsafe class NlsfCodebook
{
    public required int Order { get; init; }
    public required int[] VectorCounts { get; init; }
    public required int[] VectorsQ15 { get; init; }
    public required int[] NDeltaMinQ15 { get; init; }
    public required ushort[] Cdf { get; init; }
    public required int[] CdfStartOffsets { get; init; }
    public required int[] CdfMiddleIx { get; init; }
    public required int[] RatesQ5 { get; init; }
    public int Stages => VectorCounts.Length;

    public static readonly NlsfCodebook Voiced16 = new()
    {
        Order = 16,
        VectorCounts = [128, 16, 8, 8, 8, 8, 8, 8, 8, 16],
        VectorsQ15 = SilkSpec.NlsfMsvqCB016Q15,
        NDeltaMinQ15 = SilkSpec.NlsfMsvqCB016NdeltaMinQ15,
        Cdf = SilkSpec.NlsfMsvqCB016CDF,
        CdfStartOffsets = SilkSpec.NlsfMsvqCB016CDFStartPtr,
        CdfMiddleIx = SilkSpec.NlsfMsvqCB016CDFMiddleIdx,
        RatesQ5 = SilkSpec.NlsfMsvqCB016RatesQ5,
    };

    public static readonly NlsfCodebook Unvoiced16 = new()
    {
        Order = 16,
        VectorCounts = [32, 8, 8, 8, 8, 8, 8, 8, 8, 8],
        VectorsQ15 = SilkSpec.NlsfMsvqCB116Q15,
        NDeltaMinQ15 = SilkSpec.NlsfMsvqCB116NdeltaMinQ15,
        Cdf = SilkSpec.NlsfMsvqCB116CDF,
        CdfStartOffsets = SilkSpec.NlsfMsvqCB116CDFStartPtr,
        CdfMiddleIx = SilkSpec.NlsfMsvqCB116CDFMiddleIdx,
        RatesQ5 = SilkSpec.NlsfMsvqCB116RatesQ5,
    };

    public static readonly NlsfCodebook Voiced10 = new()
    {
        Order = 10,
        VectorCounts = [64, 16, 8, 8, 8, 16],
        VectorsQ15 = SilkSpec.NlsfMsvqCB010Q15,
        NDeltaMinQ15 = SilkSpec.NlsfMsvqCB010NdeltaMinQ15,
        Cdf = SilkSpec.NlsfMsvqCB010CDF,
        CdfStartOffsets = SilkSpec.NlsfMsvqCB010CDFStartPtr,
        CdfMiddleIx = SilkSpec.NlsfMsvqCB010CDFMiddleIdx,
        RatesQ5 = SilkSpec.NlsfMsvqCB010RatesQ5,
    };

    public static readonly NlsfCodebook Unvoiced10 = new()
    {
        Order = 10,
        VectorCounts = [32, 8, 8, 8, 8, 8],
        VectorsQ15 = SilkSpec.NlsfMsvqCB110Q15,
        NDeltaMinQ15 = SilkSpec.NlsfMsvqCB110NdeltaMinQ15,
        Cdf = SilkSpec.NlsfMsvqCB110CDF,
        CdfStartOffsets = SilkSpec.NlsfMsvqCB110CDFStartPtr,
        CdfMiddleIx = SilkSpec.NlsfMsvqCB110CDFMiddleIdx,
        RatesQ5 = SilkSpec.NlsfMsvqCB110RatesQ5,
    };

    public void DecodePath(RangeCoder rc, Span<int> indices)
    {
        ValidateLayout();
        int stages = Stages;
        if (indices.Length < stages)
            throw new SilkCodecException("SILK NLSF index output is too short.");
        for (int s = 0; s < stages; s++)
        {
            int count = VectorCounts[s] + 1;
            indices[s] = rc.Decode(Cdf.AsSpan(CdfStartOffsets[s], count), CdfMiddleIx[s]);
            if ((uint)indices[s] >= (uint)VectorCounts[s])
                throw new SilkCodecException("SILK NLSF index is outside its codebook stage.");
        }
    }

    public void DecodeVector(ReadOnlySpan<int> indices, Span<int> nlsfQ15)
    {
        ValidateLayout();
        int order = Order;
        if (indices.Length < Stages || nlsfQ15.Length < order)
            throw new SilkCodecException("SILK NLSF vector input or output is too short.");
        for (int s = 0; s < Stages; s++)
            if ((uint)indices[s] >= (uint)VectorCounts[s])
                throw new SilkCodecException("SILK NLSF vector index is outside its codebook stage.");

        int offset = 0;
        int idx0 = indices[0] * order;
        for (int i = 0; i < order; i++)
            nlsfQ15[i] = VectorsQ15[idx0 + i];

        offset += VectorCounts[0];
        int stages = Stages;
        for (int s = 1; s < stages; s++)
        {
            int add = (offset + indices[s]) * order;
            for (int i = 0; i < order; i++)
                nlsfQ15[i] += VectorsQ15[add + i];
            offset += VectorCounts[s];
        }
        Stabilize(nlsfQ15, NDeltaMinQ15, order);
    }

    void ValidateLayout()
    {
        if (VectorCounts is null || VectorsQ15 is null || NDeltaMinQ15 is null ||
            Cdf is null || CdfStartOffsets is null || CdfMiddleIx is null || RatesQ5 is null)
            throw new SilkCodecException("SILK NLSF codebook arrays are missing.");
        if (Order < 1 || Order > SilkLimits.MaxLpcOrder || Stages is < 1 or > 10 ||
            CdfStartOffsets.Length < Stages || CdfMiddleIx.Length < Stages ||
            NDeltaMinQ15.Length < Order + 1)
            throw new SilkCodecException("SILK NLSF codebook layout is invalid.");
        long vectorTotal = 0;
        for (int s = 0; s < Stages; s++)
        {
            int vectors = VectorCounts[s];
            int start = CdfStartOffsets[s];
            if (vectors < 1 || vectors >= int.MaxValue || start < 0 ||
                (long)start + vectors + 1 > Cdf.Length ||
                (uint)CdfMiddleIx[s] > (uint)vectors)
                throw new SilkCodecException("SILK NLSF stage CDF layout is invalid.");
            vectorTotal += vectors;
        }
        if (vectorTotal * Order > VectorsQ15.Length || vectorTotal > RatesQ5.Length)
            throw new SilkCodecException("SILK NLSF codebook vectors or rates are truncated.");
    }

    public static void Stabilize(Span<int> nlsfQ15, ReadOnlySpan<int> nDeltaMinQ15, int order)
    {
        if (order < 1 || order > SilkLimits.MaxLpcOrder ||
            nlsfQ15.Length < order || nDeltaMinQ15.Length < order + 1)
            throw new SilkCodecException("SILK NLSF stabilization input is invalid.");
        long deltaTotal = 0;
        for (int i = 0; i <= order; i++)
        {
            if (nDeltaMinQ15[i] < 0)
                throw new SilkCodecException("SILK NLSF minimum delta is negative.");
            deltaTotal += nDeltaMinQ15[i];
        }
        if (deltaTotal > 1 << 15)
            throw new SilkCodecException("SILK NLSF minimum deltas exceed its domain.");
        const int maxLoops = 20;
        fixed (int* nlsf = nlsfQ15)
        fixed (int* nd = nDeltaMinQ15)
        {
            for (int loops = 0; loops < maxLoops; loops++)
            {
                int minDiff = nlsf[0] - nd[0];
                int I = 0;
                for (int i = 1; i <= order - 1; i++)
                {
                    int diff = nlsf[i] - (nlsf[i - 1] + nd[i]);
                    if (diff < minDiff)
                    {
                        minDiff = diff;
                        I = i;
                    }
                }
                int lastDiff = (1 << 15) - (nlsf[order - 1] + nd[order]);
                if (lastDiff < minDiff)
                {
                    minDiff = lastDiff;
                    I = order;
                }
                if (minDiff >= 0) return;

                if (I == 0)
                    nlsf[0] = nd[0];
                else if (I == order)
                    nlsf[order - 1] = (1 << 15) - nd[order];
                else
                {
                    int minCenter = 0;
                    for (int k = 0; k < I; k++) minCenter += nd[k];
                    minCenter += nd[I] >> 1;
                    int maxCenter = 1 << 15;
                    for (int k = order; k > I; k--) maxCenter -= nd[k];
                    maxCenter -= nd[I] - (nd[I] >> 1);
                    int center = Fx.Limit(Fx.RShiftRound(nlsf[I - 1] + nlsf[I], 1), minCenter, maxCenter);
                    nlsf[I - 1] = center - (nd[I] >> 1);
                    nlsf[I] = nlsf[I - 1] + nd[I];
                }
            }

            InsertionSort(nlsf, order);
            if (nlsf[0] < nd[0]) nlsf[0] = nd[0];
            for (int i = 1; i < order; i++)
            {
                int min = nlsf[i - 1] + nd[i];
                if (nlsf[i] < min) nlsf[i] = min;
            }
            int lastMax = (1 << 15) - nd[order];
            if (nlsf[order - 1] > lastMax) nlsf[order - 1] = lastMax;
            for (int i = order - 2; i >= 0; i--)
            {
                int max = nlsf[i + 1] - nd[i + 1];
                if (nlsf[i] > max) nlsf[i] = max;
            }
        }
    }

    static void InsertionSort(int* a, int length)
    {
        for (int i = 1; i < length; i++)
        {
            int value = a[i];
            int j = i - 1;
            while (j >= 0 && value < a[j])
            {
                a[j + 1] = a[j];
                j--;
            }
            a[j + 1] = value;
        }
    }
}
