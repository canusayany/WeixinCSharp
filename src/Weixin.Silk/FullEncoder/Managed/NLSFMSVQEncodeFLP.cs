// Upstream constant feature switches retain disabled reference paths.
#pragma warning disable CS0162
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
 * NLSF vector encoder.
 *
 * @author Dingxin Xu
 */
internal class NLSFMSVQEncodeFLP
{
    /**
     * NLSF vector encoder.
     * @param NLSFIndices Codebook path vector [ CB_STAGES ]
     * @param pNLSF Quantized NLSF vector [ LPC_ORDER ]
     * @param psNLSF_CB_FLP Codebook object
     * @param pNLSF_q_prev Prev. quantized NLSF vector [LPC_ORDER]
     * @param pW NLSF weight vector [ LPC_ORDER ]
     * @param NLSF_mu Rate weight for the RD optimization
     * @param NLSF_mu_fluc_red Fluctuation reduction error weight
     * @param NLSF_MSVQ_Survivors  Max survivors from each stage
     * @param LPC_order LPC order
     * @param deactivate_fluc_red Deactivate fluctuation reduction
     */
    internal static void SKP_Silk_NLSF_MSVQ_encode_FLP(
              int                   []NLSFIndices,       /* O    Codebook path vector [ CB_STAGES ]      */
              float                 []pNLSF,             /* I/O  Quantized NLSF vector [ LPC_ORDER ]     */
        SKP_Silk_NLSF_CB_FLP  psNLSF_CB_FLP,     /* I    Codebook object                         */
        float                 []pNLSF_q_prev,      /* I    Prev. quantized NLSF vector [LPC_ORDER] */
        float                 []pW,                /* I    NLSF weight vector [ LPC_ORDER ]        */
        float                 NLSF_mu,            /* I    Rate weight for the RD optimization     */
        float                 NLSF_mu_fluc_red,   /* I    Fluctuation reduction error weight      */
        int                   NLSF_MSVQ_Survivors,/* I    Max survivors from each stage           */
        int                   LPC_order,          /* I    LPC order                               */
        int                   deactivate_fluc_red,/* I    Deactivate fluctuation reduction        */
        EncoderWorkspace      workspace
    )
    {
        int     i, s, k, cur_survivors, prev_survivors, input_index, cb_index, bestIndex;
        float   se, wsse, rateDistThreshold, bestRateDist;
        float[] pNLSF_in = workspace.NlsfInput;

        float[] pRateDist;
        float[] pRate;
        float[] pRate_new;
        int[] pTempIndices;
        int[] pPath;
        int[] pPath_new;
        float[] pRes;
        float[] pRes_new;
        pRateDist = workspace.NlsfRateDist;
        pRate = workspace.NlsfRate;
        pRate_new = workspace.NlsfRateNew;
        pTempIndices = workspace.NlsfTempIndices;
        pPath = workspace.NlsfPath;
        pPath_new = workspace.NlsfPathNew;
        pRes = workspace.NlsfResidual;
        pRes_new = workspace.NlsfResidualNew;

        float[] pConstFloat;int pConstFloat_offset;
        float[] pFloat; int pFloat_offset;
        int[]   pConstInt; int pConstInt_offset;
        int[]   pInt; int pInt_offset;
        float[] pCB_element;int pCB_element_offset;
        SKP_Silk_NLSF_CBS_FLP pCurrentCBStage;

        System.Diagnostics.Debug.Assert( NLSF_MSVQ_Survivors <= MAX_NLSF_MSVQ_SURVIVORS );
        System.Diagnostics.Debug.Assert( ( !LOW_COMPLEXITY_ONLY ) || ( NLSF_MSVQ_Survivors <= MAX_NLSF_MSVQ_SURVIVORS_LC_MODE ) );

        cur_survivors = NLSF_MSVQ_Survivors;



        /* Copy the input vector */
        Array.Copy(pNLSF, 0, pNLSF_in, 0, LPC_order);

        /****************************************************/
        /* Tree search for the multi-stage vector quantizer */
        /****************************************************/

        /* Clear accumulated rates */
        Array.Fill(pRate, 0.0f, 0, NLSF_MSVQ_Survivors);

        /* Copy NLSFs into residual signal vector */
        Array.Copy(pNLSF, 0, pRes, 0, LPC_order);

        /* Set first stage values */
        prev_survivors = 1;

        /* Loop over all stages */
        for( s = 0; s < psNLSF_CB_FLP.nStages; s++ ) {

            /* Set a pointer to the current stage codebook */
            pCurrentCBStage = psNLSF_CB_FLP.CBStages[ s ];

            /* Calculate the number of survivors input the current stage */
            cur_survivors = Math.Min( NLSF_MSVQ_Survivors, prev_survivors * pCurrentCBStage.nVectors );

            if(!NLSF_MSVQ_FLUCTUATION_REDUCTION) {
                 /* Find a single best survivor input the last stage, if we */
                 /* do not need candidates for fluctuation reduction     */
                 if( s == psNLSF_CB_FLP.nStages - 1 ) {
                    cur_survivors = 1;
                 }
            }
            /* Nearest neighbor clustering for multiple input data vectors */
            NLSFVQRateDistortionFLP.SKP_Silk_NLSF_VQ_rate_distortion_FLP( pRateDist, pCurrentCBStage,
                    pRes, pW, pRate, NLSF_mu, prev_survivors, LPC_order, workspace.NlsfWeightCopy );

            /* Sort the rate-distortion errors */
            SortFLP.SKP_Silk_insertion_sort_increasing_FLP( pRateDist, 0, pTempIndices, prev_survivors * pCurrentCBStage.nVectors, cur_survivors );

            /* Discard survivors with rate-distortion values too far above the best one */
            rateDistThreshold = NLSF_MSVQ_SURV_MAX_REL_RD * pRateDist[ 0 ];
            while( pRateDist[ cur_survivors - 1 ] > rateDistThreshold && cur_survivors > 1 ) {
                cur_survivors--;
            }

            /* Update accumulated codebook contributions for the 'cur_survivors' best codebook indices */
            for( k = 0; k < cur_survivors; k++ ) {
                if( s > 0 ) {
                    /* Find the indices of the input and the codebook vector */
                    if( pCurrentCBStage.nVectors == 8 ) {
                        input_index = ( pTempIndices[ k ] >> 3 );
                        cb_index    = pTempIndices[ k ] & 7;
                    } else {
                        input_index = pTempIndices[ k ] / pCurrentCBStage.nVectors;
                        cb_index    = pTempIndices[ k ] - input_index * pCurrentCBStage.nVectors;
                    }
                } else {
                    /* Find the indices of the input and the codebook vector */
                    input_index = 0;
                    cb_index    = pTempIndices[ k ];
                }

                /* Subtract new contribution from the previous residual vector for each of 'cur_survivors' */
                pConstFloat = pRes;
                pConstFloat_offset = input_index * LPC_order;
                pCB_element = pCurrentCBStage.CB;
                pCB_element_offset = cb_index * LPC_order;
                pFloat      = pRes_new;
                pFloat_offset = k * LPC_order;
                for( i = 0; i < LPC_order; i++ ) {
                    pFloat[ pFloat_offset + i ] = pConstFloat[ pConstFloat_offset + i ] - pCB_element[ pCB_element_offset + i ];
                }

                /* Update accumulated rate for stage 1 to the current */
                pRate_new[ k ] = pRate[ input_index ] + pCurrentCBStage.Rates[ cb_index ];

                /* Copy paths from previous matrix, starting with the best path */
                pConstInt = pPath;
                pConstInt_offset = input_index * psNLSF_CB_FLP.nStages;
                pInt      = pPath_new;
                pInt_offset = k * psNLSF_CB_FLP.nStages;
                for( i = 0; i < s; i++ ) {
                    pInt[ pInt_offset + i ] = pConstInt[ pConstInt_offset + i ];
                }
                /* Write the current stage indices for the 'cur_survivors' to the best path matrix */
                pInt[ pInt_offset + s ] = cb_index;
            }

            if( s < psNLSF_CB_FLP.nStages - 1 ) {
                /* Copy NLSF residual matrix for next stage */
                Array.Copy(pRes_new, 0, pRes, 0, cur_survivors * LPC_order);

                /* Copy rate vector for next stage */
                Array.Copy(pRate_new, 0, pRate, 0, cur_survivors);

                /* Copy best path matrix for next stage */
                Array.Copy(pPath_new, 0, pPath, 0, cur_survivors * psNLSF_CB_FLP.nStages);
            }

            prev_survivors = cur_survivors;
        }

        /* (Preliminary) index of the best survivor, later to be decoded */
        bestIndex = 0;

        if (NLSF_MSVQ_FLUCTUATION_REDUCTION)
        {
            /******************************/
            /* NLSF fluctuation reduction */
            /******************************/
            if( deactivate_fluc_red != 1 ) {

                /* Search among all survivors, now taking also weighted fluctuation errors into account */
                bestRateDist = float.MaxValue;
                for( s = 0; s < cur_survivors; s++ ) {
                    /* Decode survivor to compare with previous quantized NLSF vector */
                    NLSFMSVQDecodeFLP.SKP_Silk_NLSF_MSVQ_decode_FLP( pNLSF, psNLSF_CB_FLP,
                            pPath_new, s * psNLSF_CB_FLP.nStages, LPC_order, workspace );

                    /* Compare decoded NLSF vector with the previously quantized vector */
                    wsse = 0;
                    for( i = 0; i < LPC_order; i += 2 ) {
                        /* Compute weighted squared quantization error for index i */
                        se = pNLSF[ i ] - pNLSF_q_prev[ i ];
                        wsse += pW[ i ] * se * se;

                        /* Compute weighted squared quantization error for index i + 1 */
                        se = pNLSF[ i + 1 ] - pNLSF_q_prev[ i + 1 ];
                        wsse += pW[ i + 1 ] * se * se;
                    }

                    /* Add the fluctuation reduction penalty to the rate distortion error */
                    wsse = pRateDist[s] + wsse * NLSF_mu_fluc_red;

                    /* Keep index of best survivor */
                    if( wsse < bestRateDist ) {
                        bestRateDist = wsse;
                        bestIndex = s;
                    }
                }
            }
        }

        /* Copy best path to output argument */
        Array.Copy(pPath_new, bestIndex * psNLSF_CB_FLP.nStages, NLSFIndices, 0, psNLSF_CB_FLP.nStages);

        /* Decode and stabilize the best survivor */
        NLSFMSVQDecodeFLP.SKP_Silk_NLSF_MSVQ_decode_FLP( pNLSF, psNLSF_CB_FLP, NLSFIndices, 0, LPC_order, workspace );
    }
}
