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
 * Encode parameters to create the payload.
 *
 * @author Dingxin Xu
 */
internal class EncodeParameters
{
    /**
     * Encode parameters to create the payload.
     * @param psEncC Encoder state.
     * @param psEncCtrlC Encoder control.
     * @param psRC Range encoder state.
     * @param q Quantization indices.
     */
    internal static void SKP_Silk_encode_parameters(
        SKP_Silk_encoder_state          psEncC,        /* I/O  Encoder state                   */
        SKP_Silk_encoder_control        psEncCtrlC,    /* I/O  Encoder control                 */
        SKP_Silk_range_coder_state      psRC,          /* I/O  Range encoder state             */
        byte                            []q            /* I    Quantization indices            */
    )
    {
        int   i, k, typeOffset;
        SKP_Silk_NLSF_CB_struct psNLSF_CB;


        /************************/
        /* Encode sampling rate */
        /************************/
        /* only done for first frame input packet */
        if( psEncC.nFramesInPayloadBuf == 0 ) {
            /* get sampling rate index */
            for( i = 0; i < 3; i++ ) {
                if( TablesOther.SKP_Silk_SamplingRates_table[ i ] == psEncC.fs_kHz ) {
                    break;
                }
            }
            RangeCoder.SKP_Silk_range_encoder( psRC, i, TablesOther.SKP_Silk_SamplingRates_CDF, 0 );
        }

        /*******************************************/
        /* Encode signal type and quantizer offset */
        /*******************************************/
        typeOffset = 2 * psEncCtrlC.sigtype + psEncCtrlC.QuantOffsetType;
        if( psEncC.nFramesInPayloadBuf == 0 ) {
            /* first frame input packet: independent coding */
            RangeCoder.SKP_Silk_range_encoder( psRC, typeOffset, TablesTypeOffset.SKP_Silk_type_offset_CDF, 0);
        } else {
            /* condidtional coding */
            RangeCoder.SKP_Silk_range_encoder( psRC, typeOffset, TablesTypeOffset.SKP_Silk_type_offset_joint_CDF[ psEncC.typeOffsetPrev ], 0);
        }
        psEncC.typeOffsetPrev = typeOffset;

        /****************/
        /* Encode gains */
        /****************/
        /* first subframe */
        if( psEncC.nFramesInPayloadBuf == 0 ) {
            /* first frame input packet: independent coding */
            RangeCoder.SKP_Silk_range_encoder( psRC, psEncCtrlC.GainsIndices[ 0 ], TablesGain.SKP_Silk_gain_CDF[ psEncCtrlC.sigtype ], 0);
        } else {
            /* condidtional coding */
            RangeCoder.SKP_Silk_range_encoder( psRC, psEncCtrlC.GainsIndices[ 0 ], TablesGain.SKP_Silk_delta_gain_CDF, 0);
        }

        /* remaining subframes */
        for( i = 1; i < NB_SUBFR; i++ ) {
            RangeCoder.SKP_Silk_range_encoder( psRC, psEncCtrlC.GainsIndices[ i ], TablesGain.SKP_Silk_delta_gain_CDF, 0);
        }


        /****************/
        /* Encode NLSFs */
        /****************/
        /* Range encoding of the NLSF path */
        psNLSF_CB = psEncC.psNLSF_CB[ psEncCtrlC.sigtype ];
        RangeCoder.SKP_Silk_range_encoder_multi( psRC, psEncCtrlC.NLSFIndices, psNLSF_CB.StartPtr, psNLSF_CB.nStages );

        /* Encode NLSF interpolation factor */
        System.Diagnostics.Debug.Assert( psEncC.useInterpolatedNLSFs == 1 || psEncCtrlC.NLSFInterpCoef_Q2 == ( 1 << 2 ) );
        RangeCoder.SKP_Silk_range_encoder( psRC, psEncCtrlC.NLSFInterpCoef_Q2, TablesOther.SKP_Silk_NLSF_interpolation_factor_CDF, 0);


        if( psEncCtrlC.sigtype == SIG_TYPE_VOICED ) {
            /*********************/
            /* Encode pitch lags */
            /*********************/


            /* lag index */
            if( psEncC.fs_kHz == 8 ) {
                RangeCoder.SKP_Silk_range_encoder( psRC, psEncCtrlC.lagIndex, TablesPitchLag.SKP_Silk_pitch_lag_NB_CDF, 0);
            } else if( psEncC.fs_kHz == 12 ) {
                RangeCoder.SKP_Silk_range_encoder( psRC, psEncCtrlC.lagIndex, TablesPitchLag.SKP_Silk_pitch_lag_MB_CDF, 0);
            } else if( psEncC.fs_kHz == 16 ) {
                RangeCoder.SKP_Silk_range_encoder( psRC, psEncCtrlC.lagIndex, TablesPitchLag.SKP_Silk_pitch_lag_WB_CDF, 0);
            } else {
                RangeCoder.SKP_Silk_range_encoder( psRC, psEncCtrlC.lagIndex, TablesPitchLag.SKP_Silk_pitch_lag_SWB_CDF, 0);
            }


            /* countour index */
            if( psEncC.fs_kHz == 8 ) {
                /* Less codevectors used input 8 khz mode */
                RangeCoder.SKP_Silk_range_encoder( psRC, psEncCtrlC.contourIndex, TablesPitchLag.SKP_Silk_pitch_contour_NB_CDF, 0);
            } else {
                /* Joint for 12, 16, 24 khz */
                RangeCoder.SKP_Silk_range_encoder( psRC, psEncCtrlC.contourIndex, TablesPitchLag.SKP_Silk_pitch_contour_CDF, 0);
            }

            /********************/
            /* Encode LTP gains */
            /********************/

            /* PERIndex value */
            RangeCoder.SKP_Silk_range_encoder( psRC, psEncCtrlC.PERIndex, TablesLTP.SKP_Silk_LTP_per_index_CDF, 0);

            /* Codebook Indices */
            for( k = 0; k < NB_SUBFR; k++ ) {
                RangeCoder.SKP_Silk_range_encoder( psRC, psEncCtrlC.LTPIndex[ k ], TablesLTP.SKP_Silk_LTP_gain_CDF_ptrs[ psEncCtrlC.PERIndex ], 0);
            }

            /**********************/
            /* Encode LTP scaling */
            /**********************/
            RangeCoder.SKP_Silk_range_encoder( psRC, psEncCtrlC.LTP_scaleIndex, TablesOther.SKP_Silk_LTPscale_CDF, 0);
        }


        /***************/
        /* Encode seed */
        /***************/
        RangeCoder.SKP_Silk_range_encoder( psRC, psEncCtrlC.Seed, TablesOther.SKP_Silk_Seed_CDF, 0);

        /*********************************************/
        /* Encode quantization indices of excitation */
        /*********************************************/
        EncodePulses.SKP_Silk_encode_pulses( psRC, psEncCtrlC.sigtype, psEncCtrlC.QuantOffsetType, q, psEncC.frame_length,
            psEncC.encodeAbsPulses, psEncC.encodeSumPulses, psEncC.encodePulseShifts,
            psEncC.encodePulsesCombined, psEncC.shellEncoderScratch );


        /*********************************************/
        /* Encode VAD flag                           */
        /*********************************************/
        RangeCoder.SKP_Silk_range_encoder( psRC, psEncC.vadFlag, TablesOther.SKP_Silk_vadflag_CDF, 0);
    }
}
