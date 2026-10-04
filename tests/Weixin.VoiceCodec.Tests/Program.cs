using System.Buffers.Binary;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Weixin.Protocol;
using Weixin.Silk;

return await CodecTests.RunAsync(args);

internal static class CodecTests
{
    private sealed record Result(string Name, bool Passed, long DurationMilliseconds, string? Error);
    private static readonly List<Result> Results = [];
    private static string output = "";
    private static byte[] pcm = [], silk = [], wave = [];
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };

    public static async Task<int> RunAsync(string[] args)
    {
        try
        {
            for (var i = 0; i < args.Length; i++)
                if (args[i] == "--output" && i + 1 < args.Length) output = Path.GetFullPath(args[++i]);
                else throw new ArgumentException("Use --output <new directory>.");
            if (output.Length == 0 || Directory.Exists(output)) throw new ArgumentException("A fresh output directory is required.");
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
            await Case("independent_legacy_silk_golden_vectors_decode_at_all_rates", async () =>
            {
                var goldenRoot = Path.Combine(AppContext.BaseDirectory, "Golden");
                using var manifest = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(goldenRoot, "manifest.json")));
                foreach (var vector in manifest.RootElement.GetProperty("files").EnumerateArray())
                {
                    var data = await File.ReadAllBytesAsync(Path.Combine(goldenRoot, vector.GetProperty("path").GetString()!));
                    Assert(Convert.ToHexString(SHA256.HashData(data)).Equals(vector.GetProperty("sha256").GetString(), StringComparison.OrdinalIgnoreCase), "Golden bytes differ.");
                    var rate = vector.GetProperty("apiSampleRate").GetInt32();
                    var decoded = await Codec().DecodeSilkToWaveAsync(data, rate);
                    var duration = vector.GetProperty("durationMilliseconds").GetInt32();
                    Assert(decoded.DurationMilliseconds == duration && decoded.Data.Length == 44 + (long)rate * 2 * duration / 1000, "Golden sample duration differs.");
                    Assert(decoded.Data.AsSpan(44).ContainsAnyExcept((byte)0), "Golden sample audio is silent.");
                    if (vector.TryGetProperty("referencePcmSha256", out var reference))
                        Assert(Convert.ToHexString(SHA256.HashData(decoded.Data.AsSpan(44))).Equals(reference.GetString(), StringComparison.OrdinalIgnoreCase), "PCM differs from independent Skype SDK reference.");
                    CryptographicOperations.ZeroMemory(decoded.Data);
                }
            });
            await Case("wave_input_encodes_identically_to_same_pcm", async () =>
            {
                var encoded = await Codec().EncodeWaveToSilkAsync(Wave(pcm));
                try { Assert(encoded.DurationMilliseconds == 2000 && encoded.Data.AsSpan().SequenceEqual(silk), "Equivalent WAV/PCM produced different bytes."); }
                finally { CryptographicOperations.ZeroMemory(encoded.Data); }
            });
            await Case("complete_managed_encoder_all_api_rates_and_frame_cancellation", async () =>
            {
                foreach (var rate in new[] { 8000, 12000, 16000, 24000, 32000, 44100, 48000 })
                {
                    var sample = new byte[rate * 2 * 2];
                    for (var n = 0; n < sample.Length / 2; n++)
                        BinaryPrimitives.WriteInt16LittleEndian(sample.AsSpan(n * 2), (short)(9000 * Math.Sin(n * Math.PI * 880 / rate)));
                    var encoded = await Codec().EncodePcmToSilkAsync(sample, rate);
                    var decoded = await Codec().DecodeSilkToWaveAsync(encoded.Data, rate);
                    Assert(encoded.DurationMilliseconds == 2000 && decoded.DurationMilliseconds == 2000 && decoded.Data.Length == sample.Length + 44 && decoded.Data.AsSpan(44).ContainsAnyExcept((byte)0), "Complete encoder rate interoperability differs.");
                    CryptographicOperations.ZeroMemory(sample); CryptographicOperations.ZeroMemory(encoded.Data); CryptographicOperations.ZeroMemory(decoded.Data);
                }
                using var cancel = new CancellationTokenSource();
                var checkpoints = 0;
                await Reject<OperationCanceledException>(() => Task.Run(() => ManagedSilkEncoder.Encode(pcm, 24000, 16 * 1024 * 1024, () =>
                {
                    // Entry, first frame start/end: at least one frame has really run.
                    if (++checkpoints == 4) cancel.Cancel();
                    cancel.Token.ThrowIfCancellationRequested();
                })));
                Assert(checkpoints == 4, "Cancellation was not observed at an in-flight frame checkpoint.");
            });
            await Case("partial_packet_and_short_audio_pad_to_legacy_compatible_40ms", async () =>
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
                await Reject<ArgumentException>(() => new VoiceCodec() { MaxDurationMilliseconds = 1000 }.EncodePcmToSilkAsync(pcm));
                await Reject<ArgumentException>(() => new VoiceCodec() { MaxInputBytes = 100 }.EncodePcmToSilkAsync(pcm));
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
            await Case("silk_headers_truncation_zero_oversize_and_minimum_packet_guards", async () =>
            {
                foreach (var invalid in new[] { Encoding.ASCII.GetBytes("#!SILK_V3"), silk[1..], silk[..11], Framed([[]]), Framed([new byte[1251], new byte[1]]), Framed([new byte[1]]) })
                    await Reject<InvalidDataException>(() => Codec().DecodeSilkToWaveAsync(invalid));
                var truncated = silk[..^1]; await Reject<InvalidDataException>(() => Codec().DecodeSilkToWaveAsync(truncated));
            });
            await Case("silk_duration_and_worst_case_output_guard_precede_decoder", async () =>
            {
                await Reject<ArgumentException>(() => new VoiceCodec() { MaxDurationMilliseconds = 1000 }.DecodeSilkToWaveAsync(silk));
                await Reject<ArgumentException>(() => new VoiceCodec() { MaxOutputBytes = pcm.Length + 44 }.DecodeSilkToWaveAsync(silk));
                await Reject<ArgumentException>(() => new VoiceCodec() { MaxOutputBytes = 64 }.DecodeSilkToWaveAsync(silk));
            });
            await Case("pre_cancelled_calls_return_cancellation", async () =>
            {
                using var cancel = new CancellationTokenSource(); cancel.Cancel();
                await Reject<OperationCanceledException>(() => Codec().EncodePcmToSilkAsync(pcm, cancel.Token));
                await Reject<OperationCanceledException>(() => Codec().DecodeSilkToWaveAsync(silk, cancel.Token));
            });
            await Case("active_managed_encoding_cooperates_with_cancellation", async () =>
            {
                var longPcm = Sine(60000);
                using var cancel = new CancellationTokenSource();
                var task = Codec().EncodePcmToSilkAsync(longPcm, cancel.Token);
                await Task.Delay(10); cancel.Cancel();
                await Reject<OperationCanceledException>(async () => await task);
                Assert(task.IsCompleted, "Cancelled operation was abandoned in the background.");
                CryptographicOperations.ZeroMemory(longPcm);
            });
            await Case("operation_timeout_is_generic_and_operation_is_joined", async () =>
            {
                var codec = new VoiceCodec { OperationTimeout = TimeSpan.FromTicks(1) };
                var task = codec.EncodePcmToSilkAsync(pcm);
                var error = await Reject<InvalidDataException>(async () => await task);
                Assert(task.IsCompleted && !error.Message.Contains("SilkCodec.NET", StringComparison.Ordinal), "Timeout exposed implementation diagnostics or left work running.");
            });
            await Case("hard_configuration_guards", async () =>
            {
                await Reject<ArgumentException>(() => new VoiceCodec { MaxInputBytes = 100 * 1024 * 1024 + 1 }.EncodePcmToSilkAsync(pcm));
                await Reject<ArgumentException>(() => new VoiceCodec { MaxDurationMilliseconds = 0 }.EncodePcmToSilkAsync(pcm));
                await Reject<ArgumentException>(() => new VoiceCodec { MaxOutputBytes = 0 }.EncodePcmToSilkAsync(pcm));
            });
            await Case("managed_output_limit_and_corrupt_payload_fail_without_audio", async () =>
            {
                await Reject<InvalidDataException>(() => new VoiceCodec { MaxOutputBytes = 64 }.EncodePcmToSilkAsync(pcm));
                // 0xffffffff lies outside the initial range-coder interval.
                // Zero payloads are not assumed corrupt: SILK has no general CRC.
                var outOfRange = Enumerable.Repeat((byte)255, 4).ToArray();
                await Reject<InvalidDataException>(() => Codec().DecodeSilkToWaveAsync(Framed([outOfRange, outOfRange])));
                foreach (var length in new[] { 1025, 1250 })
                {
                    var oversizedRangePayload = Enumerable.Repeat((byte)255, length).ToArray();
                    await Reject<InvalidDataException>(() => Codec().DecodeSilkToWaveAsync(Framed([oversizedRangePayload, oversizedRangePayload])));
                }
            });
            await Case("concurrent_operations_own_independent_codec_state", async () =>
            {
                var codec = Codec();
                var results = await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => codec.EncodePcmToSilkAsync(pcm)));
                foreach (var result in results)
                {
                    Assert(result.Data.AsSpan().SequenceEqual(silk), "Independent codec state changed concurrent output.");
                    CryptographicOperations.ZeroMemory(result.Data);
                }
            });
            await Case("packet_continuation_over_five_frames_or_missing_frame_is_rejected", async () =>
            {
                var goldenRoot = Path.Combine(AppContext.BaseDirectory, "Golden");
                using var manifest = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(goldenRoot, "manifest.json")));
                foreach (var vector in manifest.RootElement.GetProperty("invalidFiles").EnumerateArray())
                {
                    var bytes = await File.ReadAllBytesAsync(Path.Combine(goldenRoot, vector.GetProperty("path").GetString()!));
                    Assert(Convert.ToHexString(SHA256.HashData(bytes)).Equals(vector.GetProperty("sha256").GetString(), StringComparison.OrdinalIgnoreCase), "Malformed synthetic vector bytes differ.");
                    await Reject<InvalidDataException>(() => Codec().DecodeSilkToWaveAsync(bytes));
                }
            });
            var summary = new { schemaVersion = 2, scope = "pure-csharp-codec-and-independent-legacy-golden-vectors", realWeChatDeliveryVerified = false,
                completedUtc = DateTimeOffset.UtcNow, externalProcessesStarted = 0,
                codecAssemblySha256 = Hash(typeof(VoiceCodec).Assembly.Location),
                silkAssemblySha256 = Hash(typeof(SilkCodec.NET.SilkEncoder).Assembly.Location),
                goldenManifestSha256 = Hash(Path.Combine(AppContext.BaseDirectory, "Golden", "manifest.json")),
                total = Results.Count, passed = Results.Count(x => x.Passed), failed = Results.Count(x => !x.Passed), cases = Results,
                isolation = new { syntheticAudioOnly = true, accountStateAccess = false, networkRequests = 0 } };
            await File.WriteAllTextAsync(Path.Combine(output, "tests-summary.json"), JsonSerializer.Serialize(summary, Json));
            Console.WriteLine($"VOICE CODEC: {Results.Count(x => x.Passed)}/{Results.Count} passed; {Path.Combine(output, "tests-summary.json")}");
            return Results.All(x => x.Passed) ? 0 : 1;
        }
        catch (Exception ex) { Console.Error.WriteLine(ex.Message); return 1; }
        finally { CryptographicOperations.ZeroMemory(pcm); CryptographicOperations.ZeroMemory(silk); CryptographicOperations.ZeroMemory(wave); }
    }

    private static VoiceCodec Codec() => new();
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
    private static string Hash(string path) => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)));
}
