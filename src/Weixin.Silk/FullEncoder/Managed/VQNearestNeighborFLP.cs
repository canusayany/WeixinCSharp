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
 *
 * @author Dingxin Xu
 */
internal class VQNearestNeighborFLP
{
    /**
     * entropy constrained MATRIX-weighted VQ, for a single input data vector.
     * @param ind Index of best codebook vector
     * @param rate_dist Best weighted quant. error + mu * rate
     * @param input Input vector to be quantized
     * @param W Weighting matrix
     * @param cb Codebook
     * @param cl_Q6 Code length for each codebook vector
     * @param mu Tradeoff between WSSE and rate
     * @param L Number of vectors input codebook
     */
    internal static void SKP_Silk_VQ_WMat_EC_FLP(
              int                   []ind,               /* O    Index of best codebook vector           */
              int                   ind_offset,
              float                 []rate_dist,         /* O    Best weighted quant. error + mu * rate  */
        float                 []input,                /* I    Input vector to be quantized            */
              int                   in_offset,
        float                 []W,                 /* I    Weighting matrix                        */
              int                   W_offset,
        short                 []cb,                /* I    Codebook                                */
        short                 []cl_Q6,             /* I    Code length for each codebook vector    */
        float                 mu,                 /* I    Tradeoff between WSSE and rate          */
        int                   L                   /* I    Number of vectors input codebook           */
    )
    {
//        SKP_int   k;
//        SKP_float sum1;
//        SKP_float diff[ 5 ];
//        const SKP_int16 *cb_row;
        int k;
        float sum1;
        float[] diff = new float[5];
        short []cb_row;
        int cb_row_offset = 0;

        /* Loop over codebook */
//        *rate_dist = SKP_float_MAX;
        rate_dist[0] = float.MaxValue;

        cb_row = cb;
        cb_row_offset = 0;

        for( k = 0; k < L; k++ ) {
            /* Calc difference between input vector and cbk vector */
            diff[ 0 ] = input[ in_offset + 0 ] - cb_row[ 0 ] * DefineFLP.Q14_CONVERSION_FAC;
            diff[ 1 ] = input[ in_offset + 1 ] - cb_row[ 1 ] * DefineFLP.Q14_CONVERSION_FAC;
            diff[ 2 ] = input[ in_offset + 2 ] - cb_row[ 2 ] * DefineFLP.Q14_CONVERSION_FAC;
            diff[ 3 ] = input[ in_offset + 3 ] - cb_row[ 3 ] * DefineFLP.Q14_CONVERSION_FAC;
            diff[ 4 ] = input[ in_offset + 4 ] - cb_row[ 4 ] * DefineFLP.Q14_CONVERSION_FAC;

            /* Weighted rate */
            sum1 = mu * cl_Q6[ k ] / 64.0f;

            /* Add weighted quantization error, assuming W is symmetric */
            /* first row of W */
            sum1 += diff[ 0 ] * ( W[ W_offset + 0 ] * diff[ 0 ] +
                         2.0f * ( W[ W_offset + 1 ] * diff[ 1 ] +
                                  W[ W_offset + 2 ] * diff[ 2 ] +
                                  W[ W_offset + 3 ] * diff[ 3 ] +
                                  W[ W_offset + 4 ] * diff[ 4 ] ) );

            /* second row of W */
            sum1 += diff[ 1 ] * ( W[ W_offset + 6 ] * diff[ 1 ] +
                         2.0f * ( W[ W_offset + 7 ] * diff[ 2 ] +
                                  W[ W_offset + 8 ] * diff[ 3 ] +
                                  W[ W_offset + 9 ] * diff[ 4 ] ) );

            /* third row of W */
            sum1 += diff[ 2 ] * ( W[ W_offset + 12 ] * diff[ 2 ] +
                        2.0f *  ( W[ W_offset + 13 ] * diff[ 3 ] +
                                  W[ W_offset + 14 ] * diff[ 4 ] ) );

            /* fourth row of W */
            sum1 += diff[ 3 ] * ( W[ W_offset + 18 ] * diff[ 3 ] +
                         2.0f * ( W[ W_offset + 19 ] * diff[ 4 ] ) );

            /* last row of W */
            sum1 += diff[ 4 ] * ( W[ W_offset + 24 ] * diff[ 4 ] );

            /* find best */
            if( sum1 < rate_dist[0] ) {
                rate_dist[0] = sum1;
                ind[ind_offset + 0] = k;
            }

            /* Go to next cbk vector */
            cb_row_offset += LTP_ORDER;
        }
    }
}
