namespace SilkCodec.NET;

/// <summary>Raised when Silk v3 encoding or decoding fails.</summary>
public sealed class SilkCodecException : Exception
{
    public SilkCodecException(string message) : base(message) { }
    public SilkCodecException(string message, Exception inner) : base(message, inner) { }
}
