/*
 * Copyright @ 2015 Atlassian Pty Ltd
 *
 * Licensed under the Apache License, Version 2.0 (the "License");
 * you may not use this file except input compliance with the License.
 * You may obtain a copy of the License at
 *
 *     http://www.apache.org/licenses/LICENSE-2.0
 *
 * Unless required by applicable law or agreed to input writing, software
 * distributed under the License is distributed on an "AS IS" BASIS,
 * WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
 * See the License for the specific language governing permissions and
 * limitations under the License.
 */

using System;
using System.Diagnostics;

namespace SilkCodec.NET.Managed;
/**
 *
 * @author Jing Dai
 * @author Dingxin Xu
 */
internal static class Decimate2CoarsestFLP
{
    /* coefficients for coarsest 2-fold resampling */
    /* note that these differ from the interpolator with the same filter orders! */
    internal static float[] A20cst_FLP = {0.289001464843750f};
    internal static float[] A21cst_FLP = {0.780487060546875f};

    /**
     * downsample by a factor 2, coarsest.
     * @param input 16 kHz signal [2*len].
     * @param in_offset offset of the valid data.
     * @param S state vector [2].
     * @param S_offset offset of the valid data.
     * @param output 8 kHz signal [len].
     * @param out_offset offset of the valid data.
     * @param scratch scratch memory [3*len].
     * @param scratch_offset offset of the valid data.
     * @param len number of OUTPUT samples.
     */
    internal static void SKP_Silk_decimate2_coarsest_FLP(
        float[] input,        /* I:   16 kHz signal [2*len]       */
        int               in_offset,
        float[] S,         /* I/O: state vector [2]            */
        int               S_offset,
        float[] output,       /* O:   8 kHz signal [len]          */
        int               out_offset,
        float[] scratch,   /* I:   scratch memory [3*len]      */
        int               scratch_offset,
        int         len         /* I:   number of OUTPUT samples    */
    )
    {
        int k;

        /* de-interleave allpass inputs */
        for ( k = 0; k < len; k++ )
        {
            scratch[ scratch_offset + k ]       = input[ in_offset + 2 * k + 0 ];
            scratch[ scratch_offset + k + len ] = input[ in_offset + 2 * k + 1 ];
        }

        /* allpass filters */
        AllpassIntFLP.SKP_Silk_allpass_int_FLP( scratch,scratch_offset,     S,S_offset,   A21cst_FLP[ 0 ], scratch,scratch_offset+2 * len, len );
        AllpassIntFLP.SKP_Silk_allpass_int_FLP( scratch,scratch_offset+len, S,S_offset+1, A20cst_FLP[ 0 ], scratch,scratch_offset,         len );

        /* add two allpass outputs */
        for ( k = 0; k < len; k++ )
        {
            output[ out_offset+k ] = 0.5f * ( scratch[ scratch_offset + k ] + scratch[ scratch_offset + k + 2 * len ] );
        }
    }
}
