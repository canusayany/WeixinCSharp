/*
 * Copyright @ 2015 Atlassian Pty Ltd
 *
 * Licensed under the Apache License, Version 2.0 (the "License");
 * you may not use this file except in compliance with the License.
 * You may obtain a copy of the License at
 *
 *     http://www.apache.org/licenses/LICENSE-2.0
 *
 * Unless required by applicable law or agreed to in writing, software
 * distributed under the License is distributed on an "AS IS" BASIS,
 * WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
 * See the License for the specific language governing permissions and
 * limitations under the License.
 */

using System;
using System.Numerics;
using static SilkCodec.NET.Managed.Macros;

namespace SilkCodec.NET.Managed;

/**
 *
 * @author Jing Dai
 * @author Dingxin Xu
 */
internal static class SigProcFIX
{
    /**
     * max order of the LPC analysis input schur() and k2a().
     */
    internal const int SKP_Silk_MAX_ORDER_LPC =           16; /* max order of the LPC analysis input schur() and k2a()    */
    /**
     * max input length to the correlation.
     */
    internal const int SKP_Silk_MAX_CORRELATION_LENGTH =  640;/* max input length to the correlation                   */

    /* Pitch estimator */
    internal const int SKP_Silk_PITCH_EST_MIN_COMPLEX =       0;
    internal const int SKP_Silk_PITCH_EST_MID_COMPLEX =       1;
    internal const int SKP_Silk_PITCH_EST_MAX_COMPLEX =       2;

    /* parameter defining the size and accuracy of the piecewise linear  */
    /* cosine approximatin table.                                        */
    internal const int LSF_COS_TAB_SZ_FIX =     128;
    /* rom table with cosine values */
//    (to see rom table value, refer to LSFCosTable.java)
    /**
     * Rotate a32 right by 'rot' bits. Negative rot values result input rotating
     * left. Output is 32bit int.
     *
     * @param a32
     * @param rot
     * @return
     */
    internal static int SKP_ROR32( int a32, int rot )
    {
        unchecked
        {
        if(rot <= 0)
            return ((a32 << -rot) | (a32 >>> (32 + rot)));
        else
            return ((a32 << (32 - rot)) | (a32 >>> rot));
            }
}

    /* fixed point */

    /**
     * (a32 * b32) output have to be 32bit int
     */
    internal static int SKP_MUL(int a32, int b32)
    {
        unchecked
        {
        return a32*b32;
            }
}

    /**
     * (a32 * b32) output have to be 32bit uint.
     * @param a32
     * @param b32
     * @return
     */
    internal static long SKP_MUL_uint(long a32, long b32)
    {
        unchecked
        {
        return a32*b32;
            }
}

    /**
     *  a32 + (b32 * c32) output have to be 32bit int
     * @param a32
     * @param b32
     * @param c32
     * @return
     */
    internal static int SKP_MLA(int a32, int b32, int c32)
    {
        unchecked
        {
        return a32 + b32*c32;
            }
}

    /**
     *  a32 + (b32 * c32) output have to be 32bit uint
     * @param a32
     * @param b32
     * @param c32
     * @return
     */
    internal static long SKP_MLA_uint(long a32, long b32, long c32)
    {
        unchecked
        {
        return a32 + b32*c32;
            }
}

    /**
     * ((a32 >> 16)  * (b32 >> 16)) output have to be 32bit int
     * @param a32
     * @param b32
     * @return
     */
    internal static int SKP_SMULTT(int a32, int b32)
    {
        unchecked
        {
        return (((a32) >> 16) * ((b32) >> 16));
            }
}

    /**
     *  a32 + ((b32 >> 16)  * (c32 >> 16)) output have to be 32bit int
     * @param a32
     * @param b32
     * @param c32
     * @return
     */
    internal static int SKP_SMLATT(int a32, int b32, int c32)
    {
        unchecked
        {
        return (a32) + ((b32) >> 16) * ((c32) >> 16);
            }
}

    /**
     * SKP_ADD64((a64),(SKP_int64)((SKP_int32)(b16) * (SKP_int32)(c16))).
     * @param a64
     * @param b16
     * @param c16
     * @return
     */
    internal static long SKP_SMLALBB(long a64, short b16, short c16)
    {
        unchecked
        {
        return (a64) + ((long)(b16) * (long)(c16));
            }
}

    /**
     * (a32 * b32)
     * @param a32
     * @param b32
     * @return
     */
    internal static long SKP_SMULL(int a32, int b32)
    {
        unchecked
        {
        return ((long)(a32) * /*(long)*/(b32));
            }
}

    // multiply-accumulate macros that allow overflow input the addition (ie, no asserts input debug mode)
    /**
     * SKP_MLA(a32, b32, c32).
     */
    internal static int SKP_MLA_ovflw(int a32, int b32, int c32)
    {
        unchecked
        {
        return a32 + b32*c32;
            }
}

    /**
     * SKP_SMLABB(a32, b32, c32)
     * @param a32
     * @param b32
     * @param c32
     * @return
     */
    internal static int SKP_SMLABB_ovflw(int a32, int b32, int c32)
    {
        unchecked
        {
        return ((a32) + (((short)(b32))) * ((short)(c32)));
            }
}
    /**
     * SKP_SMLABT(a32, b32, c32)
     * @param a32
     * @param b32
     * @param c32
     * @return
     */
    internal static int SKP_SMLABT_ovflw(int a32, int b32, int c32)
    {
        unchecked
        {
        return ((a32) + (((short)(b32))) * ((c32) >> 16));
            }
}
    /**
     * SKP_SMLATT(a32, b32, c32)
     * @param a32
     * @param b32
     * @param c32
     * @return
     */
    internal static int SKP_SMLATT_ovflw(int a32, int b32, int c32)
    {
        unchecked
        {
        return (a32) + ((b32) >> 16) * ((c32) >> 16);
            }
}
    /**
     * SKP_SMLAWB(a32, b32, c32)
     * @param a32
     * @param b32
     * @param c32
     * @return
     */
    internal static int SKP_SMLAWB_ovflw(int a32, int b32, int c32)
    {
        unchecked
        {
        return ((a32) + ((((b32) >> 16) * ((short)(c32))) + ((((b32) & 0x0000FFFF) * ((short)(c32))) >> 16)));
            }
}
    /**
     * SKP_SMLAWT(a32, b32, c32)
     * @param a32
     * @param b32
     * @param c32
     * @return
     */
    internal static int SKP_SMLAWT_ovflw(int a32, int b32, int c32)
    {
        unchecked
        {
        return ((a32) + (((b32) >> 16) * ((c32) >> 16)) + ((((b32) & 0x0000FFFF) * ((c32) >> 16)) >> 16));
            }
}

    /**
     * ((a64)/(b32))
     * TODO: rewrite it as a set of SKP_DIV32.
     */
    internal static long SKP_DIV64_32(long a64, int b32)
    {
        unchecked
        {
        return a64/b32;
            }
}

    /**
     * ((int)((a32) / (b16)))
     * @param a32
     * @param b16
     * @return
     */
    internal static int SKP_DIV32_16(int a32, short b16)
    {
        unchecked
        {
        return (((a32) / (b16)));
            }
}
    /**
     * ((SKP_int32)((a32) / (b32)))
     * @param a32
     * @param b32
     * @return
     */
    internal static int SKP_DIV32(int a32, int b32)
    {
        unchecked
        {
        return (((a32) / (b32)));
            }
}

    // These macros enables checking for overflow input SKP_Silk_API_Debug.h
    /**
     * ((a) + (b))
     */
    internal static short SKP_ADD16(short a, short b)
    {
        unchecked
        {
        return (short)(a+b);
            }
}
    /**
     * ((a) + (b))
     * @param a
     * @param b
     * @return
     */
    internal static int SKP_ADD32(int a, int b)
    {
        unchecked
        {
        return a+b;
            }
}
    /**
     * ((a) + (b))
     * @param a
     * @param b
     * @return
     */
    internal static long SKP_ADD64(long a, long b)
    {
        unchecked
        {
        return a+b;
            }
}

    /**
     * ((a) - (b))
     * @param a
     * @param b
     * @return
     */
    internal static short SKP_SUB16(short a, short b)
    {
        unchecked
        {
        return (short)(a-b);
            }
}
    /**
     * ((a) - (b))
     * @param a
     * @param b
     * @return
     */
    internal static int SKP_SUB32(int a, int b)
    {
        unchecked
        {
        return a-b;
            }
}
    /**
     * ((a) - (b))
     * @param a
     * @param b
     * @return
     */
    internal static long SKP_SUB64(long a, long b)
    {
        unchecked
        {
        return a-b;
            }
}

    internal static int SKP_SAT8(int a)
    {
        unchecked
        {
        return ((a) > sbyte.MaxValue ? sbyte.MaxValue  : ((a) < sbyte.MinValue ? sbyte.MinValue  : (a)));
            }
}

    internal static int SKP_SAT16(int a)
    {
        unchecked
        {
        return ((a) > short.MaxValue ? short.MaxValue : ((a) < short.MinValue ? short.MinValue : (a)));
            }
}

    internal static long SKP_SAT32(long a)
    {
        unchecked
        {
        return ((a) > int.MaxValue ? int.MaxValue : ((a) < int.MinValue ? int.MinValue : (a)));
            }
}

    /**
     * (a)
     * @param a
     * @return
     */
    internal static byte SKP_CHECK_FIT8(int a)
    {
        unchecked
        {
        return (byte)a;
            }
}
    /**
     * (a)
     * @param a
     * @return
     */
    internal static short SKP_CHECK_FIT16(int a)
    {
        unchecked
        {
        return (short)a;
            }
}
    /**
     * (a)
     * @param a
     * @return
     */
    internal static int SKP_CHECK_FIT32(int a)
    {
        unchecked
        {
        return a;
            }
}

    internal static short SKP_ADD_SAT16(short a, short b)
    {
        unchecked
        {
        return (short)SKP_SAT16( (a) + (b) );
            }
}

    internal static long SKP_ADD_SAT64(long a, long b)
    {
        unchecked
        {
        if( ((a + b) & long.MinValue) == 0 )
            return ((a & b) & long.MinValue) != 0 ? long.MinValue : a+b;
        else
            return ((a | b) & long.MinValue) == 0 ? long.MaxValue : a+b;
            }
}

    internal static short SKP_SUB_SAT16(short a, short b)
    {
        unchecked
        {
        return (short)SKP_SAT16( (a) - (b) );
            }
}

    internal static long SKP_SUB_SAT64(long a, long b)
    {
        unchecked
        {
        if( ((a - b) & long.MinValue) == 0 )
            return ( a & (b^long.MinValue) & long.MinValue) != 0 ? long.MinValue : a-b;
        else
            return ( (a^long.MinValue) & b & long.MinValue) != 0 ? long.MaxValue : a-b;
            }
}

    /* Saturation for positive input values */
    internal static long SKP_POS_SAT32(long a)
    {
        unchecked
        {
        return ((a) > int.MaxValue ? int.MaxValue : (a));
            }
}

    /* Add with saturation for positive input values */
    internal static sbyte SKP_ADD_POS_SAT8(sbyte a, sbyte b)
    {
        unchecked
        {
        return ((a+b) & 0x80) != 0? sbyte.MaxValue  : unchecked((sbyte)(a+b));
            }
}
    internal static short SKP_ADD_POS_SAT16(short a, short b)
    {
        unchecked
        {
        return ((a+b) & 0x8000) != 0 ? short.MaxValue : (short)(a+b);
            }
}
    internal static int SKP_ADD_POS_SAT32(int a, int b)
    {
        unchecked
        {
        return ((((a)+(b)) & 0x80000000) != 0 ? int.MaxValue : ((a)+(b)));
            }
}
    internal static long SKP_ADD_POS_SAT64(long a, long b)
    {
        unchecked
        {
        return ((((a)+(b)) & long.MinValue) != 0 ? long.MaxValue : ((a)+(b)));
            }
}

    /**
     * ((a)<<(shift))                // shift >= 0, shift < 8
     * @param a
     * @param shift
     * @return
     */
    internal static byte SKP_LSHIFT8(byte a, int shift)
    {
        unchecked
        {
        return (byte)(a<<shift);
            }
}
    /**
     * ((a)<<(shift))                // shift >= 0, shift < 16
     * @param a
     * @param shift
     * @return
     */
    internal static short SKP_LSHIFT16(short a, int shift)
    {
        unchecked
        {
        return (short)(a<<shift);
            }
}
    /**
     * ((a)<<(shift))                // shift >= 0, shift < 32
     * @param a
     * @param shift
     * @return
     */
    internal static int SKP_LSHIFT32(int a, int shift)
    {
        unchecked
        {
        return a<<shift;
            }
}
    /**
     * ((a)<<(shift))                // shift >= 0, shift < 64
     * @param a
     * @param shift
     * @return
     */
    internal static long SKP_LSHIFT64(long a, int shift)
    {
        unchecked
        {
        return a<<shift;
            }
}
    /**
     * (a, shift)               SKP_LSHIFT32(a, shift)        // shift >= 0, shift < 32
     * @param a
     * @param shift
     * @return
     */
    internal static int SKP_LSHIFT(int a, int shift)
    {
        unchecked
        {
        return a<<shift;
            }
}

    /**
     * ((a)>>(shift))                // shift >= 0, shift < 8
     * @param a
     * @param shift
     * @return
     */
    internal static byte SKP_RSHIFT8(byte a, int shift)
    {
        unchecked
        {
        return (byte)(a>>shift);
            }
}
    /**
     * ((a)>>(shift))                // shift >= 0, shift < 16
     * @param a
     * @param shift
     * @return
     */
    internal static short SKP_RSHIFT16(short a, int shift)
    {
        unchecked
        {
        return (short)(a>>shift);
            }
}
    /**
     * ((a)>>(shift))                // shift >= 0, shift < 32
     * @param a
     * @param shift
     * @return
     */
    internal static int SKP_RSHIFT32(int a, int shift)
    {
        unchecked
        {
        return a>>shift;
            }
}
    /**
     * ((a)>>(shift))                // shift >= 0, shift < 64
     * @param a
     * @param shift
     * @return
     */
    internal static long SKP_RSHIFT64(long a, int shift)
    {
        unchecked
        {
        return a>>shift;
            }
}
    /**
     * SKP_RSHIFT32(a, shift)        // shift >= 0, shift < 32
     * @param a
     * @param shift
     * @return
     */
    internal static int SKP_RSHIFT(int a, int shift)
    {
        unchecked
        {
        return a>>shift;
            }
}

    /* saturates before shifting */
    internal static short SKP_LSHIFT_SAT16(short a, int shift)
    {
        unchecked
        {
        return SKP_LSHIFT16( SKP_LIMIT_16( a, (short)(short.MinValue>>shift), (short)(short.MaxValue>>shift) ), shift );
            }
}

    internal static int SKP_LSHIFT_SAT32(int a, int shift)
    {
        unchecked
        {
        return SKP_LSHIFT32( SKP_LIMIT( a, int.MinValue>>shift, int.MaxValue>>shift ), shift );
            }
}

    /**
     * ((a)<<(shift))        // shift >= 0, allowed to overflow
     * @param a
     * @param shift
     * @return
     */
    internal static int SKP_LSHIFT_ovflw(int a, int shift)
    {
        unchecked
        {
        return a<<shift;
            }
}
    /**
     * ((a)<<(shift))        // shift >= 0
     * @param a
     * @param shift
     * @return
     */
    internal static int SKP_LSHIFT_uint(int a, int shift)
    {
        unchecked
        {
        return a<<shift;
            }
}
    /**
     * ((a)>>(shift))        // shift >= 0
     * @param a
     * @param shift
     * @return
     */
    internal static int SKP_RSHIFT_uint(int a, int shift)
    {
        unchecked
        {
        return a>>>shift;
            }
}

    /**
     * ((a) + SKP_LSHIFT((b), (shift)))            // shift >= 0
     * @param a
     * @param b
     * @param shift
     * @return
     */
    internal static int SKP_ADD_LSHIFT(int a, int b, int shift)
    {
        unchecked
        {
        return a + (b<<shift);
            }
}
    /**
     * SKP_ADD32((a), SKP_LSHIFT32((b), (shift)))    // shift >= 0
     * @param a
     * @param b
     * @param shift
     * @return
     */
    internal static int SKP_ADD_LSHIFT32(int a, int b, int shift)
    {
        unchecked
        {
        return a + (b<<shift);
            }
}
    /**
     * ((a) + SKP_LSHIFT_uint((b), (shift)))        // shift >= 0
     * @param a
     * @param b
     * @param shift
     * @return
     */
    internal static int SKP_ADD_LSHIFT_uint(int a, int b, int shift)
    {
        unchecked
        {
        return a + (b<<shift);
            }
}
    /**
     * ((a) + SKP_RSHIFT((b), (shift)))            // shift >= 0
     * @param a
     * @param b
     * @param shift
     * @return
     */
    internal static int SKP_ADD_RSHIFT(int a, int b, int shift)
    {
        unchecked
        {
        return a + (b>>shift);
            }
}
    /**
     * SKP_ADD32((a), SKP_RSHIFT32((b), (shift)))    // shift >= 0
     * @param a
     * @param b
     * @param shift
     * @return
     */
    internal static int SKP_ADD_RSHIFT32(int a, int b, int shift)
    {
        unchecked
        {
        return a + (b>>shift);
            }
}
    /**
     * ((a) + SKP_RSHIFT_uint((b), (shift)))        // shift >= 0
     * @param a
     * @param b
     * @param shift
     * @return
     */
    internal static int SKP_ADD_RSHIFT_uint(int a, int b, int shift)
    {
        unchecked
        {
        return a + (b>>>shift);
            }
}
    /**
     * SKP_SUB32((a), SKP_LSHIFT32((b), (shift)))    // shift >= 0
     * @param a
     * @param b
     * @param shift
     * @return
     */
    internal static int SKP_SUB_LSHIFT32(int a, int b, int shift)
    {
        unchecked
        {
        return a - (b<<shift);
            }
}
    /**
     * SKP_SUB32((a), SKP_RSHIFT32((b), (shift)))    // shift >= 0
     * @param a
     * @param b
     * @param shift
     * @return
     */
    internal static int SKP_SUB_RSHIFT32(int a, int b, int shift)
    {
        unchecked
        {
        return a - (b>>shift);
            }
}

    /* Requires that shift > 0 */
    /**
     * ((shift) == 1 ? ((a) >> 1) + ((a) & 1) : (((a) >> ((shift) - 1)) + 1) >> 1)
     */
    internal static int SKP_RSHIFT_ROUND(int a, int shift)
    {
        unchecked
        {
        return shift == 1 ? (a >> 1) + (a & 1) : ((a >> (shift - 1)) + 1) >> 1;
            }
}
    /**
     * ((shift) == 1 ? ((a) >> 1) + ((a) & 1) : (((a) >> ((shift) - 1)) + 1) >> 1)
     * @param a
     * @param shift
     * @return
     */
    internal static long SKP_RSHIFT_ROUND64(long a, int shift)
    {
        unchecked
        {
        return shift == 1 ? (a >> 1) + (a & 1) : ((a >> (shift - 1)) + 1) >> 1;
            }
}

    /* Number of rightshift required to fit the multiplication */
    internal static int SKP_NSHIFT_MUL_32_32(int a, int b)
    {
        unchecked
        {
        return -(31- (32-SKP_Silk_CLZ32(Math.Abs(a)) + (32-SKP_Silk_CLZ32(Math.Abs(b)))));
            }
}
    internal static int SKP_NSHIFT_MUL_16_16(short a, short b)
    {
        unchecked
        {
        return -(15- (16-SKP_Silk_CLZ16((short)Math.Abs(a)) + (16-SKP_Silk_CLZ16((short)Math.Abs(b)))));
            }
}


    internal static int SKP_min(int a, int b)
    {
        unchecked
        {
        return a<b ? a:b;
            }
}
    internal static int SKP_max(int a, int b)
    {
        unchecked
        {
        return a>b ? a:b;
            }
}

    /* Macro to convert floating-point constants to fixed-point */
    /**
     * ((int)((C) * (1 << (Q)) + 0.5))
     */
    internal static int SKP_FIX_CONST( float C, int Q )
    {
        unchecked
        {
        return (int)(C * (1 << Q) + 0.5);
            }
}

    /* SKP_min() versions with typecast input the function call */
    internal static int SKP_min_int(int a, int b)
    {
        unchecked
        {
        return (((a) < (b)) ? (a) : (b));
            }
}
    internal static short SKP_min_16(short a, short b)
    {
        unchecked
        {
        return (((a) < (b)) ? (a) : (b));
            }
}
    internal static int SKP_min_32(int a, int b)
    {
        unchecked
        {
        return (((a) < (b)) ? (a) : (b));
            }
}
    internal static long SKP_min_64(long a, long b)
    {
        unchecked
        {
        return (((a) < (b)) ? (a) : (b));
            }
}

    /* SKP_min() versions with typecast input the function call */
    internal static int SKP_max_int(int a, int b)
    {
        unchecked
        {
        return (((a) > (b)) ? (a) : (b));
            }
}
    internal static short SKP_max_16(short a, short b)
    {
        unchecked
        {
        return (((a) > (b)) ? (a) : (b));
            }
}
    internal static int SKP_max_32(int a, int b)
    {
        unchecked
        {
        return (((a) > (b)) ? (a) : (b));
            }
}
    internal static long SKP_max_64(long a, long b)
    {
        unchecked
        {
        return (((a) > (b)) ? (a) : (b));
            }
}

    internal static int SKP_LIMIT( int a, int limit1, int limit2)
    {
        unchecked
        {
        if( limit1 > limit2 )
            return a > limit1 ? limit1 : (a < limit2 ? limit2 : a);
        else
            return a > limit2 ? limit2 : (a < limit1 ? limit1 : a);
            }
}
    internal static float SKP_LIMIT( float a, float limit1, float limit2)
    {
        unchecked
        {
        if( limit1 > limit2 )
            return a > limit1 ? limit1 : (a < limit2 ? limit2 : a);
        else
            return a > limit2 ? limit2 : (a < limit1 ? limit1 : a);
            }
}

    internal static int SKP_LIMIT_int( int a, int limit1, int limit2)
    {
        unchecked
        {
        if( limit1 > limit2 )
            return a > limit1 ? limit1 : (a < limit2 ? limit2 : a);
        else
            return a > limit2 ? limit2 : (a < limit1 ? limit1 : a);
            }
}
    internal static short SKP_LIMIT_16( short a, short limit1, short limit2)
    {
        unchecked
        {
        if( limit1 > limit2 )
            return a > limit1 ? limit1 : (a < limit2 ? limit2 : a);
        else
            return a > limit2 ? limit2 : (a < limit1 ? limit1 : a);
            }
}
    internal static int SKP_LIMIT_32( int a, int limit1, int limit2)
    {
        unchecked
        {
        if( limit1 > limit2 )
            return a > limit1 ? limit1 : (a < limit2 ? limit2 : a);
        else
            return a > limit2 ? limit2 : (a < limit1 ? limit1 : a);
            }
}


    /**
     * (((a) >  0)  ? (a) : -(a))
     * Be careful, SKP_abs returns wrong when input equals to SKP_intXX_MIN
     * @param a
     * @return
     */
    internal static int SKP_abs(int a)
    {
        unchecked
        {
        return  (((a) >  0)  ? (a) : -(a));
            }
}
    internal static int SKP_abs_int(int a)
    {
        unchecked
        {
        return (((a) ^ ((a) >> (32 - 1))) - ((a) >> (32 - 1)));
            }
}
    internal static int SKP_abs_int32(int a)
    {
        unchecked
        {
        return (((a) ^ ((a) >> 31)) - ((a) >> 31));
            }
}
    internal static long SKP_abs_int64(long a)
    {
        unchecked
        {
        return (((a) >  0)  ? (a) : -(a));
            }
}

    internal static int SKP_sign(int a)
    {
        unchecked
        {
        return ((a) > 0 ? 1 : ( (a) < 0 ? -1 : 0 ));
            }
}

    internal static double SKP_sqrt(int a)
    {
        unchecked
        {
        return Math.Sqrt(a);
            }
}

    /**
     * PSEUDO-RANDOM GENERATOR
     * Make sure to store the result as the seed for the next call (also input between
     * frames), otherwise result won't be random at all. When only using some of the
     * bits, take the most significant bits by right-shifting. Do not just mask off
     * the lowest bits.
     * SKP_RAND(seed)                   (SKP_MLA_ovflw(907633515, (seed), 196314165))
     * @param seed
     * @return
     */
    internal static int SKP_RAND(int seed)
    {
        unchecked
        {
        return 907633515 + seed*196314165;
            }
}

    // Add some multiplication functions that can be easily mapped to ARM.

//       SKP_SMMUL: Signed top word multiply.
//            ARMv6        2 instruction cycles.
//            ARMv3M+        3 instruction cycles. use SMULL and ignore LSB registers.(except xM)
//  #define SKP_SMMUL(a32, b32)            (SKP_int32)SKP_RSHIFT(SKP_SMLAL(SKP_SMULWB((a32), (b32)), (a32), SKP_RSHIFT_ROUND((b32), 16)), 16)
//     the following seems faster on x86
//    #define SKP_SMMUL(a32, b32)              (SKP_int32)SKP_RSHIFT64(SKP_SMULL((a32), (b32)), 32)
    internal static int SKP_SMMUL(int a32, int b32)
    {
        unchecked
        {
        return (int)( ( (long)a32*b32 )>>32 );
            }
}
}
