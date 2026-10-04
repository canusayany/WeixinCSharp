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
 * Classes for IIR/FIR resamplers.
 *
 * @author Jing Dai
 * @author Dingxin Xu
 */
internal static class ResamplerStructs
{
    /**
     * Flag to enable support for input/output sampling rates above 48 kHz. Turn off for embedded devices.
     */
    internal static readonly bool RESAMPLER_SUPPORT_ABOVE_48KHZ = true;

    internal const int SKP_Silk_RESAMPLER_MAX_FIR_ORDER =                16;
    internal const int SKP_Silk_RESAMPLER_MAX_IIR_ORDER =                6;
}
internal sealed class SKP_Silk_resampler_state_struct
 {
    internal int[]       sIIR = new int[ ResamplerStructs.SKP_Silk_RESAMPLER_MAX_IIR_ORDER ];        /* this must be the first element of this struct */
    internal int[]       sFIR = new int[ ResamplerStructs.SKP_Silk_RESAMPLER_MAX_FIR_ORDER ];
    internal int[]       sDown2 = new int[ 2 ];

    internal string resampler_function_name = null!;
    internal ResamplerFP resamplerCB = null!;
    internal void resampler_function( SKP_Silk_resampler_state_struct state, short[] output, int out_offset, short[] input, int in_offset, int len )
    {
        resamplerCB.resampler_function(state, output, out_offset, input, in_offset, len);
    }

    internal string up2_function_name = null!;
    internal Up2FP up2CB = null!;
    internal void up2_function(  int[] state, short[] output, int out_offset, short[] input, int in_offset, int len )
    {
        up2CB.up2_function(state, output, out_offset, input, in_offset, len);

    }

    internal int       batchSize;
    internal int       invRatio_Q16;
    internal int       FIR_Fracs;
    internal int       input2x;
    internal short[]   Coefs = null!;

    internal int[]       sDownPre = new int[ 2 ];
    internal int[]       sUpPost = new int[ 2 ];

    internal string down_pre_function_name = null!;
    internal DownPreFP  downPreCB = null!;
    internal void down_pre_function ( int[] state, short[] output, int out_offset, short[] input, int in_offset, int len )
    {
        downPreCB.down_pre_function(state, output, out_offset, input, in_offset, len);
    }

    internal string up_post_function_name = null!;
    internal UpPostFP  upPostCB = null!;
    internal void up_post_function ( int[] state, short[] output, int out_offset, short[] input, int in_offset, int len )
    {
        upPostCB.up_post_function(state, output, out_offset, input, in_offset, len);
    }
    internal int       batchSizePrePost;
    internal int       ratio_Q16;
    internal int       nPreDownsamplers;
    internal int       nPostUpsamplers;
    internal int magic_number;

    /**
     * set all fields of the instance to zero.
     */
    public void memZero()
    {
//        {
//            if(this.Coefs != null)
//            {
//                Array.Fill(this.Coefs, (short)0);
//            }
//        }
        this.Coefs = null!;

        Array.Fill(this.sDown2, 0);
        Array.Fill(this.sDownPre, 0);
        Array.Fill(this.sFIR, 0);
        Array.Fill(this.sIIR, 0);
        Array.Fill(this.sUpPost, 0);

        this.batchSize = 0;
        this.batchSizePrePost = 0;
        this.down_pre_function_name = null!;
        this.downPreCB = null!;
        this.FIR_Fracs = 0;
        this.input2x = 0;
        this.invRatio_Q16 = 0;
        this.magic_number = 0;
        this.nPostUpsamplers = 0;
        this.nPreDownsamplers = 0;
        this.ratio_Q16 = 0;
        this.resampler_function_name = null!;
        this.resamplerCB = null!;
        this.up2_function_name = null!;
        this.up2CB = null!;
        this.up_post_function_name = null!;
        this.upPostCB = null!;
    }
}
 /*************************************************************************************/
internal interface ResamplerFP
 {
     void resampler_function( SKP_Silk_resampler_state_struct state, short[] output, int out_offset, short[] input, int in_offset, int len );
 }
internal interface Up2FP
 {
     void up2_function(  int[] state, short[] output, int out_offset, short[] input, int in_offset, int len );
 }
internal interface DownPreFP
 {
     void down_pre_function ( int[] state, short[] output, int out_offset, short[] input, int in_offset, int len );
 }
internal interface UpPostFP
 {
     void up_post_function ( int[] state, short[] output, int out_offset, short[] input, int in_offset, int len );
 }
 /*************************************************************************************/
