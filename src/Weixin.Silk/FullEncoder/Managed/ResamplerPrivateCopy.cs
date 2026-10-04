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

using System;
using System.Diagnostics;

namespace SilkCodec.NET.Managed;
/**
 * Simple opy.
 * @author Jing Dai
 * @author Dingxin Xu
 */
internal static class ResamplerPrivateCopy
{
    /**
     * Simple copy.
     * @param SS Resampler state (unused).
     * @param output Output signal
     * @param out_offset offset of valid data.
     * @param input Input signal
     * @param in_offset offset of valid data.
     * @param inLen Number of input samples
     */
    internal static void SKP_Silk_resampler_private_copy(
        object                        SS,            /* I/O: Resampler state (unused)                */
        short[] output,        /* O:    Output signal                             */
        int out_offset,
        short[] input,            /* I:    Input signal                            */
        int in_offset,
        int                            inLen       /* I:    Number of input samples                    */
    )
    {
        for(int k=0; k<inLen; k++)
            output[out_offset+k] = input[in_offset+k];
    }
}
