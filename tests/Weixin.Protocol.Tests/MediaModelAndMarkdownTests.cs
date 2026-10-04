using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Weixin.Protocol;

namespace Weixin.Protocol.Tests;

public static class MediaModelAndMarkdownTests
{
    public static async Task RunAsync()
    {
        var tests = new Func<Task>[]
        {
            TypedMediaRoundTripsAsync, UploadedVoiceMetadataRemainsExplicitAsync, VoiceTranscriptRemainsSeparateAsync, Utf16ChunksPreserveUnicodeAsync,
            OfficialMarkdownVectorsAsync, MarkdownStreamingBoundariesAsync,
            MediaIntentPrecedesNetworkAsync, MediaSourceDedupeChecksContentAsync,
            UnknownMediaIsNotRetransmittedAsync, InvalidMediaFailsBeforeIntentAsync,
            MutableItemsAreFrozenBeforeWaitingAsync, TextAndItemApiShareHistoricalHashAsync,
            PayloadIdentityRemainsLocalAsync, MarkdownJobResumesAfterCancelledWaitAsync,
            CompletedMarkdownSurvivesOutboxPruningAsync, UnknownMarkdownBlocksResumeAsync,
            MarkdownPreflightAndHistoryCapacityAsync, CorruptMarkdownJobFailsClosedAsync,
            SourceKeysCannotMixJobAndSingleSendAsync, LegacyFallbackKeysMigrateWithoutRedeliveryAsync
        };
        foreach (var test in tests) await test();
        Console.WriteLine($"MediaModelAndMarkdownTests: {tests.Length} test groups passed (official filter vectors + offline HTTP/DPAPI).");
    }

    private static Task TypedMediaRoundTripsAsync()
    {
        const string json = """
            {"item_list":[
              {"type":2,"image_item":{"media":{"encrypt_query_param":"image-query","aes_key":"a2V5","encrypt_type":1,"future_cdn":18446744073709551615},"aeskey":"0123456789abcdef0123456789abcdef","mid_size":1234,"thumb_height":50,"custom_image":"kept"}},
              {"type":3,"voice_item":{"media":{"full_url":"https://novac2c.cdn.weixin.qq.com/c2c/download?fixture"},"encode_type":6,"sample_rate":24000,"bits_per_sample":16,"playtime":1234,"text":"语音转写"}},
              {"type":4,"file_item":{"media":{"encrypt_query_param":"file-query","aes_key":"a2V5"},"file_name":"示例.txt","md5":"fixture-md5","len":18446744073709551615}},
              {"type":5,"video_item":{"media":{"encrypt_query_param":"video-query","aes_key":"a2V5"},"video_size":4096,"play_length":3000,"video_md5":"fixture-video-md5","thumb_media":{"encrypt_query_param":"thumb-query"},"thumb_width":120,"thumb_height":80}}
            ],"future_message":"retained"}
            """;
        var message = JsonSerializer.Deserialize<InboundMessage>(json, ILinkClient.Json)!;
        var items = message.Items!;
        var image = items[0].ImageItem!; var cdn = image.Media!;
        var voice = items[1].VoiceItem!; var file = items[2].FileItem!; var video = items[3].VideoItem!;
        Assert(cdn.EncryptQueryParam == "image-query" && image.AesKey!.Length == 32, "Image fields must be typed.");
        Assert(cdn.OtherFields!["future_cdn"].GetRawText() == "18446744073709551615" && image.OtherFields!["custom_image"].GetString() == "kept", "Nested unknown fields must remain lossless.");
        Assert(voice.Playtime == 1234 && voice.SampleRate == 24000, "Voice metadata must not be inferred or altered.");
        Assert(file.Length == "18446744073709551615" && file.FileName == "示例.txt", "File len accepts a lossless numeric representation.");
        Assert(video.VideoSize == 4096 && video.ThumbMedia!.EncryptQueryParam == "thumb-query", "Video and thumbnail references must remain typed.");
        var serialized = JsonSerializer.Serialize(message, ILinkClient.Json);
        using var wire = JsonDocument.Parse(serialized);
        var imageWire = wire.RootElement.GetProperty("item_list")[0];
        Assert(!imageWire.TryGetProperty("voice_item", out _) && !imageWire.TryGetProperty("text_item", out _) &&
            !imageWire.GetProperty("image_item").TryGetProperty("url", out _), "Unset optional descriptors must be omitted from outgoing JSON.");
        var roundTrip = JsonSerializer.Deserialize<InboundMessage>(serialized, ILinkClient.Json)!;
        Assert(roundTrip.Items![0].ImageItem!.Media!.OtherFields!["future_cdn"].GetRawText() == "18446744073709551615" && roundTrip.OtherFields!["future_message"].GetString() == "retained", "Typed round trips must keep unknown descriptors.");
        return Task.CompletedTask;
    }

    private static Task UploadedVoiceMetadataRemainsExplicitAsync()
    {
        var uploaded = new UploadedMedia(MediaKind.Voice, "fixture-query", new string('a', 32), 123, 128, "tone.silk");
        var unspecified = JsonSerializer.Serialize(uploaded.ToMessageItem(2000, 6), ILinkClient.Json);
        Assert(!unspecified.Contains("sample_rate", StringComparison.Ordinal) && !unspecified.Contains("bits_per_sample", StringComparison.Ordinal), "Unknown source metadata must remain omitted.");
        var item = uploaded.ToMessageItem(2000, 6, 24000, 16);
        Assert(item.Type == 3 && item.VoiceItem is { EncodeType: 6, Playtime: 2000, SampleRate: 24000, BitsPerSample: 16 }, "Explicit PCM source metadata must be retained with SILK type 6.");
        using var wire = JsonDocument.Parse(JsonSerializer.Serialize(item, ILinkClient.Json));
        var voice = wire.RootElement.GetProperty("voice_item");
        Assert(voice.GetProperty("sample_rate").GetInt32() == 24000 && voice.GetProperty("bits_per_sample").GetInt32() == 16, "Official field names differ.");
        Throws<ArgumentException>(() => uploaded.ToMessageItem(2000, 6, 0, 16));
        Throws<ArgumentException>(() => uploaded.ToMessageItem(2000, 6, 24000, -1));
        return Task.CompletedTask;
    }

    private static Task VoiceTranscriptRemainsSeparateAsync()
    {
        var voice = new InboundMessage { Items = [new() { Type = 3, VoiceItem = new() { Text = "自动转写" } }] };
        Assert(voice.Text == "" && voice.VoiceTranscript == "自动转写", "An audio transcript must not become a text message for echo handlers.");
        voice.Items.Add(new() { Type = 1, TextItem = new() { Text = "真正的文本" } });
        Assert(voice.Text == "真正的文本" && voice.VoiceTranscript == "自动转写", "Mixed messages expose text and transcript separately.");
        voice.Items.Add(null!);
        Throws<InvalidDataException>(() => _ = voice.VoiceTranscript);
        return Task.CompletedTask;
    }

    private static Task Utf16ChunksPreserveUnicodeAsync()
    {
        var cases = new[] { "", new string('界', 4000), new string('界', 4001), new string('a', 3999) + "😀tail", "😀" + new string('汉', 7999) + "👨‍👩‍👧" };
        foreach (var input in cases)
        foreach (var limit in new[] { 2, 3, 4000 })
        {
            var chunks = MarkdownFormatting.ChunkText(input, limit);
            Assert(string.Concat(chunks) == input && chunks.All(c => c.Length <= limit && c.Length > 0), "Chunking must be lossless and obey UTF-16 limits.");
            for (int i = 1; i < chunks.Count; i++)
                Assert(!(char.IsHighSurrogate(chunks[i - 1][^1]) && char.IsLowSurrogate(chunks[i][0])), "A valid surrogate pair must never cross chunks.");
        }
        Assert(MarkdownFormatting.ChunkText(new string('界', 4000)).Single().Length == 4000, "Chinese text uses 4000 UTF-16 units, rather than an invented UTF-8 byte limit.");
        Throws<ArgumentOutOfRangeException>(() => MarkdownFormatting.ChunkText("😀", 1));
        return Task.CompletedTask;
    }

    // Expected results are explicit Tencent markdown-filter.test.ts vectors, not
    // predictions generated by the C# implementation. Supported markers remain visible.
    private static readonly (string Input, string Expected)[] MarkdownVectors =
    [
        ("hello world", "hello world"), ("", ""), ("line1\nline2\nline3", "line1\nline2\nline3"),
        ("你好世界", "你好世界"), ("Hello 你好 World 世界", "Hello 你好 World 世界"),
        ("![alt](http://example.com/img.png)", ""), ("before ![alt](url) after", "before  after"),
        ("![not an image] text", "![not an image] text"), ("![a](u1)![b](u2)", ""),
        ("![alt](url", "![alt](url"), ("~~deleted~~", "~~deleted~~"),
        ("**bold** and *italic*", "**bold** and *italic*"), ("*中文斜体*", "中文斜体"),
        ("*hello 你好*", "hello 你好"), ("*unclosed\nnext", "*unclosed\nnext"),
        ("3 * 4 = 12", "3 * 4 = 12"), ("3 *\nnext", "3 *\nnext"),
        ("***粗斜体文字***", "粗斜体文字"), ("***bold italic***", "***bold italic***"),
        ("***unclosed", "***unclosed"), ("_中文_", "中文"), ("_italic_", "_italic_"),
        ("___粗斜体___", "粗斜体"), ("___bold italic___", "___bold italic___"),
        ("*こんにちは*", "こんにちは"), ("*안녕하세요*", "안녕하세요"),
        ("中文 *English* 中文", "中文 *English* 中文"), ("*第1章*", "第1章"),
        ("##### 小标题", "小标题"), ("###### 小标题", "小标题"), ("#### 保留", "#### 保留"),
        ("> quote", "> quote"), ("---\n***\n___", "---\n***\n___"),
        ("use `fmt.Println` here", "use `fmt.Println` here"),
        ("结果如下：\n| A | B |\n|---|---|\n| 1 | 2 |\n完毕。", "结果如下：\n| A | B |\n|---|---|\n| 1 | 2 |\n完毕。")
    ];

    private static Task OfficialMarkdownVectorsAsync()
    {
        foreach (var (input, expected) in MarkdownVectors)
            Assert(MarkdownFormatting.ConvertToPlainText(input) == expected, "Official Markdown vector mismatch: " + JsonSerializer.Serialize(input));
        foreach (var fence in new[] { "before\n```js\nconst x = 1;\n```\nafter", "```\n**bold** *italic* ~~strike~~\n```\n", "```\ncode\n```" })
            Assert(MarkdownFormatting.ConvertToPlainText(fence) == fence, "Fences must preserve code and markers verbatim.");
        return Task.CompletedTask;
    }

    private static Task MarkdownStreamingBoundariesAsync()
    {
        foreach (var (input, expected) in MarkdownVectors)
        for (int chunk = 1; chunk <= 7; chunk++)
        {
            var filter = new StreamingMarkdownFilter(); var output = new StringBuilder();
            for (int start = 0; start < input.Length; start += chunk) output.Append(filter.Feed(input.Substring(start, Math.Min(chunk, input.Length - start))));
            output.Append(filter.Flush());
            Assert(output.ToString() == expected, $"Streaming chunk {chunk} mismatches official vector {JsonSerializer.Serialize(input)}.");
            Assert(filter.Flush() == "", "A second flush must not replay output.");
        }
        var code = new StreamingMarkdownFilter();
        Assert(code.Feed("```") + code.Feed("\ncode\n```\n") + code.Flush() == "```\ncode\n```\n", "The official split-fence vector must retain all code.");
        return Task.CompletedTask;
    }

    private static async Task MediaIntentPrecedesNetworkAsync()
    {
        Fixture? fixture = null;
        fixture = new Fixture(async request =>
        {
            var intent = (await fixture!.LoadAsync()).Outbox.Last();
            using var body = JsonDocument.Parse(request.Body);
            var msg = body.RootElement.GetProperty("msg");
            var item = msg.GetProperty("item_list")[0];
            Assert(intent.Status == "Sending" && intent.ClientId == msg.GetProperty("client_id").GetString(), "Media intent must be durable before HTTP.");
            Assert(msg.GetProperty("to_user_id").GetString() == "fixture-peer" && msg.GetProperty("context_token").GetString() == "source-context", "Each media reply must use the source context and bound peer.");
            Assert(intent.ContentSha256 == Hash(Encoding.UTF8.GetBytes(item.GetRawText())), "The durable media hash must identify exactly the wire descriptor.");
            return Reply("{}");
        });
        using (fixture)
        {
            for (int type = 2; type <= 5; type++)
            {
                await fixture.Runner.StageUpdatesAsync(new() { Messages = [Source("source-" + type)] });
                var key = fixture.Runner.State.Inbox.Last().Key;
                var receipt = await fixture.Runner.SendBoundItemAsync(Media(type), key);
                Assert(receipt.Status == "Sent", "A media API acknowledgment must persist Sent acceptance.");
            }
            Assert(fixture.Requests.Count == 4 && (await fixture.LoadAsync()).Outbox.All(r => r.Status == "Sent"), "Each media type must make one independent attempt.");
        }
    }

    private static async Task MediaSourceDedupeChecksContentAsync()
    {
        using var fixture = new Fixture();
        await fixture.Runner.StageUpdatesAsync(new() { Messages = [Source("media-source")] });
        var key = fixture.Runner.State.Inbox.Single().Key;
        var sent = await fixture.Runner.SendBoundItemAsync(Media(2), key);
        var same = await fixture.Runner.SendBoundItemAsync(Media(2), key);
        var changed = Media(2); changed.ImageItem!.Media!.EncryptQueryParam = "other-upload";
        await ThrowsAsync<InvalidOperationException>(() => fixture.Runner.SendBoundItemAsync(changed, key));
        Assert(same.ClientId == sent.ClientId && fixture.Requests.Count == 1, "The same source cannot silently reuse a receipt for a different media descriptor.");
    }

    private static async Task UnknownMediaIsNotRetransmittedAsync()
    {
        using var fixture = new Fixture(_ => Task.FromException<HttpResponseMessage>(new HttpRequestException("fixture lost acknowledgment")));
        await fixture.Runner.StageUpdatesAsync(new() { Messages = [Source("unknown-media-source")] });
        var key = fixture.Runner.State.Inbox.Single().Key;
        await ThrowsAsync<DeliveryUnknownException>(() => fixture.Runner.SendBoundItemAsync(Media(5), key));
        fixture.ReplaceState(await fixture.LoadAsync());
        await fixture.Runner.RecoverAsync();
        await ThrowsAsync<InvalidOperationException>(() => fixture.Runner.SendBoundItemAsync(Media(5), key));
        Assert(fixture.Requests.Count == 1 && fixture.Runner.State.Outbox.Single().Status == "Unknown", "Unknown media delivery must never automatically retransmit.");
    }

    private static async Task InvalidMediaFailsBeforeIntentAsync()
    {
        using var fixture = new Fixture(); await fixture.PersistAsync();
        var before = await fixture.FileBytesAsync();
        foreach (var invalid in new MessageItem[]
        {
            new() { Type = 99 }, new() { Type = 2 }, new() { Type = 2, ImageItem = new() { Media = new() { AesKey = "missing-query" } } },
            new() { Type = 4, FileItem = new() { Media = new() { EncryptQueryParam = "missing-key" } } },
            new() { Type = 1, TextItem = new() { Text = new string('x', 4001) } }
        }) await ThrowsAsync<ArgumentException>(() => fixture.Runner.SendBoundItemAsync(invalid));
        await ThrowsAsync<ArgumentNullException>(() => fixture.Runner.SendBoundItemAsync(null!));
        Assert(fixture.Requests.Count == 0 && fixture.Runner.State.Outbox.Count == 0 && Enumerable.SequenceEqual(before, await fixture.FileBytesAsync()), "Invalid local descriptors must not create Sending/Unknown receipts or touch HTTP/disk.");
    }

    private static async Task MutableItemsAreFrozenBeforeWaitingAsync()
    {
        using var fixture = new Fixture(interval: TimeSpan.FromMilliseconds(150));
        fixture.Runner.State.LastSendAttempt = DateTimeOffset.UtcNow;
        var original = Media(4);
        var send = fixture.Runner.SendBoundItemAsync(original);
        original.FileItem!.FileName = "mutated.txt"; original.FileItem.Media!.EncryptQueryParam = "mutated-query";
        var receipt = await send;
        using var body = JsonDocument.Parse(fixture.Requests.Single().Body);
        var wire = body.RootElement.GetProperty("msg").GetProperty("item_list")[0];
        Assert(wire.GetProperty("file_item").GetProperty("file_name").GetString() == "fixture.txt" &&
            wire.GetProperty("file_item").GetProperty("media").GetProperty("encrypt_query_param").GetString() == "fixture-query-4" &&
            receipt.ContentSha256 == Hash(Encoding.UTF8.GetBytes(wire.GetRawText())), "External mutation during the throttle wait must not change sent content or its durable hash.");
    }

    private static async Task TextAndItemApiShareHistoricalHashAsync()
    {
        using var fixture = new Fixture();
        await fixture.Runner.StageUpdatesAsync(new() { Messages = [Source("text-source")] });
        var key = fixture.Runner.State.Inbox.Single().Key;
        var text = new string('界', 4000);
        var original = await fixture.Runner.SendBoundTextAsync(text, key);
        var item = await fixture.Runner.SendBoundItemAsync(new() { Type = 1, TextItem = new() { Text = text } }, key);
        Assert(original.ContentSha256 == Hash(Encoding.UTF8.GetBytes(text)) && item.ClientId == original.ClientId && fixture.Requests.Count == 1,
            "Text hashes must remain compatible with existing receipts across text and generic-item APIs.");
    }

    private static async Task PayloadIdentityRemainsLocalAsync()
    {
        using var fixture = new Fixture();
        var source = Hash(Encoding.UTF8.GetBytes("media-file-business-source"));
        var payload = Hash(Encoding.UTF8.GetBytes("raw-file-and-type-metadata"));
        var sent = await fixture.Runner.SendBoundItemAsync(Media(4), source, payloadSha256: payload);
        var repeated = await fixture.Runner.SendBoundItemAsync(Media(4), source, payloadSha256: payload);
        await ThrowsAsync<InvalidOperationException>(() => fixture.Runner.SendBoundItemAsync(Media(4), source,
            payloadSha256: Hash(Encoding.UTF8.GetBytes("different-file-metadata"))));
        await ThrowsAsync<ArgumentException>(() => fixture.Runner.SendBoundItemAsync(Media(4), payloadSha256: "invalid"));
        Assert(sent.PayloadSha256 == payload && repeated.ClientId == sent.ClientId && (await fixture.LoadAsync()).Outbox.Single().PayloadSha256 == payload,
            "Raw-payload identity must survive persistence and constrain descriptor idempotency.");
        Assert(fixture.Requests.Count == 1 && !fixture.Requests.Single().Body.Contains("payload", StringComparison.OrdinalIgnoreCase),
            "Local payload fingerprints must never be added to the official wire request.");
    }

    private static async Task MarkdownJobResumesAfterCancelledWaitAsync()
    {
        using var fixture = new Fixture(interval: TimeSpan.FromMilliseconds(300));
        var sourceMessage = Source("markdown-source"); sourceMessage.ContextToken = "original-markdown-context";
        var laterMessage = Source("later-source"); laterMessage.ContextToken = "latest-unrelated-context";
        await fixture.Runner.StageUpdatesAsync(new() { Messages = [sourceMessage, laterMessage] });
        var source = fixture.Runner.State.Inbox[0].Key;
        var markdown = "##### " + new string('界', 4500);
        using var cancellation = new CancellationTokenSource();
        var partial = fixture.Runner.SendBoundMarkdownAsync(markdown, source, cancellation.Token);
        var deadline = DateTimeOffset.UtcNow.AddSeconds(5);
        while (fixture.Runner.State.MarkdownJobs.SingleOrDefault()?.CompletedChunks.Count is not >= 1 && !partial.IsCompleted && DateTimeOffset.UtcNow < deadline)
            await Task.Delay(5);
        Assert(!partial.IsCompleted && fixture.Runner.State.MarkdownJobs.Single().CompletedChunks.Count == 1, "The first acknowledged chunk must be archived before the second throttle wait.");
        cancellation.Cancel();
        await ThrowsAsync<OperationCanceledException>(() => partial);
        var first = fixture.Runner.State.MarkdownJobs.Single().CompletedChunks.Single();
        Assert(fixture.Requests.Count == 1 && fixture.Runner.State.Outbox.All(r => r.Status == "Sent"), "Cancellation before the next HTTP must not create an Unknown second attempt.");
        fixture.ReplaceState(await fixture.LoadAsync()); await fixture.Runner.RecoverAsync();
        var completed = await fixture.Runner.SendBoundMarkdownAsync(markdown, source);
        Assert(completed.Count == 2 && completed[0].ClientId == first.ClientId && fixture.Requests.Count == 2, "Resume must skip a previously acknowledged chunk and send only the remaining segment.");
        var wireChunks = fixture.Requests.Select(request =>
        {
            using var body = JsonDocument.Parse(request.Body); var msg = body.RootElement.GetProperty("msg");
            Assert(msg.GetProperty("context_token").GetString() == "original-markdown-context", "All Markdown chunks must retain the original inbound context.");
            return msg.GetProperty("item_list")[0].GetProperty("text_item").GetProperty("text").GetString()!;
        }).ToArray();
        Assert(wireChunks.Select(s => s.Length).SequenceEqual(new[] { 4000, 500 }) && string.Concat(wireChunks) == new string('界', 4500),
            "The persisted job must filter headings and send lossless UTF-16 chunks.");
    }

    private static async Task CompletedMarkdownSurvivesOutboxPruningAsync()
    {
        using var fixture = new Fixture();
        var source = Hash(Encoding.UTF8.GetBytes("archived-markdown-source"));
        var markdown = new string('a', 4000) + "tail";
        var original = await fixture.Runner.SendBoundMarkdownAsync(markdown, source);
        for (int index = 0; index < 126; index++) fixture.Runner.State.Outbox.Add(new()
        {
            ClientId = "fixture-prunable-" + index, Status = "Sent", ContentSha256 = Hash(Encoding.UTF8.GetBytes("other text")), AttemptedAt = DateTimeOffset.UtcNow
        });
        await fixture.PersistAsync();
        await fixture.Runner.SendBoundTextAsync("first prune"); await fixture.Runner.SendBoundTextAsync("second prune");
        Assert(original.All(r => fixture.Runner.State.Outbox.All(active => active.ClientId != r.ClientId)), "The general outbox must actually have pruned the old chunk receipts.");
        fixture.ReplaceState(await fixture.LoadAsync());
        var repeated = await fixture.Runner.SendBoundMarkdownAsync(markdown, source);
        Assert(repeated.Select(r => r.ClientId).SequenceEqual(original.Select(r => r.ClientId)) && fixture.Requests.Count == 4,
            "The Markdown job archive must prevent resending after ordinary outbox pruning and restart.");
        var directRepeat = await fixture.Runner.SendBoundTextAsync(new string('a', 4000), original[0].SourceKey);
        Assert(directRepeat.ClientId == original[0].ClientId && fixture.Requests.Count == 4, "Archived child-source receipts must retain global send idempotency.");
    }

    private static async Task UnknownMarkdownBlocksResumeAsync()
    {
        int requests = 0;
        using var fixture = new Fixture(_ => ++requests == 1 ? Task.FromResult(Reply("{}")) :
            Task.FromException<HttpResponseMessage>(new HttpRequestException("fixture uncertain second segment")));
        var source = Hash(Encoding.UTF8.GetBytes("unknown-markdown-source"));
        var markdown = new string('a', 4000) + "last segment";
        await ThrowsAsync<DeliveryUnknownException>(() => fixture.Runner.SendBoundMarkdownAsync(markdown, source));
        Assert(fixture.Runner.State.MarkdownJobs.Single().CompletedChunks.Count == 1 && fixture.Runner.State.Outbox.Last().Status == "Unknown",
            "Only confirmed Markdown chunks may be archived; the uncertain segment stays Unknown.");
        fixture.ReplaceState(await fixture.LoadAsync()); await fixture.Runner.RecoverAsync();
        var before = await fixture.FileBytesAsync();
        await ThrowsAsync<InvalidOperationException>(() => fixture.Runner.SendBoundMarkdownAsync(markdown, source));
        await ThrowsAsync<InvalidOperationException>(() => fixture.Runner.SendBoundMarkdownAsync(markdown + "changed", source));
        await ThrowsAsync<InvalidOperationException>(() => fixture.Runner.SendBoundMarkdownAsync("shorter input", source));
        Assert(requests == 2 && Enumerable.SequenceEqual(before, await fixture.FileBytesAsync()),
            "Unknown must block resume, and whole-job fingerprints must reject changed input or chunk count without new HTTP/disk writes.");
    }

    private static async Task MarkdownPreflightAndHistoryCapacityAsync()
    {
        using var fixture = new Fixture(); await fixture.PersistAsync();
        var source = Hash(Encoding.UTF8.GetBytes("preflight-markdown-source"));
        var before = await fixture.FileBytesAsync();
        await ThrowsAsync<ArgumentException>(() => fixture.Runner.SendBoundMarkdownAsync("valid", "invalid-source"));
        await ThrowsAsync<ArgumentException>(() => fixture.Runner.SendBoundMarkdownAsync("![only-image](https://example.invalid/image)", source));
        await ThrowsAsync<ArgumentException>(() => fixture.Runner.SendBoundMarkdownAsync(new string('x', 64 * 4000 + 1), source));
        Assert(fixture.Runner.State.MarkdownJobs.Count == 0 && fixture.Requests.Count == 0 && Enumerable.SequenceEqual(before, await fixture.FileBytesAsync()),
            "Markdown preflight must reject invalid keys, empty filtered content and over-limit jobs before saving or sending.");
        fixture.Runner.State.MarkdownJobs = Enumerable.Range(0, 64).Select(index => new MarkdownJob
        {
            SourceKey = Hash(Encoding.UTF8.GetBytes("retained-job-" + index)), ContentSha256 = Hash(Encoding.UTF8.GetBytes("retained content")), ChunkCount = 1, CreatedAt = DateTimeOffset.UtcNow
        }).ToList();
        await fixture.PersistAsync(); before = await fixture.FileBytesAsync();
        await ThrowsAsync<InvalidOperationException>(() => fixture.Runner.SendBoundMarkdownAsync("new job", source));
        Assert(fixture.Runner.State.MarkdownJobs.Count == 64 && fixture.Requests.Count == 0 && Enumerable.SequenceEqual(before, await fixture.FileBytesAsync()),
            "A full local job history must fail visibly and protect all previous fingerprints.");
    }

    private static async Task CorruptMarkdownJobFailsClosedAsync()
    {
        var corruptions = new Action<BotState>[]
        {
            state => state.MarkdownJobs = null!, state => state.MarkdownJobs.Add(null!),
            state => state.MarkdownJobs[0].SourceKey = "bad", state => state.MarkdownJobs[0].ContentSha256 = "bad",
            state => state.MarkdownJobs[0].ChunkCount = 0, state => state.MarkdownJobs[0].ChunkCount = 65,
            state => state.MarkdownJobs[0].CreatedAt = default, state => state.MarkdownJobs[0].CompletedChunks = null!,
            state => state.MarkdownJobs[0].CompletedChunks[0] = null!,
            state => state.MarkdownJobs[0].CompletedChunks[0].Status = "Unknown",
            state => state.MarkdownJobs[0].CompletedChunks[0].SourceKey = Hash(Encoding.UTF8.GetBytes("wrong-child-source")),
            state => state.MarkdownJobs[0].CompletedChunks[0].PayloadSha256 = "bad",
            state => state.Outbox[0].PayloadSha256 = "bad",
            state => state.MarkdownJobs.Add(new() { SourceKey = state.MarkdownJobs[0].SourceKey, ContentSha256 = state.MarkdownJobs[0].ContentSha256, ChunkCount = 1, CreatedAt = DateTimeOffset.UtcNow })
        };
        foreach (var corrupt in corruptions)
        {
            using var fixture = new Fixture();
            var source = Hash(Encoding.UTF8.GetBytes("corruption-job"));
            await fixture.Runner.SendBoundMarkdownAsync("fixture Markdown", source);
            corrupt(fixture.Runner.State); await fixture.PersistAsync(); var before = await fixture.FileBytesAsync();
            await ThrowsAsync<InvalidDataException>(() => fixture.Runner.RecoverAsync());
            await ThrowsAsync<InvalidDataException>(() => fixture.Runner.SendBoundMarkdownAsync("fixture Markdown", source));
            Assert(fixture.Requests.Count == 1 && Enumerable.SequenceEqual(before, await fixture.FileBytesAsync()), "Corrupt Markdown history must stop before recovery mutation or another HTTP request.");
        }
    }

    private static async Task SourceKeysCannotMixJobAndSingleSendAsync()
    {
        using var fixture = new Fixture();
        var singleSource = Hash(Encoding.UTF8.GetBytes("single-source"));
        await fixture.Runner.SendBoundTextAsync("direct bound send", singleSource);
        await ThrowsAsync<InvalidOperationException>(() => fixture.Runner.SendBoundMarkdownAsync("direct bound send", singleSource));
        var jobSource = Hash(Encoding.UTF8.GetBytes("job-source"));
        await fixture.Runner.SendBoundMarkdownAsync("Markdown send", jobSource);
        await ThrowsAsync<InvalidOperationException>(() => fixture.Runner.SendBoundTextAsync("Markdown send", jobSource));
        await ThrowsAsync<InvalidOperationException>(() => fixture.Runner.SendBoundItemAsync(Media(2), jobSource));
        Assert(fixture.Requests.Count == 2, "One business event key must not be reused for another send API and silently produce duplicate delivery.");
    }

    private static async Task LegacyFallbackKeysMigrateWithoutRedeliveryAsync()
    {
        foreach (bool recoverFirst in new[] { false, true })
        {
            using var fixture = new Fixture();
            var message = Source("unused-id"); message.MessageId = null;
            message.Items = [Media(2)];
            var currentJson = JsonSerializer.Serialize(message, ILinkClient.Json);
            // Before typed media DTOs, MessageItem.text_item was serialized as null,
            // followed by raw image_item extension data. This changes the fallback hash.
            var legacyJson = currentJson.Replace("\"type\":2,", "\"type\":2,\"text_item\":null,", StringComparison.Ordinal);
            var legacyKey = Hash(Encoding.UTF8.GetBytes(message.FromUserId + "\nfallback:" + legacyJson));
            var currentKey = PersistentBotRunner.MessageKey(message);
            Assert(legacyKey != currentKey, "The fixture must represent a real schema-dependent fallback hash change.");
            fixture.Runner.State.Inbox = [new() { Key = legacyKey, Message = message }];
            fixture.Runner.State.SeenKeys = [legacyKey];
            await fixture.PersistAsync(); fixture.ReplaceState(await fixture.LoadAsync());
            if (recoverFirst) await fixture.Runner.RecoverAsync();
            else await fixture.Runner.StageUpdatesAsync(new() { Messages = [message] });
            Assert(fixture.Runner.State.Inbox.Count == 1 && fixture.Runner.State.Inbox[0].Key == legacyKey && fixture.Runner.State.SeenKeys.Contains(currentKey),
                "Migration must preserve the original handler idempotency key and durably add the new fallback alias.");
            int handled = 0;
            await fixture.Runner.DrainInboxAsync((entry, _) =>
            {
                Assert(entry.Key == legacyKey, "Recovery cannot change a previously delivered application key."); handled++; return Task.CompletedTask;
            });
            fixture.ReplaceState(await fixture.LoadAsync());
            await fixture.Runner.StageUpdatesAsync(new() { Messages = [message] });
            Assert(handled == 1 && fixture.Runner.State.Inbox.Count == 0 && fixture.Requests.Count == 0,
                "A migrated optional-ID message must not be redelivered after acknowledgment or restart.");
        }
    }

    private static MessageItem Media(int type)
    {
        var reference = new CdnMedia { EncryptQueryParam = "fixture-query-" + type, AesKey = Convert.ToBase64String(Encoding.ASCII.GetBytes("0123456789abcdef")), EncryptType = 1 };
        return type switch
        {
            2 => new() { Type = 2, ImageItem = new() { Media = reference, MidSize = 128 } },
            3 => new() { Type = 3, VoiceItem = new() { Media = reference, EncodeType = 6, SampleRate = 24000, Playtime = 1000 } },
            4 => new() { Type = 4, FileItem = new() { Media = reference, FileName = "fixture.txt", Length = "128", Md5 = new string('0', 32) } },
            5 => new() { Type = 5, VideoItem = new() { Media = reference, VideoSize = 128, PlayLength = 1000 } },
            _ => throw new ArgumentOutOfRangeException(nameof(type))
        };
    }
    private static InboundMessage Source(string id) => new()
    {
        MessageId = id, FromUserId = "fixture-peer", ToUserId = "fixture-bot", MessageType = 1,
        MessageState = 2, ContextToken = "source-context", Items = [new() { Type = 1, TextItem = new() { Text = "source" } }]
    };
    private static string Hash(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes));
    private static HttpResponseMessage Reply(string json) => new(HttpStatusCode.OK) { Content = new StringContent(json, Encoding.UTF8, "application/json") };
    private static void Assert(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
    private static void Throws<T>(Action action) where T : Exception
    {
        try { action(); } catch (T) { return; }
        throw new InvalidOperationException($"Expected {typeof(T).Name}.");
    }
    private static async Task<T> ThrowsAsync<T>(Func<Task> action) where T : Exception
    {
        try { await action(); } catch (T error) { return error; }
        throw new InvalidOperationException($"Expected {typeof(T).Name}.");
    }
    private sealed record Request(string Body);
    private sealed class Handler(Func<Request, Task<HttpResponseMessage>> response) : HttpMessageHandler
    {
        public List<Request> Requests { get; } = [];
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Assert(request.RequestUri!.AbsolutePath == "/ilink/bot/sendmessage", "Only fixture media sends are expected.");
            var observed = new Request(await request.Content!.ReadAsStringAsync(cancellationToken)); Requests.Add(observed);
            return await response(observed);
        }
    }
    private sealed class Fixture : IDisposable
    {
        private readonly string directory = Path.Combine(Path.GetTempPath(), "weixin-media-model-" + Guid.NewGuid().ToString("N"));
        private readonly Handler handler;
        private readonly HttpClient http;
        private readonly ILinkClient client;
        private readonly TimeSpan interval;
        private string FilePath => Path.Combine(directory, "state.bin");
        public StateVault Vault { get; }
        public PersistentBotRunner Runner { get; private set; }
        public List<Request> Requests => handler.Requests;
        public Fixture(Func<Request, Task<HttpResponseMessage>>? response = null, TimeSpan? interval = null)
        {
            handler = new Handler(response ?? (_ => Task.FromResult(Reply("{}"))));
            http = new HttpClient(handler) { Timeout = Timeout.InfiniteTimeSpan }; client = new ILinkClient(httpClient: http);
            this.interval = interval ?? TimeSpan.Zero;
            Vault = new StateVault(FilePath);
            Runner = CreateRunner(new() { Session = new() { BotToken = "fixture-token", BotId = "fixture-bot", UserId = "fixture-peer", ContextToken = "latest-context" } });
        }
        private PersistentBotRunner CreateRunner(BotState state) => new(client, Vault, state) { MinimumSendInterval = interval };
        public void ReplaceState(BotState state) => Runner = CreateRunner(state);
        public Task PersistAsync() => Vault.SaveAsync(Runner.State);
        public async Task<BotState> LoadAsync() => await Vault.LoadAsync<BotState>() ?? throw new InvalidOperationException("Missing fixture state.");
        public Task<byte[]> FileBytesAsync() => File.ReadAllBytesAsync(FilePath);
        public void Dispose()
        {
            Vault.Dispose(); client.Dispose(); http.Dispose();
            var target = Path.GetFullPath(directory);
            var root = Path.GetFullPath(Path.GetTempPath()).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            if (!target.StartsWith(root, StringComparison.OrdinalIgnoreCase) || !Path.GetFileName(target).StartsWith("weixin-media-model-", StringComparison.Ordinal)) throw new InvalidOperationException("Unexpected fixture path.");
            if (Directory.Exists(target)) Directory.Delete(target, recursive: true);
        }
    }
}
