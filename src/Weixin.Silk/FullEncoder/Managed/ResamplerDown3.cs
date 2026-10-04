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
 * Downsample by a factor 3, low quality.
 *
 * @author Jing Dai
 * @author Dingxin Xu
 */
internal static class ResamplerDown3
{
    internal static int ORDER_FIR =                  4;

    /**
     * Downsample by a factor 3, low quality.
     * @param S State vector [ 8 ]
     * @param S_offset offset of valid data.
     * @param output Output signal [ floor(inLen/3) ]
     * @param out_offset offset of valid data.
     * @param input Input signal [ inLen ]
     * @param in_offset offset of valid data.
     * @param inLen Number of input samples
     */
    internal static void SKP_Silk_resampler_down3(
        int[] S,         /* I/O: State vector [ 8 ]                  */
        int S_offset,
        short[] output,       /* O:   Output signal [ floor(inLen/3) ]    */
        int out_offset,
        short[] input,        /* I:   Input signal [ inLen ]              */
        int in_offset,
        int                             inLen      /* I:   Number of input samples             */
    )
    {
        int nSamplesIn, counter, res_Q6;
        int[] buf = new int[ ResamplerPrivate.RESAMPLER_MAX_BATCH_SIZE_IN + ORDER_FIR ];
        int buf_ptr;

        /* Copy buffered samples to start of buffer */
        for(int i_djinn=0; i_djinn<ORDER_FIR; i_djinn++)
            buf[i_djinn] = S[S_offset+i_djinn];

        /* Iterate over blocks of frameSizeIn input samples */
        while( true )
        {
            nSamplesIn = Math.Min( inLen, ResamplerPrivate.RESAMPLER_MAX_BATCH_SIZE_IN );

            /* Second-order AR filter (output input Q8) */
            ResamplerPrivateAR2.SKP_Silk_resampler_private_AR2( S,ORDER_FIR, buf,ORDER_FIR, input,in_offset,
                    ResamplerRom.SKP_Silk_Resampler_1_3_COEFS_LQ,0, nSamplesIn );

            /* Interpolate filtered signal */
            buf_ptr = 0;
            counter = nSamplesIn;
            while( counter > 2 )
            {
                /* Inner product */
                res_Q6 = SKP_SMULWB(         buf[ buf_ptr   ] + buf[ buf_ptr+5 ], ResamplerRom.SKP_Silk_Resampler_1_3_COEFS_LQ[ 2 ] );
                res_Q6 = SKP_SMLAWB( res_Q6, buf[ buf_ptr+1 ] + buf[ buf_ptr+4 ], ResamplerRom.SKP_Silk_Resampler_1_3_COEFS_LQ[ 3 ] );
                res_Q6 = SKP_SMLAWB( res_Q6, buf[ buf_ptr+2 ] + buf[ buf_ptr+3 ], ResamplerRom.SKP_Silk_Resampler_1_3_COEFS_LQ[ 4 ] );

                /* Scale down, saturate and store input output array */
                output[out_offset++] = (short)SigProcFIX.SKP_SAT16( SigProcFIX.SKP_RSHIFT_ROUND( res_Q6, 6 ) );

                buf_ptr += 3;
                counter -= 3;
            }

            in_offset += nSamplesIn;
            inLen -= nSamplesIn;

            if( inLen > 0 )
            {
                /* More iterations to do; copy last part of filtered signal to beginning of buffer */
                for(int i_djinn=0; i_djinn<ORDER_FIR; i_djinn++)
                    buf[i_djinn] = buf[nSamplesIn+i_djinn];
            }
            else
            {
                break;
            }
        }

        /* Copy last part of filtered signal to the state for the next call */
        for(int i_djinn=0; i_djinn<ORDER_FIR; i_djinn++)
            S[S_offset+i_djinn] = buf[nSamplesIn+i_djinn];
    }
}
