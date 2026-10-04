using System.Buffers.Binary;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;

namespace Weixin.Protocol;

/// <summary>Stores state encrypted for the current Windows user, with an exclusive lifetime lock.</summary>
public sealed class StateVault : IDisposable
{
    private const int HeaderSize = 12;
    private const int MaxFileBytes = 8 * 1024 * 1024;
    private const uint FormatVersion = 1;
    private static readonly byte[] Magic = "WXDPAPI\0"u8.ToArray();
    private readonly string _path;
    private readonly FileStream _lockStream;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private volatile bool _disposed;

    public StateVault(string path)
    {
        if (!OperatingSystem.IsWindows())
            throw new PlatformNotSupportedException("StateVault requires Windows DPAPI; plaintext storage is not supported.");
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        _path = Path.GetFullPath(path);
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        _lockStream = new FileStream(_path + ".lock", FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
    }

    /// <summary>Returns default when the file is absent; malformed or undecryptable state throws.</summary>
    public async Task<T?> LoadAsync<T>(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        byte[]? plaintext = null;
        try
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            FileStream stream;
            try
            {
                stream = new FileStream(_path, FileMode.Open, FileAccess.Read, FileShare.Read,
                    4096, FileOptions.Asynchronous | FileOptions.SequentialScan);
            }
            catch (FileNotFoundException) { return default; }
            catch (DirectoryNotFoundException) { return default; }

            byte[] stored;
            await using (stream.ConfigureAwait(false))
            {
                if (stream.Length <= HeaderSize || stream.Length > MaxFileBytes)
                    throw new InvalidDataException("State file size is invalid.");
                stored = new byte[(int)stream.Length];
                try { await stream.ReadExactlyAsync(stored, cancellationToken).ConfigureAwait(false); }
                catch (EndOfStreamException) { throw new InvalidDataException("State file is incomplete."); }
            }

            if (!stored.AsSpan(0, Magic.Length).SequenceEqual(Magic)
                || BinaryPrimitives.ReadUInt32LittleEndian(stored.AsSpan(Magic.Length, 4)) != FormatVersion)
                throw new InvalidDataException("State file format or version is invalid.");

            byte[] ciphertext = stored.AsSpan(HeaderSize).ToArray();
            try { plaintext = Transform(ciphertext, protect: false); }
            catch (CryptographicException)
            {
                throw new InvalidDataException("State file is damaged or cannot be decrypted for this Windows user.");
            }
            finally { CryptographicOperations.ZeroMemory(ciphertext); }

            cancellationToken.ThrowIfCancellationRequested();
            try { return JsonSerializer.Deserialize<T>(plaintext); }
            catch (JsonException) { throw new InvalidDataException("Decrypted state is not valid JSON for the requested type."); }
        }
        finally
        {
            if (plaintext is not null) CryptographicOperations.ZeroMemory(plaintext);
            _gate.Release();
        }
    }

    /// <summary>Encrypts before writing and replaces the prior file only after a durable, complete write.</summary>
    public async Task SaveAsync<T>(T value, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        byte[]? plaintext = null;
        byte[]? ciphertext = null;
        string? temporaryPath = null;
        try
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            cancellationToken.ThrowIfCancellationRequested();
            plaintext = JsonSerializer.SerializeToUtf8Bytes(value);
            if (plaintext.Length > MaxFileBytes - HeaderSize)
                throw new InvalidDataException("Serialized state exceeds the size limit.");
            ciphertext = Transform(plaintext, protect: true);
            if (ciphertext.Length > MaxFileBytes - HeaderSize)
                throw new InvalidDataException("Encrypted state exceeds the size limit.");

            byte[] header = new byte[HeaderSize];
            Magic.CopyTo(header, 0);
            BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(Magic.Length, 4), FormatVersion);
            cancellationToken.ThrowIfCancellationRequested();
            temporaryPath = _path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            await using (var stream = new FileStream(temporaryPath, FileMode.CreateNew, FileAccess.Write,
                FileShare.None, 4096, FileOptions.Asynchronous | FileOptions.WriteThrough))
            {
                await stream.WriteAsync(header, cancellationToken).ConfigureAwait(false);
                await stream.WriteAsync(ciphertext, cancellationToken).ConfigureAwait(false);
                await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
                stream.Flush(flushToDisk: true);
            }
            // Cancellation before this commit preserves the previous file. After the commit, save succeeded.
            cancellationToken.ThrowIfCancellationRequested();
            File.Move(temporaryPath, _path, overwrite: true);
            temporaryPath = null;
        }
        finally
        {
            if (plaintext is not null) CryptographicOperations.ZeroMemory(plaintext);
            if (ciphertext is not null) CryptographicOperations.ZeroMemory(ciphertext);
            if (temporaryPath is not null)
            {
                try { File.Delete(temporaryPath); }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
            }
            _gate.Release();
        }
    }

    public void Dispose()
    {
        _gate.Wait();
        try
        {
            if (_disposed) return;
            _disposed = true;
            _lockStream.Dispose();
        }
        finally { _gate.Release(); }
        // Queued operations can still acquire the managed gate and observe the disposed state.
    }

    private static byte[] Transform(byte[] input, bool protect)
    {
        var inputBlob = new DataBlob { Size = input.Length, Data = Marshal.AllocHGlobal(input.Length) };
        var outputBlob = new DataBlob();
        try
        {
            Marshal.Copy(input, 0, inputBlob.Data, input.Length);
            bool succeeded = protect
                ? CryptProtectData(ref inputBlob, null, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, 1, out outputBlob)
                : CryptUnprotectData(ref inputBlob, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, 1, out outputBlob);
            if (!succeeded)
                throw new CryptographicException($"Windows DPAPI operation failed (error {Marshal.GetLastWin32Error()}).");
            if (outputBlob.Size <= 0 || outputBlob.Size > MaxFileBytes || outputBlob.Data == IntPtr.Zero)
                throw new CryptographicException("Windows DPAPI returned an invalid result size.");
            var output = new byte[outputBlob.Size];
            Marshal.Copy(outputBlob.Data, output, 0, output.Length);
            return output;
        }
        finally
        {
            ClearNative(inputBlob.Data, inputBlob.Size);
            Marshal.FreeHGlobal(inputBlob.Data);
            if (outputBlob.Data != IntPtr.Zero)
            {
                ClearNative(outputBlob.Data, outputBlob.Size);
                LocalFree(outputBlob.Data);
            }
        }
    }

    private static void ClearNative(IntPtr pointer, int length)
    {
        var zeros = new byte[4096];
        for (int offset = 0; offset < length; offset += zeros.Length)
            Marshal.Copy(zeros, 0, IntPtr.Add(pointer, offset), Math.Min(zeros.Length, length - offset));
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct DataBlob
    {
        public int Size;
        public IntPtr Data;
    }

    [DllImport("Crypt32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CryptProtectData(ref DataBlob input, string? description, IntPtr optionalEntropy,
        IntPtr reserved, IntPtr prompt, uint flags, out DataBlob output);

    [DllImport("Crypt32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CryptUnprotectData(ref DataBlob input, IntPtr description, IntPtr optionalEntropy,
        IntPtr reserved, IntPtr prompt, uint flags, out DataBlob output);

    [DllImport("Kernel32.dll")]
    private static extern IntPtr LocalFree(IntPtr memory);
}
