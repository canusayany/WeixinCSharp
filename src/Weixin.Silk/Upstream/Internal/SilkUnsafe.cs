using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace SilkCodec.NET.Internal;

/// <summary>Pinned buffers, pointer casts, and block copies for the Silk DSP inner loops.</summary>
internal static unsafe class SilkUnsafe
{
#if NETCOREAPP3_0_OR_GREATER
    public const MethodImplOptions Hot =
        MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization;
#else
    public const MethodImplOptions Hot = MethodImplOptions.AggressiveInlining;
#endif

    [MethodImpl(Hot)]
    public static T[] Pinned<T>(int length) where T : unmanaged
#if NET5_0_OR_GREATER
        => GC.AllocateArray<T>(length, pinned: true);
#else
        => new T[length];
#endif

    [MethodImpl(Hot)]
    public static T[] Uninit<T>(int length) where T : unmanaged
#if NET5_0_OR_GREATER
        => GC.AllocateUninitializedArray<T>(length);
#else
        => new T[length];
#endif

    [MethodImpl(Hot)]
    public static T* Ptr<T>(T[] array) where T : unmanaged
#if NET5_0_OR_GREATER
        => (T*)Unsafe.AsPointer(ref MemoryMarshal.GetArrayDataReference(array));
#else
        => (T*)Unsafe.AsPointer(ref MemoryMarshal.GetReference(array.AsSpan()));
#endif

    [MethodImpl(Hot)]
    public static T* Ptr<T>(Span<T> span) where T : unmanaged
        => (T*)Unsafe.AsPointer(ref MemoryMarshal.GetReference(span));

    [MethodImpl(Hot)]
    public static T* Ptr<T>(ReadOnlySpan<T> span) where T : unmanaged
        => (T*)Unsafe.AsPointer(ref MemoryMarshal.GetReference(span));

    [MethodImpl(Hot)]
    public static ref T At<T>(T[] array, int index)
#if NET5_0_OR_GREATER
        => ref Unsafe.Add(ref MemoryMarshal.GetArrayDataReference(array), index);
#else
        => ref Unsafe.Add(ref MemoryMarshal.GetReference(array.AsSpan()), index);
#endif

    [MethodImpl(Hot)]
    public static void Clear<T>(T* dest, int count) where T : unmanaged
    {
        if (count > 0)
#if NET6_0_OR_GREATER
            NativeMemory.Clear(dest, (nuint)count * (nuint)sizeof(T));
#else
            Unsafe.InitBlock(dest, 0, (uint)count * (uint)sizeof(T));
#endif
    }

    [MethodImpl(Hot)]
    public static void Clear<T>(T[] array) where T : unmanaged
        => array.AsSpan().Clear();

    [MethodImpl(Hot)]
    public static void Clear<T>(T[] array, int count) where T : unmanaged
        => array.AsSpan(0, count).Clear();

    [MethodImpl(Hot)]
    public static void Copy<T>(T* dest, T* src, int count) where T : unmanaged
    {
        if (count > 0)
#if NETCOREAPP2_0_OR_GREATER
            Buffer.MemoryCopy(src, dest, (long)count * sizeof(T), (long)count * sizeof(T));
#else
            Unsafe.CopyBlock(dest, src, (uint)count * (uint)sizeof(T));
#endif
    }

    [MethodImpl(Hot)]
    public static void Copy<T>(Span<T> dest, ReadOnlySpan<T> src) where T : unmanaged
        => src.CopyTo(dest);

    [MethodImpl(Hot)]
    public static int Abs(int value) => value >= 0 ? value : unchecked(-value);
}
