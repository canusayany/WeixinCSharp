/*
 * Portions adapted from the SILK SDK 1.0.9 reference implementation.
 * Copyright (c) 2006-2012, Skype Limited. All rights reserved.
 * See THIRD-PARTY-NOTICES for the BSD 3-Clause license and disclaimer.
 */

namespace SilkCodec.NET.Managed;

internal static class WarpedAutocorrelationFLP
{
    internal static void SKP_Silk_warped_autocorrelation_FLP(
        float[] corr,
        int corrOffset,
        float[] input,
        int inputOffset,
        float warping,
        int length,
        int order)
    {
        SKP_Silk_warped_autocorrelation_FLP(corr, corrOffset, input, inputOffset, warping, length, order,
            new double[Define.SHAPE_LPC_ORDER_MAX + 1], new double[Define.SHAPE_LPC_ORDER_MAX + 1]);
    }

    internal static void SKP_Silk_warped_autocorrelation_FLP(
        float[] corr,
        int corrOffset,
        float[] input,
        int inputOffset,
        float warping,
        int length,
        int order,
        double[] state,
        double[] correlations)
    {
        EncoderCompat.Assert((order & 1) == 0);

        Array.Clear(state, 0, order + 1);
        Array.Clear(correlations, 0, order + 1);
        for (var n = 0; n < length; n++)
        {
            double tmp1 = input[inputOffset + n];
            for (var i = 0; i < order; i += 2)
            {
                var tmp2 = state[i] + warping * (state[i + 1] - tmp1);
                state[i] = tmp1;
                correlations[i] += state[0] * tmp1;

                tmp1 = state[i + 1] + warping * (state[i + 2] - tmp2);
                state[i + 1] = tmp2;
                correlations[i + 1] += state[0] * tmp2;
            }
            state[order] = tmp1;
            correlations[order] += state[0] * tmp1;
        }

        for (var i = 0; i <= order; i++)
            corr[corrOffset + i] = (float)correlations[i];
    }
}
