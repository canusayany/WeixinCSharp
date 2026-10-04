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
using static SilkCodec.NET.Managed.Macros;

namespace SilkCodec.NET.Managed;

/**
 * Fourth order ARMA filter.
 * Internally operates as two biquad filters input sequence.
 * Coeffients are stored input a packed format:
 * { B1_Q14[1], B2_Q14[1], -A1_Q14[1], -A1_Q14[2], -A2_Q14[1], -A2_Q14[2], gain_Q16 }
 * where it is assumed that B*_Q14[0], B*_Q14[2], A*_Q14[0] are all 16384.
 *
 * @author Jing Dai
 * @author Dingxin Xu
 */
internal static class ResamplerPrivateARMA4
{
    /**
     *
     * @param S State vector [ 4 ].
     * @param S_offset offset of valid data.
     * @param output Output signal.
     * @param out_offset  offset of valid data.
     * @param input Input signal.
     * @param in_offset offset of valid data.
     * @param Coef ARMA coefficients [ 7 ].
     * @param Coef_offset offset of valid data.
     * @param len Signal length.
     */
    internal static void SKP_Silk_resampler_private_ARMA4(
        int[] S,            /* I/O: State vector [ 4 ]                        */
        int S_offset,
        short[] output,        /* O:    Output signal                            */
        int out_offset,
        short[] input,            /* I:    Input signal                            */
        int in_offset,
        short[] Coef,        /* I:    ARMA coefficients [ 7 ]                 */
        int Coef_offset,
        int                            len            /* I:    Signal length                            */
    )
    {
        int k;
        int in_Q8, out1_Q8, out2_Q8, X;

        for( k = 0; k < len; k++ )
        {
            in_Q8  = input[ in_offset+k ] << 8;

            /* Outputs of first and second biquad */
            out1_Q8 = in_Q8 + ( S[ S_offset ] << 2 );
            out2_Q8 = out1_Q8 + ( S[ S_offset+2 ] << 2 );

            /* Update states, which are stored input Q6. Coefficients are input Q14 here */
            X      = SKP_SMLAWB( S[ S_offset+1 ], in_Q8,   Coef[ Coef_offset ] );
            S[ S_offset ] = SKP_SMLAWB( X,      out1_Q8, Coef[ Coef_offset+2 ] );

            X      = SKP_SMLAWB( S[ S_offset+3 ], out1_Q8, Coef[ Coef_offset+1 ] );
            S[ S_offset+2 ] = SKP_SMLAWB( X,      out2_Q8, Coef[ Coef_offset+4 ] );

            S[ S_offset+1 ] = SKP_SMLAWB( in_Q8 >> 2, out1_Q8, Coef[ Coef_offset+3 ] );
            S[ S_offset+3 ] = SKP_SMLAWB( out1_Q8 >> 2 , out2_Q8, Coef[ Coef_offset+5 ] );

            /* Apply gain and store to output. The coefficient is input Q16 */
            output[ out_offset+k ] = (short)SigProcFIX.SKP_SAT16( SKP_SMLAWB( 128, out2_Q8, Coef[ Coef_offset+6 ] ) >> 8  );
        }
    }
}
