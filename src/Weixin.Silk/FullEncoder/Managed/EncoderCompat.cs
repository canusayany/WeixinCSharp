using System.Diagnostics;

namespace SilkCodec.NET.Managed;

internal static class EncoderCompat
{
    [Conditional("DEBUG")]
    internal static void Assert(bool condition)
    {
        Debug.Assert(condition);
    }

    internal static void Fill<T>(T[] array, T value)
    {
        Array.Fill(array, value);
    }

    internal static void Fill<T>(T[] array, int fromIndex, int toIndex, T value)
    {
        Array.Fill(array, value, fromIndex, toIndex - fromIndex);
    }

    internal static T[][] NewArray<T>(int firstLength, int secondLength)
    {
        var result = new T[firstLength][];
        for (var i = 0; i < result.Length; i++)
            result[i] = new T[secondLength];
        return result;
    }

    internal static T[][][] NewArray<T>(int firstLength, int secondLength, int thirdLength)
    {
        var result = new T[firstLength][][];
        for (var i = 0; i < result.Length; i++)
            result[i] = NewArray<T>(secondLength, thirdLength);
        return result;
    }
}
