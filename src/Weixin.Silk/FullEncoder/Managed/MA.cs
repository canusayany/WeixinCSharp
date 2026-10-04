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
using static SilkCodec.NET.Managed.Typedef;

namespace SilkCodec.NET.Managed;


/**
 * Variable order MA filter.
 *
 * @author Jing Dai
 * @author Dingxin Xu
 */
internal static class MA
{
    /**
     * Variable order MA filter.
     * @param input input signal.
     * @param in_offset offset of valid data.
     * @param B MA coefficients, Q13 [order+1].
     * @param S state vector [order].
     * @param output output signal.
     * @param out_offset offset of valid data.
     * @param len signal length.
     * @param order filter order.
     */
    internal static void SKP_Silk_MA(
            short[] input,            /* I:   input signal                                */
            int        in_offset,
            short[] B,             /* I:   MA coefficients, Q13 [order+1]              */
            int[] S,             /* I/O: state vector [order]                        */
            short[] output,           /* O:   output signal                               */
            int        out_offset,
            int  len,            /* I:   signal length                               */
            int  order           /* I:   filter order                                */
    )
    {
        int   k, d, in16;
        int out32;

        for( k = 0; k < len; k++ ) {
            in16 = input[ in_offset + k ];
            out32 = SKP_SMLABB(S[0], in16, B[ 0 ]);
            out32 = SigProcFIX.SKP_RSHIFT_ROUND( out32, 13 );

            for( d = 1; d < order; d++ ) {
                S[ d - 1 ] = SKP_SMLABB( S[ d ], in16, B[ d ] );
            }
            S[ order - 1 ] = SKP_SMULBB( in16, B[ order ] );

            /* Limit */
            output[ out_offset + k ] = (short)SigProcFIX.SKP_SAT16( out32 );
        }
    }

    /**
     * Variable order MA prediction error filter.
     * @param input Input signal.
     * @param in_offset offset of valid data.
     * @param B MA prediction coefficients, Q12 [order].
     * @param B_offset
     * @param S State vector [order].
     * @param output Output signal.
     * @param out_offset offset of valid data.
     * @param len Signal length.
     * @param order Filter order.
     */
    internal static void SKP_Silk_MA_Prediction(
            short[] input,            /* I:   Input signal                                */
            int          in_offset,
            short[] B,             /* I:   MA prediction coefficients, Q12 [order]     */
            int          B_offset,
            int[] S,             /* I/O: State vector [order]                        */
            short[] output,           /* O:   Output signal                               */
            int          out_offset,
            int    len,            /* I:   Signal length                               */
            int    order           /* I:   Filter order                                */
        )
    {
        int   k, d, in16;
        int out32;

        for( k = 0; k < len; k++ ) {
            in16 = input[ in_offset + k ];
            out32 = ( in16 << 12 ) - S[ 0 ];
            out32 = SigProcFIX.SKP_RSHIFT_ROUND( out32, 12 );

            for( d = 0; d < order - 1; d++ ) {
                S[ d ] = SigProcFIX.SKP_SMLABB_ovflw( S[ d + 1 ], in16, B[ B_offset + d ] );
            }
            S[ order - 1 ] = SKP_SMULBB( in16, B[ B_offset + order - 1 ] );

            /* Limit */
            output[ out_offset + k ] = (short)SigProcFIX.SKP_SAT16( out32 );
        }
    }

    /**
     *
     * @param input input signal.
     * @param in_offset offset of valid data.
     * @param B MA prediction coefficients, Q13 [order].
     * @param S state vector [order].
     * @param output output signal.
     * @param out_offset offset of valid data.
     * @param len signal length.
     * @param order filter order.
     */
    internal static void SKP_Silk_MA_Prediction_Q13(
            short[] input,            /* I:   input signal                                */
            int          in_offset,
            short[] B,             /* I:   MA prediction coefficients, Q13 [order]     */
            int[] S,             /* I/O: state vector [order]                        */
            short[] output,           /* O:   output signal                               */
            int          out_offset,
            int          len,            /* I:   signal length                               */
            int          order           /* I:   filter order                                */
        )
    {
        int   k, d, in16;
        int out32;
        for( k = 0; k < len; k++ ) {
            in16 = input[ in_offset + k ];
            out32 = ( in16 << 13 ) - S[ 0 ];
            out32 = SigProcFIX.SKP_RSHIFT_ROUND( out32, 13 );

            for( d = 0; d < order - 1; d++ ) {
                S[ d ] = SKP_SMLABB( S[ d + 1 ], in16, B[ d ] );
            }
            S[ order - 1 ] = SKP_SMULBB( in16, B[ order - 1 ] );

            /* Limit */
            output[ out_offset + k ] = ( short )SigProcFIX.SKP_SAT16( out32 );
        }
    }

    /**
     *
     * @param input Input signal.
     * @param in_offset offset of valid data.
     * @param B MA prediction coefficients, Q12 [order].
     * @param S State vector [order].
     * @param output Output signal.
     * @param out_offset offset of valid data.
     * @param len Signal length.
     * @param Order Filter order.
     */
    internal static void SKP_Silk_LPC_analysis_filter(
            short[] input,            /* I:   Input signal                                */
            int        in_offset,
            short[] B,             /* I:   MA prediction coefficients, Q12 [order]     */
            short[] S,             /* I/O: State vector [order]                        */
            short[] output,           /* O:   Output signal                               */
            int        out_offset,
            int  len,            /* I:   Signal length                               */
            int  Order           /* I:   Filter order                                */
        )
    {
        int   k, j, idx, Order_half = ( Order >> 1 );
        int out32_Q12, out32;
        short SA, SB;
        /* Order must be even */
        SKP_assert( 2 * Order_half == Order );

        /* S[] values are input Q0 */
        for( k = 0; k < len; k++ ) {
            SA = S[ 0 ];
            out32_Q12 = 0;
            for( j = 0; j < ( Order_half - 1 ); j++ ) {
                idx = SKP_SMULBB( 2, j ) + 1;
                /* Multiply-add two prediction coefficients for each loop */
                SB = S[ idx ];
                S[ idx ] = SA;
                out32_Q12 = SKP_SMLABB( out32_Q12, SA, B[ idx - 1 ] );
                out32_Q12 = SKP_SMLABB( out32_Q12, SB, B[ idx ] );
                SA = S[ idx + 1 ];
                S[ idx + 1 ] = SB;
            }

            /* Unrolled loop: epilog */
            SB = S[ Order - 1 ];
            S[ Order - 1 ] = SA;
            out32_Q12 = SKP_SMLABB( out32_Q12, SA, B[ Order - 2 ] );
            out32_Q12 = SKP_SMLABB( out32_Q12, SB, B[ Order - 1 ] );

            /* Subtract prediction */
            out32_Q12 = SKP_SUB_SAT32( ( input[ in_offset + k ] << 12 ), out32_Q12 );

            /* Scale to Q0 */
            out32 = SigProcFIX.SKP_RSHIFT_ROUND( out32_Q12, 12 );

            /* Saturate output */
            output[ out_offset + k ] = ( short )SigProcFIX.SKP_SAT16( out32 );

            /* Move input line */
            S[ 0 ] = input[ in_offset + k ];
        }
    }
}
