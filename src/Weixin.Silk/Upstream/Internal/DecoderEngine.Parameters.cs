using SilkCodec.NET.Internal.Tables;

namespace SilkCodec.NET.Internal;

// Local hardening: check decoded table indices and pitch memory regions before
// synthesis. An entropy-valid symbol combination is not necessarily a safe
// decoder contour; no malformed packet may fall through to unsafe DSP.
internal sealed unsafe partial class DecoderEngine
{
    void DecodeParameters()
    {
        if (_nFramesDecoded < 0 || _nFramesDecoded >= SilkLimits.MaxFramesPerPacket)
            throw new SilkCodecException("SILK decoded frame count is invalid.");
        if (_nFramesDecoded == 0)
        {
            int ix = _rc.Decode(SilkSpec.SamplingRatesCDF, SilkSpec.SamplingRatesOffset);
            if (ix is < 0 or > 3)
            {
                _rc.Error = SilkLimits.RangeIllegalSamplingRate;
                throw new SilkCodecException("SILK decoded sampling rate is invalid.");
            }
            SetFs(SilkSpec.SamplingRatesTable[ix]);
        }
        ValidateNsqDimensions(_frameLength);
        if (_nFramesDecoded != 0 && (uint)_typeOffsetPrev >= SilkSpec.TypeOffsetJointCDF.Length)
            throw new SilkCodecException("SILK previous signal offset is outside its codebook.");

        int typeOffset = _nFramesDecoded == 0
            ? _rc.Decode(SilkSpec.TypeOffsetCDF, SilkSpec.TypeOffsetCDFOffset)
            : _rc.Decode(SilkSpec.TypeOffsetJointCDF[_typeOffsetPrev], SilkSpec.TypeOffsetCDFOffset);
        if ((uint)typeOffset > 3)
            throw new SilkCodecException("SILK decoded signal offset is invalid.");
        _ctrl.SigType = typeOffset >> 1;
        _ctrl.QuantOffsetType = typeOffset & 1;
        _typeOffsetPrev = typeOffset;

        _gainIndices[0] = _nFramesDecoded == 0
            ? _rc.Decode(SilkSpec.GainCDF[_ctrl.SigType], SilkSpec.GainCDFOffset)
            : _rc.Decode(SilkSpec.DeltaGainCDF, SilkSpec.DeltaGainCDFOffset);
        for (int i = 1; i < SilkLimits.NbSubfr; i++)
            _gainIndices[i] = _rc.Decode(SilkSpec.DeltaGainCDF, SilkSpec.DeltaGainCDFOffset);
        Gains.Dequantize(_ctrl.GainsQ16, _gainIndices, ref _lastGainIndex, _nFramesDecoded);

        var cb = _ctrl.SigType == SilkLimits.SigTypeVoiced ? _cbVoiced : _cbUnvoiced;
        cb.DecodePath(_rc, _nlsfIndices);
        cb.DecodeVector(_nlsfIndices, _nlsfQ15);
        _ctrl.NlsfInterpCoefQ2 = _rc.Decode(SilkSpec.NlsfInterpolationFactorCDF, SilkSpec.NlsfInterpolationFactorOffset);
        if ((uint)_ctrl.NlsfInterpCoefQ2 > 4)
            throw new SilkCodecException("SILK NLSF interpolation index is invalid.");
        if (_firstFrameAfterReset == 1)
            _ctrl.NlsfInterpCoefQ2 = 4;

        Lpc.NlsfToStableLpc(_nlsfQ15, _ctrl.PredCoef1, _lpcOrder);
        if (_ctrl.NlsfInterpCoefQ2 < 4)
        {
            fixed (int* nlsf = _nlsfQ15)
            fixed (int* prev = _prevNlsfQ15)
            fixed (int* n0 = _nlsf0Q15)
            {
                int coef = _ctrl.NlsfInterpCoefQ2;
                int n = _lpcOrder;
                for (int i = 0; i < n; i++)
                    n0[i] = prev[i] + ((coef * (nlsf[i] - prev[i])) >> 2);
            }
            Lpc.NlsfToStableLpc(_nlsf0Q15, _ctrl.PredCoef0, _lpcOrder);
        }
        else
            SilkUnsafe.Copy(_ctrl.PredCoef0.AsSpan(0, _lpcOrder), _ctrl.PredCoef1.AsSpan(0, _lpcOrder));

        SilkUnsafe.Copy(_prevNlsfQ15.AsSpan(0, _lpcOrder), _nlsfQ15.AsSpan(0, _lpcOrder));
        if (_lossCnt != 0)
        {
            Lpc.ExpandBandwidth(_ctrl.PredCoef0, _lpcOrder, SilkLimits.BweAfterLossQ16);
            Lpc.ExpandBandwidth(_ctrl.PredCoef1, _lpcOrder, SilkLimits.BweAfterLossQ16);
        }

        if (_ctrl.SigType == SilkLimits.SigTypeVoiced)
        {
            ushort[] lagCdf = _fsKhz switch
            {
                8 => SilkSpec.PitchLagNBCDF,
                12 => SilkSpec.PitchLagMBCDF,
                16 => SilkSpec.PitchLagWBCDF,
                _ => SilkSpec.PitchLagSWBCDF
            };
            int lagOff = _fsKhz switch
            {
                8 => SilkSpec.PitchLagNBCDFOffset,
                12 => SilkSpec.PitchLagMBCDFOffset,
                16 => SilkSpec.PitchLagWBCDFOffset,
                _ => SilkSpec.PitchLagSWBCDFOffset
            };
            int lagIndex = _rc.Decode(lagCdf, lagOff);
            int contour = _fsKhz == 8
                ? _rc.Decode(SilkSpec.PitchContourNBCDF, SilkSpec.PitchContourNBCDFOffset)
                : _rc.Decode(SilkSpec.PitchContourCDF, SilkSpec.PitchContourCDFOffset);
            int minLag = 2 * _fsKhz;
            int lag = minLag + lagIndex;
            var lags = _fsKhz == 8 ? SilkSpec.CBLagsStage2 : SilkSpec.CBLagsStage3;
            for (int i = 0; i < SilkLimits.NbSubfr; i++)
            {
                if ((uint)contour >= (uint)lags[i].Length)
                    throw new SilkCodecException("SILK pitch contour is outside its codebook.");
                _ctrl.PitchL[i] = lag + lags[i][contour];
                int interpFlag = _ctrl.NlsfInterpCoefQ2 < 4 ? 1 : 0;
                ValidateNsqPitch(i, _ctrl.PitchL[i], _frameLength + i * _subfrLength, interpFlag);
            }

            _ctrl.PerIndex = _rc.Decode(SilkSpec.LTPPerIndexCDF, SilkSpec.LTPPerIndexCDFOffset);
            if ((uint)_ctrl.PerIndex >= SilkSpec.LTPGainCDFOffsets.Length)
                throw new SilkCodecException("SILK LTP codebook index is invalid.");
            var vq = _ctrl.PerIndex switch
            {
                0 => SilkSpec.LTPGainVq0Q14,
                1 => SilkSpec.LTPGainVq1Q14,
                _ => SilkSpec.LTPGainVq2Q14
            };
            var cdf = _ctrl.PerIndex switch
            {
                0 => SilkSpec.LTPGainCDF0,
                1 => SilkSpec.LTPGainCDF1,
                _ => SilkSpec.LTPGainCDF2
            };
            int cdfOff = SilkSpec.LTPGainCDFOffsets[_ctrl.PerIndex];
            for (int k = 0; k < SilkLimits.NbSubfr; k++)
            {
                int ix = _rc.Decode(cdf, cdfOff);
                if ((uint)ix >= (uint)vq.Length || vq[ix].Length < SilkLimits.LtpOrder)
                    throw new SilkCodecException("SILK LTP vector index is outside its codebook.");
                for (int i = 0; i < SilkLimits.LtpOrder; i++)
                    _ctrl.LtpCoefQ14[k * SilkLimits.LtpOrder + i] = (short)vq[ix][i];
            }
            int scaleIx = _rc.Decode(SilkSpec.LTPscaleCDF, SilkSpec.LTPscaleOffset);
            if ((uint)scaleIx >= (uint)SilkSpec.LTPScalesTableQ14.Length)
                throw new SilkCodecException("SILK LTP scale index is outside its codebook.");
            _ctrl.LtpScaleQ14 = SilkSpec.LTPScalesTableQ14[scaleIx];
        }
        else
        {
            SilkUnsafe.Clear(_ctrl.PitchL);
            SilkUnsafe.Clear(_ctrl.LtpCoefQ14);
            _ctrl.PerIndex = 0;
            _ctrl.LtpScaleQ14 = 0;
        }

        _ctrl.Seed = _rc.Decode(SilkSpec.SeedCDF, SilkSpec.SeedOffset);
        PulseCodec.Decode(_rc, _ctrl.SigType, _ctrl.QuantOffsetType, _frameLength, _pulses, out _ctrl.RateLevelIndex);
        _vadFlag = _rc.Decode(SilkSpec.VadflagCDF, SilkSpec.VadflagOffset);
        _frameTermination = _rc.Decode(SilkSpec.FrameTerminationCDF, SilkSpec.FrameTerminationOffset);
        _rc.GetLength(out int nBytesUsed);
        _nBytesLeft = _rc.BufferLength - nBytesUsed;
        if (_nBytesLeft < 0) _rc.Error = SilkLimits.RangeReadBeyondBuffer;
        if (_nBytesLeft == 0) _rc.CheckAfterDecoding();
    }
}
