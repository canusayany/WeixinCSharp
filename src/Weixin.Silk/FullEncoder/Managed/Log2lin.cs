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
 * Convert input to a linear scale.
 *
 * @author Jing Dai
 * @author Dingxin Xu
 */
internal static class Log2lin
{
    /**
     * Approximation of 2^() (very close inverse of Silk_lin2log.SKP_Silk_lin2log())
     * Convert input to a linear scale.
     *
     * @param inLog_Q7 Input on log scale
     * @return
     */
    internal static int SKP_Silk_log2lin( int inLog_Q7 )    /* I:    Input on log scale */
    {
        int output, frac_Q7;

        if( inLog_Q7 < 0 ) {
            return 0;
        }

        output = ( 1 << ( inLog_Q7 >> 7 ) );

        frac_Q7 = inLog_Q7 & 0x7F;
        if( inLog_Q7 < 2048 ) {
            /* Piece-wise parabolic approximation */
            output = SigProcFIX.SKP_ADD_RSHIFT( output, SigProcFIX.SKP_MUL( output, SKP_SMLAWB( frac_Q7, SigProcFIX.SKP_MUL( frac_Q7, 128 - frac_Q7 ), -174 ) ), 7 );
        } else {
            /* Piece-wise parabolic approximation */
            output = SigProcFIX.SKP_MLA( output, SigProcFIX.SKP_RSHIFT( output, 7 ), SKP_SMLAWB( frac_Q7, SigProcFIX.SKP_MUL( frac_Q7, 128 - frac_Q7 ), -174 ) );
        }
        return output;
    }
}
