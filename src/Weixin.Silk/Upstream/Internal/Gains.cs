using System.Runtime.CompilerServices;

namespace SilkCodec.NET.Internal;

internal static class Gains
{
    const int Offset = (SilkLimits.MinQgainDb * 128) / 6 + 16 * 128;
    const int InvScaleQ16 = (65536 * (((SilkLimits.MaxQgainDb - SilkLimits.MinQgainDb) * 128) / 6)) / (SilkLimits.NLevelsQgain - 1);
    const int ScaleQ16 = (65536 * (SilkLimits.NLevelsQgain - 1)) / (((SilkLimits.MaxQgainDb - SilkLimits.MinQgainDb) * 128) / 6);

    [MethodImpl(SilkUnsafe.Hot)]
    public static void Dequantize(Span<int> gainQ16, ReadOnlySpan<int> ind, ref int prevInd, int conditional)
    {
        for (int k = 0; k < SilkLimits.NbSubfr; k++)
        {
            if (k == 0 && conditional == 0)
                prevInd = ind[k];
            else
                prevInd += ind[k] + SilkLimits.MinDeltaGainQuant;
            gainQ16[k] = Fx.Log2Lin(Math.Min(Fx.SMulWb(InvScaleQ16, prevInd) + Offset, 3967));
        }
    }

    [MethodImpl(SilkUnsafe.Hot)]
    public static void Quantize(Span<int> ind, Span<int> gainQ16, ref int prevInd, int conditional)
    {
        for (int k = 0; k < SilkLimits.NbSubfr; k++)
        {
            ind[k] = Fx.SMulWb(ScaleQ16, Fx.Lin2Log(gainQ16[k]) - Offset);
            if (ind[k] < prevInd) ind[k]++;
            if (k == 0 && conditional == 0)
            {
                ind[k] = Fx.Limit(ind[k], 0, SilkLimits.NLevelsQgain - 1);
                ind[k] = Math.Max(ind[k], prevInd + SilkLimits.MinDeltaGainQuant);
                prevInd = ind[k];
            }
            else
            {
                ind[k] = Fx.Limit(ind[k] - prevInd, SilkLimits.MinDeltaGainQuant, SilkLimits.MaxDeltaGainQuant);
                prevInd += ind[k];
                ind[k] -= SilkLimits.MinDeltaGainQuant;
            }
            gainQ16[k] = Fx.Log2Lin(Math.Min(Fx.SMulWb(InvScaleQ16, prevInd) + Offset, 3967));
        }
    }
}
