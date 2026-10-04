namespace SilkCodec.NET;

/// <summary>Options for Silk v3 encoding.</summary>
public sealed class SilkEncoderOptions
{
    /// <summary>Input PCM sample rate in Hz. 8000, 12000, 16000, 24000, 32000, 44100 or 48000.</summary>
    public int SampleRate
    {
        get => field;
        set => field = value;
    } = 24000;

    /// <summary>Maximum internal sample rate in Hz. 8000, 12000, 16000 or 24000.</summary>
    public int MaxInternalSampleRate
    {
        get => field;
        set => field = value;
    } = 24000;

    /// <summary>Target bitrate in bits per second.</summary>
    public int BitRate
    {
        get => field;
        set => field = value;
    } = 25000;

    /// <summary>Packet duration in milliseconds: 20, 40, 60, 80 or 100.</summary>
    public int PacketDurationMs
    {
        get => field;
        set => field = value;
    } = 20;

    /// <summary>Complexity 0 (lowest) to 2 (highest).</summary>
    public int Complexity
    {
        get => field;
        set => field = value;
    } = 2;

    /// <summary>Write the Tencent/WeChat leading 0x02 byte.</summary>
    public bool Tencent
    {
        get => field;
        set => field = value;
    }

    /// <summary>Uplink packet loss estimate, 0-100.</summary>
    public int PacketLossPercent
    {
        get => field;
        set => field = value;
    }

    /// <summary>Enable discontinuous transmission.</summary>
    public bool UseDtx
    {
        get => field;
        set => field = value;
    }

    /// <summary>Enable in-band FEC.</summary>
    public bool UseInBandFec
    {
        get => field;
        set => field = value;
    }
}

/// <summary>Options for Silk v3 decoding.</summary>
public sealed class SilkDecoderOptions
{
    /// <summary>Requested PCM output sample rate in Hz. 8000-48000.</summary>
    public int SampleRate
    {
        get => field;
        set => field = value;
    } = 24000;

    /// <summary>Simulated packet loss percentage used by the concealment path.</summary>
    public float PacketLossPercent
    {
        get => field;
        set => field = value;
    }
}
