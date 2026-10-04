# Local SILK runtime

This directory is a self-contained Windows x64 local codec dependency. No npm install,
internet connection, account binding, shell or FFmpeg is required at runtime.

- Node.js v24.19.0: `node.exe`, complete upstream `Node-LICENSE.txt`.
- npm `silk-wasm` 3.7.1: verified original package under `silk-wasm/`;
  production bridge imports `silk-wasm/lib/index.mjs` and `silk.wasm`.
- Fixed `codec-bridge.mjs`: binary stdin/stdout IPC, no media-selected filename,
  module, argument, code or URL. The C# wrapper starts only this bridge and uses
  Node's permission mode with read permission for this runtime directory.

Tencent's pinned openclaw-weixin source declares `silk-wasm ^3.7.1` and uses its
`decode` API for received voice. Its public dependency also exposes `encode`;
our local preparation uses that API as an extension. These codec facts do not
establish that WeChat supports outgoing native voice bubbles. Real native-voice
requests were accepted by the API but the user did not receive them on the phone.

`VoiceCodec` defaults to mono signed PCM16 little-endian, 24000Hz, 60 seconds,
16MiB input/output and a 30-second owned-process timeout. WAV input must be
uncompressed RIFF PCM16 mono 24000Hz. Other raw PCM sample rates supported by the
upstream library are available through the SDK, with the same local guards.
The final partial 20ms packet is padded with silence. Inputs shorter than 40ms
are padded to two packets because the pinned decoder prefetches two packets;
the returned duration includes that padding.

The decoder accepts Tencent `02 + #!SILK_V3` headers and complete packets of
1..1250 bytes (`MAX_BYTES_PER_FRAME 250 * MAX_INPUT_FRAMES 5` in pinned upstream
`common.h`). It validates all framing, supplies an explicit signed -1 terminal
prefix to the upstream decoder, and checks `getDuration` before decoding. Packet
count times 20ms is only a minimum-duration estimate: a packet can contain up to
five internal frames. Resource preflight therefore uses five times that estimate;
the returned actual duration comes from the decoded PCM byte count, and actual
duration/output limits are checked again. This intentionally conservative guard
can reject otherwise valid long/high-rate input when the configured output limit
is too small. The underlying upstream WASM is unmodified. Input/result arrays
belong to the caller; private bridge buffers are cleared and each operation exits
its own process. WASM internal buffers are reclaimed with that process.

The source and runtime provenance is in `provenance.json`; original download
archives, checksum list, registry metadata and pinned SDK source are retained in
`research/runtime-dependencies`, outside the production runtime.

License files must accompany redistribution:

- `silk-wasm/LICENSE`: npm package MIT, copyright 2024 idranme.
- `Skype-SILK-LICENSE.txt`: Russell wrapper's 2016 two-condition BSD license.
  This filename is retained for source provenance; it is **not** the Skype SDK license.
- `Skype-SILK-SDK-LICENSE.txt`: complete original Skype 2006–2012 SDK API header,
  containing its three-condition BSD license, disclaimer and no patent grant.
- `wav-file-decoder-LICENSE.md`: Christian d'Heureuse 2022 MIT. The npm package
  bundles this decoder and declares `^1.0.3`. The pinned tree has no dependency
  lockfile; the exact bundled resolution is not asserted.
- `Node-LICENSE.txt`: complete original Node distribution license and notices.

Independent tests: `dotnet run --project tests/Weixin.VoiceCodec.Tests -- --runtime
<absolute runtime/voice> --output <new absolute evidence directory>`. They use
synthetic audio, compare bytes against the pinned official encode/decode APIs,
exercise guards, and observe cancellation of an actual owned Node process.
