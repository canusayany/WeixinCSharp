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
using static SilkCodec.NET.Managed.Define;
using static SilkCodec.NET.Managed.Macros;
using static SilkCodec.NET.Managed.Typedef;

namespace SilkCodec.NET.Managed;

/*
 * Noise shaping constants aligned with SILK SDK 1.0.9.
 * Copyright (c) 2006-2012, Skype Limited. See THIRD-PARTY-NOTICES.
 */
/**
 * Perceptual parameters.
 *
 * @author Jing Dai
 * @author Dingxin Xu
 */
//TODO: float or double???
internal static class PerceptualParametersFLP
{
    /* reduction in coding SNR during low speech activity */
    internal const float BG_SNR_DECR_dB =                             4.0f;

    /* factor for reducing quantization noise during voiced speech */
    internal const float HARM_SNR_INCR_dB =                           2.0f;

    /* factor for reducing quantization noise for unvoiced sparse signals */
    internal const float SPARSE_SNR_INCR_dB =                         2.0f;

    /* threshold for sparseness measure above which to use lower quantization offset during unvoiced */
    internal const float SPARSENESS_THRESHOLD_QNT_OFFSET =            0.75f;


    /* noise shaping filter chirp factor */
    internal const float BANDWIDTH_EXPANSION =                        0.95f;

    /* difference between chirp factors for analysis and synthesis noise shaping filters at low bitrates */
    internal const float LOW_RATE_BANDWIDTH_EXPANSION_DELTA =         0.01f;

    /* factor to reduce all bandwdith expansion coefficients for super wideband, relative to wideband */
    internal const float SWB_BANDWIDTH_EXPANSION_REDUCTION =          1.0f;

    /* gain reduction for fricatives */
    internal const float DE_ESSER_COEF_SWB_dB =                       2.0f;
    internal const float DE_ESSER_COEF_WB_dB =                        1.0f;


    /* extra harmonic boosting (signal shaping) at low bitrates */
    internal const float LOW_RATE_HARMONIC_BOOST =                    0.1f;

    /* extra harmonic boosting (signal shaping) for noisy input signals */
    internal const float LOW_INPUT_QUALITY_HARMONIC_BOOST =           0.1f;

    /* harmonic noise shaping */
    internal const float HARMONIC_SHAPING =                           0.3f;

    /* extra harmonic noise shaping for high bitrates or noisy input */
    internal const float HIGH_RATE_OR_LOW_QUALITY_HARMONIC_SHAPING =  0.2f;


    /* parameter for shaping noise towards higher frequencies */
    internal const float HP_NOISE_COEF =                              0.3f;

    /* parameter for shaping noise extra towards higher frequencies during voiced speech */
    internal const float HARM_HP_NOISE_COEF =                         0.35f;

    /* parameter for applying a high-pass tilt to the input signal */
    internal const float INPUT_TILT =                                 0.05f;

    /* parameter for extra high-pass tilt to the input signal at high rates */
    internal const float HIGH_RATE_INPUT_TILT =                       0.1f;

    /* parameter for reducing noise at the very low frequencies */
    internal const float LOW_FREQ_SHAPING =                           3.0f;

    /* less reduction of noise at the very low frequencies for signals with low SNR at low frequencies */
    internal const float LOW_QUALITY_LOW_FREQ_SHAPING_DECR =          0.5f;

    /* fraction added to first autocorrelation value */
    internal const float SHAPE_WHITE_NOISE_FRACTION =                 1e-5f;

    /* fraction of first autocorrelation value added to residual energy value; limits prediction gain */
    internal const float SHAPE_MIN_ENERGY_RATIO =                     1.526e-5f;       // 1.526e-5 = 1/65536

    /* noise floor to put a low limit on the quantization step size */
    internal const float NOISE_FLOOR_dB =                             4.0f;

    /* noise floor relative to active speech gain level */
    internal const float RELATIVE_MIN_GAIN_dB =                       -50.0f;

    /* subframe smoothing coefficient for determining active speech gain level (lower -> more smoothing) */
    internal const float GAIN_SMOOTHING_COEF =                        1e-3f;

    /* subframe smoothing coefficient for HarmBoost, HarmShapeGain, Tilt (lower -> more smoothing) */
    internal const float SUBFR_SMTH_COEF =                            0.4f;

    /* Amount of noise added in prefilter, to simulate quantization noise */
    internal const float NOISE_GAIN_VL =                              0.12f;
    internal const float NOISE_GAIN_VH =                              0.12f;
    internal const float NOISE_GAIN_UVL =                             0.1f;
    internal const float NOISE_GAIN_UVH =                             0.15f;
}
