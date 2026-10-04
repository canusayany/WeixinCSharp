using SilkCodec.NET.Internal.Resample;
using SilkCodec.NET.Internal.Tables;

namespace SilkCodec.NET.Internal.Encoder;

internal sealed unsafe class EncoderEngine
{
    readonly RangeCoder _rc = new();
    readonly SilkResampler _resampler = new();
    readonly short[] _input = new short[SilkLimits.MaxFrameLength];
    readonly int[] _hp = new int[2];
    readonly int[] _sLpcQ14 = new int[SilkLimits.MaxFrameLength / SilkLimits.NbSubfr + SilkLimits.MaxLpcOrder];
    int _fsKhz = 24;
    int _frameLength = 480;
    int _subfrLength = 120;
    int _lpcOrder = 16;
    int _lastGainIndex = 1;
    int _typeOffsetPrev;
    int _apiHz = 24000;
    int _framesInPacket;
    int _packetFrames = 1;
    int _seed;
    int _prevInvGainQ16 = 65536;

    public void Reset(int apiHz, int maxInternalHz, int packetMs)
    {
        _apiHz = apiHz;
        _fsKhz = maxInternalHz / 1000 < SilkLimits.MaxFsKhz ? maxInternalHz / 1000 : SilkLimits.MaxFsKhz;
        if (_fsKhz is not (8 or 12 or 16 or 24)) _fsKhz = 24;
        if (apiHz < _fsKhz * 1000)
            _fsKhz = apiHz >= 16000 ? 16 : apiHz >= 12000 ? 12 : 8;
        _frameLength = SilkLimits.FrameLengthMs * _fsKhz;
        _subfrLength = _frameLength / SilkLimits.NbSubfr;
        _lpcOrder = _fsKhz == 8 ? SilkLimits.MinLpcOrder : SilkLimits.MaxLpcOrder;
        _packetFrames = Math.Clamp(packetMs / 20, 1, 5);
        _framesInPacket = 0;
        _lastGainIndex = 1;
        _typeOffsetPrev = 0;
        _seed = 0;
        _prevInvGainQ16 = 65536;
        SilkUnsafe.Clear(_hp);
        SilkUnsafe.Clear(_sLpcQ14);
        _resampler.Init(apiHz, _fsKhz * 1000);
        _rc.InitEncoder();
    }

    public byte[]? Encode20Ms(ReadOnlySpan<short> apiPcm, int bitRate)
    {
        Span<short> frame = _input.AsSpan(0, _frameLength);
        if (apiPcm.Length * 1000 != 20 * _apiHz)
            return null;
        if (_apiHz == _fsKhz * 1000)
            apiPcm.Slice(0, _frameLength).CopyTo(frame);
        else
            _resampler.Process(apiPcm, frame);

        HighPass(frame);
        EncodeFrame(frame, bitRate);
        _framesInPacket++;
        if (_framesInPacket < _packetFrames)
            return [];
        _rc.WrapUp();
        _rc.GetLength(out int nBytes);
        if (nBytes < 1) nBytes = 1;
        byte[] packet = SilkUnsafe.Uninit<byte>(nBytes);
        _rc.Buffer.AsSpan(0, nBytes).CopyTo(packet);
        _rc.InitEncoder();
        _framesInPacket = 0;
        return packet;
    }

    void HighPass(Span<short> x)
    {
        var a = _fsKhz switch
        {
            8 => SilkSpec.DecAHP8,
            12 => SilkSpec.DecAHP12,
            16 => SilkSpec.DecAHP16,
            _ => SilkSpec.DecAHP24
        };
        var b = _fsKhz switch
        {
            8 => SilkSpec.DecBHP8,
            12 => SilkSpec.DecBHP12,
            16 => SilkSpec.DecBHP16,
            _ => SilkSpec.DecBHP24
        };
        Lpc.Biquad(x, b, a, _hp, x.Length);
    }

    void EncodeFrame(Span<short> frame, int bitRate)
    {
        int* nlsf = stackalloc int[_lpcOrder];
        short* aAnal = stackalloc short[_lpcOrder];
        int* residual = stackalloc int[_frameLength];
        AnalyzeLpc(frame, new Span<short>(aAnal, _lpcOrder), new Span<int>(nlsf, _lpcOrder));

        int sigType = SilkLimits.SigTypeUnvoiced;
        int quantOff = 0;
        var cb = _fsKhz == 8 ? NlsfCodebook.Unvoiced10 : NlsfCodebook.Unvoiced16;
        int* nlsfIx = stackalloc int[cb.Stages];
        EncodeNlsf(new ReadOnlySpan<int>(nlsf, _lpcOrder), cb, new Span<int>(nlsfIx, cb.Stages));
        int* nlsfQ = stackalloc int[_lpcOrder];
        cb.DecodeVector(new ReadOnlySpan<int>(nlsfIx, cb.Stages), new Span<int>(nlsfQ, _lpcOrder));
        short* aQ = stackalloc short[_lpcOrder];
        Lpc.NlsfToStableLpc(new ReadOnlySpan<int>(nlsfQ, _lpcOrder), new Span<short>(aQ, _lpcOrder), _lpcOrder);
        Lpc.AnalysisFilter(frame, new ReadOnlySpan<short>(aQ, _lpcOrder), new Span<int>(residual, _frameLength));

        int* gainsQ16 = stackalloc int[SilkLimits.NbSubfr];
        int* gainIx = stackalloc int[SilkLimits.NbSubfr];
        int subfr = _subfrLength;
        for (int k = 0; k < SilkLimits.NbSubfr; k++)
        {
            long e = 1;
            int* r = residual + k * subfr;
            for (int i = 0; i < subfr; i++)
                e += (long)r[i] * r[i];
            int rms = Fx.SqrtApprox((int)(e / subfr < Fx.Int32Max ? e / subfr : Fx.Int32Max));
            long g = (long)(rms > 1 ? rms : 1) << 16;
            gainsQ16[k] = (int)(g < int.MaxValue ? g : int.MaxValue);
        }
        Gains.Quantize(new Span<int>(gainIx, SilkLimits.NbSubfr), new Span<int>(gainsQ16, SilkLimits.NbSubfr), ref _lastGainIndex, _framesInPacket);

        int typeOffset = (sigType << 1) | quantOff;
        int seed = _seed & 3;
        _seed++;
        int* pulses = stackalloc int[_frameLength];
        EncodeNsq(frame, new ReadOnlySpan<short>(aQ, _lpcOrder), new ReadOnlySpan<int>(gainsQ16, SilkLimits.NbSubfr), new Span<int>(pulses, _frameLength), seed, sigType, quantOff, bitRate);

        if (_framesInPacket == 0)
        {
            int fsIx = _fsKhz switch { 8 => 0, 12 => 1, 16 => 2, _ => 3 };
            _rc.Encode(fsIx, SilkSpec.SamplingRatesCDF);
            _rc.Encode(typeOffset, SilkSpec.TypeOffsetCDF);
            _rc.Encode(gainIx[0], SilkSpec.GainCDF[sigType]);
        }
        else
        {
            _rc.Encode(typeOffset, SilkSpec.TypeOffsetJointCDF[_typeOffsetPrev]);
            _rc.Encode(gainIx[0], SilkSpec.DeltaGainCDF);
        }
        _typeOffsetPrev = typeOffset;
        for (int i = 1; i < SilkLimits.NbSubfr; i++)
            _rc.Encode(gainIx[i], SilkSpec.DeltaGainCDF);

        for (int s = 0; s < cb.Stages; s++)
            _rc.Encode(nlsfIx[s], cb.Cdf.AsSpan(cb.CdfStartOffsets[s], cb.VectorCounts[s] + 1));
        _rc.Encode(4, SilkSpec.NlsfInterpolationFactorCDF);

        _rc.Encode(seed, SilkSpec.SeedCDF);
        PulseCodec.Encode(_rc, new ReadOnlySpan<int>(pulses, _frameLength), _frameLength, sigType, quantOff);
        int energy = 0;
        fixed (short* pf = frame)
        {
            for (int i = 0; i < frame.Length; i += 8)
                energy += pf[i] < 0 ? -pf[i] : pf[i];
        }
        int vad = energy > 200 ? SilkLimits.VoiceActivity : SilkLimits.NoVoiceActivity;
        _rc.Encode(vad, SilkSpec.VadflagCDF);
        int term = _framesInPacket + 1 >= _packetFrames ? SilkLimits.LastFrame : SilkLimits.MoreFrames;
        _rc.Encode(term, SilkSpec.FrameTerminationCDF);
    }

    void EncodeNsq(ReadOnlySpan<short> frame, ReadOnlySpan<short> aQ12, ReadOnlySpan<int> gainsQ16, Span<int> pulses, int seed, int sigType, int quantOff, int bitRate)
    {
        int offsetQ10 = SilkSpec.QuantizationOffsetsQ10[sigType][quantOff];
        int maxAbsQ = bitRate < 12000 ? 8 : bitRate < 20000 ? 12 : 20;
        int randSeed = seed;
        int lpcOrder = _lpcOrder;
        int subfr = _subfrLength;
        fixed (short* pf = frame)
        fixed (short* a = aQ12)
        fixed (int* gains = gainsQ16)
        fixed (int* q = pulses)
        fixed (int* sLpc = _sLpcQ14)
        {
            for (int k = 0; k < SilkLimits.NbSubfr; k++)
            {
                int gainQ16 = gains[k];
                int invGainQ16 = Fx.Inverse32VarQ(gainQ16 > 1 ? gainQ16 : 1, 32);
                if (invGainQ16 > Fx.Int16Max) invGainQ16 = Fx.Int16Max;
                int gainAdjQ16 = 1 << 16;
                if (invGainQ16 != _prevInvGainQ16)
                    gainAdjQ16 = Fx.Div32VarQ(invGainQ16, _prevInvGainQ16, 16);
                for (int i = 0; i < SilkLimits.MaxLpcOrder; i++)
                    sLpc[i] = Fx.SMulWw(gainAdjQ16, sLpc[i]);
                _prevInvGainQ16 = invGainQ16;

                int off = k * subfr;
                for (int i = 0; i < subfr; i++)
                {
                    int pred = Lpc.PredictQ10(sLpc, a, i, lpcOrder);
                    int xScQ10 = Fx.SMulBb(pf[off + i], invGainQ16) >> 6;
                    int rQ10 = xScQ10 - pred;
                    randSeed = Fx.Rand(randSeed);
                    int dither = randSeed >> 31;
                    rQ10 = (rQ10 ^ dither) - dither;
                    rQ10 -= offsetQ10;
                    rQ10 = Fx.Limit(rQ10, -64 << 10, 64 << 10);
                    int pulse = Fx.Limit(Fx.RShiftRound(rQ10, 10), -maxAbsQ, maxAbsQ);
                    q[off + i] = pulse;
                    randSeed += pulse;

                    int excQ10 = (pulse << 10) + offsetQ10;
                    excQ10 = (excQ10 ^ dither) - dither;
                    sLpc[SilkLimits.MaxLpcOrder + i] = Fx.ShiftLeftOverflow(excQ10 + pred, 4);
                }
                SilkUnsafe.Copy(sLpc, sLpc + subfr, SilkLimits.MaxLpcOrder);
            }
        }
    }

    static void EncodeNlsf(ReadOnlySpan<int> nlsf, NlsfCodebook cb, Span<int> indices)
    {
        int order = cb.Order;
        int* err = stackalloc int[order];
        fixed (int* src = nlsf)
            SilkUnsafe.Copy(err, src, order);
        int offset = 0;
        int stages = cb.Stages;
        fixed (int* vec = cb.VectorsQ15)
        fixed (int* counts = cb.VectorCounts)
        fixed (int* ix = indices)
        {
            for (int s = 0; s < stages; s++)
            {
                int best = 0;
                long bestE = long.MaxValue;
                int nVec = counts[s];
                for (int v = 0; v < nVec; v++)
                {
                    long e = 0;
                    int* cbv = vec + (offset + v) * order;
                    int i = 0;
                    for (; i <= order - 4; i += 4)
                    {
                        int d0 = err[i] - cbv[i];
                        int d1 = err[i + 1] - cbv[i + 1];
                        int d2 = err[i + 2] - cbv[i + 2];
                        int d3 = err[i + 3] - cbv[i + 3];
                        e += (long)d0 * d0 + (long)d1 * d1 + (long)d2 * d2 + (long)d3 * d3;
                    }
                    for (; i < order; i++)
                    {
                        int d = err[i] - cbv[i];
                        e += (long)d * d;
                    }
                    if (e < bestE) { bestE = e; best = v; }
                }
                ix[s] = best;
                int* chosen = vec + (offset + best) * order;
                for (int i = 0; i < order; i++)
                    err[i] -= chosen[i];
                offset += nVec;
            }
        }
    }

    void AnalyzeLpc(ReadOnlySpan<short> frame, Span<short> aQ12, Span<int> nlsf)
    {
        int* corr = stackalloc int[_lpcOrder + 1];
        Lpc.Autocorr(frame, new Span<int>(corr, _lpcOrder + 1), _lpcOrder);
        Lpc.Levinson(new ReadOnlySpan<int>(corr, _lpcOrder + 1), aQ12, _lpcOrder);
        Lpc.A2Nlsf(aQ12, nlsf, _lpcOrder);
        var cb = _fsKhz == 8 ? NlsfCodebook.Unvoiced10 : NlsfCodebook.Unvoiced16;
        NlsfCodebook.Stabilize(nlsf, cb.NDeltaMinQ15, _lpcOrder);
    }
}
