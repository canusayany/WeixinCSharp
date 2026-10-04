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
 * Quantizer lambda calculation adapted from SILK SDK 1.0.9.
 * Copyright (c) 2006-2012, Skype Limited. See THIRD-PARTY-NOTICES.
 */
using System;
using static SilkCodec.NET.Managed.Define;
using static SilkCodec.NET.Managed.Macros;
using static SilkCodec.NET.Managed.Typedef;

namespace SilkCodec.NET.Managed;

/**
 * processing of gains.
 *
 * @author Dingxin Xu
 */
internal static class ProcessGainsFLP
{
    /**
     * Processing of gains.
     * @param psEnc Encoder state FLP
     * @param psEncCtrl Encoder control FLP
     */
    internal static void SKP_Silk_process_gains_FLP(
        SKP_Silk_encoder_state_FLP      psEnc,             /* I/O  Encoder state FLP                       */
        SKP_Silk_encoder_control_FLP    psEncCtrl          /* I/O  Encoder control FLP                     */
    )
    {
        SKP_Silk_shape_state_FLP psShapeSt = psEnc.sShape;
        int     k;
        int[] pGains_Q16 = psEnc.workspace.ProcessGainsQ16;
        float   s, InvMaxSqrVal, gain, quant_offset;

        /* Gain reduction when LTP coding gain is high */
        if( psEncCtrl.sCmn.sigtype == SIG_TYPE_VOICED ) {
            s = 1.0f - 0.5f * SigProcFLP.SKP_sigmoid( 0.25f * ( psEncCtrl.LTPredCodGain - 12.0f ) );
            for( k = 0; k < NB_SUBFR; k++ ) {
                psEncCtrl.Gains[ k ] *= s;
            }
        }

        /* Limit the quantized signal */
        InvMaxSqrVal = ( float )( Math.Pow( 2.0f, 0.33f * ( 21.0f - psEncCtrl.current_SNR_dB ) ) / psEnc.sCmn.subfr_length );

        for( k = 0; k < NB_SUBFR; k++ ) {
            /* Soft limit on ratio residual energy and squared gains */
            gain = psEncCtrl.Gains[ k ];
            gain = ( float )Math.Sqrt( gain * gain + psEncCtrl.ResNrg[ k ] * InvMaxSqrVal );
            psEncCtrl.Gains[ k ] = ( gain < 32767.0f ? gain : 32767.0f );
        }

        /* Prepare gains for noise shaping quantization */
        for( k = 0; k < NB_SUBFR; k++ ) {
            pGains_Q16[ k ] = ( int ) ( psEncCtrl.Gains[ k ] * 65536.0f );
        }

        /* Noise shaping quantization */
        int[] LastGainIndex_ptr = psEnc.workspace.ScalarInt;
        LastGainIndex_ptr[0] = psShapeSt.LastGainIndex;
        GainQuant.SKP_Silk_gains_quant( psEncCtrl.sCmn.GainsIndices, pGains_Q16,
                LastGainIndex_ptr, psEnc.sCmn.nFramesInPayloadBuf );
        psShapeSt.LastGainIndex = LastGainIndex_ptr[0];
        /* Overwrite unquantized gains with quantized gains and convert back to Q0 from Q16 */
        for( k = 0; k < NB_SUBFR; k++ ) {
            psEncCtrl.Gains[ k ] = pGains_Q16[ k ] / 65536.0f;
        }

        /* Set quantizer offset for voiced signals. Larger offset when LTP coding gain is low or tilt is high (ie low-pass) */
        if( psEncCtrl.sCmn.sigtype == SIG_TYPE_VOICED ) {
            if( psEncCtrl.LTPredCodGain + psEncCtrl.input_tilt > 1.0f ) {
                psEncCtrl.sCmn.QuantOffsetType = 0;
            } else {
                psEncCtrl.sCmn.QuantOffsetType = 1;
            }
        }

        /* Quantizer boundary adjustment */
        quant_offset = TablesOther.SKP_Silk_Quantization_Offsets_Q10[ psEncCtrl.sCmn.sigtype ][ psEncCtrl.sCmn.QuantOffsetType ] / 1024.0f;
        psEncCtrl.Lambda = DefineFLP.LAMBDA_OFFSET
                         + DefineFLP.LAMBDA_DELAYED_DECISIONS * psEnc.sCmn.nStatesDelayedDecision
                         + DefineFLP.LAMBDA_SPEECH_ACT * psEnc.speech_activity
                         + DefineFLP.LAMBDA_INPUT_QUALITY * psEncCtrl.input_quality
                         + DefineFLP.LAMBDA_CODING_QUALITY * psEncCtrl.coding_quality
                         + DefineFLP.LAMBDA_QUANT_OFFSET * quant_offset;

        EncoderCompat.Assert( psEncCtrl.Lambda >  0.0f );
        EncoderCompat.Assert( psEncCtrl.Lambda <  2.0f );
    }
}
