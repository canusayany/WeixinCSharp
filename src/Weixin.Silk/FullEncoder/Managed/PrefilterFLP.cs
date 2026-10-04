/*
 * Copyright @ 2015 Atlassian Pty Ltd
 *
 * Licensed under the Apache License, Version 2.0 (the "License");
 * you may not use this file except in compliance with the License.
 * You may obtain a copy of the License at
 *
 *     http://www.apache.org/licenses/LICENSE-2.0
 *
 * Unless required by applicable law or agreed to in writing, software
 * distributed under the License is distributed on an "AS IS" BASIS,
 * WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
 * See the License for the specific language governing permissions and
 * limitations under the License.
 */
/*
 * Warped LPC analysis filtering adapted from SILK SDK 1.0.9.
 * Copyright (c) 2006-2012, Skype Limited. See THIRD-PARTY-NOTICES.
 */
using System;
using static SilkCodec.NET.Managed.Define;
using static SilkCodec.NET.Managed.Macros;
using static SilkCodec.NET.Managed.Typedef;

namespace SilkCodec.NET.Managed;


/**
 *
 * @author Dingxin Xu
 */
internal static class PrefilterFLP
{
    /**
     * SKP_Silk_prefilter. Main Prefilter Function.
     * @param psEnc Encoder state FLP.
     * @param psEncCtrl Encoder control FLP.
     * @param xw  Weighted signal.
     * @param x Speech signal.
     * @param x_offset offset of valid data.
     */
    internal static void SKP_Silk_prefilter_FLP(
        SKP_Silk_encoder_state_FLP    psEnc,         /* I/O  Encoder state FLP                       */
        SKP_Silk_encoder_control_FLP  psEncCtrl,     /* I    Encoder control FLP                     */
              float[] xw,          /* O    Weighted signal                         */
              float[] x,           /* I    Speech signal                           */
              int                     x_offset
    )
    {
        SKP_Silk_prefilter_state_FLP P = psEnc.sPrefilt;
        int   j, k, lag;
        float HarmShapeGain, Tilt, LF_MA_shp, LF_AR_shp;
        float[] B = psEnc.workspace.PrefilterB;
        float[] px;
        int px_offset;
        float[] pxw, pst_res;
        int pxw_offset;
        float[] HarmShapeFIR = psEnc.workspace.PrefilterHarmShape;
        float[] st_res = psEnc.workspace.PrefilterStateResidual;

        /* Setup pointers */
        px  = x;
        px_offset = x_offset;
        pxw = xw;
        pxw_offset = 0;
        lag = P.lagPrev;
        for( k = 0; k < NB_SUBFR; k++ )
        {
            /* Update Variables that change per sub frame */
            if( psEncCtrl.sCmn.sigtype == SIG_TYPE_VOICED )
            {
                lag = psEncCtrl.sCmn.pitchL[ k ];
            }

            /* Noise shape parameters */
            HarmShapeGain = psEncCtrl.HarmShapeGain[ k ] * ( 1.0f - psEncCtrl.HarmBoost[ k ] );
            HarmShapeFIR[ 0 ] = TablesOtherFLP.SKP_Silk_HarmShapeFIR_FLP[ 0 ] * HarmShapeGain;
            HarmShapeFIR[ 1 ] = TablesOtherFLP.SKP_Silk_HarmShapeFIR_FLP[ 1 ] * HarmShapeGain;
            HarmShapeFIR[ 2 ] = TablesOtherFLP.SKP_Silk_HarmShapeFIR_FLP[ 2 ] * HarmShapeGain;
            Tilt      =  psEncCtrl.Tilt[ k ];
            LF_MA_shp =  psEncCtrl.LF_MA_shp[ k ];
            LF_AR_shp =  psEncCtrl.LF_AR_shp[ k ];
            /* Stateful warped short-term analysis filtering. */
            SKP_Silk_warped_LPC_analysis_filter_FLP( P.sAR_shp1, st_res, 0,
                psEncCtrl.AR1, k * SHAPE_LPC_ORDER_MAX, px, px_offset,
                psEnc.sCmn.warping_Q16 / 65536.0f, psEnc.sCmn.subfr_length, psEnc.sCmn.shapingLPCOrder );

            pst_res = st_res;
            int pst_res_offset = 0;

            /* reduce (mainly) low frequencies during harmonic emphasis */
            B[ 0 ] =  psEncCtrl.GainsPre[ k ];
            B[ 1 ] = -psEncCtrl.GainsPre[ k ] *
                ( psEncCtrl.HarmBoost[ k ] * HarmShapeGain + PerceptualParametersFLP.INPUT_TILT +
                        psEncCtrl.coding_quality * PerceptualParametersFLP.HIGH_RATE_INPUT_TILT );
            pxw[ pxw_offset + 0 ] = B[ 0 ] * pst_res[ pst_res_offset + 0 ] + B[ 1 ] * P.sHarmHP;
            for( j = 1; j < psEnc.sCmn.subfr_length; j++ ) {
                pxw[ pxw_offset + j ] = B[ 0 ] * pst_res[ pst_res_offset + j ] + B[ 1 ] * pst_res[ pst_res_offset + j - 1 ];
            }
            P.sHarmHP = pst_res[ pst_res_offset + psEnc.sCmn.subfr_length - 1 ];

            SKP_Silk_prefilt_FLP( P, pxw, pxw_offset, pxw, pxw_offset, HarmShapeFIR, Tilt, LF_MA_shp, LF_AR_shp, lag, psEnc.sCmn.subfr_length );

            px_offset  += psEnc.sCmn.subfr_length;
            pxw_offset += psEnc.sCmn.subfr_length;
        }
        P.lagPrev = psEncCtrl.sCmn.pitchL[ NB_SUBFR - 1 ];
    }

    internal static void SKP_Silk_warped_LPC_analysis_filter_FLP(
        float[] state, float[] res, int res_offset, float[] coef, int coef_offset,
        float[] input, int input_offset, float lambda, int length, int order )
    {
        EncoderCompat.Assert( ( order & 1 ) == 0 );
        for( int n = 0; n < length; n++ )
        {
            float tmp2 = state[ 0 ] + lambda * state[ 1 ];
            state[ 0 ] = input[ input_offset + n ];
            float tmp1 = state[ 1 ] + lambda * ( state[ 2 ] - tmp2 );
            state[ 1 ] = tmp2;
            float acc = coef[ coef_offset ] * tmp2;
            for( int i = 2; i < order; i += 2 )
            {
                tmp2 = state[ i ] + lambda * ( state[ i + 1 ] - tmp1 );
                state[ i ] = tmp1;
                acc += coef[ coef_offset + i - 1 ] * tmp1;
                tmp1 = state[ i + 1 ] + lambda * ( state[ i + 2 ] - tmp2 );
                state[ i + 1 ] = tmp2;
                acc += coef[ coef_offset + i ] * tmp2;
            }
            state[ order ] = tmp1;
            acc += coef[ coef_offset + order - 1 ] * tmp1;
            res[ res_offset + n ] = input[ input_offset + n ] - acc;
        }
    }

    /**
     * SKP_Silk_prefilter_part1. Prefilter for finding Quantizer input signal.
     * @param P
     * @param st_res
     * @param st_res_offset
     * @param xw
     * @param xw_offset
     * @param HarmShapeFIR
     * @param Tilt
     * @param LF_MA_shp
     * @param LF_AR_shp
     * @param lag
     * @param length
     */
    internal static void SKP_Silk_prefilt_FLP(
        SKP_Silk_prefilter_state_FLP P,/* (I/O) state */
        float[] st_res,             /* (I) */
        int   st_res_offset,
        float[] xw,                 /* (O) */
        int   xw_offset,
        float[] HarmShapeFIR,        /* (I) */
        float Tilt,                 /* (I) */
        float LF_MA_shp,            /* (I) */
        float LF_AR_shp,            /* (I) */
        int   lag,                  /* (I) */
        int   length                /* (I) */
    )
    {
        int   i;
        int   idx, LTP_shp_buf_idx;
        float n_Tilt, n_LF, n_LTP;
        float sLF_AR_shp, sLF_MA_shp;
        float[] LTP_shp_buf;

        /* To speed up use temp variables instead of using the struct */
        LTP_shp_buf     = P.sLTP_shp1;
        LTP_shp_buf_idx = P.sLTP_shp_buf_idx1;
        sLF_AR_shp      = P.sLF_AR_shp1;
        sLF_MA_shp      = P.sLF_MA_shp1;

        for( i = 0; i < length; i++ ) {
            if( lag > 0 ) {
                EncoderCompat.Assert( HARM_SHAPE_FIR_TAPS == 3 );
                idx = lag + LTP_shp_buf_idx;
                n_LTP  = LTP_shp_buf[ ( idx - HARM_SHAPE_FIR_TAPS / 2 - 1) & LTP_MASK ] * HarmShapeFIR[ 0 ];
                n_LTP += LTP_shp_buf[ ( idx - HARM_SHAPE_FIR_TAPS / 2    ) & LTP_MASK ] * HarmShapeFIR[ 1 ];
                n_LTP += LTP_shp_buf[ ( idx - HARM_SHAPE_FIR_TAPS / 2 + 1) & LTP_MASK ] * HarmShapeFIR[ 2 ];
            } else {
                n_LTP = 0;
            }

            n_Tilt = sLF_AR_shp * Tilt;
            n_LF   = sLF_AR_shp * LF_AR_shp + sLF_MA_shp * LF_MA_shp;

            sLF_AR_shp = st_res[ st_res_offset + i ] - n_Tilt;
            sLF_MA_shp = sLF_AR_shp - n_LF;

            LTP_shp_buf_idx = ( LTP_shp_buf_idx - 1 ) & LTP_MASK;
            LTP_shp_buf[ LTP_shp_buf_idx ] = sLF_MA_shp;
            xw[ xw_offset + i ] = sLF_MA_shp - n_LTP;
        }
        /* Copy temp variable back to state */
        P.sLF_AR_shp1       = sLF_AR_shp;
        P.sLF_MA_shp1       = sLF_MA_shp;
        P.sLTP_shp_buf_idx1 = LTP_shp_buf_idx;
    }
}
