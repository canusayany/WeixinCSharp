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
/**
 *
 * @author Jing Dai
 * @author Dingxin Xu
 */
internal static class MainFLP
{
    /**
     * using log2() helps the fixed-point conversion.
     * @param x
     * @return
     */
    internal static float SKP_Silk_log2( double x )
    {
        return ( float )( 3.32192809488736 * Math.Log10( x ) );
    }
}
