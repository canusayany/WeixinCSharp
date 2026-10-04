# Local codec tests

Run with the repository .NET 10 SDK:

```powershell
dotnet run --project tests/Weixin.VoiceCodec.Tests -- --output artifacts/tests/voice-codec-new-run
```

The output directory must not exist. This project references the production
Protocol and Silk assemblies. It generates synthetic PCM and reads fixed synthetic
SILK vectors from `Golden`, with SHA-256 checks and independent Skype SDK PCM
hashes where bit-exact comparison is established. The fixed vectors include
20/40/60/80/100 ms packets; no external codec is launched during these tests.

Published CLI E2E tests separately call the actual executable. No account state
is read and no messages or network requests are sent. `tests-summary.json` records
named outcomes, durations and assembly/manifest hashes. This is local codec
evidence, not WeChat delivery evidence.
