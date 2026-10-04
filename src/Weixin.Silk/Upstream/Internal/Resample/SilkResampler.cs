using SilkCodec.NET.Internal.Tables;

namespace SilkCodec.NET.Internal.Resample;

internal sealed unsafe class SilkResampler
{
    int _inHz = 24000;
    int _outHz = 24000;
    readonly int[] _sDown2 = new int[2];
    readonly int[] _sUp2 = new int[6];
    bool _ready;

    public void Init(int inHz, int outHz)
    {
        _inHz = inHz;
        _outHz = outHz;
        SilkUnsafe.Clear(_sDown2);
        SilkUnsafe.Clear(_sUp2);
        _ready = true;
    }

    public int Process(ReadOnlySpan<short> input, Span<short> output)
    {
        if (!_ready) Init(_inHz, _outHz);
        if (_inHz == _outHz)
        {
            input.CopyTo(output);
            return input.Length;
        }
        if (_outHz == _inHz * 2)
        {
            Up2(input, output);
            return input.Length * 2;
        }
        if (_inHz == _outHz * 2)
        {
            Down2(input, output);
            return input.Length / 2;
        }
        return Linear(input, output);
    }

    void Up2(ReadOnlySpan<short> input, Span<short> output)
    {
        int len = input.Length;
        int s0 = _sUp2[0], s1 = _sUp2[1];
        int c0 = SilkSpec.ResamplerUp2Hq0[0];
        int c1 = SilkSpec.ResamplerUp2Hq1[0];
        fixed (short* pin = input)
        fixed (short* pout = output)
        {
            for (int k = 0; k < len; k++)
            {
                int in32 = pin[k] << 10;
                int y = in32 - s0;
                int x = Fx.SMulWb(y, c0);
                int out32 = s0 + x;
                s0 = in32 + x;
                pout[2 * k] = (short)Fx.Sat16(Fx.RShiftRound(out32, 10));
                y = in32 - s1;
                x = Fx.SMulWb(y, c1);
                out32 = s1 + x;
                s1 = in32 + x;
                pout[2 * k + 1] = (short)Fx.Sat16(Fx.RShiftRound(out32, 10));
            }
        }
        _sUp2[0] = s0;
        _sUp2[1] = s1;
    }

    void Down2(ReadOnlySpan<short> input, Span<short> output)
    {
        int nOut = input.Length / 2;
        int s0 = _sDown2[0], s1 = _sDown2[1];
        int c0 = SilkSpec.ResamplerDown20;
        int c1 = SilkSpec.ResamplerUp2Lq0;
        fixed (short* pin = input)
        fixed (short* pout = output)
        {
            for (int k = 0; k < nOut; k++)
            {
                int in32 = pin[2 * k] << 10;
                int y = in32 - s0;
                int x = Fx.SMulWb(y, c0);
                int out32 = s0 + x;
                s0 = in32 + x;
                in32 = pin[2 * k + 1] << 10;
                y = in32 - s1;
                x = Fx.SMulWb(y, c1);
                out32 += s1 + x;
                s1 = in32 + x;
                pout[k] = (short)Fx.Sat16(Fx.RShiftRound(out32, 11));
            }
        }
        _sDown2[0] = s0;
        _sDown2[1] = s1;
    }

    int Linear(ReadOnlySpan<short> input, Span<short> output)
    {
        int inLen = input.Length;
        if (inLen == 0) return 0;
        int outLen = (int)((long)inLen * _outHz / _inHz);
        int last = inLen - 1;
        int inHz = _inHz;
        int outHz = _outHz;
        fixed (short* pin = input)
        fixed (short* pout = output)
        {
            for (int i = 0; i < outLen; i++)
            {
                long pos = (long)i * inHz;
                int idx = (int)(pos / outHz);
                int frac = (int)(pos % outHz);
                int aIdx = idx < last ? idx : last;
                int bIdx = idx + 1 < last ? idx + 1 : last;
                int a = pin[aIdx];
                int b = pin[bIdx];
                pout[i] = (short)(a + (int)(((long)(b - a) * frac) / outHz));
            }
        }
        return outLen;
    }
}
