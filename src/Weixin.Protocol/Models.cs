using System.Text.Json;
using System.Text.Json.Serialization;

namespace Weixin.Protocol;

public sealed class QrCode
{
    [JsonPropertyName("qrcode")] public string Code { get; set; } = "";
    [JsonPropertyName("qrcode_img_content")] public string Content { get; set; } = "";
}

public sealed class QrStatus
{
    [JsonPropertyName("status")] public string Status { get; set; } = "";
    [JsonPropertyName("bot_token")] public string? BotToken { get; set; }
    [JsonPropertyName("ilink_bot_id")] public string? BotId { get; set; }
    [JsonPropertyName("ilink_user_id")] public string? UserId { get; set; }
    [JsonPropertyName("baseurl")] public string? BaseUrl { get; set; }
    [JsonPropertyName("redirect_host")] public string? RedirectHost { get; set; }
}

public sealed class ApiResult
{
    [JsonPropertyName("ret")] public int? Ret { get; set; }
    [JsonPropertyName("errcode")] public int? ErrorCode { get; set; }
}

public sealed class Updates
{
    [JsonPropertyName("get_updates_buf")] public string? Cursor { get; set; }
    [JsonPropertyName("longpolling_timeout_ms")] public int? TimeoutMilliseconds { get; set; }
    [JsonPropertyName("msgs")] public List<InboundMessage> Messages { get; set; } = [];
}

public sealed class InboundMessage
{
    // JSON number IDs can exceed Int64 and JavaScript's safe integer range.
    [JsonPropertyName("message_id"), JsonConverter(typeof(WireIdConverter))]
    public string? MessageId { get; set; }
    [JsonPropertyName("seq"), JsonConverter(typeof(WireIdConverter))]
    public string? Sequence { get; set; }
    [JsonPropertyName("client_id")] public string? ClientId { get; set; }
    [JsonPropertyName("from_user_id")] public string? FromUserId { get; set; }
    [JsonPropertyName("to_user_id")] public string? ToUserId { get; set; }
    [JsonPropertyName("group_id")] public string? GroupId { get; set; }
    [JsonPropertyName("message_type")] public int? MessageType { get; set; }
    [JsonPropertyName("message_state")] public int? MessageState { get; set; }
    [JsonPropertyName("create_time_ms")] public long? CreatedMilliseconds { get; set; }
    [JsonPropertyName("context_token")] public string? ContextToken { get; set; }
    [JsonPropertyName("item_list")] public List<MessageItem>? Items { get; set; }
    [JsonExtensionData] public Dictionary<string, JsonElement>? OtherFields { get; set; }

    [JsonIgnore] public string Text
    {
        get
        {
            ValidateStructure();
            return string.Join("\n", (Items ?? []).Where(i => i.Type == 1)
                .Select(i => i.TextItem?.Text).Where(t => t is not null));
        }
    }

    internal void ValidateStructure()
    {
        if (Items is not null && Items.Any(item => item is null))
            throw new InvalidDataException("消息 item_list 包含空项。");
    }

    [JsonIgnore] public string VoiceTranscript
    {
        get
        {
            ValidateStructure();
            return string.Join("\n", (Items ?? []).Where(i => i.Type == 3)
                .Select(i => i.VoiceItem?.Text).Where(t => t is not null));
        }
    }
}

public sealed class MessageItem
{
    [JsonPropertyName("type")] public int Type { get; set; }
    [JsonPropertyName("text_item"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public TextItem? TextItem { get; set; }
    [JsonPropertyName("image_item"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public ImageItem? ImageItem { get; set; }
    [JsonPropertyName("voice_item"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public VoiceItem? VoiceItem { get; set; }
    [JsonPropertyName("file_item"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public FileItem? FileItem { get; set; }
    [JsonPropertyName("video_item"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public VideoItem? VideoItem { get; set; }
    // Preserve unsupported media descriptors; never silently treat them as text.
    [JsonExtensionData] public Dictionary<string, JsonElement>? OtherFields { get; set; }
}

public sealed class TextItem
{
    [JsonPropertyName("text"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? Text { get; set; }
    [JsonExtensionData] public Dictionary<string, JsonElement>? OtherFields { get; set; }
}

public sealed class CdnMedia
{
    [JsonPropertyName("encrypt_query_param"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? EncryptQueryParam { get; set; }
    [JsonPropertyName("aes_key"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? AesKey { get; set; }
    [JsonPropertyName("encrypt_type"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public int? EncryptType { get; set; }
    [JsonPropertyName("full_url"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? FullUrl { get; set; }
    [JsonExtensionData] public Dictionary<string, JsonElement>? OtherFields { get; set; }
}

public sealed class ImageItem
{
    [JsonPropertyName("media"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public CdnMedia? Media { get; set; }
    [JsonPropertyName("thumb_media"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public CdnMedia? ThumbMedia { get; set; }
    [JsonPropertyName("aeskey"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? AesKey { get; set; }
    [JsonPropertyName("url"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? Url { get; set; }
    [JsonPropertyName("mid_size"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public long? MidSize { get; set; }
    [JsonPropertyName("thumb_size"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public long? ThumbSize { get; set; }
    [JsonPropertyName("thumb_height"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public int? ThumbHeight { get; set; }
    [JsonPropertyName("thumb_width"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public int? ThumbWidth { get; set; }
    [JsonPropertyName("hd_size"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public long? HdSize { get; set; }
    [JsonExtensionData] public Dictionary<string, JsonElement>? OtherFields { get; set; }
}

public sealed class VoiceItem
{
    [JsonPropertyName("media"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public CdnMedia? Media { get; set; }
    [JsonPropertyName("encode_type"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public int? EncodeType { get; set; }
    [JsonPropertyName("bits_per_sample"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public int? BitsPerSample { get; set; }
    [JsonPropertyName("sample_rate"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public int? SampleRate { get; set; }
    [JsonPropertyName("playtime"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public long? Playtime { get; set; }
    [JsonPropertyName("text"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? Text { get; set; }
    [JsonExtensionData] public Dictionary<string, JsonElement>? OtherFields { get; set; }
}

public sealed class FileItem
{
    [JsonPropertyName("media"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public CdnMedia? Media { get; set; }
    [JsonPropertyName("file_name"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? FileName { get; set; }
    [JsonPropertyName("md5"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? Md5 { get; set; }
    [JsonPropertyName("len"), JsonConverter(typeof(WireIdConverter)), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? Length { get; set; }
    [JsonExtensionData] public Dictionary<string, JsonElement>? OtherFields { get; set; }
}

public sealed class VideoItem
{
    [JsonPropertyName("media"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public CdnMedia? Media { get; set; }
    [JsonPropertyName("video_size"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public long? VideoSize { get; set; }
    [JsonPropertyName("play_length"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public long? PlayLength { get; set; }
    [JsonPropertyName("video_md5"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? VideoMd5 { get; set; }
    [JsonPropertyName("thumb_media"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public CdnMedia? ThumbMedia { get; set; }
    [JsonPropertyName("thumb_size"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public long? ThumbSize { get; set; }
    [JsonPropertyName("thumb_height"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public int? ThumbHeight { get; set; }
    [JsonPropertyName("thumb_width"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public int? ThumbWidth { get; set; }
    [JsonExtensionData] public Dictionary<string, JsonElement>? OtherFields { get; set; }
}

public sealed class WireIdConverter : JsonConverter<string>
{
    public override string? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.String) return reader.GetString();
        if (reader.TokenType == JsonTokenType.Null) return null;
        if (reader.TokenType == JsonTokenType.Number)
        {
            using var doc = JsonDocument.ParseValue(ref reader);
            return doc.RootElement.GetRawText();
        }
        throw new JsonException("Invalid message identifier type.");
    }
    public override void Write(Utf8JsonWriter writer, string value, JsonSerializerOptions options) => writer.WriteStringValue(value);
}

public sealed class ApiException : Exception
{
    public int? HttpStatus { get; }
    public int? Ret { get; }
    public int? ErrorCode { get; }
    public bool IsTransient { get; }
    public TimeSpan? RetryAfter { get; }
    public bool SessionExpired => HttpStatus is 401 or 403 || Ret == -14 || ErrorCode == -14;
    public ApiException(string safeMessage, int? httpStatus = null, int? ret = null,
        int? errorCode = null, bool transient = false, TimeSpan? retryAfter = null) : base(safeMessage)
    { HttpStatus = httpStatus; Ret = ret; ErrorCode = errorCode; IsTransient = transient; RetryAfter = retryAfter; }
}

public sealed class DeliveryUnknownException : Exception
{
    public DeliveryUnknownException() : base("发送结果未知，可能已送达。请先在微信中核对；程序不会自动重发。") { }
}
