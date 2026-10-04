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
 *
 * @author Jing Dai
 * @author Dingxin Xu
 */
internal static class Typedef
{
    internal static int SKP_STR_CASEINSENSITIVE_COMPARE(string x, string y)
    {
        return string.CompareOrdinal(x, y);
    }

    internal const long SKP_int64_MAX =  0x7FFFFFFFFFFFFFFFL;   //  2^63 - 1
    internal const long SKP_int64_MIN =  long.MinValue;   // -2^63
    internal const int SKP_int32_MAX =  0x7FFFFFFF;             //  2^31 - 1 =  2147483647
    internal const int SKP_int32_MIN =  int.MinValue;             // -2^31     = -2147483648
    internal const short SKP_int16_MAX =  0x7FFF;               //  2^15 - 1 =  32767
    internal const short SKP_int16_MIN =  short.MinValue;        // -2^15     = -32768
    internal const byte SKP_int8_MAX =   0x7F;                  //  2^7 - 1  =  127
    internal const sbyte SKP_int8_MIN =  sbyte.MinValue;            // -2^7      = -128

    internal const long SKP_uint32_MAX = 0xFFFFFFFFL;  // 2^32 - 1 = 4294967295
    internal const long SKP_uint32_MIN = 0x00000000L;
    internal const int SKP_uint16_MAX = 0xFFFF;        // 2^16 - 1 = 65535
    internal const int SKP_uint16_MIN = 0x0000;
    internal const short SKP_uint8_MAX =  0xFF;        //  2^8 - 1 = 255
    internal const short SKP_uint8_MIN =  0x00;

    internal const bool SKP_TRUE =       true;
    internal const bool SKP_FALSE =      false;

    /* assertions */
    internal static void SKP_assert(bool COND)
    {
        System.Diagnostics.Debug.Assert(COND);
    }
}
