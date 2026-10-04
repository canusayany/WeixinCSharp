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

namespace SilkCodec.NET.Managed;

/*
 * Warped noise shaping constants adapted from SILK SDK 1.0.9.
 * Copyright (c) 2006-2012, Skype Limited. See THIRD-PARTY-NOTICES.
 */

/**
 *
 * @author Jing Dai
 * @author Dingxin Xu
 */
internal static class DefineFLP
{
    /*******************/
    /* Pitch estimator */
    /*******************/

    /* Level of noise floor for whitening filter LPC analysis input pitch analysis */
    internal const float FIND_PITCH_WHITE_NOISE_FRACTION =                1e-3f;

    /* Bandwidth expansion for whitening filter input pitch analysis */
    internal const float FIND_PITCH_BANDWITH_EXPANSION =                  0.99f;

    /* Threshold used by pitch estimator for early escape */
    internal const float FIND_PITCH_CORRELATION_THRESHOLD_HC_MODE =       0.7f;
    internal const float FIND_PITCH_CORRELATION_THRESHOLD_MC_MODE =       0.75f;
    internal const float FIND_PITCH_CORRELATION_THRESHOLD_LC_MODE =       0.8f;

    /***********************/
    /* Long-Term predictor */
    /***********************/

    /* Regualarization factor for correlation matrix. Equivalent to adding noise at -50 dB */
    internal const float FIND_LTP_COND_FAC =                              1e-5f;
    internal const float FIND_LPC_COND_FAC =                              6e-5f;

    /* Find prediction coefficients defines */
    internal const float LTP_DAMPING =                                    0.001f;
    internal const float LTP_SMOOTHING =                                  0.1f;

    /* LTP quantization settings */
    internal const float MU_LTP_QUANT_NB =                                0.03f;
    internal const float MU_LTP_QUANT_MB =                                0.025f;
    internal const float MU_LTP_QUANT_WB =                                0.02f;
    internal const float MU_LTP_QUANT_SWB =                               0.016f;

    /***********************/
    /* High pass filtering */
    /***********************/

    /* Smoothing parameters for low end of pitch frequency range estimation */
    internal const float VARIABLE_HP_SMTH_COEF1 =                         0.1f;
    internal const float VARIABLE_HP_SMTH_COEF2 =                         0.015f;

    /* Min and max values for low end of pitch frequency range estimation */
    internal const float VARIABLE_HP_MIN_FREQ =                           80.0f;
    internal const float VARIABLE_HP_MAX_FREQ =                           150.0f;

    /* Max absolute difference between log2 of pitch frequency and smoother state, to enter the smoother */
    internal const float VARIABLE_HP_MAX_DELTA_FREQ =                     0.4f;

    /***********/
    /* Various */
    /***********/

    /* Required speech activity for counting frame as active */
    internal const float WB_DETECT_ACTIVE_SPEECH_LEVEL_THRES =            0.7f;

    internal const float SPEECH_ACTIVITY_DTX_THRES =                      0.1f;

    /* Speech Activity LBRR enable threshold (needs tuning) */
    internal static float LBRR_SPEECH_ACTIVITY_THRES =                           0.5f;

    internal const float Q14_CONVERSION_FAC =                             6.1035e-005f; // 1 / 2^14

    internal const float WARPING_MULTIPLIER =                             0.015f;

    internal const float LAMBDA_OFFSET =                                  1.2f;
    internal const float LAMBDA_SPEECH_ACT =                              -0.3f;
    internal const float LAMBDA_DELAYED_DECISIONS =                       -0.05f;
    internal const float LAMBDA_INPUT_QUALITY =                           -0.2f;
    internal const float LAMBDA_CODING_QUALITY =                          -0.1f;
    internal const float LAMBDA_QUANT_OFFSET =                            1.5f;
}
