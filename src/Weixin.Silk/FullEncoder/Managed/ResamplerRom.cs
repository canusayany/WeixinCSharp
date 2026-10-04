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

/**
 * Filter coefficients for IIR/FIR polyphase resampling.
 * Total size: 550 Words (1.1 kB).
 *
 * @author Jing Dai
 * @author Dingxin Xu
 */
internal static class ResamplerRom
{
    internal const int RESAMPLER_DOWN_ORDER_FIR =               12;
    internal const int RESAMPLER_ORDER_FIR_144 =                6;

    /* Tables for 2x downsampler. Values above 32767 intentionally wrap to a negative value. */
    internal const short SKP_Silk_resampler_down2_0 = 9872;
    internal const short SKP_Silk_resampler_down2_1 = unchecked((short)39809);

    /* Tables for 2x upsampler, low quality. Values above 32767 intentionally wrap to a negative value. */
    internal const short SKP_Silk_resampler_up2_lq_0 = 8102;
    internal const short SKP_Silk_resampler_up2_lq_1 = unchecked((short)36783);

    /* Tables for 2x upsampler, high quality. Values above 32767 intentionally wrap to a negative value. */
    internal static short[] SKP_Silk_resampler_up2_hq_0 = {  4280, unchecked((short)33727) };
    internal static short[] SKP_Silk_resampler_up2_hq_1 = { 16295, unchecked((short)54015) };
    /* Matlab code for the notch filter coefficients: */
    /* B = [1, 0.12, 1];  A = [1, 0.055, 0.8]; G = 0.87; freqz(G * B, A, 2^14, 16e3); axis([0, 8000, -10, 1]);  */
    /* fprintf('\t%6d, %6d, %6d, %6d\n', round(B(2)*2^16), round(-A(2)*2^16), round((1-A(3))*2^16), round(G*2^15)) */
    internal static short[] SKP_Silk_resampler_up2_hq_notch = { 7864,  -3604,  13107,  28508 };

    /* Tables with IIR and FIR coefficients for fractional downsamplers (70 Words) */
    internal static short[] SKP_Silk_Resampler_3_4_COEFS =
    {
        -18249, -12532,
           -97,    284,   -495,    309,  10268,  20317,
           -94,    156,    -48,   -720,   5984,  18278,
           -45,     -4,    237,   -847,   2540,  14662,
    };

    internal static short[] SKP_Silk_Resampler_2_3_COEFS =
    {
        -11891, -12486,
            20,    211,   -657,    688,   8423,  15911,
           -44,    197,   -152,   -653,   3855,  13015,
    };

    internal static short[] SKP_Silk_Resampler_1_2_COEFS =
    {
          2415, -13101,
           158,   -295,   -400,   1265,   4832,   7968,
    };

    internal static short[] SKP_Silk_Resampler_3_8_COEFS =
    {
         13270, -13738,
          -294,   -123,    747,   2043,   3339,   3995,
          -151,   -311,    414,   1583,   2947,   3877,
           -33,   -389,    143,   1141,   2503,   3653,
    };

    internal static short[] SKP_Silk_Resampler_1_3_COEFS =
    {
         16643, -14000,
          -331,     19,    581,   1421,   2290,   2845,
    };

    internal static short[] SKP_Silk_Resampler_2_3_COEFS_LQ =
    {
         -2797,  -6507,
          4697,  10739,
          1567,   8276,
    };

    internal static short[] SKP_Silk_Resampler_1_3_COEFS_LQ =
    {
         16777,  -9792,
           890,   1614,   2148,
    };

    /* Tables with coefficients for 4th order ARMA filter (35 Words), input a packed format:       */
    /*    { B1_Q14[1], B2_Q14[1], -A1_Q14[1], -A1_Q14[2], -A2_Q14[1], -A2_Q14[2], gain_Q16 }    */
    /* where it is assumed that B*_Q14[0], B*_Q14[2], A*_Q14[0] are all 16384                   */
    internal static short[] SKP_Silk_Resampler_320_441_ARMA4_COEFS =
    {
         31454,  24746,  -9706,  -3386, -17911, -13243,  24797
    };

    internal static short[] SKP_Silk_Resampler_240_441_ARMA4_COEFS =
    {
         28721,  11254,   3189,  -2546,  -1495, -12618,  11562
    };

    internal static short[] SKP_Silk_Resampler_160_441_ARMA4_COEFS =
    {
         23492,  -6457,  14358,  -4856,  14654, -13008,   4456
    };

    internal static short[] SKP_Silk_Resampler_120_441_ARMA4_COEFS =
    {
         19311, -15569,  19489,  -6950,  21441, -13559,   2370
    };

    internal static short[] SKP_Silk_Resampler_80_441_ARMA4_COEFS =
    {
         13248, -23849,  24126,  -9486,  26806, -14286,   1065
    };

    /* Table with interplation fractions of 1/288 : 2/288 : 287/288 (432 Words) */
    internal static short[][] SKP_Silk_resampler_frac_FIR_144 =
    {
        new short[] { -647,  1884, 30078},
        new short[] { -625,  1736, 30044},
        new short[] { -603,  1591, 30005},
        new short[] { -581,  1448, 29963},
        new short[] { -559,  1308, 29917},
        new short[] { -537,  1169, 29867},
        new short[] { -515,  1032, 29813},
        new short[] { -494,   898, 29755},
        new short[] { -473,   766, 29693},
        new short[] { -452,   636, 29627},
        new short[] { -431,   508, 29558},
        new short[] { -410,   383, 29484},
        new short[] { -390,   260, 29407},
        new short[] { -369,   139, 29327},
        new short[] { -349,    20, 29242},
        new short[] { -330,   -97, 29154},
        new short[] { -310,  -211, 29062},
        new short[] { -291,  -324, 28967},
        new short[] { -271,  -434, 28868},
        new short[] { -253,  -542, 28765},
        new short[] { -234,  -647, 28659},
        new short[] { -215,  -751, 28550},
        new short[] { -197,  -852, 28436},
        new short[] { -179,  -951, 28320},
        new short[] { -162, -1048, 28200},
        new short[] { -144, -1143, 28077},
        new short[] { -127, -1235, 27950},
        new short[] { -110, -1326, 27820},
        new short[] {  -94, -1414, 27687},
        new short[] {  -77, -1500, 27550},
        new short[] {  -61, -1584, 27410},
        new short[] {  -45, -1665, 27268},
        new short[] {  -30, -1745, 27122},
        new short[] {  -15, -1822, 26972},
        new short[] {    0, -1897, 26820},
        new short[] {   15, -1970, 26665},
        new short[] {   29, -2041, 26507},
        new short[] {   44, -2110, 26346},
        new short[] {   57, -2177, 26182},
        new short[] {   71, -2242, 26015},
        new short[] {   84, -2305, 25845},
        new short[] {   97, -2365, 25673},
        new short[] {  110, -2424, 25498},
        new short[] {  122, -2480, 25320},
        new short[] {  134, -2534, 25140},
        new short[] {  146, -2587, 24956},
        new short[] {  157, -2637, 24771},
        new short[] {  168, -2685, 24583},
        new short[] {  179, -2732, 24392},
        new short[] {  190, -2776, 24199},
        new short[] {  200, -2819, 24003},
        new short[] {  210, -2859, 23805},
        new short[] {  220, -2898, 23605},
        new short[] {  229, -2934, 23403},
        new short[] {  238, -2969, 23198},
        new short[] {  247, -3002, 22992},
        new short[] {  255, -3033, 22783},
        new short[] {  263, -3062, 22572},
        new short[] {  271, -3089, 22359},
        new short[] {  279, -3114, 22144},
        new short[] {  286, -3138, 21927},
        new short[] {  293, -3160, 21709},
        new short[] {  300, -3180, 21488},
        new short[] {  306, -3198, 21266},
        new short[] {  312, -3215, 21042},
        new short[] {  318, -3229, 20816},
        new short[] {  323, -3242, 20589},
        new short[] {  328, -3254, 20360},
        new short[] {  333, -3263, 20130},
        new short[] {  338, -3272, 19898},
        new short[] {  342, -3278, 19665},
        new short[] {  346, -3283, 19430},
        new short[] {  350, -3286, 19194},
        new short[] {  353, -3288, 18957},
        new short[] {  356, -3288, 18718},
        new short[] {  359, -3286, 18478},
        new short[] {  362, -3283, 18238},
        new short[] {  364, -3279, 17996},
        new short[] {  366, -3273, 17753},
        new short[] {  368, -3266, 17509},
        new short[] {  369, -3257, 17264},
        new short[] {  371, -3247, 17018},
        new short[] {  372, -3235, 16772},
        new short[] {  372, -3222, 16525},
        new short[] {  373, -3208, 16277},
        new short[] {  373, -3192, 16028},
        new short[] {  373, -3175, 15779},
        new short[] {  373, -3157, 15529},
        new short[] {  372, -3138, 15279},
        new short[] {  371, -3117, 15028},
        new short[] {  370, -3095, 14777},
        new short[] {  369, -3072, 14526},
        new short[] {  368, -3048, 14274},
        new short[] {  366, -3022, 14022},
        new short[] {  364, -2996, 13770},
        new short[] {  362, -2968, 13517},
        new short[] {  359, -2940, 13265},
        new short[] {  357, -2910, 13012},
        new short[] {  354, -2880, 12760},
        new short[] {  351, -2848, 12508},
        new short[] {  348, -2815, 12255},
        new short[] {  344, -2782, 12003},
        new short[] {  341, -2747, 11751},
        new short[] {  337, -2712, 11500},
        new short[] {  333, -2676, 11248},
        new short[] {  328, -2639, 10997},
        new short[] {  324, -2601, 10747},
        new short[] {  320, -2562, 10497},
        new short[] {  315, -2523, 10247},
        new short[] {  310, -2482,  9998},
        new short[] {  305, -2442,  9750},
        new short[] {  300, -2400,  9502},
        new short[] {  294, -2358,  9255},
        new short[] {  289, -2315,  9009},
        new short[] {  283, -2271,  8763},
        new short[] {  277, -2227,  8519},
        new short[] {  271, -2182,  8275},
        new short[] {  265, -2137,  8032},
        new short[] {  259, -2091,  7791},
        new short[] {  252, -2045,  7550},
        new short[] {  246, -1998,  7311},
        new short[] {  239, -1951,  7072},
        new short[] {  232, -1904,  6835},
        new short[] {  226, -1856,  6599},
        new short[] {  219, -1807,  6364},
        new short[] {  212, -1758,  6131},
        new short[] {  204, -1709,  5899},
        new short[] {  197, -1660,  5668},
        new short[] {  190, -1611,  5439},
        new short[] {  183, -1561,  5212},
        new short[] {  175, -1511,  4986},
        new short[] {  168, -1460,  4761},
        new short[] {  160, -1410,  4538},
        new short[] {  152, -1359,  4317},
        new short[] {  145, -1309,  4098},
        new short[] {  137, -1258,  3880},
        new short[] {  129, -1207,  3664},
        new short[] {  121, -1156,  3450},
        new short[] {  113, -1105,  3238},
        new short[] {  105, -1054,  3028},
        new short[] {   97, -1003,  2820},
        new short[] {   89,  -952,  2614},
        new short[] {   81,  -901,  2409},
        new short[] {   73,  -851,  2207},
    };
}
