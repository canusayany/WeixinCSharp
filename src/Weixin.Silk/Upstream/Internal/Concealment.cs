namespace SilkCodec.NET.Internal;

internal sealed unsafe class PlcState
{
    public int PitchLQ8;
    public readonly short[] LtpCoefQ14 = new short[SilkLimits.LtpOrder];
    public readonly short[] PrevLpcQ12 = new short[SilkLimits.MaxLpcOrder];
    public int LastFrameLost;
    public int RandSeed;
    public short RandScaleQ14;
    public int ConcEnergy;
    public int ConcEnergyShift;
    public int PrevLtpScaleQ14;
    public readonly int[] PrevGainQ16 = new int[SilkLimits.NbSubfr];
    public int FsKhz;

    public void Reset(int frameLength) => PitchLQ8 = frameLength >> 1;

    public void Update(DecoderControl ctrl, int fsKhz, int lpcOrder, int subfrLength)
    {
        if (fsKhz != FsKhz)
        {
            PitchLQ8 = (SilkLimits.FrameLengthMs * fsKhz) >> 1;
            FsKhz = fsKhz;
        }
        int ltpGain = 0;
        if (ctrl.SigType == SilkLimits.SigTypeVoiced)
        {
            for (int j = 0; j * subfrLength < ctrl.PitchL[SilkLimits.NbSubfr - 1]; j++)
            {
                int temp = 0;
                for (int i = 0; i < SilkLimits.LtpOrder; i++)
                    temp += ctrl.LtpCoefQ14[(SilkLimits.NbSubfr - 1 - j) * SilkLimits.LtpOrder + i];
                if (temp > ltpGain)
                {
                    ltpGain = temp;
                    ctrl.LtpCoefQ14.AsSpan((SilkLimits.NbSubfr - 1 - j) * SilkLimits.LtpOrder, SilkLimits.LtpOrder).CopyTo(LtpCoefQ14);
                    PitchLQ8 = ctrl.PitchL[SilkLimits.NbSubfr - 1 - j] << 8;
                }
            }
            SilkUnsafe.Clear(LtpCoefQ14);
            LtpCoefQ14[SilkLimits.LtpOrder / 2] = (short)ltpGain;
            if (ltpGain < SilkLimits.VPitchGainStartMinQ14)
            {
                int scale = (SilkLimits.VPitchGainStartMinQ14 << 10) / (ltpGain > 1 ? ltpGain : 1);
                for (int i = 0; i < SilkLimits.LtpOrder; i++)
                    LtpCoefQ14[i] = (short)(Fx.SMulBb(LtpCoefQ14[i], scale) >> 10);
            }
            else if (ltpGain > SilkLimits.VPitchGainStartMaxQ14)
            {
                int scale = (SilkLimits.VPitchGainStartMaxQ14 << 14) / (ltpGain > 1 ? ltpGain : 1);
                for (int i = 0; i < SilkLimits.LtpOrder; i++)
                    LtpCoefQ14[i] = (short)(Fx.SMulBb(LtpCoefQ14[i], scale) >> 14);
            }
        }
        else
        {
            PitchLQ8 = (fsKhz * 18) << 8;
            SilkUnsafe.Clear(LtpCoefQ14);
        }
        ctrl.PredCoef1.AsSpan(0, lpcOrder).CopyTo(PrevLpcQ12);
        PrevLtpScaleQ14 = ctrl.LtpScaleQ14;
        ctrl.GainsQ16.AsSpan().CopyTo(PrevGainQ16);
    }

    public void Conceal(DecoderEngine dec, DecoderControl ctrl, Span<short> signal, int length)
    {
        if (dec.FsKhz != FsKhz)
        {
            Reset(dec.FrameLength);
            FsKhz = dec.FsKhz;
        }
        Lpc.ExpandBandwidth(PrevLpcQ12, dec.LpcOrder, SilkLimits.BweCoefQ16);
        short* excBuf = stackalloc short[SilkLimits.MaxFrameLength];
        int ptr = 0;
        fixed (int* sLtpQ16All = dec.SLtpQ16)
        fixed (int* sLpcQ14All = dec.SLpcQ14)
        fixed (int* excQ10 = dec.ExcQ10)
        {
            SilkUnsafe.Copy(sLtpQ16All, sLtpQ16All + dec.FrameLength, dec.FrameLength);
        fixed (int* prevGain = PrevGainQ16)
        {
            for (int k = SilkLimits.NbSubfr >> 1; k < SilkLimits.NbSubfr; k++)
            {
                for (int i = 0; i < dec.SubfrLength; i++)
                    excBuf[ptr + i] = (short)(Fx.SMulWw(excQ10[i + k * dec.SubfrLength], prevGain[k]) >> 10);
                ptr += dec.SubfrLength;
            }
        }
        Energy.SumSqrShift(new ReadOnlySpan<short>(excBuf, dec.SubfrLength), dec.SubfrLength, out int energy1, out int shift1);
        Energy.SumSqrShift(new ReadOnlySpan<short>(excBuf + dec.SubfrLength, dec.SubfrLength), dec.SubfrLength, out int energy2, out int shift2);
        int randPtr = (energy1 >> shift2) < (energy2 >> shift1)
            ? (3 * dec.SubfrLength - SilkLimits.RandBufSize > 0 ? 3 * dec.SubfrLength - SilkLimits.RandBufSize : 0)
            : (dec.FrameLength - SilkLimits.RandBufSize > 0 ? dec.FrameLength - SilkLimits.RandBufSize : 0);

        short* bQ14;
        fixed (short* bFixed = LtpCoefQ14)
        {
            bQ14 = bFixed;
            short randScale = RandScaleQ14;
            int harmGain = SilkLimits.HarmAttQ15[dec.LossCnt < 1 ? dec.LossCnt : 1];
            int randGain = dec.PrevSigType == SilkLimits.SigTypeVoiced
                ? SilkLimits.PlcRandAttenuateVQ15[dec.LossCnt < 1 ? dec.LossCnt : 1]
                : SilkLimits.PlcRandAttenuateUvQ15[dec.LossCnt < 1 ? dec.LossCnt : 1];
            if (dec.LossCnt == 0)
            {
                randScale = 1 << 14;
                if (dec.PrevSigType == SilkLimits.SigTypeVoiced)
                {
                    for (int i = 0; i < SilkLimits.LtpOrder; i++) randScale -= bQ14[i];
                    if (randScale < 3277) randScale = 3277;
                    randScale = (short)(Fx.SMulBb(randScale, PrevLtpScaleQ14) >> 14);
                }
                if (dec.PrevSigType == SilkLimits.SigTypeUnvoiced)
                {
                    Lpc.InversePredGain(PrevLpcQ12, dec.LpcOrder, out int invGainQ30);
                    int down = invGainQ30 < ((1 << 30) >> SilkLimits.Log2InvLpcGainHighThres)
                        ? invGainQ30
                        : ((1 << 30) >> SilkLimits.Log2InvLpcGainHighThres);
                    int low = (1 << 30) >> SilkLimits.Log2InvLpcGainLowThres;
                    if (down < low) down = low;
                    down <<= SilkLimits.Log2InvLpcGainHighThres;
                    randGain = Fx.SMulWb(down, randGain) >> 14;
                }
            }

            int randSeed = RandSeed;
            int lag = Fx.RShiftRound(PitchLQ8, 8);
            int sLtpBufIdx = dec.FrameLength;
            int* sigQ10 = stackalloc int[SilkLimits.MaxFrameLength];
            int sigPtr = 0;
            int* sLtpQ16 = sLtpQ16All;
            int* sLpcQ14 = sLpcQ14All;
            fixed (short* prevLpc = PrevLpcQ12)
            {
                for (int k = 0; k < SilkLimits.NbSubfr; k++)
                {
                    int* predLag = sLtpQ16 + sLtpBufIdx - lag + SilkLimits.LtpOrder / 2;
                    for (int i = 0; i < dec.SubfrLength; i++)
                    {
                        randSeed = Fx.Rand(randSeed);
                        int idx = (randSeed >> 25) & SilkLimits.RandBufMask;
                        int ltpPred = Fx.SMulWb(predLag[0], bQ14[0]);
                        ltpPred = Fx.SMlaWb(ltpPred, predLag[-1], bQ14[1]);
                        ltpPred = Fx.SMlaWb(ltpPred, predLag[-2], bQ14[2]);
                        ltpPred = Fx.SMlaWb(ltpPred, predLag[-3], bQ14[3]);
                        ltpPred = Fx.SMlaWb(ltpPred, predLag[-4], bQ14[4]);
                        predLag++;
                        int lpcExc = (Fx.SMulWb(excQ10[randPtr + idx], randScale) << 2) + Fx.RShiftRound(ltpPred, 4);
                        sLtpQ16[sLtpBufIdx] = lpcExc << 6;
                        sLtpBufIdx++;
                        sigQ10[sigPtr + i] = lpcExc;
                    }
                    sigPtr += dec.SubfrLength;
                    for (int j = 0; j < SilkLimits.LtpOrder; j++)
                        bQ14[j] = (short)(Fx.SMulBb(harmGain, bQ14[j]) >> 15);
                    randScale = (short)(Fx.SMulBb(randScale, randGain) >> 15);
                    PitchLQ8 += Fx.SMulWb(PitchLQ8, SilkLimits.PitchDriftFacQ16);
                    int maxPitch = (SilkLimits.MaxPitchLagMs * dec.FsKhz) << 8;
                    if (PitchLQ8 > maxPitch) PitchLQ8 = maxPitch;
                    lag = Fx.RShiftRound(PitchLQ8, 8);
                }
                sigPtr = 0;
                for (int k = 0; k < SilkLimits.NbSubfr; k++)
                {
                    for (int i = 0; i < dec.SubfrLength; i++)
                    {
                        int pred = Lpc.PredictQ10(sLpcQ14, prevLpc, i, dec.LpcOrder);
                        sigQ10[sigPtr + i] += pred;
                        sLpcQ14[SilkLimits.MaxLpcOrder + i] = sigQ10[sigPtr + i] << 4;
                    }
                    sigPtr += dec.SubfrLength;
                    SilkUnsafe.Copy(sLpcQ14, sLpcQ14 + dec.SubfrLength, SilkLimits.MaxLpcOrder);
                }
                fixed (short* dst = signal)
                {
                    int gain = PrevGainQ16[SilkLimits.NbSubfr - 1];
                    for (int i = 0; i < dec.FrameLength; i++)
                        dst[i] = (short)Fx.Sat16(Fx.RShiftRound(Fx.SMulWw(sigQ10[i], gain), 10));
                }
            }
            RandSeed = randSeed;
            RandScaleQ14 = randScale;
            for (int i = 0; i < SilkLimits.NbSubfr; i++)
                ctrl.PitchL[i] = lag;
        }
        }
    }

    public void GlueFrames(Span<short> signal, int length, int lossCnt)
    {
        if (lossCnt != 0)
        {
            Energy.SumSqrShift(signal, length, out ConcEnergy, out ConcEnergyShift);
            LastFrameLost = 1;
            return;
        }
        if (LastFrameLost != 0)
        {
            Energy.SumSqrShift(signal, length, out int energy, out int energyShift);
            if (energyShift > ConcEnergyShift) ConcEnergy >>= energyShift - ConcEnergyShift;
            else if (energyShift < ConcEnergyShift) energy >>= ConcEnergyShift - energyShift;
            if (energy > ConcEnergy)
            {
                int lz = Fx.Clz32(ConcEnergy) - 1;
                ConcEnergy <<= lz;
                int r = 24 - lz;
                energy >>= r > 0 ? r : 0;
                int fracQ24 = ConcEnergy / (energy > 1 ? energy : 1);
                int gainQ12 = Fx.SqrtApprox(fracQ24);
                int slopeQ12 = ((1 << 12) - gainQ12) / length;
                fixed (short* p = signal)
                {
                    for (int i = 0; i < length; i++)
                    {
                        p[i] = (short)((gainQ12 * p[i]) >> 12);
                        gainQ12 += slopeQ12;
                        if (gainQ12 > (1 << 12)) gainQ12 = 1 << 12;
                    }
                }
            }
        }
        LastFrameLost = 0;
    }
}

internal sealed unsafe class CngState
{
    readonly int[] _excBufQ10 = new int[SilkLimits.MaxFrameLength];
    readonly int[] _smthNlsfQ15 = new int[SilkLimits.MaxLpcOrder];
    readonly int[] _synthState = new int[SilkLimits.MaxLpcOrder];
    int _smthGainQ16;
    int _randSeed = 3176576;
    int _fsKhz;

    public void Reset(int lpcOrder)
    {
        int step = Fx.Int16Max / (lpcOrder + 1);
        int acc = 0;
        for (int i = 0; i < lpcOrder; i++)
        {
            acc += step;
            _smthNlsfQ15[i] = acc;
        }
        _smthGainQ16 = 0;
        _randSeed = 3176576;
        SilkUnsafe.Clear(_excBufQ10);
        SilkUnsafe.Clear(_synthState);
    }

    public void Apply(DecoderEngine dec, DecoderControl ctrl, Span<short> signal, int length, int lossCnt, int vadFlag, int lpcOrder, int fsKhz, int subfrLength, ReadOnlySpan<int> prevNlsf, ReadOnlySpan<int> excQ10)
    {
        if (fsKhz != _fsKhz)
        {
            Reset(lpcOrder);
            _fsKhz = fsKhz;
        }
        if (lossCnt == 0 && vadFlag == SilkLimits.NoVoiceActivity)
        {
            fixed (int* prev = prevNlsf)
            {
                for (int i = 0; i < lpcOrder; i++)
                    _smthNlsfQ15[i] += Fx.SMulWb(prev[i] - _smthNlsfQ15[i], SilkLimits.CngNlsfSmthQ16);
            }
            int maxGain = 0, subfr = 0;
            for (int i = 0; i < SilkLimits.NbSubfr; i++)
            {
                if (ctrl.GainsQ16[i] > maxGain) { maxGain = ctrl.GainsQ16[i]; subfr = i; }
            }
            fixed (int* excBuf = _excBufQ10)
            fixed (int* src = excQ10)
            {
                SilkUnsafe.Copy(excBuf + subfrLength, excBuf, (SilkLimits.NbSubfr - 1) * subfrLength);
                SilkUnsafe.Copy(excBuf, src + subfr * subfrLength, subfrLength);
            }
            for (int i = 0; i < SilkLimits.NbSubfr; i++)
                _smthGainQ16 += Fx.SMulWb(ctrl.GainsQ16[i] - _smthGainQ16, SilkLimits.CngGainSmthQ16);
        }
        if (lossCnt != 0)
        {
            short* cng = stackalloc short[length];
            int mask = SilkLimits.CngBufMaskMax;
            while (mask > length) mask >>= 1;
            int seed = _randSeed;
            for (int i = 0; i < length; i++)
            {
                seed = Fx.Rand(seed);
                int idx = (seed >> 24) & mask;
                cng[i] = (short)Fx.Sat16(Fx.RShiftRound(Fx.SMulWw(_excBufQ10[idx], _smthGainQ16), 10));
            }
            _randSeed = seed;
            short* lpc = stackalloc short[SilkLimits.MaxLpcOrder];
            Lpc.NlsfToStableLpc(_smthNlsfQ15.AsSpan(0, lpcOrder), new Span<short>(lpc, lpcOrder), lpcOrder);
            Lpc.SynthesisFilter(new ReadOnlySpan<short>(cng, length), new ReadOnlySpan<short>(lpc, lpcOrder), 1 << 26, _synthState.AsSpan(0, lpcOrder), new Span<short>(cng, length), length, lpcOrder);
            fixed (short* dst = signal)
            {
                for (int i = 0; i < length; i++)
                    dst[i] = (short)Fx.Sat16(dst[i] + cng[i]);
            }
        }
        else
            SilkUnsafe.Clear(_synthState, lpcOrder);
    }
}

internal static unsafe class Energy
{
    public static void SumSqrShift(ReadOnlySpan<short> x, int len, out int energy, out int shift)
    {
        fixed (short* px = x)
        {
            int nrg = 0, shft = 0, i = 0, last = len - 1;
            while (i < last)
            {
                nrg = Fx.SMlaBbOv(nrg, px[i], px[i]);
                nrg = Fx.SMlaBbOv(nrg, px[i + 1], px[i + 1]);
                i += 2;
                if (nrg < 0) { nrg = (int)((uint)nrg >> 2); shft = 2; break; }
            }
            for (; i < last; i += 2)
            {
                int tmp = Fx.SMulBb(px[i], px[i]);
                tmp = Fx.SMlaBbOv(tmp, px[i + 1], px[i + 1]);
                nrg = (int)((uint)nrg + ((uint)tmp >> shft));
                if (nrg < 0) { nrg = (int)((uint)nrg >> 2); shft += 2; }
            }
            if (i == last)
                nrg = (int)((uint)nrg + ((uint)Fx.SMulBb(px[i], px[i]) >> shft));
            if ((nrg & unchecked((int)0xC0000000)) != 0)
            {
                nrg = (int)((uint)nrg >> 2);
                shft += 2;
            }
            shift = shft;
            energy = nrg;
        }
    }
}
