// Upstream state and codebook fields are initialized by the encoder helpers.
#pragma warning disable CS8618, CS0169
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

using System;
using System.Numerics;
using static SilkCodec.NET.Managed.Define;

namespace SilkCodec.NET.Managed;

/*
 * Warped noise shaping state fields adapted from SILK SDK 1.0.9.
 * Copyright (c) 2006-2012, Skype Limited. See THIRD-PARTY-NOTICES.
 */

/**
 *
 * @author Jing Dai
 * @author Dingxin Xu
 */
internal static class Structs
{
    internal static T[][] CreateJagged<T>(int rows, int columns)
    {
        var result = new T[rows][];
        for (int i = 0; i < rows; i++)
        {
            result[i] = new T[columns];
        }
        return result;
    }
}

/**
 * Noise shaping quantization state.
 *
 * @author Jing Dai
 * @author Dingxin Xu
 */
internal sealed class SKP_Silk_nsq_state
{
    internal short[] xq = new short[2 * MAX_FRAME_LENGTH]; /* Buffer for quantized output signal */
    internal int[]   sLTP_shp_Q10 = new int[ 2 * MAX_FRAME_LENGTH ];
    internal int[]   sLPC_Q14 = new int[ MAX_FRAME_LENGTH / NB_SUBFR + NSQ_LPC_BUF_LENGTH() ];
    internal int[]   sAR2_Q14 = new int[ SHAPE_LPC_ORDER_MAX ];
    internal int     sLF_AR_shp_Q12;
    internal int     lagPrev;
    internal int     sLTP_buf_idx;
    internal int     sLTP_shp_buf_idx;
    internal int     rand_seed;
    internal int     prev_inv_gain_Q16;
    internal int     rewhite_flag;

    /**
     * override clone mthod.
     */
    public object clone()
    {
        var copy = (SKP_Silk_nsq_state)MemberwiseClone();
        copy.xq = (short[])xq.Clone();
        copy.sLTP_shp_Q10 = (int[])sLTP_shp_Q10.Clone();
        copy.sLPC_Q14 = (int[])sLPC_Q14.Clone();
        copy.sAR2_Q14 = (int[])sAR2_Q14.Clone();
        return copy;
    }

    internal void CopyFrom(SKP_Silk_nsq_state source)
    {
        Array.Copy(source.xq, xq, xq.Length);
        Array.Copy(source.sLTP_shp_Q10, sLTP_shp_Q10, sLTP_shp_Q10.Length);
        Array.Copy(source.sLPC_Q14, sLPC_Q14, sLPC_Q14.Length);
        Array.Copy(source.sAR2_Q14, sAR2_Q14, sAR2_Q14.Length);
        sLF_AR_shp_Q12 = source.sLF_AR_shp_Q12;
        lagPrev = source.lagPrev;
        sLTP_buf_idx = source.sLTP_buf_idx;
        sLTP_shp_buf_idx = source.sLTP_shp_buf_idx;
        rand_seed = source.rand_seed;
        prev_inv_gain_Q16 = source.prev_inv_gain_Q16;
        rewhite_flag = source.rewhite_flag;
    }

    /**
     * set all fields of the instance to zero
     */
    public void memZero()
    {
        Array.Fill(this.sAR2_Q14, 0);
        Array.Fill(this.sLPC_Q14, 0);
        Array.Fill(this.sLTP_shp_Q10, 0);
        Array.Fill(this.xq, (short)0);

        this.lagPrev = 0;
        this.prev_inv_gain_Q16 = 0;
        this.rand_seed = 0;
        this.rewhite_flag = 0;
        this.sLF_AR_shp_Q12 = 0;
        this.sLTP_buf_idx = 0;
        this.sLTP_shp_buf_idx = 0;
    }
}/* FIX*/

/**
 * Class for Low BitRate Redundant (LBRR) information.
 *
 * @author Jing Dai
 * @author Dingxin Xu
 */
internal sealed class SKP_SILK_LBRR_struct
{
    internal byte[]        payload = new byte[MAX_ARITHM_BYTES];
    internal int         nBytes;                         /* Number of bytes input payload                               */
    internal int         usage;                          /* Tells how the payload should be used as FEC              */

    public void memZero()
    {
        this.nBytes = 0;
        this.usage = 0;
        Array.Fill(this.payload, (byte)0);
    }
}

/**
 * VAD state.
 *
 * @author Jing Dai
 * @author Dingxin Xu
 */
internal sealed class SKP_Silk_VAD_state
{
    internal int[]     AnaState = new int[ 2 ];                  /* Analysis filterbank state: 0-8 kHz                       */
    internal int[]     AnaState1 = new int[ 2 ];                 /* Analysis filterbank state: 0-4 kHz                       */
    internal int[]     AnaState2 = new int[ 2 ];                 /* Analysis filterbank state: 0-2 kHz                       */
    internal int[]     XnrgSubfr = new int[ VAD_N_BANDS ];       /* Subframe energies                                        */
    internal int[]     NrgRatioSmth_Q8 = new int[ VAD_N_BANDS ]; /* Smoothed energy level input each band                       */
    internal short     HPstate;                        /* State of differentiator input the lowest band               */
    internal int[]     NL = new int[ VAD_N_BANDS ];              /* Noise energy level input each band                          */
    internal int[]     inv_NL = new int[ VAD_N_BANDS ];          /* Inverse noise energy level input each band                  */
    internal int[]     NoiseLevelBias = new int[ VAD_N_BANDS ];  /* Noise level estimator bias/offset                        */
    internal int   counter;                        /* Frame counter used input the initial phase                  */
}

/**
 * Range encoder/decoder state.
 *
 * @author Jing Dai
 * @author Dingxin Xu
 */
internal sealed class SKP_Silk_range_coder_state
{
    internal int   bufferLength;
    internal int   bufferIx;
    internal long  base_Q32;
    internal long  range_Q16;
    internal int   error;
    internal byte[] buffer = new byte[MAX_ARITHM_BYTES];/* Buffer containing payload                                */
}

/**
 * Input frequency range detection struct.
 *
 * @author Jing Dai
 * @author Dingxin Xu
 */
internal sealed class SKP_Silk_detect_SWB_state
{
    internal int[][] S_HP_8_kHz = Structs.CreateJagged<int>( NB_SOS ,  2 );  /* HP filter State */
    internal int     ConsecSmplsAboveThres;
    internal int     ActiveSpeech_ms;            /* Accumulated time with active speech */
    internal int     SWB_detected;               /* Flag to indicate SWB input */
    internal int     WB_detected;                /* Flag to indicate WB input */
}

/**
 * Variable cut-off low-pass filter state.
 *
 * @author Jing Dai
 * @author Dingxin Xu
 */
internal sealed class SKP_Silk_LP_state
{
    internal int[] In_LP_State = new int[ 2 ];           /* Low pass filter state */
    internal int   transition_frame_no;        /* Counter which is mapped to a cut-off frequency */
    internal int   mode;                       /* Operating mode, 0: switch down, 1: switch up */
}

/**
 * Class for one stage of MSVQ.
 *
 * @author Jing Dai
 * @author Dingxin Xu
 */
internal sealed class SKP_Silk_NLSF_CBS
{
    public SKP_Silk_NLSF_CBS(int nVectors, short[] CB_NLSF_Q15, short[] Rates_Q5)
    {
        this.CB_NLSF_Q15 = CB_NLSF_Q15;
        this.nVectors    = nVectors;
        this.Rates_Q5    = Rates_Q5;
    }
    public SKP_Silk_NLSF_CBS(int nVectors, short[] SKP_Silk_NLSF_MSVQ_CB0_10_Q15, int Q15_offset,
                            short[] SKP_Silk_NLSF_MSVQ_CB0_10_rates_Q5, int Q5_offset)
    {
        this.nVectors = nVectors;
        this.CB_NLSF_Q15 = new short[SKP_Silk_NLSF_MSVQ_CB0_10_Q15.Length-Q15_offset];
        Array.Copy( SKP_Silk_NLSF_MSVQ_CB0_10_Q15, Q15_offset, this.CB_NLSF_Q15, 0, this.CB_NLSF_Q15.Length);
        this.Rates_Q5 = new short[SKP_Silk_NLSF_MSVQ_CB0_10_rates_Q5.Length - Q5_offset];
        Array.Copy(SKP_Silk_NLSF_MSVQ_CB0_10_rates_Q5, Q5_offset, this.Rates_Q5, 0, this.Rates_Q5.Length);
    }
    public SKP_Silk_NLSF_CBS()
    {
    }
    //TODO: the three fields are constant input C.
    internal int      nVectors;
    internal short[]   CB_NLSF_Q15;
    internal short[]   Rates_Q5;
}

/**
 * Class containing NLSF MSVQ codebook.
 *
 * @author Jing Dai
 * @author Dingxin Xu
 */
internal sealed class SKP_Silk_NLSF_CB_struct
{
    public SKP_Silk_NLSF_CB_struct(int nStates, SKP_Silk_NLSF_CBS[] CBStages, int[] NDeltaMin_Q15,
                                    int[] CDF, int[][] StartPtr, int[] MiddleIx)
    {
        this.CBStages        = CBStages;
        this.CDF             = CDF;
        this.MiddleIx        = MiddleIx;
        this.NDeltaMin_Q15 = NDeltaMin_Q15;
        this.nStages       = nStates;
        this.StartPtr      = StartPtr;

    }
    public SKP_Silk_NLSF_CB_struct()
    {
    }
//TODO: this filed is constant input C.
    internal int                 nStages;

    /* Fields for (de)quantizing */
//TODO:CBStates should be defined as an array or an object reference?
    internal SKP_Silk_NLSF_CBS[]     CBStages;
    internal int[]                 NDeltaMin_Q15;

    /* Fields for arithmetic (de)coding */
    internal int[]                 CDF;
    internal int[][]                StartPtr;
    internal int[]                MiddleIx;
}

/**
 * Encoder state.
 *
 * @author Jing Dai
 * @author Dingxin Xu
 */
internal sealed class SKP_Silk_encoder_state
{
    internal SKP_Silk_range_coder_state      sRC = new SKP_Silk_range_coder_state();                            /* Range coder state                                                    */
    internal SKP_Silk_range_coder_state      sRC_LBRR = new SKP_Silk_range_coder_state();                       /* Range coder state (for low bitrate redundancy)                       */
    internal int[]                           In_HP_State = new int[ 2 ];     /* High pass filter state                                               */
    internal SKP_Silk_LP_state               sLP = new SKP_Silk_LP_state();                            /* Low pass filter state */
    internal SKP_Silk_VAD_state              sVAD = new SKP_Silk_VAD_state();                           /* Voice activity detector state                                        */

    internal int                         LBRRprevLastGainIndex;
    internal int                         prev_sigtype;
    internal int                         typeOffsetPrev;                 /* Previous signal type and quantization offset                         */
    internal int                         prevLag;
    internal int                         prev_lagIndex;
    internal int                         API_fs_Hz;                      /* API sampling frequency (Hz)                                          */
    internal int                         prev_API_fs_Hz;                 /* Previous API sampling frequency (Hz)                                 */
    internal int                         maxInternal_fs_kHz;             /* Maximum internal sampling frequency (kHz)                            */
    internal int                         fs_kHz;                         /* Internal sampling frequency (kHz)                                    */
    internal int                         fs_kHz_changed;                 /* Did we switch yet?                                                   */
    internal int                         frame_length;                   /* Frame length (samples)                                               */
    internal int                         subfr_length;                   /* Subframe length (samples)                                            */
    internal int                         la_pitch;                       /* Look-ahead for pitch analysis (samples)                              */
    internal int                         la_shape;                       /* Look-ahead for noise shape analysis (samples)                        */
    internal int                         shapeWinLength;                 /* Window length for noise shape analysis (samples)                     */
    internal int                         TargetRate_bps;                 /* Target bitrate (bps)                                                 */
    internal int                         PacketSize_ms;                  /* Number of milliseconds to put input each packet                         */
    internal int                         PacketLoss_perc;                /* Packet loss rate measured by farend                                  */
    internal int                         frameCounter;
    internal int                         Complexity;                     /* Complexity setting: 0-> low; 1-> medium; 2->high                     */
    internal int                         nStatesDelayedDecision;         /* Number of states input delayed decision quantization                    */
    internal int                         useInterpolatedNLSFs;           /* Flag for using NLSF interpolation                                    */
    internal int                         shapingLPCOrder;                /* Filter order for noise shaping filters                               */
    internal int                         warping_Q16;                    /* Warping parameter for warped noise shaping                           */
    internal int                         predictLPCOrder;                /* Filter order for prediction filters                                  */
    internal int                         pitchEstimationComplexity;      /* Complexity level for pitch estimator                                 */
    internal int                         pitchEstimationLPCOrder;        /* Whitening filter order for pitch estimator                           */
    internal int                         LTPQuantLowComplexity;          /* Flag for low complexity LTP quantization                             */
    internal int                         NLSF_MSVQ_Survivors;            /* Number of survivors input NLSF MSVQ                                     */
    internal int                         first_frame_after_reset;        /* Flag for deactivating NLSF interp. and fluc. reduction after resets  */

    /* Input/output buffering */
    internal short[]                     inputBuf = new short[ MAX_FRAME_LENGTH ];   /* buffer containin input signal                                        */
    internal int                         inputBufIx;
    internal int                         nFramesInPayloadBuf;            /* number of frames sitting input outputBuf                                */
    internal int                         nBytesInPayloadBuf;             /* number of bytes sitting input outputBuf                                 */

    /* Parameters For LTP scaling Control */
    internal int                         frames_since_onset;

    internal SKP_Silk_NLSF_CB_struct[]   psNLSF_CB = new SKP_Silk_NLSF_CB_struct[ 2 ];                /* Pointers to voiced/unvoiced NLSF codebooks */

    /* Struct for Inband LBRR */
    internal SKP_SILK_LBRR_struct[]      LBRR_buffer = new SKP_SILK_LBRR_struct[ MAX_LBRR_DELAY ];
    /*
     * LBRR_buffer is an array of references, which has to be created manually.
     */
    internal SKP_Silk_encoder_state()
    {
        for (int i = 0; i < MAX_LBRR_DELAY; i++)
        {
            LBRR_buffer[i] = new SKP_SILK_LBRR_struct();
        }
    }
    internal int                         oldest_LBRR_idx;
    internal int                         useInBandFEC;                   /* Saves the API setting for query                                      */
    internal int                         LBRR_enabled;
    internal int                         LBRR_GainIncreases;             /* Number of shifts to Gains to get LBRR rate Voiced frames             */

    /* Bitrate control */
    internal int                       bitrateDiff;                    /* Accumulated diff. between the target bitrate and the switch bitrates */
    internal int                       bitrate_threshold_up;           /* Threshold for switching to a higher internal sample frequency        */
    internal int                       bitrate_threshold_down;         /* Threshold for switching to a lower internal sample frequency         */
    internal SKP_Silk_resampler_state_struct  resampler_state = new SKP_Silk_resampler_state_struct();

    /* DTX */
    internal int                         noSpeechCounter;                /* Counts concecutive nonactive frames, used by DTX                     */
    internal int                         useDTX;                         /* Flag to enable DTX                                                   */
    internal int                         inDTX;                          /* Flag to signal DTX period                                            */
    internal int                         vadFlag;                        /* Flag to indicate Voice Activity                                      */

    /* Struct for detecting SWB input */
    internal SKP_Silk_detect_SWB_state       sSWBdetect = new SKP_Silk_detect_SWB_state();


    /* Buffers */
    internal byte[]                      q = new byte[ MAX_FRAME_LENGTH ];      /* pulse signal buffer */
    internal byte[]                      q_LBRR = new byte[ MAX_FRAME_LENGTH ]; /* pulse signal buffer */
    internal int[]                       nsqSLtpQ16 = new int[2 * MAX_FRAME_LENGTH];
    internal short[]                     nsqSLtp = new short[2 * MAX_FRAME_LENGTH];
    internal int[]                       nsqFiltState = new int[MAX_LPC_ORDER];
    internal int[]                       nsqXScQ10 = new int[MAX_FRAME_LENGTH / NB_SUBFR];
    internal NSQDelDecWorkspace          delayedDecisionWorkspace = new NSQDelDecWorkspace();

    internal int[]                       encodeAbsPulses = new int[MAX_FRAME_LENGTH];
    internal int[]                       encodeSumPulses = new int[MAX_NB_SHELL_BLOCKS];
    internal int[]                       encodePulseShifts = new int[MAX_NB_SHELL_BLOCKS];
    internal int[]                       encodePulsesCombined = new int[8];
    internal int[]                       shellEncoderScratch = new int[15];
}

/**
 * Encoder control.
 *
 * @author Jing Dai
 * @author Dingxin Xu
 */
internal sealed class SKP_Silk_encoder_control
{
    /* Quantization indices */
    internal int     lagIndex;
    internal int     contourIndex;
    internal int     PERIndex;
    internal int[]   LTPIndex = new int[ NB_SUBFR ];
    internal int[]   NLSFIndices = new int[ NLSF_MSVQ_MAX_CB_STAGES ];  /* NLSF path of quantized LSF vector   */
    internal int     NLSFInterpCoef_Q2;
    internal int[]   GainsIndices = new int[ NB_SUBFR ];
    internal int     Seed;
    internal int     LTP_scaleIndex;
    internal int     RateLevelIndex;
    internal int     QuantOffsetType;
    internal int     sigtype;

    /* Prediction and coding parameters */
    internal int[]   pitchL = new int[ NB_SUBFR ];

    internal int     LBRR_usage;                     /* Low bitrate redundancy usage                             */

    internal void ResetForFrame()
    {
        lagIndex = 0;
        contourIndex = 0;
        PERIndex = 0;
        NLSFInterpCoef_Q2 = 0;
        Seed = 0;
        LTP_scaleIndex = 0;
        RateLevelIndex = 0;
        QuantOffsetType = 0;
        sigtype = 0;
        LBRR_usage = 0;
        Array.Clear(LTPIndex);
        Array.Clear(NLSFIndices);
        Array.Clear(GainsIndices);
        Array.Clear(pitchL);
    }
}

/**
 * Class for Packet Loss Concealment.
 *
 * @author Jing Dai
 * @author Dingxin Xu
 */
internal sealed class SKP_Silk_PLC_struct
 {
    internal int       pitchL_Q8;                      /* Pitch lag to use for voiced concealment                  */
    internal short[]   LTPCoef_Q14 = new short[ LTP_ORDER ];       /* LTP coeficients to use for voiced concealment            */
    internal short[]   prevLPC_Q12 = new short[ MAX_LPC_ORDER ];
    internal int         last_frame_lost;                /* Was previous frame lost                                  */
    internal int       rand_seed;                      /* Seed for unvoiced signal generation                      */
    internal short     randScale_Q14;                  /* Scaling of unvoiced random signal                        */
    internal int       conc_energy;
    internal int       conc_energy_shift;
    internal short     prevLTP_scale_Q14;
    internal int[]     prevGain_Q16 = new int[ NB_SUBFR ];
    internal int       fs_kHz;
}

 /**
  * Class for CNG.
  *
  * @author Jing Dai
  * @author Dingxin Xu
  */
internal sealed class SKP_Silk_CNG_struct
 {
     int[]   CNG_exc_buf_Q10 = new int[ MAX_FRAME_LENGTH ];
     int[]   CNG_smth_NLSF_Q15 = new int[ MAX_LPC_ORDER ];
     int[]   CNG_synth_state = new int[ MAX_LPC_ORDER ];
     int     CNG_smth_Gain_Q16;
     int     rand_seed;
     int     fs_kHz;
 }

 /**
  * Decoder state
  *
  * @author Jing Dai
  * @author Dingxin Xu
  */
internal sealed class SKP_Silk_decoder_state
 {
    internal SKP_Silk_range_coder_state  sRC = new  SKP_Silk_range_coder_state();                            /* Range coder state */
    internal int       prev_inv_gain_Q16;
    internal int[]     sLTP_Q16 = new int[ 2 * MAX_FRAME_LENGTH ];
    internal int[]     sLPC_Q14 = new int[ MAX_FRAME_LENGTH / NB_SUBFR + MAX_LPC_ORDER ];
    internal int[]     exc_Q10 = new int [ MAX_FRAME_LENGTH ];
    internal int[]     res_Q10 = new int [ MAX_FRAME_LENGTH ];
    internal short[]   outBuf = new short[ 2 * MAX_FRAME_LENGTH ];             /* Buffer for output signal                                             */
    internal int       lagPrev;                                    /* Previous Lag                                                         */
    internal int       LastGainIndex;                              /* Previous gain index                                                  */
    internal int       LastGainIndex_EnhLayer;                     /* Previous gain index                                                  */
    internal int       typeOffsetPrev;                             /* Previous signal type and quantization offset                         */
    internal int[]     HPState = new int[ DEC_HP_ORDER ];                    /* HP filter state                                                      */
    internal short[]   HP_A;                                        /* HP filter AR coefficients                                            */
    internal short[]   HP_B;                                        /* HP filter MA coefficients                                            */
    internal int       fs_kHz;                                     /* Sampling frequency input kHz                                            */
    internal int       prev_API_sampleRate;                        /* Previous API sample frequency (Hz)                                   */
    internal int         frame_length;                               /* Frame length (samples)                                               */
    internal int         subfr_length;                               /* Subframe length (samples)                                            */
    internal int         LPC_order;                                  /* LPC order                                                            */
    internal int[]       prevNLSF_Q15 = new int[ MAX_LPC_ORDER ];              /* Used to interpolate LSFs                                             */
    internal int         first_frame_after_reset;                    /* Flag for deactivating NLSF interp. and fluc. reduction after resets  */

    /* For buffering payload input case of more frames per packet */
    internal int         nBytesLeft;
    internal int         nFramesDecoded;
    internal int         nFramesInPacket;
    internal int         moreInternalDecoderFrames;
    internal int         FrameTermination;

    internal SKP_Silk_resampler_state_struct  resampler_state = new SKP_Silk_resampler_state_struct();

    internal SKP_Silk_NLSF_CB_struct[]  psNLSF_CB= new SKP_Silk_NLSF_CB_struct[ 2 ];      /* Pointers to voiced/unvoiced NLSF codebooks */

    /* Parameters used to investigate if inband FEC is used */
    internal int         vadFlag;
    internal int         no_FEC_counter;                             /* Counts number of frames wo inband FEC                                */
    internal int         inband_FEC_offset;                          /* 0: no FEC, 1: FEC with 1 packet offset, 2: FEC w 2 packets offset    */

    internal SKP_Silk_CNG_struct sCNG = new SKP_Silk_CNG_struct();

    /* Stuff used for PLC */
    internal SKP_Silk_PLC_struct sPLC = new SKP_Silk_PLC_struct();
    internal int         lossCnt;
    internal int         prev_sigtype;                               /* Previous sigtype                                                     */
}

 /**
  * Decoder control.
  *
  * @author Jing Dai
  * @author Dingxin Xu
  */
internal sealed class SKP_Silk_decoder_control
{
    /* prediction and coding parameters */
    internal int[]             pitchL = new int[ NB_SUBFR ];
    internal int[]             Gains_Q16 = new int[ NB_SUBFR ];
    internal int               Seed;
    /* holds interpolated and coefficients, 4-byte aligned */
    //TODO:
    internal int[]            dummy_int32PredCoef_Q12 = new int[2];
    internal short[][]        PredCoef_Q12 = Structs.CreateJagged<short>(2, MAX_LPC_ORDER);

    internal short[]           LTPCoef_Q14 = new short[ LTP_ORDER * NB_SUBFR ];
    internal int               LTP_scale_Q14;

    /* quantization indices */
    internal int             PERIndex;
    internal int             RateLevelIndex;
    internal int             QuantOffsetType;
    internal int             sigtype;
    internal int             NLSFInterpCoef_Q2;
}
