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
 * shell coder; pulse-subframe length is hardcoded.
 *
 * @author Jing Dai
 * @author Dingxin Xu
 */
internal class ShellCoder
{
    /**
     *
     * @param output combined pulses vector [len]
     * @param out_offset offset of valid data.
     * @param input input vector       [2 * len]
     * @param in_offset offset of valid data.
     * @param len number of OUTPUT samples
     */
    internal static void combine_pulses(
            int         []output,   /* O:   combined pulses vector [len] */
            int         out_offset,
            int         []input,    /* I:   input vector       [2 * len] */
            int         in_offset,
            int   len     /* I:   number of OUTPUT samples     */
    )
    {
        int k;
        for( k = 0; k < len; k++ ) {
            output[ out_offset + k ] = input[ in_offset + 2 * k ] + input[ in_offset + 2 * k + 1 ];
        }
    }

    /**
     *
     * @param sRC compressor data structure
     * @param p_child1 pulse amplitude of first child subframe
     * @param p pulse amplitude of current subframe
     * @param shell_table table of shell cdfs
     */
    internal static void encode_split(
        SKP_Silk_range_coder_state  sRC,           /* I/O: compressor data structure                   */
        int               p_child1,       /* I:   pulse amplitude of first child subframe     */
        int               p,              /* I:   pulse amplitude of current subframe         */
        int[]             shell_table
    )
    {
        int[] cdf;
        int cdf_offset;

        if( p > 0 )
        {
            cdf = shell_table;
            cdf_offset =  TablesPulsesPerBlock.SKP_Silk_shell_code_table_offsets[ p ] ;
            RangeCoder.SKP_Silk_range_encoder( sRC, p_child1, cdf, cdf_offset );
        }
    }

    /**
     *
     * @param p_child1  pulse amplitude of first child subframe
     * @param p_child1_offset offset of valid data.
     * @param p_child2 pulse amplitude of second child subframe
     * @param p_child2_offset offset of valid data.
     * @param sRC compressor data structure
     * @param p pulse amplitude of current subframe
     * @param shell_table table of shell cdfs
     */
    internal static void decode_split(
            int                    []p_child1,      /* O:   pulse amplitude of first child subframe     */
            int                    p_child1_offset,
            int                    []p_child2,      /* O:   pulse amplitude of second child subframe    */
            int                    p_child2_offset,
            SKP_Silk_range_coder_state  sRC,           /* I/O: compressor data structure                   */
            int               p,              /* I:   pulse amplitude of current subframe         */
            int[]                  shell_table    /* I:   table of shell cdfs                         */
    )
    {
        int cdf_middle;
        int[] cdf;
        int   cdf_offset;

        if( p > 0 )
        {
            cdf_middle = ( p >> 1 );
            cdf = shell_table;
            cdf_offset = TablesPulsesPerBlock.SKP_Silk_shell_code_table_offsets[ p ];

            RangeCoder.SKP_Silk_range_decoder( p_child1, p_child1_offset, sRC, cdf, cdf_offset, cdf_middle );
            p_child2[ p_child2_offset + 0 ] = p - p_child1[ p_child1_offset + 0 ];
        }
        else
        {
            p_child1[ p_child1_offset + 0 ] = 0;
            p_child2[ p_child2_offset + 0 ] = 0;
        }
    }

    /**
     * Shell encoder, operates on one shell code frame of 16 pulses.
     * @param sRC compressor data structure.
     * @param pulses0 data: nonnegative pulse amplitudes.
     * @param pulses0_offset valid data
     */
    internal static void SKP_Silk_shell_encoder(
        SKP_Silk_range_coder_state sRC,               /* I/O  compressor data structure                   */
        int[]                      pulses0,           /* I    data: nonnegative pulse amplitudes          */
        int                        pulses0_offset,
        int[]                      scratch
    )
    {
        const int pulses1 = 0;
        const int pulses2 = 8;
        const int pulses3 = 12;
        const int pulses4 = 14;

        /* this function operates on one shell code frame of 16 pulses */
        System.Diagnostics.Debug.Assert( SHELL_CODEC_FRAME_LENGTH == 16 );

        /* tree representation per pulse-subframe */
        combine_pulses( scratch,pulses1, pulses0,pulses0_offset, 8 );
        combine_pulses( scratch,pulses2, scratch,pulses1, 4 );
        combine_pulses( scratch,pulses3, scratch,pulses2, 2 );
        combine_pulses( scratch,pulses4, scratch,pulses3, 1 );

        encode_split( sRC, scratch[pulses3], scratch[pulses4], TablesPulsesPerBlock.SKP_Silk_shell_code_table3 );

        encode_split( sRC, scratch[pulses2], scratch[pulses3], TablesPulsesPerBlock.SKP_Silk_shell_code_table2 );

        encode_split( sRC, scratch[pulses1], scratch[pulses2], TablesPulsesPerBlock.SKP_Silk_shell_code_table1 );
        encode_split( sRC, pulses0[  pulses0_offset + 0 ], scratch[pulses1], TablesPulsesPerBlock.SKP_Silk_shell_code_table0 );
        encode_split( sRC, pulses0[  pulses0_offset + 2 ], scratch[pulses1 + 1], TablesPulsesPerBlock.SKP_Silk_shell_code_table0 );

        encode_split( sRC, scratch[pulses1 + 2], scratch[pulses2 + 1], TablesPulsesPerBlock.SKP_Silk_shell_code_table1 );
        encode_split( sRC, pulses0[  pulses0_offset + 4 ], scratch[pulses1 + 2], TablesPulsesPerBlock.SKP_Silk_shell_code_table0 );
        encode_split( sRC, pulses0[  pulses0_offset + 6 ], scratch[pulses1 + 3], TablesPulsesPerBlock.SKP_Silk_shell_code_table0 );

        encode_split( sRC, scratch[pulses2 + 2], scratch[pulses3 + 1], TablesPulsesPerBlock.SKP_Silk_shell_code_table2 );

        encode_split( sRC, scratch[pulses1 + 4], scratch[pulses2 + 2], TablesPulsesPerBlock.SKP_Silk_shell_code_table1 );
        encode_split( sRC, pulses0[  pulses0_offset + 8 ], scratch[pulses1 + 4], TablesPulsesPerBlock.SKP_Silk_shell_code_table0 );
        encode_split( sRC, pulses0[  pulses0_offset + 10 ], scratch[pulses1 + 5], TablesPulsesPerBlock.SKP_Silk_shell_code_table0 );

        encode_split( sRC, scratch[pulses1 + 6], scratch[pulses2 + 3], TablesPulsesPerBlock.SKP_Silk_shell_code_table1 );
        encode_split( sRC, pulses0[ pulses0_offset + 12 ], scratch[pulses1 + 6], TablesPulsesPerBlock.SKP_Silk_shell_code_table0 );
        encode_split( sRC, pulses0[ pulses0_offset + 14 ], scratch[pulses1 + 7], TablesPulsesPerBlock.SKP_Silk_shell_code_table0 );
    }

    /**
     * Shell decoder, operates on one shell code frame of 16 pulses.
     * @param pulses0 data: nonnegative pulse amplitudes
     * @param pulses0_offset offset of valid data.
     * @param sRC compressor data structure
     * @param pulses4 number of pulses per pulse-subframe
     */
    internal static void SKP_Silk_shell_decoder(
            int                         []pulses0,           /* O    data: nonnegative pulse amplitudes          */
            int                         pulses0_offset,
            SKP_Silk_range_coder_state  sRC,               /* I/O  compressor data structure                   */
            int                   pulses4             /* I    number of pulses per pulse-subframe         */
    )
    {
        int[] pulses3 = new int[ 2 ], pulses2 = new int[ 4 ], pulses1 = new int[ 8 ];

        /* this function operates on one shell code frame of 16 pulses */
        System.Diagnostics.Debug.Assert( SHELL_CODEC_FRAME_LENGTH == 16 );

        decode_split( pulses3, 0, pulses3, 1, sRC, pulses4, TablesPulsesPerBlock.SKP_Silk_shell_code_table3 );

        decode_split( pulses2, 0, pulses2, 1, sRC, pulses3[ 0 ], TablesPulsesPerBlock.SKP_Silk_shell_code_table2 );

        decode_split( pulses1, 0, pulses1, 1, sRC, pulses2[ 0 ], TablesPulsesPerBlock.SKP_Silk_shell_code_table1 );
        decode_split( pulses0, pulses0_offset + 0, pulses0, pulses0_offset + 1, sRC, pulses1[ 0 ], TablesPulsesPerBlock.SKP_Silk_shell_code_table0 );
        decode_split( pulses0, pulses0_offset + 2, pulses0, pulses0_offset + 3, sRC, pulses1[ 1 ], TablesPulsesPerBlock.SKP_Silk_shell_code_table0 );

        decode_split( pulses1, 2, pulses1, 3, sRC, pulses2[ 1 ], TablesPulsesPerBlock.SKP_Silk_shell_code_table1 );
        decode_split( pulses0, pulses0_offset + 4, pulses0, pulses0_offset + 5, sRC, pulses1[ 2 ], TablesPulsesPerBlock.SKP_Silk_shell_code_table0 );
        decode_split( pulses0, pulses0_offset + 6, pulses0, pulses0_offset + 7, sRC, pulses1[ 3 ], TablesPulsesPerBlock.SKP_Silk_shell_code_table0 );

        decode_split( pulses2, 2, pulses2, 3, sRC, pulses3[ 1 ], TablesPulsesPerBlock.SKP_Silk_shell_code_table2 );

        decode_split( pulses1, 4, pulses1, 5, sRC, pulses2[ 2 ], TablesPulsesPerBlock.SKP_Silk_shell_code_table1 );
        decode_split( pulses0, pulses0_offset + 8, pulses0, pulses0_offset + 9, sRC, pulses1[ 4 ], TablesPulsesPerBlock.SKP_Silk_shell_code_table0 );
        decode_split( pulses0, pulses0_offset + 10,pulses0, pulses0_offset + 11, sRC, pulses1[ 5 ], TablesPulsesPerBlock.SKP_Silk_shell_code_table0 );

        decode_split( pulses1, 6, pulses1, 7, sRC, pulses2[ 3 ], TablesPulsesPerBlock.SKP_Silk_shell_code_table1 );
        decode_split( pulses0, pulses0_offset + 12, pulses0, pulses0_offset + 13, sRC, pulses1[ 6 ], TablesPulsesPerBlock.SKP_Silk_shell_code_table0 );
        decode_split( pulses0, pulses0_offset + 14, pulses0, pulses0_offset + 15, sRC, pulses1[ 7 ], TablesPulsesPerBlock.SKP_Silk_shell_code_table0 );
    }
}
