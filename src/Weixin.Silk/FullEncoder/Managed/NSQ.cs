using static SilkCodec.NET.Managed.Define;
using static SilkCodec.NET.Managed.Macros;
using static SilkCodec.NET.Managed.Typedef;
using System.Numerics;

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
 * Noise shaping state handling adapted from SILK SDK 1.0.9.
 * Copyright (c) 2006-2012, Skype Limited. See THIRD-PARTY-NOTICES.
 */
/**
 *
 * @author Dingxin Xu
 */
internal class NSQ
{
    /**
     *
     * @param psEncC Encoder State
     * @param psEncCtrlC Encoder Control
     * @param NSQ NSQ state
     * @param x prefiltered input signal
     * @param q quantized qulse signal
     * @param LSFInterpFactor_Q2 LSF interpolation factor input Q2
     * @param PredCoef_Q12 Short term prediction coefficients
     * @param LTPCoef_Q14 Long term prediction coefficients
     * @param AR2_Q13
     * @param HarmShapeGain_Q14
     * @param Tilt_Q14 Spectral tilt
     * @param LF_shp_Q14
     * @param Gains_Q16
     * @param Lambda_Q10
     * @param LTP_scale_Q14 LTP state scaling
     */
    internal static void SKP_Silk_NSQ(
        SKP_Silk_encoder_state     psEncC,             /* I/O  Encoder State                       */
        SKP_Silk_encoder_control   psEncCtrlC,         /* I    Encoder Control                     */
        SKP_Silk_nsq_state         NSQ,                /* I/O  NSQ state                           */
        short[] x,                /* I    prefiltered input signal            */
        byte[] q,                /* O    quantized qulse signal              */
        int                        LSFInterpFactor_Q2, /* I    LSF interpolation factor input Q2      */
        short[] PredCoef_Q12,     /* I    Short term prediction coefficients  */
        short[] LTPCoef_Q14,      /* I    Long term prediction coefficients   */
        short[] AR2_Q13,          /* I                                        */
        int[] HarmShapeGain_Q14,/* I                                        */
        int[] Tilt_Q14,         /* I    Spectral tilt                       */
        int[] LF_shp_Q14,       /* I                                        */
        int[] Gains_Q16,        /* I                                        */
        int                        Lambda_Q10,         /* I                                        */
        int                        LTP_scale_Q14       /* I    LTP state scaling                   */
    )
    {
        int     k, lag, start_idx, LSF_interpolation_flag;
        short []A_Q12;
        short [] B_Q14;
        short [] AR_shp_Q13;
        int           A_Q12_offset, B_Q14_offset, AR_shp_Q13_offset;
        short   []pxq;
        int     pxq_offset;
        int[] sLTP_Q16 = psEncC.nsqSLtpQ16;
        short[] sLTP = psEncC.nsqSLtp;
        int     HarmShapeFIRPacked_Q14;
        int     offset_Q10;
        int[] FiltState = psEncC.nsqFiltState;
        int[] x_sc_Q10 = psEncC.nsqXScQ10;

        Array.Clear(sLTP_Q16);
        Array.Clear(sLTP);

        NSQ.rand_seed  =  psEncCtrlC.Seed;
        /* Set unvoiced lag to the previous one, overwrite later for voiced */
        lag             = NSQ.lagPrev;

        System.Diagnostics.Debug.Assert( NSQ.prev_inv_gain_Q16 != 0 );

        offset_Q10 = TablesOther.SKP_Silk_Quantization_Offsets_Q10[ psEncCtrlC.sigtype ][ psEncCtrlC.QuantOffsetType ];

        if( LSFInterpFactor_Q2 == ( 1 << 2 ) ) {
            LSF_interpolation_flag = 0;
        } else {
            LSF_interpolation_flag = 1;
        }

        /* Setup pointers to start of sub frame */
        NSQ.sLTP_shp_buf_idx = psEncC.frame_length;
        NSQ.sLTP_buf_idx     = psEncC.frame_length;
        pxq                  = NSQ.xq;
        pxq_offset           = psEncC.frame_length;
//TODO: use a local copy of the parameter short[] x, which is supposed to be input;
        short[] x_tmp = x;
        int     x_tmp_offset = 0;
//TODO: use a local copy of the parameter[] byte q, which is supposed to be output;
        byte[]  q_tmp = q;
        int     q_tmp_offset = 0;

        for( k = 0; k < NB_SUBFR; k++ ) {
            A_Q12      = PredCoef_Q12;
            A_Q12_offset = (( k >> 1 ) | ( 1 - LSF_interpolation_flag )) * MAX_LPC_ORDER;
            B_Q14      = LTPCoef_Q14;
            B_Q14_offset = k * LTP_ORDER;
            AR_shp_Q13 = AR2_Q13;
            AR_shp_Q13_offset = k * SHAPE_LPC_ORDER_MAX;

            /* Noise shape parameters */
            System.Diagnostics.Debug.Assert( HarmShapeGain_Q14[ k ] >= 0 );
            HarmShapeFIRPacked_Q14  = ( HarmShapeGain_Q14[ k ] >> 2 );
            HarmShapeFIRPacked_Q14 |= ( ( HarmShapeGain_Q14[ k ] >> 1 ) << 16 );


            NSQ.rewhite_flag = 0;
            if( psEncCtrlC.sigtype == SIG_TYPE_VOICED ) {
                /* Voiced */
                lag = psEncCtrlC.pitchL[ k ];

                /* Re-whitening */
                if( ( k & ( 3 - ( LSF_interpolation_flag << 1 ) ) ) == 0 ) {
                    /* Rewhiten with new A coefs */

                    start_idx = psEncC.frame_length - lag - psEncC.predictLPCOrder - LTP_ORDER / 2;
                    System.Diagnostics.Debug.Assert( start_idx >= 0 );
                    System.Diagnostics.Debug.Assert( start_idx <= psEncC.frame_length - psEncC.predictLPCOrder );

                    Array.Fill(FiltState, 0, 0, psEncC.predictLPCOrder);
                    MA.SKP_Silk_MA_Prediction( NSQ.xq, start_idx + k * ( psEncC.frame_length >> 2 ),
                        A_Q12, A_Q12_offset, FiltState, sLTP, start_idx, psEncC.frame_length - start_idx, psEncC.predictLPCOrder );

                    NSQ.rewhite_flag = 1;
                    NSQ.sLTP_buf_idx = psEncC.frame_length;
                }
            }

            SKP_Silk_nsq_scale_states( NSQ, x_tmp, x_tmp_offset, x_sc_Q10, psEncC.subfr_length, sLTP,
                    sLTP_Q16, k, LTP_scale_Q14, Gains_Q16, psEncCtrlC.pitchL );


            SKP_Silk_noise_shape_quantizer( NSQ, psEncCtrlC.sigtype, x_sc_Q10, q_tmp, q_tmp_offset, pxq, pxq_offset,
                    sLTP_Q16, A_Q12, A_Q12_offset, B_Q14, B_Q14_offset,
                AR_shp_Q13, AR_shp_Q13_offset, lag, HarmShapeFIRPacked_Q14, Tilt_Q14[ k ], LF_shp_Q14[ k ], Gains_Q16[ k ], Lambda_Q10,
                offset_Q10, psEncC.subfr_length, psEncC.shapingLPCOrder, psEncC.predictLPCOrder
            );

            x_tmp_offset          += psEncC.subfr_length;
            q_tmp_offset          += psEncC.subfr_length;
            pxq_offset        += psEncC.subfr_length;
        }

        NSQ.lagPrev                        = psEncCtrlC.pitchL[ NB_SUBFR - 1 ];
    /* Save quantized speech and noise shaping signals */
        Array.Copy(NSQ.xq, psEncC.frame_length, NSQ.xq, 0, psEncC.frame_length);
        Array.Copy(NSQ.sLTP_shp_Q10, psEncC.frame_length, NSQ.sLTP_shp_Q10, 0, psEncC.frame_length);

    }

    /**
     * SKP_Silk_noise_shape_quantizer.
     * @param NSQ NSQ state
     * @param sigtype Signal type
     * @param x_sc_Q10
     * @param q
     * @param q_offset
     * @param xq
     * @param xq_offset
     * @param sLTP_Q16 LTP state
     * @param a_Q12 Short term prediction coefs
     * @param a_Q12_offset
     * @param b_Q14 Long term prediction coefs
     * @param b_Q14_offset
     * @param AR_shp_Q13 Noise shaping AR coefs
     * @param AR_shp_Q13_offset
     * @param lag Pitch lag
     * @param HarmShapeFIRPacked_Q14
     * @param Tilt_Q14 Spectral tilt
     * @param LF_shp_Q14
     * @param Gain_Q16
     * @param Lambda_Q10
     * @param offset_Q10
     * @param length Input length
     * @param shapingLPCOrder Noise shaping AR filter order
     * @param predictLPCOrder Prediction filter order
     */
    internal static void SKP_Silk_noise_shape_quantizer(
        SKP_Silk_nsq_state  NSQ,               /* I/O  NSQ state                       */
            int             sigtype,            /* I    Signal type                     */
        int[] x_sc_Q10,         /* I                                    */
        byte[] q,                /* O                                    */
        int                 q_offset,
        short[] xq,               /* O                                    */
        int                 xq_offset,
        int[] sLTP_Q16,         /* I/O  LTP state                       */
        short[] a_Q12,            /* I    Short term prediction coefs     */
        int                 a_Q12_offset,
        short[] b_Q14,            /* I    Long term prediction coefs      */
        int                 b_Q14_offset,
        short[] AR_shp_Q13,       /* I    Noise shaping AR coefs          */
        int                 AR_shp_Q13_offset,
        int                 lag,                /* I    Pitch lag                       */
        int                 HarmShapeFIRPacked_Q14, /* I                                */
        int                 Tilt_Q14,           /* I    Spectral tilt                   */
        int                 LF_shp_Q14,         /* I                                    */
        int                 Gain_Q16,           /* I                                    */
        int                 Lambda_Q10,         /* I                                    */
        int                 offset_Q10,         /* I                                    */
        int                 length,             /* I    Input length                    */
        int                 shapingLPCOrder,    /* I    Noise shaping AR filter order   */
        int                 predictLPCOrder     /* I    Prediction filter order         */
    )
    {
        int     i, j;
        int   LTP_pred_Q14, LPC_pred_Q10, n_AR_Q10, n_LTP_Q14;
        int   n_LF_Q10, r_Q10, q_Q0, q_Q10;
        int   thr1_Q10, thr2_Q10, thr3_Q10;
        int   dither;
        int   exc_Q10, LPC_exc_Q10, xq_Q10;
        int   tmp1, tmp2, sLF_AR_shp_Q10;
        int   []psLPC_Q14;
        int   psLPC_Q14_offset;
        int   []shp_lag_ptr, pred_lag_ptr;
        int   shp_lag_ptr_offset, pred_lag_ptr_offset;

        shp_lag_ptr  = NSQ.sLTP_shp_Q10;
        shp_lag_ptr_offset = NSQ.sLTP_shp_buf_idx - lag + HARM_SHAPE_FIR_TAPS / 2;
        pred_lag_ptr = sLTP_Q16;
        pred_lag_ptr_offset = NSQ.sLTP_buf_idx - lag + LTP_ORDER / 2;

        /* Setup short term AR state */
        psLPC_Q14     = NSQ.sLPC_Q14;
        psLPC_Q14_offset = NSQ_LPC_BUF_LENGTH() - 1;

        /* Quantization thresholds */
        thr1_Q10 = ( -1536 - (Lambda_Q10 >> 1));
        thr2_Q10 = ( -512 - (Lambda_Q10 >> 1));
        thr2_Q10 = ( thr2_Q10 + (SKP_SMULBB( offset_Q10, Lambda_Q10 ) >> 10 ));
        thr3_Q10 = (  512 + (Lambda_Q10 >> 1));

        for( i = 0; i < length; i++ ) {
            /* Generate dither */
            NSQ.rand_seed = SigProcFIX.SKP_RAND( NSQ.rand_seed );

            /* dither = rand_seed < 0 ? 0xFFFFFFFF : 0; */
            dither = ( NSQ.rand_seed >> 31 );

            /* Short-term prediction */
            System.Diagnostics.Debug.Assert( ( predictLPCOrder  & 1 ) == 0 );    /* check that order is even */
//            SKP_System.Diagnostics.Debug.Assert( ( (SKP_int64)a_Q12 & 3 ) == 0 );    /* check that array starts at 4-byte aligned address */
            System.Diagnostics.Debug.Assert( predictLPCOrder >= 10 );            /* check that unrolling works */
            /* Partially unrolled */
            LPC_pred_Q10 = SKP_SMULWB(               psLPC_Q14[  psLPC_Q14_offset+0 ], a_Q12[ a_Q12_offset+0 ] );
            LPC_pred_Q10 = SKP_SMLAWB( LPC_pred_Q10, psLPC_Q14[ psLPC_Q14_offset-1 ], a_Q12[ a_Q12_offset+1 ] );
            LPC_pred_Q10 = SKP_SMLAWB( LPC_pred_Q10, psLPC_Q14[ psLPC_Q14_offset-2 ], a_Q12[ a_Q12_offset+2 ] );
            LPC_pred_Q10 = SKP_SMLAWB( LPC_pred_Q10, psLPC_Q14[ psLPC_Q14_offset-3 ], a_Q12[ a_Q12_offset+3 ] );
            LPC_pred_Q10 = SKP_SMLAWB( LPC_pred_Q10, psLPC_Q14[ psLPC_Q14_offset-4 ], a_Q12[ a_Q12_offset+4 ] );
            LPC_pred_Q10 = SKP_SMLAWB( LPC_pred_Q10, psLPC_Q14[ psLPC_Q14_offset-5 ], a_Q12[ a_Q12_offset+5 ] );
            LPC_pred_Q10 = SKP_SMLAWB( LPC_pred_Q10, psLPC_Q14[ psLPC_Q14_offset-6 ], a_Q12[ a_Q12_offset+6 ] );
            LPC_pred_Q10 = SKP_SMLAWB( LPC_pred_Q10, psLPC_Q14[ psLPC_Q14_offset-7 ], a_Q12[ a_Q12_offset+7 ] );
            LPC_pred_Q10 = SKP_SMLAWB( LPC_pred_Q10, psLPC_Q14[ psLPC_Q14_offset-8 ], a_Q12[ a_Q12_offset+8 ] );
            LPC_pred_Q10 = SKP_SMLAWB( LPC_pred_Q10, psLPC_Q14[ psLPC_Q14_offset-9 ], a_Q12[ a_Q12_offset+9 ] );

            for( j = 10; j < predictLPCOrder; j ++ ) {
                LPC_pred_Q10 = SKP_SMLAWB( LPC_pred_Q10, psLPC_Q14[ psLPC_Q14_offset-j ], a_Q12[ a_Q12_offset+j ] );
            }
            /* Long-term prediction */
            if( sigtype == SIG_TYPE_VOICED ) {
                /* Unrolled loop */
                LTP_pred_Q14 = SKP_SMULWB(               pred_lag_ptr[ pred_lag_ptr_offset +0 ], b_Q14[ b_Q14_offset+0 ] );
                LTP_pred_Q14 = SKP_SMLAWB( LTP_pred_Q14, pred_lag_ptr[ pred_lag_ptr_offset -1 ], b_Q14[ b_Q14_offset+1 ] );
                LTP_pred_Q14 = SKP_SMLAWB( LTP_pred_Q14, pred_lag_ptr[ pred_lag_ptr_offset -2 ], b_Q14[ b_Q14_offset+2 ] );
                LTP_pred_Q14 = SKP_SMLAWB( LTP_pred_Q14, pred_lag_ptr[ pred_lag_ptr_offset -3 ], b_Q14[ b_Q14_offset+3 ] );
                LTP_pred_Q14 = SKP_SMLAWB( LTP_pred_Q14, pred_lag_ptr[ pred_lag_ptr_offset -4 ], b_Q14[ b_Q14_offset+4 ] );
                pred_lag_ptr_offset++;
            } else {
                LTP_pred_Q14 = 0;
            }

            /* Noise shape feedback */
            System.Diagnostics.Debug.Assert( ( shapingLPCOrder & 1 ) == 0 );
            tmp2 = psLPC_Q14[ psLPC_Q14_offset ];
            tmp1 = NSQ.sAR2_Q14[ 0 ];
            NSQ.sAR2_Q14[ 0 ] = tmp2;
            n_AR_Q10 = SKP_SMULWB( tmp2, AR_shp_Q13[ AR_shp_Q13_offset ] );
            for( j = 2; j < shapingLPCOrder; j += 2 ) {
                tmp2 = NSQ.sAR2_Q14[ j - 1 ];
                NSQ.sAR2_Q14[ j - 1 ] = tmp1;
                n_AR_Q10 = SKP_SMLAWB( n_AR_Q10, tmp1, AR_shp_Q13[ AR_shp_Q13_offset + j - 1 ] );
                tmp1 = NSQ.sAR2_Q14[ j ];
                NSQ.sAR2_Q14[ j ] = tmp2;
                n_AR_Q10 = SKP_SMLAWB( n_AR_Q10, tmp2, AR_shp_Q13[ AR_shp_Q13_offset + j ] );
            }
            NSQ.sAR2_Q14[ shapingLPCOrder - 1 ] = tmp1;
            n_AR_Q10 = SKP_SMLAWB( n_AR_Q10, tmp1, AR_shp_Q13[ AR_shp_Q13_offset + shapingLPCOrder - 1 ] );
            n_AR_Q10 = ( n_AR_Q10 >> 1 );   /* Q11 -> Q10 */
            n_AR_Q10  = SKP_SMLAWB( n_AR_Q10, NSQ.sLF_AR_shp_Q12, Tilt_Q14 );

            n_LF_Q10   = ( SKP_SMULWB( NSQ.sLTP_shp_Q10[ NSQ.sLTP_shp_buf_idx - 1 ], LF_shp_Q14 ) << 2 );
            n_LF_Q10   = SKP_SMLAWT( n_LF_Q10, NSQ.sLF_AR_shp_Q12, LF_shp_Q14 );

            System.Diagnostics.Debug.Assert( lag > 0 || sigtype == SIG_TYPE_UNVOICED );

            /* Long-term shaping */
            if( lag > 0 ) {
                /* Symmetric, packed FIR coefficients */
                n_LTP_Q14 = SKP_SMULWB((shp_lag_ptr[ shp_lag_ptr_offset+0 ] + shp_lag_ptr[ shp_lag_ptr_offset-2 ] ),
                        HarmShapeFIRPacked_Q14 );
                n_LTP_Q14 = SKP_SMLAWT(n_LTP_Q14, shp_lag_ptr[ shp_lag_ptr_offset-1 ],HarmShapeFIRPacked_Q14 );
                shp_lag_ptr_offset++;
                n_LTP_Q14 = ( n_LTP_Q14 << 6 );
            } else {
                n_LTP_Q14 = 0;
            }

            /* Input minus prediction plus noise feedback  */
            //r = x[ i ] - LTP_pred - LPC_pred + n_AR + n_Tilt + n_LF + n_LTP;
            tmp1  = ( LTP_pred_Q14 - n_LTP_Q14 );
            tmp1 >>= 4;
            tmp1 += LPC_pred_Q10;
            tmp1 -= n_AR_Q10;
            tmp1 -= n_LF_Q10;
            r_Q10 = x_sc_Q10[ i ] - tmp1;

            /* Flip sign depending on dither */
            r_Q10 = ( r_Q10 ^ dither ) - dither;
            r_Q10 = ( r_Q10 - offset_Q10 );
            r_Q10 = SigProcFIX.SKP_LIMIT_32( r_Q10, -64 << 10, 64 << 10 );

            /* Quantize */
            if( r_Q10 < thr1_Q10 ) {
                q_Q0 = SigProcFIX.SKP_RSHIFT_ROUND( ( r_Q10 + (Lambda_Q10 >> 1) ), 10 );
                q_Q10 = ( q_Q0 << 10 );
            } else if( r_Q10 < thr2_Q10 ) {
                q_Q0 = -1;
                q_Q10 = -1024;
            } else if( r_Q10 > thr3_Q10 ) {
                q_Q0 = SigProcFIX.SKP_RSHIFT_ROUND( ( r_Q10 - (Lambda_Q10 >> 1) ), 10 );
                q_Q10 = ( q_Q0 << 10 );
            } else {
                q_Q0 = 0;
                q_Q10 = 0;
            }
            q[ q_offset + i ] = unchecked((byte)q_Q0); /* No saturation needed because max is 64 */

            /* Excitation */
            exc_Q10 = ( q_Q10 + offset_Q10 );
            exc_Q10 = ( exc_Q10 ^ dither ) - dither;

            /* Add predictions */
            LPC_exc_Q10 = ( exc_Q10 + SigProcFIX.SKP_RSHIFT_ROUND( LTP_pred_Q14, 4 ) );
            xq_Q10      = ( LPC_exc_Q10 + LPC_pred_Q10 );

            /* Scale XQ back to normal level before saving */
            xq[ xq_offset + i ] = (  short)SigProcFIX.SKP_SAT16( SigProcFIX.SKP_RSHIFT_ROUND( SKP_SMULWW( xq_Q10, Gain_Q16 ), 10 ) );


            /* Update states */
            psLPC_Q14_offset++;
            psLPC_Q14[psLPC_Q14_offset] = ( xq_Q10 << 4 );
            sLF_AR_shp_Q10 = ( xq_Q10 - n_AR_Q10 );
            NSQ.sLF_AR_shp_Q12 = ( sLF_AR_shp_Q10 << 2 );

            NSQ.sLTP_shp_Q10[ NSQ.sLTP_shp_buf_idx ] = ( sLF_AR_shp_Q10 - n_LF_Q10 );
            sLTP_Q16[ NSQ.sLTP_buf_idx ] = ( LPC_exc_Q10 << 6 );
            NSQ.sLTP_shp_buf_idx++;
            NSQ.sLTP_buf_idx++;

            /* Make dither dependent on quantized signal */
            NSQ.rand_seed += (sbyte)q[ q_offset + i ];
        }
        /* Update LPC synth buffer */
        Array.Copy(NSQ.sLPC_Q14, length, NSQ.sLPC_Q14, 0, NSQ_LPC_BUF_LENGTH());
    }

    /**
     *
     * @param NSQ NSQ state
     * @param x input input Q0
     * @param x_offset
     * @param x_sc_Q10 input scaled with 1/Gain
     * @param length length of input
     * @param sLTP re-whitened LTP state input Q0
     * @param sLTP_Q16 LTP state matching scaled input
     * @param subfr subframe number
     * @param LTP_scale_Q14
     * @param Gains_Q16
     * @param pitchL
     */
    internal static void SKP_Silk_nsq_scale_states(
            SKP_Silk_nsq_state NSQ,               /* I/O NSQ state                        */
            short[] x,                /* I input input Q0                        */
            int                x_offset,
            int[] x_sc_Q10,         /* O input scaled with 1/Gain           */
            int                length,             /* I length of input                    */
            short[] sLTP,             /* I re-whitened LTP state input Q0        */
            int[] sLTP_Q16,         /* O LTP state matching scaled input    */
            int                subfr,              /* I subframe number                    */
            int          LTP_scale_Q14,      /* I                                    */
            int[] Gains_Q16, /* I                                 */
            int[] pitchL  /* I                                    */
        )
    {
        int   i, lag;
        int   inv_gain_Q16, gain_adj_Q16, inv_gain_Q32;

        inv_gain_Q16 = Inlines.SKP_INVERSE32_varQ( Math.Max( Gains_Q16[subfr], 1 ), 32 );
        inv_gain_Q16 = Math.Min(inv_gain_Q16, short.MaxValue);
        lag          = pitchL[ subfr ];

        /* After rewhitening the LTP state is un-scaled */
        if( NSQ.rewhite_flag !=0 ) {
            inv_gain_Q32 = ( inv_gain_Q16 << 16 );
            if( subfr == 0 ) {
                /* Do LTP downscaling */
                inv_gain_Q32 = ( SKP_SMULWB( inv_gain_Q32, LTP_scale_Q14 ) << 2 );
            }
            for( i = NSQ.sLTP_buf_idx - lag - LTP_ORDER / 2; i < NSQ.sLTP_buf_idx; i++ ) {
                sLTP_Q16[ i ] = SKP_SMULWB( inv_gain_Q32, sLTP[ i ] );
            }
        }

        /* Adjust for changing gain */
        if( inv_gain_Q16 != NSQ.prev_inv_gain_Q16 ) {
            gain_adj_Q16 = SKP_DIV32_varQ( inv_gain_Q16, NSQ.prev_inv_gain_Q16, 16 );

            for( i = NSQ.sLTP_shp_buf_idx - length * NB_SUBFR; i < NSQ.sLTP_shp_buf_idx; i++ ) {
                NSQ.sLTP_shp_Q10[ i ] = SKP_SMULWW( gain_adj_Q16, NSQ.sLTP_shp_Q10[ i ] );
            }

            /* Scale LTP predict state */
            if( NSQ.rewhite_flag == 0 ) {
                for( i = NSQ.sLTP_buf_idx - lag - LTP_ORDER / 2; i < NSQ.sLTP_buf_idx; i++ ) {
                    sLTP_Q16[ i ] = SKP_SMULWW( gain_adj_Q16, sLTP_Q16[ i ] );
                }
            }
            NSQ.sLF_AR_shp_Q12 = SKP_SMULWW( gain_adj_Q16, NSQ.sLF_AR_shp_Q12 );

            /* scale short term state */
            for( i = 0; i < NSQ_LPC_BUF_LENGTH(); i++ ) {
                NSQ.sLPC_Q14[ i ] = SKP_SMULWW( gain_adj_Q16, NSQ.sLPC_Q14[ i ] );
            }
            for( i = 0; i < SHAPE_LPC_ORDER_MAX; i++ ) {
                NSQ.sAR2_Q14[ i ] = SKP_SMULWW( gain_adj_Q16, NSQ.sAR2_Q14[ i ] );
            }
        }

        /* Scale input */
        for( i = 0; i < length; i++ ) {
            x_sc_Q10[ i ] = ( SKP_SMULBB( x[ x_offset + i ], ( short )inv_gain_Q16 ) >> 6 );
        }

        /* save inv_gain */
        System.Diagnostics.Debug.Assert( inv_gain_Q16 != 0 );
        NSQ.prev_inv_gain_Q16 = inv_gain_Q16;
    }

    internal static int SKP_DIV32_varQ(int a32, int b32, int Qres)
    {
        System.Diagnostics.Debug.Assert(b32 != 0);
        System.Diagnostics.Debug.Assert(Qres >= 0);

        int a_headrm = (int)BitOperations.LeadingZeroCount((uint)Math.Abs(a32)) - 1;
        int a32_nrm = a32 << a_headrm;
        int b_headrm = (int)BitOperations.LeadingZeroCount((uint)Math.Abs(b32)) - 1;
        int b32_nrm = b32 << b_headrm;
        int b32_inv = (int.MaxValue >> 2) / (b32_nrm >> 16);
        int result = SKP_SMULWB(a32_nrm, b32_inv);

        a32_nrm -= SigProcFIX.SKP_SMMUL(b32_nrm, result) << 3;
        result = SKP_SMLAWB(result, a32_nrm, b32_inv);

        int lshift = 29 + a_headrm - b_headrm - Qres;
        if (lshift <= 0)
        {
            return SigProcFIX.SKP_LSHIFT_SAT32(result, -lshift);
        }

        return lshift < 32 ? result >> lshift : 0;
    }
}
