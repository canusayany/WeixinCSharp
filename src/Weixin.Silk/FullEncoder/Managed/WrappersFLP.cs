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
/*
 * Warped NSQ dispatch and conversion behavior adapted from SILK SDK 1.0.9.
 * Copyright (c) 2006-2012, Skype Limited. See THIRD-PARTY-NOTICES.
 */
/**
 *
 * @author Jing Dai
 * @author Dingxin Xu
 */
internal static class WrappersFLP
{
    /* Wrappers. Calls flp / fix code */
    internal static long ar2_q13_file_offset = 0;
    internal static long x_16_file_offset = 0;
    internal static int frame_cnt = 0;

    /* Convert AR filter coefficients to NLSF parameters */
    internal static void SKP_Silk_A2NLSF_FLP(
              float[]               pNLSF,             /* O    NLSF vector      [ LPC_order ]          */
              float[]               pAR,               /* I    LPC coefficients [ LPC_order ]          */
        int                   LPC_order,         /* I    LPC order                               */
        EncoderWorkspace      workspace
    )
    {
        int   i;
        int[] NLSF_fix = workspace.FixedNlsf;
        int[] a_fix_Q16 = workspace.FixedA;

        for( i = 0; i < LPC_order; i++ )
        {
            a_fix_Q16[ i ] = SigProcFLP.SKP_float2int( pAR[ i ] * 65536.0f );
        }
        A2NLSF.SKP_Silk_A2NLSF( NLSF_fix, a_fix_Q16, LPC_order, workspace.A2NlsfP, workspace.A2NlsfQ );

        for( i = 0; i < LPC_order; i++ )
        {
            pNLSF[ i ] = NLSF_fix[ i ] * ( 1.0f / 32768.0f );
        }
    }

    /* Convert LSF parameters to AR prediction filter coefficients */
    internal static void SKP_Silk_NLSF2A_stable_FLP(
              float []                pAR,               /* O    LPC coefficients [ LPC_order ]          */
              float[]                 pNLSF,             /* I    NLSF vector      [ LPC_order ]          */
        int                     LPC_order,         /* I    LPC order                               */
        EncoderWorkspace        workspace
    )
    {
        int   i;
        int[] NLSF_fix = workspace.FixedNlsf;
        short[] a_fix_Q12 = workspace.FixedAShort;

        for( i = 0; i < LPC_order; i++ )
        {
            NLSF_fix[ i ] = SigProcFLP.SKP_float2int( pNLSF[ i ] * 32768.0f );
        }

        NLSF2AStable.SKP_Silk_NLSF2A_stable( a_fix_Q12, NLSF_fix, LPC_order, workspace.NlsfInverseGain,
            workspace );

        for( i = 0; i < LPC_order; i++ )
        {
            pAR[ i ] = a_fix_Q12[ i ] / 4096.0f;
        }
    }


    /* LSF stabilizer, for a single input data vector */
    internal static void SKP_Silk_NLSF_stabilize_FLP(
              float[]                 pNLSF,             /* I/O  (Un)stable NLSF vector [ LPC_order ]    */
              float[]                 pNDelta_min,       /* I    Normalized delta min vector[LPC_order+1]*/
        int                     LPC_order,         /* I    LPC order                               */
        EncoderWorkspace        workspace
    )
    {
        int   i;
        int[] NLSF_Q15 = workspace.NlsfStabilized, ndelta_min_Q15 = workspace.NlsfDeltaMin;

        for( i = 0; i < LPC_order; i++ )
        {
            NLSF_Q15[       i ] = SigProcFLP.SKP_float2int( pNLSF[       i ] * 32768.0f );
            ndelta_min_Q15[ i ] = SigProcFLP.SKP_float2int( pNDelta_min[ i ] * 32768.0f );
        }
        ndelta_min_Q15[ LPC_order ] = SigProcFLP.SKP_float2int( pNDelta_min[ LPC_order ] * 32768.0f );

        /* NLSF stabilizer, for a single input data vector */
        NLSFStabilize.SKP_Silk_NLSF_stabilize( NLSF_Q15,0, ndelta_min_Q15, LPC_order );

        for( i = 0; i < LPC_order; i++ )
        {
            pNLSF[ i ] = NLSF_Q15[ i ] * ( 1.0f / 32768.0f );
        }
    }

    /* Interpolation function with fixed point rounding */
    internal static void SKP_Silk_interpolate_wrapper_FLP(
              float[] xi,               /* O    Interpolated vector                     */
              float[] x0,               /* I    First vector                            */
              float[] x1,               /* I    Second vector                           */
        float                 ifact,              /* I    Interp. factor, weight on second vector */
        int                   d,                  /* I    Number of parameters                    */
        EncoderWorkspace      workspace
    )
    {
        int[] x0_int = workspace.Interpolate0, x1_int = workspace.Interpolate1, xi_int = workspace.InterpolateResult;
        int ifact_Q2 = ( int )( ifact * 4.0f );
        int i;

        /* Convert input from flp to fix */
        for( i = 0; i < d; i++ ) {
            x0_int[ i ] = SigProcFLP.SKP_float2int( x0[ i ] * 32768.0f );
            x1_int[ i ] = SigProcFLP.SKP_float2int( x1[ i ] * 32768.0f );
        }

        /* Interpolate two vectors */
        Interpolate.SKP_Silk_interpolate( xi_int, x0_int, x1_int, ifact_Q2, d );

        /* Convert output from fix to flp */
        for( i = 0; i < d; i++ )
        {
            xi[ i ] = xi_int[ i ] * ( 1.0f / 32768.0f );
        }
    }

    /****************************************/
    /* Floating-point Silk VAD wrapper      */
    /****************************************/
    internal static int SKP_Silk_VAD_FLP(
        SKP_Silk_encoder_state_FLP      psEnc,             /* I/O  Encoder state FLP                       */
        SKP_Silk_encoder_control_FLP    psEncCtrl,         /* I/O  Encoder control FLP                     */
        short[]                         pIn,               /* I    Input signal                            */
        int pIn_offset
    )
    {
        int i, ret;
        int[] SA_Q8 = psEnc.workspace.VadSpeechActivity;
        int[] SNR_dB_Q7 = psEnc.workspace.VadSnr;
        int[] Tilt_Q15 = psEnc.workspace.VadTilt;
        int[] Quality_Bands_Q15 = psEnc.workspace.VadQuality;

        ret = VAD.SKP_Silk_VAD_GetSA_Q8( psEnc.sCmn.sVAD, SA_Q8, SNR_dB_Q7, Quality_Bands_Q15, Tilt_Q15,
            pIn,pIn_offset, psEnc.sCmn.frame_length, psEnc.workspace );

        psEnc.speech_activity = SA_Q8[0] / 256.0f;
        for( i = 0; i < VAD_N_BANDS; i++ )
        {
            psEncCtrl.input_quality_bands[ i ] = Quality_Bands_Q15[ i ] / 32768.0f;
        }
        psEncCtrl.input_tilt = Tilt_Q15[0] / 32768.0f;

        return ret;
    }

    /****************************************/
    /* Floating-point Silk NSQ wrapper      */
    /****************************************/
    internal static void SKP_Silk_NSQ_wrapper_FLP(
        SKP_Silk_encoder_state_FLP      psEnc,         /* I/O  Encoder state FLP                           */
        SKP_Silk_encoder_control_FLP    psEncCtrl,     /* I/O  Encoder control FLP                         */
              float[] x,            /* I    Prefiltered input signal                    */
              int x_offset,
              byte[] q,            /* O    Quantized pulse signal                      */
              int q_offset,
        int                   useLBRR         /* I    LBRR flag                                   */
    )
    {
        int     i, j;
        float   tmp_float;
        short[]   x_16 = psEnc.nsqInput;
        /* Prediction and coding parameters */
        int[]   Gains_Q16 = psEnc.nsqGainsQ16;
        short[][] PredCoef_Q12 = psEnc.nsqPredCoefQ12;
        short[]   LTPCoef_Q14 = psEnc.nsqLtpCoefQ14;
        int     LTP_scale_Q14;

        /* Noise shaping parameters */
        /* Testing */
        short[] AR2_Q13 = psEnc.nsqAr2Q13;
        int[]   LF_shp_Q14 = psEnc.nsqLfShapeQ14;         /* Packs two int16 coefficients per int32 value             */
        int     Lambda_Q10;
        int[]     Tilt_Q14 = psEnc.nsqTiltQ14;
        int[]     HarmShapeGain_Q14 = psEnc.nsqHarmShapeGainQ14;

        /* Convert control struct to fix control struct */
        /* Noise shape parameters */
        for( i = 0; i < NB_SUBFR; i++ )
        {
            for( j = 0; j < psEnc.sCmn.shapingLPCOrder; j++ )
            {
                AR2_Q13[ i * SHAPE_LPC_ORDER_MAX + j ] = (short)SigProcFLP.SKP_float2int(
                    psEncCtrl.AR2[ i * SHAPE_LPC_ORDER_MAX + j ] * 8192.0f );
            }
            for( ; j < SHAPE_LPC_ORDER_MAX; j++ )
            {
                AR2_Q13[ i * SHAPE_LPC_ORDER_MAX + j ] = 0;
            }
        }

        /*TEST************************************************************************/
        /*
         * test of the AR2_Q13
         *
         */
//        short[] ar2_q13 = new short[ NB_SUBFR * SHAPE_LPC_ORDER_MAX ];
//        String ar2_q13_filename = "D:/gsoc/ar2_q13";
//
//        /*
//         * Option 1:
//         */
//        DataInputStream ar2_q13_datain = null;
//        try
//        {
//            ar2_q13_datain = new DataInputStream(
//                                                 new FileInputStream(
//                                                     new File(ar2_q13_filename)));
//
//            for( i = 0; i < NB_SUBFR * SHAPE_LPC_ORDER_MAX; i++ )
//            {
//     //           AR2_Q13[ i ] = (short)SigProcFIX.SKP_SAT16( SigProcFLP.SKP_float2int( psEncCtrl.AR2[ i ] * 8192.0f ) );
//                  try
//                {
//                    ar2_q13[i] = ar2_q13_datain.readShort();
//                    AR2_Q13[i] = (short) (((ar2_q13[i] << 8) & 0xFF00) | ((ar2_q13[i] >>> 8) & 0x00FF));
//                }
//                catch (IOException e)
//                {
//                    // TODO Auto-generated catch block
//                    e.printStackTrace();
//                }
//            }
//            try
//            {
//                ar2_q13_datain.close();
//            }
//            catch (IOException e)
//            {
//                // TODO Auto-generated catch block
//                e.printStackTrace();
//            }
//        }
//        catch (FileNotFoundException e)
//        {
//            // TODO Auto-generated catch block
//            e.printStackTrace();
//        }
        /*
         * Option 2;
         */
//        RandomAccessFile ar2_q13_datain_rand = null;
//        try
//        {
//            ar2_q13_datain_rand = new RandomAccessFile(new File(ar2_q13_filename), "r");
//            try
//            {
//                ar2_q13_datain_rand.seek(ar2_q13_file_offset);
//                for( i = 0; i < NB_SUBFR * SHAPE_LPC_ORDER_MAX; i++ )
//                {
//         //           AR2_Q13[ i ] = (short)SigProcFIX.SKP_SAT16( SigProcFLP.SKP_float2int( psEncCtrl.AR2[ i ] * 8192.0f ) );
//                      try
//                    {
//                        ar2_q13[i] = ar2_q13_datain_rand.readShort();
//                        AR2_Q13[i] = (short) (((ar2_q13[i] << 8) & 0xFF00) | ((ar2_q13[i] >>> 8) & 0x00FF));
//                    }
//                    catch (IOException e)
//                    {
//                        // TODO Auto-generated catch block
//                        e.printStackTrace();
//                    }
//                }
//                ar2_q13_file_offset += i;
//            }
//            catch (IOException e)
//            {
//                // TODO Auto-generated catch block
//                e.printStackTrace();
//            }
//
//            try
//            {
//                ar2_q13_datain_rand.close();
//            }
//            catch (IOException e)
//            {
//                // TODO Auto-generated catch block
//                e.printStackTrace();
//            }
//        }
//        catch (FileNotFoundException e1)
//        {
//            // TODO Auto-generated catch block
//            e1.printStackTrace();
//        }

        /**
         * Option 3:
         */
//      ar2_q13_filename += frame_cnt;
//      DataInputStream ar2_q13_datain = null;
//      try
//        {
//            ar2_q13_datain = new DataInputStream(
//                                                 new FileInputStream(
//                                                     new File(ar2_q13_filename)));
//
//            for( i = 0; i < NB_SUBFR * SHAPE_LPC_ORDER_MAX; i++ )
//            {
//     //           AR2_Q13[ i ] = (short)SigProcFIX.SKP_SAT16( SigProcFLP.SKP_float2int( psEncCtrl.AR2[ i ] * 8192.0f ) );
//                  try
//                {
//                    ar2_q13[i] = ar2_q13_datain.readShort();
//                    AR2_Q13[i] = (short) (((ar2_q13[i] << 8) & 0xFF00) | ((ar2_q13[i] >>> 8) & 0x00FF));
//                }
//                catch (IOException e)
//                {
//                    // TODO Auto-generated catch block
//                    e.printStackTrace();
//                }
//            }
//            try
//            {
//                ar2_q13_datain.close();
//            }
//            catch (IOException e)
//            {
//                // TODO Auto-generated catch block
//                e.printStackTrace();
//            }
//        }
//        catch (FileNotFoundException e)
//        {
//            // TODO Auto-generated catch block
//            e.printStackTrace();
//        }
        /*TEST End***********************************************************************/

        for( i = 0; i < NB_SUBFR; i++ )
        {
            LF_shp_Q14[ i ] =   ( SigProcFLP.SKP_float2int( psEncCtrl.LF_AR_shp[ i ]     * 16384.0f ) << 16 ) |
                                  ( 0x0000FFFF & SigProcFLP.SKP_float2int( psEncCtrl.LF_MA_shp[ i ]     * 16384.0f ) );
            Tilt_Q14[ i ]   =        SigProcFLP.SKP_float2int( psEncCtrl.Tilt[ i ]          * 16384.0f );
            HarmShapeGain_Q14[ i ] = SigProcFLP.SKP_float2int( psEncCtrl.HarmShapeGain[ i ] * 16384.0f );
        }
        Lambda_Q10 = SigProcFLP.SKP_float2int( psEncCtrl.Lambda * 1024.0f );

        /* prediction and coding parameters */
        for( i = 0; i < NB_SUBFR * LTP_ORDER; i++ )
        {
            LTPCoef_Q14[ i ] = ( short )SigProcFLP.SKP_float2int( psEncCtrl.LTPCoef[ i ] * 16384.0f );
        }

        for( j = 0; j < NB_SUBFR >> 1; j++ )
        {
            for( i = 0; i < psEnc.sCmn.predictLPCOrder; i++ )
            {
                PredCoef_Q12[ j ][ i ] = ( short )SigProcFLP.SKP_float2int( psEncCtrl.PredCoef[ j ][ i ] * 4096.0f );
            }
            for( ; i < MAX_LPC_ORDER; i++ )
            {
                PredCoef_Q12[ j ][ i ] = 0;
            }
        }

        for( i = 0; i < NB_SUBFR; i++ )
        {
            tmp_float = SigProcFIX.SKP_LIMIT( ( psEncCtrl.Gains[ i ] * 65536.0f ), 2147483000.0f, -2147483000.0f );
            Gains_Q16[ i ] = SigProcFLP.SKP_float2int( tmp_float );
            if( psEncCtrl.Gains[ i ] > 0.0f )
            {
                System.Diagnostics.Debug.Assert( tmp_float >= 0.0f );
                System.Diagnostics.Debug.Assert( Gains_Q16[ i ] >= 0 );
            }
        }

        if( psEncCtrl.sCmn.sigtype == SIG_TYPE_VOICED ) {

            LTP_scale_Q14 = TablesOther.SKP_Silk_LTPScales_table_Q14[ psEncCtrl.sCmn.LTP_scaleIndex ];
        }
        else
        {
            LTP_scale_Q14 = 0;
        }

        /* Convert input to fix */
        SigProcFLP.SKP_float2short_array( x_16,0, x,x_offset, psEnc.sCmn.frame_length );

        /*TEST************************************************************************/
        /**
         * test of x_16
         */
//        short[] x_16_test = new short[ MAX_FRAME_LENGTH ];
//        String x_16_filename = "D:/gsoc/x_16";
//        /*
//         * Option 1:
//         */
//        DataInputStream x_16_datain = null;
//        try
//        {
//            x_16_datain = new DataInputStream(
//                              new FileInputStream(
//                                  new File(x_16_filename)));
//            for(int k = 0; k < psEnc.sCmn.frame_length; k++)
//            {
//                try
//                {
//                    x_16_test[k] = x_16_datain.readShort();
//                    x_16[k] = (short) (((x_16_test[k]<<8)&0xFF00)|((x_16_test[k]>>>8)&0x00FF));
//                }
//                catch (IOException e)
//                {
//                    // TODO Auto-generated catch block
//                    e.printStackTrace();
//                }
//            }
//            try
//            {
//                x_16_datain.close();
//            }
//            catch (IOException e)
//            {
//                // TODO Auto-generated catch block
//                e.printStackTrace();
//            }
//        }
//        catch (FileNotFoundException e)
//        {
//            // TODO Auto-generated catch block
//            e.printStackTrace();
//        }

        /*
         * Option 2:
         */
//        RandomAccessFile x_16_datain_rand = null;
//        int k;
//        try
//        {
//            x_16_datain_rand = new RandomAccessFile(new File(x_16_filename), "r");
//            try
//            {
//                x_16_datain_rand.seek(x_16_file_offset);
//                for(k = 0; k < psEnc.sCmn.frame_length; k++)
//                {
//                    try
//                    {
//                        x_16_test[k] = x_16_datain_rand.readShort();
//                        x_16[k] = (short) (((x_16_test[k]<<8)&0xFF00)|((x_16_test[k]>>>8)&0x00FF));
//                    }
//                    catch (IOException e)
//                    {
//                        // TODO Auto-generated catch block
//                        e.printStackTrace();
//                    }
//                }
//                x_16_file_offset += k;
//            }
//            catch (IOException e1)
//            {
//                // TODO Auto-generated catch block
//                e1.printStackTrace();
//            }
//            try
//            {
//                x_16_datain_rand.close();
//            }
//            catch (IOException e)
//            {
//                // TODO Auto-generated catch block
//                e.printStackTrace();
//            }
//        }
//        catch (FileNotFoundException e)
//        {
//            // TODO Auto-generated catch block
//            e.printStackTrace();
//        }

        /**
         * Optino 3:
         */
//      x_16_filename += frame_cnt;
//      DataInputStream x_16_datain = null;
//      try
//        {
//            x_16_datain = new DataInputStream(
//                              new FileInputStream(
//                                  new File(x_16_filename)));
//            for(int k = 0; k < psEnc.sCmn.frame_length; k++)
//            {
//                try
//                {
//                    x_16_test[k] = x_16_datain.readShort();
//                    x_16[k] = (short) (((x_16_test[k]<<8)&0xFF00)|((x_16_test[k]>>>8)&0x00FF));
//                }
//                catch (IOException e)
//                {
//                    // TODO Auto-generated catch block
//                    e.printStackTrace();
//                }
//            }
//            try
//            {
//                x_16_datain.close();
//            }
//            catch (IOException e)
//            {
//                // TODO Auto-generated catch block
//                e.printStackTrace();
//            }
//        }
//        catch (FileNotFoundException e)
//        {
//            // TODO Auto-generated catch block
//            e.printStackTrace();
//        }
//        frame_cnt++;
        /*TEST END************************************************************************/

        /* Call NSQ */
        short[] PredCoef_Q12_dim1_tmp = psEnc.nsqPredCoefQ12Flat;
        int PredCoef_Q12_offset = 0;
        for(int PredCoef_Q12_i = 0; PredCoef_Q12_i < PredCoef_Q12.Length; PredCoef_Q12_i++)
        {
            Array.Copy(PredCoef_Q12[PredCoef_Q12_i], 0, PredCoef_Q12_dim1_tmp, PredCoef_Q12_offset, PredCoef_Q12[PredCoef_Q12_i].Length);
            PredCoef_Q12_offset += PredCoef_Q12[PredCoef_Q12_i].Length;
        }
        byte[] qTarget = q_offset == 0 ? q : psEnc.nsqQScratch;
        SKP_Silk_nsq_state nsq = useLBRR != 0 ? psEnc.sNSQ_LBRR : psEnc.sNSQ;
        if( UsesDelayedDecision( psEnc.sCmn ) )
        {
            NSQDelDec.SKP_Silk_NSQ_del_dec( psEnc.sCmn, psEncCtrl.sCmn, nsq,
                x_16, qTarget, psEncCtrl.sCmn.NLSFInterpCoef_Q2, PredCoef_Q12_dim1_tmp, LTPCoef_Q14, AR2_Q13,
                HarmShapeGain_Q14, Tilt_Q14, LF_shp_Q14, Gains_Q16, Lambda_Q10, LTP_scale_Q14 );
        }
        else
        {
            NSQ.SKP_Silk_NSQ( psEnc.sCmn, psEncCtrl.sCmn, nsq,
                x_16, qTarget, psEncCtrl.sCmn.NLSFInterpCoef_Q2, PredCoef_Q12_dim1_tmp, LTPCoef_Q14, AR2_Q13,
                HarmShapeGain_Q14, Tilt_Q14, LF_shp_Q14, Gains_Q16, Lambda_Q10, LTP_scale_Q14 );
        }

        if (q_offset != 0)
        {
            Array.Copy(qTarget, 0, q, q_offset, psEnc.sCmn.frame_length);
        }
    }

    internal static bool UsesDelayedDecision( SKP_Silk_encoder_state state )
    {
        return state.nStatesDelayedDecision > 1 || state.warping_Q16 > 0;
    }
}
