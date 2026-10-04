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
using static SilkCodec.NET.Managed.CommonPitchEstDefines;

namespace SilkCodec.NET.Managed;

/**
 * Auto Generated File from generate_pitch_est_tables.m
 *
 * @author Jing Dai
 * @author Dingxin Xu
 */
internal static class PitchEstTables
{
    internal static short[][] SKP_Silk_CB_lags_stage2 =
    {
        new short[] {0, 2,-1,-1,-1, 0, 0, 1, 1, 0, 1},
        new short[] {0, 1, 0, 0, 0, 0, 0, 1, 0, 0, 0},
        new short[] {0, 0, 1, 0, 0, 0, 1, 0, 0, 0, 0},
        new short[] {0,-1, 2, 1, 0, 1, 1, 0, 0,-1,-1}
    };

    internal static short[][] SKP_Silk_CB_lags_stage3 =
    {
        new short[] {-9,-7,-6,-5,-5,-4,-4,-3,-3,-2,-2,-2,-1,-1,-1, 0, 0, 0, 1, 1, 0, 1, 2, 2, 2, 3, 3, 4, 4, 5, 6, 5, 6, 8},
        new short[] {-3,-2,-2,-2,-1,-1,-1,-1,-1, 0, 0,-1, 0, 0, 0, 0, 0, 0, 1, 0, 0, 0, 1, 1, 0, 1, 1, 2, 1, 2, 2, 2, 2, 3},
        new short[] { 3, 3, 2, 2, 2, 2, 1, 2, 1, 1, 0, 1, 1, 0, 0, 0, 1, 0, 0, 0, 0, 0, 0,-1, 0, 0,-1,-1,-1,-1,-1,-2,-2,-2},
        new short[] { 9, 8, 6, 5, 6, 5, 4, 4, 3, 3, 2, 2, 2, 1, 0, 1, 1, 0, 0, 0,-1,-1,-1,-2,-2,-2,-3,-3,-4,-4,-5,-5,-6,-7}
     };

    internal static short[][][] SKP_Silk_Lag_range_stage3 =
    {
        /* Lags to search for low number of stage3 cbks */
        new short[][] {
            new short[] {-2,6},
            new short[] {-1,5},
            new short[] {-1,5},
            new short[] {-2,7}
        },
        /* Lags to search for middle number of stage3 cbks */
        new short[][] {
            new short[] {-4,8},
            new short[] {-1,6},
            new short[] {-1,6},
            new short[] {-4,9}
        },
        /* Lags to search for max number of stage3 cbks */
        new short[][] {
            new short[] {-9,12},
            new short[] {-3,7},
            new short[] {-2,7},
            new short[] {-7,13}
        }
    };

    internal static short[] SKP_Silk_cbk_sizes_stage3 =
    {
        PITCH_EST_NB_CBKS_STAGE3_MIN,
        PITCH_EST_NB_CBKS_STAGE3_MID,
        PITCH_EST_NB_CBKS_STAGE3_MAX
    };

    internal static short[] SKP_Silk_cbk_offsets_stage3 =
    {
        ((PITCH_EST_NB_CBKS_STAGE3_MAX - PITCH_EST_NB_CBKS_STAGE3_MIN) >> 1),
        ((PITCH_EST_NB_CBKS_STAGE3_MAX - PITCH_EST_NB_CBKS_STAGE3_MID) >> 1),
        0
    };
}
