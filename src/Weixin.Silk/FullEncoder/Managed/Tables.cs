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

internal static class Tables
{
    internal const int PITCH_EST_MAX_LAG_MS = 18;          /* 18 ms -> 56 Hz */
    internal const int PITCH_EST_MIN_LAG_MS = 2;          /* 2 ms -> 500 Hz */

    /**
     * Copies the specified range of the specified array into a new array. The
     * initial index of the range (<tt>from</tt>) must lie between zero and
     * <tt>original.Length</tt>, inclusive. The value at <tt>original[from]</tt>
     * is placed into the initial element of the copy (unless
     * <tt>from == original.Length</tt> or <tt>from == to</tt>). Values from
     * subsequent elements input the original array are placed into subsequent
     * elements input the copy. The index of the range (<tt>to</tt>), which
     * must be greater than or equal to <tt>from</tt>, may be greater than
     * <tt>original.Length</tt>, input which case <tt>0</tt> is placed input all
     * elements of the copy whose index is greater than or equal to
     * <tt>original.Length - from</tt>. The length of the returned array will be
     * <tt>to - from</tt>.
     *
     * @param original the array from which a range is to be copied
     * @param from the initial index of the range to be copied, inclusive
     * @param to  the index of the range to be copied, exclusive. (This
     * index may lie outside the array.)
     * @return a new array containing the specified range from the original
     * array, truncated or padded with zeros to obtain the required length
     * @throws ArrayIndexOutOfBoundsException if <tt>from &lt; 0</tt> or
     * <tt>from &gt; original.Length()</tt>
     * @throws IllegalArgumentException if <tt>from &gt; to</tt>
     * @throws NullPointerException if <tt>original</tt> is <tt>null</tt>
     */
    internal static int[] copyOfRange(int[] original, int from, int to)
    {
        if ((from < 0) || (from > original.Length))
            throw new IndexOutOfRangeException(from.ToString());
        if (from > to)
            throw new ArgumentException("to");

        int length = to - from;
        int[] copy = new int[length];

        for (int c = 0, o = from; c < length; c++, o++)
            copy[c] = (o < original.Length) ? original[o] : 0;

        return copy;
    }
}
