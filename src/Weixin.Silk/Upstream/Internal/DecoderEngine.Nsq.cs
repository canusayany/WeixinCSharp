using SilkCodec.NET.Internal.Tables;

namespace SilkCodec.NET.Internal;

// Local hardening of the vendored decoder: preflight every synthesis region
// before entering unsafe DSP. The SDK's re-whitening start_idx assertions are
// enforced as exceptions, including when a valid range symbol combination
// produces a pitch contour that cannot fit the decoder's history buffers.
internal sealed unsafe partial class DecoderEngine
{
    void InverseNsq(Span<short> xq)
    {
        ValidateInverseNsq(xq.Length);
        int offsetQ10 = SilkSpec.QuantizationOffsetsQ10[_ctrl.SigType][_ctrl.QuantOffsetType];
        int interpFlag = _ctrl.NlsfInterpCoefQ2 < 4 ? 1 : 0;
        int randSeed = _ctrl.Seed;
        int frameLength = _frameLength;
        int subfr = _subfrLength;
        int lpcOrder = _lpcOrder;
        int lagPrev = _lagPrev;

        fixed (int* pulses = _pulses)
        fixed (int* exc = _excQ10)
        fixed (int* sLtpQ16 = _sLtpQ16)
        fixed (int* sLpcQ14 = _sLpcQ14)
        fixed (int* res = _resQ10)
        fixed (int* vec = _vecQ10)
        fixed (short* outBuf = _outBuf)
        fixed (short* sLtp = _sLtp)
        fixed (int* filtState = _filtState)
        fixed (short* pred0 = _ctrl.PredCoef0)
        fixed (short* pred1 = _ctrl.PredCoef1)
        fixed (short* ltpCoef = _ctrl.LtpCoefQ14)
        fixed (int* pitch = _ctrl.PitchL)
        fixed (int* gains = _ctrl.GainsQ16)
        {
            for (int i = 0; i < frameLength; i++)
            {
                randSeed = Fx.Rand(randSeed);
                int dither = randSeed >> 31;
                int e = (pulses[i] << 10) + offsetQ10;
                exc[i] = (e ^ dither) - dither;
                randSeed += pulses[i];
            }

            int sLtpBufIdx = frameLength;
            int pexc = 0, pres = 0, pxq = frameLength;
            for (int k = 0; k < SilkLimits.NbSubfr; k++)
            {
                short* aQ12 = (k >> 1) == 0 ? pred0 : pred1;
                short* bQ14 = ltpCoef + k * SilkLimits.LtpOrder;
                int gainQ16 = gains[k];
                int sigType = _ctrl.SigType;
                int invGainQ16 = Fx.Inverse32VarQ(gainQ16 > 1 ? gainQ16 : 1, 32);
                if (invGainQ16 > Fx.Int16Max) invGainQ16 = Fx.Int16Max;
                int gainAdjQ16 = 1 << 16;
                if (invGainQ16 != _prevInvGainQ16)
                    gainAdjQ16 = Fx.Div32VarQ(invGainQ16, _prevInvGainQ16, 16);

                if (_lossCnt != 0 && _prevSigType == SilkLimits.SigTypeVoiced && _ctrl.SigType == SilkLimits.SigTypeUnvoiced && k < (SilkLimits.NbSubfr >> 1))
                {
                    SilkUnsafe.Clear(bQ14, SilkLimits.LtpOrder);
                    bQ14[SilkLimits.LtpOrder / 2] = 1 << 12;
                    sigType = SilkLimits.SigTypeVoiced;
                    pitch[k] = lagPrev;
                }

                int lag = pitch[k];
                if (sigType == SilkLimits.SigTypeVoiced)
                {
                    if ((k & (3 - (interpFlag << 1))) == 0)
                    {
                        int startIdx = frameLength - lag - lpcOrder - SilkLimits.LtpOrder / 2;
                        SilkUnsafe.Clear(filtState, lpcOrder);
                        Lpc.MaPrediction(
                            _outBuf.AsSpan(startIdx + k * (frameLength >> 2), frameLength - startIdx),
                            new ReadOnlySpan<short>(aQ12, lpcOrder),
                            _filtState.AsSpan(0, lpcOrder),
                            _sLtp.AsSpan(startIdx, frameLength - startIdx),
                            frameLength - startIdx,
                            lpcOrder);
                        int invGainQ32 = invGainQ16 << 16;
                        if (k == 0)
                            invGainQ32 = Fx.SMulWb(invGainQ32, _ctrl.LtpScaleQ14) << 2;
                        for (int i = 0; i < lag + SilkLimits.LtpOrder / 2; i++)
                            sLtpQ16[sLtpBufIdx - i - 1] = Fx.SMulWb(invGainQ32, sLtp[frameLength - i - 1]);
                    }
                    else if (gainAdjQ16 != (1 << 16))
                    {
                        for (int i = 0; i < lag + SilkLimits.LtpOrder / 2; i++)
                            sLtpQ16[sLtpBufIdx - i - 1] = Fx.SMulWw(gainAdjQ16, sLtpQ16[sLtpBufIdx - i - 1]);
                    }
                }

                for (int i = 0; i < SilkLimits.MaxLpcOrder; i++)
                    sLpcQ14[i] = Fx.SMulWw(gainAdjQ16, sLpcQ14[i]);
                _prevInvGainQ16 = invGainQ16;

                if (sigType == SilkLimits.SigTypeVoiced)
                {
                    int* predLag = sLtpQ16 + sLtpBufIdx - lag + SilkLimits.LtpOrder / 2;
                    for (int i = 0; i < subfr; i++)
                    {
                        int ltpPred = Fx.SMulWb(predLag[0], bQ14[0]);
                        ltpPred = Fx.SMlaWb(ltpPred, predLag[-1], bQ14[1]);
                        ltpPred = Fx.SMlaWb(ltpPred, predLag[-2], bQ14[2]);
                        ltpPred = Fx.SMlaWb(ltpPred, predLag[-3], bQ14[3]);
                        ltpPred = Fx.SMlaWb(ltpPred, predLag[-4], bQ14[4]);
                        predLag++;
                        res[pres + i] = exc[pexc + i] + Fx.RShiftRound(ltpPred, 4);
                        sLtpQ16[sLtpBufIdx] = res[pres + i] << 6;
                        sLtpBufIdx++;
                    }
                }
                else
                {
                    SilkUnsafe.Copy(res + pres, exc + pexc, subfr);
                }

                for (int i = 0; i < subfr; i++)
                {
                    int pred = Lpc.PredictQ10(sLpcQ14, aQ12, i, lpcOrder);
                    vec[i] = res[pres + i] + pred;
                    sLpcQ14[SilkLimits.MaxLpcOrder + i] = Fx.ShiftLeftOverflow(vec[i], 4);
                }
                for (int i = 0; i < subfr; i++)
                    outBuf[pxq + i] = (short)Fx.Sat16(Fx.RShiftRound(Fx.SMulWw(vec[i], gainQ16), 10));
                SilkUnsafe.Copy(sLpcQ14, sLpcQ14 + subfr, SilkLimits.MaxLpcOrder);
                pexc += subfr;
                pres += subfr;
                pxq += subfr;
            }
            fixed (short* dst = xq)
                SilkUnsafe.Copy(dst, outBuf + frameLength, frameLength);
        }
    }

    void ValidateNsqDimensions(int outputLength)
    {
        if (_fsKhz is not (8 or 12 or 16 or 24) ||
            _frameLength != SilkLimits.FrameLengthMs * _fsKhz ||
            _subfrLength < 1 || (long)_subfrLength * SilkLimits.NbSubfr != _frameLength ||
            _lpcOrder != (_fsKhz == 8 ? SilkLimits.MinLpcOrder : SilkLimits.MaxLpcOrder) ||
            outputLength < _frameLength ||
            _pulses.Length < _frameLength || _excQ10.Length < _frameLength ||
            _resQ10.Length < _frameLength || _outBuf.Length < 2L * _frameLength ||
            _sLtpQ16.Length < 2L * _frameLength || _sLtp.Length < _frameLength ||
            _sLpcQ14.Length < SilkLimits.MaxLpcOrder + (long)_subfrLength ||
            _vecQ10.Length < _subfrLength || _filtState.Length < _lpcOrder ||
            _ctrl.PredCoef0.Length < _lpcOrder || _ctrl.PredCoef1.Length < _lpcOrder ||
            _ctrl.PitchL.Length < SilkLimits.NbSubfr || _ctrl.GainsQ16.Length < SilkLimits.NbSubfr ||
            _ctrl.LtpCoefQ14.Length < SilkLimits.NbSubfr * SilkLimits.LtpOrder)
            throw new SilkCodecException("SILK inverse NSQ frame, LPC state or output dimensions are invalid.");
    }

    void ValidateInverseNsq(int outputLength)
    {
        ValidateNsqDimensions(outputLength);
        if ((uint)_ctrl.SigType > 1 || (uint)_ctrl.QuantOffsetType > 1 ||
            (uint)_ctrl.NlsfInterpCoefQ2 > 4 || _prevInvGainQ16 <= 0 ||
            _lossCnt < 0 || (uint)_prevSigType > 1)
            throw new SilkCodecException("SILK inverse NSQ control state is invalid.");

        int interpFlag = _ctrl.NlsfInterpCoefQ2 < 4 ? 1 : 0;
        int sLtpBufIdx = _frameLength;
        for (int k = 0; k < SilkLimits.NbSubfr; k++)
        {
            if (_ctrl.GainsQ16[k] <= 0)
                throw new SilkCodecException("SILK inverse NSQ gain is invalid.");
            bool plcTransition = _lossCnt != 0 && _prevSigType == SilkLimits.SigTypeVoiced &&
                _ctrl.SigType == SilkLimits.SigTypeUnvoiced && k < SilkLimits.NbSubfr / 2;
            if (_ctrl.SigType == SilkLimits.SigTypeVoiced || plcTransition)
            {
                int lag = plcTransition ? _lagPrev : _ctrl.PitchL[k];
                ValidateNsqPitch(k, lag, sLtpBufIdx, interpFlag);
                sLtpBufIdx += _subfrLength;
            }
        }
    }

    void ValidateNsqPitch(int subframe, int lag, int sLtpBufIdx, int interpFlag)
    {
        // Validate the actual regions, rather than imposing the first
        // subframe's re-whitening limit on every contour subframe.
        if ((uint)subframe >= SilkLimits.NbSubfr || (uint)interpFlag > 1 ||
            lag <= SilkLimits.LtpOrder / 2 || sLtpBufIdx < 0 ||
            (long)sLtpBufIdx + _subfrLength > _sLtpQ16.Length)
            throw new SilkCodecException("SILK inverse NSQ pitch or LTP write region is invalid.");
        long historyStart = (long)sLtpBufIdx - lag - SilkLimits.LtpOrder / 2;
        long predictionStart = (long)sLtpBufIdx - lag + SilkLimits.LtpOrder / 2;
        long predictionLast = predictionStart + _subfrLength - 1;
        if (historyStart < 0 || predictionStart - (SilkLimits.LtpOrder - 1) < 0 ||
            predictionLast >= _sLtpQ16.Length)
            throw new SilkCodecException("SILK inverse NSQ pitch exceeds its LTP history or prediction region.");

        if ((subframe & (3 - (interpFlag << 1))) == 0)
        {
            long start = (long)_frameLength - lag - _lpcOrder - SilkLimits.LtpOrder / 2;
            long length = _frameLength - start;
            long source = start + (long)subframe * (_frameLength >> 2);
            if (start < 0 || start > _frameLength - _lpcOrder || length < _lpcOrder ||
                start + length > _sLtp.Length || source < 0 || source + length > _outBuf.Length ||
                (long)_frameLength - lag - SilkLimits.LtpOrder / 2 < 0)
                throw new SilkCodecException("SILK inverse NSQ pitch exceeds its re-whitening buffers.");
        }
    }
}
