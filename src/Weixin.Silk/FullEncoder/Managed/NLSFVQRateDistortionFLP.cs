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
 * Rate-Distortion calculations for multiple input data vectors
 *
 * @author Dingxin Xu
 */
internal class NLSFVQRateDistortionFLP
{
    /**
     * Rate-Distortion calculations for multiple input data vectors.
     * @param pRD Rate-distortion values [psNLSF_CBS_FLP->nVectors*N]
     * @param psNLSF_CBS_FLP NLSF codebook stage struct
     * @param input Input vectors to be quantized
     * @param w Weight vector
     * @param rate_acc Accumulated rates from previous stage
     * @param mu Weight between weighted error and rate
     * @param N Number of input vectors to be quantized
     * @param LPC_order  LPC order
     */
    internal static void SKP_Silk_NLSF_VQ_rate_distortion_FLP(
              float             []pRD,               /* O   Rate-distortion values [psNLSF_CBS_FLP->nVectors*N] */
        SKP_Silk_NLSF_CBS_FLP psNLSF_CBS_FLP,    /* I   NLSF codebook stage struct                          */
        float             []input,                /* I   Input vectors to be quantized                       */
        float             []w,                 /* I   Weight vector                                       */
        float             []rate_acc,          /* I   Accumulated rates from previous stage               */
        float             mu,                 /* I   Weight between weighted error and rate              */
        int               N,                  /* I   Number of input vectors to be quantized             */
        int               LPC_order,          /* I   LPC order                                           */
        float[]           weightCopy
    )
    {
        float[] pRD_vec;
        int     pRD_vec_offset;
        int   i, n;

        /* Compute weighted quantization errors for all input vectors over one codebook stage */
        NLSFVQSumErrorFLP.SKP_Silk_NLSF_VQ_sum_error_FLP( pRD, input, w, psNLSF_CBS_FLP.CB,
                N, psNLSF_CBS_FLP.nVectors, LPC_order, weightCopy );

        /* Loop over input vectors */
        pRD_vec = pRD;
        pRD_vec_offset = 0;
        for( n = 0; n < N; n++ ) {
            /* Add rate cost to error for each codebook vector */
            for( i = 0; i < psNLSF_CBS_FLP.nVectors; i++ ) {
                pRD_vec[ pRD_vec_offset + i ] += mu * ( rate_acc[n] + psNLSF_CBS_FLP.Rates[ i ] );
            }
            pRD_vec_offset += psNLSF_CBS_FLP.nVectors;
        }
    }
}
