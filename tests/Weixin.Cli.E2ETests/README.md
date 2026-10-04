# Published CLI process E2E

This console test project launches a published `weixin.exe` with `ProcessStartInfo`.
It uses real Windows DPAPI, real stdin/files, real process lifetime locks and an actual
forced process termination during a persisted send intent. The HTTP service is an
explicitly simulated, fail-closed fixture transport; these tests do **not** verify
real WeChat delivery, account safety, or long-term availability.

```powershell
dotnet run --project tests/Weixin.Cli.E2ETests -- --exe <absolute-published-exe> --output <new-absolute-report-directory>
```

Append `--case <exact-case-name>` to reproduce one case. The summary records this
selection explicitly; a selected-case success never counts as a full suite pass.

Every run creates new `fixture-<UUID>` directories under the output directory.
Each contains its own `.weixin-offline-test` sentinel, versioned `fixture-001.json` files, DPAPI state,
`fixture-trace.jsonl`, and process logs. It never uses the default account state or
`artifacts/live-e2e`. `tests-summary.json` records each named case, process exit
codes, durations, argument paths and SHA-256 hashes of source and launched files.
Fixture versions are retained for every subprocess; traces include the child PID
and fixture filename so restart cases remain reproducible after later steps.

The media cases exercise image/video/file/audio and explicitly selected MP3/SILK
voice metadata, AES-128-ECB/PKCS7 upload bytes, download bytes, retries, and a real
process kill during media chat-send. Synthetic codec fixtures only prove protocol
transport behavior; playable media and native voice require real-device checks.
Markdown cases use hard-coded official-filter results and a 3999-unit first chunk
followed by an intact emoji, and verify completed/unknown chunk replay behavior.
Typing cases launch the executable for start/stop, empty or non-JSON HTTP success,
explicit config acknowledgement, cached config, authentication rejection, and
cancellation while the start request is actually pending. Codec command cases run
the managed codec from the published directory for synthetic PCM/WAV/SILK
conversion without state/fixture arguments; an inbound SILK download also verifies
automatic WAV conversion while preserving the original. These are simulated/local
checks, independently of real-phone typing or native-voice delivery.
Inbound voice regression cases use locally synthesized SILK under `encode_type=6`,
`encode_type=4`, and an omitted `encode_type`, comparing automatic WAV bytes against
the published `decode-voice` command. An invented non-SILK payload must retain its
raw file, report decode failure, and create no WAV. No private phone recording is
copied into distributed fixtures.

Fixtures have `formatVersion: 1`, `testId` equal to the directory name, and ordered
`steps`. A step specifies `method`, `path`, exact `query`, optional recursive JSON
`bodySubset`, `requireBearer`, `status`, and response `body` or `rawBody`. `delayMs:
-1` blocks until canceled. `fault: "disconnect"` simulates a lost connection.
`waitMarker` writes a simple filename when the request is observed, letting tests
kill the child after its `Sending` intent is durably saved. No request falls back
to the network. Credential values must start with `fixture-`, and headers are
never written to the trace. Deliberately invalid credential fixtures are rejected
before any request and contain invented test strings only.

CDN steps set `host: "novac2c.cdn.weixin.qq.com"`, `path: "/c2c/upload"` or
`"/c2c/download"`; they categorically reject `Authorization`. Upload steps can
specify `expectedPlaintextBase64` and `aesKeyStep` referencing a previous
`getuploadurl` request. `filekey: "$uploadFileKey:0"` substitutes the captured
random upload key only for matching. `assertSentAesKeyStep: 0` proves the sent media
descriptor reuses that same AES key. Ephemeral keys stay in memory, are cleared on
transport disposal, and never enter logs. Only fixture-declared request subsets,
generated message IDs, assertion results and byte counts/hashes enter the trace.
`bodyBase64` returns binary fixture bytes, while `responseHeaders` permits the
fixture-prefixed `x-encrypted-param` CDN header.
