param(
    [string]$ArtifactDirectory = 'artifacts',
    [string]$PrivateStatePath,
    [string]$PrivateMediaDirectory,
    [string]$OutputPath = 'artifacts/delivery-audit.json'
)
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.IO.Compression.FileSystem
$workspacePath = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
Push-Location -LiteralPath $workspacePath
$privateBytes = $null
$storedBytes = $null
$encryptedBytes = $null
$privateValues = @()
$stateLock = $null
try {
    if ($PrivateStatePath) {
        Add-Type -AssemblyName System.Security
        $stateLock = [IO.File]::Open($PrivateStatePath + '.lock', [IO.FileMode]::OpenOrCreate, [IO.FileAccess]::ReadWrite, [IO.FileShare]::None)
        $storedBytes = [IO.File]::ReadAllBytes($PrivateStatePath)
        if ($storedBytes.Length -le 12 -or [Text.Encoding]::ASCII.GetString($storedBytes,0,8) -ne "WXDPAPI`0" -or [BitConverter]::ToUInt32($storedBytes,8) -ne 1) { throw 'Private state format invalid.' }
        $encryptedBytes = [byte[]]::new($storedBytes.Length - 12)
        [Array]::Copy($storedBytes,12,$encryptedBytes,0,$encryptedBytes.Length)
        $privateBytes = [Security.Cryptography.ProtectedData]::Unprotect($encryptedBytes,$null,[Security.Cryptography.DataProtectionScope]::CurrentUser)
        $privateState = [Text.Encoding]::UTF8.GetString($privateBytes) | ConvertFrom-Json
        $privateValues = @($privateState.Session.BotToken,$privateState.Session.BotId,$privateState.Session.UserId,$privateState.Session.ContextToken,$privateState.Session.Cursor) | Where-Object { $_ -and $_.Length -ge 8 }
    }
    $mediaHashes = @()
    if ($PrivateMediaDirectory -and (Test-Path -LiteralPath $PrivateMediaDirectory)) {
        $mediaHashes = @(Get-ChildItem -LiteralPath $PrivateMediaDirectory -File -Recurse | ForEach-Object { (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash })
    }
    $results = @()
    foreach ($fileName in @('WeixinCSharp-1.2.1-source.zip','WeixinCSharp-1.2.1-win-x64.zip')) {
        $zipPath = Join-Path $ArtifactDirectory $fileName
        $archive = [IO.Compression.ZipFile]::OpenRead($zipPath)
        $textCount = 0
        try {
            foreach ($entry in $archive.Entries) {
                if ($entry.FullName -match '(?i)(^|/)(artifacts|references|received[^/]*|bin|obj)/|\.dpapi|(^|/)login-qr\.(html|png)$|\.login-qr\.(html|png)$|\.tmp$|\.lock$') { throw ('Private or build-only entry found: ' + $entry.FullName) }
                if ($entry.Length -eq 0) { continue }
                if ($mediaHashes.Count -gt 0) {
                    $stream = $entry.Open()
                    $hashAlgorithm = [Security.Cryptography.SHA256]::Create()
                    try { $entryHash = [BitConverter]::ToString($hashAlgorithm.ComputeHash($stream)).Replace('-','') }
                    finally { $hashAlgorithm.Dispose(); $stream.Dispose() }
                    if ($mediaHashes -contains $entryHash) { throw 'Private media content found in delivery.' }
                }
                if ($entry.FullName -match '(?i)\.(cs|csproj|slnx|props|json|md|html|txt|ps1|py|mjs|cjs|ts|c|h|cpp)$') {
                    $reader = [IO.StreamReader]::new($entry.Open())
                    try { $content = $reader.ReadToEnd() } finally { $reader.Dispose() }
                    $unicodeDecoded = [regex]::Replace($content, '\\u([0-9a-fA-F]{4})', { param($match) [string][char][Convert]::ToInt32($match.Groups[1].Value,16) })
                    $jsonDecoded = $unicodeDecoded.Replace('\"','"').Replace('\/','/').Replace('\\','\')
                    foreach ($privateValue in $privateValues) {
                        if ($content.Contains($privateValue) -or $unicodeDecoded.Contains($privateValue) -or $jsonDecoded.Contains($privateValue)) { throw 'Private credential or account identifier found in delivery text.' }
                    }
                    $textCount++
                }
            }
            $results += [ordered]@{ file = $fileName; entryCount = $archive.Entries.Count; textEntriesChecked = $textCount; sha256 = (Get-FileHash -LiteralPath $zipPath -Algorithm SHA256).Hash.ToLowerInvariant(); privateEntryFound = $false; knownPrivateValueFound = $false; privateMediaFound = $false }
        } finally { $archive.Dispose() }
    }
    $result = [ordered]@{ formatVersion = 1; completed = [DateTimeOffset]::UtcNow; privateStateChecked = [bool]$PrivateStatePath; privateMediaChecked = $mediaHashes.Count; containsCredentialsOrAccountIds = $false; packages = $results }
    $result | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $OutputPath -Encoding utf8
    Write-Output 'Delivery archive audit passed; no private entry, known credential, account identifier or supplied private media was found.'
} finally {
    foreach ($buffer in @($privateBytes,$encryptedBytes,$storedBytes)) { if ($null -ne $buffer) { [Array]::Clear($buffer,0,$buffer.Length) } }
    $privateState = $null; $privateValues = @()
    if ($null -ne $stateLock) { $stateLock.Dispose() }
    Pop-Location
}
