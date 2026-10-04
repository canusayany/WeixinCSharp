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
 * Upsample by a factor 2, high quality.
 *
 * @author Jing Dai
 * @author Dingxin Xu
 */
internal static class ResamplerPrivateUp2HQ
{
    /**
     * Upsample by a factor 2, high quality.
     * Uses 2nd order allpass filters for the 2x upsampling, followed by a
     * notch filter just above Nyquist.
     * @param S Resampler state [ 6 ].
     * @param S_offset offset of valid data.
     * @param output Output signal [ 2 * len ].
     * @param out_offset offset of valid data.
     * @param input Input signal [ len ].
     * @param in_offset offset of valid data.
     * @param len Number of INPUT samples.
     */
    internal static void SKP_Silk_resampler_private_up2_HQ(
        int[] S,               /* I/O: Resampler state [ 6 ]                   */
        int S_offset,
        short[] output,           /* O:   Output signal [ 2 * len ]               */
        int out_offset,
        short[] input,            /* I:   Input signal [ len ]                    */
        int in_offset,
        int                         len            /* I:   Number of INPUT samples                 */
    )
    {
        int k;
        int in32, out32_1, out32_2, Y, X;

        Debug.Assert( ResamplerRom.SKP_Silk_resampler_up2_hq_0[ 0 ] > 0 );
        Debug.Assert( ResamplerRom.SKP_Silk_resampler_up2_hq_0[ 1 ] < 0 );
        Debug.Assert( ResamplerRom.SKP_Silk_resampler_up2_hq_1[ 0 ] > 0 );
        Debug.Assert( ResamplerRom.SKP_Silk_resampler_up2_hq_1[ 1 ] < 0 );

        /* Internal variables and state are input Q10 format */
        for( k = 0; k < len; k++ )
        {
            /* Convert to Q10 */
            in32 = input[ in_offset+k ] << 10;

            /* First all-pass section for even output sample */
            Y       = in32 - S[ S_offset ];
            X       = SKP_SMULWB( Y, ResamplerRom.SKP_Silk_resampler_up2_hq_0[ 0 ] );
            out32_1 = S[ S_offset ] + X;
            S[ S_offset ]  = in32 + X;

            /* Second all-pass section for even output sample */
            Y       = out32_1 - S[ S_offset+1 ];
            X       = SKP_SMLAWB( Y, Y, ResamplerRom.SKP_Silk_resampler_up2_hq_0[ 1 ] );
            out32_2 = S[ S_offset+1 ] + X;
            S[ S_offset+1 ]  = out32_1 + X;

            /* Biquad notch filter */
            out32_2 = SKP_SMLAWB( out32_2, S[ S_offset+5 ], ResamplerRom.SKP_Silk_resampler_up2_hq_notch[ 2 ] );
            out32_2 = SKP_SMLAWB( out32_2, S[ S_offset+4 ], ResamplerRom.SKP_Silk_resampler_up2_hq_notch[ 1 ] );
            out32_1 = SKP_SMLAWB( out32_2, S[ S_offset+4 ], ResamplerRom.SKP_Silk_resampler_up2_hq_notch[ 0 ] );
            S[ S_offset+5 ]  = out32_2 - S[ S_offset+5 ];

            /* Apply gain input Q15, convert back to int16 and store to output */
            output[ out_offset + 2 * k ] = (short)SigProcFIX.SKP_SAT16(
                SKP_SMLAWB( 256, out32_1, ResamplerRom.SKP_Silk_resampler_up2_hq_notch[ 3 ] ) >> 9 );

            /* First all-pass section for odd output sample */
            Y       = in32 - S[ S_offset+2 ];
            X       = SKP_SMULWB( Y, ResamplerRom.SKP_Silk_resampler_up2_hq_1[ 0 ] );
            out32_1 = S[ S_offset+2 ] + X;
            S[ S_offset+2 ]  = in32 + X;

            /* Second all-pass section for odd output sample */
            Y       = out32_1 - S[ S_offset+3 ];
            X       = SKP_SMLAWB( Y, Y, ResamplerRom.SKP_Silk_resampler_up2_hq_1[ 1 ] );
            out32_2 = S[ S_offset+3 ] + X;
            S[ S_offset+3 ]  = out32_1 + X;

            /* Biquad notch filter */
            out32_2 = SKP_SMLAWB( out32_2, S[ S_offset+4 ], ResamplerRom.SKP_Silk_resampler_up2_hq_notch[ 2 ] );
            out32_2 = SKP_SMLAWB( out32_2, S[ S_offset+5 ], ResamplerRom.SKP_Silk_resampler_up2_hq_notch[ 1 ] );
            out32_1 = SKP_SMLAWB( out32_2, S[ S_offset+5 ], ResamplerRom.SKP_Silk_resampler_up2_hq_notch[ 0 ] );
            S[ S_offset+4 ]  = out32_2 - S[ S_offset+4 ];

            /* Apply gain input Q15, convert back to int16 and store to output */
            output[ out_offset + 2 * k + 1 ] = (short)SigProcFIX.SKP_SAT16(
                SKP_SMLAWB( 256, out32_1, ResamplerRom.SKP_Silk_resampler_up2_hq_notch[ 3 ] ) >> 9 );
        }
    }

    /**
     * the wrapper method.
     * @param SS Resampler state (unused).
     * @param output Output signal [ 2 * len ].
     * @param out_offset offset of valid data.
     * @param input Input signal [ len ].
     * @param in_offset offset of valid data.
     * @param len Number of input samples.
     */
    internal static void SKP_Silk_resampler_private_up2_HQ_wrapper(
        object                            SS,               /* I/O: Resampler state (unused)                */
        short[] output,           /* O:   Output signal [ 2 * len ]               */
        int out_offset,
        short[] input,            /* I:   Input signal [ len ]                    */
        int in_offset,
        int                             len            /* I:   Number of input samples                 */
    )
    {
        SKP_Silk_resampler_state_struct S = (SKP_Silk_resampler_state_struct )SS;
        SKP_Silk_resampler_private_up2_HQ( S.sIIR,0, output,out_offset, input,in_offset, len );
    }
}
