#requires -Version 5.1
<#
.SYNOPSIS
Restore the pinned Windows x64 Node binary used by the local voice codec.
.EXAMPLE
./scripts/Restore-VoiceRuntime.ps1
.EXAMPLE
./scripts/Restore-VoiceRuntime.ps1 -ArchivePath ./research/runtime-dependencies/node-v24.19.0-win-x64.zip
#>
[CmdletBinding()]
param(
    [Parameter()]
    [ValidateNotNullOrEmpty()]
    [string]$ArchivePath
)

$ErrorActionPreference = 'Stop'
$workspacePath = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$runtimePath = Join-Path $workspacePath 'runtime\voice'
$manifestPath = Join-Path $runtimePath 'provenance.json'
$nodePath = Join-Path $runtimePath 'node.exe'
$licensePath = Join-Path $runtimePath 'Node-LICENSE.txt'

function Get-ManifestFile([object]$Manifest, [string]$Name) {
    $matches = @($Manifest.files | Where-Object { $_.path -ceq $Name })
    if ($matches.Count -ne 1) { throw "Expected one '$Name' entry in provenance.json." }
    $record = $matches[0]
    if ([string]$record.sha256 -notmatch '^[0-9A-Fa-f]{64}$' -or [long]$record.sizeBytes -le 0) {
        throw "Invalid size or SHA256 for '$Name' in provenance.json."
    }
    return $record
}

function Assert-ExpectedFile([string]$Path, [object]$Record) {
    $file = Get-Item -LiteralPath $Path
    if ($file.PSIsContainer -or $file.Length -ne [long]$Record.sizeBytes) {
        throw "File size does not match provenance.json: $Path"
    }
    $actualHash = (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash
    if ($actualHash -ine [string]$Record.sha256) {
        throw "File SHA256 does not match provenance.json: $Path"
    }
}

function Copy-VerifiedEntry(
    [object]$Archive,
    [string]$EntryName,
    [string]$Destination,
    [object]$Record
) {
    $matches = @($Archive.Entries | Where-Object { $_.FullName -ceq $EntryName })
    if ($matches.Count -ne 1 -or $matches[0].Length -ne [long]$Record.sizeBytes) {
        throw "Missing, duplicated or incorrectly sized ZIP entry: $EntryName"
    }
    $inputStream = $null
    $outputStream = $null
    try {
        $inputStream = $matches[0].Open()
        $outputStream = [IO.File]::Open($Destination, [IO.FileMode]::CreateNew, [IO.FileAccess]::Write, [IO.FileShare]::None)
        $inputStream.CopyTo($outputStream)
    } finally {
        if ($null -ne $outputStream) { $outputStream.Dispose() }
        if ($null -ne $inputStream) { $inputStream.Dispose() }
    }
    Assert-ExpectedFile $Destination $Record
}

$manifest = Get-Content -LiteralPath $manifestPath -Raw -Encoding UTF8 | ConvertFrom-Json
$nodeVersion = [string]$manifest.node.version
if ($manifest.schemaVersion -ne 1 -or $nodeVersion -notmatch '^v[0-9]+\.[0-9]+\.[0-9]+$' -or
    $manifest.node.platform -cne 'win-x64' -or $manifest.node.license -cne 'Node-LICENSE.txt') {
    throw 'Unsupported Node provenance. This script restores only the pinned Windows x64 distribution.'
}
$archiveHash = [string]$manifest.node.archiveSha256
if ($archiveHash -notmatch '^[0-9A-Fa-f]{64}$') { throw 'Invalid Node archive SHA256 in provenance.json.' }
$archiveName = "node-$nodeVersion-win-x64.zip"
$expectedUrl = "https://nodejs.org/dist/$nodeVersion/$archiveName"
if ([string]$manifest.node.url -cne $expectedUrl) {
    throw 'Node download URL must be the exact HTTPS nodejs.org distribution URL for the pinned version.'
}
$nodeRecord = Get-ManifestFile $manifest 'node.exe'
$licenseRecord = Get-ManifestFile $manifest 'Node-LICENSE.txt'

# A source checkout must already contain the original license. Never replace it
# with a downloaded copy, even if the binary needs restoring.
Assert-ExpectedFile $licensePath $licenseRecord
if (Test-Path -LiteralPath $nodePath) {
    Assert-ExpectedFile $nodePath $nodeRecord
    Write-Output "Node $nodeVersion is already verified: $nodePath"
    return
}

$downloadsPath = Join-Path $workspacePath 'artifacts\runtime-downloads'
[IO.Directory]::CreateDirectory($downloadsPath) | Out-Null
$tempPath = Join-Path $downloadsPath ('node-restore-' + [Guid]::NewGuid().ToString('N'))
[IO.Directory]::CreateDirectory($tempPath) | Out-Null
$temporaryArchive = Join-Path $tempPath $archiveName
$temporaryNode = Join-Path $tempPath 'node.exe'
$temporaryLicense = Join-Path $tempPath 'LICENSE'
$zip = $null
$archiveStream = $null
try {
    if ($PSBoundParameters.ContainsKey('ArchivePath')) {
        $providedArchive = (Resolve-Path -LiteralPath $ArchivePath).ProviderPath
        if (-not (Test-Path -LiteralPath $providedArchive -PathType Leaf)) { throw 'ArchivePath must be a file.' }
        [IO.File]::Copy($providedArchive, $temporaryArchive, $false)
    } else {
        Write-Output "Downloading Node $nodeVersion from nodejs.org..."
        $previousTls = [Net.ServicePointManager]::SecurityProtocol
        try {
            # Windows PowerShell 5.1 can otherwise negotiate an older default.
            [Net.ServicePointManager]::SecurityProtocol = $previousTls -bor [Net.SecurityProtocolType]::Tls12
            Invoke-WebRequest -Uri $expectedUrl -OutFile $temporaryArchive -UseBasicParsing -MaximumRedirection 0 -TimeoutSec 180
        } finally {
            [Net.ServicePointManager]::SecurityProtocol = $previousTls
        }
    }
    if ((Get-FileHash -LiteralPath $temporaryArchive -Algorithm SHA256).Hash -ine $archiveHash) {
        throw 'Node archive SHA256 does not match provenance.json. Nothing was installed.'
    }

    Add-Type -AssemblyName System.IO.Compression
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    $archiveStream = [IO.File]::Open($temporaryArchive, [IO.FileMode]::Open, [IO.FileAccess]::Read, [IO.FileShare]::Read)
    $zip = [IO.Compression.ZipArchive]::new($archiveStream, [IO.Compression.ZipArchiveMode]::Read, $true)
    $entryPrefix = "node-$nodeVersion-win-x64/"
    Copy-VerifiedEntry $zip ($entryPrefix + 'node.exe') $temporaryNode $nodeRecord
    Copy-VerifiedEntry $zip ($entryPrefix + 'LICENSE') $temporaryLicense $licenseRecord
    $zip.Dispose()
    $zip = $null
    $archiveStream.Dispose()
    $archiveStream = $null

    # File.Move refuses to overwrite a binary installed by another process.
    # Both paths are in this checkout, so the normal move is an atomic rename.
    [IO.File]::Move($temporaryNode, $nodePath)
    Assert-ExpectedFile $nodePath $nodeRecord
    Write-Output "Restored and verified Node $nodeVersion`: $nodePath"
} finally {
    if ($null -ne $zip) { $zip.Dispose() }
    if ($null -ne $archiveStream) { $archiveStream.Dispose() }
    # Delete only these known temporary files; never recursively clean a path.
    foreach ($temporaryFile in @($temporaryArchive, $temporaryNode, $temporaryLicense)) {
        if ([IO.File]::Exists($temporaryFile)) { [IO.File]::Delete($temporaryFile) }
    }
    if ([IO.Directory]::Exists($tempPath)) { [IO.Directory]::Delete($tempPath, $false) }
}
