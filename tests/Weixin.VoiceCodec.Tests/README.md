# Local codec tests

Run with the repository .NET 10 SDK:

```powershell
dotnet run --project tests/Weixin.VoiceCodec.Tests -- --runtime "$PWD/runtime/voice" --output "$PWD/artifacts/tests/voice-codec-new-run"
```

The output directory must not exist. All audio is synthesized locally. The
project compiles the exact production `VoiceCodec.cs` as a linked file so it can
run without mutating the shared Protocol build directories. Published CLI E2E
tests separately verify that the same runtime is copied and works through the
actual executable. No account state is read, no messages are sent and no network
is used. `tests-summary.json` records named outcomes, durations and source/runtime
hashes; this is local codec evidence, not phone delivery evidence.
