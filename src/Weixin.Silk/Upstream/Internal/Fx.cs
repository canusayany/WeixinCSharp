using System.Runtime.CompilerServices;

namespace SilkCodec.NET.Internal;

/// <summary>
/// Two's-complement fixed-point helpers used by the Silk v3 bitstream DSP.
/// Arithmetic matches the wrapping and rounding of the original 32-bit SILK SDK.
/// </summary>
internal static class Fx
{
    public const int Int16Min = -32768;
    public const int Int16Max = 32767;
    public const int Int32Min = unchecked((int)0x80000000);
    public const int Int32Max = 0x7FFFFFFF;
    const MethodImplOptions Hot = SilkUnsafe.Hot;

    [MethodImpl(Hot)]
    public static int Sat16(int a) => a > Int16Max ? Int16Max : a < Int16Min ? Int16Min : a;

    [MethodImpl(Hot)]
    public static int Sat32(long a) => a > Int32Max ? Int32Max : a < Int32Min ? Int32Min : (int)a;

    [MethodImpl(Hot)]
    public static int AddSat32(int a, int b)
    {
        int sum = unchecked(a + b);
        if (((a ^ b) & Int32Min) == 0)
        {
            if (((a ^ sum) & Int32Min) != 0)
                return a < 0 ? Int32Min : Int32Max;
        }
        return sum;
    }

    [MethodImpl(Hot)]
    public static int SubSat32(int a, int b)
    {
        int diff = unchecked(a - b);
        if (((a ^ b) & Int32Min) != 0)
        {
            if (((a ^ diff) & Int32Min) != 0)
                return a < 0 ? Int32Min : Int32Max;
        }
        return diff;
    }

    [MethodImpl(Hot)]
    public static int AddOverflow(int a, int b) => unchecked((int)((uint)a + (uint)b));

    [MethodImpl(Hot)]
    public static int SubOverflow(int a, int b) => unchecked((int)((uint)a - (uint)b));

    [MethodImpl(Hot)]
    public static int Mul(int a, int b) => unchecked(a * b);

    [MethodImpl(Hot)]
    public static uint MulU(uint a, uint b) => unchecked(a * b);

    [MethodImpl(Hot)]
    public static long Mull(int a, int b) => (long)a * b;

    [MethodImpl(Hot)]
    public static int SMulBb(int a, int b) => unchecked((short)a * (short)b);

    [MethodImpl(Hot)]
    public static int SMlaBb(int a, int b, int c) => unchecked(a + (short)b * (short)c);

    [MethodImpl(Hot)]
    public static int SMlaBbOv(int a, int b, int c) => AddOverflow(a, SMulBb(b, c));

    [MethodImpl(Hot)]
    public static int SMulBt(int a, int b) => unchecked((short)a * (b >> 16));

    [MethodImpl(Hot)]
    public static int SMlaBt(int a, int b, int c) => unchecked(a + (short)b * (c >> 16));

    [MethodImpl(Hot)]
    public static int SMulWb(int a, int b)
    {
        short b16 = (short)b;
        return unchecked(((a >> 16) * b16) + (((a & 0x0000FFFF) * b16) >> 16));
    }

    [MethodImpl(Hot)]
    public static int SMlaWb(int a, int b, int c) => unchecked(a + SMulWb(b, c));

    [MethodImpl(Hot)]
    public static int SMulWt(int a, int b)
    {
        int bt = b >> 16;
        return unchecked(((a >> 16) * bt) + (((a & 0x0000FFFF) * bt) >> 16));
    }

    [MethodImpl(Hot)]
    public static int SMlaWt(int a, int b, int c) => unchecked(a + SMulWt(b, c));

    [MethodImpl(Hot)]
    public static int SMulTt(int a, int b) => unchecked((a >> 16) * (b >> 16));

    [MethodImpl(Hot)]
    public static int SMlaTtOv(int a, int b, int c) => AddOverflow(a, SMulTt(b, c));

    [MethodImpl(Hot)]
    public static int RShiftRound(int a, int shift)
        => shift == 1 ? (a >> 1) + (a & 1) : ((a >> (shift - 1)) + 1) >> 1;

    [MethodImpl(Hot)]
    public static int RShiftRound64(long a, int shift)
        => (int)(shift == 1 ? (a >> 1) + (a & 1) : ((a >> (shift - 1)) + 1) >> 1);

    [MethodImpl(Hot)]
    public static int SMulWw(int a, int b) => unchecked(SMulWb(a, b) + a * RShiftRound(b, 16));

    [MethodImpl(Hot)]
    public static int SMlaWw(int a, int b, int c) => unchecked(SMlaWb(a, b, c) + b * RShiftRound(c, 16));

    [MethodImpl(Hot)]
    public static int Smmul(int a, int b) => (int)(Mull(a, b) >> 32);

    [MethodImpl(Hot)]
    public static int ShiftLeftOverflow(int a, int shift) => unchecked((int)((uint)a << shift));

    [MethodImpl(Hot)]
    public static int ShiftLeftSat(int a, int shift)
    {
        int min = Int32Min >> shift;
        int max = Int32Max >> shift;
        if (a > max) a = max;
        if (a < min) a = min;
        return a << shift;
    }

    [MethodImpl(Hot)]
    public static int Limit(int a, int lo, int hi)
    {
        if (lo > hi) (lo, hi) = (hi, lo);
        if (a > hi) return hi;
        if (a < lo) return lo;
        return a;
    }

    [MethodImpl(Hot)]
    public static int Abs32(int a) => a >= 0 ? a : unchecked(-a);

    [MethodImpl(Hot)]
    public static int Clz32(int in32)
    {
        uint v = (uint)in32;
        return v == 0 ? 32 : System.Numerics.BitOperations.LeadingZeroCount(v);
    }

    [MethodImpl(Hot)]
    public static int Clz16(short in16) => Clz32((ushort)in16) - 16;

    [MethodImpl(Hot)]
    public static void ClzFrac(int value, out int lz, out int fracQ7)
    {
        lz = Clz32(value);
        int rot = 24 - lz;
        uint x = (uint)value;
        uint rotated = rot <= 0
            ? (x << (-rot)) | (x >> (32 + rot))
            : (x >> rot) | (x << (32 - rot));
        fracQ7 = (int)(rotated & 0x7F);
    }

    [MethodImpl(Hot)]
    public static int SqrtApprox(int x)
    {
        if (x <= 0) return 0;
        ClzFrac(x, out int lz, out int fracQ7);
        int y = (lz & 1) != 0 ? 32768 : 46214;
        y >>= lz >> 1;
        y = SMlaWb(y, y, SMulBb(213, fracQ7));
        return y;
    }

    [MethodImpl(Hot)]
    public static int Rand(int seed) => AddOverflow(907633515, unchecked((int)((uint)seed * 196314165u)));

    [MethodImpl(Hot)]
    public static int Lin2Log(int inLin)
    {
        ClzFrac(inLin, out int lz, out int fracQ7);
        return ((31 - lz) << 7) + SMlaWb(fracQ7, Mul(fracQ7, 128 - fracQ7), 179);
    }

    [MethodImpl(Hot)]
    public static int Log2Lin(int inLogQ7)
    {
        if (inLogQ7 < 0) return 0;
        if (inLogQ7 >= (31 << 7)) return Int32Max;
        int outv = 1 << (inLogQ7 >> 7);
        int fracQ7 = inLogQ7 & 0x7F;
        int adj = SMlaWb(fracQ7, Mul(fracQ7, 128 - fracQ7), -174);
        if (inLogQ7 < 2048)
            outv += (Mul(outv, adj) >> 7);
        else
            outv = unchecked(outv + (outv >> 7) * adj);
        return outv;
    }

    public static int Inverse32VarQ(int b32, int qRes)
    {
        int bHead = Clz32(Abs32(b32)) - 1;
        int bNrm = b32 << bHead;
        int bInv = (Int32Max >> 2) / (bNrm >> 16);
        int result = bInv << 16;
        int errQ32 = ShiftLeftOverflow(-SMulWb(bNrm, bInv), 3);
        result = SMlaWw(result, errQ32, bInv);
        int lshift = 61 - bHead - qRes;
        if (lshift <= 0) return ShiftLeftSat(result, -lshift);
        if (lshift < 32) return result >> lshift;
        return 0;
    }

    public static int Div32VarQ(int a32, int b32, int qRes)
    {
        int aHead = Clz32(Abs32(a32)) - 1;
        int aNrm = a32 << aHead;
        int bHead = Clz32(Abs32(b32)) - 1;
        int bNrm = b32 << bHead;
        int bInv = (Int32Max >> 2) / (bNrm >> 16);
        int result = SMulWb(aNrm, bInv);
        aNrm -= ShiftLeftOverflow(Smmul(bNrm, result), 3);
        result = SMlaWb(result, aNrm, bInv);
        int lshift = 29 + aHead - bHead - qRes;
        if (lshift <= 0) return ShiftLeftSat(result, -lshift);
        if (lshift < 32) return result >> lshift;
        return 0;
    }
}
