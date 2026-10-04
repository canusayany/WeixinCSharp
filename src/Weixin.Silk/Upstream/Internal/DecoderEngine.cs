using SilkCodec.NET.Internal.Resample;
using SilkCodec.NET.Internal.Tables;

namespace SilkCodec.NET.Internal;

internal sealed class DecoderControl
{
    public readonly int[] PitchL = new int[SilkLimits.NbSubfr];
    public readonly int[] GainsQ16 = new int[SilkLimits.NbSubfr];
    public int Seed;
    public readonly short[] PredCoef0 = new short[SilkLimits.MaxLpcOrder];
    public readonly short[] PredCoef1 = new short[SilkLimits.MaxLpcOrder];
    public readonly short[] LtpCoefQ14 = new short[SilkLimits.LtpOrder * SilkLimits.NbSubfr];
    public int LtpScaleQ14;
    public int PerIndex;
    public int RateLevelIndex;
    public int QuantOffsetType;
    public int SigType;
    public int NlsfInterpCoefQ2;
}

internal sealed unsafe partial class DecoderEngine
{
    readonly RangeCoder _rc = new();
    readonly DecoderControl _ctrl = new();
    readonly int[] _sLtpQ16 = new int[2 * SilkLimits.MaxFrameLength];
    readonly int[] _sLpcQ14 = new int[SilkLimits.MaxFrameLength / SilkLimits.NbSubfr + SilkLimits.MaxLpcOrder];
    readonly int[] _excQ10 = new int[SilkLimits.MaxFrameLength];
    readonly int[] _resQ10 = new int[SilkLimits.MaxFrameLength];
    readonly short[] _outBuf = new short[2 * SilkLimits.MaxFrameLength];
    readonly int[] _prevNlsfQ15 = new int[SilkLimits.MaxLpcOrder];
    readonly int[] _hpState = new int[SilkLimits.DecHpOrder];
    readonly int[] _pulses = new int[SilkLimits.MaxFrameLength];
    readonly int[] _nlsfQ15 = new int[SilkLimits.MaxLpcOrder];
    readonly int[] _nlsf0Q15 = new int[SilkLimits.MaxLpcOrder];
    readonly int[] _nlsfIndices = new int[SilkLimits.NlsfMsvqMaxStages];
    readonly int[] _gainIndices = new int[SilkLimits.NbSubfr];
    readonly short[] _sLtp = new short[SilkLimits.MaxFrameLength];
    readonly int[] _filtState = new int[SilkLimits.MaxLpcOrder];
    readonly int[] _vecQ10 = new int[SilkLimits.MaxFrameLength / SilkLimits.NbSubfr];
    readonly SilkResampler _resampler = new();
    readonly PlcState _plc = new();
    readonly CngState _cng = new();

    int _prevInvGainQ16 = 65536;
    int _lagPrev = 100;
    int _lastGainIndex = 1;
    int _typeOffsetPrev;
    int _fsKhz;
    int _prevApiSampleRate;
    int _frameLength;
    int _subfrLength;
    int _lpcOrder = SilkLimits.MaxLpcOrder;
    int _firstFrameAfterReset = 1;
    int _nBytesLeft;
    int _nFramesDecoded;
    int _nFramesInPacket = 1;
    int _moreInternalDecoderFrames;
    int _frameTermination;
    int _vadFlag;
    int _noFecCounter;
    int _inbandFecOffset;
    int _lossCnt;
    int _prevSigType;
    NlsfCodebook _cbVoiced = NlsfCodebook.Voiced16;
    NlsfCodebook _cbUnvoiced = NlsfCodebook.Unvoiced16;
    int[] _hpA = SilkSpec.DecAHP24;
    int[] _hpB = SilkSpec.DecBHP24;

    public int MoreInternalFrames => _moreInternalDecoderFrames;
    internal int RangeErrorCount { get; private set; }
    internal int LastRangeError { get; private set; }
    public int FramesInPacket => _nFramesInPacket;
    public int InBandFecOffset => _inbandFecOffset;
    internal int[] SLtpQ16 => _sLtpQ16;
    internal int[] SLpcQ14 => _sLpcQ14;
    internal int[] ExcQ10 => _excQ10;
    internal int FrameLength => _frameLength;
    internal int SubfrLength => _subfrLength;
    internal int LpcOrder => _lpcOrder;
    internal int FsKhz => _fsKhz;
    internal int PrevSigType => _prevSigType;
    internal int LossCnt => _lossCnt;

    public void Reset()
    {
        _rc.Error = 0;
        SilkUnsafe.Clear(_sLtpQ16);
        SilkUnsafe.Clear(_sLpcQ14);
        SilkUnsafe.Clear(_excQ10);
        SilkUnsafe.Clear(_resQ10);
        SilkUnsafe.Clear(_outBuf);
        SilkUnsafe.Clear(_prevNlsfQ15);
        SilkUnsafe.Clear(_hpState);
        _prevInvGainQ16 = 65536;
        _lagPrev = 100;
        _lastGainIndex = 1;
        _typeOffsetPrev = 0;
        _fsKhz = 0;
        _prevApiSampleRate = 0;
        _firstFrameAfterReset = 1;
        _nFramesDecoded = 0;
        _moreInternalDecoderFrames = 0;
        _lossCnt = 0;
        _prevSigType = 0;
        _noFecCounter = 0;
        _inbandFecOffset = 0;
        RangeErrorCount = 0;
        LastRangeError = 0;
        SetFs(24);
        _cng.Reset(_lpcOrder);
        _plc.Reset(_frameLength);
    }

    public void SetFs(int fsKhz)
    {
        if (_fsKhz == fsKhz) return;
        _fsKhz = fsKhz;
        _frameLength = SilkLimits.FrameLengthMs * fsKhz;
        _subfrLength = _frameLength / SilkLimits.NbSubfr;
        if (fsKhz == 8)
        {
            _lpcOrder = SilkLimits.MinLpcOrder;
            _cbVoiced = NlsfCodebook.Voiced10;
            _cbUnvoiced = NlsfCodebook.Unvoiced10;
        }
        else
        {
            _lpcOrder = SilkLimits.MaxLpcOrder;
            _cbVoiced = NlsfCodebook.Voiced16;
            _cbUnvoiced = NlsfCodebook.Unvoiced16;
        }
        SilkUnsafe.Clear(_sLpcQ14);
        SilkUnsafe.Clear(_outBuf);
        SilkUnsafe.Clear(_prevNlsfQ15);
        _lagPrev = 100;
        _lastGainIndex = 1;
        _prevSigType = 0;
        _firstFrameAfterReset = 1;
        (_hpA, _hpB) = fsKhz switch
        {
            24 => (SilkSpec.DecAHP24, SilkSpec.DecBHP24),
            16 => (SilkSpec.DecAHP16, SilkSpec.DecBHP16),
            12 => (SilkSpec.DecAHP12, SilkSpec.DecBHP12),
            8 => (SilkSpec.DecAHP8, SilkSpec.DecBHP8),
            _ => (SilkSpec.DecAHP24, SilkSpec.DecBHP24)
        };
    }

    public int DecodePayload(ReadOnlySpan<byte> payload, int lostFlag, int apiSampleRate, Span<short> output, out int nSamples)
    {
        nSamples = 0;
        if (_moreInternalDecoderFrames == 0)
            _nFramesDecoded = 0;
        if (_moreInternalDecoderFrames == 0 && lostFlag == 0 && payload.Length > SilkLimits.MaxArithmBytes)
            throw new SilkCodecException("Payload exceeds the decoder range buffer; offline files cannot request PLC.");
        int prevFs = _fsKhz;
        int ret = DecodeFrame(payload, lostFlag, output, out nSamples, out int usedBytes);
        if (usedBytes != 0)
        {
            if (_frameTermination == SilkLimits.MoreFrames && (_nBytesLeft <= 0 || _nFramesDecoded >= 5))
                throw new SilkCodecException("Payload continues beyond five frames or ends before its declared next frame.");
            if (_nBytesLeft > 0 && _frameTermination == SilkLimits.MoreFrames && _nFramesDecoded < 5)
                _moreInternalDecoderFrames = 1;
            else
            {
                _moreInternalDecoderFrames = 0;
                _nFramesInPacket = _nFramesDecoded;
                if (_vadFlag == SilkLimits.VoiceActivity)
                {
                    if (_frameTermination == SilkLimits.LastFrame)
                    {
                        _noFecCounter++;
                        if (_noFecCounter > SilkLimits.NoLbrrThres) _inbandFecOffset = 0;
                    }
                    else if (_frameTermination == SilkLimits.LbrrVer1) { _inbandFecOffset = 1; _noFecCounter = 0; }
                    else if (_frameTermination == SilkLimits.LbrrVer2) { _inbandFecOffset = 2; _noFecCounter = 0; }
                }
            }
        }
        if (apiSampleRate > SilkLimits.MaxApiFsKhz * 1000 || apiSampleRate < 8000)
            return ret == 0 ? -10 : ret;
        if (_fsKhz * 1000 != apiSampleRate)
        {
            short* tmp = stackalloc short[nSamples];
            fixed (short* dst = output)
            {
                SilkUnsafe.Copy(tmp, dst, nSamples);
                if (prevFs != _fsKhz || _prevApiSampleRate != apiSampleRate)
                    _resampler.Init(_fsKhz * 1000, apiSampleRate);
                nSamples = _resampler.Process(new ReadOnlySpan<short>(tmp, nSamples), output);
            }
        }
        _prevApiSampleRate = apiSampleRate;
        return ret;
    }

    int DecodeFrame(ReadOnlySpan<byte> payload, int action, Span<short> output, out int nOut, out int usedBytes)
    {
        usedBytes = 0;
        int L = _frameLength > 1 ? _frameLength : 1;
        _ctrl.LtpScaleQ14 = 0;
        if (action == 0)
        {
            int fsOld = _fsKhz;
            if (_nFramesDecoded == 0)
                _rc.InitDecoder(payload);
            DecodeParameters();
            if (_rc.Error != 0)
            {
                RangeErrorCount++;
                LastRangeError = _rc.Error;
                _nBytesLeft = 0;
                action = 1;
                SetFs(fsOld == 0 ? 24 : fsOld);
                usedBytes = _rc.BufferLength;
                nOut = _frameLength;
                _plc.Conceal(this, _ctrl, output, _frameLength);
                _lossCnt++;
                return _rc.Error == SilkLimits.RangePayloadTooLong ? -11 : -12;
            }
            usedBytes = _rc.BufferLength - _nBytesLeft;
            _nFramesDecoded++;
            L = _frameLength;
            InverseNsq(output);
            _plc.Update(_ctrl, _fsKhz, _lpcOrder, _subfrLength);
            _lossCnt = 0;
            _prevSigType = _ctrl.SigType;
            _firstFrameAfterReset = 0;
        }
        if (action == 1)
        {
            _plc.Conceal(this, _ctrl, output, _frameLength);
            _lossCnt++;
            L = _frameLength;
        }
        output.Slice(0, L).CopyTo(_outBuf);
        _plc.GlueFrames(output, L, _lossCnt);
        _cng.Apply(this, _ctrl, output, L, _lossCnt, _vadFlag, _lpcOrder, _fsKhz, _subfrLength, _prevNlsfQ15, _excQ10);
        Lpc.Biquad(output.Slice(0, L), _hpB, _hpA, _hpState, L);
        nOut = L;
        _lagPrev = _ctrl.PitchL[SilkLimits.NbSubfr - 1];
        return 0;
    }
}
