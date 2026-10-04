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
 * Warped noise shaping control fields adapted from SILK SDK 1.0.9.
 * Copyright (c) 2006-2012, Skype Limited. See THIRD-PARTY-NOTICES.
 */

/**
 *
 * @author Jing Dai
 * @author Dingxin Xu
 */
internal static class StructsFLP
{
}

/**
 * Noise shaping analysis state.
 *
 * @author Jing Dai
 * @author Dingxin Xu
 */
internal sealed class SKP_Silk_shape_state_FLP
{
    internal int     LastGainIndex;
    internal float   HarmBoost_smth;
    internal float   HarmShapeGain_smth;
    internal float   Tilt_smth;

    /**
     * set all fields of the instance to zero
     */
    public void memZero()
    {
        this.LastGainIndex = 0;
        this.HarmBoost_smth = 0;
        this.HarmShapeGain_smth = 0;
        this.Tilt_smth = 0;
    }
}

/**
 * Prefilter state
 *
 * @author Jing Dai
 * @author Dingxin Xu
 */
internal sealed class SKP_Silk_prefilter_state_FLP
{
    internal float[]   sLTP_shp1 = new float[ LTP_BUF_LENGTH ];
    internal float[]   sLTP_shp2 = new float[ LTP_BUF_LENGTH ];
    internal float[]   sAR_shp1 = new float[ SHAPE_LPC_ORDER_MAX + 1 ];
    internal float[]   sAR_shp2 = new float[ SHAPE_LPC_ORDER_MAX ];
    internal int     sLTP_shp_buf_idx1;
    internal int     sLTP_shp_buf_idx2;
    internal int     sAR_shp_buf_idx2;
    internal float   sLF_AR_shp1;
    internal float   sLF_MA_shp1;
    internal float   sLF_AR_shp2;
    internal float   sLF_MA_shp2;
    internal float   sHarmHP;
    internal int   rand_seed;
    internal int     lagPrev;

    /**
     * set all fields of the instance to zero
     */
    public void memZero()
    {
        Array.Fill(this.sAR_shp1, 0);
        Array.Fill(this.sAR_shp2, 0);
        Array.Fill(this.sLTP_shp1, 0);
        Array.Fill(this.sLTP_shp2, 0);

        this.sLTP_shp_buf_idx1 = 0;
        this.sLTP_shp_buf_idx2 = 0;
        this.sAR_shp_buf_idx2 = 0;
        this.sLF_AR_shp1 = 0;
        this.sLF_AR_shp2 = 0;
        this.sLF_MA_shp1 = 0;
        this.sLF_MA_shp2 = 0;
        this.sHarmHP = 0;
        this.rand_seed = 0;
        this.lagPrev = 0;
    }
}

/**
 * Prediction analysis state
 *
 * @author Jing Dai
 * @author Dingxin Xu
 */
internal sealed class SKP_Silk_predict_state_FLP
{
    internal int     pitch_LPC_win_length;
    internal int     min_pitch_lag;                      /* Lowest possible pitch lag (samples)  */
    internal int     max_pitch_lag;                      /* Highest possible pitch lag (samples) */
    internal float[]   prev_NLSFq = new float[ MAX_LPC_ORDER ];        /* Previously quantized NLSF vector     */

    /**
     * set all fields of the instance to zero
     */
    public void memZero()
    {
        this.pitch_LPC_win_length = 0;
        this.max_pitch_lag = 0;
        this.min_pitch_lag = 0;
        Array.Fill(this.prev_NLSFq, 0.0f);
    }
}

/*******************************************/
/* Structure containing NLSF MSVQ codebook */
/*******************************************/
/* structure for one stage of MSVQ */
internal sealed class SKP_Silk_NLSF_CBS_FLP
{
    public SKP_Silk_NLSF_CBS_FLP()
    {
    }

    public SKP_Silk_NLSF_CBS_FLP(int nVectors, float[] CB, float[] Rates)
    {
        this.nVectors = nVectors;
        this.CB = CB;
        this.Rates = Rates;
    }

    public SKP_Silk_NLSF_CBS_FLP(int nVectors, float[] CB, int CB_offset, float[] Rates, int Rates_offset)
    {
        this.nVectors = nVectors;
        this.CB = new float[CB.Length - CB_offset];
        Array.Copy(CB, CB_offset, this.CB, 0, this.CB.Length);
        this.Rates = new float[Rates.Length - Rates_offset];
        Array.Copy(Rates, Rates_offset, this.Rates, 0, this.Rates.Length);
    }

    internal int         nVectors;
    internal float[]     CB;
    internal float[]     Rates;
}
internal sealed class SKP_Silk_NLSF_CB_FLP
{
    public SKP_Silk_NLSF_CB_FLP()
    {
    }

    public SKP_Silk_NLSF_CB_FLP(int nStages, SKP_Silk_NLSF_CBS_FLP[] CBStages,
            float[] NDeltaMin, int[] CDF, int[][] StartPtr, int[] MiddleIx)
    {
        this.nStages = nStages;
        this.CBStages = CBStages;
        this.NDeltaMin = NDeltaMin;
        this.CDF = CDF;
        this.StartPtr = StartPtr;
        this.MiddleIx = MiddleIx;
    }
//const SKP_int32                         nStages;
    internal int                         nStages;

    /* fields for (de)quantizing */
    internal SKP_Silk_NLSF_CBS_FLP[] CBStages;
    internal float[]                         NDeltaMin;

    /* fields for arithmetic (de)coding */
//    const SKP_uint16                        *CDF;
    internal int[] CDF;
//    const SKP_uint16 * const                *StartPtr;
    internal int[][] StartPtr;
//    const SKP_int                           *MiddleIx;
    internal int[] MiddleIx;
}

/************************************/
/* Noise shaping quantization state */
/************************************/

/**
 * Encoder state FLP.
 *
 * @author Jing Dai
 * @author Dingxin Xu
 */
internal sealed class SKP_Silk_encoder_state_FLP
{
//    SKP_Silk_encoder_state              sCmn;                       /* Common struct, shared with fixed-point code */
    internal SKP_Silk_encoder_state              sCmn = new SKP_Silk_encoder_state(); /* Common struct, shared with fixed-point code */


    internal float                           variable_HP_smth1;          /* State of first smoother */
    internal float                           variable_HP_smth2;          /* State of second smoother */

    internal SKP_Silk_shape_state_FLP            sShape = new SKP_Silk_shape_state_FLP();                     /* Noise shaping state */
    internal SKP_Silk_prefilter_state_FLP        sPrefilt = new SKP_Silk_prefilter_state_FLP();                   /* Prefilter State */
    internal SKP_Silk_predict_state_FLP          sPred = new SKP_Silk_predict_state_FLP();                      /* Prediction State */
    internal SKP_Silk_nsq_state                  sNSQ = new SKP_Silk_nsq_state();                       /* Noise Shape Quantizer State */
    internal SKP_Silk_nsq_state                  sNSQ_LBRR = new SKP_Silk_nsq_state();                  /* Noise Shape Quantizer State ( for low bitrate redundancy )*/

    /* Function pointer to noise shaping quantizer (will be set to SKP_Silk_NSQ or SKP_Silk_NSQ_del_dec) */
//    void    (* NoiseShapingQuantizer)( SKP_Silk_encoder_state *, SKP_Silk_encoder_control *, SKP_Silk_nsq_state *, const SKP_int16 *,
//                                       SKP_int8 *, const SKP_int, const SKP_int16 *, const SKP_int16 *, const SKP_int16 *, const SKP_int *,
//                                        const SKP_int *, const SKP_int32 *, const SKP_int32 *, SKP_int, const SKP_int
//    );
    internal NoiseShapingQuantizerFP noiseShapingQuantizerCB;
    internal void    NoiseShapingQuantizer( SKP_Silk_encoder_state psEnc, SKP_Silk_encoder_control psEncCtrl, SKP_Silk_nsq_state NSQ, short[]x ,
        byte[]q , int arg6, short[] arg7, short[]arg8, short[]arg9, int[]arg10,
         int []arg11, int[]arg12, int[]arg13, int arg14 , int arg15
    )
    {
        noiseShapingQuantizerCB.NoiseShapingQuantizer(psEnc, psEncCtrl, NSQ, x, q, arg6, arg7, arg8, arg9, arg10, arg11, arg12, arg13, arg14, arg15);
    }


    /* Buffer for find pitch and noise shape analysis */
    internal float[]                         x_buf = new float[ 2 * MAX_FRAME_LENGTH + LA_SHAPE_MAX ];/* Buffer for find pitch and noise shape analysis */
    internal byte[]                          nsqQScratch = new byte[MAX_FRAME_LENGTH];
    internal short[]                         nsqInput = new short[MAX_FRAME_LENGTH];
    internal int[]                           nsqGainsQ16 = new int[NB_SUBFR];
    internal short[][]                       nsqPredCoefQ12 = Structs.CreateJagged<short>(2, MAX_LPC_ORDER);
    internal short[]                         nsqPredCoefQ12Flat = new short[2 * MAX_LPC_ORDER];
    internal short[]                         nsqLtpCoefQ14 = new short[LTP_ORDER * NB_SUBFR];
    internal short[]                         nsqAr2Q13 = new short[NB_SUBFR * SHAPE_LPC_ORDER_MAX];
    internal int[]                           nsqLfShapeQ14 = new int[NB_SUBFR];
    internal int[]                           nsqTiltQ14 = new int[NB_SUBFR];
    internal int[]                           nsqHarmShapeGainQ14 = new int[NB_SUBFR];
    internal float[]                         noiseShapeWindow = new float[SHAPE_LPC_WIN_MAX];
    internal float[]                         noiseShapeAutoCorrelation = new float[SHAPE_LPC_ORDER_MAX + 1];
    internal double[]                        warpedState = new double[SHAPE_LPC_ORDER_MAX + 1];
    internal double[]                        warpedCorrelations = new double[SHAPE_LPC_ORDER_MAX + 1];
    internal float[]                         lpcInvPredScratch0 = new float[MAX_LPC_ORDER];
    internal float[]                         lpcInvPredScratch1 = new float[MAX_LPC_ORDER];
    internal PitchAnalysisCoreFLP.Workspace  pitchAnalysisWorkspace = new PitchAnalysisCoreFLP.Workspace();
    internal EncoderWorkspace                workspace = new EncoderWorkspace();
// djinn: add a parameter: offset
    internal int x_buf_offset;
    internal float                           LTPCorr;                    /* Normalized correlation from pitch lag estimator */
    internal float                           mu_LTP;                     /* Rate-distortion tradeoff input LTP quantization */
    internal float                           SNR_dB;                     /* Quality setting */
    internal float                           avgGain;                    /* average gain during active speech */
    internal float                           BufferedInChannel_ms;       /* Simulated number of ms buffer input channel because of exceeded TargetRate_bps */
    internal float                           speech_activity;            /* Speech activity */
    internal float                           pitchEstimationThreshold;   /* Threshold for pitch estimator */

    /* Parameters for LTP scaling control */
    internal float                           prevLTPredCodGain;
    internal float                           HPLTPredCodGain;

    internal float                           inBandFEC_SNR_comp;         /* Compensation to SNR_DB when using inband FEC Voiced */

    internal SKP_Silk_NLSF_CB_FLP[]  psNLSF_CB_FLP = new SKP_Silk_NLSF_CB_FLP[ 2 ];        /* Pointers to voiced/unvoiced NLSF codebooks */
}

/**
 * Encoder control FLP
 *
 * @author Jing Dai
 * @author Dingxin Xu
 */
internal sealed class SKP_Silk_encoder_control_FLP
{
    internal SKP_Silk_encoder_control    sCmn = new SKP_Silk_encoder_control();                               /* Common struct, shared with fixed-point code */

    /* Prediction and coding parameters */
    internal float[]                   Gains = new float[NB_SUBFR];
    internal float[][]                   PredCoef = Structs.CreateJagged<float>( 2 ,  MAX_LPC_ORDER );     /* holds interpolated and coefficients */
    internal float[]                   LTPCoef = new float[LTP_ORDER * NB_SUBFR];
    internal float                   LTP_scale;

    /* Prediction and coding parameters */
    internal int[]                   Gains_Q16 = new int[ NB_SUBFR ];
//TODO:    SKP_array_of_int16_4_byte_aligned( PredCoef_Q12[ 2 ], MAX_LPC_ORDER );
    internal int[] dummy_int32PredCoef_Q12 = new int[ 2 ];
    internal short[][] PredCoef_Q12 = Structs.CreateJagged<short>( 2 , MAX_LPC_ORDER);

    internal short[]                   LTPCoef_Q14 = new short[ LTP_ORDER * NB_SUBFR ];
    internal int                     LTP_scale_Q14;

    /* Noise shaping parameters */
    /* Testing */
//TODO    SKP_array_of_int16_4_byte_aligned( AR2_Q13, NB_SUBFR * SHAPE_LPC_ORDER_MAX );
    internal int dummy_int32AR2_Q13;
    internal short[] AR2_Q13 = new short[NB_SUBFR * SHAPE_LPC_ORDER_MAX];

    internal int[]                     LF_shp_Q14 = new int[        NB_SUBFR ];      /* Packs two int16 coefficients per int32 value             */
    internal int[]                     Tilt_Q14 = new int[          NB_SUBFR ];
    internal int[]                     HarmShapeGain_Q14 = new int[ NB_SUBFR ];
    internal int                     Lambda_Q10;

    /* Noise shaping parameters */
    internal float[]                   AR1 = new float[ NB_SUBFR * SHAPE_LPC_ORDER_MAX ];
    internal float[]                   AR2 = new float[ NB_SUBFR * SHAPE_LPC_ORDER_MAX ];
    internal float[]                   LF_MA_shp = new float[     NB_SUBFR ];
    internal float[]                   LF_AR_shp = new float[     NB_SUBFR ];
    internal float[]                   GainsPre = new float[      NB_SUBFR ];
    internal float[]                   HarmBoost = new float[     NB_SUBFR ];
    internal float[]                   Tilt = new float[          NB_SUBFR ];
    internal float[]                   HarmShapeGain = new float[ NB_SUBFR ];
    internal float                   Lambda;
    internal float                   input_quality;
    internal float                   coding_quality;
    internal float                   pitch_freq_low_Hz;
    internal float                   current_SNR_dB;

    /* Measures */
    internal float                   sparseness;
    internal float                   predGain;
    internal float                   LTPredCodGain;
    internal float[]                   input_quality_bands = new float[ VAD_N_BANDS ];
    internal float                   input_tilt;
    internal float[]                   ResNrg = new float[ NB_SUBFR ];                 /* Residual energy per subframe */

    internal void ResetForFrame()
    {
        sCmn.ResetForFrame();
        LTP_scale = 0;
        LTP_scale_Q14 = 0;
        dummy_int32AR2_Q13 = 0;
        Lambda_Q10 = 0;
        Lambda = 0;
        input_quality = 0;
        coding_quality = 0;
        pitch_freq_low_Hz = 0;
        current_SNR_dB = 0;
        sparseness = 0;
        predGain = 0;
        LTPredCodGain = 0;
        input_tilt = 0;
        Array.Clear(Gains);
        Array.Clear(PredCoef[0]);
        Array.Clear(PredCoef[1]);
        Array.Clear(LTPCoef);
        Array.Clear(Gains_Q16);
        Array.Clear(dummy_int32PredCoef_Q12);
        Array.Clear(PredCoef_Q12[0]);
        Array.Clear(PredCoef_Q12[1]);
        Array.Clear(LTPCoef_Q14);
        Array.Clear(AR2_Q13);
        Array.Clear(LF_shp_Q14);
        Array.Clear(Tilt_Q14);
        Array.Clear(HarmShapeGain_Q14);
        Array.Clear(AR1);
        Array.Clear(AR2);
        Array.Clear(LF_MA_shp);
        Array.Clear(LF_AR_shp);
        Array.Clear(GainsPre);
        Array.Clear(HarmBoost);
        Array.Clear(Tilt);
        Array.Clear(HarmShapeGain);
        Array.Clear(input_quality_bands);
        Array.Clear(ResNrg);
    }
}

internal sealed class EncoderWorkspace
{
    internal readonly SKP_Silk_encoder_control_FLP Control = new();
    internal readonly int[] ScalarInt = new int[1];
    internal readonly float[] ScalarFloat = new float[1];
    internal readonly short[] ScalarShort = new short[1];
    internal readonly short[] InputHighPass = new short[MAX_FRAME_LENGTH];
    internal readonly short[] InputLowPass = new short[MAX_FRAME_LENGTH];
    internal readonly float[] Prefiltered = new float[MAX_FRAME_LENGTH];
    internal readonly float[] PitchResidual = new float[2 * MAX_FRAME_LENGTH + LA_PITCH_MAX];
    internal readonly byte[] LbrrPayload = new byte[MAX_ARITHM_BYTES];
    internal readonly int[] LbrrGainsQ16 = new int[NB_SUBFR];
    internal readonly int[] LbrrGainIndices = new int[NB_SUBFR];
    internal readonly float[] LbrrGains = new float[NB_SUBFR];

    internal readonly float[] PitchAutoCorrelation = new float[FIND_PITCH_LPC_ORDER_MAX + 1];
    internal readonly float[] PitchA = new float[FIND_PITCH_LPC_ORDER_MAX];
    internal readonly float[] PitchReflection = new float[FIND_PITCH_LPC_ORDER_MAX];
    internal readonly float[] PitchWindow = new float[FIND_PITCH_LPC_WIN_MAX];
    internal readonly float[] PitchK2ATemp = new float[SigProcFIX.SKP_Silk_MAX_ORDER_LPC];

    internal readonly float[] Wltp = new float[NB_SUBFR * LTP_ORDER * LTP_ORDER];
    internal readonly float[] InverseGains = new float[NB_SUBFR];
    internal readonly float[] Weights = new float[NB_SUBFR];
    internal readonly float[] Nlsf = new float[MAX_LPC_ORDER];
    internal readonly float[] LpcInput = new float[NB_SUBFR * MAX_LPC_ORDER + MAX_FRAME_LENGTH];
    internal readonly float[] LtpD = new float[NB_SUBFR];
    internal readonly float[] LtpDeltaB = new float[LTP_ORDER];
    internal readonly float[] LtpW = new float[NB_SUBFR];
    internal readonly float[] LtpNrg = new float[NB_SUBFR];
    internal readonly float[] LtpRr = new float[LTP_ORDER];
    internal readonly float[] LtpRrEnergy = new float[NB_SUBFR];
    internal readonly int[] LtpTempIndices = new int[NB_SUBFR];
    internal readonly float[] LtpRateDistortion = new float[1];

    internal readonly float[] LpcA = new float[MAX_LPC_ORDER];
    internal readonly float[] LpcATemp = new float[MAX_LPC_ORDER];
    internal readonly float[] LpcNlsf0 = new float[MAX_LPC_ORDER];
    internal readonly float[] LpcResidual = new float[(MAX_FRAME_LENGTH + NB_SUBFR * MAX_LPC_ORDER) / 2];
    internal readonly double[] BurgFirstRow = new double[SigProcFIX.SKP_Silk_MAX_ORDER_LPC];
    internal readonly double[] BurgLastRow = new double[SigProcFIX.SKP_Silk_MAX_ORDER_LPC];
    internal readonly double[] BurgForward = new double[SigProcFIX.SKP_Silk_MAX_ORDER_LPC + 1];
    internal readonly double[] BurgBackward = new double[SigProcFIX.SKP_Silk_MAX_ORDER_LPC + 1];
    internal readonly double[] BurgCoefficients = new double[SigProcFIX.SKP_Silk_MAX_ORDER_LPC];
    internal readonly float[] ResidualEnergy = new float[(MAX_FRAME_LENGTH + NB_SUBFR * MAX_LPC_ORDER) / 2];

    internal readonly float[] NlsfWeights = new float[MAX_LPC_ORDER];
    internal readonly float[] NlsfInterpolated = new float[MAX_LPC_ORDER];
    internal readonly float[] NlsfInterpolatedWeights = new float[MAX_LPC_ORDER];
    internal readonly float[] NlsfInput = new float[MAX_LPC_ORDER];
    internal readonly float[] NlsfRateDist = new float[NLSF_MSVQ_TREE_SEARCH_MAX_VECTORS_EVALUATED()];
    internal readonly float[] NlsfRate = new float[MAX_NLSF_MSVQ_SURVIVORS];
    internal readonly float[] NlsfRateNew = new float[MAX_NLSF_MSVQ_SURVIVORS];
    internal readonly int[] NlsfTempIndices = new int[MAX_NLSF_MSVQ_SURVIVORS];
    internal readonly int[] NlsfPath = new int[MAX_NLSF_MSVQ_SURVIVORS * NLSF_MSVQ_MAX_CB_STAGES];
    internal readonly int[] NlsfPathNew = new int[MAX_NLSF_MSVQ_SURVIVORS * NLSF_MSVQ_MAX_CB_STAGES];
    internal readonly float[] NlsfResidual = new float[MAX_NLSF_MSVQ_SURVIVORS * MAX_LPC_ORDER];
    internal readonly float[] NlsfResidualNew = new float[MAX_NLSF_MSVQ_SURVIVORS * MAX_LPC_ORDER];
    internal readonly float[] NlsfWeightCopy = new float[MAX_LPC_ORDER];
    internal readonly int[] NlsfStabilized = new int[MAX_LPC_ORDER];
    internal readonly int[] NlsfDeltaMin = new int[MAX_LPC_ORDER + 1];

    internal readonly int[] FixedNlsf = new int[MAX_LPC_ORDER];
    internal readonly int[] FixedA = new int[MAX_LPC_ORDER];
    internal readonly short[] FixedAShort = new short[MAX_LPC_ORDER];
    internal readonly int[] FixedCosLsf = new int[SigProcFIX.SKP_Silk_MAX_ORDER_LPC];
    internal readonly int[] FixedPolynomialP = new int[SigProcFIX.SKP_Silk_MAX_ORDER_LPC / 2 + 1];
    internal readonly int[] FixedPolynomialQ = new int[SigProcFIX.SKP_Silk_MAX_ORDER_LPC / 2 + 1];
    internal readonly int[] FixedAInt32 = new int[SigProcFIX.SKP_Silk_MAX_ORDER_LPC];
    internal readonly int[] Interpolate0 = new int[MAX_LPC_ORDER];
    internal readonly int[] Interpolate1 = new int[MAX_LPC_ORDER];
    internal readonly int[] InterpolateResult = new int[MAX_LPC_ORDER];
    internal readonly int[] VadSpeechActivity = new int[1];
    internal readonly int[] VadSnr = new int[1];
    internal readonly int[] VadTilt = new int[1];
    internal readonly int[] VadQuality = new int[VAD_N_BANDS];
    internal readonly int[] VadScratch = new int[3 * MAX_FRAME_LENGTH / 2];
    internal readonly short[][] VadBands = Structs.CreateJagged<short>(VAD_N_BANDS, MAX_FRAME_LENGTH / 2);
    internal readonly int[] VadEnergy = new int[VAD_N_BANDS];
    internal readonly int[] VadNoiseRatio = new int[VAD_N_BANDS];
    internal readonly float[][] SchurCorrelation = Structs.CreateJagged<float>(SigProcFIX.SKP_Silk_MAX_ORDER_LPC + 1, 2);
    internal readonly float[] SolveL = new float[MAX_MATRIX_SIZE * MAX_MATRIX_SIZE];
    internal readonly float[] SolveT = new float[MAX_MATRIX_SIZE];
    internal readonly float[] SolveDInverse = new float[MAX_MATRIX_SIZE];
    internal readonly float[] SolveV = new float[MAX_MATRIX_SIZE];
    internal readonly float[] SolveD = new float[MAX_MATRIX_SIZE];
    internal readonly int[] A2NlsfP = new int[SigProcFIX.SKP_Silk_MAX_ORDER_LPC / 2 + 1];
    internal readonly int[] A2NlsfQ = new int[SigProcFIX.SKP_Silk_MAX_ORDER_LPC / 2 + 1];
    internal readonly int[] NlsfInverseGain = new int[1];
    internal readonly int[] HighPassB = new int[3];
    internal readonly int[] HighPassA = new int[2];
    internal readonly float[] PrefilterB = new float[2];
    internal readonly float[] PrefilterHarmShape = new float[3];
    internal readonly float[] PrefilterStateResidual = new float[MAX_FRAME_LENGTH / NB_SUBFR];
    internal readonly int[] ProcessGainsQ16 = new int[NB_SUBFR];
}
internal interface NoiseShapingQuantizerFP
{
    /* Function pointer to noise shaping quantizer (will be set to SKP_Silk_NSQ or SKP_Silk_NSQ_del_dec) */
  void    NoiseShapingQuantizer( SKP_Silk_encoder_state psEnc, SKP_Silk_encoder_control psEncCtrl, SKP_Silk_nsq_state NSQ, short[]x ,
                                     byte[]q , int arg6, short[] arg7, short[]arg8, short[]arg9, int[]arg10,
                                      int []arg11, int[]arg12, int[]arg13, int arg14 , int arg15
  );

    /* Function pointer to noise shaping quantizer (will be set to SKP_Silk_NSQ or SKP_Silk_NSQ_del_dec) */
//  void    (* NoiseShapingQuantizer)( SKP_Silk_encoder_state *, SKP_Silk_encoder_control *, SKP_Silk_nsq_state *, const SKP_int16 *,
//                                     SKP_int8 *, const SKP_int, const SKP_int16 *, const SKP_int16 *, const SKP_int16 *, const SKP_int *,
//                                      const SKP_int *, const SKP_int32 *, const SKP_int32 *, SKP_int, const SKP_int
//  );
}
