using System.Buffers.Binary;
using System.ComponentModel;
using System.Diagnostics;
using System.Security.Cryptography;

namespace Weixin.Protocol;

public sealed record VoiceCodecResult(byte[] Data, int DurationMilliseconds);

/// <summary>
/// Local Tencent-format SILK encoding and PCM16 mono WAV decoding using pinned silk-wasm 3.7.1.
/// Deployment paths are trusted configuration; media bytes cannot select code or arguments.
/// Caller owns input/result arrays. Private intermediate buffers are cleared after use.
/// Limits are local resource guards, not published WeChat account/service quotas.
/// </summary>
public sealed class VoiceCodec
{
    public const int DefaultSampleRate = 24000;
    private const int HardByteLimit = 100 * 1024 * 1024;
    // Pinned common.h: 250 bytes/frame * 5 internal frames/packet. The decoder
    // prefetches two packets; guard its unchecked C input reads before starting it.
    private const int MaxSilkPacketBytes = 1250;
    public string RuntimeDirectory { get; }
    public string NodeExecutablePath { get; }
    public int MaxDurationMilliseconds { get; init; } = 60_000;
    public int MaxInputBytes { get; init; } = 16 * 1024 * 1024;
    public int MaxOutputBytes { get; init; } = 16 * 1024 * 1024;
    public TimeSpan OperationTimeout { get; init; } = TimeSpan.FromSeconds(30);

    public VoiceCodec(string? runtimeDirectory = null, string? nodeExecutablePath = null)
    {
        RuntimeDirectory = Path.GetFullPath(runtimeDirectory ?? Path.Combine(AppContext.BaseDirectory, "runtime", "voice"));
        NodeExecutablePath = Path.GetFullPath(nodeExecutablePath ?? Path.Combine(RuntimeDirectory, OperatingSystem.IsWindows() ? "node.exe" : "node"));
    }

    public async Task<VoiceCodecResult> EncodePcmToSilkAsync(byte[] monoS16le, int sampleRate = DefaultSampleRate,
        CancellationToken ct = default)
    {
        ValidateConfiguration(); ValidateRate(sampleRate); ValidateInput(monoS16le); ct.ThrowIfCancellationRequested();
        if ((monoS16le.Length & 1) != 0) throw new ArgumentException("PCM16 输入必须包含完整的双字节采样。");
        if (monoS16le.Length >= 12 && monoS16le.AsSpan(0, 4).SequenceEqual("RIFF"u8) && monoS16le.AsSpan(8, 4).SequenceEqual("WAVE"u8))
            throw new ArgumentException("声明为 PCM 的输入包含 WAV 容器，请使用 WAV 编码入口。");
        var frameBytes = checked(sampleRate / 50 * 2);
        var paddedLength = checked(Math.Max(2, (monoS16le.Length + frameBytes - 1) / frameBytes) * frameBytes);
        if (paddedLength > MaxInputBytes || (long)paddedLength * 1000 > (long)MaxDurationMilliseconds * sampleRate * 2)
            throw new ArgumentException("语音时长或 PCM 大小超过本地保护上限。");
        // SILK uses 20ms packets. Pad the tail, and provide the two packets required
        // by the pinned decoder's lookahead. Returned duration includes that silence.
        var padded = new byte[paddedLength]; monoS16le.CopyTo(padded, 0);
        try
        {
            var result = await InvokeAsync(1, padded, sampleRate, MaxOutputBytes, ct).ConfigureAwait(false);
            try
            {
                if (ParsedSilkPacketMinimumDuration(result.Data) != result.DurationMilliseconds)
                    throw new InvalidDataException("语音编码结果的实际时长不一致。");
                return result;
            }
            catch { CryptographicOperations.ZeroMemory(result.Data); throw; }
        }
        finally { CryptographicOperations.ZeroMemory(padded); }
    }

    public Task<VoiceCodecResult> EncodePcmToSilkAsync(byte[] monoS16le, CancellationToken ct) =>
        EncodePcmToSilkAsync(monoS16le, DefaultSampleRate, ct);

    /// <summary>Accepts uncompressed RIFF PCM16 mono 24000Hz WAV only; rejects compressed/stereo formats.</summary>
    public async Task<VoiceCodecResult> EncodeWaveToSilkAsync(byte[] wave, CancellationToken ct = default)
    {
        ValidateConfiguration(); ValidateInput(wave); ct.ThrowIfCancellationRequested();
        var (offset, count) = ParseWave(wave);
        var pcm = wave.AsSpan(offset, count).ToArray();
        try { return await EncodePcmToSilkAsync(pcm, DefaultSampleRate, ct).ConfigureAwait(false); }
        finally { CryptographicOperations.ZeroMemory(pcm); }
    }

    public async Task<VoiceCodecResult> DecodeSilkToWaveAsync(byte[] silk, int sampleRate = DefaultSampleRate,
        CancellationToken ct = default)
    {
        ValidateConfiguration(); ValidateRate(sampleRate); ValidateInput(silk); ct.ThrowIfCancellationRequested();
        var minimumDuration = ParsedSilkPacketMinimumDuration(silk);
        // A crafted packet can carry up to five internal frames. Reserve that
        // conservative bound before invoking WASM, then verify actual duration.
        if ((long)minimumDuration * sampleRate * 2 / 1000 * 5 + 44 > MaxOutputBytes)
            throw new ArgumentException("解码语音大小超过本地保护上限。");
        var decoded = await InvokeAsync(2, silk, sampleRate, MaxOutputBytes - 44, ct).ConfigureAwait(false);
        try
        {
            if (decoded.DurationMilliseconds < minimumDuration || decoded.DurationMilliseconds > MaxDurationMilliseconds ||
                decoded.Data.Length != (long)decoded.DurationMilliseconds * sampleRate * 2 / 1000)
                throw new InvalidDataException("语音解码结果的实际时长或大小不一致。");
            var wave = new byte[checked(decoded.Data.Length + 44)];
            "RIFF"u8.CopyTo(wave); BinaryPrimitives.WriteUInt32LittleEndian(wave.AsSpan(4), (uint)wave.Length - 8);
            "WAVEfmt "u8.CopyTo(wave.AsSpan(8)); BinaryPrimitives.WriteUInt32LittleEndian(wave.AsSpan(16), 16);
            BinaryPrimitives.WriteUInt16LittleEndian(wave.AsSpan(20), 1); BinaryPrimitives.WriteUInt16LittleEndian(wave.AsSpan(22), 1);
            BinaryPrimitives.WriteUInt32LittleEndian(wave.AsSpan(24), (uint)sampleRate); BinaryPrimitives.WriteUInt32LittleEndian(wave.AsSpan(28), (uint)sampleRate * 2);
            BinaryPrimitives.WriteUInt16LittleEndian(wave.AsSpan(32), 2); BinaryPrimitives.WriteUInt16LittleEndian(wave.AsSpan(34), 16);
            "data"u8.CopyTo(wave.AsSpan(36)); BinaryPrimitives.WriteUInt32LittleEndian(wave.AsSpan(40), (uint)decoded.Data.Length);
            decoded.Data.CopyTo(wave, 44);
            return new(wave, decoded.DurationMilliseconds);
        }
        finally { CryptographicOperations.ZeroMemory(decoded.Data); }
    }

    public Task<VoiceCodecResult> DecodeSilkToWaveAsync(byte[] silk, CancellationToken ct) =>
        DecodeSilkToWaveAsync(silk, DefaultSampleRate, ct);

    private async Task<VoiceCodecResult> InvokeAsync(int mode, byte[] input, int sampleRate, int outputLimit, CancellationToken ct)
    {
        var bridge = Path.Combine(RuntimeDirectory, "codec-bridge.mjs");
        if (!File.Exists(NodeExecutablePath) || !File.Exists(bridge) ||
            !File.Exists(Path.Combine(RuntimeDirectory, "silk-wasm", "lib", "index.mjs")) ||
            !File.Exists(Path.Combine(RuntimeDirectory, "silk-wasm", "lib", "silk.wasm")))
            throw new InvalidOperationException("程序缺少随包提供的 runtime/voice 编解码运行时。");
        var start = new ProcessStartInfo(NodeExecutablePath)
        {
            UseShellExecute = false, CreateNoWindow = true, WorkingDirectory = RuntimeDirectory,
            RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true
        };
        start.Environment.Remove("NODE_OPTIONS"); start.Environment.Remove("NODE_PATH");
        start.ArgumentList.Add("--permission"); start.ArgumentList.Add("--allow-fs-read=" + RuntimeDirectory);
        start.ArgumentList.Add("--no-addons"); start.ArgumentList.Add("--no-warnings"); start.ArgumentList.Add(bridge);
        using var process = new Process { StartInfo = start };
        try { if (!process.Start()) throw new InvalidOperationException("语音编解码进程未能启动。"); }
        catch (Exception ex) when (ex is Win32Exception or IOException)
        { throw new InvalidOperationException("语音编解码运行时未能启动，请核对随包运行时。"); }
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct);
        deadline.CancelAfter(OperationTimeout);
        using var stop = deadline.Token.Register(() => KillOwnedProcess(process));
        var token = deadline.Token;
        async Task<T> Guard<T>(Func<Task<T>> action)
        { try { return await action().ConfigureAwait(false); } catch { deadline.Cancel(); throw; } }
        var write = Guard(async () =>
        {
            var header = new byte[28];
            try
            {
                "WXC1"u8.CopyTo(header); BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(4), 1);
                BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(8), (uint)mode); BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(12), (uint)sampleRate);
                BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(16), (uint)input.Length);
                BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(20), (uint)MaxDurationMilliseconds);
                BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(24), (uint)outputLimit);
                await process.StandardInput.BaseStream.WriteAsync(header, token).ConfigureAwait(false);
                await process.StandardInput.BaseStream.WriteAsync(input, token).ConfigureAwait(false);
                await process.StandardInput.BaseStream.FlushAsync(token).ConfigureAwait(false);
                process.StandardInput.Close(); return true;
            }
            finally { CryptographicOperations.ZeroMemory(header); }
        });
        var read = Guard(() => ReadResponseAsync(process.StandardOutput.BaseStream, outputLimit, token));
        var error = Guard(async () =>
        {
            var buffer = new byte[1024]; var count = 0;
            try
            {
                int got; while ((got = await process.StandardError.BaseStream.ReadAsync(buffer, token).ConfigureAwait(false)) != 0)
                { count += got; if (count > 65536) throw new InvalidDataException("语音编解码诊断输出超过本地上限。"); CryptographicOperations.ZeroMemory(buffer); }
                return count;
            }
            finally { CryptographicOperations.ZeroMemory(buffer); }
        });
        var exit = Guard(async () => { await process.WaitForExitAsync(token).ConfigureAwait(false); return process.ExitCode; });
        try
        {
            await Task.WhenAll(write, read, error, exit).ConfigureAwait(false);
            if (exit.Result != 0 || error.Result != 0 || read.Result.DurationMilliseconds is <= 0 ||
                read.Result.DurationMilliseconds > MaxDurationMilliseconds)
                throw new InvalidDataException("语音编解码结果无效。");
            return read.Result;
        }
        catch
        {
            deadline.Cancel(); KillOwnedProcess(process);
            try { await process.WaitForExitAsync(CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false); }
            catch (Exception ex) when (ex is InvalidOperationException or TimeoutException) { }
            if (read.IsCompletedSuccessfully) CryptographicOperations.ZeroMemory(read.Result.Data);
            if (ct.IsCancellationRequested) throw new OperationCanceledException(ct);
            throw new InvalidDataException("语音编解码未能完成，请核对输入和随包运行时；未输出原始诊断或音频数据。");
        }
    }

    private static async Task<VoiceCodecResult> ReadResponseAsync(Stream stream, int outputLimit, CancellationToken ct)
    {
        var header = new byte[16]; byte[]? data = null;
        try
        {
            await stream.ReadExactlyAsync(header, ct).ConfigureAwait(false);
            if (!header.AsSpan(0, 4).SequenceEqual("WXR1"u8) || BinaryPrimitives.ReadUInt32LittleEndian(header.AsSpan(4)) != 1)
                throw new InvalidDataException("语音编解码响应格式异常。");
            var duration = BinaryPrimitives.ReadUInt32LittleEndian(header.AsSpan(8));
            var length = BinaryPrimitives.ReadUInt32LittleEndian(header.AsSpan(12));
            if (length is 0 || length > outputLimit || duration > int.MaxValue) throw new InvalidDataException("语音编解码响应超过上限。");
            data = new byte[(int)length]; await stream.ReadExactlyAsync(data, ct).ConfigureAwait(false);
            if (await stream.ReadAsync(header.AsMemory(0, 1), ct).ConfigureAwait(false) != 0)
                throw new InvalidDataException("语音编解码响应有多余内容。");
            var result = new VoiceCodecResult(data, (int)duration); data = null; return result;
        }
        finally { CryptographicOperations.ZeroMemory(header); if (data is not null) CryptographicOperations.ZeroMemory(data); }
    }

    // Packet count is a minimum duration: SILK packets may contain 1..5 frames.
    // Actual decoded duration is derived from PCM bytes by the bridge, not getDuration.
    private int ParsedSilkPacketMinimumDuration(byte[] data)
    {
        var offset = 0;
        if (data.Length >= 10 && data[0] == 2 && data.AsSpan(1, 9).SequenceEqual("#!SILK_V3"u8)) offset = 10;
        else throw new InvalidDataException("语音不是受支持的 SILK_V3 格式。");
        var frames = 0;
        while (offset < data.Length)
        {
            if (data.Length - offset < 2) throw new InvalidDataException("SILK 帧头不完整。");
            var size = BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(offset)); offset += 2;
            if (size == ushort.MaxValue && offset == data.Length) break;
            if (size is 0 or > MaxSilkPacketBytes || size > data.Length - offset) throw new InvalidDataException("SILK 帧结构不完整或超过编解码器包上限。");
            if (++frames > MaxDurationMilliseconds / 20) throw new ArgumentException("SILK 最小包时长超过本地保护上限。");
            offset += size;
        }
        if (frames < 2) throw new InvalidDataException("SILK 至少需要两个完整语音包。");
        return checked(frames * 20);
    }

    private static (int Offset, int Count) ParseWave(byte[] data)
    {
        if (data.Length < 44 || !data.AsSpan(0, 4).SequenceEqual("RIFF"u8) || !data.AsSpan(8, 4).SequenceEqual("WAVE"u8) ||
            BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(4)) != data.Length - 8)
            throw new ArgumentException("WAV RIFF 头或文件长度不完整。");
        var offset = 12; var pcmOffset = -1; var pcmBytes = 0; var format = false;
        while (offset < data.Length)
        {
            if (data.Length - offset < 8) throw new ArgumentException("WAV 块头不完整。");
            var size = BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(offset + 4));
            var end = (long)offset + 8 + size + (size & 1);
            if (end > data.Length) throw new ArgumentException("WAV 块长度异常。");
            var tag = data.AsSpan(offset, 4);
            if (tag.SequenceEqual("fmt "u8))
            {
                if (format || size < 16) throw new ArgumentException("WAV 格式块重复或不完整。");
                var fmt = data.AsSpan(offset + 8, (int)size);
                if (BinaryPrimitives.ReadUInt16LittleEndian(fmt) != 1 || BinaryPrimitives.ReadUInt16LittleEndian(fmt[2..]) != 1 ||
                    BinaryPrimitives.ReadUInt32LittleEndian(fmt[4..]) != DefaultSampleRate || BinaryPrimitives.ReadUInt32LittleEndian(fmt[8..]) != DefaultSampleRate * 2 ||
                    BinaryPrimitives.ReadUInt16LittleEndian(fmt[12..]) != 2 || BinaryPrimitives.ReadUInt16LittleEndian(fmt[14..]) != 16)
                    throw new ArgumentException("WAV 必须为未压缩 PCM16、单声道、24000Hz；其他格式请先转换或按文件附件发送。");
                format = true;
            }
            else if (tag.SequenceEqual("data"u8))
            {
                if (pcmOffset >= 0 || size == 0 || (size & 1) != 0) throw new ArgumentException("WAV 音频块重复、为空或采样不完整。");
                pcmOffset = offset + 8; pcmBytes = (int)size;
            }
            offset = (int)end;
        }
        if (!format || pcmOffset < 0) throw new ArgumentException("WAV 缺少格式或音频数据块。");
        return (pcmOffset, pcmBytes);
    }

    private void ValidateInput(byte[] data)
    { ArgumentNullException.ThrowIfNull(data); if (data.Length is 0 || data.Length > MaxInputBytes) throw new ArgumentException("语音输入为空或超过本地大小保护上限。"); }
    private static void ValidateRate(int rate)
    { if (rate is not (8000 or 12000 or 16000 or 24000 or 32000 or 44100 or 48000)) throw new ArgumentException("PCM 采样率不受编解码器支持。"); }
    private void ValidateConfiguration()
    {
        if (MaxInputBytes is < 1 or > HardByteLimit || MaxOutputBytes is < 64 or > HardByteLimit ||
            MaxDurationMilliseconds is < 20 or > 3_600_000 || OperationTimeout <= TimeSpan.Zero || OperationTimeout > TimeSpan.FromMinutes(5))
            throw new ArgumentException("语音编解码本地保护配置无效。");
    }
    private static void KillOwnedProcess(Process process)
    { try { if (!process.HasExited) process.Kill(entireProcessTree: true); } catch (Exception ex) when (ex is InvalidOperationException or Win32Exception) { } }
}
