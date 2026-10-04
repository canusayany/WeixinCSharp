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
 * Portions adapted from SILK SDK 1.0.9.
 * Copyright (c) 2006-2012, Skype Limited. See THIRD-PARTY-NOTICES.
 */
using System;
using static SilkCodec.NET.Managed.Define;
using static SilkCodec.NET.Managed.Macros;
using static SilkCodec.NET.Managed.Typedef;

namespace SilkCodec.NET.Managed;

/**
 *
 * @author Jing Dai
 * @author Dingxin Xu
 */
internal static class FindPitchLagsFLP
{
    /**
     *
     * @param psEnc Encoder state FLP.
     * @param psEncCtrl Encoder control FLP.
     * @param res Residual.
     * @param x Speech signal.
     * @param x_offset offset of valid data.
     */
    internal static void SKP_Silk_find_pitch_lags_FLP(
            SKP_Silk_encoder_state_FLP      psEnc,             /* I/O  Encoder state FLP                       */
            SKP_Silk_encoder_control_FLP    psEncCtrl,         /* I/O  Encoder control FLP                     */
            float[] res,             /* O    Residual                                */
            float[] x,               /* I    Speech signal                           */
            int                             x_offset
        )
        {
            SKP_Silk_predict_state_FLP psPredSt = psEnc.sPred;
//            const SKP_float *x_buf_ptr, *x_buf;
            float[] x_buf_ptr, x_buf;
            int x_buf_ptr_offset, x_buf_offset;
            EncoderWorkspace encoderWorkspace = psEnc.workspace;
            float[] auto_corr = encoderWorkspace.PitchAutoCorrelation;
            float[] A = encoderWorkspace.PitchA;
            float[] refl_coef = encoderWorkspace.PitchReflection;
            float[] Wsig = encoderWorkspace.PitchWindow;
            float thrhld, res_nrg;
            float[] Wsig_ptr;
            int Wsig_ptr_offset;
            int   buf_len;

            /******************************************/
            /* Setup buffer lengths etc based of Fs   */
            /******************************************/
            buf_len = 2 * psEnc.sCmn.frame_length + psEnc.sCmn.la_pitch;

            /* Safty check */
            EncoderCompat.Assert( buf_len >= psPredSt.pitch_LPC_win_length );

//            x_buf = x - psEnc->sCmn.frame_length;
            x_buf = x;
            x_buf_offset = x_offset - psEnc.sCmn.frame_length;

            /*************************************/
            /* Estimate LPC AR coeficients */
            /*************************************/

            /* Calculate windowed signal */

            /* First LA_LTP samples */
//            x_buf_ptr = x_buf + buf_len - psPredSt->pitch_LPC_win_length;
            x_buf_ptr = x_buf;
            x_buf_ptr_offset = x_buf_offset + buf_len - psPredSt.pitch_LPC_win_length;
            Wsig_ptr  = Wsig;
            Wsig_ptr_offset=0;
            ApplySineWindowFLP.SKP_Silk_apply_sine_window_FLP( Wsig_ptr,Wsig_ptr_offset, x_buf_ptr,x_buf_ptr_offset, 1, psEnc.sCmn.la_pitch );

            /* Middle non-windowed samples */
            Wsig_ptr_offset  += psEnc.sCmn.la_pitch;
            x_buf_ptr_offset += psEnc.sCmn.la_pitch;
//            SKP_memcpy( Wsig_ptr, x_buf_ptr, ( psPredSt->pitch_LPC_win_length - ( psEnc->sCmn.la_pitch << 1 ) ) * sizeof( SKP_float ) );
            for(int i_djinn=0; i_djinn< psPredSt.pitch_LPC_win_length - ( psEnc.sCmn.la_pitch << 1 ); i_djinn++)
                Wsig_ptr[Wsig_ptr_offset + i_djinn]  = x_buf_ptr[x_buf_ptr_offset+i_djinn];

            /* Last LA_LTP samples */
            Wsig_ptr_offset  += psPredSt.pitch_LPC_win_length - ( psEnc.sCmn.la_pitch << 1 );
            x_buf_ptr_offset += psPredSt.pitch_LPC_win_length - ( psEnc.sCmn.la_pitch << 1 );
            ApplySineWindowFLP.SKP_Silk_apply_sine_window_FLP( Wsig_ptr,Wsig_ptr_offset, x_buf_ptr,x_buf_ptr_offset, 2, psEnc.sCmn.la_pitch );

            /* Calculate autocorrelation sequence */
            AutocorrelationFLP.SKP_Silk_autocorrelation_FLP( auto_corr,0, Wsig,0, psPredSt.pitch_LPC_win_length, psEnc.sCmn.pitchEstimationLPCOrder + 1 );

            /* Add white noise, as a fraction of the energy */
            auto_corr[ 0 ] += auto_corr[ 0 ] * DefineFLP.FIND_PITCH_WHITE_NOISE_FRACTION;

            /* Calculate the reflection coefficients using Schur */
            res_nrg = SchurFLP.SKP_Silk_schur_FLP( refl_coef,0, auto_corr,0, psEnc.sCmn.pitchEstimationLPCOrder,
                encoderWorkspace.SchurCorrelation );

            /* Prediction gain */
            psEncCtrl.predGain = auto_corr[ 0 ] / Math.Max( res_nrg, 1.0f );

            /* Convert reflection coefficients to prediction coefficients */
            K2aFLP.SKP_Silk_k2a_FLP( A, refl_coef, psEnc.sCmn.pitchEstimationLPCOrder,
                encoderWorkspace.PitchK2ATemp );

            /* Bandwidth expansion */
            BwexpanderFLP.SKP_Silk_bwexpander_FLP( A,0, psEnc.sCmn.pitchEstimationLPCOrder, DefineFLP.FIND_PITCH_BANDWITH_EXPANSION );

            /*****************************************/
            /* LPC analysis filtering               */
            /*****************************************/
            LPCAnalysisFilterFLP.SKP_Silk_LPC_analysis_filter_FLP( res, A, x_buf, x_buf_offset, buf_len, psEnc.sCmn.pitchEstimationLPCOrder );
//            SKP_memset( res, 0, psEnc->sCmn.pitchEstimationLPCOrder * sizeof( SKP_float ) );
            for(int i_djinn=0; i_djinn<psEnc.sCmn.pitchEstimationLPCOrder; i_djinn++)
                res[i_djinn] = 0;

            /* Threshold for pitch estimator */
            thrhld  = 0.45f;
            thrhld -= 0.004f * psEnc.sCmn.pitchEstimationLPCOrder;
            thrhld -= 0.1f  * psEnc.speech_activity;
            thrhld += 0.15f * psEnc.sCmn.prev_sigtype;
            thrhld -= 0.1f  * psEncCtrl.input_tilt;

            /*****************************************/
            /* Call Pitch estimator */
            /*****************************************/
            PitchAnalysisCoreFLP.Workspace workspace = psEnc.pitchAnalysisWorkspace;
            int[] lagIndex_djinnaddress = workspace.LagIndex;
            int[] contourIndex_djinnaddress = workspace.ContourIndex;
            float[] LTPCorr_djinnaddress = workspace.LtpCorrelation;
            lagIndex_djinnaddress[0] = psEncCtrl.sCmn.lagIndex;
            contourIndex_djinnaddress[0] = psEncCtrl.sCmn.contourIndex;
            LTPCorr_djinnaddress[0] = psEnc.LTPCorr;
            psEncCtrl.sCmn.sigtype = PitchAnalysisCoreFLP.SKP_Silk_pitch_analysis_core_FLP( res, psEncCtrl.sCmn.pitchL, lagIndex_djinnaddress,
                    contourIndex_djinnaddress, LTPCorr_djinnaddress, psEnc.sCmn.prevLag, psEnc.pitchEstimationThreshold,
                thrhld, psEnc.sCmn.fs_kHz, psEnc.sCmn.pitchEstimationComplexity, workspace );
            psEncCtrl.sCmn.lagIndex = lagIndex_djinnaddress[0];
            psEncCtrl.sCmn.contourIndex = contourIndex_djinnaddress[0];
            psEnc.LTPCorr = LTPCorr_djinnaddress[0];
        }
}
