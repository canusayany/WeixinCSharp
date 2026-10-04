using static SilkCodec.NET.Managed.Define;
using static SilkCodec.NET.Managed.Macros;
using static SilkCodec.NET.Managed.Typedef;

namespace SilkCodec.NET.Managed;

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
/**
 * Encode quantization indices of excitation.
 *
 * @author Dingxin Xu
 */
internal class EncodePulses
{
    /**
     *
     * @param pulses_comb
     * @param pulses_in
     * @param pulses_in_offset offset of valid data.
     * @param max_pulses max value for sum of pulses.
     * @param len number of output values.
     * @return
     */
    internal static int combine_and_check(       /* return ok */
        int         []pulses_comb,           /* O */
        int   []pulses_in,             /* I */
        int         pulses_in_offset,
        int         max_pulses,             /* I    max value for sum of pulses */
        int         len                     /* I    number of output values */
    )
    {
        int k, sum;

        for( k = 0; k < len; k++ ) {
            sum = pulses_in[ pulses_in_offset + 2 * k ] + pulses_in[ pulses_in_offset+ 2 * k + 1 ];
            if( sum > max_pulses ) {
                return 1;
            }
            pulses_comb[ k ] = sum;
        }
        return 0;
    }

    /**
     * Encode quantization indices of excitation.
     * @param psRC Range coder state
     * @param sigtype Sigtype
     * @param QuantOffsetType QuantOffsetType
     * @param q quantization
     * @param frame_length Frame length
     */
    internal static void SKP_Silk_encode_pulses(
            SKP_Silk_range_coder_state  psRC,           /* I/O  Range coder state               */
            int                   sigtype,        /* I    Sigtype                         */
            int                   QuantOffsetType,/* I    QuantOffsetType                 */
            byte[] q,            /* I    quantization indices            */
            int                   frame_length,   /* I    Frame length                    */
            int[] abs_pulses,
            int[] sum_pulses,
            int[] nRshifts,
            int[] pulses_comb,
            int[] shell_scratch
    )
    {
        int   i, k, j, iter, bit, nLS, scale_down, RateLevelIndex = 0;
        int abs_q, minSumBits_Q6, sumBits_Q6;
        int   []abs_pulses_ptr;
        int abs_pulses_ptr_offset;
        byte []pulses_ptr;
        int pulses_ptr_offset;
        int [] cdf_ptr;
        short[] nBits_ptr;

        Array.Clear(pulses_comb);

        /****************************/
        /* Prepare for shell coding */
        /****************************/
        /* Calculate number of shell blocks */
        iter = frame_length / SHELL_CODEC_FRAME_LENGTH;

        /* Take the absolute value of the pulses */
        for( i = 0; i < frame_length; i+=4 ) {
            abs_pulses[i+0] = Math.Abs((sbyte)q[i+0]);
            abs_pulses[i+1] = Math.Abs((sbyte)q[i+1]);
            abs_pulses[i+2] = Math.Abs((sbyte)q[i+2]);
            abs_pulses[i+3] = Math.Abs((sbyte)q[i+3]);
        }

        /* Calc sum pulses per shell code frame */
        abs_pulses_ptr = abs_pulses;
        abs_pulses_ptr_offset = 0;
        for( i = 0; i < iter; i++ ) {
            nRshifts[ i ] = 0;

            while( true ) {
                /* 1+1 -> 2 */
                scale_down = combine_and_check( pulses_comb, abs_pulses_ptr, abs_pulses_ptr_offset,
                        TablesPulsesPerBlock.SKP_Silk_max_pulses_table[ 0 ], 8 );

                /* 2+2 -> 4 */
                scale_down += combine_and_check( pulses_comb, pulses_comb, 0,
                        TablesPulsesPerBlock.SKP_Silk_max_pulses_table[ 1 ], 4 );

                /* 4+4 -> 8 */
                scale_down += combine_and_check( pulses_comb, pulses_comb, 0,
                        TablesPulsesPerBlock.SKP_Silk_max_pulses_table[ 2 ], 2 );

                /* 8+8 -> 16 */
                sum_pulses[ i ] = pulses_comb[ 0 ] + pulses_comb[ 1 ];
                if( sum_pulses[ i ] > TablesPulsesPerBlock.SKP_Silk_max_pulses_table[ 3 ] ) {
                    scale_down++;
                }

                if( scale_down !=0 ) {
                    /* We need to down scale the quantization signal */
                    nRshifts[ i ]++;
                    for( k = 0; k < SHELL_CODEC_FRAME_LENGTH; k++ ) {
                        abs_pulses_ptr[ abs_pulses_ptr_offset + k ] = ( abs_pulses_ptr[ abs_pulses_ptr_offset + k ] >>1 );
                    }
                } else {
                    /* Jump output of while(1) loop and go to next shell coding frame */
                    break;
                }
            }
            abs_pulses_ptr_offset += SHELL_CODEC_FRAME_LENGTH;
        }

        /**************/
        /* Rate level */
        /**************/
        /* find rate level that leads to fewest bits for coding of pulses per block info */
        minSumBits_Q6 = int.MaxValue;
        for( k = 0; k < N_RATE_LEVELS - 1; k++ ) {
            nBits_ptr  = TablesPulsesPerBlock.SKP_Silk_pulses_per_block_BITS_Q6[ k ];
            sumBits_Q6 = TablesPulsesPerBlock.SKP_Silk_rate_levels_BITS_Q6[sigtype][ k ];
            for( i = 0; i < iter; i++ ) {
                if( nRshifts[ i ] > 0 ) {
                    sumBits_Q6 += nBits_ptr[ MAX_PULSES + 1 ];
                } else {
                    sumBits_Q6 += nBits_ptr[ sum_pulses[ i ] ];
                }
            }
            if( sumBits_Q6 < minSumBits_Q6 ) {
                minSumBits_Q6 = sumBits_Q6;
                RateLevelIndex = k;
            }
        }
        RangeCoder.SKP_Silk_range_encoder( psRC, RateLevelIndex,
                TablesPulsesPerBlock.SKP_Silk_rate_levels_CDF[ sigtype ], 0);

        /***************************************************/
        /* Sum-Weighted-Pulses Encoding                    */
        /***************************************************/
        cdf_ptr = TablesPulsesPerBlock.SKP_Silk_pulses_per_block_CDF[ RateLevelIndex ];
        for( i = 0; i < iter; i++ ) {
            if( nRshifts[ i ] == 0 ) {
                RangeCoder.SKP_Silk_range_encoder( psRC, sum_pulses[ i ], cdf_ptr, 0);
            } else {
                RangeCoder.SKP_Silk_range_encoder( psRC, MAX_PULSES + 1, cdf_ptr, 0);
                for( k = 0; k < nRshifts[ i ] - 1; k++ ) {
                    RangeCoder.SKP_Silk_range_encoder( psRC, MAX_PULSES + 1,
                            TablesPulsesPerBlock.SKP_Silk_pulses_per_block_CDF[ N_RATE_LEVELS - 1 ], 0);
                }
                RangeCoder.SKP_Silk_range_encoder( psRC, sum_pulses[ i ],
                        TablesPulsesPerBlock.SKP_Silk_pulses_per_block_CDF[ N_RATE_LEVELS - 1 ], 0);
            }
        }

        /******************/
        /* Shell Encoding */
        /******************/
        for( i = 0; i < iter; i++ ) {
            if( sum_pulses[ i ] > 0 ) {
                ShellCoder.SKP_Silk_shell_encoder( psRC, abs_pulses, i * SHELL_CODEC_FRAME_LENGTH, shell_scratch);
            }
        }

        /****************/
        /* LSB Encoding */
        /****************/
        for( i = 0; i < iter; i++ ) {
            if( nRshifts[ i ] > 0 ) {
                pulses_ptr = q;
                pulses_ptr_offset = i * SHELL_CODEC_FRAME_LENGTH;
                nLS = nRshifts[ i ] - 1;
                for( k = 0; k < SHELL_CODEC_FRAME_LENGTH; k++ ) {
                    var pulse = (sbyte)pulses_ptr[pulses_ptr_offset + k];
                    abs_q = Math.Abs((int)pulse);
                    for( j = nLS; j > 0; j-- ) {
                        bit = ( abs_q >> j ) & 1;
                        RangeCoder.SKP_Silk_range_encoder( psRC, bit, TablesOther.SKP_Silk_lsb_CDF, 0);
                    }
                    bit = abs_q & 1;
                    RangeCoder.SKP_Silk_range_encoder( psRC, bit, TablesOther.SKP_Silk_lsb_CDF, 0);
                }
            }
        }

        /****************/
        /* Encode signs */
        /****************/
        CodeSigns.SKP_Silk_encode_signs( psRC, q, frame_length, sigtype, QuantOffsetType, RateLevelIndex );
    }
}
