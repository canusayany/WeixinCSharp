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
 *
 * @author Jing Dai
 * @author Dingxin Xu
 */
internal static class SigProcFLP
{
    /* Pitch estimator */
    internal const int SigProc_PITCH_EST_MIN_COMPLEX =       0;
    internal const int SigProc_PITCH_EST_MID_COMPLEX =       1;
    internal const int SigProc_PITCH_EST_MAX_COMPLEX =       2;

    internal const float PI =              3.1415926536f;
    internal static float SKP_min_float(float a, float b)
    {
        return (((a) < (b)) ? (a) :  (b));
    }
    internal static float SKP_max_float(float a, float b)
    {
        return (((a) > (b)) ? (a) :  (b));
    }
    internal static float SKP_abs_float(float a)
    {
        return Math.Abs(a);
    }

    internal static float SKP_LIMIT_float( float a, float limit1, float limit2)
    {
        if( limit1 > limit2 )
            return a > limit1 ? limit1 : (a < limit2 ? limit2 : a);
        else
            return a > limit2 ? limit2 : (a < limit1 ? limit1 : a);
    }

    /* sigmoid function */
    internal static float SKP_sigmoid(float x)
    {
        return (float)(1.0 / (1.0 + Math.Exp(-x)));
    }

    /* floating-point to integer conversion (rounding) */
    internal static void SKP_float2short_array
    (
        short[]       output,
        int out_offset,
        float[]       input,
        int in_offset,
        int       length
    )
    {
        int k;
        for (k = length-1; k >= 0; k--)
        {
            double x = input[in_offset+k];
            output[out_offset+k] = (short)SigProcFIX.SKP_SAT16( (int)(( x > 0 ) ? x + 0.5 : x - 0.5) );
        }
    }

    /* floating-point to integer conversion (rounding) */
    internal static int SKP_float2int(double x)
    {
        return (int)( ( x > 0 ) ? x + 0.5 : x - 0.5 );
    }

    /* integer to floating-point conversion */
    internal static void SKP_short2float_array
    (
        float[]       output,
        int out_offset,
        short[]           input,
        int in_offset,
        int       length
    )
    {
        int k;
        for (k = length-1; k >= 0; k--)
        {
            output[out_offset+k] = input[in_offset+k];
        }
    }

//TODO:    #define SKP_round(x)        (SKP_float)((x)>=0 ? (SKP_int64)((x)+0.5) : (SKP_int64)((x)-0.5))
    internal static float SKP_round(float x)
    {
        return ((x)>=0 ? (long)(x+0.5) : (long)(x-0.5));
    }
}
