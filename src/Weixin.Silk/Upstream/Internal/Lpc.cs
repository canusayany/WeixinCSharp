using System.Runtime.CompilerServices;
using SilkCodec.NET.Internal.Tables;

namespace SilkCodec.NET.Internal;

internal static unsafe class Lpc
{
    [MethodImpl(SilkUnsafe.Hot)]
    public static int PredictQ10(int* sLpc, short* a, int i, int order)
    {
        int* s = sLpc + SilkLimits.MaxLpcOrder + i;
        int pred = Fx.SMulWb(s[-1], a[0]);
        pred = Fx.SMlaWb(pred, s[-2], a[1]);
        pred = Fx.SMlaWb(pred, s[-3], a[2]);
        pred = Fx.SMlaWb(pred, s[-4], a[3]);
        pred = Fx.SMlaWb(pred, s[-5], a[4]);
        pred = Fx.SMlaWb(pred, s[-6], a[5]);
        pred = Fx.SMlaWb(pred, s[-7], a[6]);
        pred = Fx.SMlaWb(pred, s[-8], a[7]);
        pred = Fx.SMlaWb(pred, s[-9], a[8]);
        pred = Fx.SMlaWb(pred, s[-10], a[9]);
        if (order > 10)
        {
            pred = Fx.SMlaWb(pred, s[-11], a[10]);
            pred = Fx.SMlaWb(pred, s[-12], a[11]);
            pred = Fx.SMlaWb(pred, s[-13], a[12]);
            pred = Fx.SMlaWb(pred, s[-14], a[13]);
            pred = Fx.SMlaWb(pred, s[-15], a[14]);
            pred = Fx.SMlaWb(pred, s[-16], a[15]);
        }
        return pred;
    }

    public static void NlsfToStableLpc(ReadOnlySpan<int> nlsfQ15, Span<short> aQ12, int order)
    {
        NlsfToLpc(nlsfQ15, aQ12, order);
        for (int i = 0; i < SilkLimits.MaxLpcStabilizeIterations; i++)
        {
            if (InversePredGain(aQ12, order, out _) == 1)
                ExpandBandwidth(aQ12, order, 65536 - Fx.SMulBb(10 + i, i));
            else
                return;
        }
        aQ12.Slice(0, order).Clear();
    }

    public static void NlsfToLpc(ReadOnlySpan<int> nlsf, Span<short> a, int d)
    {
        int* cosLsfQ20 = stackalloc int[SilkLimits.MaxLpcOrder];
        int* p = stackalloc int[SilkLimits.MaxLpcOrder / 2 + 1];
        int* q = stackalloc int[SilkLimits.MaxLpcOrder / 2 + 1];
        int* a32 = stackalloc int[SilkLimits.MaxLpcOrder];

        fixed (int* pn = nlsf)
        fixed (int* cosTab = SilkSpec.LSFCosTabFIXQ12)
        {
            for (int k = 0; k < d; k++)
            {
                int fInt = pn[k] >> (15 - 7);
                int fFrac = pn[k] - (fInt << (15 - 7));
                int cosVal = cosTab[fInt];
                int delta = cosTab[fInt + 1] - cosVal;
                cosLsfQ20[k] = (cosVal << 8) + Fx.Mul(delta, fFrac);
            }
        }

        int dd = d >> 1;
        FindPoly(p, cosLsfQ20, 0, dd);
        FindPoly(q, cosLsfQ20, 1, dd);
        for (int k = 0; k < dd; k++)
        {
            int pTmp = p[k + 1] + p[k];
            int qTmp = q[k + 1] - q[k];
            a32[k] = -Fx.RShiftRound(pTmp + qTmp, 9);
            a32[d - k - 1] = Fx.RShiftRound(qTmp - pTmp, 9);
        }

        int i;
        for (i = 0; i < 10; i++)
        {
            int maxAbs = 0, idx = 0;
            for (int k = 0; k < d; k++)
            {
                int absval = SilkUnsafe.Abs(a32[k]);
                if (absval > maxAbs)
                {
                    maxAbs = absval;
                    idx = k;
                }
            }
            if (maxAbs > Fx.Int16Max)
            {
                maxAbs = Math.Min(maxAbs, 98369);
                int scQ16 = 65470 - Fx.Mul(65470 >> 2, maxAbs - Fx.Int16Max) / (Fx.Mul(maxAbs, idx + 1) >> 2);
                ExpandBandwidth32(a32, d, scQ16);
            }
            else break;
        }
        if (i == 10)
        {
            for (int k = 0; k < d; k++)
                a32[k] = Fx.Sat16(a32[k]);
        }
        fixed (short* pa = a)
        {
            for (int k = 0; k < d; k++)
                pa[k] = (short)a32[k];
        }
    }

    static void FindPoly(int* output, int* cLsf, int start, int dd)
    {
        output[0] = 1 << 20;
        output[1] = -cLsf[start];
        for (int k = 1; k < dd; k++)
        {
            int ftmp = cLsf[start + 2 * k];
            output[k + 1] = (output[k - 1] << 1) - Fx.RShiftRound64(Fx.Mull(ftmp, output[k]), 20);
            for (int n = k; n > 1; n--)
                output[n] += output[n - 2] - Fx.RShiftRound64(Fx.Mull(ftmp, output[n - 1]), 20);
            output[1] -= ftmp;
        }
    }

    public static void ExpandBandwidth(Span<short> ar, int d, int chirpQ16)
    {
        fixed (short* p = ar)
            ExpandBandwidth(p, d, chirpQ16);
    }

    [MethodImpl(SilkUnsafe.Hot)]
    public static void ExpandBandwidth(short* ar, int d, int chirpQ16)
    {
        int chirpMinusOne = chirpQ16 - 65536;
        for (int i = 0; i < d - 1; i++)
        {
            ar[i] = (short)Fx.RShiftRound(Fx.Mul(chirpQ16, ar[i]), 16);
            chirpQ16 += Fx.RShiftRound(Fx.Mul(chirpQ16, chirpMinusOne), 16);
        }
        ar[d - 1] = (short)Fx.RShiftRound(Fx.Mul(chirpQ16, ar[d - 1]), 16);
    }

    public static void ExpandBandwidth32(Span<int> ar, int d, int chirpQ16)
    {
        fixed (int* p = ar)
            ExpandBandwidth32(p, d, chirpQ16);
    }

    [MethodImpl(SilkUnsafe.Hot)]
    public static void ExpandBandwidth32(int* ar, int d, int chirpQ16)
    {
        int tmp = chirpQ16;
        for (int i = 0; i < d - 1; i++)
        {
            ar[i] = Fx.SMulWw(ar[i], tmp);
            tmp = Fx.SMulWw(chirpQ16, tmp);
        }
        ar[d - 1] = Fx.SMulWw(ar[d - 1], tmp);
    }

    public static int InversePredGain(ReadOnlySpan<short> aQ12, int order, out int invGainQ30)
    {
        const int qa = 16;
        const int aLimit = 65520; // 0.99975 in Q16
        int* aQa = stackalloc int[2 * SilkLimits.MaxLpcOrder];
        int stride = SilkLimits.MaxLpcOrder;
        int newIdx = order & 1;
        fixed (short* pa = aQ12)
        {
            for (int k = 0; k < order; k++)
                aQa[newIdx * stride + k] = pa[k] << (qa - 12);
        }

        invGainQ30 = 1 << 30;
        for (int k = order - 1; k > 0; k--)
        {
            int ak = aQa[newIdx * stride + k];
            if (ak > aLimit || ak < -aLimit)
            {
                invGainQ30 = 0;
                return 1;
            }
            int rcQ31 = -(ak << (31 - qa));
            int rcMult1Q30 = (Fx.Int32Max >> 1) - Fx.Smmul(rcQ31, rcQ31);
            int rcMult2Q16 = Fx.Inverse32VarQ(rcMult1Q30, 46);
            invGainQ30 = Fx.Smmul(invGainQ30, rcMult1Q30) << 2;
            int oldIdx = newIdx;
            newIdx = k & 1;
            int headrm = Fx.Clz32(rcMult2Q16) - 1;
            rcMult2Q16 <<= headrm;
            int* oldRow = aQa + oldIdx * stride;
            int* newRow = aQa + newIdx * stride;
            for (int n = 0; n < k; n++)
            {
                int tmp = oldRow[n] - (Fx.Smmul(oldRow[k - n - 1], rcQ31) << 1);
                newRow[n] = Fx.Smmul(tmp, rcMult2Q16) << (16 - headrm);
            }
        }
        int a0 = aQa[newIdx * stride];
        if (a0 > aLimit || a0 < -aLimit)
        {
            invGainQ30 = 0;
            return 1;
        }
        int rc = -(a0 << (31 - qa));
        int rcMult1 = (Fx.Int32Max >> 1) - Fx.Smmul(rc, rc);
        invGainQ30 = Fx.Smmul(invGainQ30, rcMult1) << 2;
        return 0;
    }

    public static void Biquad(Span<short> signal, ReadOnlySpan<int> b, ReadOnlySpan<int> a, Span<int> state, int len)
    {
        fixed (short* x = signal)
        fixed (int* pb = b)
        fixed (int* pa = a)
        fixed (int* st = state)
        {
            int s0 = st[0], s1 = st[1];
            int a0Neg = -pa[0], a1Neg = -pa[1];
            int b0 = pb[0], b1 = pb[1], b2 = pb[2];
            for (int k = 0; k < len; k++)
            {
                int in16 = x[k];
                int out32 = Fx.SMlaBb(s0, in16, b0);
                s0 = Fx.SMlaBb(s1, in16, b1);
                s0 += Fx.SMulWb(out32, a0Neg) << 3;
                s1 = Fx.SMulWb(out32, a1Neg) << 3;
                s1 = Fx.SMlaBb(s1, in16, b2);
                x[k] = (short)Fx.Sat16(Fx.RShiftRound(out32, 13) + 1);
            }
            st[0] = s0;
            st[1] = s1;
        }
    }

    public static void MaPrediction(ReadOnlySpan<short> input, ReadOnlySpan<short> b, Span<int> s, Span<short> output, int len, int order)
    {
        fixed (short* pin = input)
        fixed (short* pb = b)
        fixed (int* ps = s)
        fixed (short* pout = output)
        {
            for (int k = 0; k < len; k++)
            {
                int in16 = pin[k];
                int out32 = Fx.RShiftRound((in16 << 12) - ps[0], 12);
                for (int d = 0; d < order - 1; d++)
                    ps[d] = Fx.SMlaBbOv(ps[d + 1], in16, pb[d]);
                ps[order - 1] = Fx.SMulBb(in16, pb[order - 1]);
                pout[k] = (short)Fx.Sat16(out32);
            }
        }
    }

    public static void SynthesisFilter(ReadOnlySpan<short> excitation, ReadOnlySpan<short> aQ12, int gainQ26, Span<int> state, Span<short> output, int len, int order)
    {
        int orderHalf = order >> 1;
        fixed (short* exc = excitation)
        fixed (short* a = aQ12)
        fixed (int* st = state)
        fixed (short* y = output)
        {
            for (int k = 0; k < len; k++)
            {
                int sa = st[order - 1];
                int outQ10 = 0;
                for (int j = 0; j < orderHalf - 1; j++)
                {
                    int idx = 2 * j + 1;
                    int sb = st[order - 1 - idx];
                    st[order - 1 - idx] = sa;
                    outQ10 = Fx.SMlaWb(outQ10, sa, a[j << 1]);
                    outQ10 = Fx.SMlaWb(outQ10, sb, a[(j << 1) + 1]);
                    sa = st[order - 2 - idx];
                    st[order - 2 - idx] = sb;
                }
                int sbLast = st[0];
                st[0] = sa;
                outQ10 = Fx.SMlaWb(outQ10, sa, a[order - 2]);
                outQ10 = Fx.SMlaWb(outQ10, sbLast, a[order - 1]);
                outQ10 = Fx.AddSat32(outQ10, Fx.SMulWb(gainQ26, exc[k]));
                int out32 = Fx.RShiftRound(outQ10, 10);
                y[k] = (short)Fx.Sat16(out32);
                st[order - 1] = Fx.ShiftLeftSat(outQ10, 4);
            }
        }
    }

    public static void Interpolate(Span<int> xi, ReadOnlySpan<int> x0, ReadOnlySpan<int> x1, int ifactQ2, int d)
    {
        fixed (int* pxi = xi)
        fixed (int* px0 = x0)
        fixed (int* px1 = x1)
        {
            for (int i = 0; i < d; i++)
                pxi[i] = px0[i] + (((px1[i] - px0[i]) * ifactQ2) >> 2);
        }
    }

    public static void AnalysisFilter(ReadOnlySpan<short> input, ReadOnlySpan<short> aQ12, Span<int> residual)
    {
        int order = aQ12.Length;
        int len = input.Length;
        fixed (short* x = input)
        fixed (short* a = aQ12)
        fixed (int* r = residual)
        {
            for (int i = 0; i < len; i++)
            {
                int pred = 0;
                int limit = order < i ? order : i;
                for (int j = 0; j < limit; j++)
                    pred += a[j] * x[i - 1 - j];
                r[i] = x[i] - (pred >> 12);
            }
        }
    }

    public static void Autocorr(ReadOnlySpan<short> x, Span<int> corr, int order)
    {
        int n = x.Length;
        fixed (short* px = x)
        fixed (int* pc = corr)
        {
            for (int lag = 0; lag <= order; lag++)
            {
                short* a = px + lag;
                short* b = px;
                int count = n - lag;
                long s = 0;
                int i = 0;
                for (; i <= count - 4; i += 4)
                {
                    s += a[i] * (long)b[i]
                       + a[i + 1] * (long)b[i + 1]
                       + a[i + 2] * (long)b[i + 2]
                       + a[i + 3] * (long)b[i + 3];
                }
                for (; i < count; i++)
                    s += a[i] * (long)b[i];
                pc[lag] = (int)Math.Clamp(s >> 4, int.MinValue + 1, int.MaxValue);
            }
            pc[0] = pc[0] < 1 ? 1 : pc[0];
        }
    }

    public static void Levinson(ReadOnlySpan<int> r, Span<short> aQ12, int order)
    {
        int* a = stackalloc int[order];
        SilkUnsafe.Clear(a, order);
        int err = r[0];
        fixed (int* pr = r)
        {
            for (int m = 0; m < order; m++)
            {
                int acc = pr[m + 1];
                for (int i = 0; i < m; i++)
                    acc += (int)((long)a[i] * pr[m - i] >> 16);
                int km = err == 0 ? 0 : (int)Math.Clamp(-((long)acc << 16) / err, -65000, 65000);
                for (int i = 0; i < m / 2; i++)
                {
                    int t = a[i] + (int)((long)km * a[m - 1 - i] >> 16);
                    a[m - 1 - i] = a[m - 1 - i] + (int)((long)km * a[i] >> 16);
                    a[i] = t;
                }
                a[m] = km;
                err += (int)((long)km * acc >> 16);
                err = err < 1 ? 1 : err;
            }
        }
        fixed (short* pa = aQ12)
        {
            for (int i = 0; i < order; i++)
                pa[i] = (short)Fx.Sat16(a[i] >> 4);
        }
        ExpandBandwidth(aQ12, order, 65536 - 200);
    }

    public static void A2Nlsf(ReadOnlySpan<short> aQ12, Span<int> nlsfQ15, int order)
    {
        int dd = order >> 1;
        short* work = stackalloc short[SilkLimits.MaxLpcOrder];
        fixed (short* src = aQ12)
            SilkUnsafe.Copy(work, src, order);
        for (int attempt = 0; attempt < 16; attempt++)
        {
            if (TryA2Nlsf(work, nlsfQ15, order, dd))
                return;
            ExpandBandwidth(work, order, 65536 - 1300);
        }
        int step = 32768 / (order + 1);
        fixed (int* pn = nlsfQ15)
        {
            for (int i = 0; i < order; i++)
                pn[i] = step * (i + 1);
        }
    }

    static bool TryA2Nlsf(short* aQ12, Span<int> nlsfQ15, int order, int dd)
    {
        int* p = stackalloc int[dd + 1];
        int* q = stackalloc int[dd + 1];
        p[dd] = 1 << 16;
        q[dd] = 1 << 16;
        for (int k = 0; k < dd; k++)
        {
            int aLo = aQ12[dd - k - 1] << 4;
            int aHi = aQ12[dd + k] << 4;
            p[k] = -aLo - aHi;
            q[k] = -aLo + aHi;
        }
        for (int k = dd; k > 0; k--)
        {
            p[k - 1] -= p[k];
            q[k - 1] += q[k];
        }
        TransPoly(p, dd);
        TransPoly(q, dd);

        int root = 0;
        int which = 0;
        fixed (int* cosTab = SilkSpec.LSFCosTabFIXQ12)
        fixed (int* nlsf = nlsfQ15)
        {
            int yPrev = EvalPoly(p, cosTab[0], dd);
            for (int i = 1; i <= SilkLimits.LsfCosTabSz && root < order; i++)
            {
                int x = i < SilkLimits.LsfCosTabSz ? cosTab[i] : -cosTab[SilkLimits.LsfCosTabSz];
                int* poly = which == 0 ? p : q;
                int y = EvalPoly(poly, x, dd);
                if ((yPrev ^ y) < 0 || y == 0)
                {
                    int lo = i - 1;
                    int hi = i < SilkLimits.LsfCosTabSz ? i : SilkLimits.LsfCosTabSz;
                    for (int s = 0; s < 3; s++)
                    {
                        int mid = (lo + hi) >> 1;
                        int ym = EvalPoly(poly, cosTab[mid], dd);
                        if ((yPrev ^ ym) < 0)
                        {
                            hi = mid;
                            y = ym;
                        }
                        else
                        {
                            lo = mid;
                            yPrev = ym;
                        }
                    }
                    int xLo = cosTab[lo];
                    int xHi = hi >= SilkLimits.LsfCosTabSz ? -xLo : cosTab[hi];
                    int den = xLo - xHi;
                    int frac = den == 0 ? 0 : Fx.Limit((yPrev << 8) / den, 0, 256);
                    nlsf[root] = (lo << 8) + frac;
                    if (nlsf[root] > 32767) nlsf[root] = 32767;
                    root++;
                    which ^= 1;
                    yPrev = which == 0 ? EvalPoly(p, x, dd) : EvalPoly(q, x, dd);
                }
                else
                    yPrev = y;
            }
            if (root != order) return false;
            for (int i = 1; i < order; i++)
                if (nlsf[i] <= nlsf[i - 1]) nlsf[i] = nlsf[i - 1] + 64;
        }
        return true;
    }

    static void TransPoly(int* p, int dd)
    {
        for (int k = 2; k <= dd; k++)
        {
            for (int n = dd; n > k; n--)
                p[n - 2] -= p[n];
            p[k - 2] -= p[k] << 1;
        }
    }

    [MethodImpl(SilkUnsafe.Hot)]
    static int EvalPoly(int* p, int xQ12, int dd)
    {
        int y = p[dd];
        int xQ16 = xQ12 << 4;
        for (int n = dd - 1; n >= 0; n--)
            y = Fx.SMlaWw(p[n], y, xQ16);
        return y;
    }
}
