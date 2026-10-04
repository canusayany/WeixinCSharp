param([string]$OutputDirectory = 'artifacts/tests/latest')
$ErrorActionPreference = 'Stop'
$workspacePath = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
Push-Location -LiteralPath $workspacePath
try {
    if (-not (Test-Path -LiteralPath 'runtime/voice/node.exe')) {
        throw 'Voice runtime missing. Run scripts/Restore-VoiceRuntime.ps1 before the full test pipeline.'
    }
    $testOutputPath = [IO.Path]::GetFullPath($OutputDirectory)
    [IO.Directory]::CreateDirectory($testOutputPath) | Out-Null
    & dotnet restore WeixinAssistant.slnx --locked-mode --nologo
    if ($LASTEXITCODE -ne 0) { throw 'Locked restore failed.' }
    & dotnet build WeixinAssistant.slnx -c Release --no-restore --nologo
    if ($LASTEXITCODE -ne 0) { throw 'Build failed.' }
    & dotnet run --project tests/Weixin.Protocol.Tests -c Release --no-build -- --output (Join-Path $testOutputPath 'unit-summary.json')
    if ($LASTEXITCODE -ne 0) { throw 'Unit tests failed.' }
    & dotnet run --project tests/Weixin.VoiceCodec.Tests -c Release --no-build -- --runtime (Join-Path $workspacePath 'runtime/voice') --output (Join-Path $testOutputPath 'voice-codec')
    if ($LASTEXITCODE -ne 0) { throw 'Actual local SILK codec tests failed.' }
    & dotnet publish src/Weixin.Cli -c Release -r win-x64 --self-contained true --no-restore -o artifacts/win-x64 --nologo
    if ($LASTEXITCODE -ne 0) { throw 'Publish failed.' }
    & dotnet run --project tests/Weixin.Cli.E2ETests -c Release --no-build -- --exe (Join-Path $workspacePath 'artifacts/win-x64/weixin.exe') --output (Join-Path $testOutputPath 'cli-e2e')
    if ($LASTEXITCODE -ne 0) { throw 'Published CLI end-to-end tests failed.' }
    # Recheck that publish did not change locked dependency graphs.
    & dotnet restore WeixinAssistant.slnx --locked-mode --nologo
    if ($LASTEXITCODE -ne 0) { throw 'Post-publish locked restore failed.' }
    Write-Output ('Verified unit + offline CLI E2E results: ' + $testOutputPath)
} finally { Pop-Location }
