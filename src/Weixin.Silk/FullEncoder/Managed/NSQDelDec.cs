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
 * Warped delayed-decision noise shaping adapted from SILK SDK 1.0.9.
 * Copyright (c) 2006-2012, Skype Limited. See THIRD-PARTY-NOTICES.
 */
/**
 *
 * @author Dingxin Xu
 */
internal class NSQDelDecStruct
{
    internal int[] RandState = new int[ DECISION_DELAY ];
    internal int[] Q_Q10     = new int[ DECISION_DELAY ];
    internal int[] Xq_Q10    = new int[ DECISION_DELAY ];
    internal int[] Pred_Q16  = new int[ DECISION_DELAY ];
    internal int[] Shape_Q10 = new int[ DECISION_DELAY ];
    internal int[] Gain_Q16  = new int[ DECISION_DELAY ];
    internal int[] sAR2_Q14  = new int[ SHAPE_LPC_ORDER_MAX ];
    internal int[] sLPC_Q14  = new int[ MAX_FRAME_LENGTH / NB_SUBFR + NSQ_LPC_BUF_LENGTH() ];
    internal int LF_AR_Q12;
    internal int Seed;
    internal int SeedInit;
    internal int RD_Q10;
    public void FieldsInit()
    {
        Array.Fill(this.RandState, 0);
        Array.Fill(this.Q_Q10, 0);
        Array.Fill(this.Xq_Q10, 0);
        Array.Fill(this.Pred_Q16, 0);
        Array.Fill(this.Shape_Q10, 0);
        Array.Fill(this.Gain_Q16, 0);
        Array.Fill(this.sAR2_Q14, 0);
        Array.Fill(this.sLPC_Q14, 0);
        this.LF_AR_Q12 = 0;
        this.Seed = 0;
        this.SeedInit = 0;
        this.RD_Q10 = 0;
    }
}

/**
 *
 * @author Dingxin Xu
 */
internal sealed class NSQ_sample_struct
{
    internal int Q_Q10;
    internal int RD_Q10;
    internal int xq_Q14;
    internal int LF_AR_Q12;
    internal int sLTP_shp_Q10;
    internal int LPC_exc_Q16;

    internal void CopyFrom(NSQ_sample_struct source)
    {
        Q_Q10 = source.Q_Q10;
        RD_Q10 = source.RD_Q10;
        xq_Q14 = source.xq_Q14;
        LF_AR_Q12 = source.LF_AR_Q12;
        sLTP_shp_Q10 = source.sLTP_shp_Q10;
        LPC_exc_Q16 = source.LPC_exc_Q16;
    }
}

internal sealed class NSQDelDecWorkspace
{
    internal readonly int[] SLtpQ16 = new int[2 * MAX_FRAME_LENGTH];
    internal readonly short[] SLtp = new short[2 * MAX_FRAME_LENGTH];
    internal readonly int[] FiltState = new int[MAX_LPC_ORDER];
    internal readonly int[] XScQ10 = new int[MAX_FRAME_LENGTH / NB_SUBFR];
    internal readonly NSQDelDecStruct[] DelayedDecisionStates = new NSQDelDecStruct[DEL_DEC_STATES_MAX];
    internal readonly NSQ_sample_struct[][] SampleStates = new NSQ_sample_struct[DEL_DEC_STATES_MAX][];

    internal NSQDelDecWorkspace()
    {
        for (var i = 0; i < DEL_DEC_STATES_MAX; i++)
        {
            DelayedDecisionStates[i] = new NSQDelDecStruct();
            SampleStates[i] = new[] { new NSQ_sample_struct(), new NSQ_sample_struct() };
        }
    }
}

/**
 *
 * @author Dingxin Xu
 */
internal class NSQDelDec
{
    internal static void SKP_Silk_NSQ_del_dec(
        SKP_Silk_encoder_state    psEncC,             /* I/O  Encoder State                       */
        SKP_Silk_encoder_control  psEncCtrlC,         /* I    Encoder Control                     */
        SKP_Silk_nsq_state        NSQ,                /* I/O  NSQ state                           */
        short[] x,                /* I    Prefiltered input signal            */
        byte[] q,                /* O    Quantized pulse signal              */
        int                       LSFInterpFactor_Q2, /* I    LSF interpolation factor input Q2      */
        short[] PredCoef_Q12,     /* I    Prediction coefs                    */
        short[] LTPCoef_Q14,      /* I    LT prediction coefs                 */
        short[] AR2_Q13,          /* I                                        */
        int[] HarmShapeGain_Q14,/* I                                        */
        int[] Tilt_Q14,         /* I    Spectral tilt                       */
        int[] LF_shp_Q14,       /* I                                        */
        int[] Gains_Q16,        /* I                                        */
        int                       Lambda_Q10,         /* I                                        */
        int                       LTP_scale_Q14       /* I    LTP state scaling                   */
    )
    {
        int     i, k, lag, start_idx, LSF_interpolation_flag, Winner_ind, subfr;
        int     last_smple_idx, smpl_buf_idx, decisionDelay, subfr_length;
        short[] A_Q12;
        short[] B_Q14;
        short[] AR_shp_Q13;
        int           A_Q12_offset, B_Q14_offset, AR_shp_Q13_offset;
        short[] pxq;
        int     pxq_offset;
        NSQDelDecWorkspace workspace = psEncC.delayedDecisionWorkspace;
        int[] sLTP_Q16 = workspace.SLtpQ16;
        short[] sLTP = workspace.SLtp;
        int   HarmShapeFIRPacked_Q14;
        int     offset_Q10;
        int[] FiltState = workspace.FiltState;
        int RDmin_Q10;
        int[] x_sc_Q10 = workspace.XScQ10;
        NSQDelDecStruct[] psDelDec = workspace.DelayedDecisionStates;
        NSQDelDecStruct psDD;

        Array.Clear(sLTP_Q16);
        Array.Clear(sLTP);

        subfr_length = psEncC.frame_length / NB_SUBFR;

        /* Set unvoiced lag to the previous one, overwrite later for voiced */
        lag = NSQ.lagPrev;

        System.Diagnostics.Debug.Assert( NSQ.prev_inv_gain_Q16 != 0 );

      //TODO: use a local copy of the parameter short[] x, which is supposed to be input;
        short[] x_tmp = x;
        int     x_tmp_offset = 0;
//TODO: use a local copy of the parameter[] byte q, which is supposed to be output;
        byte[]  q_tmp = q;
        int     q_tmp_offset = 0;

        /* Initialize delayed decision states */
//        SKP_memset( psDelDec, 0, psEncC.nStatesDelayedDecision * sizeof( NSQ_del_dec_struct ) );
//TODO:
        for(int inx=0; inx<psEncC.nStatesDelayedDecision; inx++)
        {
            psDelDec[inx].FieldsInit();
        }
        for( k = 0; k < psEncC.nStatesDelayedDecision; k++ ) {
            psDD                 = psDelDec[ k ];
            psDD.Seed           = ( k + psEncCtrlC.Seed ) & 3;
            psDD.SeedInit       = psDD.Seed;
            psDD.RD_Q10         = 0;
            psDD.LF_AR_Q12      = NSQ.sLF_AR_shp_Q12;
            psDD.Shape_Q10[ 0 ] = NSQ.sLTP_shp_Q10[ psEncC.frame_length - 1 ];
//            SKP_memcpy( psDD.sLPC_Q14, NSQ.sLPC_Q14, NSQ_LPC_BUF_LENGTH * sizeof( SKP_int32 ) );
            Array.Copy(NSQ.sLPC_Q14, 0, psDD.sLPC_Q14, 0, NSQ_LPC_BUF_LENGTH());
            Array.Copy(NSQ.sAR2_Q14, 0, psDD.sAR2_Q14, 0, SHAPE_LPC_ORDER_MAX);
        }

        offset_Q10   = TablesOther.SKP_Silk_Quantization_Offsets_Q10[ psEncCtrlC.sigtype ][ psEncCtrlC.QuantOffsetType ];
        smpl_buf_idx = 0; /* index of oldest samples */

        decisionDelay = ( DECISION_DELAY < subfr_length ? DECISION_DELAY:subfr_length );

        /* For voiced frames limit the decision delay to lower than the pitch lag */
        if( psEncCtrlC.sigtype == SIG_TYPE_VOICED ) {
            for( k = 0; k < NB_SUBFR; k++ ) {
                decisionDelay = ( decisionDelay < (psEncCtrlC.pitchL[ k ] - LTP_ORDER / 2 - 1) ?
                        decisionDelay:(psEncCtrlC.pitchL[ k ] - LTP_ORDER / 2 - 1));
            }
        } else if( lag > 0 ) {
            decisionDelay = Math.Min( decisionDelay, lag - LTP_ORDER / 2 - 1 );
        }

        if( LSFInterpFactor_Q2 == ( 1 << 2 ) ) {
            LSF_interpolation_flag = 0;
        } else {
            LSF_interpolation_flag = 1;
        }

        /* Setup pointers to start of sub frame */
        pxq                   = NSQ.xq;
        pxq_offset            = psEncC.frame_length;
        NSQ.sLTP_shp_buf_idx = psEncC.frame_length;
        NSQ.sLTP_buf_idx     = psEncC.frame_length;
        subfr = 0;
        for( k = 0; k < NB_SUBFR; k++ ) {
            A_Q12      = PredCoef_Q12;
            A_Q12_offset =  ( ( k >> 1 ) | ( 1 - LSF_interpolation_flag ) ) * MAX_LPC_ORDER ;
            B_Q14      = LTPCoef_Q14;
            B_Q14_offset = k * LTP_ORDER;
            AR_shp_Q13 = AR2_Q13;
            AR_shp_Q13_offset = k * SHAPE_LPC_ORDER_MAX;

            NSQ.rewhite_flag = 0;
            if( psEncCtrlC.sigtype == SIG_TYPE_VOICED ) {
                /* Voiced */
                lag = psEncCtrlC.pitchL[ k ];

                /* Re-whitening */
                if( ( k & ( 3 - ( LSF_interpolation_flag << 1 ) ) ) == 0 ) {
                    if( k == 2 ) {
                        /* RESET DELAYED DECISIONS */
                        /* Find winner */
                        RDmin_Q10 = psDelDec[ 0 ].RD_Q10;
                        Winner_ind = 0;
                        for( i = 1; i < psEncC.nStatesDelayedDecision; i++ ) {
                            if( psDelDec[ i ].RD_Q10 < RDmin_Q10 ) {
                                RDmin_Q10 = psDelDec[ i ].RD_Q10;
                                Winner_ind = i;
                            }
                        }
                        for( i = 0; i < psEncC.nStatesDelayedDecision; i++ ) {
                            if( i != Winner_ind ) {
                                psDelDec[ i ].RD_Q10 += int.MaxValue >> 4;
                                System.Diagnostics.Debug.Assert( psDelDec[ i ].RD_Q10 >= 0 );
                            }
                        }

                        /* Copy part of signals from winner state to output and long-term filter states */
                        psDD = psDelDec[ Winner_ind ];
                        last_smple_idx = smpl_buf_idx + decisionDelay;
                        for( i = 0; i < decisionDelay; i++ ) {
                            last_smple_idx = ( last_smple_idx - 1 ) & DECISION_DELAY_MASK;
//                            q[   i - decisionDelay ] = ( SKP_int )SKP_RSHIFT( psDD.Q_Q10[ last_smple_idx ], 10 );
                            q_tmp[   q_tmp_offset + i - decisionDelay ] = unchecked((byte)(psDD.Q_Q10[last_smple_idx] >> 10));

//                            pxq[ i - decisionDelay ] = ( SKP_int16 )SKP_SAT16( SKP_RSHIFT_ROUND(
//                                SKP_SMULWW( psDD.Xq_Q10[ last_smple_idx ],
//                                psDD.Gain_Q16[ last_smple_idx ] ), 10 ) );
                            pxq[ pxq_offset + i - decisionDelay ] = (short) SigProcFIX.SKP_SAT16( SigProcFIX.SKP_RSHIFT_ROUND(
                                    SKP_SMULWW( psDD.Xq_Q10[ last_smple_idx ],
                                    psDD.Gain_Q16[ last_smple_idx ] ), 10 ) );
                            NSQ.sLTP_shp_Q10[ NSQ.sLTP_shp_buf_idx - decisionDelay + i ] = psDD.Shape_Q10[ last_smple_idx ];
                        }

                        subfr = 0;
                    }

                    /* Rewhiten with new A coefs */
                    start_idx = psEncC.frame_length - lag - psEncC.predictLPCOrder - LTP_ORDER / 2;
                    System.Diagnostics.Debug.Assert( start_idx >= 0 );
                    System.Diagnostics.Debug.Assert( start_idx <= psEncC.frame_length - psEncC.predictLPCOrder );

//                    SKP_memset( FiltState, 0, psEncC.predictLPCOrder * sizeof( SKP_int32 ) );
                    Array.Fill(FiltState, 0, 0, psEncC.predictLPCOrder);
                    MA.SKP_Silk_MA_Prediction( NSQ.xq, start_idx + k * psEncC.subfr_length,
                        A_Q12, A_Q12_offset, FiltState, sLTP, start_idx, psEncC.frame_length - start_idx, psEncC.predictLPCOrder );

                    NSQ.sLTP_buf_idx = psEncC.frame_length;
                    NSQ.rewhite_flag = 1;
                }
            }

            /* Noise shape parameters */
            System.Diagnostics.Debug.Assert( HarmShapeGain_Q14[ k ] >= 0 );
            HarmShapeFIRPacked_Q14  =                        ( HarmShapeGain_Q14[ k ] >> 2 );
            HarmShapeFIRPacked_Q14 |= ( ( HarmShapeGain_Q14[ k ] >> 1 ) << 16 );

            SKP_Silk_nsq_del_dec_scale_states( NSQ, psDelDec, x_tmp, x_tmp_offset, x_sc_Q10,
                subfr_length, sLTP, sLTP_Q16, k, psEncC.nStatesDelayedDecision, smpl_buf_idx,
                LTP_scale_Q14, Gains_Q16, psEncCtrlC.pitchL );

            SKP_Silk_noise_shape_quantizer_del_dec( NSQ, psDelDec, psEncCtrlC.sigtype, x_sc_Q10, q_tmp, q_tmp_offset, pxq, pxq_offset,
                    sLTP_Q16, A_Q12, A_Q12_offset, B_Q14, B_Q14_offset, AR_shp_Q13, AR_shp_Q13_offset, lag, HarmShapeFIRPacked_Q14, Tilt_Q14[ k ],
                    LF_shp_Q14[ k ], Gains_Q16[ k ], Lambda_Q10, offset_Q10, psEncC.subfr_length, subfr++, psEncC.shapingLPCOrder, psEncC.predictLPCOrder,
            psEncC.warping_Q16, psEncC.nStatesDelayedDecision, ref smpl_buf_idx, decisionDelay,
            workspace.SampleStates );

            x_tmp_offset   += psEncC.subfr_length;
            q_tmp_offset   += psEncC.subfr_length;
            pxq_offset += psEncC.subfr_length;
        }

        /* Find winner */
        RDmin_Q10 = psDelDec[ 0 ].RD_Q10;
        Winner_ind = 0;
        for( k = 1; k < psEncC.nStatesDelayedDecision; k++ ) {
            if( psDelDec[ k ].RD_Q10 < RDmin_Q10 ) {
                RDmin_Q10 = psDelDec[ k ].RD_Q10;
                Winner_ind = k;
            }
        }

        /* Copy part of signals from winner state to output and long-term filter states */
        psDD = psDelDec[ Winner_ind ];
        psEncCtrlC.Seed = psDD.SeedInit;
        last_smple_idx = smpl_buf_idx + decisionDelay;
        for( i = 0; i < decisionDelay; i++ ) {
            last_smple_idx = ( last_smple_idx - 1 ) & DECISION_DELAY_MASK;
            q_tmp[q_tmp_offset + i - decisionDelay] = unchecked((byte)(psDD.Q_Q10[last_smple_idx] >> 10));
            pxq[ pxq_offset + i - decisionDelay ] = ( short )SigProcFIX.SKP_SAT16( SigProcFIX.SKP_RSHIFT_ROUND(
                SKP_SMULWW( psDD.Xq_Q10[ last_smple_idx ], psDD.Gain_Q16[ last_smple_idx ] ), 10 ) );
            NSQ.sLTP_shp_Q10[ NSQ.sLTP_shp_buf_idx - decisionDelay + i ] = psDD.Shape_Q10[ last_smple_idx ];
            sLTP_Q16[          NSQ.sLTP_buf_idx     - decisionDelay + i ] = psDD.Pred_Q16[  last_smple_idx ];

        }
//        SKP_memcpy( NSQ.sLPC_Q14, &psDD.sLPC_Q14[ psEncC.subfr_length ], NSQ_LPC_BUF_LENGTH * sizeof( SKP_int32 ) );
        Array.Copy(psDD.sLPC_Q14, psEncC.subfr_length, NSQ.sLPC_Q14, 0, NSQ_LPC_BUF_LENGTH());
        Array.Copy(psDD.sAR2_Q14, 0, NSQ.sAR2_Q14, 0, SHAPE_LPC_ORDER_MAX);

        /* Update states */
        NSQ.sLF_AR_shp_Q12    = psDD.LF_AR_Q12;
        NSQ.lagPrev           = psEncCtrlC.pitchL[ NB_SUBFR - 1 ];

        /* Save quantized speech and noise shaping signals */
//        SKP_memcpy( NSQ.xq,           &NSQ.xq[           psEncC.frame_length ], psEncC.frame_length * sizeof( SKP_int16 ) );
//        SKP_memcpy( NSQ.sLTP_shp_Q10, &NSQ.sLTP_shp_Q10[ psEncC.frame_length ], psEncC.frame_length * sizeof( SKP_int32 ) );
        Array.Copy(NSQ.xq, psEncC.frame_length, NSQ.xq, 0, psEncC.frame_length);
        Array.Copy(NSQ.sLTP_shp_Q10, psEncC.frame_length, NSQ.sLTP_shp_Q10, 0, psEncC.frame_length);
    }

    /**
     * Noise shape quantizer for one subframe.
     * @param NSQ NSQ state
     * @param psDelDec Delayed decision states
     * @param sigtype Signal type
     * @param x_Q10
     * @param q
     * @param q_offset
     * @param xq
     * @param xq_offset
     * @param sLTP_Q16 LTP filter state
     * @param a_Q12 Short term prediction coefs
     * @param a_Q12_offset
     * @param b_Q14 Long term prediction coefs
     * @param b_Q14_offset
     * @param AR_shp_Q13 Noise shaping coefs
     * @param AR_shp_Q13_offset
     * @param lag Pitch lag
     * @param HarmShapeFIRPacked_Q14
     * @param Tilt_Q14 Spectral tilt
     * @param LF_shp_Q14
     * @param Gain_Q16
     * @param Lambda_Q10
     * @param offset_Q10
     * @param length Input length
     * @param subfr Subframe number
     * @param shapingLPCOrder Shaping LPC filter order
     * @param predictLPCOrder Prediction LPC filter order
     * @param nStatesDelayedDecision Number of states input decision tree
     * @param smpl_buf_idx Index to newest samples input buffers
     * @param decisionDelay
     */
    internal static void SKP_Silk_noise_shape_quantizer_del_dec(
        SKP_Silk_nsq_state  NSQ,                   /* I/O  NSQ state                           */
        NSQDelDecStruct[] psDelDec,             /* I/O  Delayed decision states             */
        int                 sigtype,                /* I    Signal type                         */
        int[] x_Q10,                /* I                                        */
        byte[] q,                    /* O                                        */
        int                 q_offset,
        short[] xq,                   /* O                                        */
        int                 xq_offset,
        int[] sLTP_Q16,             /* I/O  LTP filter state                    */
        short[] a_Q12,                /* I    Short term prediction coefs         */
        int                 a_Q12_offset,
        short[] b_Q14,                /* I    Long term prediction coefs          */
        int                 b_Q14_offset,
        short[] AR_shp_Q13,           /* I    Noise shaping coefs                 */
        int                 AR_shp_Q13_offset,
        int                 lag,                    /* I    Pitch lag                           */
        int                 HarmShapeFIRPacked_Q14, /* I                                        */
        int                 Tilt_Q14,               /* I    Spectral tilt                       */
        int                 LF_shp_Q14,             /* I                                        */
        int                 Gain_Q16,               /* I                                        */
        int                 Lambda_Q10,             /* I                                        */
        int                 offset_Q10,             /* I                                        */
        int                 length,                 /* I    Input length                        */
        int                 subfr,                  /* I    Subframe number                     */
        int                 shapingLPCOrder,        /* I    Shaping LPC filter order            */
        int                 predictLPCOrder,        /* I    Prediction LPC filter order         */
        int                 warping_Q16,            /* I    Warping parameter                   */
        int                 nStatesDelayedDecision, /* I    Number of states input decision tree   */
        ref int             smpl_buf_idx,            /* I/O  Index to newest samples input buffers */
        int                 decisionDelay,          /* I                                        */
        NSQ_sample_struct[][] psSampleState
    )
    {
        int     i, j, k, Winner_ind, RDmin_ind, RDmax_ind, last_smple_idx;
        int   Winner_rand_state;
        int   LTP_pred_Q14, LPC_pred_Q10, n_AR_Q10, n_LTP_Q14;
        int   n_LF_Q10;
        int   r_Q10, rr_Q20, rd1_Q10, rd2_Q10, RDmin_Q10, RDmax_Q10;
        int   q1_Q10, q2_Q10;
        int   dither;
        int   exc_Q10, LPC_exc_Q10, xq_Q10;
        int   tmp, tmp1, tmp2, sLF_AR_shp_Q10;
        int[] pred_lag_ptr, shp_lag_ptr;
        int   pred_lag_ptr_offset, shp_lag_ptr_offset;
        int   []psLPC_Q14; int psLPC_Q14_offset;
        NSQDelDecStruct psDD;
        NSQ_sample_struct[]  psSS;

        shp_lag_ptr  = NSQ.sLTP_shp_Q10;
        shp_lag_ptr_offset = NSQ.sLTP_shp_buf_idx - lag + HARM_SHAPE_FIR_TAPS / 2;
        pred_lag_ptr = sLTP_Q16;
        pred_lag_ptr_offset = NSQ.sLTP_buf_idx - lag + LTP_ORDER / 2;

        for( i = 0; i < length; i++ ) {
            /* Perform common calculations used input all states */

            /* Long-term prediction */
            if( sigtype == SIG_TYPE_VOICED ) {
                /* Unrolled loop */
                LTP_pred_Q14 = SKP_SMULWB(               pred_lag_ptr[ pred_lag_ptr_offset+0 ], b_Q14[ b_Q14_offset+0 ] );
                LTP_pred_Q14 = SKP_SMLAWB( LTP_pred_Q14, pred_lag_ptr[ pred_lag_ptr_offset-1 ], b_Q14[ b_Q14_offset+1 ] );
                LTP_pred_Q14 = SKP_SMLAWB( LTP_pred_Q14, pred_lag_ptr[ pred_lag_ptr_offset-2 ], b_Q14[ b_Q14_offset+2 ] );
                LTP_pred_Q14 = SKP_SMLAWB( LTP_pred_Q14, pred_lag_ptr[ pred_lag_ptr_offset-3 ], b_Q14[ b_Q14_offset+3 ] );
                LTP_pred_Q14 = SKP_SMLAWB( LTP_pred_Q14, pred_lag_ptr[ pred_lag_ptr_offset-4 ], b_Q14[ b_Q14_offset+4 ] );
                pred_lag_ptr_offset++;
            } else {
                LTP_pred_Q14 = 0;
            }

            /* Long-term shaping */
            if( lag > 0 ) {
                /* Symmetric, packed FIR coefficients */
                n_LTP_Q14 = SKP_SMULWB( ( shp_lag_ptr[ shp_lag_ptr_offset+0 ] + shp_lag_ptr[ shp_lag_ptr_offset-2 ] ), HarmShapeFIRPacked_Q14 );
                n_LTP_Q14 = SKP_SMLAWT( n_LTP_Q14, shp_lag_ptr[ shp_lag_ptr_offset-1 ], HarmShapeFIRPacked_Q14 );
//                n_LTP_Q14 = SKP_LSHIFT( n_LTP_Q14, 6 );
                n_LTP_Q14 = ( n_LTP_Q14 << 6 );
                shp_lag_ptr_offset++;
            } else {
                n_LTP_Q14 = 0;
            }

            for( k = 0; k < nStatesDelayedDecision; k++ ) {
                /* Delayed decision state */
                psDD = psDelDec[ k ];

                /* Sample state */
                psSS = psSampleState[ k ];

                /* Generate dither */
                psDD.Seed = SigProcFIX.SKP_RAND( psDD.Seed );

                /* dither = rand_seed < 0 ? 0xFFFFFFFF : 0; */
//                dither = SKP_RSHIFT( psDD.Seed, 31 );
                dither = ( psDD.Seed >> 31 );

                /* Pointer used input short term prediction and shaping */
                psLPC_Q14 = psDD.sLPC_Q14;
                psLPC_Q14_offset = NSQ_LPC_BUF_LENGTH() - 1 + i;
                /* Short-term prediction */
                System.Diagnostics.Debug.Assert( predictLPCOrder >= 10 );            /* check that unrolling works */
                System.Diagnostics.Debug.Assert( ( predictLPCOrder  & 1 ) == 0 );    /* check that order is even */
//                SKP_System.Diagnostics.Debug.Assert( ( (SKP_int64)a_Q12 & 3 ) == 0 );    /* check that array starts at 4-byte aligned address */
                /* Partially unrolled */
                LPC_pred_Q10 = SKP_SMULWB(               psLPC_Q14[ psLPC_Q14_offset+0 ], a_Q12[ a_Q12_offset+0 ] );
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
                    LPC_pred_Q10 = SKP_SMLAWB( LPC_pred_Q10, psLPC_Q14[ psLPC_Q14_offset-j ], a_Q12[ a_Q12_offset+j]);
                }

                /* Noise shape feedback */
                System.Diagnostics.Debug.Assert( ( shapingLPCOrder & 1 ) == 0 );
                tmp2 = SKP_SMLAWB( psLPC_Q14[ psLPC_Q14_offset ], psDD.sAR2_Q14[ 0 ], warping_Q16 );
                tmp1 = SKP_SMLAWB( psDD.sAR2_Q14[ 0 ], psDD.sAR2_Q14[ 1 ] - tmp2, warping_Q16 );
                psDD.sAR2_Q14[ 0 ] = tmp2;
                n_AR_Q10 = SKP_SMULWB( tmp2, AR_shp_Q13[ AR_shp_Q13_offset ] );
                for( j = 2; j < shapingLPCOrder; j += 2 ) {
                    tmp2 = SKP_SMLAWB( psDD.sAR2_Q14[ j - 1 ], psDD.sAR2_Q14[ j ] - tmp1, warping_Q16 );
                    psDD.sAR2_Q14[ j - 1 ] = tmp1;
                    n_AR_Q10 = SKP_SMLAWB( n_AR_Q10, tmp1, AR_shp_Q13[ AR_shp_Q13_offset + j - 1 ] );
                    tmp1 = SKP_SMLAWB( psDD.sAR2_Q14[ j ], psDD.sAR2_Q14[ j + 1 ] - tmp2, warping_Q16 );
                    psDD.sAR2_Q14[ j ] = tmp2;
                    n_AR_Q10 = SKP_SMLAWB( n_AR_Q10, tmp2, AR_shp_Q13[ AR_shp_Q13_offset + j ] );
                }
                psDD.sAR2_Q14[ shapingLPCOrder - 1 ] = tmp1;
                n_AR_Q10 = SKP_SMLAWB( n_AR_Q10, tmp1, AR_shp_Q13[ AR_shp_Q13_offset + shapingLPCOrder - 1 ] );

//                n_AR_Q10 = SKP_RSHIFT( n_AR_Q10, 1 );           /* Q11 -> Q10 */
                n_AR_Q10 = ( n_AR_Q10 >> 1 );           /* Q11 -> Q10 */
                n_AR_Q10 = SKP_SMLAWB( n_AR_Q10, psDD.LF_AR_Q12, Tilt_Q14 );

                n_LF_Q10   = ( SKP_SMULWB( psDD.Shape_Q10[ smpl_buf_idx ], LF_shp_Q14 ) << 2 );
                n_LF_Q10   = SKP_SMLAWT( n_LF_Q10, psDD.LF_AR_Q12, LF_shp_Q14 );

                /* Input minus prediction plus noise feedback                       */
                /* r = x[ i ] - LTP_pred - LPC_pred + n_AR + n_Tilt + n_LF + n_LTP  */
                tmp   = ( LTP_pred_Q14 - n_LTP_Q14 );                       /* Add Q14 stuff */
                tmp >>= 4;
                tmp   = ( tmp + LPC_pred_Q10 );                             /* add Q10 stuff */
                tmp   = ( tmp - n_AR_Q10 );                                 /* subtract Q10 stuff */
                tmp   = ( tmp - n_LF_Q10 );                                 /* subtract Q10 stuff */
                r_Q10 = ( x_Q10[ i ] - tmp );                               /* residual error Q10 */

                /* Flip sign depending on dither */
                r_Q10 = ( r_Q10 ^ dither ) - dither;
                r_Q10 = ( r_Q10 - offset_Q10 );
                r_Q10 = SigProcFIX.SKP_LIMIT_32( r_Q10, -64 << 10, 64 << 10 );

                /* Find two quantization level candidates and measure their rate-distortion */
                if( r_Q10 < -1536 ) {
                    q1_Q10  = ( SigProcFIX.SKP_RSHIFT_ROUND( r_Q10, 10 ) << 10 );
                    r_Q10   = ( r_Q10 - q1_Q10 );
                    rd1_Q10 = ( SKP_SMLABB( ( -( q1_Q10 + offset_Q10 ) * Lambda_Q10 ), r_Q10, r_Q10 ) >> 10 );
                    rd2_Q10 = ( rd1_Q10 + 1024 );
                    rd2_Q10 = ( rd2_Q10 - SigProcFIX.SKP_ADD_LSHIFT32( Lambda_Q10, r_Q10, 1 ) );
                    q2_Q10  = ( q1_Q10 + 1024 );
                } else if( r_Q10 > 512 ) {
                    q1_Q10  = ( SigProcFIX.SKP_RSHIFT_ROUND( r_Q10, 10 ) << 10 );
                    r_Q10   = ( r_Q10 - q1_Q10 );
                    rd1_Q10 = ( SKP_SMLABB( ( ( q1_Q10 + offset_Q10 ) * Lambda_Q10 ), r_Q10, r_Q10 ) >> 10 );
                    rd2_Q10 = ( rd1_Q10 + 1024 );
                    rd2_Q10 = ( rd2_Q10 - SigProcFIX.SKP_SUB_LSHIFT32( Lambda_Q10, r_Q10, 1 ) );
                    q2_Q10  = ( q1_Q10 - 1024 );
                } else {            /* r_Q10 >= -1536 && q1_Q10 <= 512 */
                    rr_Q20  = SKP_SMULBB( offset_Q10, Lambda_Q10 );
                    rd2_Q10 = ( SKP_SMLABB( rr_Q20, r_Q10, r_Q10 ) >> 10 );
                    rd1_Q10 = ( rd2_Q10 + 1024 );
                    rd1_Q10 = ( rd1_Q10 + SigProcFIX.SKP_SUB_RSHIFT32( SigProcFIX.SKP_ADD_LSHIFT32( Lambda_Q10, r_Q10, 1 ), rr_Q20, 9 ) );
                    q1_Q10  = -1024;
                    q2_Q10  = 0;
                }

                if( rd1_Q10 < rd2_Q10 ) {
                    psSS[ 0 ].RD_Q10 = ( psDD.RD_Q10 + rd1_Q10 );
                    psSS[ 1 ].RD_Q10 = ( psDD.RD_Q10 + rd2_Q10 );
                    psSS[ 0 ].Q_Q10 = q1_Q10;
                    psSS[ 1 ].Q_Q10 = q2_Q10;
                } else {
                    psSS[ 0 ].RD_Q10 = ( psDD.RD_Q10 + rd2_Q10 );
                    psSS[ 1 ].RD_Q10 = ( psDD.RD_Q10 + rd1_Q10 );
                    psSS[ 0 ].Q_Q10 = q2_Q10;
                    psSS[ 1 ].Q_Q10 = q1_Q10;
                }

                /* Update states for best quantization */

                /* Quantized excitation */
                exc_Q10 = ( offset_Q10 + psSS[ 0 ].Q_Q10 );
                exc_Q10 = ( exc_Q10 ^ dither ) - dither;

                /* Add predictions */
                LPC_exc_Q10 = exc_Q10 + SigProcFIX.SKP_RSHIFT_ROUND( LTP_pred_Q14, 4 );
                xq_Q10      = ( LPC_exc_Q10 + LPC_pred_Q10 );

                /* Update states */
                sLF_AR_shp_Q10         = (  xq_Q10 - n_AR_Q10 );
                psSS[ 0 ].sLTP_shp_Q10 = (  sLF_AR_shp_Q10 - n_LF_Q10 );
                psSS[ 0 ].LF_AR_Q12    = ( sLF_AR_shp_Q10 << 2 );
                psSS[ 0 ].xq_Q14       = ( xq_Q10 << 4 );
                psSS[ 0 ].LPC_exc_Q16  = ( LPC_exc_Q10 << 6 );

                /* Update states for second best quantization */

                /* Quantized excitation */
                exc_Q10 = ( offset_Q10 + psSS[ 1 ].Q_Q10 );
                exc_Q10 = ( exc_Q10 ^ dither ) - dither;

                /* Add predictions */
                LPC_exc_Q10 = exc_Q10 + SigProcFIX.SKP_RSHIFT_ROUND( LTP_pred_Q14, 4 );
                xq_Q10      = ( LPC_exc_Q10 + LPC_pred_Q10 );

                /* Update states */
                sLF_AR_shp_Q10         = (  xq_Q10 - n_AR_Q10 );
                psSS[ 1 ].sLTP_shp_Q10 = (  sLF_AR_shp_Q10 - n_LF_Q10 );
                psSS[ 1 ].LF_AR_Q12    = ( sLF_AR_shp_Q10 << 2 );
                psSS[ 1 ].xq_Q14       = ( xq_Q10 << 4 );
                psSS[ 1 ].LPC_exc_Q16  = ( LPC_exc_Q10 << 6 );
            }

            smpl_buf_idx  = ( smpl_buf_idx - 1 ) & DECISION_DELAY_MASK;                   /* Index to newest samples              */
            last_smple_idx = ( smpl_buf_idx + decisionDelay ) & DECISION_DELAY_MASK;       /* Index to decisionDelay old samples   */

            /* Find winner */
            RDmin_Q10 = psSampleState[ 0 ][ 0 ].RD_Q10;
            Winner_ind = 0;
            for( k = 1; k < nStatesDelayedDecision; k++ ) {
                if( psSampleState[ k ][ 0 ].RD_Q10 < RDmin_Q10 ) {
                    RDmin_Q10   = psSampleState[ k ][ 0 ].RD_Q10;
                    Winner_ind = k;
                }
            }

            /* Increase RD values of expired states */
            Winner_rand_state = psDelDec[ Winner_ind ].RandState[ last_smple_idx ];
            for( k = 0; k < nStatesDelayedDecision; k++ ) {
                if( psDelDec[ k ].RandState[ last_smple_idx ] != Winner_rand_state ) {
                    psSampleState[ k ][ 0 ].RD_Q10 += int.MaxValue >> 4;
                    psSampleState[ k ][ 1 ].RD_Q10 += int.MaxValue >> 4;
                    System.Diagnostics.Debug.Assert( psSampleState[ k ][ 0 ].RD_Q10 >= 0 );
                }
            }

            /* Find worst input first set and best input second set */
            RDmax_Q10  = psSampleState[ 0 ][ 0 ].RD_Q10;
            RDmin_Q10  = psSampleState[ 0 ][ 1 ].RD_Q10;
            RDmax_ind = 0;
            RDmin_ind = 0;
            for( k = 1; k < nStatesDelayedDecision; k++ ) {
                /* find worst input first set */
                if( psSampleState[ k ][ 0 ].RD_Q10 > RDmax_Q10 ) {
                    RDmax_Q10  = psSampleState[ k ][ 0 ].RD_Q10;
                    RDmax_ind = k;
                }
                /* find best input second set */
                if( psSampleState[ k ][ 1 ].RD_Q10 < RDmin_Q10 ) {
                    RDmin_Q10  = psSampleState[ k ][ 1 ].RD_Q10;
                    RDmin_ind = k;
                }
            }

            /* Replace a state if best from second set outperforms worst input first set */
            if( RDmin_Q10 < RDmax_Q10 ) {
//                SKP_Silk_copy_del_dec_state( &psDelDec[ RDmax_ind ], &psDelDec[ RDmin_ind ], i );
                SKP_Silk_copy_del_dec_state( psDelDec[ RDmax_ind ], psDelDec[ RDmin_ind ], i );
//TODO:how to copy a struct ???
//                SKP_memcpy( &psSampleState[ RDmax_ind ][ 0 ], &psSampleState[ RDmin_ind ][ 1 ], sizeof( NSQ_sample_struct ) );
                psSampleState[ RDmax_ind ][ 0 ].CopyFrom(psSampleState[ RDmin_ind ][ 1 ]);
            }

            /* Write samples from winner to output and long-term filter states */
            psDD = psDelDec[ Winner_ind ];
            if( subfr > 0 || i >= decisionDelay ) {
                q[  q_offset + i - decisionDelay ] = unchecked((byte)(psDD.Q_Q10[last_smple_idx] >> 10));
                xq[ xq_offset + i - decisionDelay ] = ( short )SigProcFIX.SKP_SAT16( SigProcFIX.SKP_RSHIFT_ROUND(
                    SKP_SMULWW( psDD.Xq_Q10[ last_smple_idx ], psDD.Gain_Q16[ last_smple_idx ] ), 10 ) );
                NSQ.sLTP_shp_Q10[ NSQ.sLTP_shp_buf_idx - decisionDelay ] = psDD.Shape_Q10[ last_smple_idx ];
                sLTP_Q16[          NSQ.sLTP_buf_idx     - decisionDelay ] = psDD.Pred_Q16[  last_smple_idx ];
            }
            NSQ.sLTP_shp_buf_idx++;
            NSQ.sLTP_buf_idx++;

            /* Update states */
            for( k = 0; k < nStatesDelayedDecision; k++ ) {
                psDD                                     = psDelDec[ k ];
//TODO: psSS is an array of reference rather than a reference.
//                psSS                                     = &psSampleState[ k ][ 0 ];
                psSS                                     = psSampleState[ k ];
                psDD.LF_AR_Q12                          = psSS[0].LF_AR_Q12;
                psDD.sLPC_Q14[ NSQ_LPC_BUF_LENGTH() + i ] = psSS[0].xq_Q14;
                psDD.Xq_Q10[    smpl_buf_idx ]         = ( psSS[0].xq_Q14 >> 4 );
                psDD.Q_Q10[     smpl_buf_idx ]         = psSS[0].Q_Q10;
                psDD.Pred_Q16[  smpl_buf_idx ]         = psSS[0].LPC_exc_Q16;
                psDD.Shape_Q10[ smpl_buf_idx ]         = psSS[0].sLTP_shp_Q10;
                psDD.Seed                               = SigProcFIX.SKP_ADD_RSHIFT32( psDD.Seed, psSS[0].Q_Q10, 10 );
                psDD.RandState[ smpl_buf_idx ]         = psDD.Seed;
                psDD.RD_Q10                             = psSS[0].RD_Q10;
                psDD.Gain_Q16[  smpl_buf_idx ]         = Gain_Q16;
            }
        }
        /* Update LPC states */
        for( k = 0; k < nStatesDelayedDecision; k++ ) {
            psDD = psDelDec[ k ];
            Array.Copy(psDD.sLPC_Q14, length, psDD.sLPC_Q14, 0, NSQ_LPC_BUF_LENGTH());
        }
    }

    /**
     *
     * @param NSQ NSQ state
     * @param psDelDec Delayed decision states
     * @param x Input input Q0
     * @param x_offset offset of valid data.
     * @param x_sc_Q10 nput scaled with 1/Gain input Q10
     * @param length Length of input
     * @param sLTP Re-whitened LTP state input Q0
     * @param sLTP_Q16 LTP state matching scaled input
     * @param subfr Subframe number
     * @param nStatesDelayedDecision Number of del dec states
     * @param smpl_buf_idx Index to newest samples input buffers
     * @param LTP_scale_Q14 LTP state scaling
     * @param Gains_Q16
     * @param pitchL Pitch lag
     */
    internal static void SKP_Silk_nsq_del_dec_scale_states(
            SKP_Silk_nsq_state  NSQ,                   /* I/O  NSQ state                           */
            NSQDelDecStruct[] psDelDec,             /* I/O  Delayed decision states             */
            short[] x,                    /* I    Input input Q0                         */
            int                 x_offset,
            int[] x_sc_Q10,             /* O    Input scaled with 1/Gain input Q10     */
            int                 length,                 /* I    Length of input                     */
            short[] sLTP,                 /* I    Re-whitened LTP state input Q0         */
            int[] sLTP_Q16,             /* O    LTP state matching scaled input     */
            int                 subfr,                  /* I    Subframe number                     */
            int                 nStatesDelayedDecision, /* I    Number of del dec states            */
            int                 smpl_buf_idx,           /* I    Index to newest samples input buffers  */
            int           LTP_scale_Q14,          /* I    LTP state scaling                   */
            int[] Gains_Q16,  /* I                                        */
            int[] pitchL      /* I    Pitch lag                           */
    )
    {
        int            i, k, lag;
        int            inv_gain_Q16, gain_adj_Q16, inv_gain_Q32;
        NSQDelDecStruct psDD;

        inv_gain_Q16 = Inlines.SKP_INVERSE32_varQ( Math.Max( Gains_Q16[subfr], 1 ), 32 );
        inv_gain_Q16 = Math.Min(inv_gain_Q16, short.MaxValue);
        lag          = pitchL[ subfr ];
        /* After rewhitening the LTP state is un-scaled. So scale with inv_gain_Q16 */
        if( NSQ.rewhite_flag != 0) {
            inv_gain_Q32 = ( inv_gain_Q16 << 16 );
            if( subfr == 0 ) {
                /* Do LTP downscaling */
                inv_gain_Q32 = ( SKP_SMULWB( inv_gain_Q32, LTP_scale_Q14 ) << 2 );
            }
            for( i = NSQ.sLTP_buf_idx - lag - LTP_ORDER / 2; i < NSQ.sLTP_buf_idx; i++ ) {
                System.Diagnostics.Debug.Assert( i < MAX_FRAME_LENGTH );
                sLTP_Q16[ i ] = SKP_SMULWB( inv_gain_Q32, sLTP[ i ] );
            }
        }

        /* Adjust for changing gain */
        if( inv_gain_Q16 != NSQ.prev_inv_gain_Q16 ) {
            gain_adj_Q16 = SilkCodec.NET.Managed.NSQ.SKP_DIV32_varQ( inv_gain_Q16, NSQ.prev_inv_gain_Q16, 16 );

            for( k = 0; k < nStatesDelayedDecision; k++ ) {
                psDD = psDelDec[ k ];

                /* Scale scalar states */
                psDD.LF_AR_Q12 = SKP_SMULWW( gain_adj_Q16, psDD.LF_AR_Q12 );

                /* scale short term state */
                for( i = 0; i < NSQ_LPC_BUF_LENGTH(); i++ ) {
                    psDD.sLPC_Q14[ NSQ_LPC_BUF_LENGTH() - i - 1 ] = SKP_SMULWW( gain_adj_Q16,
                            psDD.sLPC_Q14[ NSQ_LPC_BUF_LENGTH() - i - 1 ] );
                }
                for( i = 0; i < SHAPE_LPC_ORDER_MAX; i++ ) {
                    psDD.sAR2_Q14[ i ] = SKP_SMULWW( gain_adj_Q16, psDD.sAR2_Q14[ i ] );
                }
                for( i = 0; i < DECISION_DELAY; i++ ) {
                    psDD.Pred_Q16[  i ] = SKP_SMULWW( gain_adj_Q16, psDD.Pred_Q16[  i ] );
                    psDD.Shape_Q10[ i ] = SKP_SMULWW( gain_adj_Q16, psDD.Shape_Q10[ i ] );
                }
            }

            /* Scale long term shaping state */

            for( i = NSQ.sLTP_shp_buf_idx - length * NB_SUBFR; i < NSQ.sLTP_shp_buf_idx; i++ ) {
                NSQ.sLTP_shp_Q10[ i ] = SKP_SMULWW( gain_adj_Q16, NSQ.sLTP_shp_Q10[ i ] );
            }

            /* Scale LTP predict state */
            if( NSQ.rewhite_flag == 0 ) {
                for( i = NSQ.sLTP_buf_idx - lag - LTP_ORDER / 2; i < NSQ.sLTP_buf_idx; i++ ) {
                    sLTP_Q16[ i ] = SKP_SMULWW( gain_adj_Q16, sLTP_Q16[ i ] );
                }
            }
        }

        /* Scale input */
        for( i = 0; i < length; i++ ) {
            x_sc_Q10[ i ] = (SKP_SMULBB( x[ x_offset + i ], ( short )inv_gain_Q16 ) >> 6 );
        }

        /* save inv_gain */
        System.Diagnostics.Debug.Assert( inv_gain_Q16 != 0 );
        NSQ.prev_inv_gain_Q16 = inv_gain_Q16;
    }

    /**
     *
     * @param DD_dst Dst del dec state
     * @param DD_src Src del dec state
     * @param LPC_state_idx Index to LPC buffer
     */
    internal static void SKP_Silk_copy_del_dec_state(
            NSQDelDecStruct  DD_dst,                /* I    Dst del dec state                   */
            NSQDelDecStruct  DD_src,                /* I    Src del dec state                   */
            int                 LPC_state_idx           /* I    Index to LPC buffer                 */
    )
    {
        Array.Copy(DD_src.RandState, 0, DD_dst.RandState, 0, DECISION_DELAY);
        Array.Copy(DD_src.Q_Q10, 0, DD_dst.Q_Q10, 0, DECISION_DELAY);
        Array.Copy(DD_src.Pred_Q16, 0, DD_dst.Pred_Q16, 0, DECISION_DELAY);
        Array.Copy(DD_src.Shape_Q10, 0, DD_dst.Shape_Q10, 0, DECISION_DELAY);
        Array.Copy(DD_src.Xq_Q10, 0, DD_dst.Xq_Q10, 0, DECISION_DELAY);
        Array.Copy(DD_src.sAR2_Q14, 0, DD_dst.sAR2_Q14, 0, SHAPE_LPC_ORDER_MAX);

        Array.Copy(DD_src.sLPC_Q14, LPC_state_idx, DD_dst.sLPC_Q14, LPC_state_idx, NSQ_LPC_BUF_LENGTH());
        DD_dst.LF_AR_Q12 = DD_src.LF_AR_Q12;
        DD_dst.Seed      = DD_src.Seed;
        DD_dst.SeedInit  = DD_src.SeedInit;
        DD_dst.RD_Q10    = DD_src.RD_Q10;
    }
}
