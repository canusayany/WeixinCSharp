using System.Buffers.Binary;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Weixin.Protocol;

return await CodecTests.RunAsync(args);

internal static class CodecTests
{
    private sealed record Result(string Name, bool Passed, long DurationMilliseconds, string? Error);
    private static readonly List<Result> Results = [];
    private static string runtime = "", output = "";
    private static byte[] pcm = [], silk = [], wave = [];
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };

    public static async Task<int> RunAsync(string[] args)
    {
        try
        {
            for (var i = 0; i < args.Length; i++)
                if (args[i] == "--runtime" && i + 1 < args.Length) runtime = Path.GetFullPath(args[++i]);
                else if (args[i] == "--output" && i + 1 < args.Length) output = Path.GetFullPath(args[++i]);
                else throw new ArgumentException("Use --runtime <directory> --output <new directory>.");
            if (runtime.Length == 0 || output.Length == 0 || Directory.Exists(output)) throw new ArgumentException("Runtime and a fresh output directory are required.");
            Directory.CreateDirectory(output);
            pcm = Sine(2000); await File.WriteAllBytesAsync(Path.Combine(output, "synthetic.pcm"), pcm);
            await Case("pcm_encode_tencent_header_actual_duration_preserves_input", async () =>
            {
                var before = SHA256.HashData(pcm); var encoded = await Codec().EncodePcmToSilkAsync(pcm);
                Assert(encoded.DurationMilliseconds == 2000 && encoded.Data.AsSpan(0, 10).SequenceEqual("\u0002#!SILK_V3"u8), "Tencent encoded header/duration differs.");
                Assert(SHA256.HashData(pcm).AsSpan().SequenceEqual(before), "Caller PCM was modified.");
                silk = encoded.Data; await File.WriteAllBytesAsync(Path.Combine(output, "synthetic.silk"), silk);
            });
            await Case("silk_decode_pcm16_mono_wave_actual_bytes", async () =>
            {
                var before = SHA256.HashData(silk); var decoded = await Codec().DecodeSilkToWaveAsync(silk);
                Assert(decoded.DurationMilliseconds == 2000 && decoded.Data.Length == pcm.Length + 44, "Decoded actual duration/length differs.");
                CheckWave(decoded.Data, pcm.Length); Assert(decoded.Data.AsSpan(44).ContainsAnyExcept((byte)0), "Decoded audio is all zero.");
                Assert(SHA256.HashData(silk).AsSpan().SequenceEqual(before), "Caller SILK was modified.");
                wave = decoded.Data; await File.WriteAllBytesAsync(Path.Combine(output, "synthetic.wav"), wave);
            });
            await Case("independent_official_sdk_encode_and_decode_interoperability", async () =>
            {
                var p = NewNode(Path.Combine(AppContext.BaseDirectory, "official-interop.mjs"));
                p.StartInfo.ArgumentList.Add(Path.Combine(runtime, "silk-wasm", "lib", "index.mjs"));
                p.StartInfo.ArgumentList.Add(Path.Combine(output, "synthetic.pcm"));
                p.StartInfo.ArgumentList.Add(Path.Combine(output, "synthetic.silk"));
                p.StartInfo.ArgumentList.Add(Path.Combine(output, "synthetic.wav"));
                using (p)
                {
                    Assert(p.Start(), "Official test process did not start.");
                    var stdout = p.StandardOutput.ReadToEndAsync(); var stderr = p.StandardError.ReadToEndAsync();
                    await p.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(30));
                    Assert(p.ExitCode == 0 && (await stderr).Length == 0, "Official interoperability process failed.");
                    using var json = JsonDocument.Parse(await stdout);
                    Assert(json.RootElement.GetProperty("equalEncode").GetBoolean() && json.RootElement.GetProperty("equalDecode").GetBoolean(), "Official bytes differ.");
                    await File.WriteAllTextAsync(Path.Combine(output, "official-interop.json"), json.RootElement.GetRawText());
                }
            });
            await Case("wave_input_encodes_identically_to_same_pcm", async () =>
            {
                var encoded = await Codec().EncodeWaveToSilkAsync(Wave(pcm));
                try { Assert(encoded.DurationMilliseconds == 2000 && encoded.Data.AsSpan().SequenceEqual(silk), "Equivalent WAV/PCM produced different bytes."); }
                finally { CryptographicOperations.ZeroMemory(encoded.Data); }
            });
            await Case("partial_packet_and_short_audio_pad_to_decoder_lookahead", async () =>
            {
                foreach (var count in new[] { 2, 962 })
                {
                    var encoded = await Codec().EncodePcmToSilkAsync(new byte[count]);
                    try
                    {
                        Assert(encoded.DurationMilliseconds == 40, "Short input must report its 40ms padded duration.");
                        var decoded = await Codec().DecodeSilkToWaveAsync(encoded.Data);
                        try { Assert(decoded.DurationMilliseconds == 40 && decoded.Data.Length == 1964, "Short padded decode differs."); }
                        finally { CryptographicOperations.ZeroMemory(decoded.Data); }
                    }
                    finally { CryptographicOperations.ZeroMemory(encoded.Data); }
                }
            });
            await Case("explicit_terminal_packet_decodes_identically", async () =>
            {
                var terminated = silk.Concat(new byte[] { 255, 255 }).ToArray();
                var decoded = await Codec().DecodeSilkToWaveAsync(terminated);
                try { Assert(decoded.Data.AsSpan().SequenceEqual(wave), "Terminal marker changed audio."); }
                finally { CryptographicOperations.ZeroMemory(decoded.Data); CryptographicOperations.ZeroMemory(terminated); }
            });
            await Case("pcm_empty_odd_rate_and_duration_guards", async () =>
            {
                await Reject<ArgumentException>(() => Codec().EncodePcmToSilkAsync([]));
                await Reject<ArgumentException>(() => Codec().EncodePcmToSilkAsync(new byte[3]));
                await Reject<ArgumentException>(() => Codec().EncodePcmToSilkAsync(pcm, 22050));
                await Reject<ArgumentException>(() => Codec().EncodePcmToSilkAsync(Wave(pcm)));
                await Reject<ArgumentException>(() => new VoiceCodec(runtime) { MaxDurationMilliseconds = 1000 }.EncodePcmToSilkAsync(pcm));
                await Reject<ArgumentException>(() => new VoiceCodec(runtime) { MaxInputBytes = 100 }.EncodePcmToSilkAsync(pcm));
            });
            await Case("wave_stereo_compression_rate_and_structure_guards", async () =>
            {
                foreach (var offset in new[] { 20, 22, 24, 28, 32, 34 })
                {
                    var wrong = Wave(pcm); wrong[offset] ^= 3;
                    await Reject<ArgumentException>(() => Codec().EncodeWaveToSilkAsync(wrong));
                }
                var truncated = Wave(pcm)[..^1]; await Reject<ArgumentException>(() => Codec().EncodeWaveToSilkAsync(truncated));
                var duplicate = Wave(pcm).Concat(Wave(pcm).AsSpan(12, 24).ToArray()).ToArray();
                BinaryPrimitives.WriteUInt32LittleEndian(duplicate.AsSpan(4), (uint)duplicate.Length - 8);
                await Reject<ArgumentException>(() => Codec().EncodeWaveToSilkAsync(duplicate));
                var huge = Wave(pcm); BinaryPrimitives.WriteUInt32LittleEndian(huge.AsSpan(40), uint.MaxValue);
                await Reject<ArgumentException>(() => Codec().EncodeWaveToSilkAsync(huge));
            });
            await Case("silk_headers_truncation_zero_oversize_and_lookahead_guards", async () =>
            {
                foreach (var invalid in new[] { Encoding.ASCII.GetBytes("#!SILK_V3"), silk[1..], silk[..11], Framed([[]]), Framed([new byte[1251], new byte[1]]), Framed([new byte[1]]) })
                    await Reject<InvalidDataException>(() => Codec().DecodeSilkToWaveAsync(invalid));
                var truncated = silk[..^1]; await Reject<InvalidDataException>(() => Codec().DecodeSilkToWaveAsync(truncated));
            });
            await Case("silk_duration_and_worst_case_output_guard_precede_decoder", async () =>
            {
                await Reject<ArgumentException>(() => new VoiceCodec(runtime) { MaxDurationMilliseconds = 1000 }.DecodeSilkToWaveAsync(silk));
                await Reject<ArgumentException>(() => new VoiceCodec(runtime) { MaxOutputBytes = pcm.Length + 44 }.DecodeSilkToWaveAsync(silk));
                await Reject<ArgumentException>(() => new VoiceCodec(runtime) { MaxOutputBytes = 64 }.DecodeSilkToWaveAsync(silk));
            });
            await Case("pre_cancelled_calls_return_cancellation_without_process", async () =>
            {
                using var cancel = new CancellationTokenSource(); cancel.Cancel();
                await Reject<OperationCanceledException>(() => Codec().EncodePcmToSilkAsync(pcm, cancel.Token));
                await Reject<OperationCanceledException>(() => Codec().DecodeSilkToWaveAsync(silk, cancel.Token));
            });
            await Case("live_owned_codec_process_cancel_kills_only_its_process", async () =>
            {
                var existing = NodePids(); using var cancel = new CancellationTokenSource();
                var longPcm = Sine(60000); var task = Codec().EncodePcmToSilkAsync(longPcm, cancel.Token); var pid = 0;
                var timer = Stopwatch.StartNew();
                while (timer.Elapsed < TimeSpan.FromSeconds(10) && !task.IsCompleted)
                {
                    pid = NodePids().FirstOrDefault(id => !existing.Contains(id)); if (pid != 0) break;
                    await Task.Delay(10);
                }
                cancel.Cancel(); await Reject<OperationCanceledException>(async () => await task);
                Assert(pid != 0, "No actual owned Node process was observed before cancellation.");
                await WaitGone(pid); CryptographicOperations.ZeroMemory(longPcm);
                await File.WriteAllTextAsync(Path.Combine(output, "cancel-process.json"), JsonSerializer.Serialize(new { ownedNodePid = pid, exited = true, unrelatedProcessesKilled = false }, Json));
            });
            await Case("operation_timeout_is_generic_and_does_not_leak_process", async () =>
            {
                var existing = NodePids(); var codec = new VoiceCodec(runtime) { OperationTimeout = TimeSpan.FromTicks(1) };
                var error = await Reject<InvalidDataException>(() => codec.EncodePcmToSilkAsync(pcm));
                Assert(!error.Message.Contains(runtime, StringComparison.Ordinal) && !error.Message.Contains("WASM", StringComparison.Ordinal), "Timeout exposed raw runtime diagnostics.");
                foreach (var pid in NodePids().Where(id => !existing.Contains(id))) await WaitGone(pid);
            });
            await Case("runtime_missing_and_hard_configuration_guards", async () =>
            {
                await Reject<InvalidOperationException>(() => new VoiceCodec(Path.Combine(output, "missing-runtime")).EncodePcmToSilkAsync(pcm));
                await Reject<ArgumentException>(() => new VoiceCodec(runtime) { MaxInputBytes = 100 * 1024 * 1024 + 1 }.EncodePcmToSilkAsync(pcm));
                await Reject<ArgumentException>(() => new VoiceCodec(runtime) { MaxDurationMilliseconds = 0 }.EncodePcmToSilkAsync(pcm));
                await Reject<ArgumentException>(() => new VoiceCodec(runtime) { MaxOutputBytes = 0 }.EncodePcmToSilkAsync(pcm));
            });
            await Case("binary_bridge_invalid_request_and_output_limits_fail_safely", async () =>
            {
                await BridgeReject(new byte[28]);
                await BridgeReject(Request(2, silk, 1000, 16 * 1024 * 1024));
                await BridgeReject(Request(2, silk, 60000, 64));
                await BridgeReject(Request(2, Framed([new byte[1251], new byte[1]]), 60000, 16 * 1024 * 1024));
            });
            var summary = new { schemaVersion = 1, scope = "local-pinned-codec-interoperability", realWeChatDeliveryVerified = false,
                completedUtc = DateTimeOffset.UtcNow, runtime, nodeSha256 = Hash(Path.Combine(runtime, "node.exe")),
                bridgeSha256 = Hash(Path.Combine(runtime, "codec-bridge.mjs")), sourceSha256 = Hash(Path.GetFullPath(Path.Combine(runtime, "..", "..", "src", "Weixin.Protocol", "VoiceCodec.cs"))),
                total = Results.Count, passed = Results.Count(x => x.Passed), failed = Results.Count(x => !x.Passed), cases = Results,
                isolation = new { syntheticAudioOnly = true, accountStateAccess = false, networkRequests = 0 } };
            await File.WriteAllTextAsync(Path.Combine(output, "tests-summary.json"), JsonSerializer.Serialize(summary, Json));
            Console.WriteLine($"VOICE CODEC: {Results.Count(x => x.Passed)}/{Results.Count} passed; {Path.Combine(output, "tests-summary.json")}");
            return Results.All(x => x.Passed) ? 0 : 1;
        }
        catch (Exception ex) { Console.Error.WriteLine(ex.Message); return 1; }
        finally { CryptographicOperations.ZeroMemory(pcm); CryptographicOperations.ZeroMemory(silk); CryptographicOperations.ZeroMemory(wave); }
    }

    private static VoiceCodec Codec() => new(runtime);
    private static async Task Case(string name, Func<Task> action)
    {
        var timer = Stopwatch.StartNew();
        try { await action(); Results.Add(new(name, true, timer.ElapsedMilliseconds, null)); Console.WriteLine("PASS " + name); }
        catch (Exception ex) { Results.Add(new(name, false, timer.ElapsedMilliseconds, ex.GetType().Name + ": " + ex.Message)); Console.WriteLine("FAIL " + name + ": " + ex.Message); }
    }
    private static void Assert(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
    private static async Task<T> Reject<T>(Func<Task> action) where T : Exception
    { try { await action(); } catch (T ex) { return ex; } throw new InvalidOperationException("Expected " + typeof(T).Name + "."); }
    private static byte[] Sine(int ms)
    {
        var data = new byte[ms * 48];
        for (var i = 0; i < data.Length / 2; i++) BinaryPrimitives.WriteInt16LittleEndian(data.AsSpan(i * 2), (short)(Math.Sin(i * Math.Tau * 440 / 24000) * 9000));
        return data;
    }
    private static byte[] Wave(byte[] data)
    {
        var wav = new byte[data.Length + 44]; "RIFF"u8.CopyTo(wav); BinaryPrimitives.WriteUInt32LittleEndian(wav.AsSpan(4), (uint)wav.Length - 8);
        "WAVEfmt "u8.CopyTo(wav.AsSpan(8)); BinaryPrimitives.WriteUInt32LittleEndian(wav.AsSpan(16), 16);
        BinaryPrimitives.WriteUInt16LittleEndian(wav.AsSpan(20), 1); BinaryPrimitives.WriteUInt16LittleEndian(wav.AsSpan(22), 1);
        BinaryPrimitives.WriteUInt32LittleEndian(wav.AsSpan(24), 24000); BinaryPrimitives.WriteUInt32LittleEndian(wav.AsSpan(28), 48000);
        BinaryPrimitives.WriteUInt16LittleEndian(wav.AsSpan(32), 2); BinaryPrimitives.WriteUInt16LittleEndian(wav.AsSpan(34), 16);
        "data"u8.CopyTo(wav.AsSpan(36)); BinaryPrimitives.WriteUInt32LittleEndian(wav.AsSpan(40), (uint)data.Length); data.CopyTo(wav, 44); return wav;
    }
    private static void CheckWave(byte[] data, int pcmBytes)
    { Assert(data.AsSpan(0, 44).SequenceEqual(Wave(new byte[pcmBytes]).AsSpan(0, 44)), "WAV PCM16 mono 24k header differs."); }
    private static byte[] Framed(byte[][] packets)
    {
        using var stream = new MemoryStream(); stream.Write("\u0002#!SILK_V3"u8);
        foreach (var packet in packets) { var size = new byte[2]; BinaryPrimitives.WriteUInt16LittleEndian(size, (ushort)packet.Length); stream.Write(size); stream.Write(packet); }
        return stream.ToArray();
    }
    private static Process NewNode(string script)
    {
        var start = new ProcessStartInfo(Path.Combine(runtime, "node.exe")) { UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true };
        start.Environment.Remove("NODE_OPTIONS"); start.Environment.Remove("NODE_PATH"); start.ArgumentList.Add(script);
        return new Process { StartInfo = start };
    }
    private static HashSet<int> NodePids()
    {
        var ids = new HashSet<int>();
        foreach (var p in Process.GetProcessesByName("node")) using (p)
            try { if (string.Equals(p.MainModule?.FileName, Path.Combine(runtime, "node.exe"), StringComparison.OrdinalIgnoreCase)) ids.Add(p.Id); }
            catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException) { }
        return ids;
    }
    private static async Task WaitGone(int pid)
    {
        try { using var p = Process.GetProcessById(pid); await p.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(5)); }
        catch (ArgumentException) { }
    }
    private static byte[] Request(int mode, byte[] data, int maxDuration, int maxOutput)
    {
        var request = new byte[data.Length + 28]; "WXC1"u8.CopyTo(request);
        BinaryPrimitives.WriteUInt32LittleEndian(request.AsSpan(4), 1); BinaryPrimitives.WriteUInt32LittleEndian(request.AsSpan(8), (uint)mode);
        BinaryPrimitives.WriteUInt32LittleEndian(request.AsSpan(12), 24000); BinaryPrimitives.WriteUInt32LittleEndian(request.AsSpan(16), (uint)data.Length);
        BinaryPrimitives.WriteUInt32LittleEndian(request.AsSpan(20), (uint)maxDuration); BinaryPrimitives.WriteUInt32LittleEndian(request.AsSpan(24), (uint)maxOutput);
        data.CopyTo(request, 28); return request;
    }
    private static async Task BridgeReject(byte[] request)
    {
        using var p = NewNode(Path.Combine(runtime, "codec-bridge.mjs")); Assert(p.Start(), "Bridge process did not start.");
        var stdout = p.StandardOutput.BaseStream.CopyToAsync(Stream.Null); var stderr = p.StandardError.ReadToEndAsync();
        await p.StandardInput.BaseStream.WriteAsync(request); p.StandardInput.Close();
        await p.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(10)); await stdout;
        Assert(p.ExitCode == 1 && (await stderr) == "Voice codec operation failed.\n", "Bridge did not fail with its safe generic error.");
        CryptographicOperations.ZeroMemory(request);
    }
    private static string? Hash(string path) => File.Exists(path) ? Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))) : null;
}
