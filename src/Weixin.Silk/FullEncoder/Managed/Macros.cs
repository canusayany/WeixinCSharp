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

namespace SilkCodec.NET.Managed;

/**
 * Translated from what is an inline header file for general platform.
 *
 * @author Jing Dai
 * @author Dingxin Xu
 */
internal static class Macros
{
    // (a32 * (SKP_int32)((SKP_int16)(b32))) >> 16 output have to be 32bit int
    internal static int SKP_SMULWB(int a32, int b32)
    {
        unchecked
        {
        return ((((a32) >> 16) * ((short)(b32))) + ((((a32) & 0x0000FFFF) * ((short)(b32))) >> 16));
            }
}

    // a32 + (b32 * (SKP_int32)((SKP_int16)(c32))) >> 16 output have to be 32bit int
    internal static int SKP_SMLAWB(int a32, int b32, int c32)
    {
        unchecked
        {
        return ((a32) + ((((b32) >> 16) * ((short)(c32))) + ((((b32) & 0x0000FFFF) * ((short)(c32))) >> 16)));
            }
}

    // (a32 * (b32 >> 16)) >> 16
    internal static int SKP_SMULWT(int a32, int b32)
    {
        unchecked
        {
        return (((a32) >> 16) * ((b32) >> 16) + ((((a32) & 0x0000FFFF) * ((b32) >> 16)) >> 16));
            }
}

    // a32 + (b32 * (c32 >> 16)) >> 16
    internal static int SKP_SMLAWT(int a32, int b32, int c32)
    {
        unchecked
        {
        return ((a32) + (((b32) >> 16) * ((c32) >> 16)) + ((((b32) & 0x0000FFFF) * ((c32) >> 16)) >> 16));
            }
}

    // (SKP_int32)((SKP_int16)(a3))) * (SKP_int32)((SKP_int16)(b32)) output have to be 32bit int
    internal static int SKP_SMULBB(int a32, int b32)
    {
        unchecked
        {
        return (((short)(a32)) * ((short)(b32)));
            }
}

    // a32 + (SKP_int32)((SKP_int16)(b32)) * (SKP_int32)((SKP_int16)(c32)) output have to be 32bit int
    internal static int SKP_SMLABB(int a32, int b32, int c32)
    {
        unchecked
        {
        return ((a32) + (((short)(b32))) * ((short)(c32)));
            }
}

    // (SKP_int32)((SKP_int16)(a32)) * (b32 >> 16)
    internal static int SKP_SMULBT(int a32, int b32)
    {
        unchecked
        {
        return (((short)(a32)) * ((b32) >> 16));
            }
}

    // a32 + (SKP_int32)((SKP_int16)(b32)) * (c32 >> 16)
    internal static int SKP_SMLABT(int a32, int b32, int c32)
    {
        unchecked
        {
        return ((a32) + (((short)(b32))) * ((c32) >> 16));
            }
}

    // a64 + (b32 * c32)
    internal static long SKP_SMLAL(long a64, int b32, int c32)
    {
        unchecked
        {
        return a64 + (long)b32 * (long)c32;
            }
}

    // (a32 * b32) >> 16
    internal static int SKP_SMULWW(int a32, int b32)
    {
        unchecked
        {
        return SKP_SMULWB(a32, b32) + a32 * SigProcFIX.SKP_RSHIFT_ROUND(b32, 16);
            }
}

    // a32 + ((b32 * c32) >> 16)
    internal static int SKP_SMLAWW(int a32, int b32, int c32)
    {
        unchecked
        {
        return SKP_SMLAWB(a32, b32, c32) + b32 * SigProcFIX.SKP_RSHIFT_ROUND(c32, 16);
            }
}

    /* add/subtract with output saturated */
    internal static int SKP_ADD_SAT32(int a, int b)
    {
        unchecked
        {
        if( ((a + b) & 0x80000000) == 0 )
            return ((a & b) & 0x80000000) != 0 ? int.MinValue : a+b;
        else
            return ((a | b) & 0x80000000) == 0 ? int.MaxValue : a+b;
            }
}

    internal static int SKP_SUB_SAT32(int a, int b)
    {
        unchecked
        {
        if( ((a - b) & 0x80000000) == 0 )
            return ( a & (b^0x80000000) & 0x80000000) != 0 ? int.MinValue : a-b;
        else
            return ( (a^0x80000000) & b & 0x80000000) != 0 ? int.MaxValue : a-b;
            }
}

    internal static int SKP_Silk_CLZ16(short in16)
    {
        unchecked
        {
        return (int)BitOperations.LeadingZeroCount((uint)(in16 & 0x0000FFFF)) - 16;
            }
}

    internal static int SKP_Silk_CLZ32(int in32)
    {
        unchecked
        {
        return (int)BitOperations.LeadingZeroCount((uint)(in32));
            }
}
}
