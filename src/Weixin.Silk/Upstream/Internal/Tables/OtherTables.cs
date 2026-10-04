// SILK v3 bitstream specification tables.
// Numeric constants required for interoperability with Skype SILK SDK 1.0.9 / WeChat-QQ SILK v3.

namespace SilkCodec.NET.Internal.Tables;

internal static partial class SilkSpec
{

        internal static readonly int[] LTPVqSizes =
        [
            10, 20, 40
        ];

        internal static readonly int[] TargetRateTableNB =
        [
            0, 8000, 9000, 11000, 13000, 16000, 22000
        ];

        internal static readonly int[] TargetRateTableMB =
        [
            0, 10000, 12000, 14000, 17000, 21000, 28000
        ];

        internal static readonly int[] TargetRateTableWB =
        [
            0, 11000, 14000, 17000, 21000, 26000, 36000
        ];

        internal static readonly int[] TargetRateTableSWB =
        [
            0, 13000, 16000, 19000, 25000, 32000, 46000
        ];

        internal static readonly int[] SNRTableQ1 =
        [
            19, 31, 35, 39, 43, 47, 54, 64
        ];

        internal static readonly int[] SNRTableOneBitPerSampleQ7 =
        [
            1984, 2240, 2408, 2708
        ];

        internal static readonly int[][] SWBDetectBHPQ13 =
        [
            [
                575, -948, 575
            ],
            [
                575, -221, 575
            ],
            [
                575, 104, 575
            ],
        ];

        internal static readonly int[][] SWBDetectAHPQ13 =
        [
            [
                14613, 6868
            ],
            [
                12883, 7337
            ],
            [
                11586, 7911
            ],
        ];

        internal static readonly int[] DecAHP24 =
        [
            -16220, 8030
        ];

        internal static readonly int[] DecBHP24 =
        [
            8000, -16000, 8000
        ];

        internal static readonly int[] DecAHP16 =
        [
            -16127, 7940
        ];

        internal static readonly int[] DecBHP16 =
        [
            8000, -16000, 8000
        ];

        internal static readonly int[] DecAHP12 =
        [
            -16043, 7859
        ];

        internal static readonly int[] DecBHP12 =
        [
            8000, -16000, 8000
        ];

        internal static readonly int[] DecAHP8 =
        [
            -15885, 7710
        ];

        internal static readonly int[] DecBHP8 =
        [
            8000, -16000, 8000
        ];

        internal static readonly int[] SamplingRatesTable =
        [
            8, 12, 16, 24
        ];

        internal static readonly int[][] QuantizationOffsetsQ10 =
        [
            [
                32, 100
            ],
            [
                100, 256
            ],
        ];

        internal static readonly int[] LTPScalesTableQ14 =
        [
            15565, 11469, 8192
        ];

        internal static readonly int[][] TransitionLPBQ28 =
        [
            [
                250767114, 501534038, 250767114
            ],
            [
                209867381, 419732057, 209867381
            ],
            [
                170987846, 341967853, 170987846
            ],
            [
                131531482, 263046905, 131531482
            ],
            [
                89306658, 178584282, 89306658
            ],
        ];

        internal static readonly int[][] TransitionLPAQ28 =
        [
            [
                506393414, 239854379
            ],
            [
                411067935, 169683996
            ],
            [
                306733530, 116694253
            ],
            [
                185807084, 77959395
            ],
            [
                35497197, 57401098
            ],
        ];

        internal const int LTPscaleOffset = 2;

        internal const int SamplingRatesOffset = 2;

        internal const int FrameTerminationOffset = 2;

        internal const int SeedOffset = 2;

        internal static readonly int[] LSFCosTabFIXQ12 =
        [
            8192, 8190, 8182, 8170, 8152, 8130, 8104, 8072, 8034, 7994, 7946, 7896, 7840, 7778, 7714, 7644,
            7568, 7490, 7406, 7318, 7226, 7128, 7026, 6922, 6812, 6698, 6580, 6458, 6332, 6204, 6070, 5934,
            5792, 5648, 5502, 5352, 5198, 5040, 4880, 4718, 4552, 4382, 4212, 4038, 3862, 3684, 3502, 3320,
            3136, 2948, 2760, 2570, 2378, 2186, 1990, 1794, 1598, 1400, 1202, 1002, 802, 602, 402, 202, 0,
            -202, -402, -602, -802, -1002, -1202, -1400, -1598, -1794, -1990, -2186, -2378, -2570, -2760,
            -2948, -3136, -3320, -3502, -3684, -3862, -4038, -4212, -4382, -4552, -4718, -4880, -5040, -5198,
            -5352, -5502, -5648, -5792, -5934, -6070, -6204, -6332, -6458, -6580, -6698, -6812, -6922, -7026,
            -7128, -7226, -7318, -7406, -7490, -7568, -7644, -7714, -7778, -7840, -7896, -7946, -7994, -8034,
            -8072, -8104, -8130, -8152, -8170, -8182, -8190, -8192
        ];

}
