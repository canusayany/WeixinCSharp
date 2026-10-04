# Project tools

The build and release helpers are C# programs. Run them from the repository root
with the .NET 10 SDK. They do not call Python, PowerShell, Node or a native codec.

```text
dotnet run --project src/Weixin.Tools -- test-all --output artifacts/tests/my-run
dotnet run --project src/Weixin.Tools -- render-report
dotnet run --project src/Weixin.Tools -- package --skip-build
dotnet run --project src/Weixin.Tools -- audit --state artifacts/live-e2e/binding.dpapi --private-media artifacts/live-e2e/received-u8v --output artifacts/my-delivery-audit.json
```

`test-all` restores the locked dependencies, builds in Release, runs the unit,
managed codec and tool tests, publishes the Windows application, runs the offline
process tests against that exact executable, and checks the lock files again.
The test output directory and `artifacts/win-x64-{version}` must both be new. If a
published directory already exists, move it aside before starting another build.

`render-report` reads `docs/REPORT.md` and writes an offline `docs/REPORT.html`.
Use `--input` and `--output` for other files. It escapes HTML and accepts HTTP,
HTTPS and relative links; it does not run scripts.

`package` normally runs the tests first. `--skip-build` packages the existing
versioned publish output. The source ZIP includes C# code, tests, public reports
and static research/licensing snapshots. The Windows ZIP contains the .NET
application and the public reports. Neither ZIP takes files from the private
`artifacts` state/media directories; the runtime ZIP rejects legacy scripts,
WASM and Node files.

`audit` checks those two ZIPs. Supplying `--state` loads the current Windows user's
encrypted state through `StateVault` and scans text for the known credentials and
account identifiers. Supplying `--private-media` also compares the file bytes by
SHA-256. It reports counts and package hashes without printing the supplied
secrets. Omitted inputs are recorded as unchecked; a passing audit only covers
the inputs supplied to that run. Choose a new output JSON name for every audit.

Files named `research/*provenance.json` are included in the source ZIP alongside
the static research snapshots. Public licenses under `docs/` are included in both
ZIPs; legacy codec licenses under `research/legacy-codec-licenses` stay in the
source ZIP.
